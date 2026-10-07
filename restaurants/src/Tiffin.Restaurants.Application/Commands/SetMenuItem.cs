using MPCore.Application.Messaging;
using MPCore.Application.Results;
using MPCore.Caching.Abstractions;
using MPCore.Persistence.Abstractions;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Restaurants.Application.Ports;
using Tiffin.Restaurants.Application.Views;

namespace Tiffin.Restaurants.Application.Commands;

/// <summary>The manager adds an item to the menu, or changes its name, its price or whether it is sold out.</summary>
public sealed record SetMenuItem(Guid RestaurantId, string Code, string Name, decimal Price, bool IsAvailable, Guid? PictureId = null) : ICommand<Result<MenuView>>;

/// <summary>Opens or closes the restaurant.</summary>
public sealed record SetOpen(Guid RestaurantId, bool Open) : ICommand<Result<MenuView>>;

/// <summary>Shows a picture the Media service keeps, or none.</summary>
public sealed record ShowPicture(Guid RestaurantId, Guid? PictureId) : ICommand<Result<MenuView>>;

/// <summary>
/// The three changes a manager makes to a restaurant. Each loads the restaurant of the caller's city,
/// asks whether the caller manages it, changes it, and forgets the cached menu.
/// </summary>
/// <remarks>
/// The menu is forgotten before the change is committed, and the read that follows caches what it finds.
/// A read between the two caches the old menu for its lifetime of five minutes: the price of evicting by
/// key without a lock. An order is priced from the database, never from the cache (see <c>QuoteOrder</c>),
/// so what a stale menu can cost is a customer who sees an old price and is told the new one at checkout.
/// </remarks>
public static class ManageRestaurantHandler
{
    public static Task<Result<MenuView>> Handle(
        SetMenuItem command, ICurrentActorAccessor actor, ITenantContext tenant, IRestaurantRepository restaurants,
        ICache cache, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ChangeAsync(
            command.RestaurantId, actor, tenant, restaurants, cache,
            restaurant => restaurant.SetMenuItem(command.Code.Trim(), command.Name, command.Price, command.IsAvailable, command.PictureId),
            cancellationToken);
    }

    public static Task<Result<MenuView>> Handle(
        SetOpen command, ICurrentActorAccessor actor, ITenantContext tenant, IRestaurantRepository restaurants,
        ICache cache, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ChangeAsync(
            command.RestaurantId, actor, tenant, restaurants, cache,
            restaurant =>
            {
                if (command.Open)
                {
                    restaurant.Open();
                }
                else
                {
                    restaurant.Close();
                }
            },
            cancellationToken);
    }

    public static Task<Result<MenuView>> Handle(
        ShowPicture command, ICurrentActorAccessor actor, ITenantContext tenant, IRestaurantRepository restaurants,
        ICache cache, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ChangeAsync(
            command.RestaurantId, actor, tenant, restaurants, cache,
            restaurant => restaurant.ShowPicture(command.PictureId), cancellationToken);
    }

    private static async Task<Result<MenuView>> ChangeAsync(
        Guid restaurantId, ICurrentActorAccessor actor, ITenantContext tenant, IRestaurantRepository restaurants, ICache cache,
        Action<Domain.Restaurant> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(restaurants);
        ArgumentNullException.ThrowIfNull(cache);

        if (tenant.TenantId is not { } city)
        {
            return Result<MenuView>.FromFailure(RestaurantFailures.CityRequired());
        }

        var restaurant = await restaurants.GetAsync(restaurantId, city, cancellationToken).ConfigureAwait(false);
        if (restaurant is null)
        {
            return Result<MenuView>.FromFailure(RestaurantFailures.RestaurantNotFound());
        }

        if (actor.Current.SubjectId is not { } caller || !restaurant.IsManagedBy(caller))
        {
            return Result<MenuView>.FromFailure(RestaurantFailures.NotTheManager());
        }

        change(restaurant);
        await cache.RemoveAsync(MenuCache.KeyOf(city, restaurant.Id), cancellationToken).ConfigureAwait(false);
        return Result<MenuView>.Success(RestaurantViews.MenuOf(restaurant));
    }
}
