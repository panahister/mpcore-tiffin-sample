using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tiffin.Payments.Domain;

namespace Tiffin.Payments.Infrastructure.Persistence;

/// <summary>
/// Erases the token of every payment that was opened and never used. A payment that was charged, declined
/// or voided has already erased its own; this covers the order that was never placed after all.
/// </summary>
/// <remarks>
/// A hosted service, not a handler: it runs outside Wolverine and may create its own scope. It works for
/// every city at once, which is why it asks the database directly and names no tenant. One UPDATE per
/// round, so several instances running it at once do no harm.
/// </remarks>
public sealed class PaymentTokenEraser(IServiceScopeFactory scopes, TimeProvider clock, ILogger<PaymentTokenEraser> logger) : BackgroundService
{
    /// <summary>How often expired tokens are erased: a token outlives its payment by at most this long.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                await EraseExpiredAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Expired payment tokens could not be erased; trying again in {Interval}", Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>Erases the tokens of the payments that expired unused. Returns how many.</summary>
    public async Task<int> EraseExpiredAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var now = clock.GetUtcNow();
        var erased = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Payment>()
            .Where(p => p.PaymentToken != null && p.ExpiresOnUtc <= now)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.PaymentToken, (string?)null), cancellationToken)
            .ConfigureAwait(false);
        if (erased > 0)
        {
            logger.LogInformation("Erased the tokens of {Count} expired payment(s)", erased);
        }

        return erased;
    }
}
