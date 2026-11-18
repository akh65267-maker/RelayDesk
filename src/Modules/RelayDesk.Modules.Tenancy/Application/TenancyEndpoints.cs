using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RelayDesk.Modules.Tenancy.Domain;
using RelayDesk.Modules.Tenancy.Keycloak;
using RelayDesk.Modules.Tenancy.Persistence;
using Wolverine.EntityFrameworkCore;
using RelayDesk.Contracts;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.Tenancy.Application;

internal sealed record CreateTenantRequest(string Name, string Slug);
internal sealed record TenantDto(Guid Id, string Name, string Slug, string Region, TenantRole Role);
internal sealed record MemberDto(Guid Id, string UserId, string Email, TenantRole Role, DateTimeOffset CreatedAt);
internal sealed record InviteRequest(string Email, TenantRole Role);
internal sealed record InvitationDto(Guid Id, string Email, TenantRole Role, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt);
internal sealed record InvitationCreatedDto(Guid Id, string Email, TenantRole Role, DateTimeOffset ExpiresAt, string Token);
internal sealed record AcceptInvitationRequest(string Token);

internal static partial class TenancyEndpoints
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    [GeneratedRegex("^[a-z0-9](?:-?[a-z0-9]){2,39}$")]
    private static partial Regex SlugPattern();

    public static void Map(IEndpointRouteBuilder g)
    {
        g.MapGet("/me/tenants", ListMyTenants).RequireAuthorization();
        g.MapPost("/tenants", CreateTenant).RequireAuthorization();
        g.MapPost("/invitations/accept", AcceptInvitation).RequireAuthorization();

        g.MapGet("/tenant", GetCurrentTenant).RequireTenantRole(TenantRole.Agent);
        g.MapGet("/members", ListMembers).RequireTenantRole(TenantRole.Agent);
        g.MapGet("/invitations", ListInvitations).RequireTenantRole(TenantRole.Admin);
        g.MapPost("/invitations", Invite).RequireTenantRole(TenantRole.Admin);
    }

    private static async Task<IResult> ListMyTenants(TenancyDbContext db, ITenantContext ctx, CancellationToken ct)
    {
        var tenants = await db.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UserId == ctx.UserId)
            .Join(db.Tenants, m => m.TenantId, t => t.Id, (m, t) => new { m.Role, Tenant = t })
            .OrderBy(x => x.Tenant.Name) // sort before projecting: EF can't translate members of a DTO
            .Select(x => new TenantDto(x.Tenant.Id, x.Tenant.Name, x.Tenant.Slug, x.Tenant.Region, x.Role))
            .ToListAsync(ct);
        return Results.Ok(tenants);
    }

    private static async Task<IResult> CreateTenant(
        CreateTenantRequest req, TenancyDbContext db, TenantContext ctx, IKeycloakAdmin keycloak,
        IDbContextOutbox<TenancyDbContext> outbox, ILogger<TenancyModule> log, TimeProvider clock, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? "";
        var slug = req.Slug?.Trim().ToLowerInvariant() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (name.Length is < 2 or > 100) errors["name"] = ["Name must be 2-100 characters."];
        if (!SlugPattern().IsMatch(slug)) errors["slug"] = ["Slug must be 3-40 lowercase letters, digits or single hyphens."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        // RLS hides other tenants, so uniqueness is enforced by Keycloak (org alias) and the unique slug index.
        var slugTaken = Results.Problem("Slug is already taken.", statusCode: StatusCodes.Status409Conflict);
        string orgId;
        try { orgId = await keycloak.CreateOrganizationAsync(name, slug, ct); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict) { return slugTaken; }

        try
        {
            await keycloak.AddMemberAsync(orgId, ctx.UserId!, ct);

            var tenant = new Tenant { Name = name, Slug = slug, KeycloakOrgId = orgId, CreatedBy = ctx.UserId! };

            // Enter the new tenant before the connection opens, so RLS allows both inserts
            // and the outbox commits them together with the events in one transaction.
            ctx.SetTenant(tenant.Id, TenantRole.Owner);
            db.Tenants.Add(tenant);
            db.Memberships.Add(new Membership { UserId = ctx.UserId!, Email = ctx.Email ?? "", Role = TenantRole.Owner });

            var now = clock.GetUtcNow();
            await outbox.PublishAsync(new TenantCreated(tenant.Id, ctx.UserId, now, tenant.Name, tenant.Slug));
            await outbox.PublishAsync(new MemberJoined(tenant.Id, ctx.UserId, now, ctx.UserId!, ctx.Email ?? "", nameof(TenantRole.Owner)));
            await outbox.SaveChangesAndFlushMessagesAsync(ct);

            return Results.Created($"/api/tenancy/tenant",
                new TenantDto(tenant.Id, tenant.Name, tenant.Slug, tenant.Region, TenantRole.Owner));
        }
        catch (Exception failure)
        {
            // Compensate: don't leave an orphaned Keycloak organization behind.
            try { await keycloak.DeleteOrganizationAsync(orgId, CancellationToken.None); }
            catch (Exception ex) { log.LogError(ex, "Failed to delete orphaned Keycloak organization {OrgId}", orgId); }

            if (failure is DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } })
                return slugTaken;
            throw;
        }
    }

    private static async Task<IResult> GetCurrentTenant(TenancyDbContext db, ITenantContext ctx, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == ctx.TenantId, ct);
        return Results.Ok(new TenantDto(t.Id, t.Name, t.Slug, t.Region, ctx.Role!.Value));
    }

    private static async Task<IResult> ListMembers(TenancyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Memberships.AsNoTracking()
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MemberDto(m.Id, m.UserId, m.Email, m.Role, m.CreatedAt))
            .ToListAsync(ct));

    private static async Task<IResult> ListInvitations(TenancyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Invitations.AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InvitationDto(i.Id, i.Email, i.Role, i.ExpiresAt, i.AcceptedAt))
            .ToListAsync(ct));

    private static async Task<IResult> Invite(
        InviteRequest req, TenancyDbContext db, ITenantContext ctx, IDbContextOutbox<TenancyDbContext> outbox, TimeProvider clock, CancellationToken ct)
    {
        var email = req.Email?.Trim().ToLowerInvariant() ?? "";
        if (!email.Contains('@') || email.Length > 320)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["A valid email is required."] });
        if (!Enum.IsDefined(req.Role))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Unknown role."] });
        if (req.Role > ctx.Role)
            return Results.Problem("You cannot invite someone with a higher role than your own.", statusCode: StatusCodes.Status403Forbidden);
        if (await db.Memberships.AnyAsync(m => m.Email == email, ct))
            return Results.Problem("This person is already a member.", statusCode: StatusCodes.Status409Conflict);

        // Re-inviting the same email replaces any pending invitation, so only the newest link works.
        var now = clock.GetUtcNow();
        db.Invitations.RemoveRange(await db.Invitations
            .Where(i => i.Email == email && i.AcceptedAt == null && i.ExpiresAt > now)
            .ToListAsync(ct));

        // The raw token is returned once (email delivery comes later); only its hash is stored.
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var invitation = new Invitation
        {
            Email = email,
            Role = req.Role,
            TokenHash = Hash(token),
            InvitedBy = ctx.UserId!,
            ExpiresAt = now.Add(InvitationLifetime),
        };
        db.Invitations.Add(invitation);
        await outbox.PublishAsync(new InvitationCreated(ctx.TenantId!.Value, ctx.UserId, now, invitation.Id, email, req.Role.ToString()));
        await outbox.SaveChangesAndFlushMessagesAsync(ct);

        return Results.Created($"/api/tenancy/invitations/{invitation.Id}",
            new InvitationCreatedDto(invitation.Id, email, req.Role, invitation.ExpiresAt, token));
    }

    private static async Task<IResult> AcceptInvitation(
        AcceptInvitationRequest req, TenancyDbContext db, TenantContext ctx, IKeycloakAdmin keycloak,
        IDbContextOutbox<TenancyDbContext> outbox, TimeProvider clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrEmpty(ctx.Email))
            return Results.NotFound();

        // Visible through RLS only when the invitation email matches the signed-in user's email.
        var hash = Hash(req.Token);
        var invitation = await db.Invitations.IgnoreQueryFilters().SingleOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invitation is null || invitation.Email != ctx.Email || !invitation.IsPending(clock.GetUtcNow()))
            return Results.NotFound();

        // From here on act inside the inviting tenant. EF reopens the connection for each
        // operation, and the session interceptor applies the new tenant context on open.
        ctx.SetTenant(invitation.TenantId, invitation.Role);

        if (await db.Memberships.AnyAsync(m => m.UserId == ctx.UserId, ct))
            return Results.Problem("You are already a member.", statusCode: StatusCodes.Status409Conflict);

        var orgId = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == invitation.TenantId).Select(t => t.KeycloakOrgId).SingleAsync(ct);
        await keycloak.AddMemberAsync(orgId, ctx.UserId!, ct);

        db.Memberships.Add(new Membership { UserId = ctx.UserId!, Email = ctx.Email, Role = invitation.Role });
        invitation.AcceptedAt = clock.GetUtcNow();
        await outbox.PublishAsync(new MemberJoined(invitation.TenantId, ctx.UserId, invitation.AcceptedAt.Value,
            ctx.UserId!, ctx.Email, invitation.Role.ToString()));
        await outbox.SaveChangesAndFlushMessagesAsync(ct);
        return Results.Ok(new { tenantId = invitation.TenantId, role = invitation.Role });
    }

    private static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
