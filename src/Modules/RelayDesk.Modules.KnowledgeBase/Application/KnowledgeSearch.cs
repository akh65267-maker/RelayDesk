using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using RelayDesk.Modules.KnowledgeBase.Persistence;
using RelayDesk.SharedKernel.Ai;

namespace RelayDesk.Modules.KnowledgeBase.Application;

/// <summary>
/// Hybrid retrieval (ADR 0008): semantic neighbours by cosine distance plus keyword matches by
/// PostgreSQL full-text rank, merged with reciprocal rank fusion. Keyword search catches exact terms
/// (product names, error codes) that small embedding models miss; vectors catch paraphrases.
/// RLS and the query filter keep it inside the current tenant.
/// </summary>
internal sealed class KnowledgeSearch(KnowledgeBaseDbContext db, IEmbeddingGenerator<string, Embedding<float>> embeddings)
    : IKnowledgeSearch
{
    private const int Candidates = 12;
    private const int RrfK = 60;

    public async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(string query, int take, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var vector = new Vector((await embeddings.GenerateAsync([query], cancellationToken: ct))[0].Vector);

        var semantic = await db.Chunks.AsNoTracking()
            .Where(c => c.Embedding != null)
            .OrderBy(c => c.Embedding!.CosineDistance(vector))
            .Take(Candidates)
            .Select(c => new { c.Id, c.ArticleId, c.Title, c.Content })
            .ToListAsync(ct);

        var keyword = await db.Chunks.AsNoTracking()
            .Where(c => c.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("english", query)))
            .OrderByDescending(c => c.SearchVector.Rank(EF.Functions.WebSearchToTsQuery("english", query)))
            .Take(Candidates)
            .Select(c => new { c.Id, c.ArticleId, c.Title, c.Content })
            .ToListAsync(ct);

        var scores = new Dictionary<Guid, (double Score, Guid ArticleId, string Title, string Content)>();
        void Add<T>(IEnumerable<T> ranked, Func<T, (Guid Id, Guid ArticleId, string Title, string Content)> row)
        {
            var rank = 0;
            foreach (var item in ranked)
            {
                var r = row(item);
                var score = 1.0 / (RrfK + ++rank);
                scores[r.Id] = scores.TryGetValue(r.Id, out var s) ? (s.Score + score, r.ArticleId, r.Title, r.Content) : (score, r.ArticleId, r.Title, r.Content);
            }
        }
        Add(semantic, c => (c.Id, c.ArticleId, c.Title, c.Content));
        Add(keyword, c => (c.Id, c.ArticleId, c.Title, c.Content));

        return scores.Values
            .OrderByDescending(s => s.Score)
            .Take(take)
            .Select(s => new KnowledgeHit(s.ArticleId, s.Title, s.Content, s.Score))
            .ToList();
    }
}
