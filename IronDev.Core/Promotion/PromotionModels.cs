using IronDev.Core.Agents;
using IronDev.Core.Builder;

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
    public string PatchSha256 { get; init; } = string.Empty;
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
    public required string PatchSha256 { get; init; }
    public required string UnifiedDiff { get; init; }
    public required LanguageRuntimeProfile RuntimeProfile { get; init; }
    public IReadOnlyList<PromotableFile> FilesToPromote { get; init; } = [];
    public IReadOnlyList<BlockedFile> FilesBlocked { get; init; } = [];
    public IReadOnlyList<TestEvidence> TestsPassed { get; init; } = [];
    public IReadOnlyList<RiskNote> Risks { get; init; } = [];
    public required HumanReviewChecklist Checklist { get; init; }
    public required EvidenceSummary EvidenceSummary { get; init; }
    public IReadOnlyList<DoubtFinding> DoubtFindings { get; init; } = [];
    public IReadOnlyList<DoubtFindingRebuttal> DoubtRebuttals { get; init; } = [];
    public KilljoyReviewSummary? KilljoyReview { get; init; }
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

public sealed record ControlledWritePolicySettings
{
    public required string PolicyId { get; init; }
    public required string Scope { get; init; }
    public bool WritePathEnabled { get; init; }
    public IReadOnlyList<string> PermittedPromotionModes { get; init; } = [];
    public string RuntimeProfileId { get; init; } = "csharp-dotnet";
    public string LanguageAdapter { get; init; } = "csharp-dotnet";
    public string BuildCommand { get; init; } = "dotnet build";
    public string TestCommand { get; init; } = "dotnet test";
    public string QualityCommand { get; init; } = "test run-plan --plan tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json";
    public IReadOnlyList<string> AllowedSourceFileExtensions { get; init; } = [".cs", ".xaml", ".csproj", ".sln"];
    public IReadOnlyList<string> BlockedPathSegments { get; init; } = ["bin/", "obj/", ".git/", ".vs/", "TestResults/"];
    public int MaxFilesChanged { get; init; } = 50;
    public int MaxLinesChanged { get; init; } = 5000;
    public IReadOnlyList<string> RequiredReviewerRoles { get; init; } = ["HumanOwner"];
    public IReadOnlyList<string> RequiredEvidenceTypes { get; init; } = ["PromotionPackage", "ProposedChange", "BuildLog", "TestLog", "QualityGate", "ConscienceDecision", "ThoughtLedgerSummary"];
    public string RequiredApprovalPhrase { get; init; } = "Approve this specific promotion package for isolated branch/worktree validation only.";
    public string BranchNameTemplate { get; init; } = "ida/{project}/{runId}";
    public string WorktreeRoot { get; init; } = "";
    public TimeSpan PromotionPackageExpiry { get; init; } = TimeSpan.FromDays(7);
    public int BuildTestRetryCount { get; init; } = 0;
}

public sealed record HardSafetyInvariant
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public required string Enforcement { get; init; }
    public bool Configurable { get; init; }
}

