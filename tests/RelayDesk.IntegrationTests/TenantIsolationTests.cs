using System.Net;
using System.Net.Http.Json;
using Npgsql;
using RelayDesk.IntegrationTests.Infrastructure;

namespace RelayDesk.IntegrationTests;

/// <summary>Roadmap Phase 1 "done when": two tenants provably cannot see each other's data (ADR 0003).</summary>
[Collection(ApiCollection.Name)]
public sealed class TenantIsolationTests(RelayApiFactory api)
{
    private sealed record TenantResponse(Guid Id, string Name, string Slug, string Region, string Role);
    private sealed record MemberResponse(Guid Id, string UserId, string Email, string Role);
    private sealed record InvitationCreated(Guid Id, string Email, string Role, string Token);

    private static string Slug(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private async Task<(TestUser Owner, TenantResponse Tenant)> SignUpAsync(string name)
    {
        var owner = TestUser.New(name);
        var res = await api.ClientFor(owner).PostAsJsonAsync("/api/tenancy/tenants", new { name, slug = Slug(name) });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (owner, (await res.Content.ReadFromJsonAsync<TenantResponse>())!);
    }

    // ---- API layer ---------------------------------------------------------

    [Fact]
    public async Task Owner_can_read_own_tenant()
    {
        var (owner, tenant) = await SignUpAsync("acme");

        var me = await api.ClientFor(owner, tenant.Id).GetFromJsonAsync<TenantResponse>("/api/tenancy/tenant");

        Assert.Equal(tenant.Id, me!.Id);
        Assert.Equal("Owner", me.Role);
    }

    [Fact]
    public async Task User_cannot_enter_another_tenant()
    {
        var (_, tenantA) = await SignUpAsync("alpha");
        var (ownerB, _) = await SignUpAsync("bravo");

        var res = await api.ClientFor(ownerB, tenantA.Id).GetAsync("/api/tenancy/members");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Unknown_tenant_looks_the_same_as_foreign_tenant()
    {
        var (owner, _) = await SignUpAsync("charlie");

        var res = await api.ClientFor(owner, Guid.NewGuid()).GetAsync("/api/tenancy/members");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task My_tenants_lists_only_my_memberships()
    {
        var (ownerA, tenantA) = await SignUpAsync("delta");
        await SignUpAsync("echo");

        var mine = await api.ClientFor(ownerA).GetFromJsonAsync<List<TenantResponse>>("/api/tenancy/me/tenants");

        Assert.Equal([tenantA.Id], mine!.Select(t => t.Id));
    }

    [Fact]
    public async Task Invited_agent_joins_only_the_inviting_tenant()
    {
        var (ownerA, tenantA) = await SignUpAsync("foxtrot");
        var (ownerB, tenantB) = await SignUpAsync("golf");
        var agent = TestUser.New("agent");

        var invite = await api.ClientFor(ownerA, tenantA.Id)
            .PostAsJsonAsync("/api/tenancy/invitations", new { email = agent.Email, role = "Agent" });
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        var token = (await invite.Content.ReadFromJsonAsync<InvitationCreated>())!.Token;

        var accept = await api.ClientFor(agent).PostAsJsonAsync("/api/tenancy/invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        var membersA = await api.ClientFor(agent, tenantA.Id).GetFromJsonAsync<List<MemberResponse>>("/api/tenancy/members");
        Assert.Equal(2, membersA!.Count);

        var membersB = await api.ClientFor(ownerB, tenantB.Id).GetFromJsonAsync<List<MemberResponse>>("/api/tenancy/members");
        Assert.Equal([ownerB.Id], membersB!.Select(m => m.UserId));

        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(agent, tenantB.Id).GetAsync("/api/tenancy/members")).StatusCode);
    }

    [Fact]
    public async Task Invitation_cannot_be_accepted_by_a_different_email()
    {
        var (owner, tenant) = await SignUpAsync("hotel");
        var invite = await api.ClientFor(owner, tenant.Id)
            .PostAsJsonAsync("/api/tenancy/invitations", new { email = "someone@example.test", role = "Agent" });
        var token = (await invite.Content.ReadFromJsonAsync<InvitationCreated>())!.Token;

        var res = await api.ClientFor(TestUser.New("intruder")).PostAsJsonAsync("/api/tenancy/invitations/accept", new { token });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Agent_cannot_invite()
    {
        var (owner, tenant) = await SignUpAsync("india");
        var agent = TestUser.New("agent");
        var invite = await api.ClientFor(owner, tenant.Id)
            .PostAsJsonAsync("/api/tenancy/invitations", new { email = agent.Email, role = "Agent" });
        var token = (await invite.Content.ReadFromJsonAsync<InvitationCreated>())!.Token;
        await api.ClientFor(agent).PostAsJsonAsync("/api/tenancy/invitations/accept", new { token });

        var res = await api.ClientFor(agent, tenant.Id)
            .PostAsJsonAsync("/api/tenancy/invitations", new { email = "x@example.test", role = "Agent" });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Duplicate_slug_is_rejected()
    {
        var slug = Slug("juliet");
        Assert.Equal(HttpStatusCode.Created,
            (await api.ClientFor(TestUser.New("a")).PostAsJsonAsync("/api/tenancy/tenants", new { name = "Juliet", slug })).StatusCode);

        var res = await api.ClientFor(TestUser.New("b")).PostAsJsonAsync("/api/tenancy/tenants", new { name = "Juliet 2", slug });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    // ---- Database layer: RLS holds even if the application code is wrong ---

    [Fact]
    public async Task Raw_sql_as_app_role_sees_only_current_tenant()
    {
        var (_, tenantA) = await SignUpAsync("kilo");
        var (_, tenantB) = await SignUpAsync("lima");

        await using var conn = await OpenAppConnectionAsync(tenantA.Id);

        Assert.Equal(0L, await ScalarAsync(conn, $"SELECT count(*) FROM tenancy.memberships WHERE tenant_id = '{tenantB.Id}'"));
        Assert.Equal(0L, await ScalarAsync(conn, $"SELECT count(*) FROM audit.entries WHERE tenant_id = '{tenantB.Id}'"));
        Assert.True(await ScalarAsync(conn, "SELECT count(*) FROM audit.entries") > 0);
    }

    [Fact]
    public async Task Raw_sql_without_tenant_context_sees_nothing()
    {
        await SignUpAsync("mike");

        await using var conn = await OpenAppConnectionAsync(tenantId: null);

        Assert.Equal(0L, await ScalarAsync(conn, "SELECT count(*) FROM tenancy.tenants"));
        Assert.Equal(0L, await ScalarAsync(conn, "SELECT count(*) FROM tenancy.memberships"));
        Assert.Equal(0L, await ScalarAsync(conn, "SELECT count(*) FROM audit.entries"));
    }

    [Fact]
    public async Task App_role_cannot_write_into_another_tenant()
    {
        var (_, tenantA) = await SignUpAsync("november");
        var (_, tenantB) = await SignUpAsync("oscar");

        await using var conn = await OpenAppConnectionAsync(tenantA.Id);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync(conn,
            $"INSERT INTO tenancy.memberships (id, tenant_id, user_id, email, role, created_at) " +
            $"VALUES (gen_random_uuid(), '{tenantB.Id}', 'evil', 'evil@example.test', 'Owner', now()) RETURNING 1"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState); // RLS WITH CHECK violation
    }

    [Fact]
    public async Task Audit_log_is_append_only_for_app_role()
    {
        var (_, tenant) = await SignUpAsync("papa");

        await using var conn = await OpenAppConnectionAsync(tenant.Id);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync(conn, "DELETE FROM audit.entries"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ex.SqlState);
    }

    [Fact]
    public async Task App_role_cannot_bypass_rls()
    {
        await using var conn = await OpenAppConnectionAsync(tenantId: null);

        Assert.Equal(0L, await ScalarAsync(conn,
            "SELECT count(*) FROM pg_roles WHERE rolname = current_user AND (rolsuper OR rolbypassrls)"));
    }

    private async Task<NpgsqlConnection> OpenAppConnectionAsync(Guid? tenantId)
    {
        var conn = new NpgsqlConnection(api.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, false)", conn);
        cmd.Parameters.AddWithValue("t", tenantId?.ToString() ?? "");
        await cmd.ExecuteNonQueryAsync();
        return conn;
    }

    private static async Task<long> ScalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
