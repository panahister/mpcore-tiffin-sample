using Microsoft.Extensions.Logging;
using MPCore.Application.Time;
using MPCore.Persistence.Abstractions;
using MPCore.Tenancy;
using Tiffin.Kitchen.Application.Contracts;
using Tiffin.Kitchen.Application.Ports;
using Tiffin.Kitchen.Domain;

namespace Tiffin.Kitchen.Application.Events;

/// <summary>A paid order waits for the restaurant's word. Arrives from RabbitMQ.</summary>
/// <remarks>
/// <para>
/// A broker delivers at least once. Two guards stand in front of a second ticket. MP Core's inbox stops a
/// second delivery of the same message, by its <c>EventId</c>, before this handler runs. And the ticket is
/// keyed by the order, so the same order announced in a second message finds its ticket here.
/// </para>
/// <para>
/// The handler works for the city of the message that arrived: MP Core opens it for the whole execution.
/// </para>
/// </remarks>
public static class PreparationRequestedHandler
{
    public static async Task Handle(
        PreparationRequested message, ITicketRepository tickets, ITenantContext tenant, IUnitOfWork unitOfWork, IClock clock,
        ILogger<PreparationRequested> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(tickets);

        var city = Cities.Of(tenant, message.OrderId);
        if (await tickets.GetAsync(message.OrderId, city, cancellationToken).ConfigureAwait(false) is not null)
        {
            logger.LogInformation("Order {OrderNumber} already has a ticket; nothing added", message.OrderNumber);
            return;
        }

        tickets.Add(Ticket.Receive(
            message.OrderId, city, message.OrderNumber, message.RestaurantId, message.RestaurantName,
            [.. message.Lines.Select(static l => new TicketLine(l.Code, l.Name, l.Quantity))], clock.UtcNow));
        logger.LogInformation("Order {OrderNumber} waits for {Restaurant}", message.OrderNumber, message.RestaurantName);
    }
}

/// <summary>The order was cancelled: the restaurant stops. Arrives from RabbitMQ.</summary>
public static class PreparationCancelledHandler
{
    public static async Task Handle(
        PreparationCancelled message, ITicketRepository tickets, ITenantContext tenant, IUnitOfWork unitOfWork, IClock clock,
        ILogger<PreparationCancelled> logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(tickets);

        var ticket = await tickets.GetAsync(message.OrderId, Cities.Of(tenant, message.OrderId), cancellationToken).ConfigureAwait(false);
        if (ticket is null)
        {
            // The cancellation overtook the request, which travels on a queue of its own. The request,
            // when it arrives, opens a ticket for an order that is already cancelled. Whatever the
            // restaurant then says about it is met by a cancelled order, and the order tells the Kitchen
            // to stop once more (OrderProcessHandler in the Ordering service): the ticket ends cancelled.
            logger.LogInformation("Order {OrderId} was cancelled before its ticket arrived", message.OrderId);
            return;
        }

        if (ticket.Cancel(message.Reason, clock.UtcNow))
        {
            logger.LogInformation("Ticket of order {OrderNumber} cancelled: {Reason}", ticket.OrderNumber, message.Reason);
        }
    }
}

/// <summary>A restaurant exists. Arrives from Kafka, where Restaurants says what happened.</summary>
public static class RestaurantRegisteredHandler
{
    public static async Task Handle(
        RestaurantRegistered message, IKnownRestaurants restaurants, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(restaurants);

        // The event says which city; it is the fact that is recorded, whoever's work delivered it.
        var known = await restaurants.GetAsync(message.RestaurantId, message.City, cancellationToken).ConfigureAwait(false);
        if (known is null)
        {
            restaurants.Add(new KnownRestaurant(message.RestaurantId, message.City, message.RestaurantName, message.ManagerId));
        }
        else
        {
            known.Learn(message.RestaurantName, message.ManagerId);
        }
    }
}

internal static class Cities
{
    public static string Of(ITenantContext tenant, Guid orderId)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return tenant.TenantId
            ?? throw new InvalidOperationException($"The message for order {orderId} names no city; every message of the platform carries its tenant.");
    }
}
