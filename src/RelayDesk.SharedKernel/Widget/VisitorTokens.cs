using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.SharedKernel.Widget;

/// <summary>
/// Short-lived tokens for anonymous chat-widget visitors (ADR 0005). Signed with a shared key by the
/// API and validated by the API and the Realtime service. A visitor token is scoped to one tenant and
/// one visitor and is never accepted by agent endpoints.
/// </summary>
public sealed class VisitorTokens(IConfiguration configuration, TimeProvider clock)
{
    public const string Scheme = "Visitor";
    public const string CorsPolicy = "widget";
    public const string ConfigKey = "Widget:SigningKey";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private const string Issuer = "relay-widget";
    private const string TenantClaim = "tid";
    private const string KindClaim = "typ";

    public sealed record Issued(string Token, Guid VisitorId, DateTimeOffset ExpiresAt);

    public Issued Issue(Guid tenantId, string? name, string? email)
    {
        var visitorId = Guid.CreateVersion7();
        var now = clock.GetUtcNow();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, visitorId.ToString()),
            new(TenantClaim, tenantId.ToString()),
            new(KindClaim, "visitor"),
        };
        if (!string.IsNullOrWhiteSpace(name)) claims.Add(new(JwtRegisteredClaimNames.Name, name));
        if (!string.IsNullOrWhiteSpace(email)) claims.Add(new(JwtRegisteredClaimNames.Email, email));

        var token = new JwtSecurityToken(Issuer, Issuer, claims, now.UtcDateTime, now.Add(Lifetime).UtcDateTime,
            new SigningCredentials(Key(configuration), SecurityAlgorithms.HmacSha256));
        return new Issued(new JwtSecurityTokenHandler().WriteToken(token), visitorId, now.Add(Lifetime));
    }

    /// <summary>Adds the "Visitor" bearer scheme. Hubs pass the token as ?access_token=... (WebSockets).</summary>
    public static AuthenticationBuilder AddVisitorScheme(AuthenticationBuilder auth, IConfiguration configuration) =>
        auth.AddJwtBearer(Scheme, o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = Issuer,
                ValidAudience = Issuer,
                IssuerSigningKey = Key(configuration),
                ClockSkew = TimeSpan.FromSeconds(30),
            };
            o.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Query.TryGetValue("access_token", out var t))
                        ctx.Token = t;
                    return Task.CompletedTask;
                },
            };
        });

    /// <summary>Reads (tenant, visitor, name, email) from an authenticated visitor principal.</summary>
    public static (Guid TenantId, Guid VisitorId, string? Name, string? Email)? Read(ClaimsPrincipal user) =>
        user.FindFirstValue(KindClaim) == "visitor"
        && Guid.TryParse(user.FindFirstValue(TenantClaim), out var tenantId)
        && Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var visitorId)
            ? (tenantId, visitorId, user.FindFirstValue(JwtRegisteredClaimNames.Name), user.FindFirstValue(JwtRegisteredClaimNames.Email))
            : null;

    private static SymmetricSecurityKey Key(IConfiguration configuration)
    {
        var secret = configuration[ConfigKey];
        if (string.IsNullOrEmpty(secret) || secret.Length < 32)
            throw new InvalidOperationException($"'{ConfigKey}' must be set to at least 32 characters.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }
}

public static class VisitorEndpointExtensions
{
    /// <summary>
    /// Authenticates the request as a widget visitor (the "Visitor" scheme only, never Keycloak)
    /// and puts the visitor and their tenant into the tenant context, so RLS applies.
    /// </summary>
    public static TBuilder RequireVisitor<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var result = await http.AuthenticateAsync(VisitorTokens.Scheme);
            if (!result.Succeeded || VisitorTokens.Read(result.Principal) is not { } visitor)
                return Results.Unauthorized();

            http.User = result.Principal; // AuthenticateAsync doesn't set it for non-default schemes
            http.RequestServices.GetRequiredService<TenantContext>()
                .SetVisitor(visitor.TenantId, visitor.VisitorId, visitor.Email);
            return await next(ctx);
        });
        return builder;
    }
}
