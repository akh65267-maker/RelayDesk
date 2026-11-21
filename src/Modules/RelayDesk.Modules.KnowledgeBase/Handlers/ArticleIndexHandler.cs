using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Pgvector;
using RelayDesk.Contracts;
using RelayDesk.Modules.KnowledgeBase.Application;
using RelayDesk.Modules.KnowledgeBase.Domain;
using RelayDesk.Modules.KnowledgeBase.Persistence;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.KnowledgeBase.Handlers;

/// <summary>
/// Keeps the search index in step with articles. Runs in the AI worker (embedding is slow and can fail),
/// from a durable queue, so a failed embedding call is retried rather than lost.
/// Public because Wolverine generates code that calls it.
/// </summary>
public static class ArticleIndexHandler
{
    public static Task Handle(ArticlePublished e, TenantContext ctx, IServiceProvider services, CancellationToken ct) =>
        Indexer(services).ReindexAsync(e, ctx, ct);

    public static Task Handle(ArticleUnpublished e, TenantContext ctx, IServiceProvider services, CancellationToken ct) =>
        Indexer(services).RemoveAsync(e, ctx, ct);

    // Resolved here so the generated handler code only sees public types.
    private static ArticleIndexer Indexer(IServiceProvider services) =>
        (ArticleIndexer)services.GetService(typeof(ArticleIndexer))!;
}

internal sealed class ArticleIndexer(KnowledgeBaseDbContext db, IEmbeddingGenerator<string, Embedding<float>> embeddings)
{
    public async Task ReindexAsync(ArticlePublished e, TenantContext ctx, CancellationToken ct)
    {
        ctx.SetFromEvent(e); // before the first query, so RLS sees the event's tenant
        var article = await db.Articles.AsNoTracking().SingleOrDefaultAsync(a => a.Id == e.ArticleId, ct);
        if (article is null || article.Status != ArticleStatus.Published) return; // changed again since

        var pieces = Chunker.Split(article.Body);
        var vectors = pieces.Count == 0 ? [] : await embeddings.GenerateAsync(
            pieces.Select(p => $"{article.Title}\n\n{p}"), cancellationToken: ct);

        await db.Chunks.Where(c => c.ArticleId == article.Id).ExecuteDeleteAsync(ct);
        db.Chunks.AddRange(pieces.Select((content, i) => new ArticleChunk
        {
            ArticleId = article.Id,
            Ordinal = i,
            Title = article.Title,
            Content = content,
            Embedding = new Vector(vectors[i].Vector),
        }));
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(ArticleUnpublished e, TenantContext ctx, CancellationToken ct)
    {
        ctx.SetFromEvent(e);
        await db.Chunks.Where(c => c.ArticleId == e.ArticleId).ExecuteDeleteAsync(ct);
    }
}
