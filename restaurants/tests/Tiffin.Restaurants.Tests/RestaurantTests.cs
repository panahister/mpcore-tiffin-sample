using MPCore.Application.Querying;
using MPCore.Caching.Abstractions;
using Tiffin.Restaurants.Application;
using Tiffin.Restaurants.Application.Commands;
using Tiffin.Restaurants.Application.Ports;
using Tiffin.Restaurants.Application.Queries;
using Tiffin.Restaurants.Application.Validators;
using Tiffin.Restaurants.Application.Views;
using Tiffin.Restaurants.Domain;
using Tiffin.Restaurants.Domain.Events;
using Tiffin.Restaurants.Tests.Support;

namespace Tiffin.Restaurants.Tests;

/// <summary>Both sides of the store in one list: what a handler changes, a query reads.</summary>
public sealed class FakeRestaurants : IRestaurantRepository, IRestaurantReadModel
{
    private readonly List<Restaurant> items = [];

    public int Reads { get; private set; }

    public Task<Restaurant?> GetAsync(Guid id, string city, CancellationToken cancellationToken) =>
        Task.FromResult(items.Find(r => r.Id == id && r.City == city));

    public Task<bool> NameExistsAsync(string name, string city, CancellationToken cancellationToken) =>
        Task.FromResult(items.Exists(r => r.City == city && r.Name == name));

    public void Add(Restaurant restaurant) => items.Add(restaurant);

    public Task<Page<RestaurantSummary>> ListAsync(string city, bool openOnly, PageRequest page, CancellationToken cancellationToken)
    {
        var found = items.Where(r => r.City == city && (!openOnly || r.IsOpen))
            .Select(static r => new RestaurantSummary(r.Id, r.Name, r.City, r.Currency, r.IsOpen, r.PictureId)).ToList();
        return Task.FromResult(new Page<RestaurantSummary>(found, page.Number, page.Size, found.Count));
    }

    public Task<MenuView?> MenuAsync(Guid restaurantId, string city, CancellationToken cancellationToken)
    {
        Reads++;
        return Task.FromResult(items.Find(r => r.Id == restaurantId && r.City == city) is { } restaurant ? RestaurantViews.MenuOf(restaurant) : null);
    }

    public Task<Restaurant?> ForQuoteAsync(Guid restaurantId, CancellationToken cancellationToken) => Task.FromResult(items.Find(r => r.Id == restaurantId));
}

/// <summary>A cache that keeps everything until it is told to forget: what a test needs to see whether it was asked, and what it was told.</summary>
public sealed class FakeCache : ICache, IReadThroughCache
{
    private readonly Dictionary<string, object?> items = [];

    public IReadOnlyCollection<string> Keys => items.Keys;

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(items.TryGetValue(key, out var value) ? (T?)value : default);

    public Task SetAsync<T>(string key, T value, TimeSpan? absoluteExpiration = null, CancellationToken cancellationToken = default)
    {
        items[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        items.Remove(key);
        return Task.CompletedTask;
    }

    public async ValueTask<T> GetOrCreateAsync<T>(
        string key, Func<CancellationToken, ValueTask<T>> factory, TimeSpan? absoluteExpiration = null, CancellationToken cancellationToken = default)
    {
        if (!items.TryGetValue(key, out var value))
        {
            items[key] = value = await factory(cancellationToken);
        }

        return (T)value!;
    }
}

/// <summary>A restaurant of one city, its menu, and what an order costs there.</summary>
public sealed class RestaurantTests
{
    private readonly FakeRestaurants restaurants = new();
    private readonly FakeCache cache = new();
    private readonly FakeAudit audit = new();
    private readonly FakeClock clock = FakeClock.At2026();
    private readonly FakeActor mina = FakeActor.User("mina", "restaurant-manager");

    private async Task<MenuView> Registered(string name = "Dizi Sara", FakeActor? manager = null, FakeTenant? tenant = null) =>
        (await RegisterRestaurantHandler.Handle(
            new RegisterRestaurant(name, "irr"), manager ?? mina, tenant ?? FakeTenant.Tehran(), restaurants, audit, new FakeUnitOfWork(), clock, default)).Value;

    private Task<MPCore.Application.Results.Result<MenuView>> Item(Guid restaurant, string code, decimal price, bool available = true, FakeActor? by = null, FakeTenant? tenant = null, Guid? pictureId = null) =>
        ManageRestaurantHandler.Handle(
            new SetMenuItem(restaurant, code, code, price, available, pictureId), by ?? mina, tenant ?? FakeTenant.Tehran(), restaurants, cache, new FakeUnitOfWork(), default);

