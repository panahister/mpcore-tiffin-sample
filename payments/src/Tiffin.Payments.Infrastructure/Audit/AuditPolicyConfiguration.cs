using MPCore.Audit;
using Tiffin.Payments.Domain;

namespace Tiffin.Payments.Infrastructure.Audit;

/// <summary>
/// Which entity changes the audit trail records. Default deny: an entity that is not declared here
/// leaves no entity-change trace, and a property that is not included is not captured.
/// </summary>
/// <remarks>
/// The card's token is a credential and is not included: MP Core's policy would refuse it, and it is
/// never offered. What a reviewer asks of a payment is what happened to the money, and that is recorded.
/// </remarks>
public static class AuditPolicyConfiguration
{
    /// <summary>Declares the audited entities.</summary>
    public static void Configure(AuditPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        policy.Entity<Payment>("payments")
            .Include(p => p.Status)
            .Include(p => p.Amount)
            .Include(p => p.Currency)
            .Include(p => p.ProviderReference)
            .Include(p => p.RefundReference)
            .Include(p => p.DeclineCode);
    }
}
