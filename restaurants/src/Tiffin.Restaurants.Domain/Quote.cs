using MPCore.Domain.Model;

namespace Tiffin.Restaurants.Domain;

/// <summary>What an order would cost at this moment. Equal by value: two quotes of the same lines at the same prices are the same quote.</summary>
public sealed class Quote(Guid restaurantId, string restaurantName, string city, string currency, IReadOnlyList<QuotedLine> lines) : ValueObject
{
    public Guid RestaurantId { get; } = restaurantId;

    public string RestaurantName { get; } = restaurantName;

    public string City { get; } = city;

    public string Currency { get; } = currency;

    public IReadOnlyList<QuotedLine> Lines { get; } = lines;

    public decimal Total => Lines.Sum(static l => l.LineTotal);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return RestaurantId;
        yield return Currency;
        foreach (var line in Lines)
        {
            yield return line;
        }
    }
}

public sealed record QuotedLine(string Code, string Name, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}
