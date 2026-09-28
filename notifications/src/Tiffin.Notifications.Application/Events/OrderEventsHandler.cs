using System.Globalization;
using MPCore.Domain.Events;
using MPCore.Persistence.Abstractions;
using Tiffin.Notifications.Application.Contracts;
using Tiffin.Notifications.Application.Ports;
using Tiffin.Notifications.Domain;

namespace Tiffin.Notifications.Application.Events;

/// <summary>What happened to an order becomes something its customer is told. Arrives from Kafka.</summary>
/// <remarks>
/// <para>
/// This service is a reader of the stream and nothing else. Ordering does not know that it exists, and it
/// was added without a change to any other service: that is what a stream is for.
/// </para>
/// <para>
/// Kafka delivers at least once, and a reader that starts again reads again. MP Core's inbox stops a
/// second delivery of the same event; and a notification is keyed by the event it is about, so the same
/// event read anew finds its notification here.
/// </para>
/// </remarks>
public static class OrderEventsHandler
{
    public static Task Handle(OrderPlaced message, INotificationRepository notifications, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return TellAsync(notifications, message, message.City, message.CustomerId, message.OrderId, "notifications.order_placed", new()
        {
            ["order_number"] = message.OrderNumber,
            ["restaurant"] = message.RestaurantName,
            ["total"] = message.Total.ToString("0.##", CultureInfo.InvariantCulture),
            ["currency"] = message.Currency
        }, cancellationToken);
    }

    public static Task Handle(OrderOutForDelivery message, INotificationRepository notifications, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return TellAsync(notifications, message, message.City, message.CustomerId, message.OrderId, "notifications.order_out_for_delivery", new()
        {
            ["order_number"] = message.OrderNumber,
            ["courier"] = message.CourierName
        }, cancellationToken);
    }

    public static Task Handle(OrderDelivered message, INotificationRepository notifications, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return TellAsync(notifications, message, message.City, message.CustomerId, message.OrderId, "notifications.order_delivered", new()
        {
            ["order_number"] = message.OrderNumber
        }, cancellationToken);
    }

    public static Task Handle(OrderCancelled message, INotificationRepository notifications, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // One text per reason. A reason this service has not heard of yet is told as a cancellation
        // without a reason, which is true and says less.
        var key = Reasons.Contains(message.Reason) ? "notifications.order_cancelled." + message.Reason : "notifications.order_cancelled";
        return TellAsync(notifications, message, message.City, message.CustomerId, message.OrderId, key, new()
        {
            ["order_number"] = message.OrderNumber
        }, cancellationToken);
    }

    /// <summary>The reasons of the <c>order-cancelled</c> contract this service has a text for.</summary>
    private static readonly HashSet<string> Reasons = new(StringComparer.Ordinal)
    {
        "cancelled-by-customer", "payment-declined", "restaurant-refused", "no-courier"
    };

    private static async Task TellAsync(
        INotificationRepository notifications, IIntegrationEvent message, string city, string customerId, Guid orderId, string key,
        Dictionary<string, string> arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        // The event says which city; it is the fact that is recorded.
        if (await notifications.GetAsync(message.EventId, city, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        notifications.Add(Notification.About(message.EventId, city, customerId, key, arguments, orderId, message.OccurredOnUtc));
    }
}
