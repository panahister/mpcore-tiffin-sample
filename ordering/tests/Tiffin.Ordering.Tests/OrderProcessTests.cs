using Microsoft.Extensions.Logging.Abstractions;
using Tiffin.Ordering.Application.Contracts;
using Tiffin.Ordering.Application.Ports;
using Tiffin.Ordering.Application.Process;
using Tiffin.Ordering.Domain;
using Tiffin.Ordering.Tests.Support;

namespace Tiffin.Ordering.Tests;

public sealed class FakeOrders : IOrderRepository
{
    private readonly Dictionary<Guid, Order> items = [];

    public Task<Order?> GetAsync(Guid id, string city, CancellationToken cancellationToken) =>
        Task.FromResult(items.GetValueOrDefault(id) is { } order && order.City == city ? order : null);

    public void Add(Order order) => items[order.Id] = order;

    public Order Holding(Order order)
    {
        Add(order);
        return order;
    }
}

/// <summary>
/// The order process: every answer moves the order one step, or takes back what was done. Every answer can
/// arrive late, twice, or after the order was cancelled, and each of those is a test.
/// </summary>
public sealed class OrderProcessTests
{
    private static readonly Guid Event = Guid.NewGuid();
    private readonly FakeOrders orders = new();
    private readonly RecordingPublisher publisher = new();
    private readonly FakeUnitOfWork unitOfWork = new();
    private readonly FakeClock clock = FakeClock.At2026();
    private readonly FakeTenant tehran = FakeTenant.Tehran();
    private readonly OrderDeadlines deadlines = new(TimeSpan.FromMinutes(10));

    private Order Paid()
    {
        var order = orders.Holding(OrderTests.Placed());
        order.MarkPaid("PLA-1", clock.UtcNow);
        return order;
    }

    private Order Accepted()
    {
        var order = Paid();
        order.Accept(20, clock.UtcNow);
        return order;
    }

    private Task Authorized(Order order, FakeTenant? tenant = null) => OrderProcessHandler.Handle(
        new PaymentAuthorized(Event, order.Id, "PLA-1", clock.UtcNow), orders, tenant ?? tehran, publisher, unitOfWork, deadlines, clock,
        NullLogger<PaymentAuthorized>.Instance, default);

    private Task DeadlinePasses(Order order) => OrderProcessHandler.Handle(
        new RestaurantDeadline(order.Id), orders, tehran, publisher, unitOfWork, deadlines, clock, NullLogger<RestaurantDeadline>.Instance, default);

    [Fact]
    public async Task Asking_the_restaurant_gives_it_a_deadline_in_the_same_transaction()
    {
        var order = orders.Holding(OrderTests.Placed());

        await Authorized(order);

        var (message, delivery) = Assert.Single(publisher.Deliveries, static d => d.Message is RestaurantDeadline);
        Assert.Equal(order.Id, ((RestaurantDeadline)message).OrderId);
        Assert.Equal(TimeSpan.FromMinutes(10), delivery.DeliverAfter);
    }

    [Fact]
    public async Task A_restaurant_that_did_not_answer_in_time_has_the_order_cancelled_stopped_and_paid_back()
    {
        var order = Paid();

        await DeadlinePasses(order);

        Assert.Equal((OrderStatus.Cancelled, CancellationReasons.RestaurantDidNotAnswer), (order.Status, order.CancellationReason));
        Assert.Equal("no answer within 10 minutes", order.History[^1].Note);
        Assert.Equal(order.Id, Assert.Single(publisher.Messages.OfType<PreparationCancelled>()).OrderId);
        Assert.Equal(CancellationReasons.RestaurantDidNotAnswer, Assert.Single(publisher.Messages.OfType<RefundRequested>()).Reason);
    }

    [Fact]
    public async Task A_deadline_that_arrives_after_the_restaurant_answered_does_nothing()
    {
        var accepted = Accepted();
        var refused = Paid();
        await KitchenRejects(refused);
        publisher.Messages.Clear();

        await DeadlinePasses(accepted);
        await DeadlinePasses(refused);

        Assert.Equal(OrderStatus.Accepted, accepted.Status);
        Assert.Equal(CancellationReasons.RestaurantRefused, refused.CancellationReason);
        Assert.Empty(publisher.Messages);
    }

    private Task KitchenAccepts(Order order) => OrderProcessHandler.Handle(
        new KitchenAccepted(Event, order.Id, 25, clock.UtcNow), orders, tehran, publisher, unitOfWork, clock, NullLogger<KitchenAccepted>.Instance, default);

    private Task KitchenRejects(Order order) => OrderProcessHandler.Handle(
        new KitchenRejected(Event, order.Id, "out of lamb", clock.UtcNow), orders, tehran, publisher, unitOfWork, clock,
        NullLogger<KitchenRejected>.Instance, default);

    private Task NoCourier(Order order) => OrderProcessHandler.Handle(
        new CourierUnavailable(Event, order.Id, clock.UtcNow), orders, tehran, publisher, unitOfWork, clock, NullLogger<CourierUnavailable>.Instance, default);

    private Task CourierFound(Order order) => OrderProcessHandler.Handle(
        new CourierAssigned(Event, order.Id, "omid", "Omid Sadeghi", clock.UtcNow), orders, tehran, unitOfWork, clock,
        NullLogger<CourierAssigned>.Instance, default);

