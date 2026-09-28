using MPCore.Application.Messaging;
using MPCore.Application.Querying;
using MPCore.Application.Results;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Notifications.Application.Ports;
using Tiffin.Notifications.Application.Views;

namespace Tiffin.Notifications.Application.Queries;

/// <summary>The caller's own notifications, newest first.</summary>
public sealed record ListMyNotifications(bool UnreadOnly = false, int Page = 1, int Size = PageRequest.DefaultSize)
    : IQuery<Result<Page<NotificationView>>>;

public static class ListMyNotificationsHandler
{
    public static async Task<Result<Page<NotificationView>>> Handle(
        ListMyNotifications query, ICurrentActorAccessor actor, ITenantContext tenant, INotificationReadModel notifications,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(notifications);

        if (actor.Current.SubjectId is not { } reader || tenant.TenantId is not { } city)
        {
            return Result<Page<NotificationView>>.FromFailure(NotificationFailures.SignInRequired());
        }

        return Result<Page<NotificationView>>.Success(
            await notifications.ListForAsync(reader, city, query.UnreadOnly, new PageRequest(query.Page, query.Size), cancellationToken).ConfigureAwait(false));
    }
}
