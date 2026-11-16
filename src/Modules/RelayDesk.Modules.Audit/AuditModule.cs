using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RelayDesk.SharedKernel.Auditing;
using RelayDesk.SharedKernel.Modules;
using RelayDesk.SharedKernel.Persistence;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.Audit;

/// <summary>Append-only, tenant-scoped audit trail.</summary>
public sealed class AuditModule : IModule
{
    public string Name => "Audit";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("relaydesk")
            ?? throw new InvalidOperationException("Connection string 'relaydesk' is missing.");

        services.AddDbContext<AuditDbContext>((sp, o) => o
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations", AuditDbContext.SchemaName))
            .AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()));

        services.AddScoped<IModuleDatabase, AuditDatabase>();
        services.AddScoped<IAuditLog, AuditLog>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/entries", async (AuditDbContext db, int? take, CancellationToken ct) =>
                Results.Ok(await db.Entries.AsNoTracking()
                    .OrderByDescending(e => e.OccurredAt)
                    .Take(Math.Clamp(take ?? 50, 1, 200))
                    .Select(e => new { e.Id, e.ActorUserId, e.Action, e.EntityType, e.EntityId, e.Data, e.OccurredAt })
                    .ToListAsync(ct)))
            .RequireTenantRole(TenantRole.Admin);
    }
}

internal sealed class AuditEntry : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public string? ActorUserId { get; init; }
    public required string Action { get; init; }
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public string? Data { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

internal sealed class AuditDbContext(DbContextOptions<AuditDbContext> options, ITenantContext tenant)
    : TenantDbContext(options, tenant)
{
    public const string SchemaName = "audit";

    protected override string Schema => SchemaName;

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();
}

internal sealed class AuditEntryConfig : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("entries");
        b.Property(x => x.ActorUserId).HasMaxLength(64);
        b.Property(x => x.Action).HasMaxLength(64);
        b.Property(x => x.EntityType).HasMaxLength(64);
        b.Property(x => x.EntityId).HasMaxLength(64);
        b.Property(x => x.Data).HasColumnType("jsonb");
        b.HasIndex(x => new { x.TenantId, x.OccurredAt });
    }
}

internal sealed class AuditLog(AuditDbContext db, ITenantContext tenant) : IAuditLog
{
    public async Task WriteAsync(string action, string entityType, string entityId, object? data = null, CancellationToken ct = default)
    {
        db.Entries.Add(new AuditEntry
        {
            ActorUserId = tenant.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Data = data is null ? null : JsonSerializer.Serialize(data),
        });
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class AuditDatabase(AuditDbContext db) : IModuleDatabase
{
    public string Module => "Audit";

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);
}

/// <summary>Used only by <c>dotnet ef</c>.</summary>
internal sealed class AuditDesignTimeFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AuditDbContext>()
                .UseNpgsql("Host=localhost;Database=relaydesk", o => o.MigrationsHistoryTable("__ef_migrations", AuditDbContext.SchemaName))
                .Options,
            new TenantContext());
}
