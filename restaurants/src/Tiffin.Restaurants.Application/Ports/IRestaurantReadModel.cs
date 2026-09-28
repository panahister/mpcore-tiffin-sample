using MPCore.Application.Querying;
using Tiffin.Restaurants.Application.Views;
using Tiffin.Restaurants.Domain;

namespace Tiffin.Restaurants.Application.Ports;

/// <summary>The read side. Nothing here is tracked, and nothing is changed.</summary>
public interface IRestaurantReadModel
{
    Task<Page<RestaurantSummary>> ListAsync(string city, bool openOnly, PageRequest page, CancellationToken cancellationToken);

    Task<MenuView?> MenuAsync(Guid restaurantId, string city, CancellationToken cancellationToken);

    /// <summary>
    /// A restaurant by its identity alone, for a service of the platform that asks on behalf of an order.
    /// The answer names the city, and the caller compares it with the city of whoever ordered.
    /// </summary>
    Task<Restaurant?> ForQuoteAsync(Guid restaurantId, CancellationToken cancellationToken);
}
