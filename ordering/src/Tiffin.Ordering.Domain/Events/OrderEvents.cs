using MPCore.Domain.Events;

namespace Tiffin.Ordering.Domain.Events;

/// <summary>
/// What every event of an order's life says: which order, and in which city. Kafka keeps the events of one
/// order in order, because the order is the partition key.
/// </summary>
public interface IOrderEvent : IIntegrationEvent
{
    Guid OrderId { get; }

    string City { get; }
}

/// <summary>An order exists. Its charge has been asked for; nothing else has happened yet.</summary>
public sealed record OrderPlaced : IntegrationEvent, IOrderEvent
{
    public const string Name = "tiffin.ordering.order-placed";

    public OrderPlaced(
        Guid orderId, string orderNumber, string city, string customerId, Guid restaurantId, string restaurantName,
        decimal total, string currency, int itemCount, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        City = city;
        CustomerId = customerId;
        RestaurantId = restaurantId;
        RestaurantName = restaurantName;
        Total = total;
        Currency = currency;
        ItemCount = itemCount;
    }

    public Guid OrderId { get; init; }

    public string OrderNumber { get; init; }

    public string City { get; init; }

    public string CustomerId { get; init; }

    public Guid RestaurantId { get; init; }

    public string RestaurantName { get; init; }

    public decimal Total { get; init; }

    public string Currency { get; init; }

    public int ItemCount { get; init; }
}

/// <summary>The order will not arrive. <see cref="Reason"/> is one of <see cref="CancellationReasons"/>.</summary>
public sealed record OrderCancelled : IntegrationEvent, IOrderEvent
{
    public const string Name = "tiffin.ordering.order-cancelled";

    public OrderCancelled(Guid orderId, string orderNumber, string city, string customerId, string reason, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
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

/// <summary>A courier carries the order.</summary>
public sealed record OrderOutForDelivery : IntegrationEvent, IOrderEvent
{
    public const string Name = "tiffin.ordering.order-out-for-delivery";

    public OrderOutForDelivery(
        Guid orderId, string orderNumber, string city, string customerId, string courierId, string courierName, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        City = city;
        CustomerId = customerId;
        CourierId = courierId;
        CourierName = courierName;
    }

    public Guid OrderId { get; init; }

    public string OrderNumber { get; init; }

    public string City { get; init; }

    public string CustomerId { get; init; }

    public string CourierId { get; init; }

    public string CourierName { get; init; }
}

/// <summary>The customer has the order.</summary>
public sealed record OrderDelivered : IntegrationEvent, IOrderEvent
{
    public const string Name = "tiffin.ordering.order-delivered";

    public OrderDelivered(Guid orderId, string orderNumber, string city, string customerId, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
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
