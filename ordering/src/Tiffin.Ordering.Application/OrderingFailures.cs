using System.Globalization;
using MPCore.Application.Results;
using Tiffin.Ordering.Domain.Rules;

namespace Tiffin.Ordering.Application;

/// <summary>The failures a handler returns as values: expected outcomes, not broken rules.</summary>
public static class OrderingFailures
{
    public const string Domain = OrderingRule.Domain;

    /// <summary>Also the answer for an order of another city or of another customer.</summary>
    public static FailureDescriptor OrderNotFound() => new(
        new ErrorIdentity(Domain, "ORDER_NOT_FOUND"), ErrorCategory.NotFound, new FailureMessageDescriptor("ordering.order_not_found"));

    public static FailureDescriptor CustomerRequired() => new(
        new ErrorIdentity(Domain, "CUSTOMER_REQUIRED"), ErrorCategory.Forbidden, new FailureMessageDescriptor("ordering.customer_required"));

    /// <summary>Also the answer for a restaurant of another city.</summary>
    public static FailureDescriptor RestaurantNotFound() => new(
        new ErrorIdentity(Domain, "RESTAURANT_NOT_FOUND"), ErrorCategory.NotFound, new FailureMessageDescriptor("ordering.restaurant_not_found"));

    public static FailureDescriptor TotalChanged(decimal total, string currency) => new(
        new ErrorIdentity(Domain, "ORDER_TOTAL_CHANGED"), ErrorCategory.Precondition,
        new FailureMessageDescriptor("ordering.total_changed", new Dictionary<string, string>
        {
            ["total"] = total.ToString("0.##", CultureInfo.InvariantCulture),
            ["currency"] = currency
        }));

    /// <summary>What the restaurant refused, under the restaurant's own code: closed, or an item that is not on sale.</summary>
    public static FailureDescriptor RefusedByRestaurant(string code, string? detail) => new(
        new ErrorIdentity("tiffin.restaurants", code), ErrorCategory.BusinessRule,
        new FailureMessageDescriptor("ordering.refused_by_restaurant", new Dictionary<string, string> { ["detail"] = detail ?? code }));

    public static FailureDescriptor RestaurantsUnavailable() => Unavailable("RESTAURANTS_UNAVAILABLE", "ordering.restaurants_unavailable");

    public static FailureDescriptor PaymentsUnavailable() => Unavailable("PAYMENTS_UNAVAILABLE", "ordering.payments_unavailable");

    public static FailureDescriptor PaymentTokenRefused() => new(
        new ErrorIdentity(Domain, "PAYMENT_TOKEN_REFUSED"), ErrorCategory.Validation, new FailureMessageDescriptor("ordering.payment_token_refused"),
        RetryDirective.Never,
        [new ValidationFailureDetail([new FieldViolation("paymentToken", "REFUSED", new FailureMessageDescriptor("ordering.payment_token_refused"))])]);

    private static FailureDescriptor Unavailable(string code, string messageKey) => new(
        new ErrorIdentity(Domain, code), ErrorCategory.DependencyUnavailable, new FailureMessageDescriptor(messageKey),
        RetryDirective.After(TimeSpan.FromSeconds(5)));
}
