using Microsoft.EntityFrameworkCore;
using Tiffin.Dispatch.Application.Ports;
using Tiffin.Dispatch.Application.Queries;
using Tiffin.Dispatch.Application.Views;
using Tiffin.Dispatch.Domain;

namespace Tiffin.Dispatch.Infrastructure.Persistence;

public sealed class CourierRepository(AppDbContext database) : ICourierRepository
{
    public async Task<Courier?> GetAsync(string courierId, string city, CancellationToken cancellationToken) =>
        await database.Set<Courier>().FirstOrDefaultAsync(c => c.Id == courierId && c.City == city, cancellationToken).ConfigureAwait(false);

    public async Task<Courier?> LongestFreeAsync(string city, CancellationToken cancellationToken) =>
        await database.Set<Courier>()
            .Where(c => c.City == city && c.IsOnDuty && c.CarryingOrderId == null)
            .OrderBy(c => c.FreeSinceUtc)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Courier courier) => database.Set<Courier>().Add(courier);
}

public sealed class DeliveryRepository(AppDbContext database) : IDeliveryRepository
{
    public async Task<Delivery?> GetAsync(Guid orderId, string city, CancellationToken cancellationToken) =>
        await database.Set<Delivery>().FirstOrDefaultAsync(d => d.Id == orderId && d.City == city, cancellationToken).ConfigureAwait(false);

    public void Add(Delivery delivery) => database.Set<Delivery>().Add(delivery);
}

/// <summary>The read side: read without tracking, mapped to a view before it leaves.</summary>
public sealed class DispatchReadModel(AppDbContext database) : IDispatchReadModel
{
    public async Task<DeliveryView?> CarriedByAsync(string courierId, string city, CancellationToken cancellationToken)
    {
        var delivery = await database.Set<Delivery>().AsNoTracking()
            .Where(d => d.City == city && d.CourierId == courierId && d.Status == DeliveryStatus.Assigned)
            .OrderByDescending(d => d.AssignedOnUtc)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return delivery is null ? null : DispatchViews.Of(delivery);
    }
}
