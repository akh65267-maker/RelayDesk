using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.SharedKernel.Persistence;

/// <summary>
/// Base for every module DbContext (ADR 0002/0003): own schema, snake_case names,
/// a tenant query filter on <see cref="ITenantOwned"/> entities and TenantId stamping on insert.
/// </summary>
public abstract class TenantDbContext(DbContextOptions options, ITenantContext tenant) : DbContext(options)
{
    protected abstract string Schema { get; }

    protected ITenantContext Tenant => tenant;

    // Referenced by the query filter; EF parameterizes it per context instance.
    private Guid CurrentTenantId => tenant.TenantId ?? Guid.Empty;

    /// <summary>Re-sends the tenant context to the open connection, e.g. after switching tenant mid-request.</summary>
    public Task RefreshTenantSessionAsync(CancellationToken ct = default) =>
        TenantSessionInterceptor.ApplyAsync(
            Database.GetDbConnection(), tenant,
            Database.CurrentTransaction?.GetDbTransaction(), ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly, t => t.Namespace?.StartsWith(GetType().Namespace!) == true);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entity.ClrType))
                modelBuilder.Entity(entity.ClrType).HasQueryFilter(BuildTenantFilter(entity.ClrType));

            entity.SetTableName(ToSnake(entity.GetTableName()!));
            foreach (var p in entity.GetProperties()) p.SetColumnName(ToSnake(p.Name));
            foreach (var k in entity.GetKeys()) k.SetName(ToSnake(k.GetName()!));
            foreach (var fk in entity.GetForeignKeys()) fk.SetConstraintName(ToSnake(fk.GetConstraintName()!));
            foreach (var ix in entity.GetIndexes()) ix.SetDatabaseName(ToSnake(ix.GetDatabaseName()!));
        }
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == Guid.Empty)
                entry.Entity.TenantId = tenant.TenantId
                    ?? throw new InvalidOperationException("Cannot add tenant-owned data without a tenant context.");
        }
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    private LambdaExpression BuildTenantFilter(Type type)
    {
        var e = Expression.Parameter(type, "e");
        var body = Expression.Equal(
            Expression.Property(e, nameof(ITenantOwned.TenantId)),
            Expression.Property(Expression.Constant(this, typeof(TenantDbContext)), nameof(CurrentTenantId)));
        return Expression.Lambda(body, e);
    }

    internal static string ToSnake(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && name[i - 1] != '_' && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
