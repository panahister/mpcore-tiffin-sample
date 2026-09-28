using Microsoft.EntityFrameworkCore;
using MPCore.Application.Querying;
using Tiffin.Kitchen.Application.Ports;
using Tiffin.Kitchen.Application.Views;
using Tiffin.Kitchen.Domain;

namespace Tiffin.Kitchen.Infrastructure.Persistence;

public sealed class TicketRepository(AppDbContext database) : ITicketRepository
{
    public async Task<Ticket?> GetAsync(Guid orderId, string city, CancellationToken cancellationToken) =>
        await database.Set<Ticket>().FirstOrDefaultAsync(t => t.Id == orderId && t.City == city, cancellationToken).ConfigureAwait(false);

    public void Add(Ticket ticket) => database.Set<Ticket>().Add(ticket);
}

public sealed class KnownRestaurants(AppDbContext database) : IKnownRestaurants
{
    public async Task<KnownRestaurant?> GetAsync(Guid restaurantId, string city, CancellationToken cancellationToken) =>
        await database.Set<KnownRestaurant>().FirstOrDefaultAsync(r => r.Id == restaurantId && r.City == city, cancellationToken).ConfigureAwait(false);

    public void Add(KnownRestaurant restaurant) => database.Set<KnownRestaurant>().Add(restaurant);
}

/// <summary>The read side: read without tracking, mapped to a view before it leaves.</summary>
public sealed class TicketReadModel(AppDbContext database) : ITicketReadModel
{
    public async Task<Page<TicketView>> ListForManagerAsync(
        string managerId, string city, TicketStatus? status, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var managed = database.Set<KnownRestaurant>().AsNoTracking().Where(r => r.City == city && r.ManagerId == managerId).Select(r => r.Id);
        var query = database.Set<Ticket>().AsNoTracking().Where(t => t.City == city && managed.Contains(t.RestaurantId));
        if (status is not null)
        {
            query = query.Where(t => t.Status == status);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var tickets = await query.OrderBy(t => t.ReceivedOnUtc).Skip(page.Skip).Take(page.Size).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Page<TicketView>([.. tickets.Select(TicketViews.Of)], page.Number, page.Size, total);
    }
}
