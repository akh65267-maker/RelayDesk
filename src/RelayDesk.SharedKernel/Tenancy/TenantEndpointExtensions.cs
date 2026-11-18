using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace RelayDesk.SharedKernel.Tenancy;

public static class TenantEndpointExtensions
{
    public const string TenantHeader = "X-Tenant-Id";

    /// <summary>Requires a resolved tenant (via the X-Tenant-Id header) and at least <paramref name="minimum"/> role.</summary>
    public static TBuilder RequireTenantRole<TBuilder>(this TBuilder builder, TenantRole minimum = TenantRole.Agent)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var tenant = ctx.HttpContext.RequestServices.GetRequiredService<ITenantContext>();
            if (!tenant.HasTenant)
                return Results.Problem($"Missing or unknown {TenantHeader} header.", statusCode: StatusCodes.Status400BadRequest);
            // A missing role (e.g. a visitor) must fail too: null < minimum would be false.
            if (tenant.Role is not { } role || role < minimum)
                return Results.Problem($"Requires {minimum} role.", statusCode: StatusCodes.Status403Forbidden);
            return await next(ctx);
        });
        return builder;
    }
}
