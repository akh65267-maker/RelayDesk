using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.SharedKernel.Persistence;

/// <summary>
/// Copies the tenant context into PostgreSQL session settings every time a connection is opened,
/// so row-level security policies (ADR 0003) see the caller. Pooled connections are reset by Npgsql on return.
/// Stateless: the tenant comes from the <see cref="TenantDbContext"/> that opened the connection, so one
/// instance serves every scope (Wolverine registers DbContext options as a singleton).
/// </summary>
public sealed class TenantSessionInterceptor : DbConnectionInterceptor
{
    public static readonly TenantSessionInterceptor Instance = new();

    internal const string Sql =
        "SELECT set_config('app.tenant_id', @t, false), set_config('app.user_id', @u, false), set_config('app.user_email', @e, false)";

    private TenantSessionInterceptor() { }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (eventData.Context is TenantDbContext db) Apply(connection, db.TenantForSession, transaction: null);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default) =>
        eventData.Context is TenantDbContext db
            ? ApplyAsync(connection, db.TenantForSession, transaction: null, cancellationToken)
            : Task.CompletedTask;

    internal static void Apply(DbConnection connection, ITenantContext tenant, DbTransaction? transaction)
    {
        using var cmd = Build(connection, tenant, transaction);
        cmd.ExecuteNonQuery();
    }

    internal static async Task ApplyAsync(
        DbConnection connection, ITenantContext tenant, DbTransaction? transaction, CancellationToken ct)
    {
        await using var cmd = Build(connection, tenant, transaction);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static DbCommand Build(DbConnection connection, ITenantContext tenant, DbTransaction? transaction)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = Sql;
        cmd.Transaction = transaction;
        Add(cmd, "t", tenant.TenantId?.ToString() ?? "");
        Add(cmd, "u", tenant.UserId ?? "");
        Add(cmd, "e", tenant.Email ?? "");
        return cmd;
    }

    private static void Add(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
