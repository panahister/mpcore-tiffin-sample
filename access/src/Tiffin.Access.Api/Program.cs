using Tiffin.Access.Api.Hosting;
using Microsoft.EntityFrameworkCore;
using Tiffin.Access.Application;
using Tiffin.Access.Application.Resources;
using Tiffin.Access.Domain.Events;
using Tiffin.Access.Infrastructure.Keycloak;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Kafka;
using ResultFailureException = MPCore.Application.Results.ResultFailureException;
using Tiffin.Access.Infrastructure;
using Tiffin.Access.Infrastructure.Persistence;
using MPCore.Hosting;
using MPCore.Localization;
using MPCore.Messaging.Wolverine;
using MPCore.Observability;
using MPCore.Observability.Prometheus;
using MPCore.Security.AspNetCore;
using MPCore.Validation.FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Tiffin.Access.Api.Rest.Endpoints;
using MPCore.Transport.Http;
using MPCore.Messaging.Wolverine.Kafka;

var builder = WebApplication.CreateBuilder(args);

// Per-endpoint Kestrel "Protocols" values in appsettings.json are authoritative; no protocol is
// forced globally.
const TransportMode HostTransport = TransportMode.Rest;
TransportEndpointGuard.Validate(builder.Configuration, HostTransport);

builder.Services.AddMPCoreFoundation(new MPCoreObservabilityOptions
{
    ServiceName = "Tiffin.Access",
    ServiceNamespace = "Tiffin",
    ServiceVersion = typeof(Program).Assembly.GetName().Version?.ToString(),
    EnableOtlpExporter = builder.Configuration.GetValue("Observability:EnableOtlpExporter", false),
    // Each signal has its own destination, sampling and redaction settings; see docs/architecture.md.
    Signals = builder.Configuration.GetSection("Observability").Get<MPCoreObservabilitySignals>()
});
// Prometheus pull is a REST-listener surface. It carries no anonymous metadata: the scraper must
// present a bearer token, or the deployment must confine the listener to the scraper's network.
var metricsScrapeEnabled = builder.Configuration.GetValue("Observability:Metrics:Prometheus:Enabled", false);
if (metricsScrapeEnabled)
{
    builder.Services.AddMPCorePrometheusScrape();
}
builder.Services.AddApplication();

// Failures reach the caller as message keys, rendered here in the caller's language: MP Core's own
// messages ship in English and Persian, and each module adds its resource file. See
// docs/architecture.md, "Business rules, validation and messages".
builder.Services.AddMPCoreMessageCatalog(catalog => catalog.AddResources<AccessMessages>());
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
builder.Services.AddPolicyContributor<AccessPolicies>();

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
builder.Services.AddMPCoreHttpFailureHandling(options =>
{
    options.SupportedCultures.Add("zh-Hans");
    options.SupportedCultures.Add("tr");
});
builder.Services.AddMPCoreProblemDetailsSecurityResponses();
if (enableOpenApi)
{
    builder.Services.AddOpenApi();
}

var databaseConnection = builder.Configuration.GetConnectionString("PostgreSql")
    ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
var authority = builder.Configuration.GetValue<Uri>("Security:Authority")
    ?? throw new InvalidOperationException("Security:Authority is required.");
builder.Services.AddInfrastructure(
    databaseConnection,
    new KeycloakOptions
    {
        Administration = builder.Configuration.GetValue<Uri>("IdentityProvider:Administration")
            ?? throw new InvalidOperationException("IdentityProvider:Administration is required.")
    },
    new ServiceIdentity
    {
        Authority = authority,
        ClientId = builder.Configuration["ServiceIdentity:ClientId"]
            ?? throw new InvalidOperationException("ServiceIdentity:ClientId is required."),
        ClientSecret = builder.Configuration["ServiceIdentity:ClientSecret"]
            ?? throw new InvalidOperationException("ServiceIdentity:ClientSecret is required. Set it in user secrets or the environment."),
        RequireHttps = builder.Configuration.GetValue("Security:RequireHttpsMetadata", true)
    });
