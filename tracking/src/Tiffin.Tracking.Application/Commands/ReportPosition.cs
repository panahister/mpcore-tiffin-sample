using MPCore.Application.Messaging;
using MPCore.Application.Results;
using MPCore.Application.Time;
using MPCore.Persistence.Abstractions;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Tracking.Application.Ports;
using Tiffin.Tracking.Application.Views;

namespace Tiffin.Tracking.Application.Commands;

/// <summary>The courier's app says where the courier is.</summary>
/// <remarks>Where somebody is, is personal data: this record says what it prints.</remarks>
public sealed record ReportPosition(Guid OrderId, double Latitude, double Longitude) : ICommand<Result<TrackingView>>
{
    public override string ToString() => $"{nameof(ReportPosition)} {{ OrderId = {OrderId} }}";
}

/// <summary>
/// Records the position twice in one transaction: on the delivery, which answers "where is it now", and in
/// the time series, which answers "which way did it come". Nothing is audited and nothing is published: a
/// position is a measurement, there are very many of them, and nobody decides anything by one.
/// </summary>
public static class ReportPositionHandler
{
    public static async Task<Result<TrackingView>> Handle(
        ReportPosition command, ICurrentActorAccessor actor, ITenantContext tenant, ITrackingRepository deliveries, IUnitOfWork unitOfWork,
        IClock clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(deliveries);

        if (actor.Current.SubjectId is not { } courierId || tenant.TenantId is not { } city)
        {
            return Result<TrackingView>.FromFailure(TrackingFailures.SignInRequired());
        }

        var delivery = await deliveries.GetAsync(command.OrderId, city, cancellationToken).ConfigureAwait(false);
        if (delivery is null)
        {
            return Result<TrackingView>.FromFailure(TrackingFailures.DeliveryNotFound());
        }

        // Somebody else's delivery breaks rule T1; one that has arrived, rule T2; a place that is not on
        // Earth, rule T3.
        deliveries.Log(delivery.Report(courierId, command.Latitude, command.Longitude, clock.UtcNow));
        return Result<TrackingView>.Success(TrackingViews.Of(delivery, []));
    }
}
