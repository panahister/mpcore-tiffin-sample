using MPCore.Domain.Events;

namespace Tiffin.Payments.Domain.Events;

// The answers of this service. Each is sent on a queue of its own to the Ordering service, which declares
// the same contract in its own code. Decline codes and references are the provider's.

/// <summary>The card was charged.</summary>
public sealed record PaymentAuthorized : IntegrationEvent
{
    public const string Name = "tiffin.payments.payment-authorized";

    public PaymentAuthorized(Guid orderId, string providerReference, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        ProviderReference = providerReference;
    }

    public Guid OrderId { get; init; }

    public string ProviderReference { get; init; }
}

/// <summary>The card was not charged, and will not be.</summary>
public sealed record PaymentDeclined : IntegrationEvent
{
    public const string Name = "tiffin.payments.payment-declined";

    public PaymentDeclined(Guid orderId, string declineCode, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        DeclineCode = declineCode;
    }

    public Guid OrderId { get; init; }

    public string DeclineCode { get; init; }
}

/// <summary>What was charged went back.</summary>
public sealed record PaymentRefunded : IntegrationEvent
{
    public const string Name = "tiffin.payments.payment-refunded";

    public PaymentRefunded(Guid orderId, string refundReference, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        RefundReference = refundReference;
    }

    public Guid OrderId { get; init; }

    public string RefundReference { get; init; }
}
