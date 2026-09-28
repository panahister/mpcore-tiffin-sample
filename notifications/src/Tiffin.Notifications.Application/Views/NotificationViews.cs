using Tiffin.Notifications.Domain;

namespace Tiffin.Notifications.Application.Views;

/// <summary>
/// A notification as it is kept: a key and its values. <see cref="Text"/> is empty when it leaves the
/// application layer; the transport fills it in the language of whoever asked.
/// </summary>
public sealed record NotificationView(
    Guid NotificationId, Guid OrderId, string MessageKey, IReadOnlyDictionary<string, string> Arguments, DateTimeOffset OccurredOnUtc,
    DateTimeOffset? ReadOnUtc)
{
    public string? Text { get; init; }
}

public static class NotificationViews
{
    public static NotificationView Of(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return new NotificationView(
            notification.Id, notification.OrderId, notification.MessageKey, new Dictionary<string, string>(notification.Arguments),
            notification.OccurredOnUtc, notification.ReadOnUtc);
    }
}
