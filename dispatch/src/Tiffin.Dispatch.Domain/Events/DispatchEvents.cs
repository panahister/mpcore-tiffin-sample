using MPCore.Domain.Events;

namespace Tiffin.Dispatch.Domain.Events;

// The first two are answers, each sent on a queue of its own to the Ordering service. The last two are
// facts, written to the event stream for whoever follows deliveries.

/// <summary>To the order: a courier carries it.</summary>
public sealed record CourierAssigned : IntegrationEvent
{
    public const string Name = "tiffin.dispatch.courier-assigned";

    public CourierAssigned(Guid orderId, string courierId, string courierName, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        CourierId = courierId;
        CourierName = courierName;
    }

    public Guid OrderId { get; init; }

    public string CourierId { get; init; }

    public string CourierName { get; init; }
}

/// <summary>To the order: nobody can carry it.</summary>
public sealed record CourierUnavailable : IntegrationEvent
{
    public const string Name = "tiffin.dispatch.courier-unavailable";

    public CourierUnavailable(Guid orderId, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
    }

    public Guid OrderId { get; init; }
}

/// <summary>What every event of a delivery says: which order. Kafka keeps the events of one order in order.</summary>
public interface IDeliveryEvent : IIntegrationEvent
{
    Guid OrderId { get; }
}

/// <summary>A delivery began.</summary>
public sealed record DeliveryAssigned : IntegrationEvent, IDeliveryEvent
{
    public const string Name = "tiffin.dispatch.delivery-assigned";

    public DeliveryAssigned(Guid orderId, string orderNumber, string city, string customerId, string courierId, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        City = city;
        CustomerId = customerId;
        CourierId = courierId;
    }

    public Guid OrderId { get; init; }

    public string OrderNumber { get; init; }

    public string City { get; init; }

    public string CustomerId { get; init; }

    public string CourierId { get; init; }
}

/// <summary>The courier handed the order over.</summary>
public sealed record DeliveryCompleted : IntegrationEvent, IDeliveryEvent
{
    public const string Name = "tiffin.dispatch.delivery-completed";

    public DeliveryCompleted(Guid orderId, string orderNumber, string city, string customerId, string courierId, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        OrderNumber = orderNumber;
        City = city;
        CustomerId = customerId;
        CourierId = courierId;
    }

    public Guid OrderId { get; init; }

    public string OrderNumber { get; init; }

    public string City { get; init; }

    public string CustomerId { get; init; }

    public string CourierId { get; init; }
}
