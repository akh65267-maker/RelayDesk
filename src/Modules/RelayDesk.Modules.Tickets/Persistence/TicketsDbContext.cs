using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelayDesk.Modules.Tickets.Domain;
using RelayDesk.SharedKernel.Persistence;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.Tickets.Persistence;

internal sealed class TicketsDbContext(DbContextOptions<TicketsDbContext> options, ITenantContext tenant)
    : TenantDbContext(options, tenant)
{
    public const string SchemaName = "tickets";

    protected override string Schema => SchemaName;

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketMessage> Messages => Set<TicketMessage>();
    public DbSet<TicketCounter> Counters => Set<TicketCounter>();

    /// <summary>
    /// Atomically reserves the next ticket number for the current tenant (upsert on the counter row).
    /// A failed ticket insert can leave a gap, which is acceptable for display numbers.
    /// </summary>
    public async Task<int> NextTicketNumberAsync(CancellationToken ct)
    {
        var tenantId = Tenant.TenantId ?? throw new InvalidOperationException("No tenant context.");
        var numbers = await Database.SqlQuery<int>($"""
            INSERT INTO tickets.ticket_counters (tenant_id, last_number) VALUES ({tenantId}, 1)
            ON CONFLICT (tenant_id) DO UPDATE SET last_number = ticket_counters.last_number + 1
            RETURNING last_number AS "Value"
            """).ToListAsync(ct);
        return numbers.Single();
    }
}

internal sealed class TicketConfig : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> b)
    {
        b.Property(x => x.Subject).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Priority).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.AssigneeUserId).HasMaxLength(64);
        b.Property(x => x.RequesterEmail).HasMaxLength(320);
        b.Property(x => x.RequesterName).HasMaxLength(200);
        b.Property(x => x.CreatedBy).HasMaxLength(64);
        b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status, x.UpdatedAt });
        b.HasIndex(x => new { x.TenantId, x.AssigneeUserId });
        b.HasIndex(x => x.Tags).HasMethod("gin");
        b.HasMany(x => x.Messages).WithOne().HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TicketMessageConfig : IEntityTypeConfiguration<TicketMessage>
{
    public void Configure(EntityTypeBuilder<TicketMessage> b)
    {
        b.ToTable("ticket_messages");
        b.Property(x => x.AuthorType).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.AuthorUserId).HasMaxLength(64);
        b.Property(x => x.Body).HasMaxLength(20_000);
        b.HasIndex(x => new { x.TicketId, x.CreatedAt });
    }
}

internal sealed class TicketCounterConfig : IEntityTypeConfiguration<TicketCounter>
{
    public void Configure(EntityTypeBuilder<TicketCounter> b)
    {
        b.ToTable("ticket_counters");
        b.HasKey(x => x.TenantId);
    }
}

/// <summary>Used only by <c>dotnet ef</c>.</summary>
internal sealed class TicketsDesignTimeFactory : IDesignTimeDbContextFactory<TicketsDbContext>
{
    public TicketsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TicketsDbContext>()
                .UseNpgsql("Host=localhost;Database=relaydesk", o => o.MigrationsHistoryTable("__ef_migrations", TicketsDbContext.SchemaName))
                .Options,
            new TenantContext());
}
