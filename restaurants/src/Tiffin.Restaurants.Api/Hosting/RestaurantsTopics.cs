namespace Tiffin.Restaurants.Api.Hosting;

/// <summary>The Kafka topics this host writes: what happened to a restaurant, for whoever wants to know.</summary>
/// <remarks>
/// A topic is a log that any number of readers replay, each at its own pace: a <i>Publish-Subscribe
/// Channel</i> (Gregor Hohpe and Bobby Woolf, <i>Enterprise Integration Patterns</i>). This service does
/// not know who reads what it writes.
/// </remarks>
public static class RestaurantsTopics
{
    public const string RestaurantRegistered = "tiffin.restaurants.restaurant-registered.v1";
}