public sealed record ControlledWriteEffectivePolicy
{
    public required string Command { get; init; }
    public required string Status { get; init; }
    public required string RunId { get; init; }
    public required string TraceId { get; init; }
    public required string Project { get; init; }
    public required ControlledWritePolicySettings GlobalDefaults { get; init; }
    public required ControlledWritePolicySettings ProjectSettings { get; init; }
    public required ControlledWritePolicySettings RunSettings { get; init; }
    public required ControlledWritePolicySettings ExplicitHumanOverride { get; init; }
    public required ControlledWritePolicySettings EffectiveSettings { get; init; }
    public IReadOnlyList<HardSafetyInvariant> HardInvariants { get; init; } = [];
    public IReadOnlyList<string> AttemptedInvariantOverrides { get; init; } = [];
    public IReadOnlyList<string> IgnoredInvariantOverrides { get; init; } = [];
    public IReadOnlyList<PromotionEvidenceRef> Evidence { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public required string Boundary { get; init; }
    public required string ReproCommand { get; init; }
}

public sealed record ControlledWriteApprovalRecord
{
    public required string ApprovalId { get; init; }
    public required string RunId { get; init; }
    public required string TraceId { get; init; }
    public required string Project { get; init; }
    public required string PackageId { get; init; }
    public required string ProposedChangeId { get; init; }
    public required string SourceRunId { get; init; }
    public required string SourceTraceId { get; init; }
    public required string PatchSha256 { get; init; }
    public required string ApprovedBy { get; init; }
    public required string ApprovalRole { get; init; }
    public required string ApprovalScope { get; init; }
    public required string ApprovalState { get; init; }
    public required string ApprovalPhrase { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset ExpiresUtc { get; init; }
    public required bool ValidForControlledWorktreeDryRun { get; init; }
    public required bool ValidForRealRepoWrite { get; init; }
    public IReadOnlyList<string> RequiredEvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> AllowedActions { get; init; } = [];
    public IReadOnlyList<string> BlockedActions { get; init; } = [];
    public required string Boundary { get; init; }
}

public sealed record ControlledWorktreeDryRunReport
{
    public required string Command { get; init; }
    public required string Status { get; init; }
    public required string RunId { get; init; }
    public required string TraceId { get; init; }
    public required string Project { get; init; }
    public required string PackageId { get; init; }
    public required string ProposedChangeId { get; init; }
    public required string ApprovalId { get; init; }
    public string PatchSha256 { get; init; } = string.Empty;
    public required string TargetWorktreePath { get; init; }
    public required string TargetBranchName { get; init; }
    public required bool TargetPathExplicit { get; init; }
    public required bool TargetOutsideActiveRepo { get; init; }
    public required bool TargetBranchIsNotMain { get; init; }
    public required bool WouldCreateWorktree { get; init; }
    public required bool WouldCopyFiles { get; init; }
    public IReadOnlyList<PromotableFile> FilesThatWouldApply { get; init; } = [];
    public IReadOnlyList<BlockedFile> BlockedFilesRejected { get; init; } = [];
    public required ControlledWriteEffectivePolicy PolicySnapshot { get; init; }
    public required ControlledWriteApprovalRecord ApprovalRecord { get; init; }
    public required PromotionMutationReport Mutation { get; init; }
    public IReadOnlyList<PromotionEvidenceRef> Evidence { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public required string Recommendation { get; init; }
    public required string Boundary { get; init; }
    public required string ReproCommand { get; init; }
}

public sealed record PatchProposal
{
    public required string RunId { get; init; }
    public required long TicketId { get; init; }
    public required string PatchProposalId { get; init; }
    public required string UnifiedDiff { get; init; }
    public required string PatchSha256 { get; init; }
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
    public required string CodeStandardsStatus { get; init; }
    public required string CodeStandardsSummary { get; init; }
    public int CodeStandardsFindingCount { get; init; }
    public int CodeStandardsBlockingFindingCount { get; init; }
    public required PatchValidationResult PatchValidation { get; init; }
    public required RuntimeCommandEvidence BuildResult { get; init; }
    public required RuntimeCommandEvidence TestResult { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<PromotionEvidenceRef> EvidenceLinks { get; init; } = [];
    public string RiskSummary { get; init; } = string.Empty;
    public string Boundary { get; init; } = "Patch proposal is evidence-first. It does not grant apply authority.";
}

public sealed record PatchProposalRequest
{
    public required string RunId { get; init; }
    public required long TicketId { get; init; }
    public required CodeChangeProposal Proposal { get; init; }
    public required PatchValidationResult PatchValidation { get; init; }
    public required RuntimeCommandEvidence BuildResult { get; init; }
    public required RuntimeCommandEvidence TestResult { get; init; }
    public string RequestedBy { get; init; } = "BuilderAgent";
}

public sealed record PromotionPackageRequest
{
    public required string Project { get; init; }
    public required string SourceTraceId { get; init; }
    public required PatchProposal PatchProposal { get; init; }
    public required LanguageRuntimeProfile RuntimeProfile { get; init; }
}

public sealed record ControlledWriteApprovalRequest
{
    public required string RunId { get; init; }
    public required string TraceId { get; init; }
    public required string Project { get; init; }
    public required PromotionPackage Package { get; init; }
    public required string ApprovedBy { get; init; }
    public required string ApprovalRole { get; init; }
    public required string ApprovalPhrase { get; init; }
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ControlledWorktreeApplyRequest
{
    public required string RunId { get; init; }
    public required string TraceId { get; init; }
    public required string ActiveRepoPath { get; init; }
    public required string TargetWorktreePath { get; init; }
    public required string TargetBranchName { get; init; }
    public required PromotionPackage Package { get; init; }
    public required ControlledWriteApprovalRecord Approval { get; init; }
    public ControlledWriteEffectivePolicy? PolicySnapshot { get; init; }
    public bool ForceDirtyActiveRepo { get; init; }
}

public interface IPatchProposalService
{
    Task<PatchProposal> CreateAsync(
        PatchProposalRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPromotionPackageService
{
    PromotionPackage CreatePackage(PromotionPackageRequest request);
}

public interface IControlledWriteApprovalService
{
    ControlledWriteApprovalRecord ApproveForControlledWorktreeApply(ControlledWriteApprovalRequest request);
}

public interface IControlledWorktreeApplyService
{
    Task<ControlledWorktreeDryRunReport> ApplyAsync(
        ControlledWorktreeApplyRequest request,
        CancellationToken cancellationToken = default);
}
