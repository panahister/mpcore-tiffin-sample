using MPCore.Domain.Model;
using Tiffin.Kitchen.Domain.Events;
using Tiffin.Kitchen.Domain.Rules;

namespace Tiffin.Kitchen.Domain;

public enum TicketStatus
{
    /// <summary>The restaurant has not said anything yet.</summary>
    Pending = 0,

    Accepted = 1,

    Rejected = 2,

    /// <summary>The order was cancelled; the restaurant is told to stop.</summary>
    Cancelled = 3
}

public sealed record TicketLine(string Code, string Name, int Quantity);

/// <summary>One paid order, as the restaurant sees it: what to cook, and what the restaurant said.</summary>
/// <remarks>
/// <para>
/// <b>One ticket per order, keyed by the order.</b> That single choice makes the kitchen idempotent: the
/// same order announced twice finds its ticket and adds nothing. Gregor Hohpe and Bobby Woolf call this the
/// <i>Idempotent Receiver</i> (<i>Enterprise Integration Patterns</i>).
/// </para>
/// <para>
/// The ticket holds a copy of what the order told it. It never asks the Ordering service again, so the
/// restaurant keeps working while that service is down.
/// </para>
/// </remarks>
public sealed class Ticket : AggregateRoot<Guid>
{
    private readonly List<TicketLine> lines = [];

    private Ticket()
    {
        City = string.Empty;
        OrderNumber = string.Empty;
        RestaurantName = string.Empty;
    }

    private Ticket(Guid orderId, string city, string orderNumber, Guid restaurantId, string restaurantName, IEnumerable<TicketLine> ticketLines, DateTimeOffset now)
        : base(orderId)
    {
        City = city;
        OrderNumber = orderNumber;
        RestaurantId = restaurantId;
        RestaurantName = restaurantName;
        lines.AddRange(ticketLines);
        Status = TicketStatus.Pending;
        ReceivedOnUtc = now;
    }

    public Guid OrderId => Id;

    /// <summary>The tenant.</summary>
    public string City { get; private set; }

    public string OrderNumber { get; private set; }

    public Guid RestaurantId { get; private set; }

    public string RestaurantName { get; private set; }

    public TicketStatus Status { get; private set; }

    public int? ReadyInMinutes { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset ReceivedOnUtc { get; private set; }

    public DateTimeOffset? DecidedOnUtc { get; private set; }

    public IReadOnlyList<TicketLine> Lines => lines;

    public static Ticket Receive(
        Guid orderId, string city, string orderNumber, Guid restaurantId, string restaurantName, IReadOnlyList<TicketLine> ticketLines, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);
        ArgumentNullException.ThrowIfNull(ticketLines);
        CheckRule(new ATicketHasLines(ticketLines.Count));

        return new Ticket(orderId, city, orderNumber, restaurantId, restaurantName, ticketLines, now);
    }

    public void Accept(int readyInMinutes, DateTimeOffset now)
    {
        CheckRule(new OnlyAPendingTicketIsDecided(this));
        CheckRule(new APromiseIsWithinReason(readyInMinutes));
        Status = TicketStatus.Accepted;
        ReadyInMinutes = readyInMinutes;
        DecidedOnUtc = now;
        Raise(new OrderAccepted(Id, readyInMinutes, now));
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        CheckRule(new OnlyAPendingTicketIsDecided(this));
        Status = TicketStatus.Rejected;
        Reason = reason.Trim();
        DecidedOnUtc = now;
        Raise(new OrderRejected(Id, Reason, now));
    }

    /// <summary>The order was cancelled. A ticket that has already ended stays as it ended.</summary>
    public bool Cancel(string reason, DateTimeOffset now)
    {
        if (Status is TicketStatus.Rejected or TicketStatus.Cancelled)
        {
            return false;
        }

        Status = TicketStatus.Cancelled;
        Reason = reason;
        DecidedOnUtc = now;
        return true;
    }
}

/// <summary>What the Kitchen knows of a restaurant, from the event stream: enough to tell who may decide its orders.</summary>
public sealed class KnownRestaurant : Entity<Guid>
{
    private KnownRestaurant()
    {
        City = string.Empty;
        Name = string.Empty;
        ManagerId = string.Empty;
    }

    public KnownRestaurant(Guid restaurantId, string city, string name, string managerId)
        : base(restaurantId)
    {
        City = city;
        Name = name;
        ManagerId = managerId;
    }

    public string City { get; private set; }

    public string Name { get; private set; }

    public string ManagerId { get; private set; }

    public void Learn(string name, string managerId)
    {
        Name = name;
        ManagerId = managerId;
    }
}
