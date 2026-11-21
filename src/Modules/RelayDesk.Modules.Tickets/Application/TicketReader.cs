using Microsoft.EntityFrameworkCore;
using RelayDesk.Modules.Tickets.Persistence;
using RelayDesk.SharedKernel.Ai;

namespace RelayDesk.Modules.Tickets.Application;

/// <summary>Read-only ticket view for AI features, inside the current tenant (query filter + RLS).</summary>
internal sealed class TicketReader(TicketsDbContext db) : ITicketReader
{
    public async Task<TicketForAi?> GetAsync(Guid ticketId, CancellationToken ct)
    {
        var t = await db.Tickets.AsNoTracking()
            .Include(x => x.Messages.OrderBy(m => m.CreatedAt))
            .SingleOrDefaultAsync(x => x.Id == ticketId, ct);
        return t is null
            ? null
            : new TicketForAi(t.Id, t.Number, t.Subject, t.Priority.ToString(), t.Tags,
                t.Messages.Select(m => new TicketMessageForAi(m.AuthorType.ToString(), m.Body, m.IsInternal, m.CreatedAt)).ToList());
    }
}
