namespace Tiffin.Ordering.Domain;

/// <summary>One item of an order, at the price the restaurant quoted when the order was placed. It never changes.</summary>
public sealed record OrderLine(string Code, string Name, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>One step of an order's life, kept for the customer and for whoever has to explain it.</summary>
public sealed record OrderHistoryEntry(OrderStatus Status, DateTimeOffset OccurredOnUtc, string? Note);
