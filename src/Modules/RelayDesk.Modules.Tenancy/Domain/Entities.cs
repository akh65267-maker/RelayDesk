using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.Tenancy.Domain;

internal sealed class Tenant
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string Slug { get; init; }
    public required string KeycloakOrgId { get; init; }
    public string Region { get; init; } = "eu";
    public required string CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

internal sealed class Membership : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public TenantRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

internal sealed class Invitation : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string Email { get; init; }
    public TenantRole Role { get; init; }
    public required string TokenHash { get; init; }
    public required string InvitedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? AcceptedAt { get; set; }

    public bool IsPending(DateTimeOffset now) => AcceptedAt is null && ExpiresAt > now;
}
