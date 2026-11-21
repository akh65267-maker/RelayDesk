using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RelayDesk.Contracts;
using RelayDesk.Modules.Ai.Application;
using RelayDesk.Modules.Ai.Domain;
using RelayDesk.Modules.Ai.Persistence;
using RelayDesk.SharedKernel.Ai;
using RelayDesk.SharedKernel.Modules;
using RelayDesk.SharedKernel.Persistence;
using RelayDesk.SharedKernel.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace RelayDesk.Modules.Ai;

/// <summary>AI suggestions (triage, reply drafts, summaries) and per-tenant usage (ADR 0008).</summary>
public sealed class AiModule : IModule
{
    public string Name => "Ai";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AiDbContext>(configuration, AiDbContext.SchemaName);
        services.AddScoped<IModuleDatabase, AiDatabase>();
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => AiEndpoints.Map(endpoints);

    /// <summary>The assistant needs a chat model, so only the AI worker registers it.</summary>
    public static void AddAiWorkerServices(IServiceCollection services) => services.AddScoped<AiAssistant>();
}

internal sealed class AiDatabase(AiDbContext db) : IModuleDatabase
{
    public string Module => "Ai";

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);
}

internal sealed record RequestSuggestion(SuggestionKind Kind);
internal sealed record SuggestionDto(Guid Id, SuggestionKind Kind, SuggestionStatus Status, string? Content,
    JsonElement? Data, string? Model, string? Error, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);
internal sealed record UsageDto(long TokensThisMonth, long MonthlyQuota, IReadOnlyList<UsageByFeature> ByFeature);
internal sealed record UsageByFeature(string Feature, long Tokens, int Calls);

internal static class AiEndpoints
{
    public static void Map(IEndpointRouteBuilder g)
    {
        g.MapGet("/tickets/{ticketId:guid}/suggestions", List).RequireTenantRole(TenantRole.Agent);
        g.MapPost("/tickets/{ticketId:guid}/suggestions", Request).RequireTenantRole(TenantRole.Agent);
        g.MapGet("/usage", Usage).RequireTenantRole(TenantRole.Admin);
    }

    /// <summary>The latest suggestion of each kind for the ticket.</summary>
    private static async Task<IResult> List(Guid ticketId, AiDbContext db, CancellationToken ct)
    {
        var rows = await db.Suggestions.AsNoTracking()
            .Where(s => s.TicketId == ticketId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(30)
            .ToListAsync(ct);
        return Results.Ok(rows.GroupBy(s => s.Kind).Select(g => ToDto(g.First())).ToList());
    }

    private static async Task<IResult> Request(
        Guid ticketId, RequestSuggestion req, AiDbContext db, IDbContextOutbox<AiDbContext> outbox,
        ITenantContext ctx, ITicketReader tickets, IOptions<AiOptions> options, TimeProvider clock, CancellationToken ct)
    {
        if (req.Kind is not (SuggestionKind.Reply or SuggestionKind.Summary or SuggestionKind.Triage))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Unknown kind."] });
        if (await tickets.GetAsync(ticketId, ct) is null) return Results.NotFound();

        var now = clock.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        if (await db.TokensSinceAsync(monthStart, ct) >= options.Value.MonthlyTokenQuota)
            return Results.Problem("This workspace has used its AI allowance for the month.", statusCode: StatusCodes.Status429TooManyRequests);

        // One pending request per ticket and kind, so double clicks don't queue duplicate model calls.
        var pending = await db.Suggestions.FirstOrDefaultAsync(
            s => s.TicketId == ticketId && s.Kind == req.Kind && s.Status == SuggestionStatus.Pending, ct);
        if (pending is not null) return Results.Accepted(value: ToDto(pending));

        var suggestion = new Suggestion { TicketId = ticketId, Kind = req.Kind, RequestedBy = ctx.UserId, CreatedAt = now };
        db.Suggestions.Add(suggestion);
        await outbox.PublishAsync(new AiSuggestionRequested(ctx.TenantId!.Value, ctx.UserId, now, ticketId, suggestion.Id, req.Kind.ToString()));
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        return Results.Accepted(value: ToDto(suggestion));
    }

    private static async Task<IResult> Usage(AiDbContext db, IOptions<AiOptions> options, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var byFeature = await db.Usage.AsNoTracking()
            .Where(u => u.OccurredAt >= monthStart)
            .GroupBy(u => u.Feature)
            .Select(g => new UsageByFeature(g.Key, g.Sum(u => u.InputTokens + u.OutputTokens), g.Count()))
            .ToListAsync(ct);
        return Results.Ok(new UsageDto(byFeature.Sum(f => f.Tokens), options.Value.MonthlyTokenQuota, byFeature));
    }

    private static SuggestionDto ToDto(Suggestion s) => new(s.Id, s.Kind, s.Status, s.Content,
        s.Data is null ? null : JsonDocument.Parse(s.Data).RootElement.Clone(), s.Model, s.Error, s.CreatedAt, s.CompletedAt);
}
