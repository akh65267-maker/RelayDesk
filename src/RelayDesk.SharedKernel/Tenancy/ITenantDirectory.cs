namespace RelayDesk.SharedKernel.Tenancy;

/// <summary>Public lookups across tenants (e.g. the chat widget finding a workspace by its ID); implemented by Tenancy.</summary>
public interface ITenantDirectory
{
    Task<Guid?> FindBySlugAsync(string slug, CancellationToken ct);
}
