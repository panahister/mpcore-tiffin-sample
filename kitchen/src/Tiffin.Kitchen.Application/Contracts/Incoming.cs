using MPCore.Domain.Events;

namespace Tiffin.Kitchen.Application.Contracts;

// What other services say, as this service reads it. The services share no assembly: this is the reader's
// copy of each contract, and it declares only what the Kitchen uses.

/// <summary>From Ordering, on a queue: <c>tiffin.ordering.preparation-requested</c>, version 1.</summary>
public sealed record PreparationRequested : IntegrationEvent
{
    public const string Name = "tiffin.ordering.preparation-requested";

    public PreparationRequested(
        Guid eventId, Guid orderId, string orderNumber, Guid restaurantId, string restaurantName, IReadOnlyList<PreparationLine> lines,
        DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
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

/// <summary>From Ordering, on a queue: <c>tiffin.ordering.preparation-cancelled</c>, version 1.</summary>
public sealed record PreparationCancelled : IntegrationEvent
{
    public const string Name = "tiffin.ordering.preparation-cancelled";

    public PreparationCancelled(Guid eventId, Guid orderId, string reason, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        OrderId = orderId;
        Reason = reason;
    }

    public Guid OrderId { get; init; }

    public string Reason { get; init; }
}

/// <summary>From Restaurants, on the event stream: <c>tiffin.restaurants.restaurant-registered</c>, version 1.</summary>
public sealed record RestaurantRegistered : IntegrationEvent
{
    public const string Name = "tiffin.restaurants.restaurant-registered";

    public RestaurantRegistered(Guid eventId, Guid restaurantId, string city, string restaurantName, string managerId, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, eventId)
    {
        RestaurantId = restaurantId;
        City = city;
        RestaurantName = restaurantName;
        ManagerId = managerId;
    }

    public Guid RestaurantId { get; init; }

    public string City { get; init; }

    public string RestaurantName { get; init; }

    public string ManagerId { get; init; }
}
