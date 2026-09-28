namespace Tiffin.Access.Domain;

/// <summary>The roles of the platform, as the identity provider names them, and who may hand each of them out.</summary>
/// <remarks>
/// A role that is not listed under <see cref="GrantableBy"/> is handed out by nobody through this service:
/// <c>platform-admin</c> and <c>service</c> are set where the realm is set up, by an operator.
/// </remarks>
public static class Roles
{
    public const string Customer = "customer";
    public const string RestaurantManager = "restaurant-manager";
    public const string Courier = "courier";
    public const string CityAdmin = "city-admin";
    public const string PlatformAdmin = "platform-admin";

    /// <summary>The city of those who manage the platform, not a city.</summary>
    public const string PlatformCity = "platform";

    private static readonly string[] OfACity = [Customer, RestaurantManager, Courier];

    /// <summary>Every role this service knows by name.</summary>
    public static IReadOnlyList<string> Known { get; } = [Customer, RestaurantManager, Courier, CityAdmin];

    /// <summary>The roles somebody with these roles may hand out or take away.</summary>
    public static IReadOnlyList<string> GrantableBy(IReadOnlyCollection<string> rolesOfTheOneWhoGrants)
    {
        ArgumentNullException.ThrowIfNull(rolesOfTheOneWhoGrants);
        if (rolesOfTheOneWhoGrants.Contains(PlatformAdmin))
        {
            return [.. OfACity, CityAdmin];
        }

        return rolesOfTheOneWhoGrants.Contains(CityAdmin) ? OfACity : [];
    }
}
