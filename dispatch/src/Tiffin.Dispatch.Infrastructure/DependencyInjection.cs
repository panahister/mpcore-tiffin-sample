using Microsoft.Extensions.DependencyInjection;
using MPCore.Idempotency.EntityFrameworkCore;
using MPCore.Messaging.Wolverine;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
using Tiffin.Dispatch.Application.Ports;
using Tiffin.Dispatch.Application.Queries;
using Tiffin.Dispatch.Infrastructure.Persistence;

namespace Tiffin.Dispatch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        // Registered through Wolverine's integration: a handler that declares IUnitOfWork runs inside this
        // context's transaction, and the events its aggregates raise are committed with it (transactional
        // outbox). The idempotency interceptor runs inside the same save.
        services.AddMPCoreWolverineDbContext<AppDbContext>((provider, options) =>
            PostgreSqlDbContextOptions.Apply(options, connectionString).UseMPCoreIdempotency(provider));

        // The inbox for requests that arrive twice (ADR-013).
        services.AddMPCoreIdempotency<AppDbContext>();

        // By type, never with a lambda: Wolverine builds a handler's dependencies inline.
        services.AddScoped<ICourierRepository, CourierRepository>();
        services.AddScoped<IDeliveryRepository, DeliveryRepository>();
        services.AddScoped<IDispatchReadModel, DispatchReadModel>();
        return services;
    }
}
