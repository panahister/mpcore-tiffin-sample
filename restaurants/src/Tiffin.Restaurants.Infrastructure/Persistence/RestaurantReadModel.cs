using Microsoft.EntityFrameworkCore;
using MPCore.Application.Querying;
using Tiffin.Restaurants.Application.Ports;
using Tiffin.Restaurants.Application.Views;
using Tiffin.Restaurants.Domain;

namespace Tiffin.Restaurants.Infrastructure.Persistence;

/// <summary>The read side: read without tracking, mapped to a view before it leaves.</summary>
public sealed class RestaurantReadModel(AppDbContext database) : IRestaurantReadModel
{
    public async Task<Page<RestaurantSummary>> ListAsync(string city, bool openOnly, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var query = database.Set<Restaurant>().AsNoTracking().Where(r => r.City == city);
        if (openOnly)
        {
            query = query.Where(r => r.IsOpen);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderBy(r => r.Name)
            .Skip(page.Skip).Take(page.Size)
            .Select(r => new RestaurantSummary(r.Id, r.Name, r.City, r.Currency, r.IsOpen, r.PictureId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Page<RestaurantSummary>(items, page.Number, page.Size, total);
    }

    public async Task<MenuView?> MenuAsync(Guid restaurantId, string city, CancellationToken cancellationToken)
    {
        var restaurant = await database.Set<Restaurant>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == restaurantId && r.City == city, cancellationToken).ConfigureAwait(false);
        return restaurant is null ? null : RestaurantViews.MenuOf(restaurant);
    }

    public async Task<Restaurant?> ForQuoteAsync(Guid restaurantId, CancellationToken cancellationToken) =>
        await database.Set<Restaurant>().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == restaurantId, cancellationToken).ConfigureAwait(false);
}
