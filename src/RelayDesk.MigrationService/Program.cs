using RelayDesk.Modules.Audit;
using RelayDesk.Modules.Tenancy;
using RelayDesk.SharedKernel.Modules;
using RelayDesk.SharedKernel.Persistence;
using RelayDesk.SharedKernel.Tenancy;

// One-off job: applies every module's migrations as the schema owner, then exits (ADR 0010).
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTenancyCore();
builder.Services.AddModules(builder.Configuration,
    typeof(TenancyModule).Assembly, // first: other modules may reference tenants later
    typeof(AuditModule).Assembly);

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILogger<Program>>();

using (var scope = host.Services.CreateScope())
{
    foreach (var db in scope.ServiceProvider.GetServices<IModuleDatabase>())
    {
        log.LogInformation("Migrating {Module}", db.Module);
        await db.MigrateAsync(CancellationToken.None);
    }
}

log.LogInformation("Migrations complete");
