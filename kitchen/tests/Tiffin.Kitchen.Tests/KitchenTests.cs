using Microsoft.Extensions.Logging.Abstractions;
using Tiffin.Kitchen.Application.Commands;
using Tiffin.Kitchen.Application.Contracts;
using Tiffin.Kitchen.Application.Events;
using Tiffin.Kitchen.Application.Ports;
using Tiffin.Kitchen.Domain;
using Tiffin.Kitchen.Domain.Events;
using Tiffin.Kitchen.Tests.Support;

namespace Tiffin.Kitchen.Tests;

public sealed class FakeTickets : ITicketRepository
{
    private readonly Dictionary<Guid, Ticket> items = [];

    public IReadOnlyCollection<Ticket> All => items.Values;

    public Task<Ticket?> GetAsync(Guid orderId, string city, CancellationToken cancellationToken) =>
        Task.FromResult(items.GetValueOrDefault(orderId) is { } ticket && ticket.City == city ? ticket : null);

    public void Add(Ticket ticket) => items[ticket.Id] = ticket;
}

public sealed class FakeRestaurants : IKnownRestaurants
{
    private readonly Dictionary<Guid, KnownRestaurant> items = [];

    public Task<KnownRestaurant?> GetAsync(Guid restaurantId, string city, CancellationToken cancellationToken) =>
        Task.FromResult(items.GetValueOrDefault(restaurantId) is { } restaurant && restaurant.City == city ? restaurant : null);

    public void Add(KnownRestaurant restaurant) => items[restaurant.Id] = restaurant;
}

/// <summary>The restaurant's side of an order: a ticket, the manager's word, and who may give it.</summary>
public sealed class KitchenTests
{
    private static readonly Guid DiziSara = Guid.NewGuid();
    private readonly FakeTickets tickets = new();
    private readonly FakeRestaurants restaurants = new();
    private readonly FakeAudit audit = new();
    private readonly FakeClock clock = FakeClock.At2026();

    public KitchenTests() => restaurants.Add(new KnownRestaurant(DiziSara, "tehran", "Dizi Sara", "mina"));

    private async Task<Ticket> Received(Guid? orderId = null, FakeTenant? tenant = null)
    {
        var id = orderId ?? Guid.NewGuid();
        await PreparationRequestedHandler.Handle(
            new PreparationRequested(Guid.NewGuid(), id, "TFN-260928-ABC123", DiziSara, "Dizi Sara", [new PreparationLine("DIZI", "Dizi", 2)], clock.UtcNow),
            tickets, tenant ?? FakeTenant.Tehran(), new FakeUnitOfWork(), clock, NullLogger<PreparationRequested>.Instance, default);
        return tickets.All.Single(t => t.Id == id);
    }

    private Task<MPCore.Application.Results.Result<Application.Views.TicketView>> Accept(Guid orderId, string manager = "mina", FakeTenant? tenant = null, int minutes = 25) =>
        DecideTicketHandler.Handle(
            new AcceptTicket(orderId, minutes), FakeActor.User(manager, "restaurant-manager"), tenant ?? FakeTenant.Tehran(), tickets, restaurants, audit,
            new FakeUnitOfWork(), clock, default);

    [Fact]
    public async Task An_order_becomes_a_ticket_of_the_city_of_its_message_and_the_same_order_again_adds_nothing()
    {
        var ticket = await Received();
        await Received(ticket.OrderId);

        Assert.Single(tickets.All);
        Assert.Equal((TicketStatus.Pending, "tehran", 1), (ticket.Status, ticket.City, ticket.Lines.Count));
    }

    [Fact]
    public async Task The_managers_yes_is_the_answer_to_the_order()
    {
        var ticket = await Received();

        var view = (await Accept(ticket.OrderId)).Value;

        Assert.Equal(("Accepted", 25), (view.Status, view.ReadyInMinutes));
        var answer = Assert.IsType<OrderAccepted>(Assert.Single(ticket.IntegrationEvents));
        Assert.Equal((ticket.OrderId, 25), (answer.OrderId, answer.ReadyInMinutes));
        Assert.Equal("order-accepted", Assert.Single(audit.Records).Action);
    }

