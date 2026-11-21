using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RelayDesk.Modules.Ai.Domain;
using RelayDesk.SharedKernel.Persistence;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.Ai.Persistence;

internal sealed class AiDbContext(DbContextOptions<AiDbContext> options, ITenantContext tenant) : TenantDbContext(options, tenant)
{
    public const string SchemaName = "ai";

    protected override string Schema => SchemaName;

    public DbSet<Suggestion> Suggestions => Set<Suggestion>();
    public DbSet<UsageEntry> Usage => Set<UsageEntry>();

    /// <summary>Tokens the current tenant used since <paramref name="since"/>.</summary>
    public Task<long> TokensSinceAsync(DateTimeOffset since, CancellationToken ct) =>
        Usage.Where(u => u.OccurredAt >= since).SumAsync(u => u.InputTokens + u.OutputTokens, ct);
}

internal sealed class SuggestionConfig : IEntityTypeConfiguration<Suggestion>
{
    public void Configure(EntityTypeBuilder<Suggestion> b)
    {
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Content).HasMaxLength(20_000);
        b.Property(x => x.Data).HasColumnType("jsonb");
        b.Property(x => x.Model).HasMaxLength(100);
        b.Property(x => x.Error).HasMaxLength(1_000);
        b.Property(x => x.RequestedBy).HasMaxLength(64);
        b.HasIndex(x => new { x.TenantId, x.TicketId, x.CreatedAt });
    }
}

internal sealed class UsageEntryConfig : IEntityTypeConfiguration<UsageEntry>
{
    public void Configure(EntityTypeBuilder<UsageEntry> b)
    {
        b.ToTable("usage");
        b.Property(x => x.Feature).HasMaxLength(32);
        b.Property(x => x.Model).HasMaxLength(100);
        b.HasIndex(x => new { x.TenantId, x.OccurredAt });
    }
}

/// <summary>Used only by <c>dotnet ef</c>.</summary>
internal sealed class AiDesignTimeFactory : IDesignTimeDbContextFactory<AiDbContext>
{
    public AiDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AiDbContext>()
                .UseNpgsql("Host=localhost;Database=relaydesk", o => o.MigrationsHistoryTable("__ef_migrations", AiDbContext.SchemaName))
                .Options,
            new TenantContext());
}
