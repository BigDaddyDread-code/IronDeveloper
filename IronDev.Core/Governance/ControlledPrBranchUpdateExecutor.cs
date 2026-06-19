using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public enum PrBranchUpdateExecutionVerdict
{
    Executed = 0,
    Blocked,
    Failed,
    RolledBack,
    Incomplete
}

public enum PrBranchUpdateFailureKind
{
    None = 0,
    MissingPackage,
    PackageIneligible,
    PackageStale,
    WrongBranch,
    WrongPullRequest,
    ExpectedHeadMismatch,
    RemoteHeadMismatch,
    DirtyPreExistingWorktree,
    UndeclaredFileChange,
    GeneratedRestoreArtifact,
    CommitNotAllowed,
    PushNotAllowed,
    ForcePushNotAllowed,
    ExpectedDiffHashMissing,
    DiffHashMismatch,
    StagedFileMismatch,
    CommitFailed,
    PostCommitDirtyWorktree,
    PostCommitFileMismatch,
    PushFailed,
    NonFastForwardPushRejected,
    PostPushRemoteMismatch,
    BoundaryViolation
}

public sealed record PrBranchUpdateBoundary
{
    public bool CanMutatePullRequestBranch { get; init; } = true;
    public bool CanStage { get; init; } = true;
    public bool CanCommit { get; init; } = true;
    public bool CanPush { get; init; } = true;
    public bool CanForcePush { get; init; }
    public bool CanMarkReadyForReview { get; init; }
    public bool CanRequestReviewers { get; init; }
    public bool CanResolveReviewThreads { get; init; }
    public bool CanApprove { get; init; }
    public bool CanMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanTag { get; init; }
    public bool CanPublish { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }

    public static PrBranchUpdateBoundary Executor { get; } = new();
    public static PrBranchUpdateBoundary Blocked { get; } = new()
    {
        CanMutatePullRequestBranch = false,
        CanStage = false,
        CanCommit = false,
        CanPush = false
    };
}

public static class PrBranchUpdateBoundaryText
{
    public const string Boundary = """
        Block AS executes an eligible PR update package against the expected PR branch and writes an execution receipt.
        It may stage, commit, and push only the exact package-declared PR branch update.
        It does not force-push by default.
        It does not mark PRs ready.
        It does not request reviewers.
        It does not resolve review threads.
        It does not approve.
        It does not merge.
        It does not release.
        It does not deploy.
        It does not tag.
        It does not publish.
        It does not promote memory.
        It does not continue workflow.
        PR branch update is not review transition.
        """;
}

public sealed record PrBranchUpdateExecutionRequest
{
    public ControlledPrUpdatePackage? Package { get; init; }
    public int? ExpectedPullRequestNumber { get; init; }
    public string? WorkspacePath { get; init; }
    public string? TargetRemote { get; init; }
    public required string RequestedBy { get; init; }
    public DateTimeOffset? RequestedAtUtc { get; init; }
}

public sealed record PrBranchUpdateObservedState
{
    public required bool RepositoryAvailable { get; init; }
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string HeadSha { get; init; }
    public required string RemoteHeadSha { get; init; }
    public string[] DirtyFiles { get; init; } = [];
    public string[] StagedFiles { get; init; } = [];
    public string[] GeneratedRestoreArtifacts { get; init; } = [];
    public required string DiffHash { get; init; }
    public bool ForcePushRequired { get; init; }
}

public sealed record PrBranchUpdateCommandResult
{
    public required bool Succeeded { get; init; }
    public string? CommitSha { get; init; }
    public string? PostHeadSha { get; init; }
    public string? RemoteHeadSha { get; init; }
    public string[] StagedFiles { get; init; } = [];
    public string[] ChangedFiles { get; init; } = [];
    public string[] Messages { get; init; } = [];
    public bool NonFastForward { get; init; }
}

public interface IPrBranchUpdateCommandClient
{
    Task<PrBranchUpdateObservedState> ObserveAsync(PrBranchUpdateExecutionRequest request, CancellationToken cancellationToken);
    Task<PrBranchUpdateCommandResult> StageAsync(ControlledPrUpdatePackage package, string[] expectedFiles, CancellationToken cancellationToken);
    Task<PrBranchUpdateCommandResult> CommitAsync(ControlledPrUpdatePackage package, CancellationToken cancellationToken);
    Task<PrBranchUpdateCommandResult> PushAsync(ControlledPrUpdatePackage package, string remote, string branch, CancellationToken cancellationToken);
}

