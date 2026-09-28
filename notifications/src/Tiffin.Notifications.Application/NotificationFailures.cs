using MPCore.Application.Results;

namespace Tiffin.Notifications.Application;

/// <summary>The failures a handler returns as values.</summary>
public static class NotificationFailures
{
    public const string Domain = "tiffin.notifications";

    public static FailureDescriptor SignInRequired() => new(
        new ErrorIdentity(Domain, "SIGN_IN_REQUIRED"), ErrorCategory.Forbidden, new FailureMessageDescriptor("notifications.sign_in_required"));

    /// <summary>Also the answer for somebody else's notification, and for one of another city.</summary>
    public static FailureDescriptor NotificationNotFound() => new(
        new ErrorIdentity(Domain, "NOTIFICATION_NOT_FOUND"), ErrorCategory.NotFound, new FailureMessageDescriptor("notifications.not_found"));
}
