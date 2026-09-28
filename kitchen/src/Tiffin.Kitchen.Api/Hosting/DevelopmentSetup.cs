using Microsoft.EntityFrameworkCore;
using Tiffin.Kitchen.Infrastructure.Persistence;

namespace Tiffin.Kitchen.Api.Hosting;

/// <summary>Development conveniences. Nothing here runs outside the Development environment.</summary>
public static class DevelopmentSetup
{
    /// <summary>
    /// Applies pending EF Core migrations before the host starts serving, when
    /// <c>Database:MigrateOnStartup</c> is true. A production deployment applies migrations as a
    /// separate, reviewed step instead.
    /// </summary>
    public static async Task MigrateIfRequestedAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!app.Environment.IsDevelopment() || !app.Configuration.GetValue("Database:MigrateOnStartup", false))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;
        var pending = (await database.GetPendingMigrationsAsync().ConfigureAwait(false)).ToList();
        if (pending.Count > 0)
        {
            app.Logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
            await database.MigrateAsync().ConfigureAwait(false);
        }
    }
}
