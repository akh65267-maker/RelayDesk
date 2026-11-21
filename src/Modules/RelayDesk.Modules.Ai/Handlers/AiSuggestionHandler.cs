using Microsoft.Extensions.DependencyInjection;
using RelayDesk.Contracts;
using RelayDesk.Modules.Ai.Application;

namespace RelayDesk.Modules.Ai.Handlers;

/// <summary>
/// Runs in the AI worker from a durable RabbitMQ queue. Public because Wolverine generates code that calls it;
/// the work happens in the internal <see cref="AiAssistant"/>.
/// </summary>
public static class AiSuggestionHandler
{
    public static Task Handle(TicketCreated e, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<AiAssistant>().TriageNewTicketAsync(e, ct);

    public static Task Handle(AiSuggestionRequested e, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<AiAssistant>().HandleRequestAsync(e, ct);
}
