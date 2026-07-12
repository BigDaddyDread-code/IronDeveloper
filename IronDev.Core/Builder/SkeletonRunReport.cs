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
    public IReadOnlyList<SkeletonRunAgentConfigurationSnapshot> AgentConfigurations { get; init; } = [];

    /// <summary>
    /// The FINAL/CURRENT proposal — the one that produced the evidence at the gate
    /// and that the critic package and approval hash bind to. After a successful
    /// bounded repair this is the repaired proposal, never the failed original.
    /// </summary>
    public SkeletonRunProposalTrace? Proposal { get; init; }

    /// <summary>
    /// REPAIR-1: the original failed proposal, populated ONLY when bounded repair
    /// replaced it. Preserved history — it exists, and it is not the gate proposal.
    /// </summary>
    public SkeletonRunProposalTrace? InitialProposal { get; init; }
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

    /// <summary>
    /// REPAIR-1: every bounded repair attempt this run made, in order, from durable
    /// events. Attempt history is never erased — a run that needed repair says so.
    /// </summary>
    public IReadOnlyList<SkeletonRunRepairAttemptTrace> RepairAttempts { get; init; } = [];

    /// <summary>
    /// REVISE-1: every human-directed revision attempt this run made, in order,
    /// from durable events. A failed revision is history too — the previous gate
    /// package stayed canonical, and the report says a revision was tried.
    /// </summary>
    public IReadOnlyList<SkeletonRunRevisionAttemptTrace> RevisionAttempts { get; init; } = [];

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

public sealed record SkeletonRunAgentConfigurationSnapshot
{
    public required string SnapshotId { get; init; }
    public required long WorkItemId { get; init; }
    public required string RunId { get; init; }
    public required string Role { get; init; }
    public long? ProfileVersion { get; init; }
    public string ProfileScopeLayer { get; init; } = string.Empty;
    public string ConnectionId { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string ControlledEndpointIdentity { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; }
    public int? InputTokenLimit { get; init; }
    public int? OutputTokenLimit { get; init; }
    public double? Temperature { get; init; }
    public string SkillVersion { get; init; } = string.Empty;
    public string SkillHash { get; init; } = string.Empty;
    public string PersonalityVersion { get; init; } = string.Empty;
    public string PersonalityHash { get; init; } = string.Empty;
    public string EffectiveProfileHash { get; init; } = string.Empty;
    public required DateTimeOffset CreatedUtc { get; init; }
    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "A run configuration snapshot is immutable non-secret provenance. It grants no capability, " +
        "satisfies no gate, and never contains credentials or retrievable secret references.";
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

    /// <summary>AG-6: which model the Builder ran on to produce this proposal.</summary>
    public string ModelProvider { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
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
    public string ApprovedByActorId { get; init; } = string.Empty;
    public string ApprovedByActorDisplayName { get; init; } = string.Empty;
    public string ContinuationRequestedByUserId { get; init; } = string.Empty;
    public bool SoloApprovalExceptionUsed { get; init; }
}

public sealed record SkeletonRunApplyTrace
{
    public bool Applied { get; init; }
    public string WorkspacePath { get; init; } = string.Empty;
    public string RefusedReason { get; init; } = string.Empty;
    public IReadOnlyList<SkeletonRunApplyStageTrace> Stages { get; init; } = [];

    /// <summary>The spine's evidence-chain files, each checked on disk. The chain is the receipt.</summary>
    public IReadOnlyList<SkeletonRunReceiptRef> Receipts { get; init; } = [];
    public IReadOnlyList<SkeletonRunApplyAttemptTrace> Attempts { get; init; } = [];
}

public sealed record SkeletonRunApplyAttemptTrace
{
    public string AttemptId { get; init; } = string.Empty;
    public int AttemptNumber { get; init; }
    public string RequestedAction { get; init; } = string.Empty;
    public string RequestedByUserId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Status { get; init; } = SkeletonApplyAttemptStatuses.InProgress;
    public DateTimeOffset StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public string WorkspacePath { get; init; } = string.Empty;
    public string InterruptedStage { get; init; } = string.Empty;
    public string RefusedReason { get; init; } = string.Empty;
    public string MutationState { get; init; } = SkeletonApplyMutationStates.NotObserved;
    public IReadOnlyList<SkeletonRunApplyStageTrace> Stages { get; init; } = [];
    public IReadOnlyList<SkeletonRunReceiptRef> Receipts { get; init; } = [];
    public IReadOnlyList<string> AvailableActions { get; init; } = [];
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

/// <summary>
/// One bounded repair attempt, reconstructed from durable events (REPAIR-1).
/// A repair attempt is proposal-shaped work, never authority — its presence in
/// the report is honesty about the mess, not a mark against the run.
/// </summary>
public sealed record SkeletonRunRepairAttemptTrace
{
    /// <summary>The attempt this repair produced (2 = first repair).</summary>
    public int AttemptNumber { get; init; }

    /// <summary>What the previous attempt failed on.</summary>
    public string FailureKind { get; init; } = string.Empty;
    public string FailedCommand { get; init; } = string.Empty;

    public string RepairProposalId { get; init; } = string.Empty;
    public string ModelProvider { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public bool RepairProposalEvidenceExistsOnDisk { get; init; }
}

/// <summary>
/// One human-directed revision attempt, reconstructed from durable events
/// (REVISE-1). A revision is directed by the human's written instruction, never
/// by the critic's authority — and a failed attempt left the previous gate
/// package canonical.
/// </summary>
public sealed record SkeletonRunRevisionAttemptTrace
{
    /// <summary>The revision ordinal for this run (1 = first revision).</summary>
    public int AttemptNumber { get; init; }

    /// <summary>The finding ids the human cited.</summary>
    public IReadOnlyList<string> FindingIds { get; init; } = [];

    /// <summary>The human's written revision instruction.</summary>
    public string Reason { get; init; } = string.Empty;
    public string RequestedByUserId { get; init; } = string.Empty;

    public string RevisionProposalId { get; init; } = string.Empty;
    public string ModelProvider { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public bool RevisionProposalEvidenceExistsOnDisk { get; init; }

    /// <summary>True when this attempt failed; the previous gate package remained canonical.</summary>
    public bool Failed { get; init; }
    public string FailureKind { get; init; } = string.Empty;
    public string FailedCommand { get; init; } = string.Empty;
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
