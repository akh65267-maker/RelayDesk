namespace RelayDesk.SharedKernel.Tenancy;

/// <summary>Membership queries other modules and hosts may use; implemented by the Tenancy module.</summary>
public interface ITenantMembershipReader
{
    /// <summary>The caller's role in <paramref name="tenantId"/>, or null when not a member.</summary>
    Task<TenantRole?> GetRoleAsync(Guid tenantId, CancellationToken ct);

    /// <summary>Whether <paramref name="userId"/> belongs to the current tenant.</summary>
    Task<bool> IsMemberAsync(string userId, CancellationToken ct);
}
