using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using MPCore.Application.Results;
using Tiffin.Dispatch.Api.Hosting;
using Tiffin.Dispatch.Application;
using Tiffin.Dispatch.Application.Commands;
using Tiffin.Dispatch.Application.Queries;
using Tiffin.Dispatch.Application.Views;
using Wolverine;
using ResultFailureException = MPCore.Application.Results.ResultFailureException;

namespace Tiffin.Dispatch.Api.Grpc.Services;

/// <summary>What a courier does, over gRPC.</summary>
/// <remarks>
/// Every method does the same three things: read the request, invoke one command or query through
/// Wolverine, and map the view. A failure is thrown as <see cref="ResultFailureException"/>, and MP Core's
/// gRPC failure handling turns it into the native status with rich error details, in the caller's language.
/// </remarks>
[Authorize(Policy = DispatchPolicies.Courier)]
public sealed class CouriersService(IMessageBus bus) : Couriers.CouriersBase
{
    /// <inheritdoc />
    public override async Task<CourierReply> GoOnDuty(DutyRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ToReply((await bus.InvokeAsync<Result<CourierView>>(new GoOnDuty(), context.CancellationToken).ConfigureAwait(false)).ValueOrThrow());
    }

    /// <inheritdoc />
    public override async Task<CourierReply> GoOffDuty(DutyRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ToReply((await bus.InvokeAsync<Result<CourierView>>(new GoOffDuty(), context.CancellationToken).ConfigureAwait(false)).ValueOrThrow());
    }

    /// <inheritdoc />
    public override async Task<DeliveryReply> GetMyDelivery(MyDeliveryRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ToReply((await bus.InvokeAsync<Result<DeliveryView>>(new GetMyDelivery(), context.CancellationToken).ConfigureAwait(false)).ValueOrThrow());
    }

    /// <inheritdoc />
    public override async Task<DeliveryReply> CompleteDelivery(CompleteDeliveryRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (!Guid.TryParse(request.OrderId, out var orderId))
        {
            throw new ResultFailureException(DispatchFailures.OrderIdInvalid());
        }

        return ToReply((await bus.InvokeAsync<Result<DeliveryView>>(new CompleteDelivery(orderId), context.CancellationToken).ConfigureAwait(false))
            .ValueOrThrow());
    }

    private static CourierReply ToReply(CourierView courier) => new()
    {
        CourierId = courier.CourierId,
        Name = courier.Name,
        City = courier.City,
        OnDuty = courier.IsOnDuty,
        CarryingOrderId = courier.CarryingOrderId?.ToString() ?? string.Empty
    };

    private static DeliveryReply ToReply(DeliveryView delivery)
    {
        var reply = new DeliveryReply
        {
            OrderId = delivery.OrderId.ToString(),
            OrderNumber = delivery.OrderNumber,
            Status = delivery.Status,
            RestaurantName = delivery.RestaurantName,
            Recipient = delivery.Recipient,
            Phone = delivery.Phone,
            District = delivery.District,
            Line = delivery.Line,
            AssignedOn = Timestamp.FromDateTimeOffset(delivery.AssignedOnUtc)
        };
        if (delivery.CompletedOnUtc is { } completed)
        {
            reply.CompletedOn = Timestamp.FromDateTimeOffset(completed);
        }

        return reply;
    }
}
