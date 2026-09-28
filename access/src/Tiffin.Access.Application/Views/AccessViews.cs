using Tiffin.Access.Application.Ports;
using Tiffin.Access.Domain;

namespace Tiffin.Access.Application.Views;

/// <summary>Somebody of a city, as an admin sees them: who, and with which roles of the platform.</summary>
public sealed record PersonView(string PersonId, string UserName, string Name, string? City, IReadOnlyList<string> Roles, bool Enabled);

public sealed record GrantView(
    Guid GrantId, string City, string PersonId, string PersonName, string Role, string Kind, string State, string DecidedBy,
    DateTimeOffset DecidedOnUtc, DateTimeOffset? AppliedOnUtc, string? Failure);

public sealed record GrantableRoles(IReadOnlyList<string> Roles);

public static class AccessViews
{
    /// <summary>Only the roles of the platform are shown. What else the identity provider keeps on a person is its own.</summary>
    public static PersonView Of(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return new PersonView(
            person.PersonId, person.UserName, person.Name, person.City,
            [.. person.Roles.Where(static r => Domain.Roles.Known.Contains(r) || r == Domain.Roles.PlatformAdmin).Order(StringComparer.Ordinal)],
            person.Enabled);
    }

    public static GrantView Of(Grant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return new GrantView(
            grant.Id, grant.City, grant.PersonId, grant.PersonName, grant.Role, grant.Kind.ToString(), grant.State.ToString(), grant.DecidedBy,
            grant.DecidedOnUtc, grant.AppliedOnUtc, grant.Failure);
    }
}
