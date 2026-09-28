namespace Tiffin.Tracking.Api.Hosting;

/// <summary>The Kafka topics this host reads: what Dispatch says happened to a delivery.</summary>
public static class TrackingTopics
{
    public const string DeliveryAssigned = "tiffin.dispatch.delivery-assigned.v1";
    public const string DeliveryCompleted = "tiffin.dispatch.delivery-completed.v1";

    /// <summary>The group this service reads the stream as: every instance of it shares the work.</summary>
    public const string ConsumerGroup = "tiffin-tracking";
}
