using MPCore.Application.Querying;
using Tiffin.Access.Application.Views;
using Tiffin.Access.Domain;

namespace Tiffin.Access.Application.Ports;

/// <summary>
/// The identity provider, in this platform's own words: people, the city each belongs to, and their roles.
/// </summary>
/// <remarks>
/// <para>
/// An <b>anti-corruption layer</b> (Eric Evans, <i>Domain-Driven Design</i>, 2003). The identity provider
/// speaks of realms, groups, paths, role mappings and representations; the platform speaks of cities and
/// roles. Nothing of the provider's language passes this port, so that the provider can be replaced by
/// replacing one adapter, and so that no other service ever has a reason to call the provider's
/// administration.
/// </para>
/// <para>
/// A failure to reach the provider is thrown as <see cref="DirectoryUnavailableException"/>; "there is no
/// such person" is an answer, and is returned.
/// </para>
/// </remarks>
public interface IIdentityDirectory
{
    Task<Person?> FindAsync(string personId, CancellationToken cancellationToken);

    Task<Page<Person>> PeopleOfAsync(string city, PageRequest page, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one complete authoritative snapshot for startup/recovery reconciliation. The result contains
    /// only business identities (people assigned to a Tiffin city), never credentials or service accounts.
    /// </summary>
    Task<IReadOnlyList<Person>> SnapshotAsync(CancellationToken cancellationToken);

    /// <summary>Gives the role. To give a role the person has changes nothing.</summary>
    Task GrantAsync(string personId, string role, CancellationToken cancellationToken);

    /// <summary>Takes the role away. To take a role the person has not changes nothing.</summary>
    Task RevokeAsync(string personId, string role, CancellationToken cancellationToken);
}

/// <summary>Somebody the identity provider knows. <see cref="City"/> is null for somebody who belongs to no city.</summary>
public sealed record Person(string PersonId, string UserName, string Name, string? City, IReadOnlyList<string> Roles, bool Enabled);

/// <summary>
/// Food Delivery's eventually consistent identity projection. Keycloak remains authoritative; this
/// store gives business data a stable local reference to the immutable Keycloak subject.
/// </summary>
public interface IIdentityProjection
{
    Task UpsertAsync(Person person, string eventId, DateTimeOffset occurredAt, CancellationToken cancellationToken);

    Task DisableAsync(string personId, string eventId, DateTimeOffset occurredAt, CancellationToken cancellationToken);

    Task<Person?> FindAsync(string personId, CancellationToken cancellationToken);

    Task<Page<Person>> PeopleOfAsync(string city, PageRequest page, CancellationToken cancellationToken);
}

/// <summary>The identity provider could not be reached, or did not answer as it should.</summary>
public sealed class DirectoryUnavailableException : Exception
{
    public DirectoryUnavailableException()
    {
    }

    public DirectoryUnavailableException(string message)
        : base(message)
    {
    }

    public DirectoryUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The decisions of one city.</summary>
public interface IGrantRepository
{
    Task<Grant?> GetAsync(Guid id, string city, CancellationToken cancellationToken);

    void Add(Grant grant);
}

/// <summary>The read side. Nothing here is tracked, and nothing is changed.</summary>
public interface IGrantReadModel
{
    Task<GrantView?> FindAsync(Guid id, string city, CancellationToken cancellationToken);

    Task<Page<GrantView>> ListAsync(string city, string? personId, PageRequest page, CancellationToken cancellationToken);
}
