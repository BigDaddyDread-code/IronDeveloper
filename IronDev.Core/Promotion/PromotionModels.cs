namespace IronDev.Core.Promotion;

public static class LanguageRuntimeAvailability
{
    public const string Executable = "Executable";
    public const string NotExecutableYet = "NotExecutableYet";
}

public sealed record LanguageRuntimeProfile
{
    public required string RuntimeProfileId { get; init; }
    public required string TargetLanguage { get; init; }
    public required string TargetStack { get; init; }
    public required string Availability { get; init; }
    public required string BuildTool { get; init; }
    public required string TestTool { get; init; }
    public IReadOnlyList<string> SourceFileExtensions { get; init; } = [];
    public IReadOnlyList<string> ProjectFileNames { get; init; } = [];
    public IReadOnlyList<string> DependencyFileNames { get; init; } = [];
    public IReadOnlyList<string> TestFilePatterns { get; init; } = [];
    public IReadOnlyList<string> ForbiddenPathSegments { get; init; } = [];
    public IReadOnlyList<string> EvidenceRequirements { get; init; } = [];
    public IReadOnlyList<string> KnownRisks { get; init; } = [];
    public string Boundary { get; init; } = "Runtime profile describes language support only. It does not grant write authority.";
}

public interface ILanguageRuntimeRegistry
{
    IReadOnlyList<LanguageRuntimeProfile> ListProfiles();
    LanguageRuntimeProfile GetRequired(string runtimeProfileId);
    LanguageRuntimeProfile DetectForWorkspace(string workspacePath);
}

public sealed record ProposedChange
{
    public required string ProposedChangeId { get; init; }
    public required string Project { get; init; }
    public required string Title { get; init; }
    public required string SourceGoal { get; init; }
    public IReadOnlyList<string> SourceDocumentIds { get; init; } = [];
    public IReadOnlyList<string> SourceTicketIds { get; init; } = [];
    public IReadOnlyList<string> SourceRunIds { get; init; } = [];
    public IReadOnlyList<string> SourceTraceIds { get; init; } = [];
    public required string TargetRuntimeProfileId { get; init; }
    public required string CurrentStage { get; init; }
    public string? PromotionPackageId { get; init; }
    public string? IsolatedApplyRunId { get; init; }
    public string? PullRequestUrl { get; init; }
    public IReadOnlyList<string> Risks { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public required string Recommendation { get; init; }
    public required string ApprovalState { get; init; }
    public required string Boundary { get; init; }
}

public sealed record PromotionPackage
{
    public required string PackageId { get; init; }
    public required string ProposedChangeId { get; init; }
    public required string SourceRunId { get; init; }
    public required string SourceTraceId { get; init; }
    public required string Project { get; init; }
    public required LanguageRuntimeProfile RuntimeProfile { get; init; }
    public IReadOnlyList<PromotableFile> FilesToPromote { get; init; } = [];
    public IReadOnlyList<BlockedFile> FilesBlocked { get; init; } = [];
    public IReadOnlyList<TestEvidence> TestsPassed { get; init; } = [];
    public IReadOnlyList<RiskNote> Risks { get; init; } = [];
    public required HumanReviewChecklist Checklist { get; init; }
    public required EvidenceSummary EvidenceSummary { get; init; }
    public required string Recommendation { get; init; }
    public required string ApprovalState { get; init; }
    public required string Boundary { get; init; }
}

public sealed record PromotableFile
{
    public required string RelativePath { get; init; }
    public required string Language { get; init; }
    public required string FileRole { get; init; }
    public required string Sha256 { get; init; }
    public required long SizeBytes { get; init; }
    public required string Rationale { get; init; }
}

public sealed record BlockedFile
{
    public required string RelativePath { get; init; }
    public required string Reason { get; init; }
}

public sealed record TestEvidence
{
    public required string Name { get; init; }
    public required string Status { get; init; }
    public required string Tool { get; init; }
    public required string EvidenceRef { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public sealed record RiskNote
{
    public required string Severity { get; init; }
    public required string Category { get; init; }
    public required string Message { get; init; }
    public string Mitigation { get; init; } = string.Empty;
}

public sealed record HumanReviewChecklist
{
    public IReadOnlyList<string> RequiredChecks { get; init; } = [];
    public IReadOnlyList<string> ExplicitApprovalsNeeded { get; init; } = [];
    public IReadOnlyList<string> BlockedActions { get; init; } = [];
}

public sealed record EvidenceSummary
{
    public required string BuildStatus { get; init; }
    public required string TestStatus { get; init; }
    public required string QualityStatus { get; init; }
    public required int RealRepoMutationCount { get; init; }
    public required int PromotableFileCount { get; init; }
    public required int BlockedFileCount { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
}

public sealed record IsolatedPromotionApplyReport
{
    public required string Command { get; init; }
    public required string Status { get; init; }
    public required string RunId { get; init; }
    public required string TraceId { get; init; }
    public required string Project { get; init; }
    public required string PackageId { get; init; }
    public required string ProposedChangeId { get; init; }
    public required string SourceRunId { get; init; }
    public required string SourceTraceId { get; init; }
    public required string IsolatedWorkspacePath { get; init; }
    public required string IsolatedBranchName { get; init; }
    public required LanguageRuntimeProfile RuntimeProfile { get; init; }
    public IReadOnlyList<AppliedPromotionFile> AppliedFiles { get; init; } = [];
    public IReadOnlyList<BlockedFile> RejectedBlockedFiles { get; init; } = [];
    public required RuntimeCommandEvidence Build { get; init; }
    public required RuntimeCommandEvidence Test { get; init; }
    public required PromotionMutationReport Mutation { get; init; }
    public IReadOnlyList<PromotionEvidenceRef> Evidence { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public required string Recommendation { get; init; }
    public required string ApprovalState { get; init; }
    public required string Boundary { get; init; }
    public required string ReproCommand { get; init; }
}

public sealed record AppliedPromotionFile
{
    public required string RelativePath { get; init; }
    public required string Language { get; init; }
    public required string FileRole { get; init; }
    public required string SourceSha256 { get; init; }
    public required string AppliedSha256 { get; init; }
    public required long SizeBytes { get; init; }
    public required bool HashMatchesPackage { get; init; }
}

public sealed record RuntimeCommandEvidence
{
    public required string Command { get; init; }
    public required int ExitCode { get; init; }
    public required string Status { get; init; }
    public required string LogPath { get; init; }
    public required string Summary { get; init; }
}

public sealed record PromotionMutationReport
{
    public required bool ActiveRepoMutationAllowed { get; init; }
    public required int ActiveRepoMutationCount { get; init; }
    public required bool IsolatedWorkspaceMutationAllowed { get; init; }
    public required string ActiveRepoStatusBefore { get; init; }
    public required string ActiveRepoStatusAfter { get; init; }
    public required string IsolatedWorkspacePath { get; init; }
    public required int IsolatedFilesChanged { get; init; }
    public IReadOnlyList<string> ForbiddenPathsTouched { get; init; } = [];
}

public sealed record PromotionEvidenceRef(string Type, string Path, string Summary);
