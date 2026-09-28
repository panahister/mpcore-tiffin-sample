using MPCore.Audit;
using Tiffin.Media.Domain;

namespace Tiffin.Media.Infrastructure.Audit;

/// <summary>
/// Which entity changes the audit trail records. Default deny: an entity that is not declared here
/// leaves no entity-change trace, and a property that is not included is not captured.
/// </summary>
/// <remarks>
/// What a reviewer asks of a file is who put what, for what, and what became of it. The file's name is
/// what somebody called it on their own machine, may say something about them, and is not recorded.
/// </remarks>
public static class AuditPolicyConfiguration
{
    /// <summary>Declares the audited entities.</summary>
    public static void Configure(AuditPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy.Entity<MediaFile>("media")
            .Include(f => f.Purpose)
            .Include(f => f.ContentType)
            .Include(f => f.Size)
            .Include(f => f.State);
    }
}
