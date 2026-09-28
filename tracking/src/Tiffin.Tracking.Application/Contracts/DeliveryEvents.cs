using MPCore.Domain.Events;

namespace Tiffin.Tracking.Application.Contracts;

// What Dispatch says happened, as this service reads it from the event stream. The reader's copy of each
// contract: the two services share no assembly.

/// <summary><c>tiffin.dispatch.delivery-assigned</c>, version 1.</summary>
public sealed record DeliveryAssigned : IntegrationEvent
{
    public const string Name = "tiffin.dispatch.delivery-assigned";

    public DeliveryAssigned(Guid eventId, Guid orderId, string orderNumber, string city, string customerId, string courierId, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
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

/// <summary><c>tiffin.dispatch.delivery-completed</c>, version 1.</summary>
public sealed record DeliveryCompleted : IntegrationEvent
{
    public const string Name = "tiffin.dispatch.delivery-completed";

    public DeliveryCompleted(Guid eventId, Guid orderId, string city, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        City = city;
    }

    public Guid OrderId { get; init; }

    public string City { get; init; }
}