public sealed record PrBranchUpdateExecutionReceipt
{
    public required string ExecutionId { get; init; }
    public required string PackageId { get; init; }
    public required string Repository { get; init; }
    public required int PrNumber { get; init; }
    public required string Branch { get; init; }
    public required string PreExecutionHeadSha { get; init; }
    public required string PostExecutionHeadSha { get; init; }
    public required string CommitSha { get; init; }
    public required bool Pushed { get; init; }
    public required string PushRemote { get; init; }
    public required string PushBranch { get; init; }
    public string? SourceApplyReceipt { get; init; }
    public string[] ValidationReceipts { get; init; } = [];
    public required bool DirtyWorktreeBefore { get; init; }
    public required bool DirtyWorktreeAfter { get; init; }
    public string[] ExpectedFilesChanged { get; init; } = [];
    public string[] ActualFilesChanged { get; init; } = [];
    public required bool RollbackAvailable { get; init; }
    public required string RollbackInstructions { get; init; }
    public required PrBranchUpdateExecutionVerdict ExecutionVerdict { get; init; }
    public required PrBranchUpdateFailureKind FailureClassification { get; init; }
    public string[] Issues { get; init; } = [];
    public required DateTimeOffset ExecutedAtUtc { get; init; }
    public PrBranchUpdateBoundary Boundary { get; init; } = PrBranchUpdateBoundary.Executor;
}

public sealed record PrBranchUpdateExecutionResult
{
    public required PrBranchUpdateExecutionVerdict Verdict { get; init; }
    public required PrBranchUpdateFailureKind FailureKind { get; init; }
    public string[] Issues { get; init; } = [];
    public PrBranchUpdateExecutionReceipt? Receipt { get; init; }
}

public static class PrBranchUpdateExecutor
{
    public static async Task<PrBranchUpdateExecutionResult> ExecuteAsync(
        PrBranchUpdateExecutionRequest request,
        IPrBranchUpdateCommandClient client,
        CancellationToken cancellationToken = default)
    {
        var now = request.RequestedAtUtc ?? DateTimeOffset.UtcNow;
        var package = request.Package;
        var issues = new List<string>();
        if (package is null)
            return Blocked(null, request, null, PrBranchUpdateFailureKind.MissingPackage, ["MissingPrUpdatePackage"], now);

        ValidatePackage(package, request, issues);
        if (issues.Count > 0)
            return Blocked(package, request, null, Classify(issues), issues.ToArray(), now);

        var observed = await client.ObserveAsync(request, cancellationToken).ConfigureAwait(false);
        ValidateObserved(package, request, observed, issues);
        if (issues.Count > 0)
            return Blocked(package, request, observed, Classify(issues), issues.ToArray(), now);

        var expectedFiles = FeedbackText.SafeList(package.ExpectedState.ExpectedChangedFiles);
        var stage = await client.StageAsync(package, expectedFiles, cancellationToken).ConfigureAwait(false);
        if (!stage.Succeeded)
            return Failed(package, request, observed, PrBranchUpdateFailureKind.StagedFileMismatch, ["StageFailed", .. stage.Messages], now);

        var staged = FeedbackText.SafeList(stage.StagedFiles);
        if (!SameSet(staged, expectedFiles))
            return Blocked(package, request, observed, PrBranchUpdateFailureKind.StagedFileMismatch, ["StagedFileMismatch"], now, staged);

        var commit = await client.CommitAsync(package, cancellationToken).ConfigureAwait(false);
        if (!commit.Succeeded || string.IsNullOrWhiteSpace(commit.CommitSha))
            return Failed(package, request, observed, PrBranchUpdateFailureKind.CommitFailed, ["CommitFailed", .. commit.Messages], now, staged);

        var postCommitObserved = await client.ObserveAsync(request, cancellationToken).ConfigureAwait(false);
        var postCommitIssues = new List<string>();
        var committedFiles = FeedbackText.SafeList(commit.ChangedFiles.Length == 0 ? staged : commit.ChangedFiles);
        ValidatePostCommit(package, postCommitObserved, commit.CommitSha, committedFiles, postCommitIssues);
        if (postCommitIssues.Count > 0)
        {
            return Failed(
                package,
                request,
                observed,
                postCommitObserved,
                Classify(postCommitIssues),
                postCommitIssues.ToArray(),
                now,
                committedFiles,
                commit.CommitSha,
                postCommitObserved.HeadSha);
        }

        var remote = FeedbackText.Safe(package.BranchUpdateConstraints.TargetRemote);
        var push = await client.PushAsync(package, remote, package.Target.TargetBranch, cancellationToken).ConfigureAwait(false);
        if (!push.Succeeded)
        {
            var kind = push.NonFastForward ? PrBranchUpdateFailureKind.NonFastForwardPushRejected : PrBranchUpdateFailureKind.PushFailed;
            return Failed(package, request, observed, kind, ["PushFailed", .. push.Messages], now, staged, commit.CommitSha, commit.PostHeadSha ?? commit.CommitSha);
        }

        var postPushObserved = await client.ObserveAsync(request, cancellationToken).ConfigureAwait(false);
        var postPushIssues = new List<string>();
        ValidatePostPush(package, postPushObserved, commit.CommitSha, postPushIssues);
        if (postPushIssues.Count > 0)
        {
            return Failed(
                package,
                request,
                observed,
                postPushObserved,
                Classify(postPushIssues),
                postPushIssues.ToArray(),
                now,
                committedFiles,
                commit.CommitSha,
                postPushObserved.HeadSha,
                pushed: false);
        }

        var receipt = BuildReceipt(
            package,
            request,
            observed,
            postPushObserved,
            now,
            verdict: PrBranchUpdateExecutionVerdict.Executed,
            failureKind: PrBranchUpdateFailureKind.None,
            issues: [],
            actualFiles: committedFiles,
            commitSha: commit.CommitSha,
            postHeadSha: postPushObserved.HeadSha,
            pushed: true,
            boundary: PrBranchUpdateBoundary.Executor);

        return new PrBranchUpdateExecutionResult
        {
            Verdict = PrBranchUpdateExecutionVerdict.Executed,
            FailureKind = PrBranchUpdateFailureKind.None,
            Issues = [],
            Receipt = receipt
        };
    }

