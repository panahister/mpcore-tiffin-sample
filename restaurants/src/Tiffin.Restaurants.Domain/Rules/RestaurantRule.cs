using System.Globalization;
using MPCore.Domain.Rules;

namespace Tiffin.Restaurants.Domain.Rules;

/// <summary>
/// A business rule of the Restaurants service. Every rule has a stable code, is reported under the
/// <c>tiffin.restaurants</c> error domain, and names a message key the transport renders in the caller's
/// language.
/// </summary>
/// <remarks>
/// The named-rule pattern comes from Kamil Grzybek's <i>Modular Monolith with DDD</i>. The aggregate checks
/// a rule before it changes state, so an aggregate is never invalid: Vladimir Khorikov's <i>always-valid
/// domain model</i>.
/// </remarks>
public abstract class RestaurantRule(string code, string messageKey, IReadOnlyDictionary<string, string>? arguments = null)
    : BusinessRule(Domain, code, messageKey, arguments)
{
    public const string Domain = "tiffin.restaurants";
}

/// <summary>Rule R1: a price is more than nothing.</summary>
public sealed class PriceMustBePositive(string code, decimal price) : RestaurantRule(
    "PRICE_NOT_POSITIVE", "restaurants.price_not_positive",
    new Dictionary<string, string> { ["code"] = code, ["price"] = price.ToString(CultureInfo.InvariantCulture) })
{
    public override bool IsBroken() => price <= 0;
}

/// <summary>Rule R2: a restaurant opens with something to sell.</summary>
public sealed class AnOpenRestaurantSellsSomething(Restaurant restaurant) : RestaurantRule(
    "MENU_EMPTY", "restaurants.menu_empty", new Dictionary<string, string> { ["restaurant"] = restaurant.Name })
{
    public override bool IsBroken() => !restaurant.Menu.Any(static i => i.IsAvailable);
}

/// <summary>Rule R3: a closed restaurant takes no order, so it quotes none.</summary>
public sealed class OnlyAnOpenRestaurantQuotes(Restaurant restaurant) : RestaurantRule(
    "RESTAURANT_CLOSED", "restaurants.closed", new Dictionary<string, string> { ["restaurant"] = restaurant.Name })
{
    public override bool IsBroken() => !restaurant.IsOpen;
}

/// <summary>Rule R4: what is ordered is on the menu, and not sold out.</summary>
public sealed class AQuotedItemIsOnSale(string restaurant, string code, MenuItem? item) : RestaurantRule(
    "ITEM_NOT_ON_SALE", "restaurants.item_not_on_sale", new Dictionary<string, string> { ["restaurant"] = restaurant, ["code"] = code })
{
    public override bool IsBroken() => item is null || !item.IsAvailable;
}

/// <summary>Rule R5: a restaurant sells in a currency the platform settles.</summary>
public sealed class CurrencyMustBeKnown(string? currency) : RestaurantRule(
    "CURRENCY_UNKNOWN", "restaurants.currency_unknown", new Dictionary<string, string> { ["currency"] = currency ?? string.Empty })
{
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase) { "IRR", "TRY", "USD", "EUR" };

    public override bool IsBroken() => currency is null || !Known.Contains(currency);
}
