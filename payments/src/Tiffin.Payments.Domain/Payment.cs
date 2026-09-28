using MPCore.Domain.Model;
using Tiffin.Payments.Domain.Events;
using Tiffin.Payments.Domain.Rules;

namespace Tiffin.Payments.Domain;

public enum PaymentStatus
{
    /// <summary>A reference exists; the card has not been charged.</summary>
    Pending = 0,

    Authorized = 1,

    Declined = 2,

    Refunded = 3,

    /// <summary>Not wanted any more before it was charged. It never will be.</summary>
    Voided = 4
}

/// <summary>The money of one order: a reference for a card, then a charge, then perhaps a refund.</summary>
/// <remarks>
/// <para>
/// <b>The card's token lives here and nowhere else, and not for long.</b> The Ordering service hands it
/// over in one call and keeps the identifier of this payment. The token is erased the moment it was used
/// (charged or declined), the moment the payment is voided, and thirty minutes after it was opened if
/// nothing happened. No message and no log carries it. PCI DSS, requirement 3: keep cardholder data only
/// as long as it is needed.
/// </para>
/// <para>
/// <b>Every answer this service gives is an event of this aggregate</b>, so an answer leaves with the
/// commit of the change it reports, and never without it (the transactional outbox).
/// </para>
/// </remarks>
public sealed class Payment : AggregateRoot<Guid>
{
    /// <summary>How long a reference waits to be used.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private Payment()
    {
        City = string.Empty;
        CustomerId = string.Empty;
        Currency = string.Empty;
    }

    private Payment(Guid id, Guid orderId, string city, string customerId, string paymentToken, decimal amount, string currency, DateTimeOffset now)
        : base(id)
    {
        OrderId = orderId;
        City = city;
        CustomerId = customerId;
        PaymentToken = paymentToken;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.Pending;
        OpenedOnUtc = now;
        ExpiresOnUtc = now + Lifetime;
    }

    public Guid OrderId { get; private set; }

    /// <summary>The tenant.</summary>
    public string City { get; private set; }

    public string CustomerId { get; private set; }

    /// <summary>The provider's token for the card, until it was used. Never logged, never audited, never sent on.</summary>
    public string? PaymentToken { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string? ProviderReference { get; private set; }

    public string? RefundReference { get; private set; }

    public string? DeclineCode { get; private set; }

    public DateTimeOffset OpenedOnUtc { get; private set; }

    public DateTimeOffset ExpiresOnUtc { get; private set; }

    public static Payment Open(Guid orderId, string city, string customerId, string paymentToken, decimal amount, string currency, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        CheckRule(new AnAmountIsPositive(amount));

        return new Payment(Guid.CreateVersion7(now), orderId, city, customerId, paymentToken, amount, currency.ToUpperInvariant(), now);
    }

    public bool HasExpired(DateTimeOffset now) => Status == PaymentStatus.Pending && now >= ExpiresOnUtc;

    public void Authorize(string providerReference, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerReference);
        CheckRule(new APaymentIsChargedOnce(this));
        Status = PaymentStatus.Authorized;
        ProviderReference = providerReference;
        PaymentToken = null;
        Raise(new PaymentAuthorized(OrderId, providerReference, now));
    }

    public void Decline(string declineCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declineCode);
        CheckRule(new APaymentIsChargedOnce(this));
        Status = PaymentStatus.Declined;
        DeclineCode = declineCode;
        PaymentToken = null;
        Raise(new PaymentDeclined(OrderId, declineCode, now));
    }

    public void Refund(string refundReference, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refundReference);
        CheckRule(new OnlyAChargeIsRefunded(this));
        Status = PaymentStatus.Refunded;
        RefundReference = refundReference;
        Raise(new PaymentRefunded(OrderId, refundReference, now));
    }

    /// <summary>The order was cancelled before its card was charged: it will not be.</summary>
    public void Void()
    {
        CheckRule(new APaymentIsChargedOnce(this));
        Status = PaymentStatus.Voided;
        PaymentToken = null;
    }

    /// <summary>What a log may show of a payment: never the token.</summary>
    public override string ToString() => $"{nameof(Payment)} {{ OrderId = {OrderId}, Status = {Status} }}";
}
