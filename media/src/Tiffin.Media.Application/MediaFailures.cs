using MPCore.Application.Results;
using Tiffin.Media.Domain.Rules;

namespace Tiffin.Media.Application;

/// <summary>The failures a handler returns as values: expected outcomes, not broken rules.</summary>
public static class MediaFailures
{
    public const string Domain = MediaRule.Domain;

    public static FailureDescriptor SignInRequired() => new(
        new ErrorIdentity(Domain, "SIGN_IN_REQUIRED"), ErrorCategory.Forbidden, new FailureMessageDescriptor("media.sign_in_required"));

    /// <summary>Also the answer for a file of another city, and for one that was deleted.</summary>
    public static FailureDescriptor FileNotFound() => new(
        new ErrorIdentity(Domain, "FILE_NOT_FOUND"), ErrorCategory.NotFound, new FailureMessageDescriptor("media.file_not_found"));

    public static FailureDescriptor NotTheOwner() => new(
        new ErrorIdentity(Domain, "NOT_THE_OWNER"), ErrorCategory.Forbidden, new FailureMessageDescriptor("media.not_the_owner"));

    public static FailureDescriptor NothingArrived() => new(
        new ErrorIdentity(Domain, "NOTHING_ARRIVED"), ErrorCategory.Precondition, new FailureMessageDescriptor("media.nothing_arrived"));

    public static FailureDescriptor UploadWindowClosed() => new(
        new ErrorIdentity(Domain, "UPLOAD_WINDOW_CLOSED"), ErrorCategory.Precondition, new FailureMessageDescriptor("media.upload_window_closed"));

    public static FailureDescriptor StoreUnavailable() => new(
        new ErrorIdentity(Domain, "STORE_UNAVAILABLE"), ErrorCategory.DependencyUnavailable,
        new FailureMessageDescriptor("media.store_unavailable"), RetryDirective.After(TimeSpan.FromSeconds(3)));
}
