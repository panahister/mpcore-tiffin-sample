using System.Globalization;
using MPCore.Domain.Model;
using Tiffin.Tracking.Domain.Rules;

namespace Tiffin.Tracking.Domain;

public enum TrackingStatus
{
    UnderWay = 0,
    Arrived = 1
}

/// <summary>One delivery, as the map sees it: who carries it for whom, and where it was seen last.</summary>
/// <remarks>
/// <para>
/// Tracking learns of a delivery from the event stream of Dispatch and never asks: it knows a delivery
/// from the moment it began, and it keeps knowing it while Dispatch is down. Martin Fowler calls the
/// pattern <i>event-carried state transfer</i>.
/// </para>
/// <para>
/// The delivery keeps the last position; every position is kept beside it as a row of a time series
/// (see <see cref="Position"/>). The first answers "where is it now" with one read of one row, the second
/// "which way did it come".
/// </para>
/// </remarks>
public sealed class TrackedDelivery : AggregateRoot<Guid>
{
    private TrackedDelivery()
    {
        City = string.Empty;
        OrderNumber = string.Empty;
        CustomerId = string.Empty;
        CourierId = string.Empty;
    }

    private TrackedDelivery(Guid orderId, string city, string orderNumber, string customerId, string courierId, DateTimeOffset now)
        : base(orderId)
    {
        City = city;
        OrderNumber = orderNumber;
        CustomerId = customerId;
        CourierId = courierId;
        Status = TrackingStatus.UnderWay;
        BeganOnUtc = now;
    }

    public Guid OrderId => Id;

    /// <summary>The tenant.</summary>
    public string City { get; private set; }

    public string OrderNumber { get; private set; }

    public string CustomerId { get; private set; }

    public string CourierId { get; private set; }

    public TrackingStatus Status { get; private set; }

    public double? LastLatitude { get; private set; }

    public double? LastLongitude { get; private set; }

    public DateTimeOffset? LastSeenOnUtc { get; private set; }

    public int PositionCount { get; private set; }

    public DateTimeOffset BeganOnUtc { get; private set; }

    public DateTimeOffset? ArrivedOnUtc { get; private set; }

    public static TrackedDelivery Begin(Guid orderId, string city, string orderNumber, string customerId, string courierId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(courierId);
        return new TrackedDelivery(orderId, city, orderNumber, customerId, courierId, now);
    }

    /// <summary>The courier says where they are. Returns the row of the time series.</summary>
    public Position Report(string courierId, double latitude, double longitude, DateTimeOffset now)
    {
        CheckRule(new OnlyTheCourierOfADeliveryReports(this, courierId));
        CheckRule(new OnlyADeliveryUnderWayIsReported(this));
        CheckRule(new APositionIsOnEarth(latitude, longitude));

        LastLatitude = latitude;
        LastLongitude = longitude;
        LastSeenOnUtc = now;
        PositionCount++;
        return new Position(Id, now, City, courierId, latitude, longitude);
    }

    public void Arrive(DateTimeOffset now)
    {
        if (Status == TrackingStatus.Arrived)
        {
            return;
        }

        Status = TrackingStatus.Arrived;
        ArrivedOnUtc = now;
    }

    /// <summary>Who may look: the customer who waits for it, and the courier who carries it.</summary>
    public bool MayBeSeenBy(string subjectId) =>
        string.Equals(CustomerId, subjectId, StringComparison.Ordinal) || string.Equals(CourierId, subjectId, StringComparison.Ordinal);
}

/// <summary>Where a courier was at one moment. A row of a time series: written once, never changed.</summary>
public sealed class Position
{
    private Position()
    {
        City = string.Empty;
        CourierId = string.Empty;
    }

    internal Position(Guid orderId, DateTimeOffset recordedOnUtc, string city, string courierId, double latitude, double longitude)
    {
        OrderId = orderId;
        RecordedOnUtc = recordedOnUtc;
        City = city;
        CourierId = courierId;
        Latitude = latitude;
        Longitude = longitude;
    }

    public Guid OrderId { get; private set; }

    public DateTimeOffset RecordedOnUtc { get; private set; }

    public string City { get; private set; }

    public string CourierId { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    /// <summary>What a log may show of a position: that there is one. Where somebody was is personal data.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{nameof(Position)} {{ OrderId = {OrderId}, RecordedOnUtc = {RecordedOnUtc:O} }}");
}
