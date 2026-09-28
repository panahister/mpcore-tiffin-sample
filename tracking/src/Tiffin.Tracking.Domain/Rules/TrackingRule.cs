using MPCore.Domain.Rules;

namespace Tiffin.Tracking.Domain.Rules;

/// <summary>
/// A business rule of the Tracking service, reported under the <c>tiffin.tracking</c> error domain with a
/// stable code and a message key. The named-rule pattern is Kamil Grzybek's (<i>Modular Monolith with
/// DDD</i>); checking before changing is Vladimir Khorikov's <i>always-valid domain model</i>.
/// </summary>
public abstract class TrackingRule(string code, string messageKey, IReadOnlyDictionary<string, string>? arguments = null)
    : BusinessRule(Domain, code, messageKey, arguments)
{
    public const string Domain = "tiffin.tracking";
}

/// <summary>Rule T1: where a delivery is, is said by the courier who carries it.</summary>
public sealed class OnlyTheCourierOfADeliveryReports(TrackedDelivery delivery, string courierId) : TrackingRule(
    "NOT_YOUR_DELIVERY", "tracking.not_your_delivery", new Dictionary<string, string> { ["order_number"] = delivery.OrderNumber })
{
    public override bool IsBroken() => !string.Equals(delivery.CourierId, courierId, StringComparison.Ordinal);
}

/// <summary>Rule T2: a delivery that has arrived is not reported any more. Where the courier goes afterwards is the courier's own.</summary>
public sealed class OnlyADeliveryUnderWayIsReported(TrackedDelivery delivery) : TrackingRule(
    "DELIVERY_HAS_ARRIVED", "tracking.delivery_has_arrived", new Dictionary<string, string> { ["order_number"] = delivery.OrderNumber })
{
    public override bool IsBroken() => delivery.Status != TrackingStatus.UnderWay;
}

/// <summary>Rule T3: a position is on Earth.</summary>
public sealed class APositionIsOnEarth(double latitude, double longitude) : TrackingRule("POSITION_NOT_ON_EARTH", "tracking.position_not_on_earth")
{
    public override bool IsBroken() =>
        double.IsNaN(latitude) || double.IsNaN(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180;
}
