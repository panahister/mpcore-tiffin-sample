using MPCore.Audit;
using Tiffin.Restaurants.Domain;

namespace Tiffin.Restaurants.Infrastructure.Audit;

/// <summary>
/// Which entity changes the audit trail records. Default deny: an entity that is not declared here
/// leaves no entity-change trace, and a property that is not included is not captured.
/// </summary>
/// <remarks>
/// What a reviewer asks of a restaurant is who opened or closed it and who renamed it, in which city.
/// The manager's identifier is not recorded as a value: the record already names the actor.
/// </remarks>
public static class AuditPolicyConfiguration
{
    /// <summary>Declares the audited entities.</summary>
    public static void Configure(AuditPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy.Entity<Restaurant>("restaurants")
            .Include(r => r.Name)
            .Include(r => r.City)
            .Include(r => r.IsOpen);
    }
}
