using System.Globalization;
using MPCore.Domain.Rules;

namespace Tiffin.Payments.Domain.Rules;

/// <summary>
/// A business rule of the Payments service, reported under the <c>tiffin.payments</c> error domain with a
/// stable code and a message key. The named-rule pattern is Kamil Grzybek's (<i>Modular Monolith with
/// DDD</i>); checking before changing is Vladimir Khorikov's <i>always-valid domain model</i>.
/// </summary>
public abstract class PaymentRule(string code, string messageKey, IReadOnlyDictionary<string, string>? arguments = null)
    : BusinessRule(Domain, code, messageKey, arguments)
{
    public const string Domain = "tiffin.payments";
}

/// <summary>Rule P1: an amount is more than nothing.</summary>
public sealed class AnAmountIsPositive(decimal amount) : PaymentRule(
    "AMOUNT_NOT_POSITIVE", "payments.amount_not_positive",
    new Dictionary<string, string> { ["amount"] = amount.ToString(CultureInfo.InvariantCulture) })
{
    public override bool IsBroken() => amount <= 0;
}

/// <summary>Rule P2: a card is charged once for an order, or not at all.</summary>
public sealed class APaymentIsChargedOnce(Payment payment) : PaymentRule(
    "PAYMENT_NOT_PENDING", "payments.not_pending", new Dictionary<string, string> { ["status"] = payment.Status.ToString() })
{
    public override bool IsBroken() => payment.Status != PaymentStatus.Pending;
}

/// <summary>Rule P3: what was not charged is not given back.</summary>
public sealed class OnlyAChargeIsRefunded(Payment payment) : PaymentRule(
    "PAYMENT_NOT_CHARGED", "payments.not_charged", new Dictionary<string, string> { ["status"] = payment.Status.ToString() })
{
    public override bool IsBroken() => payment.Status != PaymentStatus.Authorized;
}
