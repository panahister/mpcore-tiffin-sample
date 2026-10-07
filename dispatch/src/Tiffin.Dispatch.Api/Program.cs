using Tiffin.Dispatch.Api.Hosting;
using Microsoft.EntityFrameworkCore;
using MPCore.Messaging.Wolverine.Kafka;
using Npgsql;
using Tiffin.Dispatch.Application;
using Tiffin.Dispatch.Application.Contracts;
using Tiffin.Dispatch.Application.Resources;
using Tiffin.Dispatch.Domain.Events;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Kafka;
using Wolverine.RabbitMQ;
using ResultFailureException = MPCore.Application.Results.ResultFailureException;
using Tiffin.Dispatch.Infrastructure;
using Tiffin.Dispatch.Infrastructure.Persistence;
using MPCore.Hosting;
using MPCore.Localization;
using MPCore.Messaging.Wolverine;
using MPCore.Observability;
using MPCore.Security.AspNetCore;
using MPCore.Validation.FluentValidation;
using Tiffin.Dispatch.Api.Grpc.Services;
using MPCore.Transport.Grpc;
using MPCore.Messaging.Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Per-endpoint Kestrel "Protocols" values in appsettings.json are authoritative; no protocol is
// forced globally.
// A single cleartext Http1AndHttp2 endpoint cannot serve prior-knowledge h2c HTTP/2, so the
// endpoint that carries binary RPC declares Http2 exclusively.
const TransportMode HostTransport = TransportMode.Grpc;
TransportEndpointGuard.Validate(builder.Configuration, HostTransport);

builder.Services.AddMPCoreFoundation(new MPCoreObservabilityOptions
{
    ServiceName = "Tiffin.Dispatch",
    ServiceNamespace = "Tiffin",
    ServiceVersion = typeof(Program).Assembly.GetName().Version?.ToString(),
    EnableOtlpExporter = builder.Configuration.GetValue("Observability:EnableOtlpExporter", false),
    // Each signal has its own destination, sampling and redaction settings; see docs/architecture.md.
    Signals = builder.Configuration.GetSection("Observability").Get<MPCoreObservabilitySignals>()
});
builder.Services.AddApplication();

// Failures reach the caller as message keys, rendered here in the caller's language: MP Core's own
// messages ship in English and Persian, and each module adds its resource file. See
// docs/architecture.md, "Business rules, validation and messages".
builder.Services.AddMPCoreMessageCatalog(catalog => catalog.AddResources<DispatchMessages>());
// Input validators (FluentValidation) of every handler assembly. They run before the handler.
foreach (var assembly in HandlerAssemblies.All)
{
    builder.Services.AddMPCoreValidators(assembly);
}

// Bearer-only OIDC resource server. Login, signup, OTP, password and identity-provider
// administration are Product surfaces and are never implemented here.
builder.Services.AddMPCoreBearerAuthentication(options =>
{
    options.Authority = builder.Configuration["Security:Authority"]
        ?? throw new InvalidOperationException("Security:Authority is required.");
    options.RequireHttpsMetadata = builder.Configuration.GetValue("Security:RequireHttpsMetadata", true);
    foreach (var audience in builder.Configuration.GetSection("Security:Audiences").Get<string[]>() ?? [])
    {
        options.ValidAudiences.Add(audience);
    }
});
// Claim mapping follows the identity provider. Keycloak-shaped by default (realm and client roles,
// preferred_username, azp, service-account-* convention); GenericOidc reads a flat roles claim.
builder.Services.AddMPCoreCurrentActor(mapping =>
{
    if (string.Equals(builder.Configuration["Security:ClaimMapping:Preset"], "GenericOidc", StringComparison.OrdinalIgnoreCase))
    {
        mapping.UseGenericOidc();
    }
    else
    {
        mapping.UseKeycloakDefaults();
    }
});
// The tenant, when the token names one. Business code reads ITenantContext; audit records it.
builder.Services.AddMPCoreTenancyFromClaim(builder.Configuration["Security:TenantClaim"] ?? "tenant_id");
builder.Services.AddForwardedIdentityHeaderGuard();
// X-Forwarded-* is honoured only from the proxies listed here (APISIX or another gateway). Empty
// means the host reasons from the real connection and ignores the headers entirely.
builder.Services.AddMPCoreGatewayForwarding(options =>
{
    foreach (var proxy in builder.Configuration.GetSection("Gateway:TrustedProxies").Get<string[]>() ?? [])
    {
        options.TrustedProxies.Add(proxy);
    }
});

