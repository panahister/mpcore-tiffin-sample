using MPCore.Application.Messaging;
using MPCore.Application.Querying;
using MPCore.Application.Results;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Ordering.Application.Ports;
using Tiffin.Ordering.Application.Views;

namespace Tiffin.Ordering.Application.Queries;

/// <summary>One order, for the customer who placed it.</summary>
public sealed record GetOrder(Guid OrderId) : IQuery<Result<OrderView>>;

/// <summary>The caller's own orders, newest first.</summary>
public sealed record ListMyOrders(int Page = 1, int Size = PageRequest.DefaultSize) : IQuery<Result<Page<OrderSummary>>>;

public static class OrderQueriesHandler
{
    public static async Task<Result<OrderView>> Handle(
        GetOrder query, ICurrentActorAccessor actor, ITenantContext tenant, IOrderReadModel orders, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(orders);

        if (actor.Current.SubjectId is not { } customerId || tenant.TenantId is not { } city)
        {
            return Result<OrderView>.FromFailure(OrderingFailures.CustomerRequired());
        }

        // Another customer's order, and an order of another city, are answered like one that does not
        // exist: the answer must not say that it does.
        var order = await orders.FindAsync(query.OrderId, city, cancellationToken).ConfigureAwait(false);
        return order is null || !string.Equals(order.CustomerId, customerId, StringComparison.Ordinal)
            ? Result<OrderView>.FromFailure(OrderingFailures.OrderNotFound())
            : Result<OrderView>.Success(order);
    }

    public static async Task<Result<Page<OrderSummary>>> Handle(
        ListMyOrders query, ICurrentActorAccessor actor, ITenantContext tenant, IOrderReadModel orders, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(orders);

        if (actor.Current.SubjectId is not { } customerId || tenant.TenantId is not { } city)
        {
            return Result<Page<OrderSummary>>.FromFailure(OrderingFailures.CustomerRequired());
        }

        return Result<Page<OrderSummary>>.Success(
            await orders.ListOfCustomerAsync(customerId, city, new PageRequest(query.Page, query.Size), cancellationToken).ConfigureAwait(false));
    }
}
