using MPCore.Domain.Events;

namespace Tiffin.Dispatch.Application.Contracts;

/// <summary>
/// From Ordering, on a queue: <c>tiffin.ordering.courier-requested</c>, version 1. The reader's copy of the
/// contract: the two services share no assembly.
/// </summary>
public sealed record CourierRequested : IntegrationEvent
{
    public const string Name = "tiffin.ordering.courier-requested";

    public CourierRequested(
        Guid eventId, Guid orderId, string orderNumber, string customerId, string restaurantName, CourierDestination deliverTo,
        DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
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
