using MPCore.Application.Results;
using Tiffin.Access.Domain.Rules;

namespace Tiffin.Access.Application;

/// <summary>The failures a handler returns as values: expected outcomes, not broken rules.</summary>
public static class AccessFailures
{
    public const string Domain = AccessRule.Domain;

    public static FailureDescriptor AdminRequired() => new(
        new ErrorIdentity(Domain, "ADMIN_REQUIRED"), ErrorCategory.Forbidden, new FailureMessageDescriptor("access.admin_required"));

    public static FailureDescriptor CityRequired() => new(
        new ErrorIdentity(Domain, "ACCESS_INVALID"), ErrorCategory.Validation, new FailureMessageDescriptor("access.city_required"),
        RetryDirective.Never,
        [new ValidationFailureDetail([new FieldViolation("city", "REQUIRED", new FailureMessageDescriptor("access.city_required"))])]);

    /// <summary>Also the answer for a person of another city: what an admin may not see does not exist for them.</summary>
    public static FailureDescriptor PersonNotFound() => new(
        new ErrorIdentity(Domain, "PERSON_NOT_FOUND"), ErrorCategory.NotFound, new FailureMessageDescriptor("access.person_not_found"));

    public static FailureDescriptor GrantNotFound() => new(
        new ErrorIdentity(Domain, "GRANT_NOT_FOUND"), ErrorCategory.NotFound, new FailureMessageDescriptor("access.grant_not_found"));

    /// <summary>The identity provider did not answer. Whoever asked may ask again, and a queued step is tried again.</summary>
    public static FailureDescriptor DirectoryUnavailable() => new(
        new ErrorIdentity(Domain, "DIRECTORY_UNAVAILABLE"), ErrorCategory.DependencyUnavailable,
        new FailureMessageDescriptor("access.directory_unavailable"), RetryDirective.After(TimeSpan.FromSeconds(3)));
}
