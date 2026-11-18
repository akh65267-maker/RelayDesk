using RelayDesk.Modules.Audit;
using RelayDesk.Modules.Tenancy;
using RelayDesk.Modules.Tickets;
using RelayDesk.SharedKernel.Messaging;
using RelayDesk.SharedKernel.Modules;
using RelayDesk.SharedKernel.Persistence;
using RelayDesk.SharedKernel.Tenancy;

// One-off job: applies every module's migrations and Wolverine's message storage as the
// schema owner, grants the app role access, then exits (ADR 0010).
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddTenancyCore();
builder.Services.AddModules(builder.Configuration,
    typeof(TenancyModule).Assembly, // first: other modules may reference tenants later
    typeof(AuditModule).Assembly,
    typeof(TicketsModule).Assembly);
builder.AddRelayMessaging(buildStorage: true);

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILogger<Program>>();

await RelayDatabaseMigrator.MigrateAsync(host.Services, log);

log.LogInformation("Migrations complete");
