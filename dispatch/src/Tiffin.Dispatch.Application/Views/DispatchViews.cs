using Tiffin.Dispatch.Domain;

namespace Tiffin.Dispatch.Application.Views;

public sealed record CourierView(string CourierId, string Name, string City, bool IsOnDuty, Guid? CarryingOrderId);

/// <summary>What the courier needs to find the door. Shown to the courier of the delivery only.</summary>
public sealed record DeliveryView(
    Guid OrderId, string OrderNumber, string Status, string RestaurantName, string Recipient, string Phone, string District, string Line,
    DateTimeOffset AssignedOnUtc, DateTimeOffset? CompletedOnUtc)
{
    public override string ToString() => $"{nameof(DeliveryView)} {{ OrderNumber = {OrderNumber}, Status = {Status} }}";
}

public static class DispatchViews
{
    public static CourierView Of(Courier courier)
    {
        ArgumentNullException.ThrowIfNull(courier);
        return new CourierView(courier.Id, courier.Name, courier.City, courier.IsOnDuty, courier.CarryingOrderId);
    }

    public static DeliveryView Of(Delivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var to = delivery.Destination;
        return new DeliveryView(
            delivery.OrderId, delivery.OrderNumber, delivery.Status.ToString(), delivery.RestaurantName, to.Recipient, to.Phone, to.District,
            to.Line, delivery.AssignedOnUtc, delivery.CompletedOnUtc);
    }
}
