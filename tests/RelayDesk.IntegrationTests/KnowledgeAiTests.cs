using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
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

namespace RelayDesk.IntegrationTests;

/// <summary>
/// Phase 4: knowledge base and AI suggestions through the real database (RLS, outbox, usage ledger).
/// The AI worker's steps are run in-process with a fake model and fake embeddings, so results are deterministic.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class KnowledgeAiTests(RelayApiFactory api)
{
    private sealed record TenantResponse(Guid Id);
    private sealed record Article(Guid Id, string Title, string Status);
    private sealed record Ticket(Guid Id, int Number);
    private sealed record Suggestion(Guid Id, string Kind, string Status, string? Content, JsonElement? Data);
    private sealed record Usage(long TokensThisMonth, long MonthlyQuota);

    private async Task<(TestUser Owner, Guid TenantId)> SignUpAsync(string name)
    {
        var owner = TestUser.New(name);
        var res = await api.ClientFor(owner).PostAsJsonAsync("/api/tenancy/tenants", new { name, slug = $"{name}-{Guid.NewGuid():N}"[..20] });
        Assert.True(res.StatusCode == HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        return (owner, (await res.Content.ReadFromJsonAsync<TenantResponse>())!.Id);
    }

    private static async Task<Article> PublishAsync(HttpClient client, string title, string body)
    {
        var res = await client.PostAsJsonAsync("/api/kb/articles", new { title, body, publish = true });
        Assert.True(res.StatusCode == HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<Article>())!;
    }

    /// <summary>What the AI worker does for ArticlePublished, with fake embeddings.</summary>
    private async Task IndexAsync(Guid tenantId, Guid articleId)
    {
        using var scope = api.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await new ArticleIndexer(sp.GetRequiredService<KnowledgeBaseDbContext>(), new FakeEmbeddings())
            .ReindexAsync(new ArticlePublished(tenantId, null, DateTimeOffset.UtcNow, articleId, ""), sp.GetRequiredService<TenantContext>(), default);
    }

    /// <summary>The AI worker's assistant, wired with the fake model and embeddings.</summary>
    private AiAssistant Assistant(IServiceProvider sp, FakeChatClient chat) => new(
        sp.GetRequiredService<AiDbContext>(),
        sp.GetRequiredService<IDbContextOutbox<AiDbContext>>(),
        sp.GetRequiredService<TenantContext>(),
        sp.GetRequiredService<ITicketReader>(),
        new KnowledgeSearch(sp.GetRequiredService<KnowledgeBaseDbContext>(), new FakeEmbeddings()),
        chat,
        sp.GetRequiredService<IOptions<AiOptions>>(),
        TimeProvider.System,
        NullLogger<AiAssistant>.Instance);

    [Fact]
    public async Task Reply_draft_is_grounded_in_the_knowledge_base_and_never_sees_personal_data()
    {
        var (owner, tenantId) = await SignUpAsync("kbai");
        var client = api.ClientFor(owner, tenantId);
        var reset = await PublishAsync(client, "Resetting your password",
            "# Forgot password\n\nOpen the sign-in page and choose \"Forgot password\". We email a reset link that works for 1 hour.");
        var billing = await PublishAsync(client, "Refunds", "Refunds go back to the original payment method within 5 to 10 days.");
        await IndexAsync(tenantId, reset.Id);
        await IndexAsync(tenantId, billing.Id);

        var ticket = (await (await client.PostAsJsonAsync("/api/tickets", new
        {
            subject = "Can't log in",
            body = "I forgot my password and can't sign in. Reach me at lena.private@example.com or +49 151 2345 6789.",
            requesterEmail = "lena.private@example.com",
        })).Content.ReadFromJsonAsync<Ticket>())!;

        // The agent asks for a draft: accepted and pending; a double click doesn't queue a second call.
        var first = await client.PostAsJsonAsync($"/api/ai/tickets/{ticket.Id}/suggestions", new { kind = "Reply" });
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var pending = (await first.Content.ReadFromJsonAsync<Suggestion>())!;
        Assert.Equal("Pending", pending.Status);
        var again = (await (await client.PostAsJsonAsync($"/api/ai/tickets/{ticket.Id}/suggestions", new { kind = "Reply" }))
            .Content.ReadFromJsonAsync<Suggestion>())!;
        Assert.Equal(pending.Id, again.Id);

        // The worker generates it.
        var chat = new FakeChatClient();
        using (var scope = api.Services.CreateScope())
            await Assistant(scope.ServiceProvider, chat).HandleRequestAsync(
                new AiSuggestionRequested(tenantId, owner.Id, DateTimeOffset.UtcNow, ticket.Id, pending.Id, "Reply"), default);

        var reply = (await client.GetFromJsonAsync<List<Suggestion>>($"/api/ai/tickets/{ticket.Id}/suggestions"))!.Single(s => s.Kind == "Reply");
        Assert.Equal("Ready", reply.Status);
        Assert.Contains("[1]", reply.Content);
        Assert.Equal("Resetting your password", reply.Data!.Value[0].GetProperty("title").GetString());

        // The prompt carried the right article and no personal data.
        var prompt = string.Join("\n", chat.Calls.Single().Select(m => m.Text));
        Assert.Contains("We email a reset link", prompt);
        Assert.DoesNotContain("lena.private@example.com", prompt);
        Assert.DoesNotContain("2345 6789", prompt);
        Assert.Contains("[email]", prompt);

        // Usage is recorded for the workspace.
        var usage = await client.GetFromJsonAsync<Usage>("/api/ai/usage");
        Assert.Equal(150, usage!.TokensThisMonth);
    }

    [Fact]
    public async Task New_tickets_are_triaged_and_summaries_include_internal_notes()
    {
        var (owner, tenantId) = await SignUpAsync("triage");
        var client = api.ClientFor(owner, tenantId);
        var ticket = (await (await client.PostAsJsonAsync("/api/tickets", new
        {
            subject = "Locked out of my account",
            body = "I can't log in since this morning!",
            requesterEmail = "c@example.test",
        })).Content.ReadFromJsonAsync<Ticket>())!;
        await client.PostAsJsonAsync($"/api/tickets/{ticket.Id}/messages", new { body = "SECRET-NOTE: checked SSO logs", @internal = true });

        var chat = new FakeChatClient();
        using (var scope = api.Services.CreateScope())
            await Assistant(scope.ServiceProvider, chat).TriageNewTicketAsync(
                new TicketCreated(tenantId, owner.Id, DateTimeOffset.UtcNow, ticket.Id, ticket.Number, "Locked out of my account", "Normal", "c@example.test"), default);

        var summaryRequest = (await (await client.PostAsJsonAsync($"/api/ai/tickets/{ticket.Id}/suggestions", new { kind = "Summary" }))
            .Content.ReadFromJsonAsync<Suggestion>())!;
        using (var scope = api.Services.CreateScope())
            await Assistant(scope.ServiceProvider, chat).HandleRequestAsync(
                new AiSuggestionRequested(tenantId, owner.Id, DateTimeOffset.UtcNow, ticket.Id, summaryRequest.Id, "Summary"), default);

        var all = (await client.GetFromJsonAsync<List<Suggestion>>($"/api/ai/tickets/{ticket.Id}/suggestions"))!;
        var triage = all.Single(s => s.Kind == "Triage");
        Assert.Equal("Ready", triage.Status);
        Assert.Equal("account", triage.Data!.Value.GetProperty("category").GetString());
        Assert.Equal("High", triage.Data.Value.GetProperty("priority").GetString());
        Assert.Equal("Ready", all.Single(s => s.Kind == "Summary").Status);

        // Triage never sees internal notes; summaries (for the team) do.
        Assert.DoesNotContain("SECRET-NOTE", string.Join("\n", chat.Calls[0].Select(m => m.Text)));
        Assert.Contains("SECRET-NOTE", string.Join("\n", chat.Calls[1].Select(m => m.Text)));
    }

    [Fact]
    public async Task Requests_stop_when_the_monthly_quota_is_used_up()
    {
        var (owner, tenantId) = await SignUpAsync("quota");
        var client = api.ClientFor(owner, tenantId);
        var ticket = (await (await client.PostAsJsonAsync("/api/tickets", new { subject = "Hi", body = "Hello", requesterEmail = "q@example.test" }))
            .Content.ReadFromJsonAsync<Ticket>())!;

        await using (var conn = new NpgsqlConnection(api.OwnerConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO ai.usage (id, tenant_id, feature, model, input_tokens, output_tokens, occurred_at) VALUES (gen_random_uuid(), @t, 'reply', 'm', 10000000, 0, now())", conn);
            cmd.Parameters.AddWithValue("t", tenantId);
            await cmd.ExecuteNonQueryAsync();
        }

        var res = await client.PostAsJsonAsync($"/api/ai/tickets/{ticket.Id}/suggestions", new { kind = "Reply" });

        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
    }

    [Fact]
    public async Task Agents_read_articles_but_only_admins_write_and_tenants_are_isolated()
    {
        var (ownerA, tenantA) = await SignUpAsync("kb-a");
        var (ownerB, tenantB) = await SignUpAsync("kb-b");
        var article = await PublishAsync(api.ClientFor(ownerA, tenantA), "Shipping times", "We ship in 3 days.");

        var agent = TestUser.New("agent");
        var invite = await api.ClientFor(ownerA, tenantA).PostAsJsonAsync("/api/tenancy/invitations", new { email = agent.Email, role = "Agent" });
        var token = (await invite.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        await api.ClientFor(agent).PostAsJsonAsync("/api/tenancy/invitations/accept", new { token });

        var agentClient = api.ClientFor(agent, tenantA);
        Assert.Single((await agentClient.GetFromJsonAsync<List<Article>>("/api/kb/articles"))!);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await agentClient.PostAsJsonAsync("/api/kb/articles", new { title = "x", body = "y", publish = false })).StatusCode);

        var clientB = api.ClientFor(ownerB, tenantB);
        Assert.Empty((await clientB.GetFromJsonAsync<List<Article>>("/api/kb/articles"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/kb/articles/{article.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.DeleteAsync($"/api/kb/articles/{article.Id}")).StatusCode);
    }

    [Fact]
    public async Task Unpublishing_removes_an_article_from_search()
    {
        var (owner, tenantId) = await SignUpAsync("unpub");
        var client = api.ClientFor(owner, tenantId);
        var article = await PublishAsync(client, "Gift cards", "Gift cards never expire.");
        await IndexAsync(tenantId, article.Id);

        async Task<int> HitsAsync()
        {
            using var scope = api.Services.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<TenantContext>().SetFromEvent(new ArticleUnpublished(tenantId, null, DateTimeOffset.UtcNow, article.Id));
            return (await new KnowledgeSearch(sp.GetRequiredService<KnowledgeBaseDbContext>(), new FakeEmbeddings())
                .SearchAsync("do gift cards expire", 5, default)).Count;
        }
        Assert.True(await HitsAsync() > 0);

        await client.PutAsJsonAsync($"/api/kb/articles/{article.Id}", new { title = "Gift cards", body = "Gift cards never expire.", publish = false });
        using (var scope = api.Services.CreateScope())
            await new ArticleIndexer(scope.ServiceProvider.GetRequiredService<KnowledgeBaseDbContext>(), new FakeEmbeddings())
                .RemoveAsync(new ArticleUnpublished(tenantId, null, DateTimeOffset.UtcNow, article.Id), scope.ServiceProvider.GetRequiredService<TenantContext>(), default);

        Assert.Equal(0, await HitsAsync());
    }
}