    public static string ComputeDiffHash(string diffText) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(diffText ?? string.Empty))).ToLowerInvariant();

    public static bool IsGeneratedRestoreArtifact(string path)
    {
        var safe = FeedbackText.Safe(path).Replace('\\', '/');
        return safe.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
               safe.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               safe.EndsWith("/project.assets.json", StringComparison.OrdinalIgnoreCase) ||
               safe.EndsWith(".nuget.g.props", StringComparison.OrdinalIgnoreCase) ||
               safe.EndsWith(".nuget.g.targets", StringComparison.OrdinalIgnoreCase) ||
               safe.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidatePackage(ControlledPrUpdatePackage package, PrBranchUpdateExecutionRequest request, List<string> issues)
    {
        if (package.Verdict != PrUpdatePackageVerdict.PackageReadyForExecutor)
            issues.Add("PackageVerdictNotReady");
        if (package.ExecutionEligibility != PrUpdateExecutionEligibility.Eligible || !package.CanExecuteBranchUpdate)
            issues.Add("PackageNotExecutionEligible");
        if (package.SourceApplyEvidence is null)
            issues.Add("SourceApplyEvidenceRequired");
        if (!string.Equals(package.Target.PrState, "open", StringComparison.OrdinalIgnoreCase))
            issues.Add("TargetPrStateNotOpen");
        if (request.ExpectedPullRequestNumber is not null && request.ExpectedPullRequestNumber.Value != package.Target.PrNumber)
            issues.Add("WrongPullRequest");
        if (!string.IsNullOrWhiteSpace(request.TargetRemote) && !Same(request.TargetRemote, package.BranchUpdateConstraints.TargetRemote))
            issues.Add("TargetRemoteMismatch");
        if (!package.BranchUpdateConstraints.CommitAllowed)
            issues.Add("CommitNotAllowed");
        if (!package.BranchUpdateConstraints.PushAllowed)
            issues.Add("PushNotAllowed");
        if (package.BranchUpdateConstraints.ForcePushAllowed)
            issues.Add("ForcePushNotAllowed");
        if (string.IsNullOrWhiteSpace(package.ExpectedState.ExpectedDiffHash))
            issues.Add("ExpectedDiffHashMissing");
        if (string.IsNullOrWhiteSpace(package.ExpectedState.ExpectedCommitMessage))
            issues.Add("CommitMessageRequired");

        foreach (var file in FeedbackText.SafeList(package.ExpectedState.ExpectedChangedFiles))
        {
            if (IsGeneratedRestoreArtifact(file))
                issues.Add($"GeneratedRestoreArtifact:{file}");
        }
    }

    private static void ValidateObserved(ControlledPrUpdatePackage package, PrBranchUpdateExecutionRequest request, PrBranchUpdateObservedState observed, List<string> issues)
    {
        if (!observed.RepositoryAvailable)
            issues.Add("RepositoryUnavailable");
        if (!string.IsNullOrWhiteSpace(observed.Repository) && !Same(observed.Repository, package.Target.Repository))
            issues.Add("RepositoryMismatch");
        if (!Same(observed.Branch, package.Target.TargetBranch))
            issues.Add("WrongBranch");
        if (!Same(observed.HeadSha, package.Target.ExpectedCurrentHeadSha))
            issues.Add("ExpectedHeadMismatch");
        if (!Same(observed.RemoteHeadSha, package.Target.ExpectedCurrentHeadSha))
            issues.Add("RemoteHeadMismatch");
        if (observed.ForcePushRequired)
            issues.Add("ForcePushNotAllowed");
        if (observed.StagedFiles.Length > 0)
            issues.Add("DirtyPreExistingWorktree");
        if (!Same(observed.DiffHash, package.ExpectedState.ExpectedDiffHash))
            issues.Add("DiffHashMismatch");

        var expectedFiles = FeedbackText.SafeList(package.ExpectedState.ExpectedChangedFiles);
        var dirtyFiles = FeedbackText.SafeList(observed.DirtyFiles);
        if (!SameSet(dirtyFiles, expectedFiles))
        {
            foreach (var file in dirtyFiles.Where(file => !expectedFiles.Contains(file, StringComparer.OrdinalIgnoreCase)))
                issues.Add($"UndeclaredFileChange:{file}");
            foreach (var file in expectedFiles.Where(file => !dirtyFiles.Contains(file, StringComparer.OrdinalIgnoreCase)))
                issues.Add($"ExpectedFileNotDirty:{file}");
        }

        foreach (var file in FeedbackText.SafeList(observed.GeneratedRestoreArtifacts.Concat(dirtyFiles)))
        {
            if (IsGeneratedRestoreArtifact(file))
                issues.Add($"GeneratedRestoreArtifact:{file}");
        }
    }

    private static void ValidatePostCommit(
        ControlledPrUpdatePackage package,
        PrBranchUpdateObservedState observed,
        string commitSha,
        string[] committedFiles,
        List<string> issues)
    {
        if (!Same(observed.Branch, package.Target.TargetBranch))
            issues.Add("PostCommitWrongBranch");
        if (!Same(observed.HeadSha, commitSha))
            issues.Add("PostCommitHeadMismatch");
        if (observed.StagedFiles.Length > 0)
            issues.Add("PostCommitStagedFilesRemain");
        if (observed.DirtyFiles.Length > 0)
            issues.Add("PostCommitDirtyWorktree");

        var expectedFiles = FeedbackText.SafeList(package.ExpectedState.ExpectedChangedFiles);
        var changed = FeedbackText.SafeList(committedFiles);
        if (!SameSet(changed, expectedFiles))
        {
            foreach (var file in changed.Where(file => !expectedFiles.Contains(file, StringComparer.OrdinalIgnoreCase)))
                issues.Add($"UndeclaredCommittedFile:{file}");
            foreach (var file in expectedFiles.Where(file => !changed.Contains(file, StringComparer.OrdinalIgnoreCase)))
                issues.Add($"ExpectedFileNotCommitted:{file}");
        }

        foreach (var file in FeedbackText.SafeList(observed.GeneratedRestoreArtifacts.Concat(observed.DirtyFiles).Concat(changed)))
        {
            if (IsGeneratedRestoreArtifact(file))
                issues.Add($"GeneratedRestoreArtifact:{file}");
        }
    }

    private static void ValidatePostPush(
        ControlledPrUpdatePackage package,
        PrBranchUpdateObservedState observed,
        string commitSha,
        List<string> issues)
    {
        if (!Same(observed.Branch, package.Target.TargetBranch))
            issues.Add("PostPushWrongBranch");
        if (!Same(observed.HeadSha, commitSha))
            issues.Add("PostPushHeadMismatch");
        if (!Same(observed.RemoteHeadSha, commitSha))
            issues.Add("PostPushRemoteHeadMismatch");
        if (observed.StagedFiles.Length > 0 || observed.DirtyFiles.Length > 0)
            issues.Add("PostPushDirtyWorktree");
    }

    private static PrBranchUpdateExecutionResult Blocked(
        ControlledPrUpdatePackage? package,
        PrBranchUpdateExecutionRequest request,
        PrBranchUpdateObservedState? observed,
        PrBranchUpdateFailureKind kind,
        string[] issues,
        DateTimeOffset now,
        string[]? actualFiles = null)
    {
        var receipt = BuildReceipt(package, request, observed, observed, now, PrBranchUpdateExecutionVerdict.Blocked, kind, issues, actualFiles ?? [], string.Empty, observed?.HeadSha ?? string.Empty, pushed: false, PrBranchUpdateBoundary.Blocked);
        return new PrBranchUpdateExecutionResult { Verdict = PrBranchUpdateExecutionVerdict.Blocked, FailureKind = kind, Issues = issues, Receipt = receipt };
    }

    private static PrBranchUpdateExecutionResult Failed(
        ControlledPrUpdatePackage package,
        PrBranchUpdateExecutionRequest request,
        PrBranchUpdateObservedState preObserved,
        PrBranchUpdateFailureKind kind,
        string[] issues,
        DateTimeOffset now,
        string[]? actualFiles = null,
        string? commitSha = null,
        string? postHeadSha = null,
        bool pushed = false)
    {
        var receipt = BuildReceipt(package, request, preObserved, preObserved, now, PrBranchUpdateExecutionVerdict.Failed, kind, issues, actualFiles ?? [], commitSha ?? string.Empty, postHeadSha ?? preObserved.HeadSha, pushed, PrBranchUpdateBoundary.Blocked);
        return new PrBranchUpdateExecutionResult { Verdict = PrBranchUpdateExecutionVerdict.Failed, FailureKind = kind, Issues = issues, Receipt = receipt };
    }

    private static PrBranchUpdateExecutionResult Failed(
        ControlledPrUpdatePackage package,
        PrBranchUpdateExecutionRequest request,
        PrBranchUpdateObservedState preObserved,
        PrBranchUpdateObservedState postObserved,
        PrBranchUpdateFailureKind kind,
        string[] issues,
        DateTimeOffset now,
        string[]? actualFiles = null,
        string? commitSha = null,
        string? postHeadSha = null,
        bool pushed = false)
    {
        var receipt = BuildReceipt(package, request, preObserved, postObserved, now, PrBranchUpdateExecutionVerdict.Failed, kind, issues, actualFiles ?? [], commitSha ?? string.Empty, postHeadSha ?? postObserved.HeadSha, pushed, PrBranchUpdateBoundary.Blocked);
        return new PrBranchUpdateExecutionResult { Verdict = PrBranchUpdateExecutionVerdict.Failed, FailureKind = kind, Issues = issues, Receipt = receipt };
    }

    private static PrBranchUpdateExecutionReceipt BuildReceipt(
        ControlledPrUpdatePackage? package,
        PrBranchUpdateExecutionRequest request,
        PrBranchUpdateObservedState? preObserved,
        PrBranchUpdateObservedState? postObserved,
        DateTimeOffset now,
        PrBranchUpdateExecutionVerdict verdict,
        PrBranchUpdateFailureKind failureKind,
        string[] issues,
        string[] actualFiles,
        string commitSha,
        string postHeadSha,
        bool pushed,
        PrBranchUpdateBoundary boundary)
    {
        var safePackageId = package?.PrUpdatePackageId ?? "missing-pr-update-package";
        var target = package?.Target;
        var remote = FeedbackText.Safe(package?.BranchUpdateConstraints.TargetRemote ?? request.TargetRemote ?? "origin");
        return new PrBranchUpdateExecutionReceipt
        {
            ExecutionId = $"pr_branch_update_exec_{AsPrBranchUpdateHashing.ShortHash($"{safePackageId}|{preObserved?.HeadSha}|{postHeadSha}|{verdict}|{now:O}")}",
            PackageId = safePackageId,
            Repository = target?.Repository ?? preObserved?.Repository ?? "missing",
            PrNumber = target?.PrNumber ?? request.ExpectedPullRequestNumber ?? 0,
            Branch = target?.TargetBranch ?? preObserved?.Branch ?? "missing",
            PreExecutionHeadSha = preObserved?.HeadSha ?? target?.ExpectedCurrentHeadSha ?? string.Empty,
            PostExecutionHeadSha = postHeadSha,
            CommitSha = commitSha,
            Pushed = pushed,
            PushRemote = remote,
            PushBranch = target?.TargetBranch ?? preObserved?.Branch ?? "missing",
            SourceApplyReceipt = package?.SourceApplyEvidence?.SourceApplyReceiptId,
            ValidationReceipts = package?.ValidationEvidence.Select(item => item.ValidationRunId).ToArray() ?? [],
            DirtyWorktreeBefore = (preObserved?.DirtyFiles.Length ?? 0) > 0 || (preObserved?.StagedFiles.Length ?? 0) > 0,
            DirtyWorktreeAfter = (postObserved?.DirtyFiles.Length ?? 0) > 0 || (postObserved?.StagedFiles.Length ?? 0) > 0,
            ExpectedFilesChanged = package?.ExpectedState.ExpectedChangedFiles ?? [],
            ActualFilesChanged = FeedbackText.SafeList(actualFiles),
            RollbackAvailable = package?.RollbackPlan.RollbackAvailable ?? false,
            RollbackInstructions = package?.RollbackPlan.RollbackCommandPreview ?? "No rollback plan available.",
            ExecutionVerdict = verdict,
            FailureClassification = failureKind,
            Issues = FeedbackText.SafeList(issues),
            ExecutedAtUtc = now,
            Boundary = boundary
        };
    }

    private static PrBranchUpdateFailureKind Classify(IEnumerable<string> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Contains("MissingPrUpdatePackage", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.MissingPackage;
            if (issue.Contains("Package", StringComparison.OrdinalIgnoreCase) || issue.Contains("Eligibility", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.PackageIneligible;
            if (issue.Contains("WrongPullRequest", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.WrongPullRequest;
            if (issue.Contains("WrongBranch", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.WrongBranch;
            if (issue.Contains("ExpectedHeadMismatch", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.ExpectedHeadMismatch;
            if (issue.Contains("PostPushRemoteHeadMismatch", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.PostPushRemoteMismatch;
            if (issue.Contains("RemoteHeadMismatch", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.RemoteHeadMismatch;
            if (issue.Contains("DirtyPreExistingWorktree", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.DirtyPreExistingWorktree;
            if (issue.Contains("PostCommitDirtyWorktree", StringComparison.OrdinalIgnoreCase) || issue.Contains("PostPushDirtyWorktree", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.PostCommitDirtyWorktree;
            if (issue.Contains("UndeclaredCommittedFile", StringComparison.OrdinalIgnoreCase) || issue.Contains("ExpectedFileNotCommitted", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.PostCommitFileMismatch;
            if (issue.Contains("UndeclaredFileChange", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.UndeclaredFileChange;
            if (issue.Contains("GeneratedRestoreArtifact", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.GeneratedRestoreArtifact;
            if (issue.Contains("CommitNotAllowed", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.CommitNotAllowed;
            if (issue.Contains("PushNotAllowed", StringComparison.OrdinalIgnoreCase) || issue.Contains("TargetRemoteMismatch", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.PushNotAllowed;
            if (issue.Contains("ForcePushNotAllowed", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.ForcePushNotAllowed;
            if (issue.Contains("ExpectedDiffHashMissing", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.ExpectedDiffHashMissing;
            if (issue.Contains("DiffHashMismatch", StringComparison.OrdinalIgnoreCase)) return PrBranchUpdateFailureKind.DiffHashMismatch;
        }

        return PrBranchUpdateFailureKind.BoundaryViolation;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameSet(string[] left, string[] right) =>
        left.Length == right.Length && left.All(item => right.Contains(item, StringComparer.OrdinalIgnoreCase));
}

public static class PrBranchUpdateBypassEvaluator
{
    public static bool CanMarkReadyForReview(object? evidence) => false;
    public static bool CanRequestReviewers(object? evidence) => false;
    public static bool CanResolveReviewThreads(object? evidence) => false;
    public static bool CanApprove(object? evidence) => false;
    public static bool CanMerge(object? evidence) => false;
    public static bool CanRelease(object? evidence) => false;
    public static bool CanDeploy(object? evidence) => false;
    public static bool CanTag(object? evidence) => false;
    public static bool CanPublish(object? evidence) => false;
    public static bool CanPromoteMemory(object? evidence) => false;
    public static bool CanContinueWorkflow(object? evidence) => false;
}

internal static class AsPrBranchUpdateHashing
{
    public static string ShortHash(string? value) => Hash(value)[..16];
    public static string Hash(string? value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}
