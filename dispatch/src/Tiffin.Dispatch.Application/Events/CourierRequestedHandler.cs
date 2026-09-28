using Microsoft.Extensions.Logging;
using MPCore.Application.Time;
using MPCore.Messaging.Abstractions;
using MPCore.Persistence.Abstractions;
using MPCore.Tenancy;
using Tiffin.Dispatch.Application.Contracts;
using Tiffin.Dispatch.Application.Ports;
using Tiffin.Dispatch.Domain;
using Tiffin.Dispatch.Domain.Events;

namespace Tiffin.Dispatch.Application.Events;

/// <summary>An order needs somebody to carry it. Arrives from RabbitMQ.</summary>
/// <remarks>
/// <para>
/// The courier who has waited longest is asked first. When nobody is free the order is told so at once:
/// it is cancelled and paid back, which is better than food that waits for a courier who may not come.
/// </para>
/// <para>
/// <b>Two orders, one courier.</b> Both handlers read the same free courier. The first save wins; the
/// second fails on the courier's row version, and the host's error policy tries its message again, with a
/// pause. The second attempt reads the roster anew and finds the next courier, or nobody.
/// </para>
/// <para>
/// A repeated request finds its delivery and repeats the answer. The handler works for the city of the
/// message that arrived: MP Core opens it for the whole execution.
/// </para>
/// </remarks>
public static class CourierRequestedHandler
{
    public static async Task Handle(
        CourierRequested message, ICourierRepository couriers, IDeliveryRepository deliveries, ITenantContext tenant, IMessagePublisher publisher,
        IUnitOfWork unitOfWork, IClock clock, ILogger<CourierRequested> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(couriers);
        ArgumentNullException.ThrowIfNull(deliveries);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(publisher);

        var city = tenant.TenantId
            ?? throw new InvalidOperationException($"The request for order {message.OrderId} names no city; every message of the platform carries its tenant.");
        var now = clock.UtcNow;

        var existing = await deliveries.GetAsync(message.OrderId, city, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await publisher.PublishAsync(new CourierAssigned(existing.OrderId, existing.CourierId, existing.CourierName, now), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var courier = await couriers.LongestFreeAsync(city, cancellationToken).ConfigureAwait(false);
        if (courier is null)
        {
            logger.LogWarning("Nobody is free to carry order {OrderNumber} in {City}", message.OrderNumber, city);
            await publisher.PublishAsync(new CourierUnavailable(message.OrderId, now), cancellationToken).ConfigureAwait(false);
            return;
        }

        var to = message.DeliverTo;
        deliveries.Add(Delivery.Assign(
            message.OrderId, message.OrderNumber, message.CustomerId, courier, message.RestaurantName,
            new Destination(to.Recipient, to.Phone, to.District, to.Line), now));
        logger.LogInformation("Order {OrderNumber} goes with {Courier}", message.OrderNumber, courier.Name);
    }
}
