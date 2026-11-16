namespace RelayDesk.SharedKernel.Tenancy;

/// <summary>Role inside a tenant. Higher value includes the permissions of lower ones.</summary>
public enum TenantRole
{
    Agent = 1,
    Admin = 2,
    Owner = 3,
}

/// <summary>Who is calling and in which tenant (ADR 0003). Scoped per request or message.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    TenantRole? Role { get; }
    string? UserId { get; }
    string? Email { get; }
    bool HasTenant => TenantId is not null;
}

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public TenantRole? Role { get; private set; }
    public string? UserId { get; private set; }
    public string? Email { get; private set; }

    public void SetUser(string userId, string? email)
    {
        UserId = userId;
        Email = email?.Trim().ToLowerInvariant();
    }

    public void SetTenant(Guid tenantId, TenantRole role)
    {
        TenantId = tenantId;
        Role = role;
    }
}

/// <summary>Marks an entity as owned by a tenant: gets a query filter, RLS policy and auto-stamped TenantId.</summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
