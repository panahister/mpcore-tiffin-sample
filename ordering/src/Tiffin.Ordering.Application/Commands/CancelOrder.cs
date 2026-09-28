using MPCore.Application.Messaging;
using MPCore.Application.Results;
using MPCore.Application.Time;
using MPCore.Audit;
using MPCore.Messaging.Abstractions;
using MPCore.Persistence.Abstractions;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Ordering.Application.Contracts;
using Tiffin.Ordering.Application.Ports;
using Tiffin.Ordering.Application.Views;
using Tiffin.Ordering.Domain;

namespace Tiffin.Ordering.Application.Commands;

/// <summary>The customer changes their mind.</summary>
public sealed record CancelOrder(Guid OrderId, string? Note) : ICommand<Result<OrderView>>;

/// <summary>
/// Cancels the order and tells whoever has already been asked: the Kitchen to stop, Payments to give the
/// money back. Both leave with the commit of the cancellation.
/// </summary>
/// <remarks>
/// A charge that is still on its way cannot be stopped from here. Payments is told anyway: it marks the
/// payment as not wanted, and a charge that arrives after that is given back by Payments itself.
/// </remarks>
public static class CancelOrderHandler
{
    public static async Task<Result<OrderView>> Handle(
        CancelOrder command,
        ICurrentActorAccessor actor,
        ITenantContext tenant,
        IOrderRepository orders,
        IMessagePublisher publisher,
        IBusinessAuditRecorder audit,
        IUnitOfWork unitOfWork,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(audit);

        if (actor.Current.SubjectId is not { } customerId || tenant.TenantId is not { } city)
        {
            return Result<OrderView>.FromFailure(OrderingFailures.CustomerRequired());
        }

        var order = await orders.GetAsync(command.OrderId, city, cancellationToken).ConfigureAwait(false);
        if (order is null || !order.BelongsTo(customerId))
        {
            return Result<OrderView>.FromFailure(OrderingFailures.OrderNotFound());
        }

        var now = clock.UtcNow;
        var restaurantWasAsked = order.Status == OrderStatus.Paid;

        // An order the restaurant already cooks breaks rule O7.
        order.CancelByCustomer(command.Note, now);

        await publisher.PublishAsync(new RefundRequested(order.Id, order.OrderNumber, CancellationReasons.ByCustomer, now), cancellationToken)
            .ConfigureAwait(false);
        if (restaurantWasAsked)
        {
            await publisher.PublishAsync(new PreparationCancelled(order.Id, order.OrderNumber, CancellationReasons.ByCustomer, now), cancellationToken)
                .ConfigureAwait(false);
        }

        await audit.RecordAsync(
            "ordering", "order-cancelled", nameof(Order), order.OrderNumber,
            new Dictionary<string, string> { ["reason"] = CancellationReasons.ByCustomer }, cancellationToken).ConfigureAwait(false);
        return Result<OrderView>.Success(OrderViews.Of(order));
    }
}
