using MPCore.Domain.Rules;

namespace Tiffin.Dispatch.Domain.Rules;

/// <summary>
/// A business rule of the Dispatch service, reported under the <c>tiffin.dispatch</c> error domain with a
/// stable code and a message key. The named-rule pattern is Kamil Grzybek's (<i>Modular Monolith with
/// DDD</i>); checking before changing is Vladimir Khorikov's <i>always-valid domain model</i>.
/// </summary>
public abstract class DispatchRule(string code, string messageKey, IReadOnlyDictionary<string, string>? arguments = null)
    : BusinessRule(Domain, code, messageKey, arguments)
{
    public const string Domain = "tiffin.dispatch";
}

/// <summary>Rule D1: a courier carries one order at a time.</summary>
public sealed class ACourierCarriesOneOrder(Courier courier) : DispatchRule("COURIER_NOT_FREE", "dispatch.courier_not_free")
{
    public override bool IsBroken() => !courier.IsFree;
}

/// <summary>Rule D2: a courier goes home after handing over what they carry.</summary>
public sealed class ACourierFinishesWhatTheyCarry(Courier courier) : DispatchRule("COURIER_IS_CARRYING", "dispatch.courier_is_carrying")
{
    public override bool IsBroken() => courier.CarryingOrderId is not null;
}

/// <summary>Rule D3: a delivery is completed by the courier who carries it.</summary>
public sealed class OnlyTheCourierOfADeliveryCompletesIt(Delivery delivery, string courierId) : DispatchRule(
    "NOT_YOUR_DELIVERY", "dispatch.not_your_delivery", new Dictionary<string, string> { ["order_number"] = delivery.OrderNumber })
{
    public override bool IsBroken() => !string.Equals(delivery.CourierId, courierId, StringComparison.Ordinal);
}

/// <summary>Rule D4: a delivery is completed once.</summary>
public sealed class ADeliveryIsCompletedOnce(Delivery delivery) : DispatchRule(
    "DELIVERY_ALREADY_COMPLETED", "dispatch.already_completed", new Dictionary<string, string> { ["order_number"] = delivery.OrderNumber })
{
    public override bool IsBroken() => delivery.Status == DeliveryStatus.Completed;
}