    private Task<MPCore.Application.Results.Result<MenuView>> Open(Guid restaurant, bool open = true) => ManageRestaurantHandler.Handle(
        new SetOpen(restaurant, open), mina, FakeTenant.Tehran(), restaurants, cache, new FakeUnitOfWork(), default);

    private Task<MPCore.Application.Results.Result<QuoteView>> Quote(Guid restaurant, params (string Code, int Quantity)[] lines) => QuoteOrderHandler.Handle(
        new QuoteOrder(restaurant, [.. lines.Select(static l => new QuoteOrderLine(l.Code, l.Quantity))]), restaurants, default);

    [Fact]
    public async Task A_restaurant_is_registered_in_the_city_of_its_manager_closed_and_says_so_on_the_stream()
    {
        var menu = await Registered();

        Assert.Equal(("tehran", "IRR", false), (menu.City, menu.Currency, menu.IsOpen));
        var restaurant = await restaurants.GetAsync(menu.RestaurantId, "tehran", default);
        var registered = Assert.IsType<RestaurantRegistered>(Assert.Single(restaurant!.IntegrationEvents));
        Assert.Equal(("tehran", "mina", "Dizi Sara"), (registered.City, registered.ManagerId, registered.RestaurantName));
        Assert.Equal("tehran", Assert.Single(audit.Records).Metadata!["city"]);
    }

    [Fact]
    public async Task A_name_is_taken_once_in_a_city_and_free_in_another()
    {
        await Registered();

        var again = await RegisterRestaurantHandler.Handle(
            new RegisterRestaurant(" Dizi Sara ", "IRR"), mina, FakeTenant.Tehran(), restaurants, audit, new FakeUnitOfWork(), clock, default);
        var elsewhere = await Registered(manager: FakeActor.User("kemal", "restaurant-manager"), tenant: FakeTenant.Istanbul());

        Assert.Equal("RESTAURANT_NAME_TAKEN", again.FailureDescriptor!.Identity.Code);
        Assert.Equal("istanbul", elsewhere.City);
    }

    [Fact]
    public async Task A_restaurant_opens_with_something_to_sell()
    {
        var menu = await Registered();

        Assert.Equal("MENU_EMPTY", await Rules.BrokenAsync(() => Open(menu.RestaurantId)));
        await Item(menu.RestaurantId, "DIZI", 450_000m, available: false);
        Assert.Equal("MENU_EMPTY", await Rules.BrokenAsync(() => Open(menu.RestaurantId)));
        await Item(menu.RestaurantId, "DIZI", 450_000m);

        Assert.True((await Open(menu.RestaurantId)).Value.IsOpen);
    }

    [Fact]
    public async Task A_price_is_more_than_nothing_and_a_currency_is_one_the_platform_settles()
    {
        var menu = await Registered();

        Assert.Equal("PRICE_NOT_POSITIVE", await Rules.BrokenAsync(() => Item(menu.RestaurantId, "DIZI", 0m)));
        Assert.Equal("CURRENCY_UNKNOWN", Rules.Broken(() => Restaurant.Register("tehran", "X", "mina", "XXX", clock.UtcNow)));
    }

    [Fact]
    public async Task A_menu_item_keeps_the_media_identifier_of_its_food_picture_when_later_fields_change()
    {
        var menu = await Registered();
        var pictureId = Guid.NewGuid();

        var pictured = (await Item(menu.RestaurantId, "DIZI", 450_000m, pictureId: pictureId)).Value;
        var renamed = (await Item(menu.RestaurantId, "DIZI", 500_000m)).Value;

        Assert.Equal(pictureId, Assert.Single(pictured.Items).PictureId);
        Assert.Equal((500_000m, pictureId), (Assert.Single(renamed.Items).Price, Assert.Single(renamed.Items).PictureId));
    }

    [Fact]
    public async Task Only_the_manager_changes_a_restaurant_and_from_another_city_it_does_not_exist()
    {
        var menu = await Registered();

        var byAnother = await Item(menu.RestaurantId, "DIZI", 1m, by: FakeActor.User("ali", "restaurant-manager"));
        var fromElsewhere = await Item(menu.RestaurantId, "DIZI", 1m, by: FakeActor.User("kemal", "restaurant-manager"), tenant: FakeTenant.Istanbul());

        Assert.Equal("NOT_THE_MANAGER", byAnother.FailureDescriptor!.Identity.Code);
        Assert.Equal("RESTAURANT_NOT_FOUND", fromElsewhere.FailureDescriptor!.Identity.Code);
        Assert.Empty((await restaurants.GetAsync(menu.RestaurantId, "tehran", default))!.Menu);
    }

