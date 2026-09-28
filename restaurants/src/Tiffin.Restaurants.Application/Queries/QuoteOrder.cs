using MPCore.Application.Messaging;
using MPCore.Application.Results;
using Tiffin.Restaurants.Application.Ports;
using Tiffin.Restaurants.Application.Views;

namespace Tiffin.Restaurants.Application.Queries;

/// <summary>What the lines of an order cost at this moment. Asked by the Ordering service, for an order somebody is placing.</summary>
public sealed record QuoteOrder(Guid RestaurantId, IReadOnlyList<QuoteOrderLine> Lines) : IQuery<Result<QuoteView>>;

public sealed record QuoteOrderLine(string Code, int Quantity);

/// <summary>
/// Prices an order from the database, never from the cache: a customer may be shown a price that is five
/// minutes old, and is charged the one that is true now.
/// </summary>
/// <remarks>
/// <para>
/// <b>A question between two services, answered while the caller waits.</b> Ordering cannot place an order
/// without it, so Ordering is down for new orders while this service is. That is a decision, not an
/// accident: the alternative, a copy of every menu kept in Ordering from events (Martin Fowler's
/// <i>event-carried state transfer</i>), lets Ordering work alone and charge a price that changed a second
/// ago. A price is the one thing here that has to be current.
/// </para>
/// <para>
/// The caller is a service and its token names no city. The answer names the restaurant's city, and
/// Ordering refuses an order from somebody of another city.
/// </para>
/// </remarks>
public static class QuoteOrderHandler
{
    public static async Task<Result<QuoteView>> Handle(QuoteOrder query, IRestaurantReadModel restaurants, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(restaurants);

        var restaurant = await restaurants.ForQuoteAsync(query.RestaurantId, cancellationToken).ConfigureAwait(false);
        if (restaurant is null)
        {
            return Result<QuoteView>.FromFailure(RestaurantFailures.RestaurantNotFound());
        }

        // A closed restaurant, or an item that is sold out, breaks a rule of the aggregate (R3, R4). The
        // transport reports it under the rule's own code.
        var quote = restaurant.QuoteFor([.. query.Lines.Select(static l => (l.Code.Trim(), l.Quantity))]);
        return Result<QuoteView>.Success(RestaurantViews.Of(quote));
    }
}
