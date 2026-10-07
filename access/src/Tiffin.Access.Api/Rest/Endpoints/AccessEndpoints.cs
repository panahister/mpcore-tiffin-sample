using MPCore.Application.Querying;
using MPCore.Application.Results;
using MPCore.Transport.Http;
using Tiffin.Access.Api.Hosting;
using Tiffin.Access.Application.Commands;
using Tiffin.Access.Application.Queries;
using Tiffin.Access.Application.Views;
using Wolverine;

namespace Tiffin.Access.Api.Rest.Endpoints;

/// <summary>The REST surface of the Access service: the people of a city, their roles, and the decisions about them.</summary>
/// <remarks>
/// <para>
/// Every endpoint binds the request, invokes one command or query through Wolverine, and renders the
/// <see cref="Result"/> with MP Core's <c>ToHttpResult</c>. A failure becomes RFC 9457 Problem Details
/// with the failure's stable code; no endpoint builds an error by hand.
/// </para>
/// <para>
/// A role is given with PUT and taken with DELETE: both name the state that is wanted, and both are safe
/// to repeat. The answer is 202, because the identity provider is told afterwards; the Location is the
/// decision, which says what became of it.
/// </para>
/// </remarks>
public static class AccessEndpoints
{
    public static RouteGroupBuilder MapAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var access = endpoints.MapGroup("/v1/access").WithTags("Access").RequireAuthorization(AccessPolicies.Admin);

        access.MapGet("/roles", static async (IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<GrantableRoles>>(new GetGrantableRoles(), ct).ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .Produces<GrantableRoles>(200)
            .WithName("GetGrantableRoles");

        access.MapGet("/people", static async (string? city, int? page, int? size, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<Page<PersonView>>>(new ListPeople(city, page ?? 1, size ?? PageRequest.DefaultSize), ct)
                    .ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .Produces<Page<PersonView>>(200)
            .WithName("ListPeople");

        access.MapGet("/people/{personId}", static async (string personId, string? city, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<PersonView>>(new GetPerson(personId, city), ct).ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .Produces<PersonView>(200)
            .WithName("GetPerson");

        access.MapPut("/people/{personId}/roles/{role}", static async (string personId, string role, string? city, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<GrantView>>(new ChangeRole(personId, role, true, city), ct).ConfigureAwait(false))
                .ToHttpResult(grant => Results.Accepted($"/v1/access/grants/{grant.GrantId}", grant)))
            .Produces<GrantView>(202)
            .WithName("GiveRole");

        access.MapDelete("/people/{personId}/roles/{role}", static async (string personId, string role, string? city, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<GrantView>>(new ChangeRole(personId, role, false, city), ct).ConfigureAwait(false))
                .ToHttpResult(grant => Results.Accepted($"/v1/access/grants/{grant.GrantId}", grant)))
            .Produces<GrantView>(202)
            .WithName("TakeRole");

        access.MapGet("/grants", static async (string? city, string? personId, int? page, int? size, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<Page<GrantView>>>(new ListGrants(city, personId, page ?? 1, size ?? PageRequest.DefaultSize), ct)
                    .ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .Produces<Page<GrantView>>(200)
            .WithName("ListGrants");

        access.MapGet("/grants/{grantId:guid}", static async (Guid grantId, string? city, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<GrantView>>(new GetGrant(grantId, city), ct).ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .Produces<GrantView>(200)
            .WithName("GetGrant");

        return access;
    }
}
