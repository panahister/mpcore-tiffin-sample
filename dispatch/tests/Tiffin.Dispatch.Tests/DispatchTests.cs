using Microsoft.Extensions.Logging.Abstractions;
using Tiffin.Dispatch.Application.Commands;
using Tiffin.Dispatch.Application.Contracts;
using Tiffin.Dispatch.Application.Events;
using Tiffin.Dispatch.Application.Ports;
using Tiffin.Dispatch.Domain;
using Tiffin.Dispatch.Domain.Events;
using Tiffin.Dispatch.Tests.Support;

namespace Tiffin.Dispatch.Tests;

public sealed class FakeCouriers : ICourierRepository
{
    private readonly List<Courier> items = [];

    public Task<Courier?> GetAsync(string courierId, string city, CancellationToken cancellationToken) =>
        Task.FromResult(items.Find(c => c.Id == courierId && c.City == city));

    public Task<Courier?> LongestFreeAsync(string city, CancellationToken cancellationToken) =>
        Task.FromResult(items.Where(c => c.City == city && c.IsFree).OrderBy(static c => c.FreeSinceUtc).FirstOrDefault());

    public void Add(Courier courier) => items.Add(courier);
}

public sealed class FakeDeliveries : IDeliveryRepository
{
    private readonly Dictionary<Guid, Delivery> items = [];

    public IReadOnlyCollection<Delivery> All => items.Values;

    public Task<Delivery?> GetAsync(Guid orderId, string city, CancellationToken cancellationToken) =>
        Task.FromResult(items.GetValueOrDefault(orderId) is { } delivery && delivery.City == city ? delivery : null);

    public void Add(Delivery delivery) => items[delivery.Id] = delivery;
}

/// <summary>Who carries what: a courier carries one order, and the one who has waited longest is asked first.</summary>
public sealed class DispatchTests
{
    private readonly FakeCouriers couriers = new();
    private readonly FakeDeliveries deliveries = new();
    private readonly RecordingPublisher publisher = new();
    private readonly FakeClock clock = FakeClock.At2026();

    private async Task OnDuty(string courier, string city = "tehran")
    {
        await CourierCommandsHandler.Handle(
            new GoOnDuty(), FakeActor.User(courier, "courier"), new FakeTenant(city), couriers, new FakeUnitOfWork(), clock, default);
        clock.UtcNow += TimeSpan.FromMinutes(1);
    }

    private async Task<Guid> Requested(string city = "tehran", Guid? orderId = null)
    {
        var id = orderId ?? Guid.NewGuid();
        await CourierRequestedHandler.Handle(
            new CourierRequested(
                Guid.NewGuid(), id, "TFN-260928-ABC123", "sara", "Dizi Sara", new CourierDestination("Sara Ahmadi", "+989121234567", "Vanak", "12 Gandhi St"),
                clock.UtcNow),
            couriers, deliveries, new FakeTenant(city), publisher, new FakeUnitOfWork(), clock, NullLogger<CourierRequested>.Instance, default);
        return id;
    }

    private Task<MPCore.Application.Results.Result<Application.Views.DeliveryView>> Complete(Guid orderId, string courier, string city = "tehran") =>
        CourierCommandsHandler.Handle(
            new CompleteDelivery(orderId), FakeActor.User(courier, "courier"), new FakeTenant(city), couriers, deliveries, new FakeUnitOfWork(), clock, default);

    [Fact]
    public async Task The_courier_who_has_waited_longest_is_given_the_order_and_both_the_order_and_the_stream_are_told()
    {
        await OnDuty("omid");
        await OnDuty("babak");

        var order = await Requested();

        var delivery = Assert.Single(deliveries.All);
        Assert.Equal(("omid", "tehran", DeliveryStatus.Assigned), (delivery.CourierId, delivery.City, delivery.Status));
        Assert.Equal(["CourierAssigned", "DeliveryAssigned"], delivery.IntegrationEvents.Select(static e => e.GetType().Name));
        Assert.Equal(order, (await couriers.GetAsync("omid", "tehran", default))!.CarryingOrderId);
    }

    [Fact]
    public async Task A_courier_carries_one_order_and_the_next_order_goes_to_the_next_courier_or_to_nobody()
    {
        await OnDuty("omid");
        await OnDuty("babak");

        await Requested();
        await Requested();
        var third = await Requested();

        Assert.Equal(["babak", "omid"], deliveries.All.Select(static d => d.CourierId).Order());
        Assert.Equal(third, publisher.Single<CourierUnavailable>().OrderId);
    }

