using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RelayDesk.Contracts;
using RelayDesk.Modules.Tickets.Domain;
using RelayDesk.Modules.Tickets.Persistence;
using RelayDesk.SharedKernel.Tenancy;
using RelayDesk.SharedKernel.Widget;
using Wolverine.EntityFrameworkCore;

namespace RelayDesk.Modules.Tickets.Application;

internal sealed record StartSessionRequest(string Workspace, string? Name, string? Email);
internal sealed record SessionDto(string Token, Guid VisitorId, DateTimeOffset ExpiresAt);
internal sealed record WidgetMessageRequest(string Body);
internal sealed record ConversationDto(Guid Id, int Number, string Subject, TicketStatus Status, DateTimeOffset UpdatedAt);

/// <summary>What a visitor sees: never internal notes, and agents only as "support".</summary>
internal sealed record WidgetMessageDto(Guid Id, string From, string Body, DateTimeOffset CreatedAt);

/// <summary>
/// Endpoints used by the embeddable chat widget (Phase 3). A chat is a ticket linked to the visitor,
/// so the agent inbox, assignment, notes and audit all apply. Visitors are authenticated with
/// short-lived visitor tokens and can only reach their own conversations.
/// </summary>
internal static class WidgetEndpoints
{
    private const int SubjectLength = 80;

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup("").RequireCors(VisitorTokens.CorsPolicy);
        g.MapPost("/sessions", StartSession).AllowAnonymous();

        var visitor = g.MapGroup("/conversations").RequireVisitor();
        visitor.MapGet("/", List);
        visitor.MapPost("/", Start);
        visitor.MapGet("/{id:guid}", Get);
        visitor.MapPost("/{id:guid}/messages", Reply);
    }

    private static async Task<IResult> StartSession(
        StartSessionRequest req, ITenantDirectory directory, VisitorTokens tokens, CancellationToken ct)
    {
        var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim().ToLowerInvariant();
        var name = string.IsNullOrWhiteSpace(req.Name) ? null : req.Name.Trim();
        if (email is not null && (!email.Contains('@') || email.Length > 320))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Enter a valid email or leave it empty."] });
        if (name is { Length: > 200 })
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is too long."] });

        if (string.IsNullOrWhiteSpace(req.Workspace) || await directory.FindBySlugAsync(req.Workspace, ct) is not { } tenantId)
            return Results.NotFound();

        var issued = tokens.Issue(tenantId, name, email);
        return Results.Ok(new SessionDto(issued.Token, issued.VisitorId, issued.ExpiresAt));
    }

    private static async Task<IResult> List(TicketsDbContext db, ITenantContext ctx, CancellationToken ct) =>
        Results.Ok(await db.Tickets.AsNoTracking()
            .Where(t => t.VisitorId == ctx.VisitorId)
            .OrderByDescending(t => t.UpdatedAt)
            .Select(t => new ConversationDto(t.Id, t.Number, t.Subject, t.Status, t.UpdatedAt))
            .ToListAsync(ct));

    private static async Task<IResult> Start(
        WidgetMessageRequest req, HttpContext http, TicketsDbContext db, IDbContextOutbox<TicketsDbContext> outbox,
        ITenantContext ctx, TimeProvider clock, CancellationToken ct)
    {
        if (Validate(req.Body) is { } invalid) return invalid;
        var body = req.Body.Trim();
        var visitor = VisitorTokens.Read(http.User)!.Value; // RequireVisitor authenticated it
        var now = clock.GetUtcNow();
        var firstLine = body.Split('\n', 2)[0].Trim();

        var ticket = new Ticket
        {
            Number = await db.NextTicketNumberAsync(ct),
            Subject = firstLine.Length > SubjectLength ? firstLine[..(SubjectLength - 1)] + "…" : firstLine,
            RequesterEmail = visitor.Email ?? "",
            RequesterName = visitor.Name,
            VisitorId = visitor.VisitorId,
            Tags = ["chat"],
            CreatedBy = "widget",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var message = new TicketMessage { TicketId = ticket.Id, AuthorType = AuthorType.Customer, Body = body, CreatedAt = now };
        ticket.Messages.Add(message);
        db.Tickets.Add(ticket);

        await outbox.PublishAsync(new TicketCreated(ctx.TenantId!.Value, null, now,
            ticket.Id, ticket.Number, ticket.Subject, ticket.Priority.ToString(), ticket.RequesterEmail));
        await outbox.SaveChangesAndFlushMessagesAsync(ct);

        return Results.Created($"/api/widget/conversations/{ticket.Id}",
            new ConversationDto(ticket.Id, ticket.Number, ticket.Subject, ticket.Status, ticket.UpdatedAt));
    }

    private static async Task<IResult> Get(Guid id, TicketsDbContext db, ITenantContext ctx, CancellationToken ct)
    {
        var ticket = await db.Tickets.AsNoTracking()
            .Where(t => t.Id == id && t.VisitorId == ctx.VisitorId)
            .Select(t => new { t.Id, t.Number, t.Subject, t.Status, t.UpdatedAt })
            .SingleOrDefaultAsync(ct);
        if (ticket is null) return Results.NotFound();

        var messages = await db.Messages.AsNoTracking()
            .Where(m => m.TicketId == id && !m.IsInternal)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new WidgetMessageDto(m.Id, m.AuthorType == AuthorType.Customer ? "you" : "support", m.Body, m.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(new
        {
            conversation = new ConversationDto(ticket.Id, ticket.Number, ticket.Subject, ticket.Status, ticket.UpdatedAt),
            messages,
        });
    }

    private static async Task<IResult> Reply(
        Guid id, WidgetMessageRequest req, TicketsDbContext db, IDbContextOutbox<TicketsDbContext> outbox,
        ITenantContext ctx, TimeProvider clock, CancellationToken ct)
    {
        if (Validate(req.Body) is { } invalid) return invalid;
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == id && t.VisitorId == ctx.VisitorId, ct);
        if (ticket is null) return Results.NotFound();

        var now = clock.GetUtcNow();
        var tenantId = ctx.TenantId!.Value;
        var message = new TicketMessage { TicketId = ticket.Id, AuthorType = AuthorType.Customer, Body = req.Body.Trim(), CreatedAt = now };
        db.Messages.Add(message);

        if (ticket.StatusAfterMessage(AuthorType.Customer, isInternal: false) is { } next)
        {
            await outbox.PublishAsync(new TicketStatusChanged(tenantId, null, now, ticket.Id, ticket.Status.ToString(), next.ToString()));
            ticket.Status = next;
        }
        ticket.UpdatedAt = now;

        await outbox.PublishAsync(new TicketMessageAdded(tenantId, null, now, ticket.Id, message.Id, false,
            nameof(AuthorType.Customer), ticket.VisitorId));
        await outbox.SaveChangesAndFlushMessagesAsync(ct);

        return Results.Created($"/api/widget/conversations/{ticket.Id}",
            new WidgetMessageDto(message.Id, "you", message.Body, message.CreatedAt));
    }

    private static IResult? Validate(string? body) =>
        string.IsNullOrWhiteSpace(body) || body.Trim().Length > 5_000
            ? Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["Message must be 1-5000 characters."] })
            : null;
}
