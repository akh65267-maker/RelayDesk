using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RelayDesk.SharedKernel.Widget;
using StackExchange.Redis;

namespace RelayDesk.Realtime;

/// <summary>
/// SignalR group names (ADR 0007). Every group is scoped by tenant, so a client can only end up in a
/// group after this service has checked it belongs to that tenant.
/// </summary>
public static class Groups
{
    public static string Tenant(Guid tenantId) => $"tenant:{tenantId}";
    public static string Ticket(Guid tenantId, Guid ticketId) => $"ticket:{tenantId}:{ticketId}";
    public static string Visitor(Guid tenantId, Guid visitorId) => $"visitor:{tenantId}:{visitorId}";
}

/// <summary>
/// Membership and ownership checks. This service never reads domain tables (ADR 0007): it asks the API
/// with the caller's own token, so the API's tenant and RLS rules decide.
/// </summary>
public sealed class ApiChecks(HttpClient http)
{
    public Task<bool> IsMemberAsync(string token, Guid tenantId, CancellationToken ct) =>
        SendAsync(token, "/api/tenancy/tenant", tenantId, ct);

    public Task<bool> OwnsConversationAsync(string token, Guid ticketId, CancellationToken ct) =>
        SendAsync(token, $"/api/widget/conversations/{ticketId}", null, ct);

    private async Task<bool> SendAsync(string token, string path, Guid? tenantId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (tenantId is not null) req.Headers.Add("X-Tenant-Id", tenantId.ToString());
        using var res = await http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }
}

/// <summary>Who is online per workspace, shared across Realtime instances through Redis.</summary>
public sealed class Presence(IConnectionMultiplexer redis)
{
    private static RedisKey Key(Guid tenantId) => $"relay:presence:{tenantId}";

    public async Task<string[]> ConnectAsync(Guid tenantId, string userId)
    {
        var db = redis.GetDatabase();
        await db.HashIncrementAsync(Key(tenantId), userId);
        return await OnlineAsync(tenantId);
    }

    public async Task<string[]> DisconnectAsync(Guid tenantId, string userId)
    {
        var db = redis.GetDatabase();
        if (await db.HashDecrementAsync(Key(tenantId), userId) <= 0) await db.HashDeleteAsync(Key(tenantId), userId);
        return await OnlineAsync(tenantId);
    }

    public async Task<string[]> OnlineAsync(Guid tenantId) =>
        (await redis.GetDatabase().HashKeysAsync(Key(tenantId))).Select(k => k.ToString()).ToArray();
}

/// <summary>Agents: live inbox and ticket updates, presence, typing indicators.</summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class AgentHub(ApiChecks api, Presence presence, IHubContext<VisitorHub> visitors) : Hub
{
    private const string TenantKey = "tenant";

    /// <summary>Called once per page load with the workspace the agent is looking at.</summary>
    public async Task<string[]> JoinWorkspace(Guid tenantId)
    {
        var token = Context.GetHttpContext()!.Request.Query["access_token"].ToString();
        if (!await api.IsMemberAsync(token, tenantId, Context.ConnectionAborted))
            throw new HubException("Not a member of this workspace.");

        Context.Items[TenantKey] = tenantId;
        await Groups.AddToGroupAsync(Context.ConnectionId, Realtime.Groups.Tenant(tenantId));
        var online = await presence.ConnectAsync(tenantId, UserId);
        await Clients.OthersInGroup(Realtime.Groups.Tenant(tenantId)).SendAsync("presence", online);
        return online;
    }

    public Task ViewTicket(Guid ticketId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, Realtime.Groups.Ticket(Tenant, ticketId));

    public Task LeaveTicket(Guid ticketId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, Realtime.Groups.Ticket(Tenant, ticketId));

    /// <summary>
    /// Ephemeral; never stored. Reaches other agents viewing the ticket and the visitor. SignalR groups
    /// are per hub, so the visitor hub's group of the same name is reached through its hub context.
    /// </summary>
    public async Task Typing(Guid ticketId)
    {
        var group = Realtime.Groups.Ticket(Tenant, ticketId);
        await Clients.OthersInGroup(group).SendAsync("typing", new { ticketId, who = "agent", userId = UserId });
        await visitors.Clients.Group(group).SendAsync("typing", new { ticketId, who = "agent" });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(TenantKey, out var t) && t is Guid tenantId)
        {
            var online = await presence.DisconnectAsync(tenantId, UserId);
            await Clients.Group(Realtime.Groups.Tenant(tenantId)).SendAsync("presence", online);
        }
        await base.OnDisconnectedAsync(exception);
    }

    private string UserId => Context.User!.FindFirst("sub")!.Value;

    private Guid Tenant => Context.Items.TryGetValue(TenantKey, out var t) && t is Guid id
        ? id
        : throw new HubException("Join a workspace first.");
}

/// <summary>Chat-widget visitors: replies to their own conversations and typing indicators.</summary>
[Authorize(AuthenticationSchemes = VisitorTokens.Scheme)]
public sealed class VisitorHub(ApiChecks api, IHubContext<AgentHub> agents) : Hub
{
    private const string WatchingKey = "watching";

    public override async Task OnConnectedAsync()
    {
        var v = Visitor;
        await Groups.AddToGroupAsync(Context.ConnectionId, Realtime.Groups.Visitor(v.TenantId, v.VisitorId));
        Context.Items[WatchingKey] = new HashSet<Guid>();
        await base.OnConnectedAsync();
    }

    /// <summary>Joins the conversation's group after the API confirms it belongs to this visitor.</summary>
    public async Task WatchConversation(Guid ticketId)
    {
        var token = Context.GetHttpContext()!.Request.Query["access_token"].ToString();
        if (!await api.OwnsConversationAsync(token, ticketId, Context.ConnectionAborted))
            throw new HubException("Conversation not found.");
        Watching.Add(ticketId);
        await Groups.AddToGroupAsync(Context.ConnectionId, Realtime.Groups.Ticket(Visitor.TenantId, ticketId));
    }

    /// <summary>Reaches agents viewing the ticket (on the agent hub). Ignored for conversations not watched.</summary>
    public Task Typing(Guid ticketId) =>
        Watching.Contains(ticketId)
            ? agents.Clients.Group(Realtime.Groups.Ticket(Visitor.TenantId, ticketId)).SendAsync("typing", new { ticketId, who = "customer" })
            : Task.CompletedTask;

    private HashSet<Guid> Watching => (HashSet<Guid>)Context.Items[WatchingKey]!;

    private (Guid TenantId, Guid VisitorId, string? Name, string? Email) Visitor =>
        VisitorTokens.Read(Context.User!) ?? throw new HubException("Not a visitor.");
}