// AppDbContext is named here as the transaction owner, so a handler can depend on IUnitOfWork and
// still run inside the Entity Framework transaction whose commit releases its outgoing messages.
builder.Host.UseMPCoreWolverine<AppDbContext>(
    new WolverineFoundationOptions
    {
        ServiceName = "Tiffin.Access",
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
        // What happened to somebody's roles leaves through Kafka, keyed by the person, so that "given" is
        // read before "taken". The step that tells the identity provider needs no route: it goes to the
        // handler that exists for it, on Wolverine's durable local queues.
        options.PublishMessage<RoleChanged>().ToKafkaTopic(AccessTopics.RoleChanged);
        options.MessagePartitioning.ByMessage<RoleChanged>(e => e.PersonId);
        options.Policies.PropagateGroupIdToPartitionKey();

        // Failures carry a retry directive, and the queue obeys it: never retry what cannot succeed, retry
        // with a cooldown what might, and park the rest in the error queue for an operator. Giving a step
        // up marks its decision failed (Hosting/GivenUpMessages.cs).
        options.OnException<ResultFailureException>(e => !e.Failure.Retry.IsRetryable)
            .MoveToErrorQueue()
            .And(GivenUpMessages.MarkTheDecisionFailedAsync, GivenUpMessages.Description);
        options.OnException<ResultFailureException>(e => e.Failure.Retry.IsRetryable)
            .RetryWithCooldown(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6))
            .Then.MoveToErrorQueue()
            .And(GivenUpMessages.MarkTheDecisionFailedAsync, GivenUpMessages.Description);

        options.UseMPCoreKafka(new KafkaTransportOptions
        {
            BootstrapServers = builder.Configuration["Messaging:Kafka:BootstrapServers"]
                ?? throw new InvalidOperationException("Messaging:Kafka:BootstrapServers is required."),
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
app.UseMPCoreProblemDetails();
app.UseMPCoreRequestContext();
app.UseForwardedIdentityHeaderGuard();
if (enableOpenApi && anonymousDescriptionSurface)
{
    // The UI is a static shell that a browser navigates to, so it cannot carry a bearer token, and
    // the authenticated fallback policy applies to middleware-served content as well as to mapped
    // endpoints. It is therefore mounted ahead of authentication and only in Development. Outside
    // Development it is not served at all; the document endpoint remains and stays protected.
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Tiffin.Access v1");
        options.RoutePrefix = "openapi-ui";
    });
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();


IEndpointConventionBuilder? openApiDocument = null;
if (enableOpenApi)
{
    // The document describes the whole REST surface. Anonymous only in Development; when enabled
    // elsewhere it exists but requires a token like every other endpoint.
    openApiDocument = app.MapOpenApi();
    if (anonymousDescriptionSurface)
    {
        openApiDocument.AllowAnonymous();
    }
}

var restProbeEndpoints = app.MapPlatformProbeEndpoints();
var accessEndpoints = app.MapAccessEndpoints();
var livenessEndpoint = app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = static check => check.Tags.Contains(HostHealthChecks.Live),
        ResponseWriter = WriteAggregateStatusAsync
    });
var readinessEndpoint = app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions { ResponseWriter = WriteAggregateStatusAsync });
var startupEndpoint = app.MapHealthChecks(
    "/health/startup",
    new HealthCheckOptions { ResponseWriter = WriteAggregateStatusAsync });
if (allowAnonymousHealthEndpoints)
{
    livenessEndpoint.AllowAnonymous();
    readinessEndpoint.AllowAnonymous();
    startupEndpoint.AllowAnonymous();
}

IEndpointConventionBuilder? metricsScrape = null;
if (metricsScrapeEnabled)
{
    metricsScrape = app.MapMPCorePrometheusScrape(app.Configuration.GetValue("Observability:Metrics:Prometheus:Path", "/metrics")!);
}


await app.RunAsync();

// Health responses expose the aggregate status word only. Check names, dependency hosts, durations
// and exception text are never disclosed anonymously.
static Task WriteAggregateStatusAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "text/plain; charset=utf-8";
    return context.Response.WriteAsync(report.Status.ToString());
}

public partial class Program;