    [Fact]
    public async Task A_request_that_arrives_twice_finds_its_delivery_and_repeats_the_answer()
    {
        await OnDuty("omid");
        var order = await Requested();

        await Requested(orderId: order);

        Assert.Single(deliveries.All);
        Assert.Equal(("omid", order), (publisher.Single<CourierAssigned>().CourierId, publisher.Single<CourierAssigned>().OrderId));
    }

    [Fact]
    public async Task A_courier_of_one_city_is_never_given_an_order_of_another()
    {
        await OnDuty("omid", "tehran");

        var order = await Requested("istanbul");

        Assert.Empty(deliveries.All);
        Assert.Equal(order, publisher.Single<CourierUnavailable>().OrderId);
    }

    [Fact]
    public async Task Handing_over_completes_the_delivery_tells_the_stream_and_frees_the_courier_at_the_end_of_the_line()
    {
        await OnDuty("omid");
        await OnDuty("babak");
        var first = await Requested();
        var delivery = deliveries.All.Single();
        delivery.ClearEvents();
        clock.UtcNow += TimeSpan.FromMinutes(20);

        var view = (await Complete(first, "omid")).Value;
        await Requested();
        await Requested();

        Assert.Equal("Completed", view.Status);
        Assert.Equal(first, Assert.IsType<DeliveryCompleted>(Assert.Single(delivery.IntegrationEvents)).OrderId);
        // babak has waited since before omid handed over, so babak is asked first, and omid after him.
        Assert.Equal(["omid", "babak", "omid"], deliveries.All.OrderBy(static d => d.AssignedOnUtc).ThenBy(static d => d.CourierId == "babak" ? 0 : 1).Select(static d => d.CourierId));
    }

    [Fact]
    public async Task A_delivery_is_completed_by_who_carries_it_and_once()
    {
        await OnDuty("omid");
        await OnDuty("babak");
        var order = await Requested();

        Assert.Equal("NOT_YOUR_DELIVERY", await Rules.BrokenAsync(() => Complete(order, "babak")));
        await Complete(order, "omid");
        Assert.Equal("DELIVERY_ALREADY_COMPLETED", await Rules.BrokenAsync(() => Complete(order, "omid")));
        Assert.Equal("DELIVERY_NOT_FOUND", (await Complete(Guid.NewGuid(), "omid")).FailureDescriptor!.Identity.Code);
    }

    [Fact]
    public async Task A_courier_goes_home_after_handing_over_what_they_carry()
    {
        await OnDuty("omid");
        var order = await Requested();
        Task<MPCore.Application.Results.Result<Application.Views.CourierView>> GoHome() => CourierCommandsHandler.Handle(
            new GoOffDuty(), FakeActor.User("omid", "courier"), FakeTenant.Tehran(), couriers, new FakeUnitOfWork(), default);

        Assert.Equal("COURIER_IS_CARRYING", await Rules.BrokenAsync(GoHome));
        await Complete(order, "omid");
        var home = (await GoHome()).Value;

        Assert.False(home.IsOnDuty);
        await Requested();
        Assert.Single(publisher.Messages.OfType<CourierUnavailable>());
    }

    [Fact]
    public async Task Who_the_courier_is_and_where_comes_from_the_token()
    {
        var anonymous = await CourierCommandsHandler.Handle(
            new GoOnDuty(), FakeActor.Anonymous(), FakeTenant.Tehran(), couriers, new FakeUnitOfWork(), clock, default);
        var nowhere = await CourierCommandsHandler.Handle(
            new GoOnDuty(), FakeActor.User("omid", "courier"), FakeTenant.None(), couriers, new FakeUnitOfWork(), clock, default);

        Assert.Equal("COURIER_REQUIRED", anonymous.FailureDescriptor!.Identity.Code);
        Assert.Equal("COURIER_REQUIRED", nowhere.FailureDescriptor!.Identity.Code);
    }

    [Fact]
    public void A_destination_and_a_request_print_the_district_and_never_the_person_or_the_door()
    {
        var destination = new CourierDestination("Sara Ahmadi", "+989121234567", "Vanak", "12 Gandhi St");
        var request = new CourierRequested(Guid.NewGuid(), Guid.NewGuid(), "TFN-1", "sara", "Dizi Sara", destination, DateTimeOffset.UtcNow);

        foreach (var printed in new[] { destination.ToString(), request.ToString(), new Destination("Sara Ahmadi", "+989121234567", "Vanak", "12 Gandhi St").ToString() })
        {
            Assert.DoesNotContain("Sara Ahmadi", printed, StringComparison.Ordinal);
            Assert.DoesNotContain("Gandhi", printed, StringComparison.Ordinal);
            Assert.DoesNotContain("912", printed, StringComparison.Ordinal);
        }
    }
}
