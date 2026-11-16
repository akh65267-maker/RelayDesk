using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RelayDesk.Modules.Tenancy.Application;
using RelayDesk.Modules.Tenancy.Contracts;
using RelayDesk.Modules.Tenancy.Keycloak;
using RelayDesk.Modules.Tenancy.Persistence;
using RelayDesk.SharedKernel.Modules;
using RelayDesk.SharedKernel.Persistence;

namespace RelayDesk.Modules.Tenancy;

/// <summary>Tenants, memberships, roles and invitations (ADR 0003, 0005).</summary>
public sealed class TenancyModule : IModule
{
    public string Name => "Tenancy";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("relaydesk")
            ?? throw new InvalidOperationException("Connection string 'relaydesk' is missing.");

        services.AddDbContext<TenancyDbContext>((sp, o) => o
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations", TenancyDbContext.SchemaName))
            .AddInterceptors(sp.GetRequiredService<TenantSessionInterceptor>()));

        services.AddScoped<IModuleDatabase, TenancyDatabase>();
        services.AddScoped<ITenantMembershipReader, MembershipReader>();

        services.Configure<KeycloakAdminOptions>(configuration.GetSection(KeycloakAdminOptions.Section));
        services.AddHttpClient<IKeycloakAdmin, KeycloakAdminClient>();
        services.AddSingleton(TimeProvider.System);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => TenancyEndpoints.Map(endpoints);
}

internal sealed class TenancyDatabase(TenancyDbContext db) : IModuleDatabase
{
    public string Module => "Tenancy";

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);
}
