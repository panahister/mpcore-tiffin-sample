using MPCore.Application.Messaging;
using MPCore.Application.Results;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Dispatch.Application.Ports;
using Tiffin.Dispatch.Application.Views;

namespace Tiffin.Dispatch.Application.Queries;

/// <summary>What the caller carries at the moment.</summary>
public sealed record GetMyDelivery : IQuery<Result<DeliveryView>>;

public static class GetMyDeliveryHandler
{
    public static async Task<Result<DeliveryView>> Handle(
        GetMyDelivery query, ICurrentActorAccessor actor, ITenantContext tenant, IDispatchReadModel dispatch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(dispatch);

        if (actor.Current.SubjectId is not { } courierId || tenant.TenantId is not { } city)
        {
            return Result<DeliveryView>.FromFailure(DispatchFailures.CourierRequired());
        }

        var delivery = await dispatch.CarriedByAsync(courierId, city, cancellationToken).ConfigureAwait(false);
        return delivery is null
            ? Result<DeliveryView>.FromFailure(DispatchFailures.DeliveryNotFound())
            : Result<DeliveryView>.Success(delivery);
    }
}

/// <summary>The read side. Nothing here is tracked, and nothing is changed.</summary>
public interface IDispatchReadModel
{
    Task<DeliveryView?> CarriedByAsync(string courierId, string city, CancellationToken cancellationToken);
}
