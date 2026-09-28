using Microsoft.Extensions.Logging.Abstractions;
using MPCore.Application.Results;
using Tiffin.Tracking.Application.Commands;
using Tiffin.Tracking.Application.Contracts;
using Tiffin.Tracking.Application.Events;
using Tiffin.Tracking.Application.Ports;
using Tiffin.Tracking.Application.Queries;
using Tiffin.Tracking.Application.Views;
using Tiffin.Tracking.Domain;
using Tiffin.Tracking.Tests.Support;

namespace Tiffin.Tracking.Tests;

public sealed class FakeTracking : ITrackingRepository, ITrackingReadModel
{
    private readonly List<TrackedDelivery> deliveries = [];

    public List<Position> Positions { get; } = [];

    public IReadOnlyList<TrackedDelivery> All => deliveries;

    public Task<TrackedDelivery?> GetAsync(Guid orderId, string city, CancellationToken cancellationToken) =>
        Task.FromResult(deliveries.Find(d => d.Id == orderId && d.City == city));

    public Task<TrackedDelivery?> FindAsync(Guid orderId, string city, CancellationToken cancellationToken) => GetAsync(orderId, city, cancellationToken);

    public void Add(TrackedDelivery delivery) => deliveries.Add(delivery);

    public void Log(Position position) => Positions.Add(position);

    public Task<IReadOnlyList<PositionView>> TrailAsync(Guid orderId, string city, int points, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PositionView>>([.. Positions.Where(p => p.OrderId == orderId && p.City == city)
            .OrderByDescending(static p => p.RecordedOnUtc).Take(points).Select(static p => new PositionView(p.Latitude, p.Longitude, p.RecordedOnUtc))]);
}

/// <summary>Where an order is: said by its courier, while it is under way, and shown to who waits for it.</summary>
public sealed class TrackingTests
{
    private readonly FakeTracking tracking = new();
    private readonly FakeClock clock = FakeClock.At2026();

    private async Task<TrackedDelivery> UnderWay(Guid? orderId = null)
    {
        var id = orderId ?? Guid.NewGuid();
        await DeliveryEventsHandler.Handle(
            new DeliveryAssigned(Guid.NewGuid(), id, "TFN-260928-ABC123", "tehran", "sara", "omid", clock.UtcNow), tracking, new FakeUnitOfWork(),
            NullLogger<DeliveryAssigned>.Instance, default);
        return tracking.All.Single(d => d.Id == id);
    }

    private Task<Result<TrackingView>> Report(Guid orderId, double latitude, double longitude, string courier = "omid", FakeTenant? tenant = null)
    {
        clock.UtcNow += TimeSpan.FromSeconds(30);
        return ReportPositionHandler.Handle(
            new ReportPosition(orderId, latitude, longitude), FakeActor.User(courier, "courier"), tenant ?? FakeTenant.Tehran(), tracking,
            new FakeUnitOfWork(), clock, default);
    }

    private Task<Result<TrackingView>> Ask(Guid orderId, string who, FakeTenant? tenant = null, int points = 20) => GetTrackingHandler.Handle(
        new GetTracking(orderId, points), FakeActor.User(who), tenant ?? FakeTenant.Tehran(), tracking, default);

    [Fact]
    public async Task A_delivery_is_learnt_from_the_stream_once_in_the_city_the_event_names()
    {
        var delivery = await UnderWay();
        await UnderWay(delivery.OrderId);

        Assert.Single(tracking.All);
        Assert.Equal(("tehran", "sara", "omid", TrackingStatus.UnderWay), (delivery.City, delivery.CustomerId, delivery.CourierId, delivery.Status));
    }

    [Fact]
    public async Task A_position_is_kept_on_the_delivery_and_as_a_row_of_the_time_series()
    {
        var delivery = await UnderWay();

        await Report(delivery.OrderId, 35.7575, 51.4100);
        await Report(delivery.OrderId, 35.7601, 51.4089);

        Assert.Equal((35.7601, 51.4089, 2), (delivery.LastLatitude, delivery.LastLongitude, delivery.PositionCount));
        Assert.Equal([35.7575, 35.7601], tracking.Positions.Select(static p => p.Latitude));
        Assert.All(tracking.Positions, p => Assert.Equal(("tehran", "omid", delivery.OrderId), (p.City, p.CourierId, p.OrderId)));
    }

