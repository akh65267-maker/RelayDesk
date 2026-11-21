using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RelayDesk.Modules.Audit;
using RelayDesk.Modules.Ai;
using RelayDesk.Modules.KnowledgeBase;
using RelayDesk.Modules.Tenancy;
using RelayDesk.Modules.Tickets;
using RelayDesk.SharedKernel.Messaging;
using RelayDesk.SharedKernel.Modules;
using RelayDesk.SharedKernel.Persistence;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.IntegrationTests.Infrastructure;

/// <summary>Mirrors RelayDesk.MigrationService: same modules, same Wolverine storage, same migrator.</summary>
internal static class MigratorHost
{
    public static async Task MigrateAsync(string ownerConnectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{ModuleDbContextExtensions.ConnectionStringName}"] = ownerConnectionString,
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddTenancyCore();
        builder.Services.AddModules(builder.Configuration,
            typeof(TenancyModule).Assembly, typeof(AuditModule).Assembly, typeof(TicketsModule).Assembly,
            typeof(KnowledgeBaseModule).Assembly, typeof(AiModule).Assembly);
        builder.AddRelayMessaging(buildStorage: true);

        using var host = builder.Build();
        await RelayDatabaseMigrator.MigrateAsync(host.Services, host.Services.GetRequiredService<ILogger<RelayApiFactory>>());
    }
}
