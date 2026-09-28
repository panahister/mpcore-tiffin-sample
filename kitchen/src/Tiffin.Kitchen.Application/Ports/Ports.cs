using MPCore.Application.Querying;
using Tiffin.Kitchen.Application.Views;
using Tiffin.Kitchen.Domain;

namespace Tiffin.Kitchen.Application.Ports;

/// <summary>The tickets of one city.</summary>
public interface ITicketRepository
{
    Task<Ticket?> GetAsync(Guid orderId, string city, CancellationToken cancellationToken);

    void Add(Ticket ticket);
}

/// <summary>What the Kitchen knows of the restaurants of one city.</summary>
public interface IKnownRestaurants
{
    Task<KnownRestaurant?> GetAsync(Guid restaurantId, string city, CancellationToken cancellationToken);

    void Add(KnownRestaurant restaurant);
}

/// <summary>The read side. Nothing here is tracked, and nothing is changed.</summary>
public interface ITicketReadModel
{
    /// <summary>The tickets of the restaurants this manager manages, oldest first.</summary>
    Task<Page<TicketView>> ListForManagerAsync(string managerId, string city, TicketStatus? status, PageRequest page, CancellationToken cancellationToken);
}
