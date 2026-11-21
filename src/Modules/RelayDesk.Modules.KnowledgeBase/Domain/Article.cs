using NpgsqlTypes;
using Pgvector;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.KnowledgeBase.Domain;

internal enum ArticleStatus { Draft, Published }

/// <summary>A help-center article written in Markdown. Only published articles are searchable.</summary>
internal sealed class Article : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public ArticleStatus Status { get; set; } = ArticleStatus.Draft;
    public required string CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>
/// A searchable piece of a published article: its embedding for semantic search plus a generated
/// tsvector for keyword search (hybrid retrieval, ADR 0008). Rebuilt whenever the article is (re)published.
/// </summary>
internal sealed class ArticleChunk : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid ArticleId { get; init; }
    public int Ordinal { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public Vector? Embedding { get; set; }
    public NpgsqlTsVector SearchVector { get; init; } = null!;
}
