using MPCore.Application.Messaging;
using MPCore.Application.Querying;
using MPCore.Application.Results;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Access.Application.Ports;
using Tiffin.Access.Application.Views;
using Tiffin.Access.Domain;

namespace Tiffin.Access.Application.Queries;

/// <summary>The people of a city, with their roles. <paramref name="City"/> is for an admin of the platform.</summary>
public sealed record ListPeople(string? City, int Page = 1, int Size = PageRequest.DefaultSize) : IQuery<Result<Page<PersonView>>>;

public sealed record GetPerson(string PersonId, string? City) : IQuery<Result<PersonView>>;

/// <summary>What became of a decision.</summary>
public sealed record GetGrant(Guid GrantId, string? City) : IQuery<Result<GrantView>>;

/// <summary>The decisions of a city, newest first, or those about one person.</summary>
public sealed record ListGrants(string? City, string? PersonId, int Page = 1, int Size = PageRequest.DefaultSize) : IQuery<Result<Page<GrantView>>>;

/// <summary>The roles the caller may hand out.</summary>
public sealed record GetGrantableRoles : IQuery<Result<GrantableRoles>>;

/// <summary>
/// People are read from the local Keycloak-owned projection; decisions are read from this service's
/// database. Credentials and identity mutation remain exclusively in Keycloak.
/// </summary>
public static class AccessQueriesHandler
{
    public static async Task<Result<Page<PersonView>>> Handle(
        ListPeople query, ICurrentActorAccessor actor, ITenantContext tenant, IIdentityProjection projection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(projection);

        if (CityOf(actor.Current, tenant, query.City) is not { IsSuccess: true } city)
        {
            return Result<Page<PersonView>>.FromFailure(Refusal(actor.Current));
        }

        var people = await projection.PeopleOfAsync(city.Value, new PageRequest(query.Page, query.Size), cancellationToken).ConfigureAwait(false);
        return Result<Page<PersonView>>.Success(new Page<PersonView>([.. people.Items.Select(AccessViews.Of)], people.Number, people.Size, people.Total));
    }

    public static async Task<Result<PersonView>> Handle(
        GetPerson query, ICurrentActorAccessor actor, ITenantContext tenant, IIdentityProjection projection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(projection);

        if (CityOf(actor.Current, tenant, query.City) is not { IsSuccess: true } city)
        {
            return Result<PersonView>.FromFailure(Refusal(actor.Current));
        }

        var person = await projection.FindAsync(query.PersonId, cancellationToken).ConfigureAwait(false);
        return person is null || !string.Equals(person.City, city.Value, StringComparison.Ordinal)
            ? Result<PersonView>.FromFailure(AccessFailures.PersonNotFound())
            : Result<PersonView>.Success(AccessViews.Of(person));
    }

    public static async Task<Result<GrantView>> Handle(
        GetGrant query, ICurrentActorAccessor actor, ITenantContext tenant, IGrantReadModel grants, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(grants);

        if (CityOf(actor.Current, tenant, query.City) is not { IsSuccess: true } city)
        {
            return Result<GrantView>.FromFailure(Refusal(actor.Current));
        }

        var grant = await grants.FindAsync(query.GrantId, city.Value, cancellationToken).ConfigureAwait(false);
        return grant is null ? Result<GrantView>.FromFailure(AccessFailures.GrantNotFound()) : Result<GrantView>.Success(grant);
    }

    public static async Task<Result<Page<GrantView>>> Handle(
        ListGrants query, ICurrentActorAccessor actor, ITenantContext tenant, IGrantReadModel grants, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(grants);

        if (CityOf(actor.Current, tenant, query.City) is not { IsSuccess: true } city)
        {
            return Result<Page<GrantView>>.FromFailure(Refusal(actor.Current));
        }

        return Result<Page<GrantView>>.Success(
            await grants.ListAsync(city.Value, query.PersonId, new PageRequest(query.Page, query.Size), cancellationToken).ConfigureAwait(false));
    }

    public static Result<GrantableRoles> Handle(GetGrantableRoles query, ICurrentActorAccessor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return Cities.IsAdmin(actor.Current)
            ? Result<GrantableRoles>.Success(new GrantableRoles(Roles.GrantableBy(actor.Current.Roles)))
            : Result<GrantableRoles>.FromFailure(AccessFailures.AdminRequired());
    }

    private static Result<string>? CityOf(CurrentActor actor, ITenantContext tenant, string? asked) =>
        Cities.IsAdmin(actor) && Cities.For(actor, tenant, asked) is { } city ? Result<string>.Success(city) : null;

    private static FailureDescriptor Refusal(CurrentActor actor) =>
        Cities.IsAdmin(actor) ? AccessFailures.CityRequired() : AccessFailures.AdminRequired();
}
