using MPCore.Application.Messaging;
using MPCore.Application.Results;
using MPCore.Application.Time;
using MPCore.Audit;
using MPCore.Messaging.Abstractions;
using MPCore.Persistence.Abstractions;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Ordering.Application.Contracts;
using Tiffin.Ordering.Application.Ports;
using Tiffin.Ordering.Application.Views;
using Tiffin.Ordering.Domain;

namespace Tiffin.Ordering.Application.Commands;

/// <summary>A customer orders from one restaurant. <paramref name="ExpectedTotal"/> is what the customer saw.</summary>
/// <param name="PaymentToken">The token the payment provider gave the customer's app for the card. It is handed to Payments and kept nowhere here.</param>
/// <remarks>
/// Wolverine logs a message whose handling failed by printing it, and a record prints every property. The
/// address is personal data and the token is a credential, so this record says what it prints.
/// </remarks>
public sealed record PlaceOrder(
    Guid RestaurantId, IReadOnlyList<PlaceOrderLine> Lines, PlaceOrderAddress DeliverTo, string PaymentToken, decimal ExpectedTotal)
    : ICommand<Result<OrderAccepted>>
{
    public override string ToString() =>
        $"{nameof(PlaceOrder)} {{ RestaurantId = {RestaurantId}, Lines = {Lines?.Count ?? 0}, ExpectedTotal = {ExpectedTotal} }}";
}

public sealed record PlaceOrderLine(string Code, int Quantity);

public sealed record PlaceOrderAddress(string Recipient, string Phone, string District, string Line)
{
    public override string ToString() => $"{nameof(PlaceOrderAddress)} {{ District = {District} }}";
}

/// <summary>
/// Places the order: two questions to other services while the customer waits, then one transaction of
/// this service's own.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is asked while the customer waits.</b> The price, of Restaurants, because a price must be the
/// one that is true now; and a reference for the card, of Payments, so that the card's token goes no
/// further than that call. Both are calls of this service as itself, with a token the identity provider
/// issued to it, over a client that times out, retries and stops calling a service that does not answer.
/// When either cannot be reached the customer is told so at once and nothing was stored.
/// </para>
/// <para>
/// <b>What is not asked.</b> Whether the card has the money, whether the restaurant will cook, whether a
/// courier is free. Each of those is answered later by the service that knows, and the order moves when the
/// answer arrives (see <c>OrderProcessHandler</c>). The customer is told "accepted", HTTP 202.
/// </para>
/// <para>
/// <b>One transaction.</b> The order and the request to Payments commit together (the transactional
/// outbox). An order without its request, or a request without its order, cannot exist.
/// </para>
/// <para>
/// <b>A reference that is never used.</b> When the order's own transaction fails after Payments gave a
/// reference, the reference stays behind. Payments lets it expire and erases the token with it. The
/// customer's retry, with the same <c>Idempotency-Key</c>, asks for a new one.
/// </para>
/// </remarks>
public static class PlaceOrderHandler
{
    public static async Task<Result<OrderAccepted>> Handle(
        PlaceOrder command,
        ICurrentActorAccessor actor,
        ITenantContext tenant,
        IRestaurantQuotes quotes,
        IPaymentIntents paymentIntents,
        IOrderRepository orders,
        IMessagePublisher publisher,
        IBusinessAuditRecorder audit,
        IUnitOfWork unitOfWork,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(quotes);
        ArgumentNullException.ThrowIfNull(paymentIntents);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(audit);

        // Who orders, and in which city, comes from the validated token, never from the request.
        var customer = actor.Current;
        if (customer.SubjectId is not { } customerId || tenant.TenantId is not { } city)
        {
            return Result<OrderAccepted>.FromFailure(OrderingFailures.CustomerRequired());
        }

        var quoted = await quotes.QuoteAsync(
            command.RestaurantId, [.. command.Lines.Select(static l => (l.Code.Trim(), l.Quantity))], cancellationToken).ConfigureAwait(false);
        if (quoted.IsFailure)
        {
            return Result<OrderAccepted>.FromFailure(quoted.FailureDescriptor!);
        }

        var quote = quoted.Value;
        if (!string.Equals(quote.City, city, StringComparison.Ordinal))
        {
            // A restaurant of another city. What a caller may not see does not exist for them.
            return Result<OrderAccepted>.FromFailure(OrderingFailures.RestaurantNotFound());
        }

        if (quote.Total != command.ExpectedTotal)
        {
            return Result<OrderAccepted>.FromFailure(OrderingFailures.TotalChanged(quote.Total, quote.Currency));
        }

        // A version 7 UUID is ordered by time and needs no coordination, so the order has its identity
        // before anything was stored, and Payments can be told which order the card is for.
        var now = clock.UtcNow;
        var orderId = Guid.CreateVersion7(now);
        var intent = await paymentIntents.CreateAsync(orderId, customerId, command.PaymentToken, quote.Total, quote.Currency, cancellationToken)
            .ConfigureAwait(false);
        if (intent.IsFailure)
        {
            return Result<OrderAccepted>.FromFailure(intent.FailureDescriptor!);
        }

        var to = command.DeliverTo;
        var order = Order.Place(
            orderId, city, customerId, customer.DisplayName ?? customer.UserName ?? customerId, quote.RestaurantId, quote.RestaurantName,
            quote.Currency, new DeliveryAddress(to.Recipient, to.Phone, to.District, to.Line), intent.Value,
            [.. quote.Lines.Select(static l => new OrderLine(l.Code, l.Name, l.UnitPrice, l.Quantity))], now);
        orders.Add(order);

        await publisher.PublishAsync(
            new PaymentRequested(order.Id, order.OrderNumber, order.PaymentIntentId, order.Total, order.Currency, now), cancellationToken)
            .ConfigureAwait(false);
        await audit.RecordAsync(
            "ordering", "order-placed", nameof(Order), order.OrderNumber,
            new Dictionary<string, string> { ["restaurant"] = order.RestaurantName, ["lines"] = order.Lines.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            cancellationToken).ConfigureAwait(false);

        return Result<OrderAccepted>.Success(new OrderAccepted(order.Id, order.OrderNumber, order.Total, order.Currency, now));
    }
}
