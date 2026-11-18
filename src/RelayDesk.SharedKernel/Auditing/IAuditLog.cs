namespace RelayDesk.SharedKernel.Auditing;

/// <summary>
/// Audit writer, implemented by the Audit module. Other modules don't call it directly: they
/// publish integration events through the outbox (ADR 0006), and the Audit module records them.
/// </summary>
public interface IAuditLog
{
    Task WriteAsync(string action, string entityType, string entityId, object? data = null,
        DateTimeOffset? occurredAt = null, CancellationToken ct = default);
}
