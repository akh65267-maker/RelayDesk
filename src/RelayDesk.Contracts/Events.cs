namespace RelayDesk.Contracts;

/// <summary>
/// Integration events shared between modules and services (ADR 0006). Published through the
/// transactional outbox, so they are only delivered if the change that produced them was committed.
/// Every event is scoped to one tenant; consumers must set the tenant context from it.
/// </summary>
public interface IIntegrationEvent
{
    Guid TenantId { get; }
    string? ActorUserId { get; }
    DateTimeOffset OccurredAt { get; }
}

// ---- Tenancy -----------------------------------------------------------

public sealed record TenantCreated(Guid TenantId, string? ActorUserId, DateTimeOffset OccurredAt,
    string Name, string Slug) : IIntegrationEvent;

public sealed record InvitationCreated(Guid TenantId, string? ActorUserId, DateTimeOffset OccurredAt,
    Guid InvitationId, string Email, string Role) : IIntegrationEvent;

public sealed record MemberJoined(Guid TenantId, string? ActorUserId, DateTimeOffset OccurredAt,
    string UserId, string Email, string Role) : IIntegrationEvent;

// ---- Tickets -----------------------------------------------------------

public sealed record TicketCreated(Guid TenantId, string? ActorUserId, DateTimeOffset OccurredAt,
    Guid TicketId, int Number, string Subject, string Priority, string RequesterEmail) : IIntegrationEvent;

public sealed record TicketStatusChanged(Guid TenantId, string? ActorUserId, DateTimeOffset OccurredAt,
    Guid TicketId, string From, string To) : IIntegrationEvent;

public sealed record TicketAssigned(Guid TenantId, string? ActorUserId, DateTimeOffset OccurredAt,
    Guid TicketId, string? AssigneeUserId) : IIntegrationEvent;

public sealed record TicketUpdated(Guid TenantId, string? ActorUserId, DateTimeOffset OccurredAt,
    Guid TicketId, string Priority, IReadOnlyList<string> Tags) : IIntegrationEvent;

public sealed record TicketMessageAdded(Guid TenantId, string? ActorUserId, DateTimeOffset OccurredAt,
    Guid TicketId, Guid MessageId, bool IsInternal, string AuthorType) : IIntegrationEvent;