    [Fact]
    public async Task Who_waits_and_who_carries_see_the_way_it_came_newest_first_and_nobody_else_sees_anything()
    {
        var delivery = await UnderWay();
        await Report(delivery.OrderId, 35.7575, 51.4100);
        await Report(delivery.OrderId, 35.7601, 51.4089);
        await Report(delivery.OrderId, 35.7632, 51.4071);

        var sara = (await Ask(delivery.OrderId, "sara")).Value;
        var omid = (await Ask(delivery.OrderId, "omid", points: 1)).Value;

        Assert.Equal([35.7632, 35.7601, 35.7575], sara.Trail.Select(static p => p.Latitude));
        Assert.Equal(35.7632, sara.LastSeen!.Latitude);
        Assert.Single(omid.Trail);
        Assert.Equal("DELIVERY_NOT_FOUND", (await Ask(delivery.OrderId, "reza")).FailureDescriptor!.Identity.Code);
        Assert.Equal("DELIVERY_NOT_FOUND", (await Ask(delivery.OrderId, "sara", FakeTenant.Istanbul())).FailureDescriptor!.Identity.Code);
    }

    [Fact]
    public async Task Where_a_delivery_is_is_said_by_its_courier_from_its_city()
    {
        var delivery = await UnderWay();

        Assert.Equal("NOT_YOUR_DELIVERY", await Rules.BrokenAsync(() => Report(delivery.OrderId, 35.7, 51.4, courier: "babak")));
        Assert.Equal("DELIVERY_NOT_FOUND", (await Report(delivery.OrderId, 35.7, 51.4, tenant: FakeTenant.Istanbul())).FailureDescriptor!.Identity.Code);
        Assert.Empty(tracking.Positions);
    }

    [Theory]
    [InlineData(90.0001, 51.4)]
    [InlineData(-91, 51.4)]
    [InlineData(35.7, 180.5)]
    [InlineData(double.NaN, 51.4)]
    public async Task A_position_is_on_earth(double latitude, double longitude)
    {
        var delivery = await UnderWay();

        Assert.Equal("POSITION_NOT_ON_EARTH", await Rules.BrokenAsync(() => Report(delivery.OrderId, latitude, longitude)));
        Assert.Equal(0, delivery.PositionCount);
    }

    [Fact]
    public async Task A_delivery_that_has_arrived_is_not_followed_any_more()
    {
        var delivery = await UnderWay();
        await Report(delivery.OrderId, 35.7575, 51.4100);
        var completed = new DeliveryCompleted(Guid.NewGuid(), delivery.OrderId, "tehran", clock.UtcNow);

        await DeliveryEventsHandler.Handle(completed, tracking, new FakeUnitOfWork(), default);
        await DeliveryEventsHandler.Handle(completed, tracking, new FakeUnitOfWork(), default);

        Assert.Equal((TrackingStatus.Arrived, completed.OccurredOnUtc), (delivery.Status, delivery.ArrivedOnUtc));
        Assert.Equal("DELIVERY_HAS_ARRIVED", await Rules.BrokenAsync(() => Report(delivery.OrderId, 35.77, 51.40)));
        Assert.Single(tracking.Positions);
    }

    [Fact]
    public async Task An_arrival_without_a_beginning_marks_nothing()
    {
        await DeliveryEventsHandler.Handle(
            new DeliveryCompleted(Guid.NewGuid(), Guid.NewGuid(), "tehran", clock.UtcNow), tracking, new FakeUnitOfWork(), default);

        Assert.Empty(tracking.All);
    }

    [Fact]
    public async Task Where_somebody_was_is_not_printed()
    {
        var delivery = await UnderWay();
        await Report(delivery.OrderId, 35.7575, 51.4100);

        var printed = string.Join(' ', tracking.Positions[0], new ReportPosition(delivery.OrderId, 35.7575, 51.41), new PositionView(35.7575, 51.41, clock.UtcNow));

        Assert.DoesNotContain("35.7575", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("51.41", printed, StringComparison.Ordinal);
    }
}
