namespace RelayDesk.SharedKernel.Persistence;

/// <summary>Implemented by each module that owns a schema; the MigrationService applies them in registration order.</summary>
public interface IModuleDatabase
{
    string Module { get; }

    Task MigrateAsync(CancellationToken ct);
}
