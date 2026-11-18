using Microsoft.EntityFrameworkCore;
using RelayDesk.Modules.Tenancy.Persistence;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.Tenancy.Application;

internal sealed class MembershipReader(TenancyDbContext db, ITenantContext tenant) : ITenantMembershipReader
{
    // Runs before the tenant is known: RLS allows it through the user_id branch of the memberships policy.
    public async Task<TenantRole?> GetRoleAsync(Guid tenantId, CancellationToken ct)
    {
        if (tenant.UserId is null) return null;

        var role = await db.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.UserId == tenant.UserId)
            .Select(m => (TenantRole?)m.Role)
            .FirstOrDefaultAsync(ct);
        return role;
    }

    public Task<bool> IsMemberAsync(string userId, CancellationToken ct) =>
        db.Memberships.AsNoTracking().AnyAsync(m => m.UserId == userId, ct); // query filter: current tenant
}
