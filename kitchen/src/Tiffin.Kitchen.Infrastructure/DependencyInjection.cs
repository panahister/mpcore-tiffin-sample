using Microsoft.Extensions.DependencyInjection;
using MPCore.Audit.EntityFrameworkCore;
using MPCore.Idempotency.EntityFrameworkCore;
using MPCore.Messaging.Wolverine;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
using Tiffin.Kitchen.Application.Ports;
using Tiffin.Kitchen.Infrastructure.Audit;
using Tiffin.Kitchen.Infrastructure.Persistence;

namespace Tiffin.Kitchen.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        // Registered through Wolverine's integration: a handler that declares IUnitOfWork runs inside this
        // context's transaction, and the events its aggregates raise are committed with it (transactional
        // outbox). The audit and idempotency interceptors run inside the same save.
        services.AddMPCoreWolverineDbContext<AppDbContext>((provider, options) =>
            PostgreSqlDbContextOptions.Apply(options, connectionString).UseMPCoreAudit(provider).UseMPCoreIdempotency(provider));
        services.AddMPCoreAudit<AppDbContext>(AuditPolicyConfiguration.Configure);

        // The inbox for messages that arrive twice (ADR-013).
        services.AddMPCoreIdempotency<AppDbContext>();

        // By type, never with a lambda: Wolverine builds a handler's dependencies inline.
        services.AddScoped<ITicketRepository, TicketRepository>();
        services.AddScoped<IKnownRestaurants, KnownRestaurants>();
        services.AddScoped<ITicketReadModel, TicketReadModel>();
        return services;
    }
}
