using Microsoft.Extensions.DependencyInjection;
using MPCore.Audit.EntityFrameworkCore;
using MPCore.Caching.Memory;
using MPCore.Idempotency.EntityFrameworkCore;
using MPCore.Messaging.Wolverine;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
using MPCore.Resilience.Http;
using Tiffin.Access.Application.Ports;
using Tiffin.Access.Infrastructure.Audit;
using Tiffin.Access.Infrastructure.Keycloak;
using Tiffin.Access.Infrastructure.Persistence;

namespace Tiffin.Access.Infrastructure;

/// <summary>Who this service is when it calls the identity provider's administration.</summary>
public sealed class ServiceIdentity
{
    public required Uri Authority { get; init; }

    public required string ClientId { get; init; }

    /// <summary>From user secrets or the environment; never in a committed file.</summary>
    public required string ClientSecret { get; init; }

    /// <summary>False on a developer's machine only, where nothing speaks TLS.</summary>
    public bool RequireHttps { get; init; } = true;

    /// <summary>What a log may show of these settings: never the secret.</summary>
    public override string ToString() => $"{nameof(ServiceIdentity)} {{ ClientId = {ClientId}, Authority = {Authority} }}";
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string connectionString, KeycloakOptions keycloak, ServiceIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(keycloak);
        ArgumentNullException.ThrowIfNull(identity);

        services.AddMPCoreMemoryCache();
        // Registered through Wolverine's integration: a handler that declares IUnitOfWork runs inside this
        // context's transaction, and what it publishes is committed with it (transactional outbox). The
        // audit interceptor runs inside the same save.
        services.AddMPCoreWolverineDbContext<AppDbContext>((provider, options) =>
            PostgreSqlDbContextOptions.Apply(options, connectionString)
                .UseMPCoreAudit(provider)
                .UseMPCoreIdempotency(provider));
        services.AddMPCoreAudit<AppDbContext>(AuditPolicyConfiguration.Configure);
        services.AddMPCoreIdempotency<AppDbContext>();

        // By type, never with a lambda: Wolverine builds a handler's dependencies inline.
        services.AddSingleton(keycloak);
        services.AddScoped<IGrantRepository, GrantRepository>();
        services.AddScoped<IGrantReadModel, GrantReadModel>();
        services.AddScoped<IIdentityDirectory, KeycloakDirectory>();
        services.AddScoped<IIdentityProjection, IdentityProjection>();
        services.AddHostedService<IdentityProjectionReconciler>();

        // The only client of the platform that speaks to the identity provider's administration, as the
        // Access service itself.
        services.AddMPCoreResilientHttpClient(
                KeycloakDirectory.ClientName,
                client => client.BaseAddress = keycloak.Administration,
                resilience =>
                {
                    resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                    resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
                    resilience.Retry.MaxRetryAttempts = 2;
                    resilience.Retry.Delay = TimeSpan.FromMilliseconds(200);
                    resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
                })
            .AddMPCoreServiceIdentity(options =>
            {
                options.Authority = identity.Authority;
                options.ClientId = identity.ClientId;
                options.ClientSecret = identity.ClientSecret;
                options.RequireHttps = identity.RequireHttps;
            });
        return services;
    }
}
