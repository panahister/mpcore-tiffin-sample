using MPCore.Application.Messaging;
using MPCore.Application.Results;
using MPCore.Application.Time;
using MPCore.Persistence.Abstractions;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Dispatch.Application.Ports;
using Tiffin.Dispatch.Application.Views;
using Tiffin.Dispatch.Domain;

namespace Tiffin.Dispatch.Application.Commands;

/// <summary>The courier starts to work. The first time, the courier is put on the roster of their city.</summary>
public sealed record GoOnDuty : ICommand<Result<CourierView>>;

/// <summary>The courier goes home.</summary>
public sealed record GoOffDuty : ICommand<Result<CourierView>>;

/// <summary>The courier handed the order over.</summary>
public sealed record CompleteDelivery(Guid OrderId) : ICommand<Result<DeliveryView>>;

/// <summary>
/// What a courier does. Who the courier is and in which city comes from the validated token, never from
/// the request: a courier cannot go on duty for somebody else, or in another city.
/// </summary>
public static class CourierCommandsHandler
{
    public static async Task<Result<CourierView>> Handle(
        GoOnDuty command, ICurrentActorAccessor actor, ITenantContext tenant, ICourierRepository couriers, IUnitOfWork unitOfWork, IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(couriers);

        var caller = actor.Current;
        if (caller.SubjectId is not { } courierId || tenant.TenantId is not { } city)
        {
            return Result<CourierView>.FromFailure(DispatchFailures.CourierRequired());
        }

        var now = clock.UtcNow;
        var name = caller.DisplayName ?? caller.UserName ?? courierId;
        var courier = await couriers.GetAsync(courierId, city, cancellationToken).ConfigureAwait(false);
        if (courier is null)
        {
            courier = Courier.Enrol(courierId, city, name, now);
            couriers.Add(courier);
        }

        courier.GoOnDuty(name, now);
        return Result<CourierView>.Success(DispatchViews.Of(courier));
    }

    public static async Task<Result<CourierView>> Handle(
        GoOffDuty command, ICurrentActorAccessor actor, ITenantContext tenant, ICourierRepository couriers, IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(couriers);

        if (actor.Current.SubjectId is not { } courierId || tenant.TenantId is not { } city)
        {
            return Result<CourierView>.FromFailure(DispatchFailures.CourierRequired());
        }

        var courier = await couriers.GetAsync(courierId, city, cancellationToken).ConfigureAwait(false);
        if (courier is null)
        {
            return Result<CourierView>.FromFailure(DispatchFailures.NotOnTheRoster());
        }

        // A courier who carries an order breaks rule D2.
        courier.GoOffDuty();
        return Result<CourierView>.Success(DispatchViews.Of(courier));
    }

    public static async Task<Result<DeliveryView>> Handle(
        CompleteDelivery command, ICurrentActorAccessor actor, ITenantContext tenant, ICourierRepository couriers, IDeliveryRepository deliveries,
        IUnitOfWork unitOfWork, IClock clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(couriers);
        ArgumentNullException.ThrowIfNull(deliveries);

        if (actor.Current.SubjectId is not { } courierId || tenant.TenantId is not { } city)
        {
            return Result<DeliveryView>.FromFailure(DispatchFailures.CourierRequired());
        }

        var courier = await couriers.GetAsync(courierId, city, cancellationToken).ConfigureAwait(false);
        var delivery = await deliveries.GetAsync(command.OrderId, city, cancellationToken).ConfigureAwait(false);
        if (courier is null || delivery is null)
        {
            return Result<DeliveryView>.FromFailure(DispatchFailures.DeliveryNotFound());
        }

        // Somebody else's delivery breaks rule D3; one that was already handed over, rule D4.
        delivery.Complete(courier, clock.UtcNow);
        return Result<DeliveryView>.Success(DispatchViews.Of(delivery));
    }
}
