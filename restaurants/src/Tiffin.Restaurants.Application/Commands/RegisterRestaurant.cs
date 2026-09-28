using MPCore.Application.Messaging;
using MPCore.Application.Results;
using MPCore.Application.Time;
using MPCore.Audit;
using MPCore.Persistence.Abstractions;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Restaurants.Application.Ports;
using Tiffin.Restaurants.Application.Views;
using Tiffin.Restaurants.Domain;

namespace Tiffin.Restaurants.Application.Commands;

/// <summary>A manager registers a restaurant in the manager's own city. It starts closed and with an empty menu.</summary>
public sealed record RegisterRestaurant(string Name, string Currency) : ICommand<Result<MenuView>>;

/// <summary>
/// The city is never asked for. It is the tenant of the caller's token, so a manager cannot register a
/// restaurant anywhere else, whatever the request says.
/// </summary>
public static class RegisterRestaurantHandler
{
    public static async Task<Result<MenuView>> Handle(
        RegisterRestaurant command,
        ICurrentActorAccessor actor,
        ITenantContext tenant,
        IRestaurantRepository restaurants,
        IBusinessAuditRecorder audit,
        IUnitOfWork unitOfWork,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(restaurants);
        ArgumentNullException.ThrowIfNull(audit);

        if (tenant.TenantId is not { } city || actor.Current.SubjectId is not { } manager)
        {
            return Result<MenuView>.FromFailure(RestaurantFailures.CityRequired());
        }

        if (await restaurants.NameExistsAsync(command.Name.Trim(), city, cancellationToken).ConfigureAwait(false))
        {
            return Result<MenuView>.FromFailure(RestaurantFailures.NameTaken(command.Name.Trim()));
        }

        var restaurant = Restaurant.Register(city, command.Name, manager, command.Currency, clock.UtcNow);
        restaurants.Add(restaurant);

        await audit.RecordAsync(
            "restaurants", "restaurant-registered", nameof(Restaurant), restaurant.Id.ToString(),
            new Dictionary<string, string> { ["name"] = restaurant.Name, ["city"] = city }, cancellationToken).ConfigureAwait(false);
        return Result<MenuView>.Success(RestaurantViews.MenuOf(restaurant));
    }
}
