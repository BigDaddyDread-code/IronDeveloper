namespace IronDev.Core.Audit;

public sealed record AuditLedgerQuery
{
    public int? ProjectId { get; init; }
    public long? WorkItemId { get; init; }
    public string? Actor { get; init; }
    public string? Event { get; init; }
    public DateTimeOffset? FromUtc { get; init; }
    public DateTimeOffset? ToUtc { get; init; }
    public int Take { get; init; } = 100;
}

public sealed record AuditLedgerResponse
{
    public string Status { get; init; } = "ok";
    public AuditLedgerBoundary Boundary { get; init; } = new();
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<AuditLedgerIssue> Issues { get; init; } = [];
    public IReadOnlyList<AuditLedgerItem> Items { get; init; } = [];
    public int ReturnedCount { get; init; }
    public int Take { get; init; }
}

public sealed record AuditLedgerBoundary
{
    public bool ReadOnly { get; init; } = true;
    public bool GrantsAuthority { get; init; }
    public bool CanApprove { get; init; }
    public bool CanContinueWorkflow { get; init; }
    public bool CanApplySource { get; init; }
    public bool ExposesRawPayloadJson { get; init; }
    public string BoundaryStatement { get; init; } =
        "The audit ledger is read-only traceability. It does not approve, continue, apply, or grant authority.";
}

public sealed record AuditLedgerIssue
{
    public string Code { get; init; } = string.Empty;
    public string Field { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed record AuditLedgerItem
{
    public string LedgerId { get; init; } = string.Empty;
    public DateTimeOffset TimeUtc { get; init; }
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public long? WorkItemId { get; init; }
    public string? WorkItemTitle { get; init; }
    public string Source { get; init; } = string.Empty;
    public string ActorId { get; init; } = string.Empty;
    public string ActorDisplayName { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string? CorrelationId { get; init; }
    public IReadOnlyList<AuditLedgerEvidenceLink> EvidenceLinks { get; init; } = [];
}

public sealed record AuditLedgerEvidenceLink
{
    public string Label { get; init; } = string.Empty;
    public string Href { get; init; } = string.Empty;
}

public interface IAuditLedgerReadService
{
    Task<AuditLedgerResponse> SearchAsync(
        int tenantId,
        int currentUserId,
        AuditLedgerQuery query,
        CancellationToken cancellationToken = default);
}
