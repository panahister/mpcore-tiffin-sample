using MPCore.Application.Messaging;
using MPCore.Application.Querying;
using MPCore.Application.Results;
using MPCore.Caching.Abstractions;
using MPCore.Tenancy;
using Tiffin.Restaurants.Application.Ports;
using Tiffin.Restaurants.Application.Views;

namespace Tiffin.Restaurants.Application.Queries;

/// <summary>
/// The restaurants of a city. Anybody may look, signed in or not: <paramref name="City"/> is for a caller
/// without a token. A caller with a token sees the city of the token, whatever <paramref name="City"/> says.
/// </summary>
public sealed record ListRestaurants(string? City, bool OpenOnly = false, int Page = 1, int Size = PageRequest.DefaultSize)
    : IQuery<Result<Page<RestaurantSummary>>>;

/// <summary>The menu of one restaurant, read through the cache.</summary>
public sealed record GetMenu(Guid RestaurantId, string? City) : IQuery<Result<MenuView>>;

public static class RestaurantQueriesHandler
{
    public static async Task<Result<Page<RestaurantSummary>>> Handle(
        ListRestaurants query, ITenantContext tenant, IRestaurantReadModel restaurants, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(restaurants);

        if (CityOf(tenant, query.City) is not { } city)
        {
            return Result<Page<RestaurantSummary>>.FromFailure(RestaurantFailures.CityRequired());
        }

        return Result<Page<RestaurantSummary>>.Success(
            await restaurants.ListAsync(city, query.OpenOnly, new PageRequest(query.Page, query.Size), cancellationToken).ConfigureAwait(false));
    }

    public static async Task<Result<MenuView>> Handle(
        GetMenu query, ITenantContext tenant, IRestaurantReadModel restaurants, IReadThroughCache cache, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(restaurants);
        ArgumentNullException.ThrowIfNull(cache);

        if (CityOf(tenant, query.City) is not { } city)
        {
            return Result<MenuView>.FromFailure(RestaurantFailures.CityRequired());
        }

        // A restaurant that does not exist is cached as well, as an empty answer: asking again and again
        // for an identifier that was made up does not reach the database each time.
        var menu = await cache.GetOrCreateAsync(
            MenuCache.KeyOf(city, query.RestaurantId),
            async token => new CachedMenu(await restaurants.MenuAsync(query.RestaurantId, city, token).ConfigureAwait(false)),
            MenuCache.Lifetime, cancellationToken).ConfigureAwait(false);
        return menu.Menu is null
            ? Result<MenuView>.FromFailure(RestaurantFailures.RestaurantNotFound())
            : Result<MenuView>.Success(menu.Menu);
    }

    /// <summary>The token's city wins. Without a token, the city that was asked for.</summary>
    private static string? CityOf(ITenantContext tenant, string? asked) =>
        tenant.TenantId ?? (string.IsNullOrWhiteSpace(asked) ? null : asked.Trim().ToLowerInvariant());
}

/// <summary>What the cache holds: a menu, or the knowledge that there is none.</summary>
public sealed record CachedMenu(MenuView? Menu);
