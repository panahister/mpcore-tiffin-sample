using MPCore.Application.Results;
using Tiffin.Payments.Domain.Rules;

namespace Tiffin.Payments.Application;

/// <summary>The failures a handler returns as values: expected outcomes, not broken rules.</summary>
public static class PaymentFailures
{
    public const string Domain = PaymentRule.Domain;

    /// <summary>The provider did not answer. Whoever asked may ask again, and the broker does.</summary>
    public static FailureDescriptor ProviderUnavailable() => new(
        new ErrorIdentity(Domain, "PROVIDER_UNAVAILABLE"), ErrorCategory.DependencyUnavailable,
        new FailureMessageDescriptor("payments.provider_unavailable"), RetryDirective.After(TimeSpan.FromSeconds(2)));

    public static FailureDescriptor Invalid(string fieldPath, string ruleCode, string messageKey) => new(
        new ErrorIdentity(Domain, "PAYMENT_INVALID"), ErrorCategory.Validation,
        new FailureMessageDescriptor(messageKey), RetryDirective.Never,
        [new ValidationFailureDetail([new FieldViolation(fieldPath, ruleCode, new FailureMessageDescriptor(messageKey))])]);
}

/// <summary>Decline codes this service gives itself, beside the provider's.</summary>
public static class DeclineCodes
{
    public const string UnknownReference = "UNKNOWN_REFERENCE";
    public const string ReferenceExpired = "REFERENCE_EXPIRED";
    public const string AmountMismatch = "AMOUNT_MISMATCH";
    public const string ProviderUnavailable = "PROVIDER_UNAVAILABLE";
}
