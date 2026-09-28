using MPCore.Domain.Events;

namespace Tiffin.Kitchen.Domain.Events;

// The answers of this service. Each is sent on a queue of its own to the Ordering service, which declares
// the same contract in its own code.

/// <summary>The restaurant cooks.</summary>
public sealed record OrderAccepted : IntegrationEvent
{
    public const string Name = "tiffin.kitchen.order-accepted";

    public OrderAccepted(Guid orderId, int readyInMinutes, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        ReadyInMinutes = readyInMinutes;
    }

    public Guid OrderId { get; init; }

    public int ReadyInMinutes { get; init; }
}

/// <summary>The restaurant will not cook.</summary>
public sealed record OrderRejected : IntegrationEvent
{
    public const string Name = "tiffin.kitchen.order-rejected";

    public OrderRejected(Guid orderId, string reason, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        OrderId = orderId;
        Reason = reason;
    }

    public Guid OrderId { get; init; }

    public string Reason { get; init; }
}
