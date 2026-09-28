using MPCore.Application.Results;
using Tiffin.Restaurants.Domain.Rules;

namespace Tiffin.Restaurants.Application;

/// <summary>The failures a handler returns as values: expected outcomes, not broken rules.</summary>
public static class RestaurantFailures
{
    public const string Domain = RestaurantRule.Domain;

    /// <summary>Also the answer for a restaurant of another city: what a caller may not see does not exist for them.</summary>
    public static FailureDescriptor RestaurantNotFound() => new(
        new ErrorIdentity(Domain, "RESTAURANT_NOT_FOUND"), ErrorCategory.NotFound,
        new FailureMessageDescriptor("restaurants.not_found"));

    public static FailureDescriptor NotTheManager() => new(
        new ErrorIdentity(Domain, "NOT_THE_MANAGER"), ErrorCategory.Forbidden,
        new FailureMessageDescriptor("restaurants.not_the_manager"));

    public static FailureDescriptor CityRequired() => new(
        new ErrorIdentity(Domain, "CITY_REQUIRED"), ErrorCategory.Forbidden,
        new FailureMessageDescriptor("restaurants.city_required"));

    public static FailureDescriptor NameTaken(string name) => new(
        new ErrorIdentity(Domain, "RESTAURANT_NAME_TAKEN"), ErrorCategory.AlreadyExists,
        new FailureMessageDescriptor("restaurants.name_taken", new Dictionary<string, string> { ["restaurant"] = name }));
}
