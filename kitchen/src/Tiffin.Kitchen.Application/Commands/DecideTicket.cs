using MPCore.Application.Messaging;
using MPCore.Application.Results;
using MPCore.Application.Time;
using MPCore.Audit;
using MPCore.Persistence.Abstractions;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Kitchen.Application.Ports;
using Tiffin.Kitchen.Application.Views;
using Tiffin.Kitchen.Domain;

namespace Tiffin.Kitchen.Application.Commands;

/// <summary>The restaurant cooks the order, and promises when it is ready.</summary>
public sealed record AcceptTicket(Guid OrderId, int ReadyInMinutes) : ICommand<Result<TicketView>>;

/// <summary>The restaurant will not cook the order, and says why.</summary>
public sealed record RejectTicket(Guid OrderId, string Reason) : ICommand<Result<TicketView>>;

/// <summary>
/// The restaurant's word on an order. The aggregate raises the answer, which leaves for the Ordering
/// service with the commit of the decision, and not before (the transactional outbox).
/// </summary>
/// <remarks>
/// Who may decide is asked of the Kitchen's own copy of the restaurants, which it keeps from the event
/// stream. A ticket of a restaurant the caller does not manage is answered like one that does not exist.
/// </remarks>
public static class DecideTicketHandler
{
    public static async Task<Result<TicketView>> Handle(
        AcceptTicket command, ICurrentActorAccessor actor, ITenantContext tenant, ITicketRepository tickets, IKnownRestaurants restaurants,
        IBusinessAuditRecorder audit, IUnitOfWork unitOfWork, IClock clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(audit);

        var found = await FindAsync(command.OrderId, actor, tenant, tickets, restaurants, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found;
        }

        var ticket = await tickets.GetAsync(command.OrderId, tenant.TenantId!, cancellationToken).ConfigureAwait(false);
        ticket!.Accept(command.ReadyInMinutes, clock.UtcNow);
        await audit.RecordAsync(
            "kitchen", "order-accepted", nameof(Ticket), ticket.OrderNumber,
            new Dictionary<string, string> { ["ready_in_minutes"] = command.ReadyInMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            cancellationToken).ConfigureAwait(false);
        return Result<TicketView>.Success(TicketViews.Of(ticket));
    }

    public static async Task<Result<TicketView>> Handle(
        RejectTicket command, ICurrentActorAccessor actor, ITenantContext tenant, ITicketRepository tickets, IKnownRestaurants restaurants,
        IBusinessAuditRecorder audit, IUnitOfWork unitOfWork, IClock clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(audit);

        var found = await FindAsync(command.OrderId, actor, tenant, tickets, restaurants, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found;
        }

        var ticket = await tickets.GetAsync(command.OrderId, tenant.TenantId!, cancellationToken).ConfigureAwait(false);
        ticket!.Reject(command.Reason, clock.UtcNow);
        await audit.RecordAsync(
            "kitchen", "order-rejected", nameof(Ticket), ticket.OrderNumber,
            new Dictionary<string, string> { ["reason"] = ticket.Reason! }, cancellationToken).ConfigureAwait(false);
        return Result<TicketView>.Success(TicketViews.Of(ticket));
    }

    private static async Task<Result<TicketView>> FindAsync(
        Guid orderId, ICurrentActorAccessor actor, ITenantContext tenant, ITicketRepository tickets, IKnownRestaurants restaurants,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentNullException.ThrowIfNull(restaurants);

        if (actor.Current.SubjectId is not { } manager || tenant.TenantId is not { } city)
        {
            return Result<TicketView>.FromFailure(KitchenFailures.ManagerRequired());
        }

        var ticket = await tickets.GetAsync(orderId, city, cancellationToken).ConfigureAwait(false);
        if (ticket is null)
        {
            return Result<TicketView>.FromFailure(KitchenFailures.TicketNotFound());
        }

        var restaurant = await restaurants.GetAsync(ticket.RestaurantId, city, cancellationToken).ConfigureAwait(false);
        return restaurant is null || !string.Equals(restaurant.ManagerId, manager, StringComparison.Ordinal)
            ? Result<TicketView>.FromFailure(KitchenFailures.TicketNotFound())
            : Result<TicketView>.Success(TicketViews.Of(ticket));
    }
}
