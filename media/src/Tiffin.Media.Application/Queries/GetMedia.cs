using MPCore.Application.Messaging;
using MPCore.Application.Results;
using MPCore.Application.Time;
using MPCore.Tenancy;
using Tiffin.Media.Application.Ports;
using Tiffin.Media.Application.Views;
using Tiffin.Media.Domain;

namespace Tiffin.Media.Application.Queries;

/// <summary>A file of the caller's city: what it is, and for a file that is available, an address its bytes can be fetched from.</summary>
public sealed record GetMedia(Guid MediaId) : IQuery<Result<MediaView>>;

/// <summary>
/// The address is signed for five minutes. It is a credential for that long, handed to somebody who was
/// just asked who they are, and it is worth nothing afterwards: a link that was copied into a chat stops
/// working before it does harm.
/// </summary>
public static class GetMediaHandler
{
    public static readonly TimeSpan DownloadWindow = TimeSpan.FromMinutes(5);

    public static async Task<Result<MediaView>> Handle(
        GetMedia query, ITenantContext tenant, IMediaReadModel files, IObjectStore store, IClock clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(store);

        if (tenant.TenantId is not { } city)
        {
            return Result<MediaView>.FromFailure(MediaFailures.SignInRequired());
        }

        var file = await files.FindAsync(query.MediaId, city, cancellationToken).ConfigureAwait(false);
        if (file is null || file.State == MediaState.Deleted)
        {
            return Result<MediaView>.FromFailure(MediaFailures.FileNotFound());
        }

        if (file.State != MediaState.Available)
        {
            return Result<MediaView>.Success(MediaViews.Of(file));
        }

        try
        {
            var address = await store.SignDownloadAsync(file.StorageKey, DownloadWindow, cancellationToken).ConfigureAwait(false);
            return Result<MediaView>.Success(MediaViews.Of(file, address, clock.UtcNow + DownloadWindow));
        }
        catch (StoreUnavailableException)
        {
            return Result<MediaView>.FromFailure(MediaFailures.StoreUnavailable());
        }
    }
}
