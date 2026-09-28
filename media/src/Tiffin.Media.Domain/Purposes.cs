namespace Tiffin.Media.Domain;

/// <summary>What a file is for decides what it may be: which types, and how large.</summary>
public sealed record Purpose(string Name, IReadOnlyList<string> ContentTypes, long MaximumSize);

/// <summary>The purposes the platform has files for. A file for anything else is refused.</summary>
public static class Purposes
{
    private const long Megabyte = 1024 * 1024;

    private static readonly string[] Pictures = ["image/jpeg", "image/png", "image/webp"];

    private static readonly Dictionary<string, Purpose> All = new(StringComparer.Ordinal)
    {
        ["restaurant-picture"] = new("restaurant-picture", Pictures, 5 * Megabyte),
        ["menu-picture"] = new("menu-picture", Pictures, 5 * Megabyte),
        ["courier-document"] = new("courier-document", [.. Pictures, "application/pdf"], 10 * Megabyte),
    };

    public static bool IsKnown(string? name) => name is not null && All.ContainsKey(name);

    public static Purpose Of(string name) => All[name];
}
