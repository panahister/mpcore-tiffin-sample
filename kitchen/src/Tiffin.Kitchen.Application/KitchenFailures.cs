using MPCore.Application.Results;
using Tiffin.Kitchen.Domain.Rules;

namespace Tiffin.Kitchen.Application;

/// <summary>The failures a handler returns as values: expected outcomes, not broken rules.</summary>
public static class KitchenFailures
{
    public const string Domain = KitchenRule.Domain;

    /// <summary>Also the answer for a ticket of another city, or of a restaurant the caller does not manage.</summary>
    public static FailureDescriptor TicketNotFound() => new(
        new ErrorIdentity(Domain, "TICKET_NOT_FOUND"), ErrorCategory.NotFound, new FailureMessageDescriptor("kitchen.ticket_not_found"));

    public static FailureDescriptor ManagerRequired() => new(
        new ErrorIdentity(Domain, "MANAGER_REQUIRED"), ErrorCategory.Forbidden, new FailureMessageDescriptor("kitchen.manager_required"));

    public static FailureDescriptor StatusUnknown() => new(
        new ErrorIdentity(Domain, "TICKET_INVALID"), ErrorCategory.Validation, new FailureMessageDescriptor("kitchen.status_unknown"),
        RetryDirective.Never,
        [new ValidationFailureDetail([new FieldViolation("status", "UNKNOWN", new FailureMessageDescriptor("kitchen.status_unknown"))])]);
}
