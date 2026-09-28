using System.Globalization;
using MPCore.Domain.Rules;

namespace Tiffin.Media.Domain.Rules;

/// <summary>
/// A business rule of the Media service, reported under the <c>tiffin.media</c> error domain with a stable
/// code and a message key. The named-rule pattern is Kamil Grzybek's (<i>Modular Monolith with DDD</i>);
/// checking before changing is Vladimir Khorikov's <i>always-valid domain model</i>.
/// </summary>
public abstract class MediaRule(string code, string messageKey, IReadOnlyDictionary<string, string>? arguments = null)
    : BusinessRule(Domain, code, messageKey, arguments)
{
    public const string Domain = "tiffin.media";
}

/// <summary>Rule M1: a file is for something the platform has files for.</summary>
public sealed class APurposeIsKnown(string? purpose) : MediaRule(
    "PURPOSE_UNKNOWN", "media.purpose_unknown", new Dictionary<string, string> { ["purpose"] = purpose ?? string.Empty })
{
    public override bool IsBroken() => !Purposes.IsKnown(purpose);
}

/// <summary>Rule M2: a file is of a type its purpose allows.</summary>
public sealed class ATypeFitsItsPurpose(Purpose purpose, string? contentType) : MediaRule(
    "TYPE_NOT_ALLOWED", "media.type_not_allowed",
    new Dictionary<string, string> { ["type"] = contentType ?? string.Empty, ["purpose"] = purpose.Name, ["allowed"] = string.Join(", ", purpose.ContentTypes) })
{
    public override bool IsBroken() => contentType is null || !purpose.ContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Rule M3: a file is not empty, and not larger than its purpose allows.</summary>
public sealed class ASizeFitsItsPurpose(Purpose purpose, long size) : MediaRule(
    "SIZE_NOT_ALLOWED", "media.size_not_allowed",
    new Dictionary<string, string>
    {
        ["size"] = size.ToString(CultureInfo.InvariantCulture),
        ["purpose"] = purpose.Name,
        ["maximum"] = purpose.MaximumSize.ToString(CultureInfo.InvariantCulture)
    })
{
    public override bool IsBroken() => size <= 0 || size > purpose.MaximumSize;
}

/// <summary>Rule M4: a file is confirmed once.</summary>
public sealed class OnlyAPendingFileIsConfirmed(MediaFile file) : MediaRule(
    "FILE_NOT_PENDING", "media.file_not_pending", new Dictionary<string, string> { ["state"] = file.State.ToString() })
{
    public override bool IsBroken() => file.State != MediaState.Pending;
}

/// <summary>Rule M5: what arrived in the store is what was announced.</summary>
public sealed class WhatArrivedIsWhatWasAnnounced(MediaFile file, long sizeInTheStore, string? typeInTheStore) : MediaRule(
    "NOT_WHAT_WAS_ANNOUNCED", "media.not_what_was_announced",
    new Dictionary<string, string>
    {
        ["announced"] = string.Create(CultureInfo.InvariantCulture, $"{file.Size} bytes of {file.ContentType}"),
        ["arrived"] = string.Create(CultureInfo.InvariantCulture, $"{sizeInTheStore} bytes of {typeInTheStore ?? "an unknown type"}")
    })
{
    public override bool IsBroken() =>
        sizeInTheStore != file.Size || !string.Equals(typeInTheStore, file.ContentType, StringComparison.OrdinalIgnoreCase);
}
