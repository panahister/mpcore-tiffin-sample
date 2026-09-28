using System.Globalization;
using MPCore.Domain.Model;
using Tiffin.Ordering.Domain.Events;
using Tiffin.Ordering.Domain.Rules;

namespace Tiffin.Ordering.Domain;

/// <summary>An order: what a customer of one city asked of one restaurant, and how far it has come.</summary>
/// <remarks>
/// <para>
/// <b>The order is the state of its own process.</b> Payments, the Kitchen and Dispatch each answer in
/// their own time, and every answer can arrive late, twice, or after the order was cancelled. So the order
/// says what may happen next (<see cref="CanMarkPaid"/>, <see cref="CanAccept"/>, ...), the process asks
/// before it acts, and a transition that is not allowed breaks a rule instead of corrupting the order.
/// </para>
/// <para>
/// <b>The order holds copies, never references that have to be followed.</b> The lines carry the name and
/// the price the restaurant quoted; the restaurant's name is kept with its identifier. An order can be read
/// and explained while every other service is down. Vaughn Vernon: reference other aggregates by identity,
/// and copy what must not change (<i>Implementing Domain-Driven Design</i>, 2013).
/// </para>
/// </remarks>
public sealed class Order : AggregateRoot<Guid>
{
    private readonly List<OrderLine> lines = [];
    private readonly List<OrderHistoryEntry> history = [];

    private Order()
    {
        OrderNumber = string.Empty;
        City = string.Empty;
        CustomerId = string.Empty;
        CustomerName = string.Empty;
        RestaurantName = string.Empty;
        Currency = string.Empty;
        DeliverTo = null!;
    }

    private Order(
        Guid id, string city, string customerId, string customerName, Guid restaurantId, string restaurantName, string currency,
        DeliveryAddress deliverTo, Guid paymentIntentId, IEnumerable<OrderLine> orderLines, DateTimeOffset now)
        : base(id)
    {
        OrderNumber = NumberOf(id, now);
        City = city;
        CustomerId = customerId;
        CustomerName = customerName;
        RestaurantId = restaurantId;
        RestaurantName = restaurantName;
        Currency = currency;
        DeliverTo = deliverTo;
        PaymentIntentId = paymentIntentId;
        lines.AddRange(orderLines);
        Total = lines.Sum(static l => l.LineTotal);
        PlacedOnUtc = now;
        Move(OrderStatus.Placed, now, null);
    }

    /// <summary>The number people quote.</summary>
    public string OrderNumber { get; private set; }

    /// <summary>The tenant.</summary>
    public string City { get; private set; }

    /// <summary>The subject of the customer's token.</summary>
    public string CustomerId { get; private set; }

    public string CustomerName { get; private set; }

    public Guid RestaurantId { get; private set; }

    public string RestaurantName { get; private set; }

    public string Currency { get; private set; }

    public decimal Total { get; private set; }

    public DeliveryAddress DeliverTo { get; private set; }

    /// <summary>The reference Payments gave for the customer's card. The card's token is never here.</summary>
    public Guid PaymentIntentId { get; private set; }

    public OrderStatus Status { get; private set; }

    public string? CancellationReason { get; private set; }

    public string? PaymentReference { get; private set; }

    public string? RefundReference { get; private set; }

    public string? CourierId { get; private set; }

    public string? CourierName { get; private set; }

    public int? ReadyInMinutes { get; private set; }

    public DateTimeOffset PlacedOnUtc { get; private set; }

    public IReadOnlyList<OrderLine> Lines => lines;

    public IReadOnlyList<OrderHistoryEntry> History => history;

    public bool IsCancelled => Status == OrderStatus.Cancelled;

    public bool CanMarkPaid => Status == OrderStatus.Placed;

    public bool CanAccept => Status == OrderStatus.Paid;

    public bool CanSendOut => Status == OrderStatus.Accepted;

    public bool CanDeliver => Status == OrderStatus.OutForDelivery;

    /// <summary>The platform may cancel until a courier carries the order.</summary>
    public bool CanCancel => Status is OrderStatus.Placed or OrderStatus.Paid or OrderStatus.Accepted;

