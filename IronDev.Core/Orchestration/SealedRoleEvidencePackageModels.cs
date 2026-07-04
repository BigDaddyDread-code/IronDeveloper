namespace IronDev.Core.Orchestration;

public static class SealedRoleArtifactKinds
{
    public const string OrchestratorContract = "OrchestratorContract";
    public const string TesterCoveragePackage = "TesterCoveragePackage";
    public const string BuilderPatchPackage = "BuilderPatchPackage";
}

public static class SealedRoleArtifactRoles
{
    public const string Orchestrator = "Orchestrator";
    public const string Tester = "Tester";
    public const string Builder = "Builder";
}

public sealed record class SealedRoleEvidencePackage
{
    public string PackageId { get; init; } = string.Empty;

    public long TicketId { get; init; }
    public int ProjectId { get; init; }
    public string RunId { get; init; } = string.Empty;

    public string ContractId { get; init; } = string.Empty;
    public string ContractHash { get; init; } = string.Empty;

    public RoleArtifactRef OrchestratorContract { get; init; } = new();
    public RoleArtifactRef TesterCoveragePackage { get; init; } = new();
    public RoleArtifactRef BuilderPatchPackage { get; init; } = new();

    public string PreCriticEvidenceHash { get; init; } = string.Empty;

    public IReadOnlyList<CriticReviewEvidenceRef> CriticReviews { get; init; } = [];
    public IReadOnlyList<FindingDispositionEvidenceRef> FindingDispositions { get; init; } = [];

    public IReadOnlyList<string> KnownRisks { get; init; } = [];
    public IReadOnlyList<string> KnownGaps { get; init; } = [];

    public string FinalSealHash { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string Boundary { get; init; } = BoundaryText;

    public const string BoundaryText =
        "A sealed role evidence package binds contract, tester coverage, builder patch, critic review, and finding dispositions into a tamper-evident review bundle. " +
        "It is not approval, not test proof, not critic authority, not policy satisfaction, not workflow continuation, " +
        "not source apply permission, not release readiness, and not deployment readiness.";
}

public sealed record class RoleArtifactRef
{
    public string ArtifactId { get; init; } = string.Empty;
    public string ArtifactKind { get; init; } = string.Empty;
    public string ProducedByRole { get; init; } = string.Empty;
    public string ProducedByAgentId { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string EvidenceRef { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record class CriticReviewEvidenceRef
{
    public string ReviewId { get; init; } = string.Empty;
    public string CriticAgentRunId { get; init; } = string.Empty;
    public string CriticAgentId { get; init; } = string.Empty;

    public string ReviewedPackageHash { get; init; } = string.Empty;
    public string Verdict { get; init; } = string.Empty;

    public int FindingCount { get; init; }
    public int BlockingFindingCount { get; init; }
    public int GroundTruthCheckCount { get; init; }
    public int GroundTruthMismatchCount { get; init; }

    public IReadOnlyList<string> FindingIds { get; init; } = [];

    public string EvidenceRef { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;

    public string Boundary { get; init; } =
        "A critic review is an independent review witness. It is not approval, policy satisfaction, workflow continuation, source apply permission, release readiness, or deployment readiness.";
}

public sealed record class FindingDispositionEvidenceRef
{
    public string FindingId { get; init; } = string.Empty;
    public string Disposition { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string DecidedByUserId { get; init; } = string.Empty;

    public string EvidenceRef { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;

    public DateTimeOffset DecidedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string Boundary { get; init; } =
        "A finding disposition records a human response to a critic finding. It is not approval to continue, not source apply permission, not release readiness, and not deployment readiness.";
}

public sealed class SealedRoleEvidencePackageValidationResult
{
    public bool IsValid => Issues.Count == 0;
    public List<string> Issues { get; } = [];
    public List<string> Warnings { get; } = [];
}
