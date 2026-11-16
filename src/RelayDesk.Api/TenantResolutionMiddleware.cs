using System.Security.Claims;
using RelayDesk.Modules.Tenancy.Contracts;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Api;

/// <summary>
/// Fills the scoped <see cref="TenantContext"/> from the JWT (user) and the X-Tenant-Id header (tenant).
/// The tenant is only set when the user is a member; membership is checked in our database, not the token.
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, TenantContext tenant, ITenantMembershipReader memberships)
    {
        var user = http.User;
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Identity?.IsAuthenticated == true && sub is not null)
        {
            tenant.SetUser(sub, user.FindFirstValue("email") ?? user.FindFirstValue(ClaimTypes.Email));

            if (http.Request.Headers.TryGetValue(TenantEndpointExtensions.TenantHeader, out var raw))
            {
                if (!Guid.TryParse(raw, out var tenantId))
                {
                    await Results.Problem("Invalid X-Tenant-Id header.", statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(http);
                    return;
                }

                var role = await memberships.GetRoleAsync(tenantId, http.RequestAborted);
                if (role is null)
                {
                    // Same response for "doesn't exist" and "not a member" so tenant ids can't be probed.
                    await Results.Problem("Not a member of this tenant.", statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(http);
                    return;
                }
                tenant.SetTenant(tenantId, role.Value);
            }
        }

        await next(http);
    }
}
