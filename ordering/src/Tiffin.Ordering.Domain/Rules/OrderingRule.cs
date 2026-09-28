using MPCore.Domain.Rules;

namespace Tiffin.Ordering.Domain.Rules;

/// <summary>
/// A business rule of the Ordering service. Every rule has a stable code, is reported under the
/// <c>tiffin.ordering</c> error domain, and names a message key the transport renders in the caller's
/// language.
/// </summary>
/// <remarks>
/// The named-rule pattern comes from Kamil Grzybek's <i>Modular Monolith with DDD</i>. The aggregate checks
/// a rule before it changes state, so an aggregate is never invalid: Vladimir Khorikov's <i>always-valid
/// domain model</i>.
/// </remarks>
public abstract class OrderingRule(string code, string messageKey, IReadOnlyDictionary<string, string>? arguments = null)
    : BusinessRule(Domain, code, messageKey, arguments)
{
    public const string Domain = "tiffin.ordering";

    protected static Dictionary<string, string> Naming(Order order) =>
        new() { ["order_number"] = order.OrderNumber, ["status"] = order.Status.ToString() };
}

/// <summary>Rule O1: an order has something in it.</summary>
public sealed class AnOrderHasLines(int lineCount) : OrderingRule("ORDER_EMPTY", "ordering.order_empty")
{
    public override bool IsBroken() => lineCount == 0;
}

/// <summary>Rule O2: an order is paid once, and only while it waits for its charge.</summary>
public sealed class OnlyAPlacedOrderIsPaid(Order order) : OrderingRule("ORDER_NOT_AWAITING_PAYMENT", "ordering.not_awaiting_payment", Naming(order))
{
    public override bool IsBroken() => !order.CanMarkPaid;
}

/// <summary>Rule O3: a restaurant accepts an order that was paid.</summary>
public sealed class OnlyAPaidOrderIsAccepted(Order order) : OrderingRule("ORDER_NOT_AWAITING_RESTAURANT", "ordering.not_awaiting_restaurant", Naming(order))
{
    public override bool IsBroken() => !order.CanAccept;
}

/// <summary>Rule O4: a courier takes an order the restaurant accepted.</summary>
public sealed class OnlyAnAcceptedOrderLeaves(Order order) : OrderingRule("ORDER_NOT_AWAITING_COURIER", "ordering.not_awaiting_courier", Naming(order))
{
    public override bool IsBroken() => !order.CanSendOut;
}

/// <summary>Rule O5: what is delivered was on its way.</summary>
public sealed class OnlyAnOrderOnItsWayIsDelivered(Order order) : OrderingRule("ORDER_NOT_ON_ITS_WAY", "ordering.not_on_its_way", Naming(order))
{
    public override bool IsBroken() => !order.CanDeliver;
}

/// <summary>Rule O6: an order that has left the restaurant, or has ended, is not cancelled.</summary>
public sealed class AnOrderOnItsWayIsNotCancelled(Order order) : OrderingRule("ORDER_CANNOT_BE_CANCELLED", "ordering.cannot_be_cancelled", Naming(order))
{
    public override bool IsBroken() => !order.CanCancel;
}

/// <summary>Rule O7: a customer cancels until the restaurant has started to cook.</summary>
public sealed class ACustomerCancelsBeforeTheRestaurantCooks(Order order) : OrderingRule("ORDER_ALREADY_COOKING", "ordering.already_cooking", Naming(order))
{
    public override bool IsBroken() => !order.CanBeCancelledByCustomer;
}
