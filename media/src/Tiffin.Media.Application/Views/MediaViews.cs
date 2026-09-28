using Tiffin.Media.Domain;

namespace Tiffin.Media.Application.Views;

/// <summary>A place that was reserved, and the address the bytes are sent to. The address is a credential for ten minutes: it is never logged.</summary>
public sealed record UploadTicket(Guid MediaId, Uri UploadUrl, string Method, string ContentType, long Size, DateTimeOffset ExpiresOnUtc)
{
    public override string ToString() => $"{nameof(UploadTicket)} {{ MediaId = {MediaId}, ExpiresOnUtc = {ExpiresOnUtc:O} }}";
}

public sealed record MediaView(
    Guid MediaId, string City, string Purpose, string FileName, string ContentType, long Size, string State, DateTimeOffset ReservedOnUtc,
    DateTimeOffset? AvailableOnUtc, Uri? DownloadUrl, DateTimeOffset? DownloadUrlExpiresOnUtc)
{
    public override string ToString() => $"{nameof(MediaView)} {{ MediaId = {MediaId}, State = {State} }}";
}

public static class MediaViews
{
    public static MediaView Of(MediaFile file, Uri? downloadUrl = null, DateTimeOffset? expiresOn = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new MediaView(
            file.Id, file.City, file.Purpose, file.FileName, file.ContentType, file.Size, file.State.ToString(), file.ReservedOnUtc,
            file.AvailableOnUtc, downloadUrl, expiresOn);
    }
}
