namespace RelayDesk.SharedKernel.Auditing;

/// <summary>
/// Cross-module audit contract, implemented by the Audit module. Phase 2 moves this onto
/// integration events through the outbox (ADR 0006) so audit writes share the business transaction.
/// </summary>
public interface IAuditLog
{
    Task WriteAsync(string action, string entityType, string entityId, object? data = null, CancellationToken ct = default);
}
