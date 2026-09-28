using MPCore.Application.Querying;
using MPCore.Application.Results;
using Tiffin.Ordering.Application.Views;
using Tiffin.Ordering.Domain;

namespace Tiffin.Ordering.Application.Ports;

/// <summary>
/// The orders of one city. An order is asked for with its city, except by the process, which works for the
/// city of the message it received and asks by the order alone.
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetAsync(Guid id, string city, CancellationToken cancellationToken);

    void Add(Order order);
}

/// <summary>The read side. Nothing here is tracked, and nothing is changed.</summary>
public interface IOrderReadModel
{
    Task<OrderView?> FindAsync(Guid id, string city, CancellationToken cancellationToken);

    Task<Page<OrderSummary>> ListOfCustomerAsync(string customerId, string city, PageRequest page, CancellationToken cancellationToken);
}

/// <summary>What the lines of an order cost now. The Restaurants service answers.</summary>
public interface IRestaurantQuotes
{
    Task<Result<RestaurantQuote>> QuoteAsync(Guid restaurantId, IReadOnlyList<(string Code, int Quantity)> lines, CancellationToken cancellationToken);
}

public sealed record RestaurantQuote(
    Guid RestaurantId, string RestaurantName, string City, string Currency, IReadOnlyList<RestaurantQuoteLine> Lines, decimal Total);

public sealed record RestaurantQuoteLine(string Code, string Name, decimal UnitPrice, int Quantity);

/// <summary>
/// Hands the token of the customer's card to the Payments service and brings back a reference. The token
/// goes no further than this call: the order keeps the reference, and the messages carry the reference.
/// </summary>
public interface IPaymentIntents
{
    Task<Result<Guid>> CreateAsync(Guid orderId, string customerId, string paymentToken, decimal amount, string currency, CancellationToken cancellationToken);
}
