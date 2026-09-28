using MPCore.Domain.Events;

namespace Tiffin.Notifications.Application.Contracts;

// What Ordering says happened to an order, as this service reads it from the event stream. The reader's
// copy of each contract, with only what a notification needs: the two services share no assembly.

/// <summary><c>tiffin.ordering.order-placed</c>, version 1.</summary>
public sealed record OrderPlaced : IntegrationEvent
{
    public const string Name = "tiffin.ordering.order-placed";

    public OrderPlaced(
        Guid eventId, Guid orderId, string orderNumber, string city, string customerId, string restaurantName, decimal total, string currency,
        DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        City = city;
        CustomerId = customerId;
        RestaurantName = restaurantName;
        Total = total;
        Currency = currency;
    }

    public Guid OrderId { get; init; }

    public string OrderNumber { get; init; }

    public string City { get; init; }

    public string CustomerId { get; init; }

    public string RestaurantName { get; init; }

    public decimal Total { get; init; }

    public string Currency { get; init; }
}

/// <summary><c>tiffin.ordering.order-cancelled</c>, version 1.</summary>
public sealed record OrderCancelled : IntegrationEvent
{
    public const string Name = "tiffin.ordering.order-cancelled";

    public OrderCancelled(Guid eventId, Guid orderId, string orderNumber, string city, string customerId, string reason, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        City = city;
        CustomerId = customerId;
        Reason = reason;
    }

    public Guid OrderId { get; init; }

    public string OrderNumber { get; init; }

    public string City { get; init; }

    public string CustomerId { get; init; }

    public string Reason { get; init; }
}

/// <summary><c>tiffin.ordering.order-out-for-delivery</c>, version 1.</summary>
public sealed record OrderOutForDelivery : IntegrationEvent
{
    public const string Name = "tiffin.ordering.order-out-for-delivery";

    public OrderOutForDelivery(
        Guid eventId, Guid orderId, string orderNumber, string city, string customerId, string courierName, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        City = city;
        CustomerId = customerId;
        CourierName = courierName;
    }

    public Guid OrderId { get; init; }

    public string OrderNumber { get; init; }

    public string City { get; init; }

    public string CustomerId { get; init; }

    public string CourierName { get; init; }
}

/// <summary><c>tiffin.ordering.order-delivered</c>, version 1.</summary>
public sealed record OrderDelivered : IntegrationEvent
{
    public const string Name = "tiffin.ordering.order-delivered";

    public OrderDelivered(Guid eventId, Guid orderId, string orderNumber, string city, string customerId, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        City = city;
        CustomerId = customerId;
    }

    public Guid OrderId { get; init; }

    public string OrderNumber { get; init; }

    public string City { get; init; }

    public string CustomerId { get; init; }
}
