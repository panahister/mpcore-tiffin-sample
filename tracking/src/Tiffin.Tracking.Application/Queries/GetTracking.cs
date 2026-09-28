using MPCore.Application.Messaging;
using MPCore.Application.Results;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Tracking.Application.Ports;
using Tiffin.Tracking.Application.Views;

namespace Tiffin.Tracking.Application.Queries;

/// <summary>Where an order is, and the last <paramref name="Points"/> places it was seen, newest first.</summary>
public sealed record GetTracking(Guid OrderId, int Points = 20) : IQuery<Result<TrackingView>>;

public static class GetTrackingHandler
{
    public const int MostPoints = 500;

    public static async Task<Result<TrackingView>> Handle(
        GetTracking query, ICurrentActorAccessor actor, ITenantContext tenant, ITrackingReadModel deliveries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(deliveries);

        if (actor.Current.SubjectId is not { } caller || tenant.TenantId is not { } city)
        {
            return Result<TrackingView>.FromFailure(TrackingFailures.SignInRequired());
        }

        // Somebody else's order is answered like one that does not exist: where a courier is, is shown to
        // the customer who waits and to the courier, and to nobody else.
        var delivery = await deliveries.FindAsync(query.OrderId, city, cancellationToken).ConfigureAwait(false);
        if (delivery is null || !delivery.MayBeSeenBy(caller))
        {
            return Result<TrackingView>.FromFailure(TrackingFailures.DeliveryNotFound());
        }

        var trail = await deliveries.TrailAsync(query.OrderId, city, Math.Clamp(query.Points, 0, MostPoints), cancellationToken).ConfigureAwait(false);
        return Result<TrackingView>.Success(TrackingViews.Of(delivery, trail));
    }
}
