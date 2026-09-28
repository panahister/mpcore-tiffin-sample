using System.Globalization;
using MPCore.Domain.Rules;

namespace Tiffin.Kitchen.Domain.Rules;

/// <summary>
/// A business rule of the Kitchen service, reported under the <c>tiffin.kitchen</c> error domain with a
/// stable code and a message key. The named-rule pattern is Kamil Grzybek's (<i>Modular Monolith with
/// DDD</i>); checking before changing is Vladimir Khorikov's <i>always-valid domain model</i>.
/// </summary>
public abstract class KitchenRule(string code, string messageKey, IReadOnlyDictionary<string, string>? arguments = null)
    : BusinessRule(Domain, code, messageKey, arguments)
{
    public const string Domain = "tiffin.kitchen";
}

/// <summary>Rule K1: a restaurant decides an order once.</summary>
public sealed class OnlyAPendingTicketIsDecided(Ticket ticket) : KitchenRule(
    "TICKET_NOT_PENDING", "kitchen.ticket_not_pending",
    new Dictionary<string, string> { ["order_number"] = ticket.OrderNumber, ["status"] = ticket.Status.ToString() })
{
    public override bool IsBroken() => ticket.Status != TicketStatus.Pending;
}

/// <summary>Rule K2: a restaurant promises between five minutes and three hours.</summary>
public sealed class APromiseIsWithinReason(int readyInMinutes) : KitchenRule(
    "PROMISE_OUT_OF_RANGE", "kitchen.promise_out_of_range",
    new Dictionary<string, string> { ["minutes"] = readyInMinutes.ToString(CultureInfo.InvariantCulture) })
{
    public const int Least = 5;
    public const int Most = 180;

    public override bool IsBroken() => readyInMinutes is < Least or > Most;
}

/// <summary>Rule K3: there is something to cook.</summary>
public sealed class ATicketHasLines(int lineCount) : KitchenRule("TICKET_EMPTY", "kitchen.ticket_empty")
{
    public override bool IsBroken() => lineCount == 0;
}
