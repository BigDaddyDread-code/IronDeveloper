namespace IronDev.Core.Builder;

/// <summary>
/// P0-6 — trace completeness. A skeleton run's report reconstructs the WHOLE governed
/// loop from durable evidence: run record, published events, the critic package on
/// disk, the approval requirement it halted on, the continuation that consumed an
/// accepted approval, and the apply spine's evidence-chain receipts.
///
/// Boundary: a report is reconstruction, not authority — it grants nothing, approves
/// nothing, and cannot alter the run. It also does not merely recite: the package
/// hash is recomputed from disk and receipts are checked for existence, and every
/// link that cannot be verified is NAMED in Gaps rather than silently omitted.
/// A loop that cannot be reconstructed is reported as incomplete, never patched over.
/// </summary>
public sealed record SkeletonRunReport
{
    public required string RunId { get; init; }
    public required int ProjectId { get; init; }
    public required long TicketId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<SkeletonRunTimelineEntry> Timeline { get; init; } = [];
    public SkeletonRunProposalTrace? Proposal { get; init; }
    public SkeletonRunTestAuthoringTrace? TestAuthoring { get; init; }
    public SkeletonRunCriticPackageTrace? CriticPackage { get; init; }
    public SkeletonRunApprovalTrace? Approval { get; init; }

    /// <summary>
    /// P1-1: independent critic reviews recorded against this run, by reference.
    /// The durable review lives in the agent-run audit store; the report links,
    /// it does not embed. Reviews are advisory — their presence grants nothing.
    /// </summary>
    public IReadOnlyList<SkeletonRunCriticReviewTrace> CriticReviews { get; init; } = [];

    /// <summary>
    /// P1-3: the human dispositions recorded for critic findings. Every finding
    /// must carry one before the gate evaluates approval — a finding is not a
    /// veto, but it cannot be ignored.
    /// </summary>
    public IReadOnlyList<SkeletonRunFindingDispositionTrace> FindingDispositions { get; init; } = [];

    public SkeletonRunApplyTrace? Apply { get; init; }

    /// <summary>Every loop link the report could not verify, by name. Empty means every observed link verified.</summary>
    public IReadOnlyList<string> Gaps { get; init; } = [];

    /// <summary>
    /// True only when the run reached Applied AND every link verified: package hash
    /// matches what approval bound to, continuation consumed an accepted approval,
    /// and the apply chain's receipts exist on disk.
    /// </summary>
    public bool LoopComplete { get; init; }

    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "A report is reconstruction from durable evidence. It grants nothing, approves nothing, " +
        "and cannot alter the run. Unverifiable links are named as gaps, never patched over.";
}

public sealed record SkeletonRunTimelineEntry
{
    public DateTimeOffset TimestampUtc { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed record SkeletonRunProposalTrace
{
    public string ProposalId { get; init; } = string.Empty;
    public int FileChangeCount { get; init; }
    public string EvidenceRef { get; init; } = string.Empty;
    public bool EvidenceExistsOnDisk { get; init; }
}

public sealed record SkeletonRunTestAuthoringTrace
{
    public bool Authored { get; init; }
    public int AuthoredTestCount { get; init; }
    public string SkippedReason { get; init; } = string.Empty;

    /// <summary>AG-2: which model authored the tests.</summary>
    public string ModelProvider { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
}

public sealed record SkeletonRunCriticPackageTrace
{
    public string PackageId { get; init; } = string.Empty;
    public string PackagePath { get; init; } = string.Empty;
    public bool ExistsOnDisk { get; init; }

    /// <summary>The hash announced when the run halted — what any approval bound to.</summary>
    public string AnnouncedSha256 { get; init; } = string.Empty;

    /// <summary>Recomputed from the file at report time — verification, not recitation.</summary>
    public string Sha256OnDisk { get; init; } = string.Empty;

    public bool HashVerified { get; init; }

    /// <summary>P1-4: the coverage state announced with the package — uncovered criteria are part of what approval owns.</summary>
    public int CriterionCount { get; init; }
    public int UncoveredCriterionCount { get; init; }
}

public sealed record SkeletonRunApprovalTrace
{
    public string TargetKind { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string TargetHash { get; init; } = string.Empty;
    public string CapabilityCode { get; init; } = string.Empty;
    public bool HaltObserved { get; init; }
    public bool ContinuationUnblocked { get; init; }

    /// <summary>The accepted approval the continuation consumed, when one was verified.</summary>
    public string AcceptedApprovalId { get; init; } = string.Empty;
}

public sealed record SkeletonRunApplyTrace
{
    public bool Applied { get; init; }
    public string WorkspacePath { get; init; } = string.Empty;
    public string RefusedReason { get; init; } = string.Empty;
    public IReadOnlyList<SkeletonRunApplyStageTrace> Stages { get; init; } = [];

    /// <summary>The spine's evidence-chain files, each checked on disk. The chain is the receipt.</summary>
    public IReadOnlyList<SkeletonRunReceiptRef> Receipts { get; init; } = [];
}

/// <summary>An independent critic review linked to the run, by reference. Advisory only.</summary>
public sealed record SkeletonRunCriticReviewTrace
{
    public string CriticAgentRunId { get; init; } = string.Empty;
    public string ReviewId { get; init; } = string.Empty;
    public string Verdict { get; init; } = string.Empty;
    public int FindingCount { get; init; }
    public int BlockingFindingCount { get; init; }

    /// <summary>The durable finding ids on this review — what dispositions must answer (P1-3).</summary>
    public IReadOnlyList<string> FindingIds { get; init; } = [];

    /// <summary>The package hash the critic reviewed — comparable against the approval's target hash.</summary>
    public string PackageSha256 { get; init; } = string.Empty;

    /// <summary>P1-2: how many ground-truth checks ran and how many found claim/evidence mismatches.</summary>
    public int GroundTruthCheckCount { get; init; }
    public int GroundTruthMismatchCount { get; init; }

    /// <summary>AG-2: which model reviewed — a catch-rate is meaningless without knowing which configured critic was measured.</summary>
    public string ModelProvider { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
}

/// <summary>A human disposition recorded for a critic finding. A decision, not approval.</summary>
public sealed record SkeletonRunFindingDispositionTrace
{
    public string FindingId { get; init; } = string.Empty;
    public string Disposition { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string DecidedByUserId { get; init; } = string.Empty;
}

public sealed record SkeletonRunApplyStageTrace
{
    public string Stage { get; init; } = string.Empty;
    public bool Succeeded { get; init; }
    public string Errors { get; init; } = string.Empty;
}

public sealed record SkeletonRunReceiptRef
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public bool ExistsOnDisk { get; init; }
}
