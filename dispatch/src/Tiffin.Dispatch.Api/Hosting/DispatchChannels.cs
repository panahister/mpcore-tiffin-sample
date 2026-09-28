namespace Tiffin.Dispatch.Api.Hosting;

/// <summary>The RabbitMQ queues this host listens on and answers to.</summary>
/// <remarks>
/// One queue per contract, named after the service that reads it and the contract: a <i>Point-to-Point
/// Channel</i> (Gregor Hohpe and Bobby Woolf, <i>Enterprise Integration Patterns</i>). The names are the
/// contract with the Ordering service, which declares the same names in its own code.
/// </remarks>
public static class DispatchQueues
{
    public const string CourierRequested = "tiffin.dispatch.courier-requested.v1";

    public const string CourierAssigned = "tiffin.ordering.courier-assigned.v1";
    public const string CourierUnavailable = "tiffin.ordering.courier-unavailable.v1";
}

/// <summary>The Kafka topics this host writes: what happened to a delivery, for whoever wants to know.</summary>
public static class DispatchTopics
{
    public const string DeliveryAssigned = "tiffin.dispatch.delivery-assigned.v1";
    public const string DeliveryCompleted = "tiffin.dispatch.delivery-completed.v1";
}
