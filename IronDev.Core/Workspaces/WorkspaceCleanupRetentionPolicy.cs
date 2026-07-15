namespace IronDev.Core.Workspaces;

public enum RetainedWorkspaceState { Unknown, Active, Failed, Applied }
public enum WorkspaceEvidenceDisposition { Missing, Live, Archived }
public enum WorkspaceCleanupReason { InvalidRequest, ActiveWorkspace, ManualHold, LegalHold, AuditHold, RequiredReceiptNotArchived, FailedEvidenceNotArchived, NotDerivedWorkspace, RetentionPeriodNotElapsed, EligibleByRetention, EligibleByQuota }

public sealed record WorkspaceCleanupRetentionRequest
{
    public required string WorkspaceReferenceId { get; init; }
    public required RetainedWorkspaceState State { get; init; }
    public required bool IsDerivedWorkspace { get; init; }
    public required DateTimeOffset LastActiveUtc { get; init; }
    public required TimeSpan RetentionPeriod { get; init; }
    public required long WorkspaceBytes { get; init; }
    public required long CurrentRetainedBytes { get; init; }
    public required long QuotaBytes { get; init; }
    public bool HasManualHold { get; init; }
    public bool HasLegalHold { get; init; }
    public bool HasAuditHold { get; init; }
    public bool HasRequiredReceipts { get; init; }
    public WorkspaceEvidenceDisposition ReceiptDisposition { get; init; }
    public WorkspaceEvidenceDisposition FailedRunEvidenceDisposition { get; init; }
}

public sealed record WorkspaceCleanupRetentionResult
{
    public required bool IsEligibleForGovernedCleanupReview { get; init; }
    public required WorkspaceCleanupReason Reason { get; init; }
    public required DateTimeOffset? EarliestReviewUtc { get; init; }
    public required bool RequiredReceiptsRemainInspectable { get; init; }
    public required bool FailedRunEvidenceRemainsInspectable { get; init; }
    public bool IsDeleteCommand => false;
    public bool CanDeleteWorkspace => false;
    public bool CreatesAuthority => false;
}

public static class WorkspaceCleanupRetentionPolicy
{
    public static WorkspaceCleanupRetentionResult Evaluate(WorkspaceCleanupRetentionRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.WorkspaceReferenceId) ||
            request.State == RetainedWorkspaceState.Unknown ||
            !Enum.IsDefined(request.State) ||
            !Enum.IsDefined(request.ReceiptDisposition) ||
            !Enum.IsDefined(request.FailedRunEvidenceDisposition) ||
            request.RetentionPeriod < TimeSpan.Zero ||
            request.WorkspaceBytes < 0 ||
            request.CurrentRetainedBytes < 0 ||
            request.QuotaBytes < 0)
        {
            return Invalid();
        }

        var receiptsSafe = !request.HasRequiredReceipts || request.ReceiptDisposition == WorkspaceEvidenceDisposition.Archived;
        var failureSafe = request.State != RetainedWorkspaceState.Failed || request.FailedRunEvidenceDisposition == WorkspaceEvidenceDisposition.Archived;
        DateTimeOffset earliest;
        try
        {
            earliest = request.LastActiveUtc + request.RetentionPeriod;
        }
        catch (ArgumentOutOfRangeException)
        {
            return Invalid();
        }

        if (request.State == RetainedWorkspaceState.Active) return Block(WorkspaceCleanupReason.ActiveWorkspace, earliest, receiptsSafe, failureSafe);
        if (request.HasLegalHold) return Block(WorkspaceCleanupReason.LegalHold, earliest, receiptsSafe, failureSafe);
        if (request.HasAuditHold) return Block(WorkspaceCleanupReason.AuditHold, earliest, receiptsSafe, failureSafe);
        if (request.HasManualHold) return Block(WorkspaceCleanupReason.ManualHold, earliest, receiptsSafe, failureSafe);
        if (!receiptsSafe) return Block(WorkspaceCleanupReason.RequiredReceiptNotArchived, earliest, false, failureSafe);
        if (!failureSafe) return Block(WorkspaceCleanupReason.FailedEvidenceNotArchived, earliest, receiptsSafe, false);
        if (!request.IsDerivedWorkspace) return Block(WorkspaceCleanupReason.NotDerivedWorkspace, earliest, receiptsSafe, failureSafe);
        if (now < earliest) return Block(WorkspaceCleanupReason.RetentionPeriodNotElapsed, earliest, receiptsSafe, failureSafe);

        var reason = request.QuotaBytes > 0 && request.CurrentRetainedBytes > request.QuotaBytes
            ? WorkspaceCleanupReason.EligibleByQuota
            : WorkspaceCleanupReason.EligibleByRetention;
        return new WorkspaceCleanupRetentionResult
        {
            IsEligibleForGovernedCleanupReview = true,
            Reason = reason,
            EarliestReviewUtc = earliest,
            RequiredReceiptsRemainInspectable = receiptsSafe,
            FailedRunEvidenceRemainsInspectable = failureSafe
        };
    }

    private static WorkspaceCleanupRetentionResult Block(WorkspaceCleanupReason reason, DateTimeOffset earliest, bool receiptsSafe, bool failureSafe) => new()
    {
        IsEligibleForGovernedCleanupReview = false,
        Reason = reason,
        EarliestReviewUtc = earliest,
        RequiredReceiptsRemainInspectable = receiptsSafe,
        FailedRunEvidenceRemainsInspectable = failureSafe
    };

    private static WorkspaceCleanupRetentionResult Invalid() => new()
    {
        IsEligibleForGovernedCleanupReview = false,
        Reason = WorkspaceCleanupReason.InvalidRequest,
        EarliestReviewUtc = null,
        RequiredReceiptsRemainInspectable = false,
        FailedRunEvidenceRemainsInspectable = false
    };
}
