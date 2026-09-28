namespace Tiffin.Media.Api.Hosting;

/// <summary>The Kafka topics this host writes: what happened to a file, for whoever keeps its identifier.</summary>
public static class MediaTopics
{
    public const string FileAvailable = "tiffin.media.file-available.v1";
    public const string FileDeleted = "tiffin.media.file-deleted.v1";
}
