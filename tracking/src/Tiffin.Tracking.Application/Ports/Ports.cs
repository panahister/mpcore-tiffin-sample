using Tiffin.Tracking.Application.Views;
using Tiffin.Tracking.Domain;

namespace Tiffin.Tracking.Application.Ports;

/// <summary>The deliveries of one city, and the log of where they were.</summary>
public interface ITrackingRepository
{
    Task<TrackedDelivery?> GetAsync(Guid orderId, string city, CancellationToken cancellationToken);

    void Add(TrackedDelivery delivery);

    /// <summary>Adds a row to the time series. It commits with the delivery it belongs to.</summary>
    void Log(Position position);
}

/// <summary>The read side. Nothing here is tracked, and nothing is changed.</summary>
public interface ITrackingReadModel
{
    Task<TrackedDelivery?> FindAsync(Guid orderId, string city, CancellationToken cancellationToken);

    /// <summary>The last positions of a delivery, newest first.</summary>
    Task<IReadOnlyList<PositionView>> TrailAsync(Guid orderId, string city, int points, CancellationToken cancellationToken);
}
