using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tiffin.Media.Application.Ports;
using Tiffin.Media.Domain;

namespace Tiffin.Media.Infrastructure.Persistence;

/// <summary>
/// Removes what was sent to a place that was never confirmed: a file that was abandoned, or one that was
/// not what was announced. The place is marked deleted, and the bytes leave the store.
/// </summary>
/// <remarks>
/// A hosted service, not a handler: it runs outside Wolverine and may create its own scope. It works for
/// every city at once, which is why it asks the database directly and names no tenant. Several instances
/// running it at once remove the same bytes twice, which changes nothing the second time.
/// </remarks>
public sealed class UploadSweeper(IServiceScopeFactory scopes, TimeProvider clock, ILogger<UploadSweeper> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Abandoned uploads could not be swept; trying again in {Interval}", Interval);
            }
        }
    }

    /// <summary>Sweeps the places that expired unconfirmed. Returns how many.</summary>
    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        var now = clock.GetUtcNow();

        var abandoned = await database.Set<MediaFile>().AsNoTracking()
            .Where(f => f.State == MediaState.Pending && f.ExpiresOnUtc <= now)
            .OrderBy(f => f.ExpiresOnUtc).Take(200)
            .Select(f => new { f.Id, f.StorageKey })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var place in abandoned)
        {
            await store.RemoveAsync(place.StorageKey, cancellationToken).ConfigureAwait(false);
            await database.Set<MediaFile>().Where(f => f.Id == place.Id && f.State == MediaState.Pending)
                .ExecuteUpdateAsync(set => set.SetProperty(f => f.State, MediaState.Deleted), cancellationToken).ConfigureAwait(false);
        }

        if (abandoned.Count > 0)
        {
            logger.LogInformation("Swept {Count} abandoned upload(s)", abandoned.Count);
        }

        return abandoned.Count;
    }
}
