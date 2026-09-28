using MPCore.Domain.Model;

namespace Tiffin.Restaurants.Domain;

/// <summary>One thing a restaurant sells. It has an identity inside its restaurant, the code, and none outside.</summary>
public sealed class MenuItem : Entity<string>
{
    private MenuItem()
    {
        Name = string.Empty;
    }

    internal MenuItem(string code, string name, decimal price, bool available)
        : base(code)
    {
        Name = name;
        Price = price;
        IsAvailable = available;
    }

    public string Code => Id;

    public string Name { get; private set; }

    public decimal Price { get; private set; }

    /// <summary>Sold out for today, without leaving the menu.</summary>
    public bool IsAvailable { get; private set; }

    internal void Change(string name, decimal price, bool available)
    {
        Name = name;
        Price = price;
        IsAvailable = available;
    }
}
