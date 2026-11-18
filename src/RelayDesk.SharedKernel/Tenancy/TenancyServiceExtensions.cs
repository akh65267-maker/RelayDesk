using Microsoft.Extensions.DependencyInjection;

namespace RelayDesk.SharedKernel.Tenancy;

public static class TenancyServiceExtensions
{
    /// <summary>Registers the scoped tenant context. Call once per host.</summary>
    public static IServiceCollection AddTenancyCore(this IServiceCollection services)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        return services;
    }
}
