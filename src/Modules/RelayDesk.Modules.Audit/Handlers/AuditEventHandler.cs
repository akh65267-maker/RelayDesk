using RelayDesk.Contracts;
using RelayDesk.SharedKernel.Auditing;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.Audit.Handlers;

/// <summary>
/// Records integration events in the audit log. Wolverine discovers these handlers and runs them
/// from durable local queues, so an entry is written once per event even across retries and restarts.
/// Public because Wolverine generates code that calls them.
/// </summary>
public static class AuditEventHandler
{
    public static Task Handle(TenantCreated e, TenantContext ctx, IAuditLog audit, CancellationToken ct) =>
        Write(e, ctx, audit, "tenant.created", "tenant", e.TenantId, new { e.Name, e.Slug }, ct);

    public static Task Handle(InvitationCreated e, TenantContext ctx, IAuditLog audit, CancellationToken ct) =>
        Write(e, ctx, audit, "invitation.created", "invitation", e.InvitationId, new { e.Email, e.Role }, ct);

    public static Task Handle(MemberJoined e, TenantContext ctx, IAuditLog audit, CancellationToken ct) =>
        Write(e, ctx, audit, "member.joined", "member", e.UserId, new { e.Email, e.Role }, ct);

    public static Task Handle(TicketCreated e, TenantContext ctx, IAuditLog audit, CancellationToken ct) =>
        Write(e, ctx, audit, "ticket.created", "ticket", e.TicketId, new { e.Number, e.Subject, e.Priority }, ct);

    public static Task Handle(TicketStatusChanged e, TenantContext ctx, IAuditLog audit, CancellationToken ct) =>
        Write(e, ctx, audit, "ticket.status_changed", "ticket", e.TicketId, new { e.From, e.To }, ct);

    public static Task Handle(TicketAssigned e, TenantContext ctx, IAuditLog audit, CancellationToken ct) =>
        Write(e, ctx, audit, "ticket.assigned", "ticket", e.TicketId, new { e.AssigneeUserId }, ct);

    public static Task Handle(TicketUpdated e, TenantContext ctx, IAuditLog audit, CancellationToken ct) =>
        Write(e, ctx, audit, "ticket.updated", "ticket", e.TicketId, new { e.Priority, e.Tags }, ct);

    public static Task Handle(TicketMessageAdded e, TenantContext ctx, IAuditLog audit, CancellationToken ct) =>
        Write(e, ctx, audit, e.IsInternal ? "ticket.note_added" : "ticket.reply_added", "ticket", e.TicketId,
            new { e.MessageId, e.AuthorType }, ct);

    private static Task Write(IIntegrationEvent e, TenantContext ctx, IAuditLog audit,
        string action, string entityType, object entityId, object data, CancellationToken ct)
    {
        // Must happen before the audit DbContext opens its connection, so RLS sees the event's tenant.
        ctx.SetFromEvent(e);
        return audit.WriteAsync(action, entityType, entityId.ToString()!, data, e.OccurredAt, ct);
    }
}
