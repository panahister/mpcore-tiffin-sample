using Tiffin.Access.Application.Ports;

namespace Tiffin.Access.Application.Integration;

/// <summary>
/// Rebuilds the local business identity reference from a complete Keycloak snapshot. It complements
/// lifecycle events: realm-imported users predate the exporter and therefore have no REGISTER event.
/// </summary>
public static class IdentityProjectionReconciliation
{
    public static async Task<int> ReconcileAsync(
        IIdentityDirectory directory,
        IIdentityProjection projection,
        DateTimeOffset observedOnUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(projection);

        var people = await directory.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        var reconciliationId = $"keycloak-reconciliation:{observedOnUtc:O}";
        foreach (var person in people)
        {
            await projection.UpsertAsync(person, reconciliationId, observedOnUtc, cancellationToken).ConfigureAwait(false);
        }

        return people.Count;
    }
}