// MP Core does not map the health probes, so it cannot enforce anonymous access to them. This host
// maps them and therefore owns the decision, applied with AllowAnonymous() where they are mapped.
var allowAnonymousHealthEndpoints =
    builder.Configuration.GetValue("Security:AllowAnonymousHealthEndpoints", true);
builder.Services.AddMPCoreAuthorization();
// Tiffin's own role policies, contributed to MP Core's default-deny authorization.
builder.Services.AddPolicyContributor<DispatchPolicies>();

// Description surfaces are opt-in and default to Development only: an OpenAPI document and gRPC
// reflection describe the entire API to whoever can reach them.
var enableOpenApi = DeveloperEndpoints.IsDescriptionSurfaceEnabled(
    builder.Configuration, builder.Environment, "Transport:EnableOpenApi");
var enableGrpcReflection = DeveloperEndpoints.IsDescriptionSurfaceEnabled(
    builder.Configuration, builder.Environment, "Transport:EnableGrpcReflection");

// A description surface is only reachable without a token in Development. Enabling one explicitly in
// another environment keeps it behind the authenticated fallback: the flag says "expose it", not
// "expose it to anyone".
var anonymousDescriptionSurface = builder.Environment.IsDevelopment();

// What "alive" and "ready" mean is the same on every transport: Hosting/HostHealthChecks.cs.
builder.Services.AddHostHealthChecks();

// A failure is told in the caller's language: English, Turkish or Simplified Chinese. "zh-Hans" answers a
// caller who asks for "zh-CN": MP Core accepts the requested culture or its parent.
builder.Services.AddGrpc().AddMPCoreFailureHandling(options =>
{
    options.SupportedCultures.Add("zh-Hans");
    options.SupportedCultures.Add("tr");
    options.SupportedCultures.Add("ar");
});
// The empty service name is the whole host; "live" asks the process only.
builder.Services.AddGrpcHealthChecks(options =>
    options.Services.Map(HostHealthChecks.Live, static check => check.Tags.Contains(HostHealthChecks.Live)));
if (enableGrpcReflection)
{
    builder.Services.AddGrpcReflection();
}

var databaseConnection = builder.Configuration.GetConnectionString("PostgreSql")
    ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