    /// <summary>The customer may cancel until the restaurant cooks.</summary>
    public bool CanBeCancelledByCustomer => Status is OrderStatus.Placed or OrderStatus.Paid;

    /// <summary>Whether money was taken that has not gone back.</summary>
    public bool OwesARefund => PaymentReference is not null && RefundReference is null;

    public static Order Place(
        Guid id, string city, string customerId, string customerName, Guid restaurantId, string restaurantName, string currency,
        DeliveryAddress deliverTo, Guid paymentIntentId, IReadOnlyList<OrderLine> orderLines, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(restaurantName);
        ArgumentNullException.ThrowIfNull(deliverTo);
        ArgumentNullException.ThrowIfNull(orderLines);
        CheckRule(new AnOrderHasLines(orderLines.Count));

        var order = new Order(id, city, customerId, customerName, restaurantId, restaurantName, currency, deliverTo, paymentIntentId, orderLines, now);
        order.Raise(new OrderPlaced(
            order.Id, order.OrderNumber, city, customerId, restaurantId, restaurantName, order.Total, currency,
            order.lines.Sum(static l => l.Quantity), now));
        return order;
    }

    public void MarkPaid(string paymentReference, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentReference);
        CheckRule(new OnlyAPlacedOrderIsPaid(this));
        PaymentReference = paymentReference;
        Move(OrderStatus.Paid, now, null);
    }

    /// <summary>
    /// The charge of an order that was cancelled meanwhile. The order stays cancelled; it remembers the
    /// charge, so that it knows it owes a refund.
    /// </summary>
    public void RememberLateCharge(string paymentReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentReference);
        PaymentReference ??= paymentReference;
    }

    public void Accept(int readyInMinutes, DateTimeOffset now)
    {
        CheckRule(new OnlyAPaidOrderIsAccepted(this));
        ReadyInMinutes = readyInMinutes;
        Move(OrderStatus.Accepted, now, string.Create(CultureInfo.InvariantCulture, $"ready in {readyInMinutes} min"));
    }

    public void SendOut(string courierId, string courierName, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(courierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(courierName);
        CheckRule(new OnlyAnAcceptedOrderLeaves(this));
        CourierId = courierId;
        CourierName = courierName;
        Move(OrderStatus.OutForDelivery, now, courierName);
        Raise(new OrderOutForDelivery(Id, OrderNumber, City, CustomerId, courierId, courierName, now));
    }

    public void MarkDelivered(DateTimeOffset now)
    {
        CheckRule(new OnlyAnOrderOnItsWayIsDelivered(this));
        Move(OrderStatus.Delivered, now, null);
        Raise(new OrderDelivered(Id, OrderNumber, City, CustomerId, now));
    }

    public void Cancel(string reason, string? note, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        CheckRule(new AnOrderOnItsWayIsNotCancelled(this));
        CancellationReason = reason;
        Move(OrderStatus.Cancelled, now, note ?? reason);
        Raise(new OrderCancelled(Id, OrderNumber, City, CustomerId, reason, now));
    }

    public void CancelByCustomer(string? note, DateTimeOffset now)
    {
        CheckRule(new ACustomerCancelsBeforeTheRestaurantCooks(this));
        Cancel(CancellationReasons.ByCustomer, note, now);
    }

    /// <summary>Recorded once, however often it is reported.</summary>
    public void RecordRefund(string refundReference, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refundReference);
        if (RefundReference is not null)
        {
            return;
        }

        RefundReference = refundReference;
        history.Add(new OrderHistoryEntry(Status, now, "refunded"));
    }

    public bool BelongsTo(string customerId) => string.Equals(CustomerId, customerId, StringComparison.Ordinal);

    private void Move(OrderStatus status, DateTimeOffset now, string? note)
    {
        Status = status;
        history.Add(new OrderHistoryEntry(status, now, note));
    }

    /// <summary>TFN, the day, and the end of the identifier: short enough to say on the phone.</summary>
    private static string NumberOf(Guid id, DateTimeOffset now) =>
        string.Create(CultureInfo.InvariantCulture, $"TFN-{now:yyMMdd}-{id.ToString("N")[^6..].ToUpperInvariant()}");
}
