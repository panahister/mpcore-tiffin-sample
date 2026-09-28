namespace Tiffin.Ordering.Application.Process;

/// <summary>
/// The restaurant's deadline: a message Ordering sends itself when it asks a restaurant to cook, delivered no
/// sooner than <see cref="OrderDeadlines.RestaurantAnswer"/> later. When it arrives it asks whether the
/// restaurant answered, and cancels the order when it did not (finding T-08).
/// </summary>
/// <remarks>
/// It never leaves Ordering: it has a handler here and no route, so it goes to a local queue, durable in
/// Ordering's database. MP Core keeps it there until it is due (<c>MessageDeliveryContext.DeliverAfter</c>,
/// MP Core's ADR-015), so a restart of Ordering does not lose it. What NServiceBus calls a saga's timeout.
/// </remarks>
/// <param name="OrderId">The order that waits.</param>
public sealed record RestaurantDeadline(Guid OrderId);

/// <summary>How long the order process waits for an answer before it stops waiting.</summary>
/// <param name="RestaurantAnswer">How long a restaurant has to accept or refuse a paid order.</param>
public sealed record OrderDeadlines(TimeSpan RestaurantAnswer)
{
    /// <summary>Ten minutes: the owner's decision of 2026-09-28.</summary>
    public static readonly TimeSpan DefaultRestaurantAnswer = TimeSpan.FromMinutes(10);
}
