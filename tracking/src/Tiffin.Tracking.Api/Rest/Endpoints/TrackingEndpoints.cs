using MPCore.Application.Results;
using MPCore.Transport.Http;
using Tiffin.Tracking.Api.Hosting;
using Tiffin.Tracking.Application.Commands;
using Tiffin.Tracking.Application.Queries;
using Tiffin.Tracking.Application.Views;
using Wolverine;

namespace Tiffin.Tracking.Api.Rest.Endpoints;

/// <summary>The REST surface of the Tracking service: where a courier is, said by the courier and read by whoever waits.</summary>
/// <remarks>
/// Every endpoint binds the request, invokes one command or query through Wolverine, and renders the
/// <see cref="Result"/> with MP Core's <c>ToHttpResult</c>. A failure becomes RFC 9457 Problem Details
/// with the failure's stable code; no endpoint builds an error by hand.
/// </remarks>
public static class TrackingEndpoints
{
    public static RouteGroupBuilder MapTrackingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var deliveries = endpoints.MapGroup("/v1/tracking/deliveries").WithTags("Tracking");

        deliveries.MapPost("/{orderId:guid}/positions", static async (Guid orderId, PositionRequest request, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<TrackingView>>(new ReportPosition(orderId, request.Latitude, request.Longitude), ct).ConfigureAwait(false))
                .ToHttpResult(static _ => Results.NoContent()))
            .RequireAuthorization(TrackingPolicies.Courier)
            .WithName("ReportPosition");

        deliveries.MapGet("/{orderId:guid}", static async (Guid orderId, int? points, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<TrackingView>>(new GetTracking(orderId, points ?? 20), ct).ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .RequireAuthorization(TrackingPolicies.Followers)
            .WithName("GetTracking");

        return deliveries;
    }
}

public sealed record PositionRequest(double Latitude, double Longitude)
{
    public override string ToString() => nameof(PositionRequest);
}
