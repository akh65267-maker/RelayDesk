using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RelayDesk.Contracts;
using RelayDesk.Modules.Tickets.Domain;
using RelayDesk.Modules.Tickets.Persistence;
using RelayDesk.SharedKernel.Tenancy;
using Wolverine.EntityFrameworkCore;

namespace RelayDesk.Modules.Tickets.Application;

internal sealed record CreateTicketRequest(
    string Subject, string Body, string RequesterEmail, string? RequesterName,
    TicketPriority? Priority, List<string>? Tags);

/// <summary>Only the fields present are changed. Set <c>Unassign</c> to clear the assignee.</summary>
internal sealed record UpdateTicketRequest(
    TicketStatus? Status, TicketPriority? Priority, string? AssigneeUserId, bool? Unassign, List<string>? Tags);

internal sealed record AddMessageRequest(string Body, bool Internal);

internal sealed record TicketSummaryDto(
    Guid Id, int Number, string Subject, TicketStatus Status, TicketPriority Priority,
    string? AssigneeUserId, string RequesterEmail, List<string> Tags, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

internal sealed record MessageDto(
    Guid Id, AuthorType AuthorType, string? AuthorUserId, string Body, bool IsInternal, DateTimeOffset CreatedAt);

internal sealed record TicketDetailDto(
    Guid Id, int Number, string Subject, TicketStatus Status, TicketPriority Priority, string? AssigneeUserId,
    string RequesterEmail, string? RequesterName, List<string> Tags, string CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, List<MessageDto> Messages);

internal sealed record PageDto<T>(List<T> Items, int Total, int Page, int PageSize);

internal static class TicketEndpoints
{
    private const int MaxTags = 10;
    private const int MaxTagLength = 32;

    public static void Map(IEndpointRouteBuilder g)
    {
        var tickets = g.MapGroup("").RequireTenantRole(TenantRole.Agent);
        tickets.MapGet("/", List);
        tickets.MapPost("/", Create);
        tickets.MapGet("/{id:guid}", Get);
        tickets.MapPatch("/{id:guid}", Update);
        tickets.MapPost("/{id:guid}/messages", AddMessage);
    }

    /// <param name="assignee">"me", "unassigned", or a user id.</param>
    private static async Task<IResult> List(
        TicketsDbContext db, ITenantContext ctx, TicketStatus? status, string? assignee, string? tag, string? q,
        int? page, int? pageSize, CancellationToken ct)
    {
        var query = db.Tickets.AsNoTracking();
        if (status is not null) query = query.Where(t => t.Status == status);
        query = assignee switch
        {
            null or "" => query,
            "me" => query.Where(t => t.AssigneeUserId == ctx.UserId),
            "unassigned" => query.Where(t => t.AssigneeUserId == null),
            _ => query.Where(t => t.AssigneeUserId == assignee),
        };
        if (!string.IsNullOrWhiteSpace(tag)) query = query.Where(t => t.Tags.Contains(NormalizeTag(tag)));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = int.TryParse(term.TrimStart('#'), out var number)
                ? query.Where(t => t.Number == number || EF.Functions.ILike(t.Subject, $"%{term}%"))
                : query.Where(t => EF.Functions.ILike(t.Subject, $"%{term}%") || EF.Functions.ILike(t.RequesterEmail, $"%{term}%"));
        }

        var size = Math.Clamp(pageSize ?? 25, 1, 100);
        var current = Math.Max(page ?? 1, 1);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.UpdatedAt)
            .Skip((current - 1) * size).Take(size)
            .Select(t => new TicketSummaryDto(t.Id, t.Number, t.Subject, t.Status, t.Priority, t.AssigneeUserId,
                t.RequesterEmail, t.Tags, t.CreatedAt, t.UpdatedAt))
            .ToListAsync(ct);
        return Results.Ok(new PageDto<TicketSummaryDto>(items, total, current, size));
    }

    private static async Task<IResult> Create(
        CreateTicketRequest req, TicketsDbContext db, IDbContextOutbox<TicketsDbContext> outbox,
        ITenantContext ctx, TimeProvider clock, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var subject = req.Subject?.Trim() ?? "";
        var body = req.Body?.Trim() ?? "";
        var email = req.RequesterEmail?.Trim().ToLowerInvariant() ?? "";
        if (subject.Length is < 1 or > 200) errors["subject"] = ["Subject must be 1-200 characters."];
        if (body.Length is < 1 or > 20_000) errors["body"] = ["Message must be 1-20000 characters."];
        if (!email.Contains('@') || email.Length > 320) errors["requesterEmail"] = ["A valid requester email is required."];
        if (req.Priority is { } p && !Enum.IsDefined(p)) errors["priority"] = ["Unknown priority."];
        var tags = NormalizeTags(req.Tags, errors);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var now = clock.GetUtcNow();
        var ticket = new Ticket
        {
            Number = await db.NextTicketNumberAsync(ct),
            Subject = subject,
            Priority = req.Priority ?? TicketPriority.Normal,
            RequesterEmail = email,
            RequesterName = string.IsNullOrWhiteSpace(req.RequesterName) ? null : req.RequesterName.Trim(),
            Tags = tags,
            CreatedBy = ctx.UserId!,
            CreatedAt = now,
            UpdatedAt = now,
        };
        // Created by an agent on the customer's behalf (e.g. from a phone call); the first message is the customer's.
        ticket.Messages.Add(new TicketMessage { TicketId = ticket.Id, AuthorType = AuthorType.Customer, Body = body, CreatedAt = now });
        db.Tickets.Add(ticket);

        await outbox.PublishAsync(new TicketCreated(ctx.TenantId!.Value, ctx.UserId, now,
            ticket.Id, ticket.Number, ticket.Subject, ticket.Priority.ToString(), ticket.RequesterEmail));
        await outbox.SaveChangesAndFlushMessagesAsync(ct);

        return Results.Created($"/api/tickets/{ticket.Id}", ToSummary(ticket));
    }

    private static async Task<IResult> Get(Guid id, TicketsDbContext db, CancellationToken ct)
    {
        var t = await db.Tickets.AsNoTracking()
            .Include(x => x.Messages.OrderBy(m => m.CreatedAt))
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return Results.NotFound();

        return Results.Ok(new TicketDetailDto(t.Id, t.Number, t.Subject, t.Status, t.Priority, t.AssigneeUserId,
            t.RequesterEmail, t.RequesterName, t.Tags, t.CreatedBy, t.CreatedAt, t.UpdatedAt,
            t.Messages.Select(m => new MessageDto(m.Id, m.AuthorType, m.AuthorUserId, m.Body, m.IsInternal, m.CreatedAt)).ToList()));
    }

    private static async Task<IResult> Update(
        Guid id, UpdateTicketRequest req, TicketsDbContext db, IDbContextOutbox<TicketsDbContext> outbox,
        ITenantContext ctx, ITenantMembershipReader members, TimeProvider clock, CancellationToken ct)
    {
        var ticket = await db.Tickets.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (ticket is null) return Results.NotFound();

        var errors = new Dictionary<string, string[]>();
        if (req.Status is { } s && !Enum.IsDefined(s)) errors["status"] = ["Unknown status."];
        if (req.Priority is { } p && !Enum.IsDefined(p)) errors["priority"] = ["Unknown priority."];
        if (req.AssigneeUserId is { } a && !await members.IsMemberAsync(a, ct))
            errors["assigneeUserId"] = ["The assignee must be a member of this workspace."];
        var tags = req.Tags is null ? null : NormalizeTags(req.Tags, errors);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var now = clock.GetUtcNow();
        var tenantId = ctx.TenantId!.Value;
        var changed = false;

        if (req.Status is { } status && status != ticket.Status)
        {
            await outbox.PublishAsync(new TicketStatusChanged(tenantId, ctx.UserId, now, ticket.Id, ticket.Status.ToString(), status.ToString()));
            ticket.Status = status;
            changed = true;
        }

        var assignee = req.Unassign == true ? null : req.AssigneeUserId ?? ticket.AssigneeUserId;
        if (assignee != ticket.AssigneeUserId)
        {
            ticket.AssigneeUserId = assignee;
            await outbox.PublishAsync(new TicketAssigned(tenantId, ctx.UserId, now, ticket.Id, assignee));
            changed = true;
        }

        var priorityChanged = req.Priority is { } priority && priority != ticket.Priority;
        var tagsChanged = tags is not null && !tags.SequenceEqual(ticket.Tags);
        if (priorityChanged) ticket.Priority = req.Priority!.Value;
        if (tagsChanged) ticket.Tags = tags!;
        if (priorityChanged || tagsChanged)
        {
            await outbox.PublishAsync(new TicketUpdated(tenantId, ctx.UserId, now, ticket.Id, ticket.Priority.ToString(), ticket.Tags));
            changed = true;
        }

        if (changed)
        {
            ticket.UpdatedAt = now;
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
        }
        return Results.Ok(ToSummary(ticket));
    }

    private static async Task<IResult> AddMessage(
        Guid id, AddMessageRequest req, TicketsDbContext db, IDbContextOutbox<TicketsDbContext> outbox,
        ITenantContext ctx, TimeProvider clock, CancellationToken ct)
    {
        var body = req.Body?.Trim() ?? "";
        if (body.Length is < 1 or > 20_000)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["Message must be 1-20000 characters."] });

        var ticket = await db.Tickets.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (ticket is null) return Results.NotFound();

        var now = clock.GetUtcNow();
        var tenantId = ctx.TenantId!.Value;
        var message = new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorType = AuthorType.Agent,
            AuthorUserId = ctx.UserId,
            Body = body,
            IsInternal = req.Internal,
            CreatedAt = now,
        };
        db.Messages.Add(message);

        if (ticket.StatusAfterMessage(AuthorType.Agent, req.Internal) is { } next)
        {
            await outbox.PublishAsync(new TicketStatusChanged(tenantId, ctx.UserId, now, ticket.Id, ticket.Status.ToString(), next.ToString()));
            ticket.Status = next;
        }
        ticket.UpdatedAt = now;

        await outbox.PublishAsync(new TicketMessageAdded(tenantId, ctx.UserId, now, ticket.Id, message.Id, message.IsInternal,
            message.AuthorType.ToString(), ticket.VisitorId));
        await outbox.SaveChangesAndFlushMessagesAsync(ct);

        return Results.Created($"/api/tickets/{ticket.Id}",
            new MessageDto(message.Id, message.AuthorType, message.AuthorUserId, message.Body, message.IsInternal, message.CreatedAt));
    }

    private static TicketSummaryDto ToSummary(Ticket t) =>
        new(t.Id, t.Number, t.Subject, t.Status, t.Priority, t.AssigneeUserId, t.RequesterEmail, t.Tags, t.CreatedAt, t.UpdatedAt);

    private static string NormalizeTag(string tag) => tag.Trim().ToLowerInvariant();

    private static List<string> NormalizeTags(List<string>? tags, Dictionary<string, string[]> errors)
    {
        var result = (tags ?? []).Select(NormalizeTag).Where(t => t.Length > 0).Distinct().Order().ToList();
        if (result.Count > MaxTags || result.Any(t => t.Length > MaxTagLength))
            errors["tags"] = [$"Up to {MaxTags} tags of at most {MaxTagLength} characters."];
        return result;
    }
}
