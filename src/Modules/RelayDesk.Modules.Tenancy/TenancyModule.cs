using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RelayDesk.SharedKernel.Modules;

namespace RelayDesk.Modules.Tenancy;

/// <summary>
/// Reference module (template for new modules). Real tenancy logic lands in Phase 1.
/// Layout convention: public contracts in /Contracts, everything else internal.
/// </summary>
public sealed class TenancyModule : IModule
{
    public string Name => "Tenancy";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ping", () => Results.Ok(new { module = Name }));
    }
}
