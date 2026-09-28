using Tiffin.Ordering.Domain;
using Tiffin.Ordering.Domain.Events;
using Tiffin.Ordering.Tests.Support;

namespace Tiffin.Ordering.Tests;

/// <summary>The order, by itself: what may happen to it next, and what may not.</summary>
public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    public static Order Placed(string city = "tehran", string customer = "sara") => Order.Place(
        Guid.CreateVersion7(Now), city, customer, "Sara Ahmadi", Guid.NewGuid(), "Dizi Sara", "IRR",
        new DeliveryAddress("Sara Ahmadi", "+989121234567", "Vanak", "12 Gandhi St"), Guid.NewGuid(),
        [new OrderLine("DIZI", "Dizi", 450_000m, 2), new OrderLine("DOOGH", "Doogh", 60_000m, 1)], Now);

    [Fact]
    public void An_order_is_placed_with_its_total_its_number_and_the_event_that_says_so()
    {
        var order = Placed();

        Assert.Equal(960_000m, order.Total);
        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.StartsWith("TFN-260928-", order.OrderNumber, StringComparison.Ordinal);
        var placed = Assert.IsType<OrderPlaced>(Assert.Single(order.IntegrationEvents));
        Assert.Equal((order.Id, "tehran", 960_000m, 3), (placed.OrderId, placed.City, placed.Total, placed.ItemCount));
    }

    [Fact]
    public void An_order_has_something_in_it() =>
        Assert.Equal("ORDER_EMPTY", Rules.Broken(() => Order.Place(
            Guid.NewGuid(), "tehran", "sara", "Sara", Guid.NewGuid(), "Dizi Sara", "IRR",
            new DeliveryAddress("Sara", "+989121234567", "Vanak", "12 Gandhi St"), Guid.NewGuid(), [], Now)));

    [Fact]
    public void An_order_lives_from_placed_to_delivered()
    {
        var order = Placed();

        order.MarkPaid("PLA-1", Now.AddSeconds(1));
        order.Accept(25, Now.AddSeconds(2));
        order.SendOut("omid", "Omid Sadeghi", Now.AddSeconds(3));
        order.MarkDelivered(Now.AddSeconds(4));

        Assert.Equal(
            [OrderStatus.Placed, OrderStatus.Paid, OrderStatus.Accepted, OrderStatus.OutForDelivery, OrderStatus.Delivered],
            order.History.Select(static h => h.Status));
        Assert.Equal(["OrderPlaced", "OrderOutForDelivery", "OrderDelivered"], order.IntegrationEvents.Select(static e => e.GetType().Name));
    }

    [Fact]
    public void A_step_that_is_not_the_next_one_breaks_a_rule_and_changes_nothing()
    {
        var order = Placed();

        Assert.Equal("ORDER_NOT_AWAITING_RESTAURANT", Rules.Broken(() => order.Accept(20, Now)));
        Assert.Equal("ORDER_NOT_AWAITING_COURIER", Rules.Broken(() => order.SendOut("omid", "Omid", Now)));
        Assert.Equal("ORDER_NOT_ON_ITS_WAY", Rules.Broken(() => order.MarkDelivered(Now)));
        order.MarkPaid("PLA-1", Now);
        Assert.Equal("ORDER_NOT_AWAITING_PAYMENT", Rules.Broken(() => order.MarkPaid("PLA-2", Now)));

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal("PLA-1", order.PaymentReference);
    }

    [Fact]
    public void The_customer_cancels_until_the_restaurant_cooks_and_the_platform_until_a_courier_carries()
    {
        var cooking = Placed();
        cooking.MarkPaid("PLA-1", Now);
        cooking.Accept(20, Now);

        Assert.Equal("ORDER_ALREADY_COOKING", Rules.Broken(() => cooking.CancelByCustomer(null, Now)));
        cooking.Cancel(CancellationReasons.NoCourier, null, Now);
        Assert.Equal(CancellationReasons.NoCourier, cooking.CancellationReason);

        var onItsWay = Placed();
        onItsWay.MarkPaid("PLA-1", Now);
        onItsWay.Accept(20, Now);
        onItsWay.SendOut("omid", "Omid", Now);
        Assert.Equal("ORDER_CANNOT_BE_CANCELLED", Rules.Broken(() => onItsWay.Cancel(CancellationReasons.NoCourier, null, Now)));
    }

    [Fact]
    public void A_cancelled_order_that_was_charged_owes_a_refund_until_the_money_went_back_once()
    {
        var order = Placed();
        order.MarkPaid("PLA-1", Now);
        order.CancelByCustomer("changed my mind", Now);
        Assert.True(order.OwesARefund);

        order.RecordRefund("PLR-1", Now);
        order.RecordRefund("PLR-2", Now);

        Assert.False(order.OwesARefund);
        Assert.Equal("PLR-1", order.RefundReference);
        Assert.Equal(1, order.History.Count(static h => h.Note == "refunded"));
    }

    [Fact]
    public void An_address_prints_its_district_and_never_the_person_or_the_door()
    {
        var address = new DeliveryAddress("Sara Ahmadi", "+989121234567", "Vanak", "12 Gandhi St, unit 4");

        var printed = address.ToString();

        Assert.Contains("Vanak", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("Sara", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("Gandhi", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("912", printed, StringComparison.Ordinal);
        Assert.Equal(address, new DeliveryAddress(" Sara Ahmadi ", "+989121234567", "Vanak", "12 Gandhi St, unit 4"));
    }
}
