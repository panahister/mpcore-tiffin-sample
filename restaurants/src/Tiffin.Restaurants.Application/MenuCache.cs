namespace Tiffin.Restaurants.Application;

/// <summary>
/// The keys of the cache. A key names the city: the cache is shared by every instance of this service and
/// by every city, and a menu of Tehran is never found under a key of Istanbul.
/// </summary>
public static class MenuCache
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public static string KeyOf(string city, Guid restaurantId) => $"menu:{city}:{restaurantId:N}";
}
