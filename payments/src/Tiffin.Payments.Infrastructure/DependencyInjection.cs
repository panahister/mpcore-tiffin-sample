using Microsoft.Extensions.DependencyInjection;
using MPCore.Audit.EntityFrameworkCore;
using MPCore.Idempotency.EntityFrameworkCore;
using MPCore.Messaging.Wolverine;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
using MPCore.Resilience.Http;
using Tiffin.Payments.Application.Ports;
using Tiffin.Payments.Infrastructure.Audit;
using Tiffin.Payments.Infrastructure.Persistence;
using Tiffin.Payments.Infrastructure.Provider;

namespace Tiffin.Payments.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, PayLaneOptions provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        // Registered through Wolverine's integration: a handler that declares IUnitOfWork runs inside this
        // context's transaction, and the events its aggregates raise are committed with it (transactional
        // outbox). The audit and idempotency interceptors run inside the same save.
        services.AddMPCoreWolverineDbContext<AppDbContext>((serviceProvider, options) =>
            PostgreSqlDbContextOptions.Apply(options, connectionString).UseMPCoreAudit(serviceProvider).UseMPCoreIdempotency(serviceProvider));
        services.AddMPCoreAudit<AppDbContext>(AuditPolicyConfiguration.Configure);

        // The inbox for requests that arrive twice (ADR-013).
        services.AddMPCoreIdempotency<AppDbContext>();

        // By type, never with a lambda: Wolverine builds a handler's dependencies inline.
        services.AddSingleton(provider);
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentGateway, PayLaneGateway>();
        services.AddHostedService<PaymentTokenEraser>();

        // Nobody waits at a screen for this call: it is made from a queue. The client still gives up
        // within seconds, and what it could not do the broker tries again later, with a pause.
        services.AddMPCoreResilientHttpClient(
            PayLaneGateway.ClientName,
            client => client.BaseAddress = provider.BaseAddress,
            resilience =>
            {
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
                resilience.Retry.MaxRetryAttempts = 2;
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(300);
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(20);
            });
        return services;
    }
}
