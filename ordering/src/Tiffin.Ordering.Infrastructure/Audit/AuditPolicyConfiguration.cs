using MPCore.Audit;
using Tiffin.Ordering.Domain;

namespace Tiffin.Ordering.Infrastructure.Audit;

/// <summary>
/// Which entity changes the audit trail records. Default deny: an entity that is not declared here
/// leaves no entity-change trace, and a property that is not included is not captured.
/// </summary>
/// <remarks>
/// What a reviewer asks of an order is how it moved, and why it ended. The address is personal data and
/// is deliberately not audited; the payment reference is a reference, not a credential, and is.
/// </remarks>
public static class AuditPolicyConfiguration
{
    /// <summary>Declares the audited entities.</summary>
    public static void Configure(AuditPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy.Entity<Order>("ordering")
            .Include(o => o.Status)
            .Include(o => o.CancellationReason)
            .Include(o => o.PaymentReference)
            .Include(o => o.RefundReference)
            .Include(o => o.CourierName);
    }
}
