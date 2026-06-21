using BvCommitPackageBuilder = IronDev.Core.Governance.Commit.CommitPackageBuilder;

namespace IronDev.Core.Governance.CommitExecution;

public static class ControlledCommitExecutor
{
    private static readonly string[] ReceiptForbiddenActions =
    [
        "do not push from commit receipt",
        "do not create PR from commit receipt",
        "do not merge from commit receipt",
        "do not release from commit receipt",
        "do not deploy from commit receipt",
        "do not continue workflow from commit receipt",
        "do not promote memory from commit receipt",
        "commit receipt does not satisfy policy",
        "commit receipt does not approve the next mutation"
    ];

    private static readonly string[] BlockedForbiddenActions =
    [
        "do not commit from blocked commit execution",
        "do not push from blocked commit execution",
        "do not create PR from blocked commit execution",
        "do not merge from blocked commit execution",
        "do not release from blocked commit execution",
        "do not deploy from blocked commit execution",
        "do not continue workflow from blocked commit execution",
        "do not promote memory from blocked commit execution"
    ];

    public static async Task<ControlledCommitExecutionResult> ExecuteAsync(
        ControlledCommitExecutionRequest? request,
        ICommitWorktreeInspector inspector,
        IControlledCommitGateway gateway,
        ControlledCommitExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ControlledCommitExecutionOptions();
        var now = DateTimeOffset.UtcNow;
        if (request is null)
        {
            return BuildResult(
                null,
                ControlledCommitExecutionVerdict.Blocked,
                ControlledCommitFailureKind.MissingRequest,
                isCommitExecuted: false,
                receipt: null,
                preObservation: null,
                postObservation: null,
                issues: ["ControlledCommitExecutionRequestRequired"],
                now);
        }

        now = request.ObservedAtUtc == default ? now : request.ObservedAtUtc;
        var preflightIssues = new List<string>();
        ValidateRequestEnvelope(request, options, preflightIssues);
        ValidateCommitPackage(request, preflightIssues);
        ValidateSourceApplyReceipt(request, preflightIssues);
        ValidateExpectedDiff(request, preflightIssues);
        ValidateCommitAuthority(request, preflightIssues);
        ValidateManifest(request, preflightIssues);

        if (preflightIssues.Count > 0)
        {
            return BuildResult(
                request,
                ControlledCommitExecutionVerdict.Blocked,
                Classify(preflightIssues),
                isCommitExecuted: false,
                receipt: null,
                preObservation: null,
                postObservation: null,
                issues: preflightIssues,
                now);
        }

        var preObservation = await inspector.ObservePreCommitAsync(request, cancellationToken).ConfigureAwait(false);
        var preObservationIssues = new List<string>();
        ValidatePreObservation(request, preObservation, options, preObservationIssues);
        if (preObservationIssues.Count > 0)
        {
            return BuildResult(
                request,
                ControlledCommitExecutionVerdict.Blocked,
                Classify(preObservationIssues),
                isCommitExecuted: false,
                receipt: null,
                preObservation,
                postObservation: null,
                issues: preObservationIssues,
                now);
        }

        ControlledCommitReceipt? receipt;
        try
        {
            receipt = await gateway.CommitAsync(BuildGatewayRequest(request, preObservation), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return BuildResult(
                request,
                ControlledCommitExecutionVerdict.Failed,
                ControlledCommitFailureKind.GatewayFailed,
                isCommitExecuted: false,
                receipt: null,
                preObservation,
                postObservation: null,
                issues: ["ControlledCommitGatewayFailed", ex.GetType().Name],
                now);
        }

        var receiptIssues = new List<string>();
        ValidateReceipt(request, preObservation, receipt, options, receiptIssues);
        var isCommitExecuted = !string.IsNullOrWhiteSpace(receipt?.CommitId);
        if (receiptIssues.Count > 0 || receipt is null)
        {
            return BuildResult(
                request,
                ControlledCommitExecutionVerdict.Failed,
                Classify(receiptIssues.Count == 0 ? ["ControlledCommitReceiptRequired"] : receiptIssues),
                isCommitExecuted,
                receipt,
                preObservation,
                postObservation: null,
                issues: receiptIssues.Count == 0 ? ["ControlledCommitReceiptRequired"] : receiptIssues,
                now);
        }

        var postObservation = await inspector.ObservePostCommitAsync(request, receipt, cancellationToken).ConfigureAwait(false);
        var postIssues = new List<string>();
        ValidatePostObservation(request, receipt, postObservation, postIssues);
        if (postIssues.Count > 0)
        {
            return BuildResult(
                request,
                ControlledCommitExecutionVerdict.Failed,
                ControlledCommitFailureKind.PostStateInvalid,
                isCommitExecuted: true,
                receipt,
                preObservation,
                postObservation,
                postIssues,
                now);
        }

        return BuildResult(
            request,
            ControlledCommitExecutionVerdict.Completed,
            ControlledCommitFailureKind.None,
            isCommitExecuted: true,
            receipt,
            preObservation,
            postObservation,
            issues: [],
            now);
    }

    private static void ValidateRequestEnvelope(
        ControlledCommitExecutionRequest request,
        ControlledCommitExecutionOptions options,
        ICollection<string> issues)
    {
        RequireText(request.ExecutionId, "ExecutionIdRequired", issues);
        ValidateSingleExplicitScope(request.Repository, "Repository", issues);
        ValidateSingleExplicitScope(request.Branch, "Branch", issues);
        ValidateSingleExplicitScope(request.RunId, "RunId", issues);
        RequireText(request.WorktreeRoot, "WorktreeRootRequired", issues);
        if (string.IsNullOrWhiteSpace(request.PatchHash))
            issues.Add("PatchHashRequired");
        else if (!OperationEligibilityPatchHashRules.IsSafePatchHash(request.PatchHash))
            issues.Add("PatchHashInvalid");
        if (string.IsNullOrWhiteSpace(request.ExpectedDiffHash))
            issues.Add("ExpectedDiffHashRequired");
        else if (!OperationEligibilityPatchHashRules.IsSafePatchHash(request.ExpectedDiffHash))
            issues.Add("ExpectedDiffHashInvalid");
        if (request.ObservedAtUtc == default)
            issues.Add("ObservedAtUtcRequired");
        ValidateExactFilePaths(request.ExpectedFilePaths, "ExpectedFilePaths", options, issues);
    }

    private static void ValidateCommitPackage(ControlledCommitExecutionRequest request, ICollection<string> issues)
    {
        if (request.CommitPackageRequest is null)
        {
            issues.Add("CommitPackageRequestRequired");
            return;
        }

        var package = BvCommitPackageBuilder.Build(request.CommitPackageRequest);
        if (!package.IsPackageCreated)
        {
            issues.Add("CommitPackageNotEligible");
            foreach (var issue in package.Issues)
                issues.Add($"CommitPackage:{issue}");
        }

        if (package.OperationStatus.State != GovernedOperationState.Eligible)
            issues.Add("CommitPackageStatusNotEligible");
        if (!string.Equals(package.OperationStatus.OperationKind, RunAuthorityOperationKind.Commit.ToString(), StringComparison.OrdinalIgnoreCase))
            issues.Add("CommitPackageOperationKindMismatch");
    }

    private static void ValidateManifest(ControlledCommitExecutionRequest request, ICollection<string> issues)
    {
        var manifest = request.CommitPackageManifest;
        var packageRequest = request.CommitPackageRequest;
        if (manifest is null)
        {
            issues.Add("CommitPackageManifestRequired");
            return;
        }

        if (packageRequest is not null && !Same(manifest.PackageId, packageRequest.PackageId))
            issues.Add("CommitPackageManifestPackageIdMismatch");
        Match(manifest.Repository, request.Repository, "CommitPackageManifestRepositoryMismatch", issues);
        Match(manifest.Branch, request.Branch, "CommitPackageManifestBranchMismatch", issues);
        Match(manifest.RunId, request.RunId, "CommitPackageManifestRunIdMismatch", issues);
        Match(manifest.PatchHash, request.PatchHash, "CommitPackageManifestPatchHashMismatch", issues);
        Match(manifest.ExpectedDiffHash, request.ExpectedDiffHash, "CommitPackageManifestExpectedDiffHashMismatch", issues);
        if (!SameSet(manifest.FilePaths, request.ExpectedFilePaths))
            issues.Add("CommitPackageManifestFileSetMismatch");
        if (manifest.OperationStatus.State != GovernedOperationState.Eligible)
            issues.Add("CommitPackageManifestStatusNotEligible");
        if (!Same(manifest.OperationStatus.OperationKind, RunAuthorityOperationKind.Commit.ToString()))
            issues.Add("CommitPackageManifestOperationKindMismatch");
        if (!Contains(manifest.OperationStatus.ForbiddenActions, "do not commit from package alone"))
            issues.Add("CommitPackageManifestMissingNoCommitFromPackageBoundary");
    }

    private static void ValidateSourceApplyReceipt(ControlledCommitExecutionRequest request, ICollection<string> issues)
    {
        var receipt = request.CommitPackageRequest?.SourceApplyReceipt;
        if (receipt is null)
        {
            issues.Add("SourceApplyReceiptRequired");
            return;
        }

        if (string.IsNullOrWhiteSpace(receipt.ReceiptRef) ||
            !receipt.ReceiptRef.StartsWith("source-apply-receipt:", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("SourceApplyReceiptRefInvalid");
        }

        Match(receipt.Repository, request.Repository, "SourceApplyReceiptRepositoryMismatch", issues);
        Match(receipt.Branch, request.Branch, "SourceApplyReceiptBranchMismatch", issues);
        Match(receipt.RunId, request.RunId, "SourceApplyReceiptRunIdMismatch", issues);
        Match(receipt.PatchHash, request.PatchHash, "SourceApplyReceiptPatchHashMismatch", issues);
        if (!SameSet(receipt.AppliedFilePaths, request.ExpectedFilePaths))
            issues.Add("SourceApplyReceiptFileSetMismatch");
    }

    private static void ValidateExpectedDiff(ControlledCommitExecutionRequest request, ICollection<string> issues)
    {
        var diff = request.CommitPackageRequest?.ExpectedDiff;
        if (diff is null)
        {
            issues.Add("ExpectedDiffEvidenceRequired");
            return;
        }

        Match(diff.Repository, request.Repository, "ExpectedDiffRepositoryMismatch", issues);
        Match(diff.Branch, request.Branch, "ExpectedDiffBranchMismatch", issues);
        Match(diff.RunId, request.RunId, "ExpectedDiffRunIdMismatch", issues);
        Match(diff.PatchHash, request.PatchHash, "ExpectedDiffPatchHashMismatch", issues);
        Match(diff.ExpectedDiffHash, request.ExpectedDiffHash, "ExpectedDiffHashMismatch", issues);
        if (!diff.IsCleanExpectedDiff)
            issues.Add("ExpectedDiffNotClean");
        if (!SameSet(diff.ExpectedChangedFilePaths, request.ExpectedFilePaths))
            issues.Add("ExpectedDiffFileSetMismatch");
    }

    private static void ValidateCommitAuthority(ControlledCommitExecutionRequest request, ICollection<string> issues)
    {
        var authority = request.CommitPackageRequest?.CommitAuthority;
        if (authority is null)
        {
            issues.Add("CommitOperationAuthorityRequired");
            return;
        }

        Match(authority.Repository, request.Repository, "CommitAuthorityRepositoryMismatch", issues);
        Match(authority.Branch, request.Branch, "CommitAuthorityBranchMismatch", issues);
        Match(authority.RunId, request.RunId, "CommitAuthorityRunIdMismatch", issues);
        Match(authority.PatchHash, request.PatchHash, "CommitAuthorityPatchHashMismatch", issues);
        if (!SameSet(authority.FilePaths, request.ExpectedFilePaths))
            issues.Add("CommitAuthorityFileSetMismatch");
        if (authority.Decision is null ||
            authority.Decision.OperationKind != RunAuthorityOperationKind.Commit ||
            !authority.Decision.IsEligibleUnderProfileAndGrant ||
            authority.Decision.BlockedReasons.Count > 0 ||
            authority.Decision.MissingEvidence.Count > 0)
        {
            issues.Add("CommitOperationAuthorityRequired");
        }
    }

    private static void ValidatePreObservation(
        ControlledCommitExecutionRequest request,
        CommitWorktreeObservation observation,
        ControlledCommitExecutionOptions options,
        ICollection<string> issues)
    {
        if (!observation.IsWorktreeReadable)
            issues.Add("CommitWorktreeObservationFailed");
        Match(observation.Repository, request.Repository, "PreCommitObservationRepositoryMismatch", issues);
        Match(observation.Branch, request.Branch, "PreCommitObservationBranchMismatch", issues);
        Match(observation.WorktreeRoot, request.WorktreeRoot, "PreCommitObservationWorktreeRootMismatch", issues);
        RequireText(observation.HeadCommitId, "PreCommitHeadCommitIdRequired", issues);
        Match(observation.CurrentDiffHash, request.ExpectedDiffHash, "PreCommitDiffHashMismatch", issues);
        ValidateExactFilePaths(observation.ChangedFilePaths, "PreCommitChangedFilePaths", options, issues);
        ValidateExactFilePaths(observation.StagedFilePaths, "PreCommitStagedFilePaths", options, issues, allowEmpty: true);
        ValidateExactFilePaths(observation.UntrackedFilePaths, "PreCommitUntrackedFilePaths", options, issues, allowEmpty: true);
        if (!SameSet(observation.ChangedFilePaths, request.ExpectedFilePaths))
            issues.Add("PreCommitChangedFileSetMismatch");
        if (HasValues(observation.StagedFilePaths))
            issues.Add("PreCommitStagedFilesNotEmpty");
        if (HasValues(observation.UntrackedFilePaths))
            issues.Add("PreCommitUntrackedFilesNotEmpty");
    }

    private static void ValidateReceipt(
        ControlledCommitExecutionRequest request,
        CommitWorktreeObservation preObservation,
        ControlledCommitReceipt? receipt,
        ControlledCommitExecutionOptions options,
        ICollection<string> issues)
    {
        if (receipt is null)
        {
            issues.Add("ControlledCommitReceiptRequired");
            return;
        }

        if (string.IsNullOrWhiteSpace(receipt.ReceiptRef) ||
            !receipt.ReceiptRef.StartsWith("controlled-commit-receipt:", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("ControlledCommitReceiptRefInvalid");
        }

        Match(receipt.Repository, request.Repository, "CommitReceiptRepositoryMismatch", issues);
        Match(receipt.Branch, request.Branch, "CommitReceiptBranchMismatch", issues);
        Match(receipt.RunId, request.RunId, "CommitReceiptRunIdMismatch", issues);
        Match(receipt.PatchHash, request.PatchHash, "CommitReceiptPatchHashMismatch", issues);
        Match(receipt.PackageId, request.CommitPackageRequest?.PackageId, "CommitReceiptPackageIdMismatch", issues);
        RequireText(receipt.CommitId, "CommitReceiptCommitIdRequired", issues);
        Match(receipt.ParentCommitId, preObservation.HeadCommitId, "CommitReceiptParentCommitIdMismatch", issues);
        if (!SameSet(receipt.CommittedFilePaths, request.ExpectedFilePaths))
            issues.Add("CommitReceiptFileSetMismatch");
        ValidateExactFilePaths(receipt.CommittedFilePaths, "CommitReceiptCommittedFilePaths", options, issues);
        if (string.IsNullOrWhiteSpace(receipt.CommitSubject))
            issues.Add("CommitReceiptSubjectRequired");
        if (receipt.CommittedAtUtc == default)
            issues.Add("CommitReceiptCommittedAtUtcRequired");
        if (options.RequireHooksDisabled && !receipt.HooksDisabled)
            issues.Add("CommitReceiptHooksMustBeDisabled");
        if (receipt.PushAttempted || receipt.PullRequestCreationAttempted || receipt.MergeAttempted ||
            receipt.ReleaseAttempted || receipt.DeploymentAttempted || receipt.MemoryWriteAttempted ||
            receipt.ContinuationAttempted)
        {
            issues.Add("CommitReceiptDownstreamAuthorityAttempted");
        }
    }

    private static void ValidatePostObservation(
        ControlledCommitExecutionRequest request,
        ControlledCommitReceipt receipt,
        CommitPostStateObservation observation,
        ICollection<string> issues)
    {
        if (!observation.IsObservedAfterCommit)
            issues.Add("PostCommitObservationRequired");
        Match(observation.Repository, request.Repository, "PostCommitObservationRepositoryMismatch", issues);
        Match(observation.Branch, request.Branch, "PostCommitObservationBranchMismatch", issues);
        Match(observation.HeadCommitId, receipt.CommitId, "PostCommitHeadCommitIdMismatch", issues);
        if (HasValues(observation.RemainingChangedFilePaths))
            issues.Add("PostCommitRemainingChangedFiles");
        if (HasValues(observation.RemainingStagedFilePaths))
            issues.Add("PostCommitRemainingStagedFiles");
        if (HasValues(observation.RemainingUntrackedFilePaths))
            issues.Add("PostCommitRemainingUntrackedFiles");
    }

    private static ControlledCommitGatewayRequest BuildGatewayRequest(
        ControlledCommitExecutionRequest request,
        CommitWorktreeObservation observation) =>
        new()
        {
            Repository = request.Repository.Trim(),
            Branch = request.Branch.Trim(),
            WorktreeRoot = request.WorktreeRoot.Trim(),
            ExpectedHeadCommitId = observation.HeadCommitId.Trim(),
            FilePathsToStage = Clean(request.ExpectedFilePaths),
            CommitSubject = request.CommitPackageRequest?.MessageEvidence?.Subject.Trim() ?? string.Empty,
            CommitBody = request.CommitPackageRequest?.MessageEvidence?.Body?.Trim() ?? string.Empty,
            DisableHooks = true,
            PackageId = request.CommitPackageRequest?.PackageId.Trim() ?? string.Empty,
            RunId = request.RunId.Trim(),
            PatchHash = request.PatchHash.Trim()
        };

    private static ControlledCommitExecutionResult BuildResult(
        ControlledCommitExecutionRequest? request,
        ControlledCommitExecutionVerdict verdict,
        ControlledCommitFailureKind failureKind,
        bool isCommitExecuted,
        ControlledCommitReceipt? receipt,
        CommitWorktreeObservation? preObservation,
        CommitPostStateObservation? postObservation,
        IReadOnlyCollection<string> issues,
        DateTimeOffset now)
    {
        var status = BuildStatus(request, verdict, receipt, issues, now);
        var validation = GovernedOperationStatusValidator.Validate(status);
        return new ControlledCommitExecutionResult
        {
            IsCommitExecuted = isCommitExecuted,
            Verdict = verdict,
            FailureKind = failureKind,
            Receipt = receipt,
            PreCommitObservation = preObservation,
            PostCommitObservation = postObservation,
            OperationStatus = status,
            StatusValidation = validation,
            Issues = Clean(issues.Concat(validation.Issues).Concat(validation.RedFlags))
        };
    }

    private static GovernedOperationStatus BuildStatus(
        ControlledCommitExecutionRequest? request,
        ControlledCommitExecutionVerdict verdict,
        ControlledCommitReceipt? receipt,
        IReadOnlyCollection<string> issues,
        DateTimeOffset now)
    {
        var state = verdict switch
        {
            ControlledCommitExecutionVerdict.Completed => GovernedOperationState.Completed,
            ControlledCommitExecutionVerdict.Failed => GovernedOperationState.Failed,
            _ => GovernedOperationState.Blocked
        };

        return new GovernedOperationStatus
        {
            OperationId = CleanText(request?.ExecutionId, "controlled-commit-execution-blocked"),
            OperationKind = RunAuthorityOperationKind.Commit.ToString(),
            Subject = request is null
                ? "controlled commit execution"
                : $"controlled commit for {CleanText(request.Repository, "unknown-repository")} {CleanText(request.Branch, "unknown-branch")} {CleanText(request.PatchHash, "unknown-patch")}",
            State = state,
            BlockedReasons = state == GovernedOperationState.Blocked ? Clean(issues) : [],
            MissingEvidence = state == GovernedOperationState.Blocked ? BuildMissingEvidence(issues) : [],
            NextSafeActions = state switch
            {
                GovernedOperationState.Completed => ["inspect controlled commit receipt", "request push authority separately if needed"],
                GovernedOperationState.Failed => ["inspect commit execution failure", "request fresh authority and worktree observation before retry"],
                _ => ["collect corrected commit execution evidence", "request controlled commit execution only after preflight issues are fixed"]
            },
            ForbiddenActions = state == GovernedOperationState.Completed ? ReceiptForbiddenActions : BlockedForbiddenActions,
            EvidenceRefs = BuildEvidenceRefs(request),
            ReceiptRefs = receipt is null ? BuildReceiptRefs(request) : Clean([receipt.ReceiptRef, .. BuildReceiptRefs(request)]),
            ExpiresAtUtc = null,
            ObservedAtUtc = now
        };
    }

    private static IReadOnlyList<string> BuildMissingEvidence(IReadOnlyCollection<string> issues)
    {
        var missing = new List<string>();
        foreach (var issue in issues)
        {
            if (issue.Contains("Request", StringComparison.OrdinalIgnoreCase)) missing.Add("controlled-commit-execution-request");
            if (issue.Contains("Manifest", StringComparison.OrdinalIgnoreCase)) missing.Add("commit-package-manifest");
            if (issue.Contains("SourceApplyReceipt", StringComparison.OrdinalIgnoreCase)) missing.Add("source-apply-receipt");
            if (issue.Contains("CommitAuthority", StringComparison.OrdinalIgnoreCase) || issue.Contains("CommitOperationAuthority", StringComparison.OrdinalIgnoreCase)) missing.Add("commit-operation-authority");
            if (issue.Contains("ExpectedDiff", StringComparison.OrdinalIgnoreCase)) missing.Add("expected-diff");
            if (issue.Contains("Observation", StringComparison.OrdinalIgnoreCase) || issue.Contains("Worktree", StringComparison.OrdinalIgnoreCase)) missing.Add("fresh-worktree-observation");
        }

        return Clean(missing);
    }

    private static IReadOnlyList<string> BuildEvidenceRefs(ControlledCommitExecutionRequest? request) =>
        Clean(
        [
            Ref("controlled-commit-execution-request", request?.ExecutionId),
            Ref("repo", request?.Repository),
            Ref("branch", request?.Branch),
            Ref("run", request?.RunId),
            Ref("patch-hash", request?.PatchHash),
            request?.CommitPackageRequest?.CommitAuthority?.EvidenceRef,
            request?.CommitPackageRequest?.ExpectedDiff?.EvidenceRef,
            request?.CommitPackageRequest?.MessageEvidence?.EvidenceRef,
            .. ValuesOrEmpty(request?.EvidenceRefs)
        ]);

    private static IReadOnlyList<string> BuildReceiptRefs(ControlledCommitExecutionRequest? request) =>
        Clean(
        [
            request?.CommitPackageRequest?.SourceApplyReceipt?.ReceiptRef,
            .. ValuesOrEmpty(request?.ReceiptRefs)
        ]);

    private static ControlledCommitFailureKind Classify(IEnumerable<string> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Contains("Package", StringComparison.OrdinalIgnoreCase)) return ControlledCommitFailureKind.CommitPackageInvalid;
            if (issue.Contains("Manifest", StringComparison.OrdinalIgnoreCase)) return ControlledCommitFailureKind.ManifestMismatch;
            if (issue.Contains("SourceApplyReceipt", StringComparison.OrdinalIgnoreCase)) return ControlledCommitFailureKind.SourceApplyReceiptMismatch;
            if (issue.Contains("CommitAuthority", StringComparison.OrdinalIgnoreCase) || issue.Contains("CommitOperationAuthority", StringComparison.OrdinalIgnoreCase)) return ControlledCommitFailureKind.CommitAuthorityMismatch;
            if (issue.Contains("ExpectedDiff", StringComparison.OrdinalIgnoreCase) || issue.Contains("DiffHash", StringComparison.OrdinalIgnoreCase)) return ControlledCommitFailureKind.ExpectedDiffMismatch;
            if (issue.Contains("ForbiddenFile", StringComparison.OrdinalIgnoreCase)) return ControlledCommitFailureKind.ForbiddenFileObserved;
            if (issue.Contains("ObservationFailed", StringComparison.OrdinalIgnoreCase) || issue.Contains("WorktreeObservation", StringComparison.OrdinalIgnoreCase)) return ControlledCommitFailureKind.WorktreeObservationFailed;
            if (issue.Contains("PreCommit", StringComparison.OrdinalIgnoreCase) || issue.Contains("Worktree", StringComparison.OrdinalIgnoreCase)) return ControlledCommitFailureKind.WorktreeStateMismatch;
            if (issue.Contains("Receipt", StringComparison.OrdinalIgnoreCase)) return ControlledCommitFailureKind.ReceiptInvalid;
            if (issue.Contains("PostCommit", StringComparison.OrdinalIgnoreCase)) return ControlledCommitFailureKind.PostStateInvalid;
        }

        return ControlledCommitFailureKind.RequestInvalid;
    }

    private static void ValidateSingleExplicitScope(string? value, string label, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{label}Required");
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal) ||
            trimmed.Contains('?', StringComparison.Ordinal) ||
            trimmed.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{label}MustBeSingleExplicitScope");
        }
    }

    private static void ValidateExactFilePaths(
        IReadOnlyCollection<string>? filePaths,
        string label,
        ControlledCommitExecutionOptions options,
        ICollection<string> issues,
        bool allowEmpty = false)
    {
        if (filePaths is null || filePaths.Count == 0)
        {
            if (!allowEmpty)
                issues.Add($"{label}Required");
            return;
        }

        foreach (var filePath in filePaths)
        {
            if (!IsSafeExactRelativeFilePath(filePath))
                issues.Add($"{label}Unsafe:{filePath}");
            if (IsForbiddenFile(filePath, options))
                issues.Add($"ForbiddenFileObserved:{filePath}");
        }
    }

    private static bool IsSafeExactRelativeFilePath(string? filePath)
    {
        if (!BoundedRunAuthorityGrantFileScope.IsSafeRelativeGlob(filePath))
            return false;

        return filePath is not null &&
               !filePath.Contains('*', StringComparison.Ordinal) &&
               !filePath.Contains('?', StringComparison.Ordinal) &&
               !filePath.Trim().EndsWith("/", StringComparison.Ordinal);
    }

    private static bool IsForbiddenFile(string filePath, ControlledCommitExecutionOptions options) =>
        options.ForbiddenFileGlobs.Any(glob => BoundedRunAuthorityGrantFileScope.IsAllowed(filePath, [glob], []));

    private static void RequireText(string? value, string issue, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(issue);
    }

    private static void Match(string? actual, string? expected, string issue, ICollection<string> issues)
    {
        if (!Same(actual, expected))
            issues.Add(issue);
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameSet(IReadOnlyCollection<string>? left, IReadOnlyCollection<string>? right)
    {
        var leftSet = Clean(left);
        var rightSet = Clean(right);
        return leftSet.Count == rightSet.Count && !leftSet.Except(rightSet, StringComparer.OrdinalIgnoreCase).Any();
    }

    private static bool Contains(IEnumerable<string> values, string expected) =>
        values.Any(value => Same(value, expected));

    private static bool HasValues(IReadOnlyCollection<string>? values) =>
        values is not null && values.Any(value => !string.IsNullOrWhiteSpace(value));

    private static string Ref(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $"{prefix}:{value.Trim()}";

    private static string CleanText(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static IReadOnlyList<string> Clean(IEnumerable<string?>? values) =>
        ValuesOrEmpty(values)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string?> ValuesOrEmpty(IEnumerable<string?>? values) =>
        values ?? [];
}
