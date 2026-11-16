using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RelayDesk.Modules.Audit;
using RelayDesk.Modules.Tenancy;
using RelayDesk.SharedKernel.Modules;
using RelayDesk.SharedKernel.Persistence;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.IntegrationTests.Infrastructure;

/// <summary>Mirrors RelayDesk.MigrationService: registers modules with the owner connection and migrates them in order.</summary>
internal sealed class MigratorHost(string ownerConnectionString) : IAsyncDisposable
{
    private readonly ServiceProvider _sp = Build(ownerConnectionString);

    private static ServiceProvider Build(string cs)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:relaydesk"] = cs })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenancyCore();
        services.AddModules(config, typeof(TenancyModule).Assembly, typeof(AuditModule).Assembly);
        return services.BuildServiceProvider();
    }

    public async Task MigrateAsync()
    {
        using var scope = _sp.CreateScope();
        foreach (var db in scope.ServiceProvider.GetServices<IModuleDatabase>())
            await db.MigrateAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync() => _sp.DisposeAsync();
}