builder.Services.AddInfrastructure(databaseConnection);
// AppDbContext is named here as the transaction owner, so a handler can depend on IUnitOfWork and
// still run inside the Entity Framework transaction whose commit releases its outgoing messages.
builder.Host.UseMPCoreWolverine<AppDbContext>(
    new WolverineFoundationOptions
    {
        ServiceName = "Tiffin.Dispatch",
        PersistenceConnectionString = databaseConnection,
        PersistenceSchemaName = "wolverine",
        // This project's own handlers are discovered from here, in addition to HandlerAssemblies.All.
        ApplicationAssembly = typeof(Program).Assembly
    },
    options =>
    {
        // Handlers are discovered only in the assemblies this host names. Nothing is scanned
        // implicitly and no catch-all policy exists; see Hosting/HandlerAssemblies.cs.
        options.DiscoverHandlersIn(HandlerAssemblies.All);
        // A message whose validators fail never reaches its handler; the caller receives MP Core's
        // validation failure with one violation per field.
        options.UseMPCoreFluentValidation();
        // RabbitMQ delivers at least once. A request this host has already carried out stops at the inbox,
        // recorded by its EventId in the handler's own transaction (ADR-013).
        options.UseMPCoreInbox();

        // What the order asks, on a queue of its own, read as this service's own declaration of the
        // contract (Application/Contracts/CourierRequested.cs).
        options.ListenToRabbitQueue(DispatchQueues.CourierRequested).DefaultIncomingMessage<CourierRequested>().UseDurableInbox();

        // What this service answers, and what it says happened. Declared routes only: MP Core drops an
        // event with no route rather than guessing one (ADR-011 section 5).
        options.PublishMessage<CourierAssigned>().ToRabbitQueue(DispatchQueues.CourierAssigned).UseDurableOutbox();
        options.PublishMessage<CourierUnavailable>().ToRabbitQueue(DispatchQueues.CourierUnavailable).UseDurableOutbox();
        options.PublishMessage<DeliveryAssigned>().ToKafkaTopic(DispatchTopics.DeliveryAssigned);
        options.PublishMessage<DeliveryCompleted>().ToKafkaTopic(DispatchTopics.DeliveryCompleted);

        // Kafka orders messages within a partition only. The key is the order: "assigned" is read before
        // "completed", whichever instance wrote them.
        options.MessagePartitioning.ByMessage<IDeliveryEvent>(e => e.OrderId.ToString());
        options.Policies.PropagateGroupIdToPartitionKey();

        // Failures carry a retry directive, and the broker obeys it. Giving a request up is an answer
        // too: the order is told (Hosting/GivenUpMessages.cs).
        options.OnException<ResultFailureException>(e => !e.Failure.Retry.IsRetryable)
            .MoveToErrorQueue()
            .And(GivenUpMessages.TellTheOrderAsync, GivenUpMessages.Description);
        options.OnException<ResultFailureException>(e => e.Failure.Retry.IsRetryable)
            .RetryWithCooldown(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10))
            .Then.MoveToErrorQueue()
            .And(GivenUpMessages.TellTheOrderAsync, GivenUpMessages.Description);
        // Two orders reaching for one courier: the loser's save fails on the courier's row version, and
        // its message is tried again against a fresh roster. The pauses grow, and each is lengthened at
        // random, so that the losers of one collision do not come back together (Marc Brooker,
        // "Exponential Backoff And Jitter", AWS Architecture Blog, 2015).
        options.OnException<DbUpdateConcurrencyException>()
            .RetryWithCooldown(
                TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(800),
                TimeSpan.FromMilliseconds(1600), TimeSpan.FromMilliseconds(3200))
            .WithFullJitter()
            .Then.MoveToErrorQueue()
            .And(GivenUpMessages.TellTheOrderAsync, GivenUpMessages.Description);
        // A unique violation means somebody else just did this. Look again, once.
        options.OnException<DbUpdateException>(static e => e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            .RetryOnce()
            .Then.MoveToErrorQueue()
            .And(GivenUpMessages.TellTheOrderAsync, GivenUpMessages.Description);

        options.UseMPCoreKafka(new KafkaTransportOptions
        {
            BootstrapServers = builder.Configuration["Messaging:Kafka:BootstrapServers"]
                ?? throw new InvalidOperationException("Messaging:Kafka:BootstrapServers is required."),
            AutoProvision = builder.Configuration.GetValue("Messaging:AutoProvision", false)
        });

        options.UseMPCoreRabbitMq(new RabbitMqTransportOptions
        {
            ConnectionString = builder.Configuration["Messaging:RabbitMq:ConnectionString"]
                ?? throw new InvalidOperationException("Messaging:RabbitMq:ConnectionString is required."),
            AutoProvision = builder.Configuration.GetValue("Messaging:AutoProvision", false)
        });
    });

var app = builder.Build();

// Development only, and only when configured: bring the schema up to date. See Hosting/DevelopmentSetup.cs.
await DevelopmentSetup.MigrateIfRequestedAsync(app);

// ADR-007 pipeline order. UseAuthentication always precedes UseAuthorization, and both follow
// UseRouting so the authenticated fallback policy sees resolved endpoint metadata.
// Forwarded headers come first, so everything after it sees the scheme and client the gateway saw.
app.UseMPCoreGatewayForwarding();
app.UseForwardedIdentityHeaderGuard();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

var grpcProbeEndpoint = app.MapGrpcService<PlatformProbeService>();
var grpcCouriersEndpoint = app.MapGrpcService<CouriersService>();
var grpcHealthEndpoint = app.MapGrpcHealthChecksService();
if (allowAnonymousHealthEndpoints)
{
    grpcHealthEndpoint.AllowAnonymous();
}
IEndpointConventionBuilder? grpcReflection = null;
if (enableGrpcReflection)
{
    // Reflection lets grpcui and grpcurl discover services without a local .proto copy. Outside
    // Development it stays behind the authenticated fallback, so enabling it on a shared host does not
    // hand the service inventory to an anonymous caller.
    grpcReflection = app.MapGrpcReflectionService();
    if (anonymousDescriptionSurface)
    {
        grpcReflection.AllowAnonymous();
    }
}



await app.RunAsync();


public partial class Program;
