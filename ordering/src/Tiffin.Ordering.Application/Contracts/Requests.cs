using MPCore.Domain.Events;

namespace Tiffin.Ordering.Application.Contracts;

// What the order asks of the other services. Each is sent on a queue of its own, to one reader, with the
// change of the order that caused it (the transactional outbox). The services share no assembly: the
// reader declares the same contract in its own code, and the two agree on the name, the version and the
// JSON. This is the writer's copy.

/// <summary>To Payments: charge the card behind the payment intent.</summary>
public sealed record PaymentRequested : IntegrationEvent
{
    public const string Name = "tiffin.ordering.payment-requested";

    public PaymentRequested(Guid orderId, string orderNumber, Guid paymentIntentId, decimal amount, string currency, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        PaymentIntentId = paymentIntentId;
        Amount = amount;
        Currency = currency;
    }

    public Guid OrderId { get; init; }

    public string OrderNumber { get; init; }

    public Guid PaymentIntentId { get; init; }

    public decimal Amount { get; init; }

    public string Currency { get; init; }
}

/// <summary>To Payments: give back what the order was charged, or make sure it is never charged.</summary>
public sealed record RefundRequested : IntegrationEvent
{
    public const string Name = "tiffin.ordering.refund-requested";

    public RefundRequested(Guid orderId, string orderNumber, string reason, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        Reason = reason;
    }

    public Guid OrderId { get; init; }

    public string OrderNumber { get; init; }

    public string Reason { get; init; }
}

/// <summary>To the Kitchen: a paid order waits for the restaurant's word.</summary>
public sealed record PreparationRequested : IntegrationEvent
{
    public const string Name = "tiffin.ordering.preparation-requested";

    public PreparationRequested(
        Guid orderId, string orderNumber, Guid restaurantId, string restaurantName, IReadOnlyList<PreparationLine> lines, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        RestaurantId = restaurantId;
        RestaurantName = restaurantName;
        Lines = lines;
    }

    public Guid OrderId { get; init; }

    public string OrderNumber { get; init; }

    public Guid RestaurantId { get; init; }

    public string RestaurantName { get; init; }

    public IReadOnlyList<PreparationLine> Lines { get; init; }
}

public sealed record PreparationLine(string Code, string Name, int Quantity);

/// <summary>To the Kitchen: stop, the order was cancelled.</summary>
public sealed record PreparationCancelled : IntegrationEvent
{
    public const string Name = "tiffin.ordering.preparation-cancelled";

    public PreparationCancelled(Guid orderId, string orderNumber, string reason, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        Reason = reason;
    }

    public Guid OrderId { get; init; }

    public string OrderNumber { get; init; }

    public string Reason { get; init; }
}

/// <summary>To Dispatch: the restaurant cooks, somebody has to carry.</summary>
public sealed record CourierRequested : IntegrationEvent
{
    public const string Name = "tiffin.ordering.courier-requested";

    public CourierRequested(
        Guid orderId, string orderNumber, string customerId, string restaurantName, CourierDestination deliverTo, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        CustomerId = customerId;
        RestaurantName = restaurantName;
        DeliverTo = deliverTo;
    }

    public Guid OrderId { get; init; }

    public string OrderNumber { get; init; }

    public string CustomerId { get; init; }

    public string RestaurantName { get; init; }

    public CourierDestination DeliverTo { get; init; }

    /// <summary>What a log may show of this message: which order, never the person or the door.</summary>
    public override string ToString() => $"{nameof(CourierRequested)} {{ OrderNumber = {OrderNumber} }}";
}

public sealed record CourierDestination(string Recipient, string Phone, string District, string Line)
{
    public override string ToString() => $"{nameof(CourierDestination)} {{ District = {District} }}";
}
