namespace Tiffin.Ordering.Domain;

/// <summary>Where an order is on its way from the customer's phone to the customer's door.</summary>
public enum OrderStatus
{
    /// <summary>Accepted by the platform; the charge has been asked for.</summary>
    Placed = 0,

    /// <summary>The card was charged; the restaurant has been asked.</summary>
    Paid = 1,

    /// <summary>The restaurant cooks; a courier has been asked for.</summary>
    Accepted = 2,

    /// <summary>A courier carries it.</summary>
    OutForDelivery = 3,

    /// <summary>The customer has it.</summary>
    Delivered = 4,

    /// <summary>It will not arrive. What was charged goes back.</summary>
    Cancelled = 5
}

/// <summary>Why an order was cancelled. The codes are part of the <c>order-cancelled</c> contract.</summary>
public static class CancellationReasons
{
    public const string ByCustomer = "cancelled-by-customer";
    public const string PaymentDeclined = "payment-declined";
    public const string RestaurantRefused = "restaurant-refused";
    public const string NoCourier = "no-courier";
    public const string StepGivenUp = "step-given-up";
}