    [Fact]
    public async Task A_charge_makes_the_order_paid_and_asks_the_restaurant_with_what_to_cook()
    {
        var order = orders.Holding(OrderTests.Placed());

        await Authorized(order);

        Assert.Equal(OrderStatus.Paid, order.Status);
        var asked = publisher.Single<PreparationRequested>();
        Assert.Equal((order.Id, order.RestaurantId, 2), (asked.OrderId, asked.RestaurantId, asked.Lines.Count));
    }

    [Fact]
    public async Task The_same_charge_reported_twice_asks_the_restaurant_once()
    {
        var order = orders.Holding(OrderTests.Placed());

        await Authorized(order);
        await Authorized(order);

        // One request to cook, and one deadline for it: the second report adds neither.
        Assert.Equal(2, publisher.Messages.Count);
        Assert.Single(publisher.Messages.OfType<PreparationRequested>());
        Assert.Single(publisher.Messages.OfType<RestaurantDeadline>());
    }

    [Fact]
    public async Task A_charge_that_arrives_after_the_customer_cancelled_is_paid_back_and_the_order_stays_cancelled()
    {
        var order = orders.Holding(OrderTests.Placed());
        order.CancelByCustomer(null, clock.UtcNow);

        await Authorized(order);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.True(order.OwesARefund);
        Assert.Equal(CancellationReasons.ByCustomer, publisher.Single<RefundRequested>().Reason);
        Assert.Empty(publisher.Messages.OfType<PreparationRequested>());
    }

    [Fact]
    public async Task A_refusal_of_the_bank_cancels_the_order_and_asks_nobody_anything()
    {
        var order = orders.Holding(OrderTests.Placed());

        await OrderProcessHandler.Handle(
            new PaymentDeclined(Event, order.Id, "INSUFFICIENT_FUNDS", clock.UtcNow), orders, tehran, unitOfWork, clock,
            NullLogger<PaymentDeclined>.Instance, default);

        Assert.Equal((OrderStatus.Cancelled, CancellationReasons.PaymentDeclined), (order.Status, order.CancellationReason));
        Assert.Equal("INSUFFICIENT_FUNDS", order.History[^1].Note);
    }

    [Fact]
    public async Task The_restaurants_yes_asks_for_a_courier_with_where_to_go()
    {
        var order = Paid();

        await KitchenAccepts(order);

        Assert.Equal((OrderStatus.Accepted, 25), (order.Status, order.ReadyInMinutes));
        var asked = publisher.Single<CourierRequested>();
        Assert.Equal(("sara", "Vanak"), (asked.CustomerId, asked.DeliverTo.District));
    }

    [Fact]
    public async Task The_restaurants_no_cancels_the_order_and_gives_the_money_back()
    {
        var order = Paid();

        await KitchenRejects(order);

        Assert.Equal((OrderStatus.Cancelled, CancellationReasons.RestaurantRefused), (order.Status, order.CancellationReason));
        Assert.Equal(["RefundRequested"], publisher.Names);
    }

    [Fact]
    public async Task The_restaurants_yes_for_an_order_the_customer_cancelled_tells_the_kitchen_to_stop()
    {
        var order = Paid();
        order.CancelByCustomer(null, clock.UtcNow);

        await KitchenAccepts(order);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(["PreparationCancelled"], publisher.Names);
    }

    [Fact]
    public async Task No_courier_cancels_the_order_stops_the_kitchen_and_gives_the_money_back()
    {
        var order = Accepted();

        await NoCourier(order);

        Assert.Equal((OrderStatus.Cancelled, CancellationReasons.NoCourier), (order.Status, order.CancellationReason));
        Assert.Equal(["PreparationCancelled", "RefundRequested"], publisher.Names);
    }

    [Fact]
    public async Task No_courier_reported_twice_takes_back_once()
    {
        var order = Accepted();

        await NoCourier(order);
        await NoCourier(order);

        Assert.Equal(2, publisher.Messages.Count);
    }

    [Fact]
    public async Task A_courier_takes_the_order_and_a_second_report_changes_nothing()
    {
        var order = Accepted();

        await CourierFound(order);
        await CourierFound(order);

        Assert.Equal((OrderStatus.OutForDelivery, "Omid Sadeghi"), (order.Status, order.CourierName));
        Assert.Equal(1, order.History.Count(static h => h.Status == OrderStatus.OutForDelivery));
    }

    [Fact]
    public async Task What_the_stream_says_was_delivered_is_delivered_once()
    {
        var order = Accepted();
        await CourierFound(order);
        var completed = new DeliveryCompleted(Event, order.Id, "omid", clock.UtcNow);

        await OrderProcessHandler.Handle(completed, orders, tehran, unitOfWork, clock, NullLogger<DeliveryCompleted>.Instance, default);
        await OrderProcessHandler.Handle(completed, orders, tehran, unitOfWork, clock, NullLogger<DeliveryCompleted>.Instance, default);

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(1, order.History.Count(static h => h.Status == OrderStatus.Delivered));
    }

    [Fact]
    public async Task An_answer_from_another_city_does_not_find_the_order()
    {
        var order = orders.Holding(OrderTests.Placed());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Authorized(order, FakeTenant.Istanbul()));

        Assert.Contains("istanbul", exception.Message, StringComparison.Ordinal);
        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Empty(publisher.Messages);
    }

    [Fact]
    public async Task An_answer_that_names_no_city_is_a_broken_contract_and_is_not_repaired_in_silence()
    {
        var order = orders.Holding(OrderTests.Placed());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Authorized(order, FakeTenant.None()));

        Assert.Contains("names no city", exception.Message, StringComparison.Ordinal);
    }
}
