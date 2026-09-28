namespace Tiffin.Ordering.Api.Hosting;

/// <summary>The RabbitMQ queues of the order process: what this service asks, and what it is answered.</summary>
/// <remarks>
/// <para>
/// One queue per contract, named after the service that reads it and the contract. A queue is a work list:
/// each message is taken by one worker, which is what a request wants, and what an answer wants. Gregor
/// Hohpe and Bobby Woolf call it a <i>Point-to-Point Channel</i> (<i>Enterprise Integration Patterns</i>).
/// </para>
/// <para>
/// The names are the contract with the other services, each of which declares the same names in its own code.
/// </para>
/// </remarks>
public static class OrderingQueues
{
    public const string PaymentRequested = "tiffin.payments.payment-requested.v1";
    public const string RefundRequested = "tiffin.payments.refund-requested.v1";
    public const string PreparationRequested = "tiffin.kitchen.preparation-requested.v1";
    public const string PreparationCancelled = "tiffin.kitchen.preparation-cancelled.v1";
    public const string CourierRequested = "tiffin.dispatch.courier-requested.v1";

    public const string PaymentAuthorized = "tiffin.ordering.payment-authorized.v1";
    public const string PaymentDeclined = "tiffin.ordering.payment-declined.v1";
    public const string PaymentRefunded = "tiffin.ordering.payment-refunded.v1";
    public const string KitchenAccepted = "tiffin.ordering.kitchen-accepted.v1";
    public const string KitchenRejected = "tiffin.ordering.kitchen-rejected.v1";
    public const string CourierAssigned = "tiffin.ordering.courier-assigned.v1";
    public const string CourierUnavailable = "tiffin.ordering.courier-unavailable.v1";
}

/// <summary>The Kafka topics: what happened to an order, for whoever wants to know, and what Dispatch says happened.</summary>
/// <remarks>
/// A topic is a log that any number of readers replay, each at its own pace: a <i>Publish-Subscribe
/// Channel</i>. This service does not know who reads what it writes.
/// </remarks>
public static class OrderingTopics
{
    public const string OrderPlaced = "tiffin.ordering.order-placed.v1";
    public const string OrderCancelled = "tiffin.ordering.order-cancelled.v1";
    public const string OrderOutForDelivery = "tiffin.ordering.order-out-for-delivery.v1";
    public const string OrderDelivered = "tiffin.ordering.order-delivered.v1";

    /// <summary><c>tiffin.dispatch.delivery-completed</c> v1, from Dispatch.</summary>
    public const string DeliveryCompleted = "tiffin.dispatch.delivery-completed.v1";

    /// <summary>The group this service reads the stream as: every instance of it shares the work.</summary>
    public const string ConsumerGroup = "tiffin-ordering";
}
