using Microsoft.Extensions.Logging;
using MPCore.Application.Time;
using MPCore.Messaging.Abstractions;
using MPCore.Persistence.Abstractions;
using MPCore.Tenancy;
using Tiffin.Ordering.Application.Contracts;
using Tiffin.Ordering.Application.Ports;
using Tiffin.Ordering.Domain;

namespace Tiffin.Ordering.Application.Process;

/// <summary>
/// The order process: how an order moves from "placed" to "delivered", or to "cancelled", as Payments, the
/// Kitchen and Dispatch answer.
/// </summary>
/// <remarks>
/// <para>
/// An <b>orchestration-based saga</b> (Hector Garcia-Molina and Kenneth Salem, <i>Sagas</i>, 1987; Chris
/// Richardson, <i>Microservices Patterns</i>, 2018), which Gregor Hohpe and Bobby Woolf call a <i>process
/// manager</i> (<i>Enterprise Integration Patterns</i>, 2003). Four services change their own data in four
/// transactions of their own, and no transaction spans two of them. What keeps the whole consistent is
/// this class: it knows the next step, and it knows how each step that was taken is taken back.
/// </para>
/// <para>
/// <b>What is taken back, and by whom.</b>
/// </para>
/// <list type="table">
/// <item><term>The bank refuses the card</term><description>the order is cancelled; nothing was done that has to be undone.</description></item>
/// <item><term>The restaurant refuses the order</term><description>the order is cancelled; Payments gives the money back.</description></item>
/// <item><term>No courier is free</term><description>the order is cancelled; the Kitchen stops; Payments gives the money back.</description></item>
/// <item><term>The customer cancelled while an answer was on its way</term><description>the answer is met by a cancelled order, and whatever it reports is taken back.</description></item>
/// </list>
/// <para>
/// <b>Every answer can arrive late, twice, or after the order was cancelled.</b> The handler asks the
/// order first (<c>CanMarkPaid</c>, <c>CanAccept</c>, ...) and ignores what no longer applies, instead of
/// letting a rule break: a broken rule on a queued message is a verdict and goes to the dead-letter queue.
/// </para>
/// <para>
/// <b>The city travels with the message.</b> These handlers run from a queue, outside any request. MP Core
/// opens the tenant of the message that arrived for the whole execution, so the order is read in its city,
/// the audit trail names the city, and what is published in turn belongs to it.
/// </para>
/// </remarks>
public static class OrderProcessHandler
{
    /// <summary>The card was charged: ask the restaurant.</summary>
    public static async Task Handle(
        PaymentAuthorized message, IOrderRepository orders, ITenantContext tenant, IMessagePublisher publisher, IUnitOfWork unitOfWork,
        IClock clock, ILogger<PaymentAuthorized> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(publisher);

        var order = await LoadAsync(orders, tenant, message.OrderId, cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        if (order.IsCancelled)
        {
            // Cancelled while the provider was charging the card. Pay it back.
            order.RememberLateCharge(message.ProviderReference);
            logger.LogWarning("Order {OrderNumber} was charged after it was cancelled; refunding", order.OrderNumber);
            await publisher.PublishAsync(new RefundRequested(order.Id, order.OrderNumber, order.CancellationReason!, now), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!order.CanMarkPaid)
        {
            logger.LogInformation("Duplicate PaymentAuthorized for {OrderNumber} ignored ({Status})", order.OrderNumber, order.Status);
            return;
        }

        order.MarkPaid(message.ProviderReference, now);
        await publisher.PublishAsync(
            new PreparationRequested(
                order.Id, order.OrderNumber, order.RestaurantId, order.RestaurantName,
                [.. order.Lines.Select(static l => new PreparationLine(l.Code, l.Name, l.Quantity))], now),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The bank said no: cancel the order. Nobody else has been asked yet.</summary>
    public static async Task Handle(
        PaymentDeclined message, IOrderRepository orders, ITenantContext tenant, IUnitOfWork unitOfWork,
        IClock clock, ILogger<PaymentDeclined> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var order = await LoadAsync(orders, tenant, message.OrderId, cancellationToken).ConfigureAwait(false);
        if (order.Status != OrderStatus.Placed)
        {
            return;
        }

        order.Cancel(CancellationReasons.PaymentDeclined, message.DeclineCode, clock.UtcNow);
        logger.LogInformation("Order {OrderNumber} cancelled: payment declined ({DeclineCode})", order.OrderNumber, message.DeclineCode);
    }

    /// <summary>The restaurant cooks: ask for a courier.</summary>
    public static async Task Handle(
        KitchenAccepted message, IOrderRepository orders, ITenantContext tenant, IMessagePublisher publisher, IUnitOfWork unitOfWork,
        IClock clock, ILogger<KitchenAccepted> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(publisher);

        var order = await LoadAsync(orders, tenant, message.OrderId, cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        if (order.IsCancelled)
        {
            // The customer cancelled while the restaurant was deciding. The cancellation told the Kitchen
            // to stop; it is told again, because this answer shows that it had not heard yet.
            await publisher.PublishAsync(new PreparationCancelled(order.Id, order.OrderNumber, order.CancellationReason!, now), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!order.CanAccept)
        {
            logger.LogInformation("Duplicate KitchenAccepted for {OrderNumber} ignored ({Status})", order.OrderNumber, order.Status);
            return;
        }

        order.Accept(message.ReadyInMinutes, now);
        var to = order.DeliverTo;
        await publisher.PublishAsync(
            new CourierRequested(
                order.Id, order.OrderNumber, order.CustomerId, order.RestaurantName,
                new CourierDestination(to.Recipient, to.Phone, to.District, to.Line), now),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The restaurant will not cook: cancel the order, and give the money back.</summary>
    public static async Task Handle(
        KitchenRejected message, IOrderRepository orders, ITenantContext tenant, IMessagePublisher publisher, IUnitOfWork unitOfWork,
        IClock clock, ILogger<KitchenRejected> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(publisher);

        var order = await LoadAsync(orders, tenant, message.OrderId, cancellationToken).ConfigureAwait(false);
        if (!order.CanAccept)
        {
            return;
        }

        var now = clock.UtcNow;
        order.Cancel(CancellationReasons.RestaurantRefused, message.Reason, now);
        await publisher.PublishAsync(new RefundRequested(order.Id, order.OrderNumber, CancellationReasons.RestaurantRefused, now), cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation("Order {OrderNumber} cancelled: the restaurant refused ({Reason})", order.OrderNumber, message.Reason);
    }

    /// <summary>A courier carries it.</summary>
    public static async Task Handle(
        CourierAssigned message, IOrderRepository orders, ITenantContext tenant, IUnitOfWork unitOfWork,
        IClock clock, ILogger<CourierAssigned> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var order = await LoadAsync(orders, tenant, message.OrderId, cancellationToken).ConfigureAwait(false);
        if (!order.CanSendOut)
        {
            logger.LogInformation("CourierAssigned for {OrderNumber} ignored ({Status})", order.OrderNumber, order.Status);
            return;
        }

        order.SendOut(message.CourierId, message.CourierName, clock.UtcNow);
    }

    /// <summary>Nobody can carry it: cancel the order, stop the restaurant, give the money back.</summary>
    public static async Task Handle(
        CourierUnavailable message, IOrderRepository orders, ITenantContext tenant, IMessagePublisher publisher, IUnitOfWork unitOfWork,
        IClock clock, ILogger<CourierUnavailable> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(publisher);

        var order = await LoadAsync(orders, tenant, message.OrderId, cancellationToken).ConfigureAwait(false);
        if (!order.CanSendOut)
        {
            return;
        }

        var now = clock.UtcNow;
        order.Cancel(CancellationReasons.NoCourier, null, now);
        await publisher.PublishAsync(new PreparationCancelled(order.Id, order.OrderNumber, CancellationReasons.NoCourier, now), cancellationToken)
            .ConfigureAwait(false);
        await publisher.PublishAsync(new RefundRequested(order.Id, order.OrderNumber, CancellationReasons.NoCourier, now), cancellationToken)
            .ConfigureAwait(false);
        logger.LogWarning("Order {OrderNumber} cancelled: no courier was free", order.OrderNumber);
    }

    /// <summary>The money went back. Recorded once, however often it is reported.</summary>
    public static async Task Handle(
        PaymentRefunded message, IOrderRepository orders, ITenantContext tenant, IUnitOfWork unitOfWork, IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var order = await LoadAsync(orders, tenant, message.OrderId, cancellationToken).ConfigureAwait(false);
        order.RecordRefund(message.RefundReference, clock.UtcNow);
    }

    /// <summary>The courier handed it over. Arrives from the event stream, where Dispatch says what happened.</summary>
    public static async Task Handle(
        DeliveryCompleted message, IOrderRepository orders, ITenantContext tenant, IUnitOfWork unitOfWork, IClock clock,
        ILogger<DeliveryCompleted> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var order = await LoadAsync(orders, tenant, message.OrderId, cancellationToken).ConfigureAwait(false);
        if (!order.CanDeliver)
        {
            logger.LogInformation("DeliveryCompleted for {OrderNumber} ignored ({Status})", order.OrderNumber, order.Status);
            return;
        }

        order.MarkDelivered(clock.UtcNow);
    }

    private static async Task<Order> LoadAsync(IOrderRepository orders, ITenantContext tenant, Guid orderId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(tenant);

        // A message without a city is a broken contract between two services, and so is an answer for an
        // order this service never placed. Both go to the error queue for an operator, where a silent
        // repair would hide them.
        var city = tenant.TenantId
            ?? throw new InvalidOperationException($"The answer for order {orderId} names no city; every message of the platform carries its tenant.");
        return await orders.GetAsync(orderId, city, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Order {orderId} does not exist in {city}; a service answered for an order this service never placed.");
    }
}
