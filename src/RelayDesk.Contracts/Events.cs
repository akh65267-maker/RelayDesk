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

/// <summary>VisitorId is set when the ticket came from the chat widget, so replies can reach that visitor.</summary>
public sealed record TicketMessageAdded(Guid TenantId, string? ActorUserId, DateTimeOffset OccurredAt,
    Guid TicketId, Guid MessageId, bool IsInternal, string AuthorType, Guid? VisitorId = null) : IIntegrationEvent;

// ---- Knowledge base ----------------------------------------------------

/// <summary>Published or edited while published: (re)index it for search.</summary>
public sealed record ArticlePublished(Guid TenantId, string? ActorUserId, DateTimeOffset OccurredAt,
    Guid ArticleId, string Title) : IIntegrationEvent;

/// <summary>Unpublished or deleted: remove it from search.</summary>
public sealed record ArticleUnpublished(Guid TenantId, string? ActorUserId, DateTimeOffset OccurredAt,
    Guid ArticleId) : IIntegrationEvent;

// ---- AI ----------------------------------------------------------------

/// <summary>Kind is "Triage", "Reply" or "Summary".</summary>
public sealed record AiSuggestionRequested(Guid TenantId, string? ActorUserId, DateTimeOffset OccurredAt,
    Guid TicketId, Guid SuggestionId, string Kind) : IIntegrationEvent;

/// <summary>Status is "Ready", "Failed" or "QuotaExceeded".</summary>
public sealed record AiSuggestionReady(Guid TenantId, string? ActorUserId, DateTimeOffset OccurredAt,
    Guid TicketId, Guid SuggestionId, string Kind, string Status) : IIntegrationEvent;
