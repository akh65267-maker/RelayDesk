using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RelayDesk.Contracts;
using RelayDesk.Modules.KnowledgeBase.Application;
using RelayDesk.Modules.KnowledgeBase.Domain;
using RelayDesk.Modules.KnowledgeBase.Handlers;
using RelayDesk.Modules.KnowledgeBase.Persistence;
using RelayDesk.SharedKernel.Ai;
using RelayDesk.SharedKernel.Modules;
using RelayDesk.SharedKernel.Persistence;
using RelayDesk.SharedKernel.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace RelayDesk.Modules.KnowledgeBase;

/// <summary>Help-center articles and their search index (ADR 0008).</summary>
public sealed class KnowledgeBaseModule : IModule
{
    public string Name => "Kb";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<KnowledgeBaseDbContext>(configuration, KnowledgeBaseDbContext.SchemaName, n => n.UseVector());
        services.AddScoped<IModuleDatabase, KnowledgeBaseDatabase>();
        services.TryAddSingleton(TimeProvider.System);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => ArticleEndpoints.Map(endpoints);

    /// <summary>Indexing and retrieval need an embedding model, so only the AI worker registers them.</summary>
    public static void AddAiWorkerServices(IServiceCollection services)
    {
        services.AddScoped<IKnowledgeSearch, KnowledgeSearch>();
        services.AddScoped<ArticleIndexer>();
    }
}

internal sealed class KnowledgeBaseDatabase(KnowledgeBaseDbContext db) : IModuleDatabase
{
    public string Module => "KnowledgeBase";

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);
}

internal sealed record SaveArticleRequest(string Title, string Body, bool Publish);
internal sealed record ArticleSummaryDto(Guid Id, string Title, ArticleStatus Status, DateTimeOffset UpdatedAt, DateTimeOffset? PublishedAt);
internal sealed record ArticleDto(Guid Id, string Title, string Body, ArticleStatus Status, DateTimeOffset UpdatedAt, DateTimeOffset? PublishedAt);

internal static class ArticleEndpoints
{
    public static void Map(IEndpointRouteBuilder g)
    {
        g.MapGet("/articles", List).RequireTenantRole(TenantRole.Agent);
        g.MapGet("/articles/{id:guid}", Get).RequireTenantRole(TenantRole.Agent);
        g.MapPost("/articles", Create).RequireTenantRole(TenantRole.Admin);
        g.MapPut("/articles/{id:guid}", Update).RequireTenantRole(TenantRole.Admin);
        g.MapDelete("/articles/{id:guid}", Delete).RequireTenantRole(TenantRole.Admin);
    }

    private static async Task<IResult> List(KnowledgeBaseDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Articles.AsNoTracking()
            .OrderByDescending(a => a.UpdatedAt)
            .Select(a => new ArticleSummaryDto(a.Id, a.Title, a.Status, a.UpdatedAt, a.PublishedAt))
            .ToListAsync(ct));

    private static async Task<IResult> Get(Guid id, KnowledgeBaseDbContext db, CancellationToken ct) =>
        await db.Articles.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct) is { } a
            ? Results.Ok(new ArticleDto(a.Id, a.Title, a.Body, a.Status, a.UpdatedAt, a.PublishedAt))
            : Results.NotFound();

    private static async Task<IResult> Create(
        SaveArticleRequest req, KnowledgeBaseDbContext db, IDbContextOutbox<KnowledgeBaseDbContext> outbox,
        ITenantContext ctx, TimeProvider clock, CancellationToken ct)
    {
        if (Validate(req) is { } invalid) return invalid;
        var now = clock.GetUtcNow();
        var article = new Article { Title = req.Title.Trim(), Body = req.Body.Trim(), CreatedBy = ctx.UserId!, CreatedAt = now, UpdatedAt = now };
        db.Articles.Add(article);
        await SetStatusAsync(article, req.Publish, outbox, ctx, now);
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        return Results.Created($"/api/kb/articles/{article.Id}", ToDto(article));
    }

    private static async Task<IResult> Update(
        Guid id, SaveArticleRequest req, KnowledgeBaseDbContext db, IDbContextOutbox<KnowledgeBaseDbContext> outbox,
        ITenantContext ctx, TimeProvider clock, CancellationToken ct)
    {
        if (Validate(req) is { } invalid) return invalid;
        var article = await db.Articles.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (article is null) return Results.NotFound();

        var now = clock.GetUtcNow();
        article.Title = req.Title.Trim();
        article.Body = req.Body.Trim();
        article.UpdatedAt = now;
        await SetStatusAsync(article, req.Publish, outbox, ctx, now);
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        return Results.Ok(ToDto(article));
    }

    private static async Task<IResult> Delete(
        Guid id, KnowledgeBaseDbContext db, IDbContextOutbox<KnowledgeBaseDbContext> outbox,
        ITenantContext ctx, TimeProvider clock, CancellationToken ct)
    {
        var article = await db.Articles.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (article is null) return Results.NotFound();
        db.Articles.Remove(article); // chunks cascade
        await outbox.PublishAsync(new ArticleUnpublished(ctx.TenantId!.Value, ctx.UserId, clock.GetUtcNow(), article.Id));
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Publishing (or editing a published article) re-indexes it; unpublishing removes it from search.</summary>
    private static async Task SetStatusAsync(
        Article article, bool publish, IDbContextOutbox<KnowledgeBaseDbContext> outbox, ITenantContext ctx, DateTimeOffset now)
    {
        var wasPublished = article.Status == ArticleStatus.Published;
        if (publish)
        {
            article.Status = ArticleStatus.Published;
            article.PublishedAt ??= now;
            await outbox.PublishAsync(new ArticlePublished(ctx.TenantId!.Value, ctx.UserId, now, article.Id, article.Title));
        }
        else
        {
            article.Status = ArticleStatus.Draft;
            if (wasPublished) await outbox.PublishAsync(new ArticleUnpublished(ctx.TenantId!.Value, ctx.UserId, now, article.Id));
        }
    }

    private static IResult? Validate(SaveArticleRequest req)
    {
        var errors = new Dictionary<string, string[]>();
        if (req.Title?.Trim() is not { Length: >= 1 and <= 200 }) errors["title"] = ["Title must be 1-200 characters."];
        if (req.Body?.Trim() is not { Length: >= 1 and <= 100_000 }) errors["body"] = ["Body must be 1-100000 characters."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    private static ArticleDto ToDto(Article a) => new(a.Id, a.Title, a.Body, a.Status, a.UpdatedAt, a.PublishedAt);
}
