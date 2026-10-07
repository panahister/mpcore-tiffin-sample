using Tiffin.Restaurants.Domain;

namespace Tiffin.Restaurants.Application.Views;

public sealed record RestaurantSummary(Guid RestaurantId, string Name, string City, string Currency, bool IsOpen, Guid? PictureId);

public sealed record MenuItemView(string Code, string Name, decimal Price, bool IsAvailable, Guid? PictureId);

public sealed record MenuView(Guid RestaurantId, string Name, string City, string Currency, bool IsOpen, Guid? PictureId, IReadOnlyList<MenuItemView> Items);

public sealed record QuoteLineView(string Code, string Name, decimal UnitPrice, int Quantity, decimal LineTotal);

public sealed record QuoteView(Guid RestaurantId, string RestaurantName, string City, string Currency, IReadOnlyList<QuoteLineView> Lines, decimal Total);

public static class RestaurantViews
{
    public static MenuView MenuOf(Restaurant restaurant)
    {
        ArgumentNullException.ThrowIfNull(restaurant);
        return new MenuView(
            restaurant.Id, restaurant.Name, restaurant.City, restaurant.Currency, restaurant.IsOpen, restaurant.PictureId,
            [.. restaurant.Menu.OrderBy(static i => i.Code, StringComparer.Ordinal).Select(static i => new MenuItemView(i.Code, i.Name, i.Price, i.IsAvailable, i.PictureId))]);
    }

    public static QuoteView Of(Quote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        return new QuoteView(
            quote.RestaurantId, quote.RestaurantName, quote.City, quote.Currency,
            [.. quote.Lines.Select(static l => new QuoteLineView(l.Code, l.Name, l.UnitPrice, l.Quantity, l.LineTotal))],
            quote.Total);
    }
}
