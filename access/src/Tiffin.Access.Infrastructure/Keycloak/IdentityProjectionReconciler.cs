using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tiffin.Access.Application.Integration;
using Tiffin.Access.Application.Ports;
using Tiffin.Access.Infrastructure.Persistence;

namespace Tiffin.Access.Infrastructure.Keycloak;

/// <summary>
/// Hydrates realm-imported identities at startup and repairs a missed lifecycle event periodically.
/// Keycloak remains authoritative; only the privacy-minimal business projection is persisted.
/// </summary>
public sealed class IdentityProjectionReconciler(
    IServiceScopeFactory scopes,
    KeycloakOptions options,
    ILogger<IdentityProjectionReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileOnceAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(options.ReconciliationInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is DirectoryUnavailableException or HttpRequestException or TimeoutException)
            {
                logger.LogWarning(exception, "Keycloak identity reconciliation will retry after a transient failure.");
                await Task.Delay(options.ReconciliationRetryDelay, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IIdentityDirectory>();
        var projection = scope.ServiceProvider.GetRequiredService<IIdentityProjection>();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var observedOnUtc = DateTimeOffset.UtcNow;
        var count = await IdentityProjectionReconciliation.ReconcileAsync(
            directory, projection, observedOnUtc, cancellationToken).ConfigureAwait(false);
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Reconciled {IdentityCount} Keycloak business identities at {ObservedOnUtc}.", count, observedOnUtc);
    }
}
