using IronDev.Core.Governance;

namespace IronDev.Core.Governance.RollbackExecution;

public sealed record ControlledRollbackExecutionRequest
{
    public required string ExecutionId { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }
    public required string PatchHash { get; init; }

    public required string SourceApplyReceiptRef { get; init; }

    public required RollbackTargetEvidence? Target { get; init; }
    public required RollbackExecutionAuthorityEvidence? Authority { get; init; }
    public RollbackPolicyApprovedPathEvidence? PolicyApprovedPath { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }

    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
}
