using MPCore.Application.Results;
using Tiffin.Tracking.Domain.Rules;

namespace Tiffin.Tracking.Application;

/// <summary>The failures a handler returns as values: expected outcomes, not broken rules.</summary>
public static class TrackingFailures
{
    public const string Domain = TrackingRule.Domain;

    public static FailureDescriptor SignInRequired() => new(
        new ErrorIdentity(Domain, "SIGN_IN_REQUIRED"), ErrorCategory.Forbidden, new FailureMessageDescriptor("tracking.sign_in_required"));

    /// <summary>Also the answer for a delivery of another city, and for one the caller neither waits for nor carries.</summary>
    public static FailureDescriptor DeliveryNotFound() => new(
        new ErrorIdentity(Domain, "DELIVERY_NOT_FOUND"), ErrorCategory.NotFound, new FailureMessageDescriptor("tracking.delivery_not_found"));
}
