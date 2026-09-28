using MPCore.Audit;
using Tiffin.Kitchen.Domain;

namespace Tiffin.Kitchen.Infrastructure.Audit;

/// <summary>
/// Which entity changes the audit trail records. Default deny: an entity that is not declared here
/// leaves no entity-change trace, and a property that is not included is not captured.
/// </summary>
public static class AuditPolicyConfiguration
{
    /// <summary>Declares the audited entities.</summary>
    public static void Configure(AuditPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy.Entity<Ticket>("kitchen")
            .Include(t => t.Status)
            .Include(t => t.ReadyInMinutes)
            .Include(t => t.Reason);
    }
}
