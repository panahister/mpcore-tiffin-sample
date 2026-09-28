using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using MPCore.Application.Results;
using Tiffin.Ordering.Api.Hosting;
using Tiffin.Ordering.Application;
using Tiffin.Ordering.Application.Queries;
using Tiffin.Ordering.Application.Views;
using Wolverine;
using ResultFailureException = MPCore.Application.Results.ResultFailureException;

namespace Tiffin.Ordering.Api.Grpc.Services;

/// <summary>An order, read over gRPC.</summary>
/// <remarks>
/// The method does what every endpoint does: read the request, invoke one query through Wolverine, and map
/// the view. A failure is thrown as <see cref="ResultFailureException"/>, and MP Core's gRPC failure
/// handling turns it into the native status with rich error details, in the caller's language.
/// </remarks>
[Authorize(Policy = OrderingPolicies.Customer)]
public sealed class OrdersService(IMessageBus bus) : Orders.OrdersBase
{
    /// <inheritdoc />
    public override async Task<OrderReply> GetOrder(GetOrderRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (!Guid.TryParse(request.OrderId, out var orderId))
        {
            throw new ResultFailureException(OrderingFailures.OrderNotFound());
        }

        var order = (await bus.InvokeAsync<Result<OrderView>>(new GetOrder(orderId), context.CancellationToken).ConfigureAwait(false)).ValueOrThrow();
        var reply = new OrderReply
        {
            OrderId = order.OrderId.ToString(),
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            RestaurantName = order.RestaurantName,
            Total = order.Total.ToString(CultureInfo.InvariantCulture),
            Currency = order.Currency,
            CancellationReason = order.CancellationReason ?? string.Empty,
            CourierName = order.CourierName ?? string.Empty,
            Refunded = order.Refunded,
            PlacedOn = Timestamp.FromDateTimeOffset(order.PlacedOnUtc)
        };
        reply.Lines.AddRange(order.Lines.Select(static l => new OrderLineMessage
        {
            Code = l.Code, Name = l.Name, Quantity = l.Quantity, UnitPrice = l.UnitPrice.ToString(CultureInfo.InvariantCulture)
        }));
        return reply;
    }
}
