using Tiffin.Media.Domain;

namespace Tiffin.Media.Application.Ports;

/// <summary>The files of one city.</summary>
public interface IMediaRepository
{
    Task<MediaFile?> GetAsync(Guid id, string city, CancellationToken cancellationToken);

    void Add(MediaFile file);
}

/// <summary>The read side. Nothing here is tracked, and nothing is changed.</summary>
public interface IMediaReadModel
{
    Task<MediaFile?> FindAsync(Guid id, string city, CancellationToken cancellationToken);
}

/// <summary>
/// The store of the bytes, by what every such store can do: sign an address to send to, sign an address to
/// fetch from, say what lies at a key, and remove it.
/// </summary>
/// <remarks>
/// The port names no product. The adapter speaks the S3 API, which is a published interface that several
/// stores implement; the sample runs two of them, and Media's code is the same for both.
/// </remarks>
public interface IObjectStore
{
    /// <summary>An address the bytes can be sent to, with PUT, until it expires. It is good for this key and this type only.</summary>
    Task<Uri> SignUploadAsync(string key, string contentType, TimeSpan validFor, CancellationToken cancellationToken);

    /// <summary>An address the bytes can be fetched from, with GET, until it expires.</summary>
    Task<Uri> SignDownloadAsync(string key, TimeSpan validFor, CancellationToken cancellationToken);

    /// <summary>What lies at the key, or null when nothing does.</summary>
    Task<StoredObject?> LookAsync(string key, CancellationToken cancellationToken);

    /// <summary>Removes what lies at the key. To remove what is not there changes nothing.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken);
}

public sealed record StoredObject(long Size, string? ContentType);

/// <summary>The store could not be reached, or did not answer as it should.</summary>
public sealed class StoreUnavailableException : Exception
{
    public StoreUnavailableException()
    {
    }

    public StoreUnavailableException(string message)
        : base(message)
    {
    }

    public StoreUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
