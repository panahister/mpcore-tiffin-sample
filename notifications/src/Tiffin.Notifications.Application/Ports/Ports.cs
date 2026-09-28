using MPCore.Application.Querying;
using Tiffin.Notifications.Application.Views;
using Tiffin.Notifications.Domain;

namespace Tiffin.Notifications.Application.Ports;

/// <summary>The notifications of one city.</summary>
public interface INotificationRepository
{
    Task<Notification?> GetAsync(Guid id, string city, CancellationToken cancellationToken);

    void Add(Notification notification);
}

/// <summary>The read side. Nothing here is tracked, and nothing is changed.</summary>
public interface INotificationReadModel
{
    Task<Page<NotificationView>> ListForAsync(string recipientId, string city, bool unreadOnly, PageRequest page, CancellationToken cancellationToken);
}
