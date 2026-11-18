using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RelayDesk.IntegrationTests.Infrastructure;

namespace RelayDesk.IntegrationTests;

/// <summary>Phase 3 widget API: anonymous visitors chat through tickets, scoped to themselves.</summary>
[Collection(ApiCollection.Name)]
public sealed class WidgetTests(RelayApiFactory api)
{
    private sealed record TenantResponse(Guid Id, string Slug);
    private sealed record Session(string Token, Guid VisitorId);
    private sealed record Conversation(Guid Id, int Number, string Subject, string Status);
    private sealed record WidgetMessage(string From, string Body);
    private sealed record ConversationDetail(Conversation Conversation, List<WidgetMessage> Messages);
    private sealed record TicketDetail(string Status, string? RequesterName, List<string> Tags, List<object> Messages);
    private sealed record Page(List<Conversation> Items);

    private async Task<(TestUser Owner, TenantResponse Tenant)> SignUpAsync(string name)
    {
        var owner = TestUser.New(name);
        var res = await api.ClientFor(owner).PostAsJsonAsync("/api/tenancy/tenants",
            new { name, slug = $"{name}-{Guid.NewGuid():N}"[..20] });
        Assert.True(res.StatusCode == HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        return (owner, (await res.Content.ReadFromJsonAsync<TenantResponse>())!);
    }

    private async Task<HttpClient> VisitorAsync(string workspace, string? name = "Maria", string? email = null)
    {
        var res = await api.CreateClient().PostAsJsonAsync("/api/widget/sessions", new { workspace, name, email });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        var session = (await res.Content.ReadFromJsonAsync<Session>())!;
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        return client;
    }

    private static async Task<Conversation> StartChatAsync(HttpClient visitor, string body)
    {
        var res = await visitor.PostAsJsonAsync("/api/widget/conversations", new { body });
        Assert.True(res.StatusCode == HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<Conversation>())!;
    }

    [Fact]
    public async Task Unknown_workspace_gets_no_session()
    {
        var res = await api.CreateClient().PostAsJsonAsync("/api/widget/sessions", new { workspace = "does-not-exist" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Chat_becomes_a_ticket_and_visitor_never_sees_internal_notes()
    {
        var (owner, tenant) = await SignUpAsync("widget");
        var visitor = await VisitorAsync(tenant.Slug, "Maria", "maria@customer.test");

        var chat = await StartChatAsync(visitor, "Hi, my order #123 hasn't arrived.\nCan you check?");
        Assert.Equal("Hi, my order #123 hasn't arrived.", chat.Subject);

        // Agents see it in the inbox as a normal ticket.
        var agent = api.ClientFor(owner, tenant.Id);
        var ticket = await agent.GetFromJsonAsync<TicketDetail>($"/api/tickets/{chat.Id}");
        Assert.Equal("Maria", ticket!.RequesterName);
        Assert.Equal(["chat"], ticket.Tags);

        await agent.PostAsJsonAsync($"/api/tickets/{chat.Id}/messages", new { body = "Courier says it's lost.", @internal = true });
        await agent.PostAsJsonAsync($"/api/tickets/{chat.Id}/messages", new { body = "Sorry! A replacement ships today.", @internal = false });

        var seen = await visitor.GetFromJsonAsync<ConversationDetail>($"/api/widget/conversations/{chat.Id}");
        Assert.Equal(["you", "support"], seen!.Messages.Select(m => m.From));
        Assert.DoesNotContain(seen.Messages, m => m.Body.Contains("Courier"));
        Assert.Equal("Open", seen.Conversation.Status);
    }

    [Fact]
    public async Task Visitor_reply_reopens_a_solved_chat()
    {
        var (owner, tenant) = await SignUpAsync("reopen");
        var visitor = await VisitorAsync(tenant.Slug);
        var chat = await StartChatAsync(visitor, "Question about billing");
        await api.ClientFor(owner, tenant.Id).PatchAsJsonAsync($"/api/tickets/{chat.Id}", new { status = "Solved" });

        var reply = await visitor.PostAsJsonAsync($"/api/widget/conversations/{chat.Id}/messages", new { body = "Actually, one more thing" });
        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);

        var seen = await visitor.GetFromJsonAsync<ConversationDetail>($"/api/widget/conversations/{chat.Id}");
        Assert.Equal("Open", seen!.Conversation.Status);
    }

    [Fact]
    public async Task Visitors_only_reach_their_own_conversations()
    {
        var (_, tenantA) = await SignUpAsync("own-a");
        var (_, tenantB) = await SignUpAsync("own-b");
        var maria = await VisitorAsync(tenantA.Slug, "Maria");
        var tom = await VisitorAsync(tenantA.Slug, "Tom");
        var outsider = await VisitorAsync(tenantB.Slug, "Eve");
        var chat = await StartChatAsync(maria, "Private question");

        foreach (var other in new[] { tom, outsider })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/widget/conversations/{chat.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/widget/conversations/{chat.Id}/messages",
                new { body = "hi" })).StatusCode);
            Assert.Empty((await other.GetFromJsonAsync<List<Conversation>>("/api/widget/conversations"))!);
        }
        Assert.Single((await maria.GetFromJsonAsync<List<Conversation>>("/api/widget/conversations"))!);
    }

    [Fact]
    public async Task Visitor_tokens_are_rejected_by_agent_endpoints_and_vice_versa()
    {
        var (owner, tenant) = await SignUpAsync("schemes");
        var visitor = await VisitorAsync(tenant.Slug);
        visitor.DefaultRequestHeaders.Add("X-Tenant-Id", tenant.Id.ToString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync("/api/tickets")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.ClientFor(owner, tenant.Id).GetAsync("/api/widget/conversations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync("/api/widget/conversations")).StatusCode);
    }

    [Fact]
    public async Task Widget_endpoints_allow_any_origin()
    {
        var (_, tenant) = await SignUpAsync("cors");
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/widget/sessions");
        request.Headers.Add("Origin", "https://shop.example.test");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        var res = await api.CreateClient().SendAsync(request);

        Assert.Equal("*", res.Headers.GetValues("Access-Control-Allow-Origin").Single());
        _ = tenant;
    }
}
