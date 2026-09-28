using MPCore.Domain.Events;

namespace Tiffin.Media.Domain.Events;

/// <summary>A file can be shown. Written to the event stream for whoever keeps its identifier.</summary>
public sealed record FileAvailable : IntegrationEvent
{
    public const string Name = "tiffin.media.file-available";

    public FileAvailable(Guid mediaId, string city, string ownerId, string purpose, string contentType, long size, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        MediaId = mediaId;
        City = city;
        OwnerId = ownerId;
        Purpose = purpose;
        ContentType = contentType;
        Size = size;
    }

    public Guid MediaId { get; init; }

    public string City { get; init; }

    public string OwnerId { get; init; }

    public string Purpose { get; init; }

    public string ContentType { get; init; }

    public long Size { get; init; }
}

/// <summary>A file is gone. Whoever keeps its identifier forgets it.</summary>
public sealed record FileDeleted : IntegrationEvent
{
    public const string Name = "tiffin.media.file-deleted";

    public FileDeleted(Guid mediaId, string city, string purpose, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        MediaId = mediaId;
        City = city;
        Purpose = purpose;
    }

    public Guid MediaId { get; init; }

    public string City { get; init; }

    public string Purpose { get; init; }
}
