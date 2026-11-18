using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using RelayDesk.SharedKernel.Messaging;
using Wolverine.Persistence.Durability;

namespace RelayDesk.SharedKernel.Persistence;

/// <summary>
/// Applies every module's migrations and Wolverine's message storage as the schema owner, then
/// grants the app role access. Used by the MigrationService and the integration tests.
/// </summary>
public static class RelayDatabaseMigrator
{
    public static async Task MigrateAsync(IServiceProvider services, ILogger log, CancellationToken ct = default)
    {
        using (var scope = services.CreateScope())
        {
            foreach (var db in scope.ServiceProvider.GetServices<IModuleDatabase>())
            {
                log.LogInformation("Migrating {Module}", db.Module);
                await db.MigrateAsync(ct);
            }
        }

        // Wolverine's envelope tables (outbox, inbox, dead letters). Migrated without starting
        // message processing, so this never handles or dead-letters real messages.
        log.LogInformation("Migrating Wolverine message storage");
        await services.GetRequiredService<IMessageStore>().Admin.MigrateAsync();

        var cs = services.GetRequiredService<IConfiguration>().GetConnectionString(ModuleDbContextExtensions.ConnectionStringName);
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);
        await using var grant = new NpgsqlCommand(
            RlsSql.GrantSchema(MessagingExtensions.WolverineSchema) +
            $"GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA {MessagingExtensions.WolverineSchema} TO {RlsSql.AppRole};", conn);
        await grant.ExecuteNonQueryAsync(ct);
    }
}
