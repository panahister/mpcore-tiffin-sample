using Tiffin.Ordering.Domain;

namespace Tiffin.Ordering.Application.Views;

/// <summary>What the customer is told at once: the order has an identity and a total; the rest follows.</summary>
public sealed record OrderAccepted(Guid OrderId, string OrderNumber, decimal Total, string Currency, DateTimeOffset PlacedOnUtc);

public sealed record OrderSummary(
    Guid OrderId, string OrderNumber, string RestaurantName, string Status, decimal Total, string Currency, DateTimeOffset PlacedOnUtc);

public sealed record OrderLineView(string Code, string Name, decimal UnitPrice, int Quantity, decimal LineTotal);

public sealed record OrderHistoryView(string Status, DateTimeOffset OccurredOnUtc, string? Note);

public sealed record OrderView(
    Guid OrderId, string OrderNumber, string City, string Status, Guid RestaurantId, string RestaurantName,
    IReadOnlyList<OrderLineView> Lines, decimal Total, string Currency, string District,
    string? CancellationReason, string? CourierName, int? ReadyInMinutes, bool Refunded,
    DateTimeOffset PlacedOnUtc, IReadOnlyList<OrderHistoryView> History)
{
    /// <summary>Who ordered. Not part of the answer: the handler compares it with the caller.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string CustomerId { get; init; } = string.Empty;
}

public static class OrderViews
{
    public static OrderView Of(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new OrderView(
            order.Id, order.OrderNumber, order.City, order.Status.ToString(), order.RestaurantId, order.RestaurantName,
            [.. order.Lines.Select(static l => new OrderLineView(l.Code, l.Name, l.UnitPrice, l.Quantity, l.LineTotal))],
            order.Total, order.Currency, order.DeliverTo.District,
            order.CancellationReason, order.CourierName, order.ReadyInMinutes, order.RefundReference is not null,
            order.PlacedOnUtc,
            [.. order.History.Select(static h => new OrderHistoryView(h.Status.ToString(), h.OccurredOnUtc, h.Note))])
        {
            CustomerId = order.CustomerId
        };
    }
}
