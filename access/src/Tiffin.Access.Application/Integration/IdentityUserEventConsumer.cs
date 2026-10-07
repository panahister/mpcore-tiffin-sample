using MPCore.Persistence.Abstractions;
using Tiffin.Access.Application.Ports;

namespace Tiffin.Access.Application.Integration;

/// <summary>
/// Translates Keycloak lifecycle events into the local business identity projection. Delivery is
/// at-least-once; the projection rejects duplicates and older versions by event metadata.
/// </summary>
public static class IdentityUserEventConsumer
{
    private const string TiffinRealmPath = "/realms/tiffin";

    private static readonly HashSet<string> RefreshEvents = new(StringComparer.Ordinal)
    {
        "REGISTER",
        "UPDATE_PROFILE",
        "UPDATE_EMAIL",
        "VERIFY_EMAIL"
    };

    public static async Task Handle(
        IdentityUserEventV1 message,
        IIdentityDirectory directory,
        IIdentityProjection projection,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        if (!string.Equals(message.Type, "identity.user-event.v1", StringComparison.Ordinal)
            || !IsTiffinRealm(message)
            || !string.Equals(message.Data.Outcome, "success", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(message.Data.UserId))
        {
            return;
        }

        if (string.Equals(message.Data.KeycloakEventType, "DELETE_ACCOUNT", StringComparison.Ordinal))
        {
            await projection.DisableAsync(
                message.Data.UserId, message.Id, message.Data.OccurredAt, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!RefreshEvents.Contains(message.Data.KeycloakEventType))
        {
            return;
        }

        var person = await directory.FindAsync(message.Data.UserId, cancellationToken).ConfigureAwait(false);
        if (person is null)
        {
            // Keycloak emitted a successful lifecycle event but its authoritative record cannot be read.
            // Treat this as transient so replay/reconciliation can complete rather than storing a ghost.
            throw new DirectoryUnavailableException("The Keycloak user from a lifecycle event is not readable yet.");
        }

        await projection.UpsertAsync(person, message.Id, message.Data.OccurredAt, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsTiffinRealm(IdentityUserEventV1 message) =>
        !string.IsNullOrWhiteSpace(message.Data.RealmId)
        && Uri.TryCreate(message.Source, UriKind.Absolute, out var source)
        && string.Equals(source.AbsolutePath.TrimEnd('/'), TiffinRealmPath, StringComparison.Ordinal);
}
