using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RelayDesk.Contracts;
using RelayDesk.Modules.Ai.Domain;
using RelayDesk.Modules.Ai.Persistence;
using RelayDesk.SharedKernel.Ai;
using RelayDesk.SharedKernel.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace RelayDesk.Modules.Ai.Application;

internal sealed record TriageResult(string Category, string Priority, string Sentiment, string Reason);

internal sealed record SourceRef(int N, Guid ArticleId, string Title);

/// <summary>
/// Produces suggestions in the AI worker (ADR 0008). Every call: tenant context from the event, quota check,
/// PII-redacted prompt, usage recorded, result saved and announced through the outbox. AI is an enhancement:
/// any failure marks the suggestion Failed and the rest of the product carries on.
/// </summary>
internal sealed class AiAssistant(
    AiDbContext db,
    IDbContextOutbox<AiDbContext> outbox,
    TenantContext tenant,
    ITicketReader tickets,
    IKnowledgeSearch knowledge,
    IChatClient chat,
    IOptions<AiOptions> options,
    TimeProvider clock,
    ILogger<AiAssistant> log)
{
    public static readonly string[] Categories = ["billing", "technical", "account", "shipping", "other"];
    public static readonly string[] Priorities = ["Low", "Normal", "High", "Urgent"];
    public static readonly string[] Sentiments = ["positive", "neutral", "negative"];

    /// <summary>Every new ticket is triaged automatically.</summary>
    public async Task TriageNewTicketAsync(TicketCreated e, CancellationToken ct)
    {
        tenant.SetFromEvent(e);
        var suggestion = new Suggestion { TicketId = e.TicketId, Kind = SuggestionKind.Triage };
        db.Suggestions.Add(suggestion);
        await db.SaveChangesAsync(ct);
        await GenerateAsync(suggestion, e.ActorUserId, ct);
    }

    /// <summary>Reply drafts and summaries are requested by an agent.</summary>
    public async Task HandleRequestAsync(AiSuggestionRequested e, CancellationToken ct)
    {
        tenant.SetFromEvent(e);
        var suggestion = await db.Suggestions.SingleOrDefaultAsync(s => s.Id == e.SuggestionId, ct);
        if (suggestion is null || suggestion.Status != SuggestionStatus.Pending) return; // duplicate delivery
        await GenerateAsync(suggestion, e.ActorUserId, ct);
    }

    private async Task GenerateAsync(Suggestion suggestion, string? actor, CancellationToken ct)
    {
        var monthStart = new DateTimeOffset(clock.GetUtcNow().Year, clock.GetUtcNow().Month, 1, 0, 0, 0, TimeSpan.Zero);
        try
        {
            if (await db.TokensSinceAsync(monthStart, ct) >= options.Value.MonthlyTokenQuota)
            {
                suggestion.Status = SuggestionStatus.QuotaExceeded;
            }
            else if (await tickets.GetAsync(suggestion.TicketId, ct) is not { } ticket)
            {
                suggestion.Status = SuggestionStatus.Failed;
                suggestion.Error = "Ticket not found.";
            }
            else
            {
                switch (suggestion.Kind)
                {
                    case SuggestionKind.Triage: await TriageAsync(suggestion, ticket, ct); break;
                    case SuggestionKind.Reply: await ReplyAsync(suggestion, ticket, ct); break;
                    case SuggestionKind.Summary: await SummaryAsync(suggestion, ticket, ct); break;
                }
                suggestion.Status = SuggestionStatus.Ready;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "AI {Kind} for ticket {TicketId} failed", suggestion.Kind, suggestion.TicketId);
            suggestion.Status = SuggestionStatus.Failed;
            suggestion.Error = ex.Message.Length > 900 ? ex.Message[..900] : ex.Message;
        }

        suggestion.CompletedAt = clock.GetUtcNow();
        await outbox.PublishAsync(new AiSuggestionReady(tenant.TenantId!.Value, actor, clock.GetUtcNow(),
            suggestion.TicketId, suggestion.Id, suggestion.Kind.ToString(), suggestion.Status.ToString()));
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
    }

    private async Task TriageAsync(Suggestion s, TicketForAi ticket, CancellationToken ct)
    {
        var response = await CallAsync("triage", s, [
            new(ChatRole.System, Prompts.TriageSystem),
            new(ChatRole.User, Prompts.Conversation(ticket, includeInternal: false)),
        ], new ChatOptions { ResponseFormat = ChatResponseFormat.Json, Temperature = 0 }, ct);

        var triage = ParseTriage(response.Text);
        s.Content = triage.Reason;
        s.Data = JsonSerializer.Serialize(triage, JsonSerializerOptions.Web);
    }

    private async Task ReplyAsync(Suggestion s, TicketForAi ticket, CancellationToken ct)
    {
        // Several chunks of one article become one numbered source, so citations and the source list match articles.
        var hits = (await knowledge.SearchAsync(Prompts.RetrievalQuery(ticket), 4, ct))
            .GroupBy(h => h.ArticleId)
            .Select(g => new KnowledgeHit(g.Key, g.First().Title, string.Join("\n\n", g.Select(h => h.Content)), g.Max(h => h.Score)))
            .ToList();
        var response = await CallAsync("reply", s, [
            new(ChatRole.System, Prompts.ReplySystem),
            new(ChatRole.User, $"Knowledge-base sources:\n{Prompts.Sources(hits)}\nConversation:\n{Prompts.Conversation(ticket, includeInternal: false)}\nDraft the agent's next reply."),
        ], new ChatOptions { Temperature = 0.2f }, ct);

        s.Content = response.Text.Trim();
        s.Data = JsonSerializer.Serialize(hits.Select((h, i) => new SourceRef(i + 1, h.ArticleId, h.Title)), JsonSerializerOptions.Web);
    }

    private async Task SummaryAsync(Suggestion s, TicketForAi ticket, CancellationToken ct)
    {
        var response = await CallAsync("summary", s, [
            new(ChatRole.System, Prompts.SummarySystem),
            new(ChatRole.User, Prompts.Conversation(ticket, includeInternal: true)),
        ], new ChatOptions { Temperature = 0.2f }, ct);
        s.Content = response.Text.Trim();
    }

    private async Task<ChatResponse> CallAsync(string feature, Suggestion s, List<ChatMessage> messages, ChatOptions chatOptions, CancellationToken ct)
    {
        var response = await chat.GetResponseAsync(messages, chatOptions, ct);
        s.Model = response.ModelId ?? options.Value.ChatModel;
        db.Usage.Add(new UsageEntry
        {
            Feature = feature,
            Model = s.Model,
            InputTokens = response.Usage?.InputTokenCount ?? 0,
            OutputTokens = response.Usage?.OutputTokenCount ?? 0,
            OccurredAt = clock.GetUtcNow(),
        });
        return response;
    }

    /// <summary>Small models drift from the schema: unknown values fall back to safe defaults instead of failing.</summary>
    internal static TriageResult ParseTriage(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        string Pick(string name, string[] allowed, string fallback)
        {
            var value = doc.RootElement.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            return allowed.FirstOrDefault(a => string.Equals(a, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? fallback;
        }
        var reason = doc.RootElement.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : "";
        return new TriageResult(
            Pick("category", Categories, "other"),
            Pick("priority", Priorities, "Normal"),
            Pick("sentiment", Sentiments, "neutral"),
            reason.Length > 300 ? reason[..300] : reason);
    }
}
