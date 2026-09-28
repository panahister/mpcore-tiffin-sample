using Tiffin.Kitchen.Domain;

namespace Tiffin.Kitchen.Application.Views;

public sealed record TicketLineView(string Code, string Name, int Quantity);

public sealed record TicketView(
    Guid OrderId, string OrderNumber, Guid RestaurantId, string RestaurantName, string Status, int? ReadyInMinutes, string? Reason,
    DateTimeOffset ReceivedOnUtc, DateTimeOffset? DecidedOnUtc, IReadOnlyList<TicketLineView> Lines);

public static class TicketViews
{
    public static TicketView Of(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return new TicketView(
            ticket.OrderId, ticket.OrderNumber, ticket.RestaurantId, ticket.RestaurantName, ticket.Status.ToString(), ticket.ReadyInMinutes,
            ticket.Reason, ticket.ReceivedOnUtc, ticket.DecidedOnUtc,
            [.. ticket.Lines.Select(static l => new TicketLineView(l.Code, l.Name, l.Quantity))]);
    }
}
