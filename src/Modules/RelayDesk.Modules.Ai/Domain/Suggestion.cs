using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.Ai.Domain;

internal enum SuggestionKind { Triage, Reply, Summary }

internal enum SuggestionStatus { Pending, Ready, Failed, QuotaExceeded }

/// <summary>
/// An AI result for one ticket. Agents always review it: triage is shown as a recommendation and replies
/// are only drafts, so the model never changes a ticket or writes to a customer on its own.
/// </summary>
internal sealed class Suggestion : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid TicketId { get; init; }
    public SuggestionKind Kind { get; init; }
    public SuggestionStatus Status { get; set; } = SuggestionStatus.Pending;

    /// <summary>Reply draft or summary text; for triage, the model's short reason.</summary>
    public string? Content { get; set; }

    /// <summary>JSON: triage fields, or the knowledge-base sources a reply cites.</summary>
    public string? Data { get; set; }

    public string? Model { get; set; }
    public string? Error { get; set; }
    public string? RequestedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>Token usage per call, for quotas and billing (ADR 0008/0009).</summary>
internal sealed class UsageEntry : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string Feature { get; init; }
    public required string Model { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
