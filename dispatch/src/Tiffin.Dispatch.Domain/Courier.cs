using MPCore.Domain.Model;
using Tiffin.Dispatch.Domain.Rules;

namespace Tiffin.Dispatch.Domain;

/// <summary>Somebody who carries orders in one city. The identity is the subject of the courier's token.</summary>
/// <remarks>
/// <b>A courier carries one order at a time.</b> Two orders that reach for the same courier at the same
/// moment both read "free", and both try to write "carrying": the second save fails on the row version,
/// its message is tried again, and the second order finds the next courier. Optimistic concurrency, as
/// Martin Fowler describes the <i>Optimistic Offline Lock</i> (<i>Patterns of Enterprise Application
/// Architecture</i>, 2002): nobody waits for a lock, and a collision costs a retry.
/// </remarks>
public sealed class Courier : AggregateRoot<string>
{
    private Courier()
    {
        City = string.Empty;
        Name = string.Empty;
    }

    private Courier(string id, string city, string name, DateTimeOffset now)
        : base(id)
    {
        City = city;
        Name = name;
        FreeSinceUtc = now;
    }

    /// <summary>The tenant.</summary>
    public string City { get; private set; }

    public string Name { get; private set; }

    public bool IsOnDuty { get; private set; }

    /// <summary>The order the courier carries, if any.</summary>
    public Guid? CarryingOrderId { get; private set; }

    /// <summary>Since when the courier has had nothing to carry. Who has waited longest is asked first.</summary>
    public DateTimeOffset FreeSinceUtc { get; private set; }

    public bool IsFree => IsOnDuty && CarryingOrderId is null;

    public static Courier Enrol(string id, string city, string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Courier(id, city, name, now);
    }

    public void GoOnDuty(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        if (!IsOnDuty)
        {
            IsOnDuty = true;
            FreeSinceUtc = now;
        }
    }

    public void GoOffDuty()
    {
        CheckRule(new ACourierFinishesWhatTheyCarry(this));
        IsOnDuty = false;
    }

    public void Take(Guid orderId)
    {
        CheckRule(new ACourierCarriesOneOrder(this));
        CarryingOrderId = orderId;
    }

    public void HandOver(Guid orderId, DateTimeOffset now)
    {
        if (CarryingOrderId == orderId)
        {
            CarryingOrderId = null;
            FreeSinceUtc = now;
        }
    }
}
