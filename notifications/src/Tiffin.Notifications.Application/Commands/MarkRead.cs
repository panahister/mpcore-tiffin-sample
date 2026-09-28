using MPCore.Application.Messaging;
using MPCore.Application.Results;
using MPCore.Application.Time;
using MPCore.Persistence.Abstractions;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Notifications.Application.Ports;
using Tiffin.Notifications.Application.Views;

namespace Tiffin.Notifications.Application.Commands;

/// <summary>Whoever a notification is for has read it.</summary>
public sealed record MarkRead(Guid NotificationId) : ICommand<Result<NotificationView>>;

public static class MarkReadHandler
{
    public static async Task<Result<NotificationView>> Handle(
        MarkRead command, ICurrentActorAccessor actor, ITenantContext tenant, INotificationRepository notifications, IUnitOfWork unitOfWork,
        IClock clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(notifications);

        if (actor.Current.SubjectId is not { } reader || tenant.TenantId is not { } city)
        {
            return Result<NotificationView>.FromFailure(NotificationFailures.SignInRequired());
        }

        var notification = await notifications.GetAsync(command.NotificationId, city, cancellationToken).ConfigureAwait(false);
        if (notification is null || !notification.IsFor(reader))
        {
            return Result<NotificationView>.FromFailure(NotificationFailures.NotificationNotFound());
        }

        notification.MarkRead(clock.UtcNow);
        return Result<NotificationView>.Success(NotificationViews.Of(notification));
    }
}
