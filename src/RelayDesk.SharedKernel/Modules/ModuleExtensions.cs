using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RelayDesk.SharedKernel.Modules;

public static class ModuleExtensions
{
    public static IReadOnlyList<IModule> AddModules(
        this IServiceCollection services, IConfiguration configuration, params Assembly[] assemblies)
    {
        var modules = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IModule).IsAssignableFrom(t))
            .Select(t => (IModule)Activator.CreateInstance(t)!)
            .ToList();

        foreach (var module in modules)
            module.RegisterServices(services, configuration);

        return modules;
    }

    public static void MapModules(this IEndpointRouteBuilder endpoints, IEnumerable<IModule> modules)
    {
        foreach (var module in modules)
            module.MapEndpoints(endpoints.MapGroup($"/api/{module.Name.ToLowerInvariant()}"));
    }
}
