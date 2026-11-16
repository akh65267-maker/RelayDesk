using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelayDesk.Modules.Tenancy.Domain;
using RelayDesk.SharedKernel.Persistence;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.Tenancy.Persistence;

internal sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options, ITenantContext tenant)
    : TenantDbContext(options, tenant)
{
    public const string SchemaName = "tenancy";

    protected override string Schema => SchemaName;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
}

internal sealed class TenantConfig : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Slug).HasMaxLength(40);
        b.Property(x => x.KeycloakOrgId).HasMaxLength(64);
        b.Property(x => x.Region).HasMaxLength(16);
        b.Property(x => x.CreatedBy).HasMaxLength(64);
        b.HasIndex(x => x.Slug).IsUnique();
    }
}

internal sealed class MembershipConfig : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> b)
    {
        b.Property(x => x.UserId).HasMaxLength(64);
        b.Property(x => x.Email).HasMaxLength(320);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
        b.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InvitationConfig : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> b)
    {
        b.Property(x => x.Email).HasMaxLength(320);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.TokenHash).HasMaxLength(64);
        b.Property(x => x.InvitedBy).HasMaxLength(64);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.Email);
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Used only by <c>dotnet ef</c>.</summary>
internal sealed class TenancyDesignTimeFactory : IDesignTimeDbContextFactory<TenancyDbContext>
{
    public TenancyDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TenancyDbContext>()
                .UseNpgsql("Host=localhost;Database=relaydesk", o => o.MigrationsHistoryTable("__ef_migrations", TenancyDbContext.SchemaName))
                .Options,
            new TenantContext());
}
