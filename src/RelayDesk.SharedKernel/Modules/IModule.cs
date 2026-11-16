using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RelayDesk.SharedKernel.Modules;

/// <summary>
/// Contract every module implements (ADR 0002). The host discovers modules and
/// calls these hooks; modules never reference each other directly.
/// </summary>
public interface IModule
{
    string Name { get; }

    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
