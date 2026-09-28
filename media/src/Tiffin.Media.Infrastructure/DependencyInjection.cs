using Microsoft.Extensions.DependencyInjection;
using MPCore.Audit.EntityFrameworkCore;
using MPCore.Messaging.Wolverine;
using MPCore.Persistence.EntityFrameworkCore.PostgreSql;
using Tiffin.Media.Application.Ports;
using Tiffin.Media.Infrastructure.Audit;
using Tiffin.Media.Infrastructure.Persistence;
using Tiffin.Media.Infrastructure.Store;

namespace Tiffin.Media.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, S3Options store)
    {
        ArgumentNullException.ThrowIfNull(store);

        // Registered through Wolverine's integration: a handler that declares IUnitOfWork runs inside this
        // context's transaction, and the events its aggregates raise are committed with it (transactional
        // outbox). The audit interceptor runs inside the same save.
        services.AddMPCoreWolverineDbContext<AppDbContext>((provider, options) =>
            PostgreSqlDbContextOptions.Apply(options, connectionString).UseMPCoreAudit(provider));
        services.AddMPCoreAudit<AppDbContext>(AuditPolicyConfiguration.Configure);

        // By type, never with a lambda: Wolverine builds a handler's dependencies inline.
        services.AddSingleton(store);
        services.AddSingleton<IObjectStore, S3ObjectStore>();
        services.AddScoped<IMediaRepository, MediaRepository>();
        services.AddScoped<IMediaReadModel, MediaReadModel>();
        services.AddHostedService<UploadSweeper>();
        return services;
    }
}
