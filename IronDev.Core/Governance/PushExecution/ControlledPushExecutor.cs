using IronDev.Core.Governance.CommitExecution;

namespace IronDev.Core.Governance.PushExecution;

public static class ControlledPushExecutor
{
    private static readonly string[] ReceiptForbiddenActions =
    [
        "do not create PR from push receipt",
        "do not mark ready for review from push receipt",
        "do not request reviewers from push receipt",
        "do not merge from push receipt",
        "do not release from push receipt",
        "do not deploy from push receipt",
        "do not continue workflow from push receipt",
        "do not promote memory from push receipt",
        "push receipt does not satisfy policy",
        "push receipt does not approve the next mutation"
    ];

    private static readonly string[] BlockedForbiddenActions =
    [
        "do not push from blocked push execution",
        "do not create PR from blocked push execution",
        "do not mark ready for review from blocked push execution",
        "do not request reviewers from blocked push execution",
        "do not merge from blocked push execution",
        "do not release from blocked push execution",
        "do not deploy from blocked push execution",
        "do not continue workflow from blocked push execution",
        "do not promote memory from blocked push execution"
    ];

    public static async Task<ControlledPushExecutionResult> ExecuteAsync(
        ControlledPushExecutionRequest? request,
        IPushRemoteStateInspector inspector,
        IControlledPushGateway gateway,
        ControlledPushExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (request is null)
        {
            return BuildResult(
                null,
                ControlledPushExecutionVerdict.Blocked,
                ControlledPushFailureKind.MissingRequest,
                isPushExecuted: false,
                receipt: null,
                preObservation: null,
                postObservation: null,
                issues: ["ControlledPushExecutionRequestRequired"],
                now);
        }

        now = request.ObservedAtUtc == default ? now : request.ObservedAtUtc;
        if (inspector is null)
        {
            return BuildResult(
                request,
                ControlledPushExecutionVerdict.Blocked,
                ControlledPushFailureKind.RemoteObservationFailed,
                isPushExecuted: false,
                receipt: null,
                preObservation: null,
                postObservation: null,
                issues: ["PushRemoteStateInspectorRequired"],
                now);
        }

        if (gateway is null)
        {
            return BuildResult(
                request,
                ControlledPushExecutionVerdict.Blocked,
                ControlledPushFailureKind.GatewayFailed,
                isPushExecuted: false,
                receipt: null,
                preObservation: null,
                postObservation: null,
                issues: ["ControlledPushGatewayRequired"],
                now);
        }

        var preflightIssues = new List<string>();
        ValidateRequestEnvelope(request, preflightIssues);
        ValidateCommitReceipt(request, preflightIssues);
        ValidatePushAuthority(request, preflightIssues);

        if (preflightIssues.Count > 0)
        {
            return BuildResult(
                request,
                ControlledPushExecutionVerdict.Blocked,
                Classify(preflightIssues),
                isPushExecuted: false,
                receipt: null,
                preObservation: null,
                postObservation: null,
                preflightIssues,
                now);
        }

        var preObservation = await inspector.ObservePrePushAsync(request, cancellationToken).ConfigureAwait(false);
        var preObservationIssues = new List<string>();
        ValidatePreObservation(request, preObservation, preObservationIssues);
        if (preObservationIssues.Count > 0)
        {
            return BuildResult(
                request,
                ControlledPushExecutionVerdict.Blocked,
                Classify(preObservationIssues),
                isPushExecuted: false,
                receipt: null,
                preObservation,
                postObservation: null,
                preObservationIssues,
                now);
        }

        ControlledPushReceipt? receipt;
        try
        {
            receipt = await gateway.PushAsync(BuildGatewayRequest(request), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return BuildResult(
                request,
                ControlledPushExecutionVerdict.Failed,
                ControlledPushFailureKind.GatewayFailed,
                isPushExecuted: false,
                receipt: null,
                preObservation,
                postObservation: null,
                issues: ["ControlledPushGatewayFailed", ex.GetType().Name],
                now);
        }

        var receiptIssues = new List<string>();
        ValidateReceipt(request, receipt, receiptIssues);
        var isPushExecuted = !string.IsNullOrWhiteSpace(receipt?.PushedCommitId);
        if (receiptIssues.Count > 0 || receipt is null)
        {
            return BuildResult(
                request,
                ControlledPushExecutionVerdict.Failed,
                Classify(receiptIssues.Count == 0 ? ["ControlledPushReceiptRequired"] : receiptIssues),
                isPushExecuted,
                receipt,
                preObservation,
                postObservation: null,
                issues: receiptIssues.Count == 0 ? ["ControlledPushReceiptRequired"] : receiptIssues,
                now);
        }

        var postObservation = await inspector.ObservePostPushAsync(request, receipt, cancellationToken).ConfigureAwait(false);
        var postIssues = new List<string>();
        ValidatePostObservation(request, receipt, postObservation, postIssues);
        if (postIssues.Count > 0)
        {
            return BuildResult(
                request,
                ControlledPushExecutionVerdict.Failed,
                ControlledPushFailureKind.PostStateInvalid,
                isPushExecuted: true,
                receipt,
                preObservation,
                postObservation,
                postIssues,
                now);
        }

        return BuildResult(
            request,
            ControlledPushExecutionVerdict.Completed,
            ControlledPushFailureKind.None,
            isPushExecuted: true,
            receipt,
            preObservation,
            postObservation,
            issues: [],
            now);
    }

    private static void ValidateRequestEnvelope(
        ControlledPushExecutionRequest request,
        ICollection<string> issues)
    {
        RequireText(request.ExecutionId, "ExecutionIdRequired", issues);
        ValidateSingleExplicitScope(request.Repository, "Repository", issues);
        ValidateSingleExplicitScope(request.Branch, "Branch", issues);
        ValidateSingleExplicitScope(request.RunId, "RunId", issues);
        if (string.IsNullOrWhiteSpace(request.PatchHash))
            issues.Add("PatchHashRequired");
        else if (!OperationEligibilityPatchHashRules.IsSafePatchHash(request.PatchHash))
            issues.Add("PatchHashInvalid");
        ValidateSingleExplicitScope(request.RemoteName, "RemoteName", issues);
        RequireSafeRemoteText(request.RemoteUrl, "RemoteUrl", issues);
        ValidateSingleExplicitScope(request.RemoteBranch, "RemoteBranch", issues);
        if (!Same(request.RemoteBranch, request.Branch))
            issues.Add("RemoteBranchMustMatchBranch");
        RequireText(request.ExpectedLocalCommitId, "ExpectedLocalCommitIdRequired", issues);
        RequireText(request.ExpectedRemoteHeadCommitId, "ExpectedRemoteHeadCommitIdRequired", issues);
        if (request.ObservedAtUtc == default)
            issues.Add("ObservedAtUtcRequired");
    }

    private static void ValidateCommitReceipt(ControlledPushExecutionRequest request, ICollection<string> issues)
    {
        var receipt = request.CommitReceipt;
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
        Match(receipt.CommitId, request.ExpectedLocalCommitId, "CommitReceiptCommitIdMismatch", issues);
        if (receipt.PushAttempted || receipt.PullRequestCreationAttempted || receipt.MergeAttempted ||
            receipt.ReleaseAttempted || receipt.DeploymentAttempted || receipt.MemoryWriteAttempted ||
            receipt.ContinuationAttempted)
        {
            issues.Add("CommitReceiptDownstreamAuthorityAttempted");
        }
    }

    private static void ValidatePushAuthority(ControlledPushExecutionRequest request, ICollection<string> issues)
    {
        var authority = request.PushAuthority;
        if (authority is null)
        {
            issues.Add("PushOperationAuthorityRequired");
            return;
        }

        if (string.IsNullOrWhiteSpace(authority.EvidenceRef) ||
            !(authority.EvidenceRef.StartsWith("push-operation-authority:", StringComparison.OrdinalIgnoreCase) ||
              authority.EvidenceRef.StartsWith("operation-eligibility-decision:", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("PushAuthorityEvidenceRefInvalid");
        }

        Match(authority.Repository, request.Repository, "PushAuthorityRepositoryMismatch", issues);
        Match(authority.Branch, request.Branch, "PushAuthorityBranchMismatch", issues);
        Match(authority.RunId, request.RunId, "PushAuthorityRunIdMismatch", issues);
        Match(authority.PatchHash, request.PatchHash, "PushAuthorityPatchHashMismatch", issues);
        Match(authority.RemoteName, request.RemoteName, "PushAuthorityRemoteNameMismatch", issues);
        Match(authority.RemoteUrl, request.RemoteUrl, "PushAuthorityRemoteUrlMismatch", issues);
        Match(authority.RemoteBranch, request.RemoteBranch, "PushAuthorityRemoteBranchMismatch", issues);
        Match(authority.CommitId, request.ExpectedLocalCommitId, "PushAuthorityCommitIdMismatch", issues);
        Match(authority.ExpectedRemoteHeadCommitId, request.ExpectedRemoteHeadCommitId, "PushAuthorityExpectedRemoteHeadMismatch", issues);
        if (authority.Decision is null ||
            authority.Decision.OperationKind != RunAuthorityOperationKind.Push ||
            !authority.Decision.IsEligibleUnderProfileAndGrant ||
            authority.Decision.BlockedReasons.Count > 0 ||
            authority.Decision.MissingEvidence.Count > 0)
        {
            issues.Add("PushOperationAuthorityRequired");
        }
    }

    private static void ValidatePreObservation(
        ControlledPushExecutionRequest request,
        PushRemoteStateObservation? observation,
        ICollection<string> issues)
    {
        if (observation is null)
        {
            issues.Add("PrePushObservationRequired");
            return;
        }

        if (!observation.IsRemoteReachable)
            issues.Add("PushRemoteObservationFailed");
        Match(observation.Repository, request.Repository, "PrePushObservationRepositoryMismatch", issues);
        Match(observation.Branch, request.Branch, "PrePushObservationBranchMismatch", issues);
        Match(observation.RemoteName, request.RemoteName, "PrePushObservationRemoteNameMismatch", issues);
        Match(observation.RemoteUrl, request.RemoteUrl, "PrePushObservationRemoteUrlMismatch", issues);
        Match(observation.RemoteBranch, request.RemoteBranch, "PrePushObservationRemoteBranchMismatch", issues);
        Match(observation.LocalHeadCommitId, request.ExpectedLocalCommitId, "PrePushLocalHeadCommitIdMismatch", issues);
        if (!Same(observation.RemoteHeadCommitId, request.ExpectedRemoteHeadCommitId))
            issues.Add("PushRemoteHeadStale");
        RequireCollection(observation.LocalUnpushedCommitIds, "LocalUnpushedCommitIdsRequired", issues);
        RequireCollection(observation.LocalUncommittedFilePaths, "LocalUncommittedFilePathsRequired", issues);
        if (observation.LocalUnpushedCommitIds is not null &&
            !SameSet(observation.LocalUnpushedCommitIds, [request.ExpectedLocalCommitId]))
        {
            issues.Add("UnexpectedLocalCommits");
        }
        if (HasValues(observation.LocalUncommittedFilePaths))
            issues.Add("LocalUncommittedFilesExist");
    }

    private static void ValidateReceipt(
        ControlledPushExecutionRequest request,
        ControlledPushReceipt? receipt,
        ICollection<string> issues)
    {
        if (receipt is null)
        {
            issues.Add("ControlledPushReceiptRequired");
            return;
        }

        if (string.IsNullOrWhiteSpace(receipt.ReceiptRef) ||
            !receipt.ReceiptRef.StartsWith("controlled-push-receipt:", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("ControlledPushReceiptRefInvalid");
        }

        Match(receipt.Repository, request.Repository, "PushReceiptRepositoryMismatch", issues);
        Match(receipt.Branch, request.Branch, "PushReceiptBranchMismatch", issues);
        Match(receipt.RunId, request.RunId, "PushReceiptRunIdMismatch", issues);
        Match(receipt.PatchHash, request.PatchHash, "PushReceiptPatchHashMismatch", issues);
        Match(receipt.RemoteName, request.RemoteName, "PushReceiptRemoteNameMismatch", issues);
        Match(receipt.RemoteUrl, request.RemoteUrl, "PushReceiptRemoteUrlMismatch", issues);
        Match(receipt.RemoteBranch, request.RemoteBranch, "PushReceiptRemoteBranchMismatch", issues);
        Match(receipt.PushedCommitId, request.ExpectedLocalCommitId, "PushReceiptCommitIdMismatch", issues);
        Match(receipt.PreviousRemoteHeadCommitId, request.ExpectedRemoteHeadCommitId, "PushReceiptPreviousRemoteHeadMismatch", issues);
        Match(receipt.NewRemoteHeadCommitId, request.ExpectedLocalCommitId, "PushReceiptNewRemoteHeadMismatch", issues);
        if (receipt.PushedAtUtc == default)
            issues.Add("PushReceiptPushedAtUtcRequired");
        if (receipt.ForcePushUsed)
            issues.Add("PushReceiptForcePushUsed");
        if (receipt.TagsPushed)
            issues.Add("PushReceiptTagsPushed");
        if (receipt.PullRequestCreationAttempted || receipt.MergeAttempted || receipt.ReleaseAttempted ||
            receipt.DeploymentAttempted || receipt.MemoryWriteAttempted || receipt.ContinuationAttempted)
        {
            issues.Add("PushReceiptDownstreamAuthorityAttempted");
        }
    }

    private static void ValidatePostObservation(
        ControlledPushExecutionRequest request,
        ControlledPushReceipt receipt,
        PushPostStateObservation? observation,
        ICollection<string> issues)
    {
        if (observation is null)
        {
            issues.Add("PostPushObservationRequired");
            return;
        }

        if (!observation.IsObservedAfterPush)
            issues.Add("PostPushObservationRequired");
        Match(observation.Repository, request.Repository, "PostPushObservationRepositoryMismatch", issues);
        Match(observation.Branch, request.Branch, "PostPushObservationBranchMismatch", issues);
        Match(observation.RemoteName, request.RemoteName, "PostPushObservationRemoteNameMismatch", issues);
        Match(observation.RemoteUrl, request.RemoteUrl, "PostPushObservationRemoteUrlMismatch", issues);
        Match(observation.RemoteBranch, request.RemoteBranch, "PostPushObservationRemoteBranchMismatch", issues);
        Match(observation.RemoteHeadCommitId, receipt.NewRemoteHeadCommitId, "PostPushRemoteHeadCommitIdMismatch", issues);
        RequireCollection(observation.RemainingUnpushedCommitIds, "PostPushRemainingUnpushedCommitIdsRequired", issues);
        if (HasValues(observation.RemainingUnpushedCommitIds))
            issues.Add("PostPushRemainingUnpushedCommits");
    }

    private static ControlledPushGatewayRequest BuildGatewayRequest(ControlledPushExecutionRequest request) =>
        new()
        {
            Repository = request.Repository.Trim(),
            Branch = request.Branch.Trim(),
            RemoteName = request.RemoteName.Trim(),
            RemoteUrl = request.RemoteUrl.Trim(),
            RemoteBranch = request.RemoteBranch.Trim(),
            ExpectedLocalCommitId = request.ExpectedLocalCommitId.Trim(),
            ExpectedRemoteHeadCommitId = request.ExpectedRemoteHeadCommitId.Trim(),
            ForcePushDisabled = true,
            TagsDisabled = true,
            RunId = request.RunId.Trim(),
            PatchHash = request.PatchHash.Trim()
        };

    private static ControlledPushExecutionResult BuildResult(
        ControlledPushExecutionRequest? request,
        ControlledPushExecutionVerdict verdict,
        ControlledPushFailureKind failureKind,
        bool isPushExecuted,
        ControlledPushReceipt? receipt,
        PushRemoteStateObservation? preObservation,
        PushPostStateObservation? postObservation,
        IReadOnlyCollection<string> issues,
        DateTimeOffset now)
    {
        var status = BuildStatus(request, verdict, receipt, issues, now);
        var validation = GovernedOperationStatusValidator.Validate(status);
        return new ControlledPushExecutionResult
        {
            IsPushExecuted = isPushExecuted,
            Verdict = verdict,
            FailureKind = failureKind,
            Receipt = receipt,
            PrePushObservation = preObservation,
            PostPushObservation = postObservation,
            OperationStatus = status,
            StatusValidation = validation,
            Issues = Clean(issues.Concat(validation.Issues).Concat(validation.RedFlags))
        };
    }

    private static GovernedOperationStatus BuildStatus(
        ControlledPushExecutionRequest? request,
        ControlledPushExecutionVerdict verdict,
        ControlledPushReceipt? receipt,
        IReadOnlyCollection<string> issues,
        DateTimeOffset now)
    {
        var state = verdict switch
        {
            ControlledPushExecutionVerdict.Completed => GovernedOperationState.Completed,
            ControlledPushExecutionVerdict.Failed => GovernedOperationState.Failed,
            _ => GovernedOperationState.Blocked
        };

        return new GovernedOperationStatus
        {
            OperationId = CleanText(request?.ExecutionId, "controlled-push-execution-blocked"),
            OperationKind = RunAuthorityOperationKind.Push.ToString(),
            Subject = request is null
                ? "controlled push execution"
                : $"controlled push for {CleanText(request.Repository, "unknown-repository")} {CleanText(request.Branch, "unknown-branch")} {CleanText(request.RemoteName, "unknown-remote")} {CleanText(request.ExpectedLocalCommitId, "unknown-commit")}",
            State = state,
            BlockedReasons = state == GovernedOperationState.Blocked ? Clean(issues) : [],
            MissingEvidence = state == GovernedOperationState.Blocked ? BuildMissingEvidence(issues) : [],
            NextSafeActions = state switch
            {
                GovernedOperationState.Completed => ["inspect controlled push receipt", "request PR authority separately if needed"],
                GovernedOperationState.Failed => ["inspect push execution failure", "request fresh push authority and remote observation before retry"],
                _ => ["collect corrected push execution evidence", "request controlled push execution only after preflight issues are fixed"]
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
            if (issue.Contains("Request", StringComparison.OrdinalIgnoreCase)) missing.Add("controlled-push-execution-request");
            if (issue.Contains("CommitReceipt", StringComparison.OrdinalIgnoreCase)) missing.Add("controlled-commit-receipt");
            if (issue.Contains("PushAuthority", StringComparison.OrdinalIgnoreCase) || issue.Contains("PushOperationAuthority", StringComparison.OrdinalIgnoreCase)) missing.Add("push-operation-authority");
            if (issue.Contains("Observation", StringComparison.OrdinalIgnoreCase) || issue.Contains("Remote", StringComparison.OrdinalIgnoreCase)) missing.Add("fresh-remote-state-observation");
        }

        return Clean(missing);
    }

    private static IReadOnlyList<string> BuildEvidenceRefs(ControlledPushExecutionRequest? request) =>
        Clean(
        [
            Ref("controlled-push-execution-request", request?.ExecutionId),
            Ref("repo", request?.Repository),
            Ref("branch", request?.Branch),
            Ref("run", request?.RunId),
            Ref("patch-hash", request?.PatchHash),
            request?.PushAuthority?.EvidenceRef,
            .. ValuesOrEmpty(request?.EvidenceRefs)
        ]);

    private static IReadOnlyList<string> BuildReceiptRefs(ControlledPushExecutionRequest? request) =>
        Clean(
        [
            request?.CommitReceipt?.ReceiptRef,
            .. ValuesOrEmpty(request?.ReceiptRefs)
        ]);

    private static ControlledPushFailureKind Classify(IEnumerable<string> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Contains("CommitReceipt", StringComparison.OrdinalIgnoreCase)) return ControlledPushFailureKind.CommitReceiptMismatch;
            if (issue.Contains("PushAuthority", StringComparison.OrdinalIgnoreCase) || issue.Contains("PushOperationAuthority", StringComparison.OrdinalIgnoreCase)) return ControlledPushFailureKind.PushAuthorityMismatch;
            if (issue.Contains("ObservationFailed", StringComparison.OrdinalIgnoreCase)) return ControlledPushFailureKind.RemoteObservationFailed;
            if (issue.Contains("PrePush", StringComparison.OrdinalIgnoreCase) || issue.Contains("Local", StringComparison.OrdinalIgnoreCase) || issue.Contains("Remote", StringComparison.OrdinalIgnoreCase)) return ControlledPushFailureKind.RemoteStateMismatch;
            if (issue.Contains("Receipt", StringComparison.OrdinalIgnoreCase)) return ControlledPushFailureKind.ReceiptInvalid;
            if (issue.Contains("PostPush", StringComparison.OrdinalIgnoreCase)) return ControlledPushFailureKind.PostStateInvalid;
            if (issue.Contains("Downstream", StringComparison.OrdinalIgnoreCase)) return ControlledPushFailureKind.BoundaryViolation;
        }

        return ControlledPushFailureKind.RequestInvalid;
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

    private static void RequireSafeRemoteText(string? value, string label, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{label}Required");
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains('\r', StringComparison.Ordinal) ||
            trimmed.Contains('\n', StringComparison.Ordinal) ||
            trimmed.Contains('\0', StringComparison.Ordinal) ||
            trimmed.Contains('*', StringComparison.Ordinal) ||
            trimmed.Contains('?', StringComparison.Ordinal) ||
            trimmed.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{label}Unsafe");
        }
    }

    private static void RequireText(string? value, string issue, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(issue);
    }

    private static void RequireCollection(
        IReadOnlyCollection<string>? values,
        string issue,
        ICollection<string> issues)
    {
        if (values is null)
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
