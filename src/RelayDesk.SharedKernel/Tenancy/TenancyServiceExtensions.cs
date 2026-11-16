using Microsoft.Extensions.DependencyInjection;
using RelayDesk.SharedKernel.Persistence;

namespace RelayDesk.SharedKernel.Tenancy;

public static class TenancyServiceExtensions
{
    /// <summary>Registers the scoped tenant context and the RLS session interceptor. Call once per host.</summary>
    public static IServiceCollection AddTenancyCore(this IServiceCollection services)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<TenantSessionInterceptor>();
        return services;
    }
}
