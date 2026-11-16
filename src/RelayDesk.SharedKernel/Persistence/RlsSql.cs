namespace RelayDesk.SharedKernel.Persistence;

/// <summary>SQL snippets for migrations implementing ADR 0003 row-level security.</summary>
public static class RlsSql
{
    public const string AppRole = "relay_app";

    public const string CurrentTenant = "nullif(current_setting('app.tenant_id', true), '')::uuid";
    public const string CurrentUser = "nullif(current_setting('app.user_id', true), '')";
    public const string CurrentEmail = "nullif(current_setting('app.user_email', true), '')";

    /// <summary>Lets the app role use the schema and its tables (it owns nothing, so RLS always applies).</summary>
    public static string GrantSchema(string schema) => $"""
        GRANT USAGE ON SCHEMA {schema} TO {AppRole};
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schema} TO {AppRole};
        """;

    /// <summary>Enables and forces RLS on a table, then adds a policy.</summary>
    public static string Policy(string schema, string table, string name, string @using, string withCheck) => $"""
        ALTER TABLE {schema}.{table} ENABLE ROW LEVEL SECURITY;
        ALTER TABLE {schema}.{table} FORCE ROW LEVEL SECURITY;
        CREATE POLICY {name} ON {schema}.{table} USING ({@using}) WITH CHECK ({withCheck});
        """;

    /// <summary>The standard policy: rows are visible and writable only inside the current tenant.</summary>
    public static string TenantIsolation(string schema, string table) =>
        Policy(schema, table, "tenant_isolation",
            $"tenant_id = {CurrentTenant}",
            $"tenant_id = {CurrentTenant}");
}
