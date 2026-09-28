using Microsoft.EntityFrameworkCore;
using Tiffin.Restaurants.Application.Ports;
using Tiffin.Restaurants.Domain;

namespace Tiffin.Restaurants.Infrastructure.Persistence;

public sealed class RestaurantRepository(AppDbContext database) : IRestaurantRepository
{
    public async Task<Restaurant?> GetAsync(Guid id, string city, CancellationToken cancellationToken) =>
        await database.Set<Restaurant>().FirstOrDefaultAsync(r => r.Id == id && r.City == city, cancellationToken).ConfigureAwait(false);

    public async Task<bool> NameExistsAsync(string name, string city, CancellationToken cancellationToken) =>
        await database.Set<Restaurant>().AsNoTracking().AnyAsync(r => r.City == city && r.Name == name, cancellationToken).ConfigureAwait(false);

    public void Add(Restaurant restaurant) => database.Set<Restaurant>().Add(restaurant);
}
