using System.Net;
using System.Net.Http.Json;
using Npgsql;
using RelayDesk.IntegrationTests.Infrastructure;

namespace RelayDesk.IntegrationTests;

/// <summary>Roadmap Phase 2 "done when": full ticket lifecycle works and an event is emitted for each change.</summary>
[Collection(ApiCollection.Name)]
public sealed class TicketTests(RelayApiFactory api)
{
    private sealed record TenantResponse(Guid Id);
    private sealed record InvitationCreated(string Token);
    private sealed record TicketSummary(Guid Id, int Number, string Subject, string Status, string Priority,
        string? AssigneeUserId, string RequesterEmail, List<string> Tags);
    private sealed record Message(Guid Id, string AuthorType, string? AuthorUserId, string Body, bool IsInternal);
    private sealed record TicketDetail(Guid Id, int Number, string Subject, string Status, string Priority,
        string? AssigneeUserId, List<string> Tags, List<Message> Messages);
    private sealed record Page(List<TicketSummary> Items, int Total);
    private sealed record AuditEntry(string Action, string EntityType, string EntityId, string? ActorUserId);

    private async Task<(TestUser Owner, Guid TenantId)> SignUpAsync(string name)
    {
        var owner = TestUser.New(name);
        var res = await api.ClientFor(owner).PostAsJsonAsync("/api/tenancy/tenants",
            new { name, slug = $"{name}-{Guid.NewGuid():N}"[..20] });
        Assert.True(res.StatusCode == HttpStatusCode.Created, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (owner, (await res.Content.ReadFromJsonAsync<TenantResponse>())!.Id);
    }

    private async Task<TestUser> AddAgentAsync(TestUser owner, Guid tenantId)
    {
        var agent = TestUser.New("agent");
        var invite = await api.ClientFor(owner, tenantId)
            .PostAsJsonAsync("/api/tenancy/invitations", new { email = agent.Email, role = "Agent" });
        var token = (await invite.Content.ReadFromJsonAsync<InvitationCreated>())!.Token;
        Assert.Equal(HttpStatusCode.OK,
            (await api.ClientFor(agent).PostAsJsonAsync("/api/tenancy/invitations/accept", new { token })).StatusCode);
        return agent;
    }

    private static async Task<TicketSummary> CreateTicketAsync(HttpClient client, string subject = "Cannot log in", string[]? tags = null)
    {
        var res = await client.PostAsJsonAsync("/api/tickets", new
        {
            subject,
            body = "I get an error when I try to log in.",
            requesterEmail = "Customer@Example.test",
            priority = "High",
            tags = tags ?? ["Login", " login ", "bug"],
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<TicketSummary>())!;
    }

    [Fact]
    public async Task Ticket_lifecycle_works_end_to_end()
    {
        var (owner, tenantId) = await SignUpAsync("lifecycle");
        var agent = await AddAgentAsync(owner, tenantId);
        var ownerClient = api.ClientFor(owner, tenantId);
        var agentClient = api.ClientFor(agent, tenantId);

        // Create: numbered per tenant, tags normalized, the first message is the customer's.
        var first = await CreateTicketAsync(ownerClient);
        var second = await CreateTicketAsync(ownerClient, "Billing question");
        Assert.Equal(1, first.Number);
        Assert.Equal(2, second.Number);
        Assert.Equal("New", first.Status);
        Assert.Equal("customer@example.test", first.RequesterEmail);
        Assert.Equal(["bug", "login"], first.Tags);

        // Assign to the agent.
        var assigned = await ownerClient.PatchAsJsonAsync($"/api/tickets/{first.Id}", new { assigneeUserId = agent.Id });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        // A public agent reply moves New -> Open; an internal note doesn't change status.
        Assert.Equal(HttpStatusCode.Created, (await agentClient.PostAsJsonAsync($"/api/tickets/{first.Id}/messages",
            new { body = "Checking with engineering.", @internal = true })).StatusCode);
        var afterNote = await agentClient.GetFromJsonAsync<TicketDetail>($"/api/tickets/{first.Id}");
        Assert.Equal("New", afterNote!.Status);

        Assert.Equal(HttpStatusCode.Created, (await agentClient.PostAsJsonAsync($"/api/tickets/{first.Id}/messages",
            new { body = "Please try resetting your password.", @internal = false })).StatusCode);

        // Solve it, raise priority, retag.
        var updated = await agentClient.PatchAsJsonAsync($"/api/tickets/{first.Id}",
            new { status = "Solved", priority = "Urgent", tags = new[] { "password" } });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var detail = await agentClient.GetFromJsonAsync<TicketDetail>($"/api/tickets/{first.Id}");
        Assert.Equal("Solved", detail!.Status);
        Assert.Equal("Urgent", detail.Priority);
        Assert.Equal(agent.Id, detail.AssigneeUserId);
        Assert.Equal(["password"], detail.Tags);
        Assert.Equal(["Customer", "Agent", "Agent"], detail.Messages.Select(m => m.AuthorType));
        Assert.Equal([false, true, false], detail.Messages.Select(m => m.IsInternal));

        // Filters.
        var mine = await agentClient.GetFromJsonAsync<Page>("/api/tickets?assignee=me");
        Assert.Equal([first.Id], mine!.Items.Select(t => t.Id));
        var unassigned = await agentClient.GetFromJsonAsync<Page>("/api/tickets?assignee=unassigned");
        Assert.Equal([second.Id], unassigned!.Items.Select(t => t.Id));
        var solved = await agentClient.GetFromJsonAsync<Page>("/api/tickets?status=Solved");
        Assert.Equal([first.Id], solved!.Items.Select(t => t.Id));
        var byNumber = await agentClient.GetFromJsonAsync<Page>("/api/tickets?q=%232");
        Assert.Equal([second.Id], byNumber!.Items.Select(t => t.Id));
        var byTag = await agentClient.GetFromJsonAsync<Page>("/api/tickets?tag=Password");
        Assert.Equal([first.Id], byTag!.Items.Select(t => t.Id));

        // Every change produced an event, recorded asynchronously by the Audit module.
        var expected = new[]
        {
            "ticket.created", "ticket.assigned", "ticket.note_added", "ticket.status_changed",
            "ticket.reply_added", "ticket.updated",
        };
        var audit = await WaitForAuditAsync(ownerClient, first.Id, entries => expected.All(a => entries.Any(e => e.Action == a)));
        Assert.Equal(2, audit.Count(e => e.Action == "ticket.status_changed")); // New->Open, Open->Solved
        Assert.All(audit.Where(e => e.Action != "ticket.created" && e.Action != "ticket.assigned"),
            e => Assert.Equal(agent.Id, e.ActorUserId));
    }

    [Fact]
    public async Task Assignee_must_belong_to_the_tenant()
    {
        var (ownerA, tenantA) = await SignUpAsync("assign-a");
        var (ownerB, _) = await SignUpAsync("assign-b");
        var ticket = await CreateTicketAsync(api.ClientFor(ownerA, tenantA));

        var res = await api.ClientFor(ownerA, tenantA).PatchAsJsonAsync($"/api/tickets/{ticket.Id}", new { assigneeUserId = ownerB.Id });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Tickets_are_isolated_between_tenants()
    {
        var (ownerA, tenantA) = await SignUpAsync("iso-a");
        var (ownerB, tenantB) = await SignUpAsync("iso-b");
        var ticketA = await CreateTicketAsync(api.ClientFor(ownerA, tenantA));
        var ticketB = await CreateTicketAsync(api.ClientFor(ownerB, tenantB));

        // Both tenants start numbering at 1.
        Assert.Equal(1, ticketA.Number);
        Assert.Equal(1, ticketB.Number);

        var clientB = api.ClientFor(ownerB, tenantB);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/tickets/{ticketA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.PostAsJsonAsync($"/api/tickets/{ticketA.Id}/messages",
            new { body = "hi", @internal = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.PatchAsJsonAsync($"/api/tickets/{ticketA.Id}",
            new { status = "Closed" })).StatusCode);
        var listB = await clientB.GetFromJsonAsync<Page>("/api/tickets");
        Assert.Equal([ticketB.Id], listB!.Items.Select(t => t.Id));

        // RLS: as the app role inside tenant B, tenant A's ticket rows don't exist.
        await using var conn = new NpgsqlConnection(api.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @t, false); " +
            "SELECT (SELECT count(*) FROM tickets.tickets WHERE id = @a) + (SELECT count(*) FROM tickets.ticket_messages WHERE ticket_id = @a)", conn);
        cmd.Parameters.AddWithValue("t", tenantB.ToString());
        cmd.Parameters.AddWithValue("a", ticketA.Id);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.NextResultAsync();
        await reader.ReadAsync();
        Assert.Equal(0L, reader.GetInt64(0));
    }

    [Fact]
    public async Task Invalid_ticket_is_rejected()
    {
        var (owner, tenant) = await SignUpAsync("invalid");

        var res = await api.ClientFor(owner, tenant).PostAsJsonAsync("/api/tickets",
            new { subject = "", body = "", requesterEmail = "not-an-email", tags = Enumerable.Range(0, 11).Select(i => $"t{i}") });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var problem = await res.Content.ReadAsStringAsync();
        Assert.Contains("subject", problem);
        Assert.Contains("requesterEmail", problem);
        Assert.Contains("tags", problem);
    }

    [Fact]
    public async Task Tenant_signup_is_audited_through_events()
    {
        var (owner, tenant) = await SignUpAsync("audited");

        var entries = await WaitForAuditAsync(api.ClientFor(owner, tenant), null,
            e => e.Any(x => x.Action == "tenant.created") && e.Any(x => x.Action == "member.joined"));

        Assert.Equal(owner.Id, entries.Single(e => e.Action == "tenant.created").ActorUserId);
    }

    /// <summary>Audit entries are written by an asynchronous event handler, so poll for them.</summary>
    private static async Task<List<AuditEntry>> WaitForAuditAsync(
        HttpClient owner, Guid? entityId, Func<List<AuditEntry>, bool> done)
    {
        List<AuditEntry> entries = [];
        for (var i = 0; i < 50; i++)
        {
            entries = (await owner.GetFromJsonAsync<List<AuditEntry>>("/api/audit/entries?take=200"))!
                .Where(e => entityId is null || e.EntityId == entityId.ToString())
                .ToList();
            if (done(entries)) return entries;
            await Task.Delay(200);
        }
        Assert.Fail($"Expected audit entries did not appear. Got: {string.Join(", ", entries.Select(e => e.Action))}");
        return entries;
    }
}
