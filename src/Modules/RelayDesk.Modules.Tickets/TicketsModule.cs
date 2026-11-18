using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RelayDesk.Modules.Tickets.Application;
using RelayDesk.Modules.Tickets.Persistence;
using RelayDesk.SharedKernel.Modules;
using RelayDesk.SharedKernel.Persistence;

namespace RelayDesk.Modules.Tickets;

/// <summary>Tickets, replies and internal notes, with status, priority, assignment and tags.</summary>
public sealed class TicketsModule : IModule
{
    public string Name => "Tickets";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<TicketsDbContext>(configuration, TicketsDbContext.SchemaName);
        services.AddScoped<IModuleDatabase, TicketsDatabase>();
        services.TryAddSingleton(TimeProvider.System);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => TicketEndpoints.Map(endpoints);
}

internal sealed class TicketsDatabase(TicketsDbContext db) : IModuleDatabase
{
    public string Module => "Tickets";

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);
}

/// <summary>Public chat-widget API at /api/widget (visitor tokens, conversations).</summary>
public sealed class WidgetModule : IModule
{
    public string Name => "Widget";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services.TryAddSingleton<RelayDesk.SharedKernel.Widget.VisitorTokens>();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => WidgetEndpoints.Map(endpoints);
}
