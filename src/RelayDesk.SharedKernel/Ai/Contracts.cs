namespace RelayDesk.SharedKernel.Ai;

/// <summary>A ticket as the AI sees it. Read inside the current tenant context.</summary>
public sealed record TicketForAi(Guid Id, int Number, string Subject, string Priority, IReadOnlyList<string> Tags,
    IReadOnlyList<TicketMessageForAi> Messages);

public sealed record TicketMessageForAi(string AuthorType, string Body, bool IsInternal, DateTimeOffset CreatedAt);

/// <summary>Read-only access to tickets for AI features; implemented by the Tickets module.</summary>
public interface ITicketReader
{
    Task<TicketForAi?> GetAsync(Guid ticketId, CancellationToken ct);
}

public sealed record KnowledgeHit(Guid ArticleId, string Title, string Content, double Score);

/// <summary>Hybrid (vector + full-text) search over the current tenant's published articles; implemented by the KnowledgeBase module.</summary>
public interface IKnowledgeSearch
{
    Task<IReadOnlyList<KnowledgeHit>> SearchAsync(string query, int take, CancellationToken ct);
}
