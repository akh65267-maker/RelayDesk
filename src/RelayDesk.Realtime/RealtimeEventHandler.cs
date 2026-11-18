using Microsoft.AspNetCore.SignalR;
using RelayDesk.Contracts;

namespace RelayDesk.Realtime;

/// <summary>
/// Turns integration events from RabbitMQ into small push notifications. Pushes carry ids only: clients
/// re-fetch through the API, so tenant rules and RLS still decide what each client can see
/// (e.g. visitors never learn about internal notes). Public because Wolverine generates code that calls it.
/// </summary>
public static class RealtimeEventHandler
{
    public static Task Handle(TicketCreated e, IHubContext<AgentHub> agents) => Agents(agents, e, e.TicketId, "created");

    public static Task Handle(TicketStatusChanged e, IHubContext<AgentHub> agents) => Agents(agents, e, e.TicketId, "status");

    public static Task Handle(TicketAssigned e, IHubContext<AgentHub> agents) => Agents(agents, e, e.TicketId, "assigned");

    public static Task Handle(TicketUpdated e, IHubContext<AgentHub> agents) => Agents(agents, e, e.TicketId, "updated");

    public static async Task Handle(TicketMessageAdded e, IHubContext<AgentHub> agents, IHubContext<VisitorHub> visitors)
    {
        await Agents(agents, e, e.TicketId, "message");
        if (!e.IsInternal && e.VisitorId is { } visitorId)
            await visitors.Clients.Group(Groups.Visitor(e.TenantId, visitorId))
                .SendAsync("conversationChanged", new { ticketId = e.TicketId });
    }

    private static Task Agents(IHubContext<AgentHub> agents, IIntegrationEvent e, Guid ticketId, string kind) =>
        agents.Clients.Group(Groups.Tenant(e.TenantId)).SendAsync("ticketChanged", new { ticketId, kind });
}
