namespace Tiffin.Notifications.Api.Hosting;

/// <summary>The Kafka topics this host reads: what Ordering says happened to an order.</summary>
public static class NotificationsTopics
{
    public const string OrderPlaced = "tiffin.ordering.order-placed.v1";
    public const string OrderCancelled = "tiffin.ordering.order-cancelled.v1";
    public const string OrderOutForDelivery = "tiffin.ordering.order-out-for-delivery.v1";
    public const string OrderDelivered = "tiffin.ordering.order-delivered.v1";

    /// <summary>The group this service reads the stream as: every instance of it shares the work.</summary>
    public const string ConsumerGroup = "tiffin-notifications";
}
