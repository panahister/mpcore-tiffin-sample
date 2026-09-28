using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Access.Domain;

namespace Tiffin.Access.Application;

/// <summary>Which city an admin works in.</summary>
public static class Cities
{
    /// <summary>
    /// An admin of a city works in the city of their token, whatever they ask for. An admin of the platform
    /// belongs to no city and names the one they mean. Null when the caller is neither, or names none.
    /// </summary>
    public static string? For(CurrentActor actor, ITenantContext tenant, string? asked)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);

        if (actor.HasRole(Roles.PlatformAdmin))
        {
            return string.IsNullOrWhiteSpace(asked) ? null : asked.Trim().ToLowerInvariant();
        }

        return actor.HasRole(Roles.CityAdmin) ? tenant.TenantId : null;
    }

    public static bool IsAdmin(CurrentActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.SubjectId is not null && (actor.HasRole(Roles.PlatformAdmin) || actor.HasRole(Roles.CityAdmin));
    }
}
