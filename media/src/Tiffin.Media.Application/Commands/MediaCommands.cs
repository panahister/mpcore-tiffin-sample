using MPCore.Application.Messaging;
using MPCore.Application.Results;
using MPCore.Application.Time;
using MPCore.Audit;
using MPCore.Persistence.Abstractions;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Media.Application.Ports;
using Tiffin.Media.Application.Views;
using Tiffin.Media.Domain;

namespace Tiffin.Media.Application.Commands;

/// <summary>Somebody announces a file: what it is for, what it is called, of which type, and how large.</summary>
public sealed record ReserveUpload(string Purpose, string FileName, string ContentType, long Size) : ICommand<Result<UploadTicket>>;

/// <summary>The bytes were sent. The store is asked what arrived.</summary>
public sealed record ConfirmUpload(Guid MediaId) : ICommand<Result<MediaView>>;

public sealed record DeleteMedia(Guid MediaId) : ICommand<Result<MediaView>>;

/// <summary>
/// What is done to a file. Who does it and in which city comes from the validated token; where the bytes
/// lie in the store is decided here and never taken from a request, so that nobody can name another
/// city's key.
/// </summary>
public static class MediaCommandsHandler
{
    public static async Task<Result<UploadTicket>> Handle(
        ReserveUpload command, ICurrentActorAccessor actor, ITenantContext tenant, IMediaRepository files, IObjectStore store,
        IUnitOfWork unitOfWork, IClock clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(store);

        if (actor.Current.SubjectId is not { } owner || tenant.TenantId is not { } city)
        {
            return Result<UploadTicket>.FromFailure(MediaFailures.SignInRequired());
        }

        // A purpose that is not known, a type or a size it does not allow: rules M1, M2 and M3.
        var file = MediaFile.Reserve(city, owner, command.Purpose, command.FileName, command.ContentType, command.Size, clock.UtcNow);
        try
        {
            var address = await store.SignUploadAsync(file.StorageKey, file.ContentType, MediaFile.UploadWindow, cancellationToken).ConfigureAwait(false);
            files.Add(file);
            return Result<UploadTicket>.Success(new UploadTicket(file.Id, address, "PUT", file.ContentType, file.Size, file.ExpiresOnUtc));
        }
        catch (StoreUnavailableException)
        {
            return Result<UploadTicket>.FromFailure(MediaFailures.StoreUnavailable());
        }
    }

    public static async Task<Result<MediaView>> Handle(
        ConfirmUpload command, ICurrentActorAccessor actor, ITenantContext tenant, IMediaRepository files, IObjectStore store,
        IBusinessAuditRecorder audit, IUnitOfWork unitOfWork, IClock clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(audit);

        var found = await OwnAsync(command.MediaId, actor, tenant, files, cancellationToken).ConfigureAwait(false);
        if (found.Failure is not null)
        {
            return Result<MediaView>.FromFailure(found.Failure);
        }

        var file = found.File!;
        var now = clock.UtcNow;
        if (file.HasExpired(now))
        {
            return Result<MediaView>.FromFailure(MediaFailures.UploadWindowClosed());
        }

        StoredObject? arrived;
        try
        {
            arrived = await store.LookAsync(file.StorageKey, cancellationToken).ConfigureAwait(false);
        }
        catch (StoreUnavailableException)
        {
            return Result<MediaView>.FromFailure(MediaFailures.StoreUnavailable());
        }

        if (arrived is null)
        {
            return Result<MediaView>.FromFailure(MediaFailures.NothingArrived());
        }

        // What arrived and is not what was announced breaks rule M5. It stays in the store until the
        // place expires, and is removed with it.
        file.Confirm(arrived.Size, arrived.ContentType, now);
        await audit.RecordAsync(
            "media", "file-available", nameof(MediaFile), file.Id.ToString(),
            new Dictionary<string, string>
            {
                ["purpose"] = file.Purpose,
                ["type"] = file.ContentType,
                ["size"] = file.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }, cancellationToken).ConfigureAwait(false);
        return Result<MediaView>.Success(MediaViews.Of(file));
    }

    public static async Task<Result<MediaView>> Handle(
        DeleteMedia command, ICurrentActorAccessor actor, ITenantContext tenant, IMediaRepository files, IObjectStore store,
        IBusinessAuditRecorder audit, IUnitOfWork unitOfWork, IClock clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(audit);

        var found = await OwnAsync(command.MediaId, actor, tenant, files, cancellationToken).ConfigureAwait(false);
        if (found.Failure is not null)
        {
            return Result<MediaView>.FromFailure(found.Failure);
        }

        var file = found.File!;
        try
        {
            // The bytes first. When the store cannot be reached nothing is recorded as deleted, and the
            // owner tries again; the other order would leave bytes that nothing points to.
            await store.RemoveAsync(file.StorageKey, cancellationToken).ConfigureAwait(false);
        }
        catch (StoreUnavailableException)
        {
            return Result<MediaView>.FromFailure(MediaFailures.StoreUnavailable());
        }

        file.Delete(clock.UtcNow);
        await audit.RecordAsync(
            "media", "file-deleted", nameof(MediaFile), file.Id.ToString(),
            new Dictionary<string, string> { ["purpose"] = file.Purpose }, cancellationToken).ConfigureAwait(false);
        return Result<MediaView>.Success(MediaViews.Of(file));
    }

    private static async Task<(MediaFile? File, FailureDescriptor? Failure)> OwnAsync(
        Guid mediaId, ICurrentActorAccessor actor, ITenantContext tenant, IMediaRepository files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(files);

        if (actor.Current.SubjectId is not { } caller || tenant.TenantId is not { } city)
        {
            return (null, MediaFailures.SignInRequired());
        }

        var file = await files.GetAsync(mediaId, city, cancellationToken).ConfigureAwait(false);
        if (file is null || file.State == MediaState.Deleted)
        {
            return (null, MediaFailures.FileNotFound());
        }

        return file.IsOwnedBy(caller) ? (file, null) : (null, MediaFailures.NotTheOwner());
    }
}
