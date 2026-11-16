using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.SharedKernel.Persistence;

/// <summary>
/// Copies the tenant context into PostgreSQL session settings every time a connection is opened,
/// so row-level security policies (ADR 0003) see the caller. Pooled connections are reset by Npgsql on return.
/// </summary>
public sealed class TenantSessionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    internal const string Sql =
        "SELECT set_config('app.tenant_id', @t, false), set_config('app.user_id', @u, false), set_config('app.user_email', @e, false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        Apply(connection, tenant, transaction: null);

    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default) =>
        ApplyAsync(connection, tenant, transaction: null, cancellationToken);

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