    [Fact]
    public async Task The_managers_no_is_the_answer_too_and_says_why()
    {
        var ticket = await Received();

        await DecideTicketHandler.Handle(
            new RejectTicket(ticket.OrderId, " out of lamb "), FakeActor.User("mina", "restaurant-manager"), FakeTenant.Tehran(), tickets, restaurants,
            audit, new FakeUnitOfWork(), clock, default);

        Assert.Equal((TicketStatus.Rejected, "out of lamb"), (ticket.Status, ticket.Reason));
        Assert.Equal("out of lamb", Assert.IsType<OrderRejected>(Assert.Single(ticket.IntegrationEvents)).Reason);
    }

    [Fact]
    public async Task A_ticket_is_decided_once()
    {
        var ticket = await Received();
        await Accept(ticket.OrderId);

        Assert.Equal("TICKET_NOT_PENDING", await Rules.BrokenAsync(() => Accept(ticket.OrderId)));
        Assert.Single(ticket.IntegrationEvents);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(181)]
    public async Task A_promise_is_within_reason(int minutes)
    {
        var ticket = await Received();

        Assert.Equal("PROMISE_OUT_OF_RANGE", await Rules.BrokenAsync(() => Accept(ticket.OrderId, minutes: minutes)));
        Assert.Equal(TicketStatus.Pending, ticket.Status);
    }

    [Fact]
    public async Task A_ticket_of_a_restaurant_somebody_does_not_manage_does_not_exist_for_them()
    {
        var ticket = await Received();

        var byAnotherManager = await Accept(ticket.OrderId, manager: "kemal");
        var fromAnotherCity = await Accept(ticket.OrderId, tenant: FakeTenant.Istanbul());

        Assert.Equal("TICKET_NOT_FOUND", byAnotherManager.FailureDescriptor!.Identity.Code);
        Assert.Equal("TICKET_NOT_FOUND", fromAnotherCity.FailureDescriptor!.Identity.Code);
        Assert.Equal(TicketStatus.Pending, ticket.Status);
        Assert.Empty(audit.Records);
    }

    [Fact]
    public async Task A_cancelled_order_stops_the_kitchen_and_what_has_ended_stays_as_it_ended()
    {
        var accepted = await Received();
        await Accept(accepted.OrderId);
        var rejected = await Received();
        rejected.Reject("closed", clock.UtcNow);

        foreach (var ticket in new[] { accepted, rejected })
        {
            await PreparationCancelledHandler.Handle(
                new PreparationCancelled(Guid.NewGuid(), ticket.OrderId, "no-courier", clock.UtcNow), tickets, FakeTenant.Tehran(), new FakeUnitOfWork(), clock,
                NullLogger<PreparationCancelled>.Instance, default);
        }

        Assert.Equal((TicketStatus.Cancelled, "no-courier"), (accepted.Status, accepted.Reason));
        Assert.Equal((TicketStatus.Rejected, "closed"), (rejected.Status, rejected.Reason));
    }

    [Fact]
    public async Task What_the_stream_says_of_a_restaurant_is_learnt_and_learnt_anew_when_it_changes()
    {
        var lokanta = Guid.NewGuid();
        var known = new FakeRestaurants();

        await RestaurantRegisteredHandler.Handle(
            new RestaurantRegistered(Guid.NewGuid(), lokanta, "istanbul", "Lokanta", "kemal", clock.UtcNow), known, new FakeUnitOfWork(), default);
        await RestaurantRegisteredHandler.Handle(
            new RestaurantRegistered(Guid.NewGuid(), lokanta, "istanbul", "Lokanta Kemal", "deniz", clock.UtcNow), known, new FakeUnitOfWork(), default);

        var restaurant = await known.GetAsync(lokanta, "istanbul", default);
        Assert.Equal(("Lokanta Kemal", "deniz"), (restaurant!.Name, restaurant.ManagerId));
        Assert.Null(await known.GetAsync(lokanta, "tehran", default));
    }

    [Fact]
    public async Task A_message_that_names_no_city_is_a_broken_contract()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Received(tenant: FakeTenant.None()));

        Assert.Contains("names no city", exception.Message, StringComparison.Ordinal);
        Assert.Empty(tickets.All);
    }
}
