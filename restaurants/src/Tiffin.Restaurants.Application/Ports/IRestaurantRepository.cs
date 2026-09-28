using Tiffin.Restaurants.Domain;

namespace Tiffin.Restaurants.Application.Ports;

/// <summary>
/// The restaurants of one city. There is no way to ask for a restaurant without naming the city: the
/// boundary between tenants is in the signature, where a forgotten filter does not compile.
/// </summary>
public interface IRestaurantRepository
{
    Task<Restaurant?> GetAsync(Guid id, string city, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(string name, string city, CancellationToken cancellationToken);

    void Add(Restaurant restaurant);
}
