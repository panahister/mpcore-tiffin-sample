using MPCore.Domain.Events;

namespace Tiffin.Restaurants.Domain.Events;

/// <summary>A restaurant exists: in which city, under which name, managed by whom.</summary>
/// <remarks>
/// Written to the event stream for whoever needs to know a restaurant without asking for it. The Kitchen
/// keeps a copy, so that it can tell whether a manager may decide an order while this service is down:
/// Martin Fowler's <i>event-carried state transfer</i>. A copy is as old as the last event, which is fine
/// for a fact that changes once a year and wrong for a price, which is why a price is asked for.
/// </remarks>
public sealed record RestaurantRegistered : IntegrationEvent
{
    public const string Name = "tiffin.restaurants.restaurant-registered";

    public RestaurantRegistered(Guid restaurantId, string city, string restaurantName, string managerId, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
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
