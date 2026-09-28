using Microsoft.EntityFrameworkCore;
using MPCore.Application.Querying;
using Tiffin.Ordering.Application.Ports;
using Tiffin.Ordering.Application.Views;
using Tiffin.Ordering.Domain;

namespace Tiffin.Ordering.Infrastructure.Persistence;

public sealed class OrderRepository(AppDbContext database) : IOrderRepository
{
    public async Task<Order?> GetAsync(Guid id, string city, CancellationToken cancellationToken) =>
        await database.Set<Order>().FirstOrDefaultAsync(o => o.Id == id && o.City == city, cancellationToken).ConfigureAwait(false);

    public void Add(Order order) => database.Set<Order>().Add(order);
}

/// <summary>The read side: read without tracking, mapped to a view before it leaves.</summary>
public sealed class OrderReadModel(AppDbContext database) : IOrderReadModel
{
    public async Task<OrderView?> FindAsync(Guid id, string city, CancellationToken cancellationToken)
    {
        var order = await database.Set<Order>().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.City == city, cancellationToken).ConfigureAwait(false);
        return order is null ? null : OrderViews.Of(order);
    }

    public async Task<Page<OrderSummary>> ListOfCustomerAsync(string customerId, string city, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var query = database.Set<Order>().AsNoTracking().Where(o => o.City == city && o.CustomerId == customerId);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(o => o.PlacedOnUtc)
            .Skip(page.Skip).Take(page.Size)
            .Select(o => new OrderSummary(o.Id, o.OrderNumber, o.RestaurantName, o.Status.ToString(), o.Total, o.Currency, o.PlacedOnUtc))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Page<OrderSummary>(items, page.Number, page.Size, total);
    }
}
