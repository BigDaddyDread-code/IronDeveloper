using IronDev.Core.SourceApply;
using CoreSourceApplyDryRunResult = IronDev.Core.SourceApply.SourceApplyDryRunResult;
using CoreSourceApplyRequest = IronDev.Core.SourceApply.SourceApplyRequest;

namespace IronDev.UnitTests.SourceApply;

internal static class SourceApplyExecutionGateTestFixtures
{
    internal const string RunId = "run:g05";
    internal const string SourceApplyRequestId = "source-apply-request:g05";
    internal const string SourceApplyExecutionRequestId = "source-apply-execution:g05";
    internal const string SourceRepoPath = "repo:g05";
    internal const string SourceRepoIdentity = "repo-identity:g05";
    internal const string BaseBranch = "feature/g05";
    internal const string BaseCommit = "base-commit:g05";
    internal const string PatchPath = "patches/g05.patch";
    internal const string PatchSha256 = "sha256:g05";
    internal const string ChangedFile = "src/g05/Example.cs";
    internal const string RequestedBy = "human:g05";
    internal const string ThoughtLedgerRef = "thought-ledger:g05";
    internal const string ConscienceDecisionId = "conscience:g05";

    internal static readonly DateTimeOffset ObservedAtUtc =
        new(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

    internal static readonly DateTimeOffset ExpiresAtUtc = ObservedAtUtc.AddHours(1);

    internal static GateInputs CompleteInputs() =>
        new()
        {
            Request = ExecutionRequest(),
            ApplyRequest = SourceApplyRequest(),
            Verification = PatchVerification(),
            Approval = Approval(),
            Readiness = Readiness(),
            DryRun = DryRun(),
            RollbackDraft = RollbackDraft(),
            PreSnapshot = PreSnapshot(),
            Conscience = Conscience(),
            ThoughtLedgerRef = ThoughtLedgerRef
        };

    internal static SourceApplyExecutionGateDecision Evaluate(Action<GateInputs>? mutate = null)
    {
        var inputs = CompleteInputs();
        mutate?.Invoke(inputs);

        return SourceApplyExecutionGate.Evaluate(
            inputs.Request,
            inputs.ApplyRequest,
            inputs.Verification,
            inputs.Approval,
            inputs.Readiness,
            inputs.DryRun,
            inputs.RollbackDraft,
            inputs.PreSnapshot,
            inputs.Conscience,
            inputs.ThoughtLedgerRef,
            ObservedAtUtc);
    }

    internal static SourceApplyExecutionRequest ExecutionRequest() =>
        new()
        {
            SourceApplyExecutionRequestId = SourceApplyExecutionRequestId,
            RunId = RunId,
            SourceApplyRequestId = SourceApplyRequestId,
            SourceRepoPath = SourceRepoPath,
            SourceRepoIdentity = SourceRepoIdentity,
            BaseCommit = BaseCommit,
            PatchPath = PatchPath,
            PatchSha256 = PatchSha256,
            ChangedFiles = [ChangedFile],
            RequestedBy = RequestedBy,
            RequestedAtUtc = ObservedAtUtc,
            ConscienceDecisionId = ConscienceDecisionId,
            ThoughtLedgerRef = ThoughtLedgerRef,
            EvidenceRefs = [Evidence("source-apply-request:g05", "SourceApplyRequest")]
        };

    internal static CoreSourceApplyRequest SourceApplyRequest() =>
        new()
        {
            SourceApplyRequestId = SourceApplyRequestId,
            RunId = RunId,
            SourceRepoPath = SourceRepoPath,
            SourceRepoIdentity = SourceRepoIdentity,
            BaseBranch = BaseBranch,
            BaseCommit = BaseCommit,
            PatchPath = PatchPath,
            PatchSha256 = PatchSha256,
            ChangedFiles = [ChangedFile],
            RequestedBy = RequestedBy,
            RequestedAtUtc = ObservedAtUtc,
            EvidenceRefs = [Evidence("patch-package:g05", "PatchPackage")]
        };

    internal static PatchArtifactVerificationResult PatchVerification() =>
        new()
        {
            PatchArtifactVerificationId = "patch-verification:g05",
            RunId = RunId,
            PatchPath = PatchPath,
            PatchExists = true,
            PatchSha256 = PatchSha256,
            ExpectedPatchSha256 = PatchSha256,
            PatchHashMatchesRun = true,
            RunMetadataExists = true,
            BaseCommitMatchesRun = true,
            ChangedFilesMatchRun = true,
            ManualApplyInstructionsExist = true,
            Decision = PatchArtifactVerificationDecision.Verified,
            VerifiedAtUtc = ObservedAtUtc
        };

    internal static SourceApplyApprovalEvidence Approval() =>
        new()
        {
            ApprovalEvidenceId = "approval:g05",
            SourceApplyRequestId = SourceApplyRequestId,
            RunId = RunId,
            SourceRepoIdentity = SourceRepoIdentity,
            BaseCommit = BaseCommit,
            PatchSha256 = PatchSha256,
            ApprovedChangedFiles = [ChangedFile],
            ApprovedBy = RequestedBy,
            ApprovedAtUtc = ObservedAtUtc,
            ConscienceDecisionId = ConscienceDecisionId,
            ThoughtLedgerEntryId = ThoughtLedgerRef,
            ApprovalText = BoundedApprovalText,
            HumanReviewRequired = true
        };

    internal static SourceApplyReadinessReport Readiness() =>
        new()
        {
            SourceApplyReadinessReportId = "readiness:g05",
            RunId = RunId,
            SourceApplyRequestId = SourceApplyRequestId,
            PatchArtifactVerificationId = "patch-verification:g05",
            SourceApplyGateDecisionId = "source-apply-gate:g05",
            SourceApplyDryRunResultId = "dry-run:g05",
            RollbackPlanDraftId = "rollback-plan:g05",
            Readiness = SourceApplyReadiness.ReadyForFutureControlledApply,
            CreatedAtUtc = ObservedAtUtc,
            EvidenceRefs = [Evidence("validation-result:g05", "Validation")]
        };

    internal static CoreSourceApplyDryRunResult DryRun() =>
        new()
        {
            SourceApplyDryRunResultId = "dry-run:g05",
            RunId = RunId,
            SourceApplyDryRunPlanId = "dry-run-plan:g05",
            RehearsalWorkspacePath = "disposable/g05",
            RehearsalBaseCommit = BaseCommit,
            RehearsalHeadCommit = BaseCommit,
            Command = "controlled-dry-run",
            ExitCode = 0,
            StdoutPath = "dry-run/stdout.txt",
            StderrPath = "dry-run/stderr.txt",
            CombinedOutputPath = "dry-run/combined.txt",
            PatchAppliedInRehearsalWorkspace = true,
            SourceRepoMutated = false,
            StartedAtUtc = ObservedAtUtc,
            FinishedAtUtc = ObservedAtUtc.AddMinutes(1),
            Boundary = SourceApplyBoundary.RehearsalApplied
        };

    internal static RollbackPlanDraft RollbackDraft() =>
        new()
        {
            RollbackPlanDraftId = "rollback-plan:g05",
            RunId = RunId,
            PatchSha256 = PatchSha256,
            ChangedFiles = [ChangedFile],
            ReversePatchPath = "patches/g05.reverse.patch",
            RevertInstructionsPath = "rollback/g05.md",
            RiskNotes = ["Evidence only; not rollback execution."],
            CreatedAtUtc = ObservedAtUtc
        };

    internal static SourceSnapshot PreSnapshot() =>
        new()
        {
            SourceSnapshotId = "snapshot:g05",
            RunId = RunId,
            SourceRepoPath = SourceRepoPath,
            HeadCommit = BaseCommit,
            Branch = BaseBranch,
            StatusPorcelain = string.Empty,
            DiffSha256 = "sha256:clean-g05",
            ChangedFiles = [],
            CapturedAtUtc = ObservedAtUtc
        };

    internal static ConscienceDecision Conscience(
        GovernedActionKind actionKind = GovernedActionKind.SourceApply,
        ConscienceDecisionOutcome outcome = ConscienceDecisionOutcome.Allow,
        string subjectId = SourceApplyExecutionRequestId,
        DateTimeOffset? expiresAtUtc = null,
        string? thoughtLedgerRef = ThoughtLedgerRef) =>
        new()
        {
            DecisionId = ConscienceDecisionId,
            ActionId = "governed-action:g05",
            ActionKind = actionKind,
            SubjectKind = "SourceApplyExecutionRequest",
            SubjectId = subjectId,
            RequestedBy = RequestedBy,
            EvidenceRefs =
            [
                new ConscienceDecisionEvidenceRef
                {
                    RefId = "source-apply-evidence:g05",
                    EvidenceKind = "SourceApplyGate",
                    SafeSummary = "Fixed G05 source apply gate evidence."
                }
            ],
            PolicyRefs = ["BlockG05.SourceApplyExecutionGate"],
            RiskLevel = ConscienceDecisionRiskLevel.Critical,
            Decision = outcome,
            RequiredHumanReview = true,
            ThoughtLedgerRef = thoughtLedgerRef,
            DecisionHash = "sha256:conscience-g05",
            ExpiresAtUtc = expiresAtUtc ?? ExpiresAtUtc,
            CreatedAtUtc = ObservedAtUtc
        };

    internal static void AssertAllowed(SourceApplyExecutionGateDecision decision)
    {
        Assert.AreEqual(SourceApplyExecutionGateDecisionOutcome.AllowApplyToWorkingTree, decision.Decision);
        Assert.AreEqual(0, decision.Reasons.Length, string.Join("; ", decision.Reasons));
    }

    internal static void AssertBlocked(SourceApplyExecutionGateDecision decision, string expectedReason)
    {
        Assert.AreEqual(SourceApplyExecutionGateDecisionOutcome.Block, decision.Decision);
        CollectionAssert.Contains(decision.Reasons.ToList(), expectedReason, expectedReason);
    }

    internal static void AssertNoDownstreamAuthority(SourceApplyBoundary boundary)
    {
        Assert.IsFalse(boundary.SourceRepoMutated);
        Assert.IsFalse(boundary.SourceApplied);
        Assert.IsFalse(boundary.GitCommitCreated);
        Assert.IsFalse(boundary.GitPushPerformed);
        Assert.IsFalse(boundary.PullRequestCreated);
        Assert.IsFalse(boundary.ApprovalGranted);
        Assert.IsFalse(boundary.PolicySatisfied);
        Assert.IsFalse(boundary.ReleaseApproved);
        Assert.IsFalse(boundary.DeploymentApproved);
        Assert.IsFalse(boundary.MergeApproved);
        Assert.IsFalse(boundary.WorkflowContinued);
        Assert.IsFalse(boundary.MemoryPromoted);
        Assert.IsFalse(boundary.AgentDispatched);
        Assert.IsFalse(boundary.ModelCalled);
        Assert.IsFalse(boundary.RollbackExecuted);
        Assert.IsFalse(boundary.PatchAppliedInRehearsalWorkspace);
    }

    internal static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    internal const string BoundedApprovalText =
        "I approve this source-apply request for controlled working-tree application only. " +
        "This approval does not permit commit, push, pull request creation, merge, release, deployment, or workflow continuation.";

    private static SourceApplyEvidenceRef Evidence(string refId, string kind) =>
        new()
        {
            RefId = refId,
            EvidenceKind = kind,
            Path = $"evidence/{refId}.json",
            SafeSummary = $"Reference-only {kind} evidence.",
            Sha256 = "sha256:g05-evidence"
        };
}

internal sealed class GateInputs
{
    public required SourceApplyExecutionRequest Request { get; set; }
    public CoreSourceApplyRequest? ApplyRequest { get; set; }
    public PatchArtifactVerificationResult? Verification { get; set; }
    public SourceApplyApprovalEvidence? Approval { get; set; }
    public SourceApplyReadinessReport? Readiness { get; set; }
    public CoreSourceApplyDryRunResult? DryRun { get; set; }
    public RollbackPlanDraft? RollbackDraft { get; set; }
    public SourceSnapshot? PreSnapshot { get; set; }
    public ConscienceDecision? Conscience { get; set; }
    public string? ThoughtLedgerRef { get; set; }
}
