using RelayDesk.SharedKernel.Tenancy;

namespace RelayDesk.Modules.Tickets.Domain;

internal enum TicketStatus { New, Open, Pending, Solved, Closed }

internal enum TicketPriority { Low, Normal, High, Urgent }

internal enum AuthorType { Agent, Customer }

internal sealed class Ticket : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    /// <summary>Human-friendly number, sequential per tenant (#1, #2, ...).</summary>
    public int Number { get; init; }

    public required string Subject { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.New;
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    public string? AssigneeUserId { get; set; }
    public required string RequesterEmail { get; init; }
    public string? RequesterName { get; init; }
    public List<string> Tags { get; set; } = [];
    public required string CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<TicketMessage> Messages { get; init; } = [];

    /// <summary>
    /// A public agent reply to a new ticket moves it to Open; a customer reply to a pending or solved
    /// ticket reopens it. Internal notes never change the status.
    /// </summary>
    public TicketStatus? StatusAfterMessage(AuthorType author, bool isInternal) => (isInternal, author, Status) switch
    {
        (true, _, _) => null,
        (false, AuthorType.Agent, TicketStatus.New) => TicketStatus.Open,
        (false, AuthorType.Customer, TicketStatus.Pending or TicketStatus.Solved) => TicketStatus.Open,
        _ => null,
    };
}

internal sealed class TicketMessage : ITenantOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid TicketId { get; init; }
    public AuthorType AuthorType { get; init; }
    public string? AuthorUserId { get; init; }
    public required string Body { get; init; }

    /// <summary>Internal notes are visible to agents only.</summary>
    public bool IsInternal { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Per-tenant counter backing <see cref="Ticket.Number"/>.</summary>
internal sealed class TicketCounter : ITenantOwned
{
    public Guid TenantId { get; set; }
    public int LastNumber { get; set; }
}
