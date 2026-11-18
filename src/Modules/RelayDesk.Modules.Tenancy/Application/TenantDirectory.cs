using Microsoft.EntityFrameworkCore;
using RelayDesk.Modules.Tenancy.Persistence;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.Tenancy.Application;

internal sealed class TenantDirectory(TenancyDbContext db) : ITenantDirectory
{
    // RLS hides tenants from callers outside them, so this goes through a SECURITY DEFINER function
    // that returns only the id for an exact slug (see migration AddTenantSlugLookup).
    public async Task<Guid?> FindBySlugAsync(string slug, CancellationToken ct)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var ids = await db.Database
            .SqlQuery<Guid?>($"""SELECT tenancy.tenant_id_by_slug({normalized}) AS "Value" """)
            .ToListAsync(ct);
        return ids.SingleOrDefault();
    }
}
