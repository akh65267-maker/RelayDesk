using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RelayDesk.Modules.Tenancy.Application;
using RelayDesk.Modules.Tenancy.Keycloak;
using RelayDesk.Modules.Tenancy.Persistence;
using RelayDesk.SharedKernel.Modules;
using RelayDesk.SharedKernel.Persistence;
using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.Tenancy;

/// <summary>Tenants, memberships, roles and invitations (ADR 0003, 0005).</summary>
public sealed class TenancyModule : IModule
{
    public string Name => "Tenancy";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<TenancyDbContext>(configuration, TenancyDbContext.SchemaName);

        services.AddScoped<IModuleDatabase, TenancyDatabase>();
        services.AddScoped<ITenantMembershipReader, MembershipReader>();

        services.Configure<KeycloakAdminOptions>(configuration.GetSection(KeycloakAdminOptions.Section));
        services.AddHttpClient<IKeycloakAdmin, KeycloakAdminClient>();
        services.TryAddSingleton(TimeProvider.System);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => TenancyEndpoints.Map(endpoints);
}

internal sealed class TenancyDatabase(TenancyDbContext db) : IModuleDatabase
{
    public string Module => "Tenancy";

    public Task MigrateAsync(CancellationToken ct) => db.Database.MigrateAsync(ct);
}
