using MPCore.Application.Messaging;
using MPCore.Application.Querying;
using MPCore.Application.Results;
using MPCore.Security;
using MPCore.Tenancy;
using Tiffin.Kitchen.Application.Ports;
using Tiffin.Kitchen.Application.Views;
using Tiffin.Kitchen.Domain;

namespace Tiffin.Kitchen.Application.Queries;

/// <summary>The manager's work list, oldest first. <paramref name="Status"/> is a status of a ticket, or empty for all.</summary>
public sealed record ListTickets(string? Status, int Page = 1, int Size = PageRequest.DefaultSize) : IQuery<Result<Page<TicketView>>>;

public static class ListTicketsHandler
{
    public static async Task<Result<Page<TicketView>>> Handle(
        ListTickets query, ICurrentActorAccessor actor, ITenantContext tenant, ITicketReadModel tickets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(tickets);

        if (actor.Current.SubjectId is not { } manager || tenant.TenantId is not { } city)
        {
            return Result<Page<TicketView>>.FromFailure(KitchenFailures.ManagerRequired());
        }

        TicketStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<TicketStatus>(query.Status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Result<Page<TicketView>>.FromFailure(KitchenFailures.StatusUnknown());
            }

            status = parsed;
        }

        return Result<Page<TicketView>>.Success(
            await tickets.ListForManagerAsync(manager, city, status, new PageRequest(query.Page, query.Size), cancellationToken).ConfigureAwait(false));
    }
}
