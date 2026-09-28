using Tiffin.Tracking.Domain;

namespace Tiffin.Tracking.Application.Views;

public sealed record PositionView(double Latitude, double Longitude, DateTimeOffset RecordedOnUtc)
{
    public override string ToString() => $"{nameof(PositionView)} {{ RecordedOnUtc = {RecordedOnUtc:O} }}";
}

public sealed record TrackingView(
    Guid OrderId, string OrderNumber, string Status, PositionView? LastSeen, int PositionCount, DateTimeOffset BeganOnUtc,
    DateTimeOffset? ArrivedOnUtc, IReadOnlyList<PositionView> Trail)
{
    public override string ToString() => $"{nameof(TrackingView)} {{ OrderNumber = {OrderNumber}, Status = {Status} }}";
}

public static class TrackingViews
{
    public static TrackingView Of(TrackedDelivery delivery, IReadOnlyList<PositionView> trail)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var last = delivery is { LastLatitude: { } latitude, LastLongitude: { } longitude, LastSeenOnUtc: { } seen }
            ? new PositionView(latitude, longitude, seen)
            : null;
        return new TrackingView(
            delivery.OrderId, delivery.OrderNumber, delivery.Status.ToString(), last, delivery.PositionCount, delivery.BeganOnUtc,
            delivery.ArrivedOnUtc, trail);
    }
}
