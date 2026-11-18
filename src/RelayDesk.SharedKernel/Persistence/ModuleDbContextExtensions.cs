using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RelayDesk.SharedKernel.Messaging;
using Wolverine.EntityFrameworkCore;

namespace RelayDesk.SharedKernel.Persistence;

public static class ModuleDbContextExtensions
{
    public const string ConnectionStringName = "relaydesk";

    /// <summary>
    /// Registers a module DbContext the standard way: own schema and migrations history, the RLS
    /// session interceptor, and Wolverine's envelope tables so it can act as a transactional outbox (ADR 0006).
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, IConfiguration configuration, string schema)
        where TContext : DbContext
    {
        var cs = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing.");

        services.AddDbContextWithWolverineIntegration<TContext>(o => o
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations", schema))
            .AddInterceptors(TenantSessionInterceptor.Instance)
            // The runtime model also contains Wolverine's envelope tables, which are owned by
            // Wolverine's own schema migration, not by the module's EF migrations.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)),
            MessagingExtensions.WolverineSchema);

        return services;
    }
}
