namespace IronDev.Core.Agents;

public sealed record DoubtFinding
{
    public required string FindingId { get; init; }
    public required string Severity { get; init; }
    public required string Category { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string EvidenceCitation { get; init; }
    public required string SuggestedFix { get; init; }
    public required int Confidence { get; init; }
    public string TargetLanguage { get; init; } = string.Empty;
    public string TargetStack { get; init; } = string.Empty;
}

public sealed record DoubtReviewResult
{
    public required string ReviewId { get; init; }
    public required string Subject { get; init; }
    public required string ObservedProject { get; init; }
    public required string AffectedProject { get; init; }
    public required string TargetLanguage { get; init; }
    public required string TargetStack { get; init; }
    public IReadOnlyList<DoubtFinding> Criticisms { get; init; } = [];
    public required bool RebuttalRequired { get; init; }
    public required bool KilljoyEscalation { get; init; }
    public required bool RevisionRequired { get; init; }
    public required string Boundary { get; init; }
}

public sealed record DoubtFindingRebuttal
{
    public required string FindingId { get; init; }
    public required string Response { get; init; }
    public required string Disposition { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
}

public sealed record KilljoyReviewSummary
{
    public required string ReviewId { get; init; }
    public required string AgentName { get; init; }
    public required string Status { get; init; }
    public IReadOnlyList<DoubtFindingRebuttal> Rebuttals { get; init; } = [];
    public required bool AllHighCriticalFindingsAddressed { get; init; }
    public required string Boundary { get; init; }
}

public enum MemoryImprovementPermissionLevel
{
    ReadOnlyObserver = 0,
    ProposalOnly = 1,
    StagingAreaWrite = 2,
    AutoStageLowRiskLessons = 3,
    AutoApplyTinyNonAuthoritativeMemory = 4,
    AcceptedMemoryMutation = 5
}

public sealed record MemoryImprovementEvidenceRef
{
    public required string EvidenceId { get; init; }
    public required string EvidenceType { get; init; }
    public required string Source { get; init; }
    public required string Summary { get; init; }
    public required bool IsAuthoritativeEvidence { get; init; }
}

public sealed record MemoryProposalEvidenceBundle
{
    public required string ProposalId { get; init; }
    public required string Claim { get; init; }
    public IReadOnlyList<MemoryImprovementEvidenceRef> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> MissingEvidence { get; init; } = [];
    public required string EvidenceBoundary { get; init; }
}

public sealed record MemoryImprovementAction
{
    public required string ProposalId { get; init; }
    public required string ActionType { get; init; }
    public required string TargetDocumentId { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string RecommendedDisposition { get; init; }
    public required string MemoryAuthorityImpact { get; init; }
    public required string TargetLanguage { get; init; }
    public required string TargetStack { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public MemoryProposalEvidenceBundle? EvidenceBundle { get; init; }
    public IReadOnlyList<string> RequiredReviews { get; init; } = [];
}

public sealed record MemoryAuthorityKeyReadiness
{
    public required bool ReadyForAcceptedMemoryKey { get; init; }
    public required MemoryImprovementPermissionLevel CurrentPermissionLevel { get; init; }
    public required string CurrentAuthorityLevel { get; init; }
    public required string RequiredBeforeKey { get; init; }
    public IReadOnlyList<string> MissingEvidence { get; init; } = [];
    public string Boundary { get; init; } = "MemoryImprovementAgent cannot receive accepted-memory write authority in Alpha.";
}

public sealed record MemoryProposalAuditMetrics
{
    public required int ProposalCount { get; init; }
    public required int AcceptedByHumanCount { get; init; }
    public required int RejectedByHumanCount { get; init; }
    public required int EditedByHumanCount { get; init; }
    public required int UnsafeProposalCount { get; init; }
    public required int DuplicateProposalCount { get; init; }
    public required int MissingEvidenceCount { get; init; }
    public required decimal KilljoyApprovalRate { get; init; }
    public required decimal HumanAcceptanceRate { get; init; }
    public required bool ContextBudgetHealthy { get; init; }
    public required bool RetrievalImprovementProven { get; init; }
}

public sealed record MemoryKeyGateReview
{
    public required string ReviewId { get; init; }
    public required MemoryImprovementPermissionLevel CurrentLevel { get; init; }
    public required string CurrentLevelName { get; init; }
    public required MemoryImprovementPermissionLevel RequestedLevel { get; init; }
    public required string RequestedLevelName { get; init; }
    public required string Decision { get; init; }
    public required decimal PrecisionScore { get; init; }
    public required MemoryProposalAuditMetrics Metrics { get; init; }
    public IReadOnlyList<string> EvidenceSourcesReviewed { get; init; } = [];
    public IReadOnlyList<string> Reasons { get; init; } = [];
    public IReadOnlyList<string> RequiredNextEvidence { get; init; } = [];
    public required string Boundary { get; init; }
}

public sealed record MemoryImprovementProposal
{
    public required string ProposalBatchId { get; init; }
    public required string ObservedProject { get; init; }
    public required string AffectedProject { get; init; }
    public required int MaxContextTokens { get; init; }
    public required int ContextTokensEstimated { get; init; }
    public required int MaxProposalsPerRun { get; init; }
    public IReadOnlyList<string> ContextRefsUsed { get; init; } = [];
    public IReadOnlyList<MemoryImprovementAction> Proposals { get; init; } = [];
    public IReadOnlyList<MemoryProposalEvidenceBundle> EvidenceBundles { get; init; } = [];
    public IReadOnlyList<string> RejectedNoisyInputs { get; init; } = [];
    public required string MemoryHealthScore { get; init; }
    public required string LessonsLearnedSummary { get; init; }
    public required MemoryAuthorityKeyReadiness AuthorityKeyReadiness { get; init; }
    public required MemoryKeyGateReview KeyGateReview { get; init; }
    public required string Boundary { get; init; }
}

public sealed record AdversarialMemoryAgentsReport
{
    public required string Command { get; init; }
    public required string Status { get; init; }
    public required string RunId { get; init; }
    public required string TraceId { get; init; }
    public required string Project { get; init; }
    public required string Summary { get; init; }
    public required DoubtReviewResult DoubtReview { get; init; }
    public required KilljoyReviewSummary KilljoyReview { get; init; }
    public required MemoryImprovementProposal MemoryImprovement { get; init; }
    public IReadOnlyList<string> StageStatuses { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public required bool RealRepoMutationBlocked { get; init; }
    public required bool AcceptedMemoryMutationBlocked { get; init; }
    public required bool TicketCreationBlocked { get; init; }
    public required bool PatchApplyBlocked { get; init; }
    public required string Boundary { get; init; }
    public required string ReproCommand { get; init; }
}
