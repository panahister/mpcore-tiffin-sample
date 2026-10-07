using MPCore.Application.Messaging;
using MPCore.Application.Results;
using MPCore.Application.Time;
using MPCore.Audit;
using MPCore.Messaging.Abstractions;
using MPCore.Persistence.Abstractions;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Access.Application.Ports;
using Tiffin.Access.Application.Views;
using Tiffin.Access.Domain;

namespace Tiffin.Access.Application.Commands;

/// <summary>An admin gives somebody a role, or takes one away. <paramref name="City"/> is for an admin of the platform.</summary>
public sealed record ChangeRole(string PersonId, string Role, bool Give, string? City) : ICommand<Result<GrantView>>;

/// <summary>Tells the identity provider what was decided. Sent by this service to itself, with the decision.</summary>
public sealed record ApplyGrant(Guid GrantId);

/// <summary>
/// Records the decision, and a message to apply it, in one transaction. The identity provider is told
/// afterwards, by <see cref="ApplyGrantHandler"/>.
/// </summary>
/// <remarks>
/// The person is looked up at the identity provider while the admin waits, so that the admin can be told
/// at once that there is no such person in their city. Who the admin is, and which city is theirs, comes
/// from the validated token.
/// </remarks>
public static class ChangeRoleHandler
{
    public static async Task<Result<GrantView>> Handle(
        ChangeRole command, ICurrentActorAccessor actor, ITenantContext tenant, IIdentityDirectory directory, IGrantRepository grants,
        IMessagePublisher publisher, IBusinessAuditRecorder audit, IUnitOfWork unitOfWork, IClock clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(audit);

        var admin = actor.Current;
        if (!Cities.IsAdmin(admin))
        {
            return Result<GrantView>.FromFailure(AccessFailures.AdminRequired());
        }

        if (Cities.For(admin, tenant, command.City) is not { } city)
        {
            return Result<GrantView>.FromFailure(AccessFailures.CityRequired());
        }

        Person? person;
        try
        {
            person = await directory.FindAsync(command.PersonId, cancellationToken).ConfigureAwait(false);
        }
        catch (DirectoryUnavailableException)
        {
            return Result<GrantView>.FromFailure(AccessFailures.DirectoryUnavailable());
        }

        if (person is null || !string.Equals(person.City, city, StringComparison.Ordinal))
        {
            return Result<GrantView>.FromFailure(AccessFailures.PersonNotFound());
        }

        // A role that is not the admin's to give breaks rule A2; the admin's own roles, rule A3.
        var grant = Grant.Decide(
            city, person.PersonId, person.Name, command.Role.Trim(), command.Give ? GrantKind.Grant : GrantKind.Revoke,
            admin.SubjectId!, admin.Roles, clock.UtcNow);
        grants.Add(grant);

        // The city of the decision is the city of the person, which for an admin of the platform is not
        // the city of their token. The message says so itself.
        await publisher.PublishAsync(new ApplyGrant(grant.Id), new MessageDeliveryContext(null, null, city), cancellationToken).ConfigureAwait(false);
        await audit.RecordAsync(
            "access", command.Give ? "role-granted" : "role-revoked", nameof(Person), person.PersonId,
            new Dictionary<string, string> { ["role"] = grant.Role, ["city"] = city, ["grant"] = grant.Id.ToString() }, cancellationToken)
            .ConfigureAwait(false);
        return Result<GrantView>.Success(AccessViews.Of(grant));
    }
}

/// <summary>Tells the identity provider, and marks the decision applied. Runs from a durable queue of this service's own.</summary>
/// <remarks>
/// When the provider cannot be reached the handler throws a retryable failure. The host's error policy
/// obeys its directive: redelivery with a cooldown, then the error queue, and then the decision is marked
/// failed (<c>Hosting/GivenUpMessages.cs</c>), so that the admin who waits for it is told.
/// </remarks>
public static class ApplyGrantHandler
{
    public static async Task Handle(
        ApplyGrant message, IGrantRepository grants, ITenantContext tenant, IIdentityDirectory directory, IIdentityProjection projection,
        IUnitOfWork unitOfWork, IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(projection);

        var city = tenant.TenantId
            ?? throw new InvalidOperationException($"The message for grant {message.GrantId} names no city; every message of the platform carries its tenant.");
        var grant = await grants.GetAsync(message.GrantId, city, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Grant {message.GrantId} does not exist in {city}.");
        if (grant.State == GrantState.Applied)
        {
            return;
        }

        try
        {
            if (grant.Kind == GrantKind.Grant)
            {
                await directory.GrantAsync(grant.PersonId, grant.Role, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await directory.RevokeAsync(grant.PersonId, grant.Role, cancellationToken).ConfigureAwait(false);
            }

            var person = await directory.FindAsync(grant.PersonId, cancellationToken).ConfigureAwait(false)
                ?? throw new DirectoryUnavailableException("The identity provider did not return the changed person.");
            var appliedOnUtc = clock.UtcNow;
            await projection.UpsertAsync(person, $"access-grant:{grant.Id:N}", appliedOnUtc, cancellationToken).ConfigureAwait(false);
            grant.MarkApplied(appliedOnUtc);
        }
        catch (DirectoryUnavailableException)
        {
            throw new ResultFailureException(AccessFailures.DirectoryUnavailable());
        }
    }
}

/// <summary>The step that tells the identity provider was given up. Sent by the host's error policy.</summary>
public sealed record GrantGivenUp(Guid GrantId, string Failure);

public static class GrantGivenUpHandler
{
    public static async Task Handle(
        GrantGivenUp message, IGrantRepository grants, ITenantContext tenant, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(tenant);

        if (tenant.TenantId is { } city && await grants.GetAsync(message.GrantId, city, cancellationToken).ConfigureAwait(false) is { } grant)
        {
            grant.MarkFailed(message.Failure);
        }
    }
}
