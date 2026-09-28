using MPCore.Domain.Events;

namespace Tiffin.Payments.Application.Contracts;

// What the Ordering service asks, as this service reads it. The two share no assembly: this is the
// reader's copy of each contract, and it declares only what Payments uses.

/// <summary><c>tiffin.ordering.payment-requested</c>, version 1.</summary>
public sealed record PaymentRequested : IntegrationEvent
{
    public const string Name = "tiffin.ordering.payment-requested";

    public PaymentRequested(Guid eventId, Guid orderId, Guid paymentIntentId, decimal amount, string currency, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        PaymentIntentId = paymentIntentId;
        Amount = amount;
        Currency = currency;
    }

    public Guid OrderId { get; init; }

    public Guid PaymentIntentId { get; init; }

    public decimal Amount { get; init; }

    public string Currency { get; init; }
}

/// <summary><c>tiffin.ordering.refund-requested</c>, version 1.</summary>
public sealed record RefundRequested : IntegrationEvent
{
    public const string Name = "tiffin.ordering.refund-requested";

    public RefundRequested(Guid eventId, Guid orderId, string reason, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        Reason = reason;
    }

    public Guid OrderId { get; init; }

    public string Reason { get; init; }
}
