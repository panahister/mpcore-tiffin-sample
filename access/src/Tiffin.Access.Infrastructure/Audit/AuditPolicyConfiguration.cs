using MPCore.Audit;
using Tiffin.Access.Domain;

namespace Tiffin.Access.Infrastructure.Audit;

/// <summary>
/// Which entity changes the audit trail records. Default deny: an entity that is not declared here
/// leaves no entity-change trace, and a property that is not included is not captured.
/// </summary>
/// <remarks>
/// A decision about a role is what an auditor asks about first: who gave whom which role, in which city,
/// and whether the identity provider has it. The person's name is not recorded as a value: a name changes,
/// and the record names the person by the subject of their tokens.
/// </remarks>
public static class AuditPolicyConfiguration
{
    /// <summary>Declares the audited entities.</summary>
    public static void Configure(AuditPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy.Entity<Grant>("access")
            .Include(g => g.City)
            .Include(g => g.PersonId)
            .Include(g => g.Role)
            .Include(g => g.Kind)
            .Include(g => g.State)
            .Include(g => g.Failure);
    }
}
