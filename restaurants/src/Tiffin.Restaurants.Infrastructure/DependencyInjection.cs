using Microsoft.Extensions.DependencyInjection;
using MPCore.Audit.EntityFrameworkCore;
using Tiffin.Restaurants.Infrastructure.Audit;
using MPCore.Caching.Redis;
using MPCore.Messaging.Wolverine;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
using Tiffin.Restaurants.Application.Ports;
using Tiffin.Restaurants.Infrastructure.Persistence;

namespace Tiffin.Restaurants.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string cacheConnectionString)
    {
        services.AddMPCoreRedisCache(cacheConnectionString);
        // Registered through Wolverine's integration: a handler that takes AppDbContext runs inside its
        // transaction and the messages it publishes are committed with it (transactional outbox).
        // The audit interceptor runs inside AppDbContext, so every SaveChanges writes the entity's
        // audit rows in the same transaction as the change itself.
        services.AddMPCoreWolverineDbContext<AppDbContext>((provider, options) =>
            PostgreSqlDbContextOptions.Apply(options, connectionString).UseMPCoreAudit(provider));
        services.AddMPCoreAudit<AppDbContext>(AuditPolicyConfiguration.Configure);

        // By type, never with a lambda: Wolverine builds a handler's dependencies inline.
        services.AddScoped<IRestaurantRepository, RestaurantRepository>();
        services.AddScoped<IRestaurantReadModel, RestaurantReadModel>();
        return services;
    }
}
