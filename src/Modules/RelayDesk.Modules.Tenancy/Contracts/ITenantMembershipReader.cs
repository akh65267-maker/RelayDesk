using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.Tenancy.Contracts;

/// <summary>Public contract used by hosts to resolve the caller's role in the requested tenant.</summary>
public interface ITenantMembershipReader
{
    Task<TenantRole?> GetRoleAsync(Guid tenantId, CancellationToken ct);
}
