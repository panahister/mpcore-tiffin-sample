using MPCore.Application.Idempotency;
using MPCore.Application.Querying;
using MPCore.Application.Results;
using MPCore.Transport.Http;
using Tiffin.Ordering.Api.Hosting;
using Tiffin.Ordering.Application.Commands;
using Tiffin.Ordering.Application.Queries;
using Tiffin.Ordering.Application.Views;
using Wolverine;

namespace Tiffin.Ordering.Api.Rest.Endpoints;

/// <summary>The REST surface of the Ordering service.</summary>
/// <remarks>
/// <para>
/// Every endpoint does the same three things: bind the request, invoke one command or query through
/// Wolverine, and render the <see cref="Result"/> with MP Core's <c>ToHttpResult</c>. A failure becomes
/// RFC 9457 Problem Details with the failure's stable code; no endpoint builds an error by hand.
/// </para>
/// <para>
/// Placing an order requires an <c>Idempotency-Key</c> and goes through MP Core's
/// <see cref="IIdempotentExecutor"/> (ADR-013): a customer whose connection dropped retries with the same
/// key and receives the first answer, marked <c>Idempotency-Replayed: true</c>, and is charged once. The
/// header is the IETF HTTPAPI working group's draft, made common by Stripe.
/// </para>
/// </remarks>
public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var orders = endpoints.MapGroup("/v1/orders").WithTags("Orders").RequireAuthorization(OrderingPolicies.Customer);

        // 202, not 201: the order has its identity and its total, and nothing else about it is known yet.
        orders.MapPost("/", static async (PlaceOrder request, IIdempotentExecutor idempotent, IMessageBus bus, CancellationToken ct) =>
                (await idempotent.ExecuteAsync(request, token => bus.InvokeAsync<Result<OrderAccepted>>(request, token), ct)
                    .ConfigureAwait(false)).ToHttpResult(accepted => Results.Accepted($"/v1/orders/{accepted.OrderId}", accepted)))
            .RequireIdempotencyKey()
            .Produces<OrderAccepted>(202)
            .WithName("PlaceOrder");

        orders.MapGet("/", static async (int? page, int? size, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<Page<OrderSummary>>>(new ListMyOrders(page ?? 1, size ?? PageRequest.DefaultSize), ct)
                    .ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .Produces<Page<OrderSummary>>(200)
            .WithName("ListMyOrders");

        orders.MapGet("/{orderId:guid}", static async (Guid orderId, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<OrderView>>(new GetOrder(orderId), ct).ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .Produces<OrderView>(200)
            .WithName("GetOrder");

        orders.MapPost("/{orderId:guid}/cancel", static async (Guid orderId, CancelRequest? request, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<OrderView>>(new CancelOrder(orderId, request?.Note), ct).ConfigureAwait(false))
                .ToHttpResult(Results.Ok))
            .Produces<OrderView>(200)
            .WithName("CancelOrder");

        return orders;
    }
}

public sealed record CancelRequest(string? Note);