    [Fact]
    public async Task A_quote_is_what_the_lines_cost_now_in_the_restaurants_currency_and_names_its_city()
    {
        var menu = await Registered();
        await Item(menu.RestaurantId, "DIZI", 450_000m);
        await Item(menu.RestaurantId, "DOOGH", 60_000m);
        await Open(menu.RestaurantId);

        var quote = (await Quote(menu.RestaurantId, ("DIZI", 2), ("DOOGH", 1))).Value;
        await Item(menu.RestaurantId, "DIZI", 500_000m);
        var later = (await Quote(menu.RestaurantId, ("DIZI", 2), ("DOOGH", 1))).Value;

        Assert.Equal((960_000m, "IRR", "tehran"), (quote.Total, quote.Currency, quote.City));
        Assert.Equal(1_060_000m, later.Total);
    }

    [Fact]
    public async Task A_closed_restaurant_quotes_nothing_and_what_is_sold_out_or_unknown_is_not_quoted()
    {
        var menu = await Registered();
        await Item(menu.RestaurantId, "DIZI", 450_000m);
        Assert.Equal("RESTAURANT_CLOSED", await Rules.BrokenAsync(() => Quote(menu.RestaurantId, ("DIZI", 1))));

        await Open(menu.RestaurantId);
        await Item(menu.RestaurantId, "DOOGH", 60_000m, available: false);

        Assert.Equal("ITEM_NOT_ON_SALE", await Rules.BrokenAsync(() => Quote(menu.RestaurantId, ("DOOGH", 1))));
        Assert.Equal("ITEM_NOT_ON_SALE", await Rules.BrokenAsync(() => Quote(menu.RestaurantId, ("KABAB", 1))));
        Assert.Equal("RESTAURANT_NOT_FOUND", (await Quote(Guid.NewGuid(), ("DIZI", 1))).FailureDescriptor!.Identity.Code);
    }

    [Fact]
    public async Task A_menu_is_read_through_the_cache_under_a_key_of_its_city_and_forgotten_when_it_changes()
    {
        var menu = await Registered();
        await Item(menu.RestaurantId, "DIZI", 450_000m);
        Task<MPCore.Application.Results.Result<MenuView>> Read() =>
            RestaurantQueriesHandler.Handle(new GetMenu(menu.RestaurantId, "tehran"), FakeTenant.None(), restaurants, cache, default);

        await Read();
        await Read();
        Assert.Equal(1, restaurants.Reads);
        Assert.Equal($"menu:tehran:{menu.RestaurantId:N}", Assert.Single(cache.Keys));

        await Item(menu.RestaurantId, "DIZI", 500_000m);
        Assert.Empty(cache.Keys);
        Assert.Equal(500_000m, Assert.Single((await Read()).Value.Items).Price);
        Assert.Equal(2, restaurants.Reads);
    }

    [Fact]
    public async Task The_city_of_a_token_wins_over_the_city_that_is_asked_for()
    {
        var menu = await Registered();

        var asElif = await RestaurantQueriesHandler.Handle(new GetMenu(menu.RestaurantId, "tehran"), FakeTenant.Istanbul(), restaurants, cache, default);
        var list = await RestaurantQueriesHandler.Handle(new ListRestaurants("tehran"), FakeTenant.Istanbul(), restaurants, default);
        var nowhere = await RestaurantQueriesHandler.Handle(new ListRestaurants(null), FakeTenant.None(), restaurants, default);

        Assert.Equal("RESTAURANT_NOT_FOUND", asElif.FailureDescriptor!.Identity.Code);
        Assert.Empty(list.Value.Items);
        Assert.Equal("CITY_REQUIRED", nowhere.FailureDescriptor!.Identity.Code);
    }

    [Fact]
    public void A_quote_names_an_item_once_and_asks_for_between_one_and_ninety_nine()
    {
        var validator = new QuoteOrderValidator();
        var id = Guid.NewGuid();

        Assert.True(validator.Validate(new QuoteOrder(id, [new QuoteOrderLine("DIZI", 2)])).IsValid);
        Assert.False(validator.Validate(new QuoteOrder(id, [new QuoteOrderLine("DIZI", 1), new QuoteOrderLine("DIZI", 1)])).IsValid);
        Assert.False(validator.Validate(new QuoteOrder(id, [new QuoteOrderLine("DIZI", 0)])).IsValid);
        Assert.False(validator.Validate(new QuoteOrder(id, [])).IsValid);
        Assert.False(new SetMenuItemValidator().Validate(new SetMenuItem(id, "DIZI; DROP", "Dizi", 1m, true)).IsValid);
    }
}
