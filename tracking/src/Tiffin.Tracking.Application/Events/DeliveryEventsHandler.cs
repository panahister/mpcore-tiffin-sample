using Microsoft.Extensions.Logging;
using MPCore.Persistence.Abstractions;
using Tiffin.Tracking.Application.Contracts;
using Tiffin.Tracking.Application.Ports;
using Tiffin.Tracking.Domain;

namespace Tiffin.Tracking.Application.Events;

/// <summary>What Dispatch says happened to a delivery. Arrives from Kafka.</summary>
/// <remarks>
/// <para>
/// Kafka delivers at least once, and a reader that starts again reads again. MP Core's inbox stops a
/// second delivery of the same event, by its <c>EventId</c>; and a delivery is keyed by its order, so the
/// same delivery announced in a second event finds itself here.
/// </para>
/// <para>
/// Dispatch keys its events by the order, so "assigned" is read before "completed". A "completed" without
/// an "assigned" is a delivery that began before this service read the stream; there is nothing to mark.
/// </para>
/// </remarks>
public static class DeliveryEventsHandler
{
    public static async Task Handle(
        DeliveryAssigned message, ITrackingRepository deliveries, IUnitOfWork unitOfWork, ILogger<DeliveryAssigned> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(deliveries);

        // The event says which city; it is the fact that is recorded.
        if (await deliveries.GetAsync(message.OrderId, message.City, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        deliveries.Add(TrackedDelivery.Begin(
            message.OrderId, message.City, message.OrderNumber, message.CustomerId, message.CourierId, message.OccurredOnUtc));
        logger.LogInformation("Order {OrderNumber} is under way", message.OrderNumber);
    }

    public static async Task Handle(
        DeliveryCompleted message, ITrackingRepository deliveries, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(deliveries);

        var delivery = await deliveries.GetAsync(message.OrderId, message.City, cancellationToken).ConfigureAwait(false);
        delivery?.Arrive(message.OccurredOnUtc);
    }
}
