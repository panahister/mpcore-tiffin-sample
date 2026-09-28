using MPCore.Domain.Model;
using Tiffin.Dispatch.Domain.Events;
using Tiffin.Dispatch.Domain.Rules;

namespace Tiffin.Dispatch.Domain;

public enum DeliveryStatus
{
    Assigned = 0,
    Completed = 1
}

/// <summary>One order, as its courier sees it: where from, where to, and whether it was handed over.</summary>
/// <remarks>
/// One delivery per order, keyed by the order: the same request for a courier, delivered twice, finds its
/// delivery and repeats its answer. Gregor Hohpe and Bobby Woolf call this the <i>Idempotent Receiver</i>
/// (<i>Enterprise Integration Patterns</i>).
/// </remarks>
public sealed class Delivery : AggregateRoot<Guid>
{
    private Delivery()
    {
        City = string.Empty;
        OrderNumber = string.Empty;
        CustomerId = string.Empty;
        CourierId = string.Empty;
        CourierName = string.Empty;
        RestaurantName = string.Empty;
        Destination = null!;
    }

    private Delivery(
        Guid orderId, string city, string orderNumber, string customerId, string courierId, string courierName, string restaurantName,
        Destination destination, DateTimeOffset now)
        : base(orderId)
    {
        City = city;
        OrderNumber = orderNumber;
        CustomerId = customerId;
        CourierId = courierId;
        CourierName = courierName;
        RestaurantName = restaurantName;
        Destination = destination;
        Status = DeliveryStatus.Assigned;
        AssignedOnUtc = now;
    }

    public Guid OrderId => Id;

    /// <summary>The tenant.</summary>
    public string City { get; private set; }

    public string OrderNumber { get; private set; }

    public string CustomerId { get; private set; }

    public string CourierId { get; private set; }

    public string CourierName { get; private set; }

    public string RestaurantName { get; private set; }

    public Destination Destination { get; private set; }

    public DeliveryStatus Status { get; private set; }

    public DateTimeOffset AssignedOnUtc { get; private set; }

    public DateTimeOffset? CompletedOnUtc { get; private set; }

    public static Delivery Assign(
        Guid orderId, string orderNumber, string customerId, Courier courier, string restaurantName, Destination destination, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(courier);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);

        courier.Take(orderId);
        var delivery = new Delivery(orderId, courier.City, orderNumber, customerId, courier.Id, courier.Name, restaurantName, destination, now);

        // Two events for two readers: the order, which waits for this answer on its queue, and the stream,
        // where whoever follows deliveries reads that one began.
        delivery.Raise(new CourierAssigned(orderId, courier.Id, courier.Name, now));
        delivery.Raise(new DeliveryAssigned(orderId, orderNumber, courier.City, customerId, courier.Id, now));
        return delivery;
    }

    public void Complete(Courier courier, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(courier);
        CheckRule(new OnlyTheCourierOfADeliveryCompletesIt(this, courier.Id));
        CheckRule(new ADeliveryIsCompletedOnce(this));

        Status = DeliveryStatus.Completed;
        CompletedOnUtc = now;
        courier.HandOver(Id, now);
        Raise(new DeliveryCompleted(Id, OrderNumber, City, CustomerId, CourierId, now));
    }
}

/// <summary>Where the order goes. Equal by value.</summary>
public sealed class Destination : ValueObject
{
    private Destination()
    {
        Recipient = string.Empty;
        Phone = string.Empty;
        District = string.Empty;
        Line = string.Empty;
    }

    public Destination(string recipient, string phone, string district, string line)
    {
        Recipient = recipient;
        Phone = phone;
        District = district;
        Line = line;
    }

    public string Recipient { get; private set; }

    public string Phone { get; private set; }

    public string District { get; private set; }

    public string Line { get; private set; }

    /// <summary>What a log may show of a destination: the district, never the person or the door.</summary>
    public override string ToString() => $"{nameof(Destination)} {{ District = {District} }}";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Recipient;
        yield return Phone;
        yield return District;
        yield return Line;
    }
}
