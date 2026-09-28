using MPCore.Domain.Model;
using Tiffin.Media.Domain.Events;
using Tiffin.Media.Domain.Rules;

namespace Tiffin.Media.Domain;

public enum MediaState
{
    /// <summary>A place was reserved; the bytes have not arrived, or have not been looked at.</summary>
    Pending = 0,

    /// <summary>The bytes are in the store, and they are what was announced.</summary>
    Available = 1,

    Deleted = 2
}

/// <summary>One file of the platform: whose it is, what it is for, and where its bytes are.</summary>
/// <remarks>
/// <para>
/// <b>The single source of truth for files.</b> Every other service keeps the identifier of a file and
/// nothing else: no address, no bytes, no copy. An address changes when the store changes, and an
/// identifier does not. Vaughn Vernon: reference other aggregates by identity (<i>Implementing
/// Domain-Driven Design</i>, 2013).
/// </para>
/// <para>
/// <b>The bytes do not pass through this service.</b> It reserves a place, signs an address that is good
/// for a few minutes, and the app sends the bytes to the store itself. A file of fifty megabytes costs this
/// service two small requests. The pattern is the store's own, the <i>presigned URL</i> of Amazon S3, and
/// what Gregor Hohpe and Bobby Woolf call a <i>Claim Check</i> when the identifier travels in place of the
/// payload (<i>Enterprise Integration Patterns</i>, 2003).
/// </para>
/// <para>
/// <b>What was announced is checked.</b> An address that was signed can be used for anything that fits
/// the signature. So a file is not available until this service has asked the store what arrived, and it
/// is what was announced: the size, and the type.
/// </para>
/// </remarks>
public sealed class MediaFile : AggregateRoot<Guid>
{
    /// <summary>How long a reserved place waits for its bytes.</summary>
    public static readonly TimeSpan UploadWindow = TimeSpan.FromMinutes(10);

    private MediaFile()
    {
        City = string.Empty;
        OwnerId = string.Empty;
        Purpose = string.Empty;
        FileName = string.Empty;
        ContentType = string.Empty;
        StorageKey = string.Empty;
    }

    private MediaFile(Guid id, string city, string ownerId, string purpose, string fileName, string contentType, long size, DateTimeOffset now)
        : base(id)
    {
        City = city;
        OwnerId = ownerId;
        Purpose = purpose;
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        // The city comes first: what belongs to a city lies under one prefix of the store, and can be
        // listed, counted, moved or removed as one.
        StorageKey = $"{city}/{purpose}/{id:N}";
        State = MediaState.Pending;
        ReservedOnUtc = now;
        ExpiresOnUtc = now + UploadWindow;
    }

    /// <summary>The tenant.</summary>
    public string City { get; private set; }

    /// <summary>The subject of the token of whoever reserved the place.</summary>
    public string OwnerId { get; private set; }

    public string Purpose { get; private set; }

    /// <summary>The name the file had where it came from. Shown, never used as a path.</summary>
    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long Size { get; private set; }

    /// <summary>Where the bytes are in the store. Made here, never taken from a request.</summary>
    public string StorageKey { get; private set; }

    public MediaState State { get; private set; }

    public DateTimeOffset ReservedOnUtc { get; private set; }

    public DateTimeOffset ExpiresOnUtc { get; private set; }

    public DateTimeOffset? AvailableOnUtc { get; private set; }

    public static MediaFile Reserve(string city, string ownerId, string purpose, string fileName, string contentType, long size, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        CheckRule(new APurposeIsKnown(purpose));
        var kind = Purposes.Of(purpose);
        CheckRule(new ATypeFitsItsPurpose(kind, contentType));
        CheckRule(new ASizeFitsItsPurpose(kind, size));

        return new MediaFile(Guid.CreateVersion7(now), city, ownerId, purpose, fileName.Trim(), contentType.ToLowerInvariant(), size, now);
    }

    public bool HasExpired(DateTimeOffset now) => State == MediaState.Pending && now >= ExpiresOnUtc;

    /// <summary>The store was asked what arrived. When it is what was announced, the file is available.</summary>
    public void Confirm(long sizeInTheStore, string? typeInTheStore, DateTimeOffset now)
    {
        CheckRule(new OnlyAPendingFileIsConfirmed(this));
        CheckRule(new WhatArrivedIsWhatWasAnnounced(this, sizeInTheStore, typeInTheStore));
        State = MediaState.Available;
        AvailableOnUtc = now;
        Raise(new FileAvailable(Id, City, OwnerId, Purpose, ContentType, Size, now));
    }

    public void Delete(DateTimeOffset now)
    {
        if (State == MediaState.Deleted)
        {
            return;
        }

        var wasAvailable = State == MediaState.Available;
        State = MediaState.Deleted;
        if (wasAvailable)
        {
            Raise(new FileDeleted(Id, City, Purpose, now));
        }
    }

    public bool IsOwnedBy(string subjectId) => string.Equals(OwnerId, subjectId, StringComparison.Ordinal);
}
