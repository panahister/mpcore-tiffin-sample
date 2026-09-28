namespace Tiffin.Kitchen.Api.Hosting;

/// <summary>The RabbitMQ queues this host listens on and answers to.</summary>
/// <remarks>
/// One queue per contract, named after the service that reads it and the contract: a <i>Point-to-Point
/// Channel</i> (Gregor Hohpe and Bobby Woolf, <i>Enterprise Integration Patterns</i>). The names are the
/// contract with the Ordering service, which declares the same names in its own code.
/// </remarks>
public static class KitchenQueues
{
    public const string PreparationRequested = "tiffin.kitchen.preparation-requested.v1";
    public const string PreparationCancelled = "tiffin.kitchen.preparation-cancelled.v1";

    public const string OrderAccepted = "tiffin.ordering.kitchen-accepted.v1";
    public const string OrderRejected = "tiffin.ordering.kitchen-rejected.v1";
}

/// <summary>The Kafka topics this host reads.</summary>
public static class KitchenTopics
{
    /// <summary><c>tiffin.restaurants.restaurant-registered</c> v1, from Restaurants.</summary>
    public const string RestaurantRegistered = "tiffin.restaurants.restaurant-registered.v1";

    /// <summary>The group this service reads the stream as: every instance of it shares the work.</summary>
    public const string ConsumerGroup = "tiffin-kitchen";
}
