using Microsoft.Extensions.DependencyInjection;
using MPCore.Idempotency.EntityFrameworkCore;
using MPCore.Messaging.Wolverine;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
using Tiffin.Notifications.Application.Ports;
using Tiffin.Notifications.Infrastructure.Persistence;

namespace Tiffin.Notifications.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        // Registered through Wolverine's integration: a handler that declares IUnitOfWork runs inside this
        // context's transaction. The idempotency interceptor runs inside the same save.
        services.AddMPCoreWolverineDbContext<AppDbContext>((provider, options) =>
            PostgreSqlDbContextOptions.Apply(options, connectionString).UseMPCoreIdempotency(provider));

        // The inbox for events that are read twice (ADR-013).
        services.AddMPCoreIdempotency<AppDbContext>();

        // By type, never with a lambda: Wolverine builds a handler's dependencies inline.
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationReadModel, NotificationReadModel>();
        return services;
    }
}
