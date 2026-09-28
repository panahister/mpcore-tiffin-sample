using MPCore.Application.Results;
using Tiffin.Dispatch.Domain.Rules;

namespace Tiffin.Dispatch.Application;

/// <summary>The failures a handler returns as values: expected outcomes, not broken rules.</summary>
public static class DispatchFailures
{
    public const string Domain = DispatchRule.Domain;

    public static FailureDescriptor CourierRequired() => new(
        new ErrorIdentity(Domain, "COURIER_REQUIRED"), ErrorCategory.Forbidden, new FailureMessageDescriptor("dispatch.courier_required"));

    public static FailureDescriptor NotOnTheRoster() => new(
        new ErrorIdentity(Domain, "NOT_ON_THE_ROSTER"), ErrorCategory.NotFound, new FailureMessageDescriptor("dispatch.not_on_the_roster"));

    /// <summary>Also the answer for a delivery of another city.</summary>
    public static FailureDescriptor DeliveryNotFound() => new(
        new ErrorIdentity(Domain, "DELIVERY_NOT_FOUND"), ErrorCategory.NotFound, new FailureMessageDescriptor("dispatch.delivery_not_found"));

    public static FailureDescriptor OrderIdInvalid() => new(
        new ErrorIdentity(Domain, "DELIVERY_INVALID"), ErrorCategory.Validation, new FailureMessageDescriptor("dispatch.order_id_invalid"),
        RetryDirective.Never,
        [new ValidationFailureDetail([new FieldViolation("order_id", "NOT_A_UUID", new FailureMessageDescriptor("dispatch.order_id_invalid"))])]);
}
