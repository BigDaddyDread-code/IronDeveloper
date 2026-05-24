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
    public IReadOnlyList<string> RequiredReviews { get; init; } = [];
}

public sealed record MemoryAuthorityKeyReadiness
{
    public required bool ReadyForAcceptedMemoryKey { get; init; }
    public required string CurrentAuthorityLevel { get; init; }
    public required string RequiredBeforeKey { get; init; }
    public IReadOnlyList<string> MissingEvidence { get; init; } = [];
    public string Boundary { get; init; } = "MemoryImprovementAgent cannot receive accepted-memory write authority in Alpha.";
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
    public IReadOnlyList<string> RejectedNoisyInputs { get; init; } = [];
    public required string MemoryHealthScore { get; init; }
    public required string LessonsLearnedSummary { get; init; }
    public required MemoryAuthorityKeyReadiness AuthorityKeyReadiness { get; init; }
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
