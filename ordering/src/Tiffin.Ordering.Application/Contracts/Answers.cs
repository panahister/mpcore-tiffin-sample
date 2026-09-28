using MPCore.Domain.Events;

namespace Tiffin.Ordering.Application.Contracts;

// What the other services answer. Each arrives on a queue of its own. This is the reader's copy of each
// contract, and it declares only what the order uses.

/// <summary>From Payments: <c>tiffin.payments.payment-authorized</c>, version 1.</summary>
public sealed record PaymentAuthorized : IntegrationEvent
{
    public const string Name = "tiffin.payments.payment-authorized";

    public PaymentAuthorized(Guid eventId, Guid orderId, string providerReference, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        ProviderReference = providerReference;
    }

    public Guid OrderId { get; init; }

    public string ProviderReference { get; init; }
}

/// <summary>From Payments: <c>tiffin.payments.payment-declined</c>, version 1.</summary>
public sealed record PaymentDeclined : IntegrationEvent
{
    public const string Name = "tiffin.payments.payment-declined";

    public PaymentDeclined(Guid eventId, Guid orderId, string declineCode, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        DeclineCode = declineCode;
    }

    public Guid OrderId { get; init; }

    public string DeclineCode { get; init; }
}

/// <summary>From Payments: <c>tiffin.payments.payment-refunded</c>, version 1.</summary>
public sealed record PaymentRefunded : IntegrationEvent
{
    public const string Name = "tiffin.payments.payment-refunded";

    public PaymentRefunded(Guid eventId, Guid orderId, string refundReference, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        RefundReference = refundReference;
    }

    public Guid OrderId { get; init; }

    public string RefundReference { get; init; }
}

/// <summary>From the Kitchen: <c>tiffin.kitchen.order-accepted</c>, version 1.</summary>
public sealed record KitchenAccepted : IntegrationEvent
{
    public const string Name = "tiffin.kitchen.order-accepted";

    public KitchenAccepted(Guid eventId, Guid orderId, int readyInMinutes, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        ReadyInMinutes = readyInMinutes;
    }

    public Guid OrderId { get; init; }

    public int ReadyInMinutes { get; init; }
}

/// <summary>From the Kitchen: <c>tiffin.kitchen.order-rejected</c>, version 1.</summary>
public sealed record KitchenRejected : IntegrationEvent
{
    public const string Name = "tiffin.kitchen.order-rejected";

    public KitchenRejected(Guid eventId, Guid orderId, string reason, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        Reason = reason;
    }

    public Guid OrderId { get; init; }

    public string Reason { get; init; }
}

/// <summary>From Dispatch: <c>tiffin.dispatch.courier-assigned</c>, version 1.</summary>
public sealed record CourierAssigned : IntegrationEvent
{
    public const string Name = "tiffin.dispatch.courier-assigned";

    public CourierAssigned(Guid eventId, Guid orderId, string courierId, string courierName, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        CourierId = courierId;
        CourierName = courierName;
    }

    public Guid OrderId { get; init; }

    public string CourierId { get; init; }

    public string CourierName { get; init; }
}

/// <summary>From Dispatch: <c>tiffin.dispatch.courier-unavailable</c>, version 1.</summary>
public sealed record CourierUnavailable : IntegrationEvent
{
    public const string Name = "tiffin.dispatch.courier-unavailable";

    public CourierUnavailable(Guid eventId, Guid orderId, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
    }

    public Guid OrderId { get; init; }
}

/// <summary>From Dispatch, on the event stream: <c>tiffin.dispatch.delivery-completed</c>, version 1.</summary>
public sealed record DeliveryCompleted : IntegrationEvent
{
    public const string Name = "tiffin.dispatch.delivery-completed";

    public DeliveryCompleted(Guid eventId, Guid orderId, string courierId, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        CourierId = courierId;
    }

    public Guid OrderId { get; init; }

    public string CourierId { get; init; }
}
