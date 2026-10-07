using MPCore.Domain.Model;
using Tiffin.Restaurants.Domain.Events;
using Tiffin.Restaurants.Domain.Rules;

namespace Tiffin.Restaurants.Domain;

/// <summary>A restaurant of one city, and what it sells today.</summary>
/// <remarks>
/// <para>
/// <b>The city is the tenant.</b> A restaurant belongs to the city of the manager who registered it, and
/// nothing moves it to another. Every read and every change names the city, so that a manager of Istanbul
/// never sees a restaurant of Tehran, whatever identifier is asked for.
/// </para>
/// <para>
/// The menu is part of the aggregate: a price is changed through the restaurant, which is how the rule
/// "only an open restaurant quotes" and the rule "a price is positive" are kept in one place. Eric Evans
/// calls the boundary an <i>aggregate</i> (<i>Domain-Driven Design</i>, 2003); Vaughn Vernon's rule is to
/// keep it as small as the invariants allow (<i>Implementing Domain-Driven Design</i>, 2013).
/// </para>
/// </remarks>
public sealed class Restaurant : AggregateRoot<Guid>
{
    private readonly List<MenuItem> menu = [];

    private Restaurant()
    {
        City = string.Empty;
        Name = string.Empty;
        ManagerId = string.Empty;
        Currency = string.Empty;
    }

    private Restaurant(Guid id, string city, string name, string managerId, string currency, DateTimeOffset now)
        : base(id)
    {
        City = city;
        Name = name;
        ManagerId = managerId;
        Currency = currency;
        IsOpen = false;
        RegisteredOnUtc = now;
    }

    /// <summary>The tenant.</summary>
    public string City { get; private set; }

    public string Name { get; private set; }

    /// <summary>The subject of the manager's token. Never a name: a name changes.</summary>
    public string ManagerId { get; private set; }

    /// <summary>ISO 4217. One restaurant, one currency.</summary>
    public string Currency { get; private set; }

    public bool IsOpen { get; private set; }

    /// <summary>The picture of the restaurant: an identifier the Media service issued, never an address.</summary>
    public Guid? PictureId { get; private set; }

    public DateTimeOffset RegisteredOnUtc { get; private set; }

    public IReadOnlyList<MenuItem> Menu => menu;

    public static Restaurant Register(string city, string name, string managerId, string currency, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(managerId);
        CheckRule(new CurrencyMustBeKnown(currency));

        var restaurant = new Restaurant(Guid.CreateVersion7(now), city, name.Trim(), managerId, currency.ToUpperInvariant(), now);
        restaurant.Raise(new RestaurantRegistered(restaurant.Id, city, restaurant.Name, managerId, now));
        return restaurant;
    }

    /// <summary>Adds the item, or changes it when the restaurant already sells something under that code.</summary>
    public MenuItem SetMenuItem(string code, string name, decimal price, bool available, Guid? pictureId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        CheckRule(new PriceMustBePositive(code, price));

        var item = menu.Find(i => i.Code == code);
        if (item is null)
        {
            item = new MenuItem(code, name.Trim(), price, available, pictureId);
            menu.Add(item);
        }
        else
        {
            item.Change(name.Trim(), price, available, pictureId);
        }

        return item;
    }

    public void Open()
    {
        CheckRule(new AnOpenRestaurantSellsSomething(this));
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public void ShowPicture(Guid? pictureId) => PictureId = pictureId;

    /// <summary>What the lines cost now. Nothing is changed: a quote is a reading.</summary>
    public Quote QuoteFor(IReadOnlyList<(string Code, int Quantity)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        CheckRule(new OnlyAnOpenRestaurantQuotes(this));

        var quoted = new List<QuotedLine>(lines.Count);
        foreach (var (code, quantity) in lines)
        {
            var item = menu.Find(i => i.Code == code);
            CheckRule(new AQuotedItemIsOnSale(Name, code, item));
            quoted.Add(new QuotedLine(item!.Code, item.Name, item.Price, quantity));
        }

        return new Quote(Id, Name, City, Currency, quoted);
    }

    public bool IsManagedBy(string subjectId) => string.Equals(ManagerId, subjectId, StringComparison.Ordinal);
}
