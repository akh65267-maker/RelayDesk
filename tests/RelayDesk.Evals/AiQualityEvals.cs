using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OllamaSharp;
using RelayDesk.Contracts;
using RelayDesk.IntegrationTests.Infrastructure;
using RelayDesk.Modules.Ai.Application;
using RelayDesk.Modules.Ai.Persistence;
using RelayDesk.Modules.KnowledgeBase.Application;
using RelayDesk.Modules.KnowledgeBase.Handlers;
using RelayDesk.Modules.KnowledgeBase.Persistence;
using RelayDesk.SharedKernel.Ai;
using RelayDesk.SharedKernel.Tenancy;
using Wolverine.EntityFrameworkCore;
using Xunit.Abstractions;

namespace RelayDesk.Evals;

/// <summary>Runs only when RELAYDESK_EVAL_OLLAMA points at an Ollama with the configured models.</summary>
public sealed class EvalFactAttribute : FactAttribute
{
    public EvalFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AiQualityEvals.EndpointVariable)))
            Skip = $"Set {AiQualityEvals.EndpointVariable} (e.g. http://localhost:11434) to run AI evals.";
    }
}

/// <summary>
/// Quality gates for the AI features (ADR 0008), against the real models, the real hybrid search and the
/// real prompts. Thresholds are set for the small local default model; tighten them for stronger models.
/// CI runs this suite when prompts, retrieval or the dataset change.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AiQualityEvals(RelayApiFactory api, ITestOutputHelper output)
{
    public const string EndpointVariable = "RELAYDESK_EVAL_OLLAMA";

    private static readonly AiOptions Options = new()
    {
        Endpoint = Environment.GetEnvironmentVariable(EndpointVariable) ?? "http://localhost:11434",
        ChatModel = Environment.GetEnvironmentVariable("RELAYDESK_EVAL_CHAT_MODEL") ?? new AiOptions().ChatModel,
    };

    private sealed record DatasetArticle(string Key, string Title, string Body);
    private sealed record RetrievalCase(string Question, string Expected);
    private sealed record ReplyCase(string Subject, string Message, string Expected, string[] Facts);
    private sealed record TriageCase(string Subject, string Message, string Category);
    private sealed record Dataset(DatasetArticle[] Articles, RetrievalCase[] Retrieval, ReplyCase[] Replies, TriageCase[] Triage);
    private sealed record Created(Guid Id);

    private static readonly Dataset Data = JsonSerializer.Deserialize<Dataset>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "dataset.json")), JsonSerializerOptions.Web)!;

    private static IEmbeddingGenerator<string, Embedding<float>> Embeddings() =>
        new OllamaApiClient(new HttpClient { BaseAddress = new Uri(Options.Endpoint), Timeout = Options.RequestTimeout }, Options.EmbeddingModel);

    private static IChatClient Chat() =>
        new OllamaApiClient(new HttpClient { BaseAddress = new Uri(Options.Endpoint), Timeout = Options.RequestTimeout }, Options.ChatModel);

    /// <summary>A workspace with the dataset's articles published and indexed with the real embedding model.</summary>
    private async Task<(HttpClient Client, Guid TenantId, string OwnerId, Dictionary<Guid, string> KeyOf)> SetUpAsync()
    {
        var owner = TestUser.New("eval");
        var tenant = await (await api.ClientFor(owner).PostAsJsonAsync("/api/tenancy/tenants",
            new { name = "Evals", slug = $"eval-{Guid.NewGuid():N}"[..20] })).Content.ReadFromJsonAsync<Created>();
        var client = api.ClientFor(owner, tenant!.Id);

        var keyOf = new Dictionary<Guid, string>();
        foreach (var a in Data.Articles)
        {
            var created = await (await client.PostAsJsonAsync("/api/kb/articles", new { a.Title, a.Body, publish = true }))
                .Content.ReadFromJsonAsync<Created>();
            keyOf[created!.Id] = a.Key;
            using var scope = api.Services.CreateScope();
            await new ArticleIndexer(scope.ServiceProvider.GetRequiredService<KnowledgeBaseDbContext>(), Embeddings())
                .ReindexAsync(new ArticlePublished(tenant.Id, null, DateTimeOffset.UtcNow, created.Id, a.Title),
                    scope.ServiceProvider.GetRequiredService<TenantContext>(), default);
        }
        return (client, tenant.Id, owner.Id, keyOf);
    }

    [EvalFact]
    public async Task Retrieval_finds_the_right_article_in_the_top_3()
    {
        var (_, tenantId, _, keyOf) = await SetUpAsync();
        var hits = 0;
        foreach (var c in Data.Retrieval)
        {
            using var scope = api.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetFromEvent(new ArticleUnpublished(tenantId, null, DateTimeOffset.UtcNow, Guid.Empty));
            var top = await new KnowledgeSearch(scope.ServiceProvider.GetRequiredService<KnowledgeBaseDbContext>(), Embeddings())
                .SearchAsync(c.Question, 3, default);
            var found = top.Any(h => keyOf[h.ArticleId] == c.Expected);
            hits += found ? 1 : 0;
            output.WriteLine($"{(found ? "PASS" : "MISS")}  {c.Question}  ->  {string.Join(", ", top.Select(h => keyOf[h.ArticleId]))}");
        }

        var score = (double)hits / Data.Retrieval.Length;
        output.WriteLine($"hit@3 = {score:P0}");
        Assert.True(score >= 0.75, $"Retrieval hit@3 {score:P0} is below 75%.");
    }

    [EvalFact]
    public async Task Replies_cite_the_right_source_and_state_its_facts()
    {
        var (client, tenantId, ownerId, keyOf) = await SetUpAsync();
        var good = 0;
        foreach (var c in Data.Replies)
        {
            var ticket = await (await client.PostAsJsonAsync("/api/tickets",
                new { c.Subject, body = c.Message, requesterEmail = "customer@example.test" })).Content.ReadFromJsonAsync<Created>();
            var suggestion = await (await client.PostAsJsonAsync($"/api/ai/tickets/{ticket!.Id}/suggestions", new { kind = "Reply" }))
                .Content.ReadFromJsonAsync<Created>();

            using (var scope = api.Services.CreateScope())
                await Assistant(scope.ServiceProvider).HandleRequestAsync(
                    new AiSuggestionRequested(tenantId, ownerId, DateTimeOffset.UtcNow, ticket.Id, suggestion!.Id, "Reply"), default);

            var reply = (await client.GetFromJsonAsync<JsonElement[]>($"/api/ai/tickets/{ticket.Id}/suggestions"))!
                .Single(s => s.GetProperty("kind").GetString() == "Reply");
            var text = reply.GetProperty("content").GetString() ?? "";
            var sources = reply.GetProperty("data").EnumerateArray()
                .Select(s => (N: s.GetProperty("n").GetInt32(), Key: keyOf[s.GetProperty("articleId").GetGuid()])).ToList();

            var cited = sources.Where(s => text.Contains($"[{s.N}]")).Select(s => s.Key).ToList();
            // Redaction placeholders ([phone], [email] ...) must never leak into a draft a customer could receive.
            var placeholder = System.Text.RegularExpressions.Regex.IsMatch(text, @"\[(email|phone|card|iban|name)[^\]]*\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var ok = cited.Contains(c.Expected) && c.Facts.Any(f => text.Contains(f, StringComparison.OrdinalIgnoreCase)) && !placeholder;
            good += ok ? 1 : 0;
            output.WriteLine($"{(ok ? "PASS" : "FAIL")}  {c.Subject}: cited [{string.Join(",", cited)}]{(placeholder ? " (placeholder leaked)" : "")}\n{text}\n");
        }

        Assert.True(good >= Data.Replies.Length - 1, $"Only {good}/{Data.Replies.Length} replies were grounded.");
    }

    [EvalFact]
    public async Task Triage_picks_the_right_category()
    {
        var (client, tenantId, ownerId, _) = await SetUpAsync();
        var right = 0;
        foreach (var c in Data.Triage)
        {
            var ticket = await (await client.PostAsJsonAsync("/api/tickets",
                new { c.Subject, body = c.Message, requesterEmail = "customer@example.test" })).Content.ReadFromJsonAsync<Created>();
            using (var scope = api.Services.CreateScope())
                await Assistant(scope.ServiceProvider).TriageNewTicketAsync(
                    new TicketCreated(tenantId, ownerId, DateTimeOffset.UtcNow, ticket!.Id, 0, c.Subject, "Normal", "customer@example.test"), default);

            var triage = (await client.GetFromJsonAsync<JsonElement[]>($"/api/ai/tickets/{ticket.Id}/suggestions"))!
                .Single(s => s.GetProperty("kind").GetString() == "Triage");
            var category = triage.GetProperty("data").GetProperty("category").GetString();
            right += category == c.Category ? 1 : 0;
            output.WriteLine($"{(category == c.Category ? "PASS" : "MISS")}  {c.Subject}: {category} (expected {c.Category})");
        }

        var accuracy = (double)right / Data.Triage.Length;
        output.WriteLine($"accuracy = {accuracy:P0}");
        Assert.True(accuracy >= 0.6, $"Triage accuracy {accuracy:P0} is below 60%.");
    }

    private AiAssistant Assistant(IServiceProvider sp) => new(
        sp.GetRequiredService<AiDbContext>(),
        sp.GetRequiredService<IDbContextOutbox<AiDbContext>>(),
        sp.GetRequiredService<TenantContext>(),
        sp.GetRequiredService<ITicketReader>(),
        new KnowledgeSearch(sp.GetRequiredService<KnowledgeBaseDbContext>(), Embeddings()),
        Chat(),
        Microsoft.Extensions.Options.Options.Create(Options),
        TimeProvider.System,
        NullLogger<AiAssistant>.Instance);
}
