using MPCore.Application.Querying;
using MPCore.Application.Results;
using MPCore.Transport.Http;
using Tiffin.Kitchen.Api.Hosting;
using Tiffin.Kitchen.Application.Commands;
using Tiffin.Kitchen.Application.Queries;
using Tiffin.Kitchen.Application.Views;
using Wolverine;

namespace Tiffin.Kitchen.Api.Rest.Endpoints;

/// <summary>The REST surface of the Kitchen service: a manager's work list, and the manager's word on an order.</summary>
/// <remarks>
/// Every endpoint binds the request, invokes one command or query through Wolverine, and renders the
/// <see cref="Result"/> with MP Core's <c>ToHttpResult</c>. A failure becomes RFC 9457 Problem Details
/// with the failure's stable code; no endpoint builds an error by hand.
/// </remarks>
public static class KitchenEndpoints
{
    public static RouteGroupBuilder MapKitchenEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var tickets = endpoints.MapGroup("/v1/kitchen/tickets").WithTags("Kitchen").RequireAuthorization(KitchenPolicies.Manager);

        tickets.MapGet("/", static async (string? status, int? page, int? size, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<Page<TicketView>>>(new ListTickets(status, page ?? 1, size ?? PageRequest.DefaultSize), ct)
                    .ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .WithName("ListTickets");

        tickets.MapPost("/{orderId:guid}/accept", static async (Guid orderId, AcceptRequest request, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<TicketView>>(new AcceptTicket(orderId, request.ReadyInMinutes), ct).ConfigureAwait(false))
                .ToHttpResult(Results.Ok))
            .WithName("AcceptTicket");

        tickets.MapPost("/{orderId:guid}/reject", static async (Guid orderId, RejectRequest request, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<TicketView>>(new RejectTicket(orderId, request.Reason), ct).ConfigureAwait(false))
                .ToHttpResult(Results.Ok))
            .WithName("RejectTicket");

        return tickets;
    }
}

public sealed record AcceptRequest(int ReadyInMinutes);

public sealed record RejectRequest(string Reason);
