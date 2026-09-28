using Tiffin.Dispatch.Domain;

namespace Tiffin.Dispatch.Application.Ports;

/// <summary>The couriers of one city.</summary>
public interface ICourierRepository
{
    Task<Courier?> GetAsync(string courierId, string city, CancellationToken cancellationToken);

    /// <summary>The courier on duty who has had nothing to carry for the longest time, or null.</summary>
    Task<Courier?> LongestFreeAsync(string city, CancellationToken cancellationToken);

    void Add(Courier courier);
}

/// <summary>The deliveries of one city.</summary>
public interface IDeliveryRepository
{
    Task<Delivery?> GetAsync(Guid orderId, string city, CancellationToken cancellationToken);

    void Add(Delivery delivery);
}
