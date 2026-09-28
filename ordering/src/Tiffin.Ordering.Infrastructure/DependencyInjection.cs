using Microsoft.Extensions.DependencyInjection;
using MPCore.Audit.EntityFrameworkCore;
using MPCore.Idempotency.EntityFrameworkCore;
using MPCore.Messaging.Wolverine;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
using MPCore.Resilience.Http;
using Tiffin.Ordering.Application.Ports;
using Tiffin.Ordering.Infrastructure.Audit;
using Tiffin.Ordering.Infrastructure.Persistence;
using Tiffin.Ordering.Infrastructure.Services;

namespace Tiffin.Ordering.Infrastructure;

/// <summary>The other services this one calls, and who it is when it calls them.</summary>
public sealed class OtherServices
{
    /// <summary>The Restaurants service, over REST.</summary>
    public required Uri Restaurants { get; init; }

    /// <summary>The Payments service, over gRPC.</summary>
    public required Uri Payments { get; init; }

    /// <summary>The identity provider that issues this service's token.</summary>
    public required Uri Authority { get; init; }

    /// <summary>This service's client at the identity provider.</summary>
    public required string ClientId { get; init; }

    /// <summary>From user secrets or the environment; never in a committed file.</summary>
    public required string ClientSecret { get; init; }

    /// <summary>False on a developer's machine only, where nothing speaks TLS.</summary>
    public bool RequireHttps { get; init; } = true;

    /// <summary>What a log may show of these settings: never the secret.</summary>
    public override string ToString() => $"{nameof(OtherServices)} {{ Restaurants = {Restaurants}, Payments = {Payments}, ClientId = {ClientId} }}";
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, OtherServices others)
    {
        ArgumentNullException.ThrowIfNull(others);

        // Registered through Wolverine's integration: a handler that declares IUnitOfWork runs inside this
        // context's transaction, and what it publishes is committed with it (transactional outbox). The
        // audit and idempotency interceptors run inside the same save.
        services.AddMPCoreWolverineDbContext<AppDbContext>((provider, options) =>
            PostgreSqlDbContextOptions.Apply(options, connectionString).UseMPCoreAudit(provider).UseMPCoreIdempotency(provider));
        services.AddMPCoreAudit<AppDbContext>(AuditPolicyConfiguration.Configure);

        // Idempotency-Key on the request that must not run twice, and the inbox for answers that arrive
        // twice (ADR-013).
        services.AddMPCoreIdempotency<AppDbContext>();

        // By type, never with a lambda: Wolverine builds a handler's dependencies inline.
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderReadModel, OrderReadModel>();
        services.AddScoped<IRestaurantQuotes, RestaurantQuotesClient>();
        services.AddScoped<IPaymentIntents, PaymentIntentsClient>();

        // A customer waits for both calls. The budgets are short, and a read may be tried again; a call
        // that hands over a card's token is tried once.
        services.AddMPCoreResilientHttpClient(
                RestaurantQuotesClient.ClientName,
                client => client.BaseAddress = others.Restaurants,
                resilience =>
                {
                    resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                    resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(6);
                    resilience.Retry.MaxRetryAttempts = 2;
                    resilience.Retry.Delay = TimeSpan.FromMilliseconds(200);
                    resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
                })
            .AddMPCoreServiceIdentity(identity => Identify(identity, others))
            // The service's token names no city: the city of the order travels in x-tenant-id (T-05).
            .AddMPCoreTenantPropagation();

        // The card's token is handed over once: no retry. A call that did not come through is the
        // customer's to repeat, with the same Idempotency-Key.
        services.AddMPCoreResilientHttpClient(
                PaymentIntentsClient.ClientName,
                client =>
                {
                    client.BaseAddress = others.Payments;
                    // gRPC is HTTP/2, and between two services on one network it is spoken in cleartext.
                    client.DefaultRequestVersion = System.Net.HttpVersion.Version20;
                    client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
                },
                resilience =>
                {
                    resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(4);
                    resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
                    resilience.Retry.ShouldHandle = static _ => ValueTask.FromResult(false);
                    resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
                })
            .AddMPCoreServiceIdentity(identity => Identify(identity, others))
            .AddMPCoreTenantPropagation();
        return services;
    }

    private static void Identify(ServiceIdentityOptions identity, OtherServices others)
    {
        identity.Authority = others.Authority;
        identity.ClientId = others.ClientId;
        identity.ClientSecret = others.ClientSecret;
        identity.RequireHttps = others.RequireHttps;
    }
}
