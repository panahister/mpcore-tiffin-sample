using MPCore.Application.Querying;
using MPCore.Application.Results;
using MPCore.Transport.Http;
using Tiffin.Restaurants.Api.Hosting;
using Tiffin.Restaurants.Application.Commands;
using Tiffin.Restaurants.Application.Queries;
using Tiffin.Restaurants.Application.Views;
using Wolverine;

namespace Tiffin.Restaurants.Api.Rest.Endpoints;

/// <summary>The REST surface of the Restaurants service.</summary>
/// <remarks>
/// <para>
/// Every endpoint does the same three things: bind the request, invoke one command or query through
/// Wolverine, and render the <see cref="Result"/> with MP Core's <c>ToHttpResult</c>. A failure becomes
/// RFC 9457 Problem Details with the failure's stable code; no endpoint builds an error by hand.
/// </para>
/// <para>
/// The two reads are anonymous, a product decision made visibly here against MP Core's protect-by-default
/// fallback: a menu is an advertisement. Everything else needs a token from the <c>tiffin</c> realm.
/// </para>
/// </remarks>
public static class RestaurantEndpoints
{
    public static RouteGroupBuilder MapRestaurantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var restaurants = endpoints.MapGroup("/v1/restaurants").WithTags("Restaurants");

        restaurants.MapGet("/", static async (string? city, bool? open, int? page, int? size, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<Page<RestaurantSummary>>>(
                    new ListRestaurants(city, open ?? false, page ?? 1, size ?? PageRequest.DefaultSize), ct).ConfigureAwait(false))
                .ToHttpResult(Results.Ok))
            .AllowAnonymous()
            .Produces<Page<RestaurantSummary>>(200)
            .WithName("ListRestaurants");

        restaurants.MapGet("/{restaurantId:guid}/menu", static async (Guid restaurantId, string? city, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<MenuView>>(new GetMenu(restaurantId, city), ct).ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .AllowAnonymous()
            .Produces<MenuView>(200)
            .WithName("GetMenu");

        var manage = restaurants.MapGroup("").RequireAuthorization(RestaurantsPolicies.Manager);

        manage.MapPost("/", static async (RegisterRestaurant request, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<MenuView>>(request, ct).ConfigureAwait(false))
                .ToHttpResult(menu => Results.Created($"/v1/restaurants/{menu.RestaurantId}/menu", menu)))
            .Produces<MenuView>(201)
            .WithName("RegisterRestaurant");

        manage.MapPut("/{restaurantId:guid}/menu/{code}", static async (
                Guid restaurantId, string code, MenuItemRequest request, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<MenuView>>(
                    new SetMenuItem(restaurantId, code, request.Name, request.Price, request.IsAvailable ?? true, request.PictureId), ct).ConfigureAwait(false))
                .ToHttpResult(Results.Ok))
            .Produces<MenuView>(200)
            .WithName("SetMenuItem");

        manage.MapPost("/{restaurantId:guid}/open", static async (Guid restaurantId, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<MenuView>>(new SetOpen(restaurantId, true), ct).ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .Produces<MenuView>(200)
            .WithName("OpenRestaurant");

        manage.MapPost("/{restaurantId:guid}/close", static async (Guid restaurantId, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<MenuView>>(new SetOpen(restaurantId, false), ct).ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .Produces<MenuView>(200)
            .WithName("CloseRestaurant");

        manage.MapPut("/{restaurantId:guid}/picture", static async (Guid restaurantId, PictureRequest request, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<MenuView>>(new ShowPicture(restaurantId, request.PictureId), ct).ConfigureAwait(false))
                .ToHttpResult(Results.Ok))
            .Produces<MenuView>(200)
            .WithName("ShowRestaurantPicture");

        // For a service of the platform only: a person's token is refused here, whatever its role.
        restaurants.MapPost("/{restaurantId:guid}/quotes", static async (Guid restaurantId, QuoteRequest request, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<QuoteView>>(
                    new QuoteOrder(restaurantId, [.. (request.Lines ?? []).Select(static l => new QuoteOrderLine(l.Code, l.Quantity))]), ct)
                    .ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .RequireAuthorization(RestaurantsPolicies.Service)
            .Produces<QuoteView>(200)
            .WithName("QuoteOrder");

        return restaurants;
    }
}

public sealed record MenuItemRequest(string Name, decimal Price, bool? IsAvailable, Guid? PictureId);

public sealed record PictureRequest(Guid? PictureId);

public sealed record QuoteRequest(IReadOnlyList<QuoteRequestLine>? Lines);

public sealed record QuoteRequestLine(string Code, int Quantity);
