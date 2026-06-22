using IronDev.Core.Governance.PushExecution;

namespace IronDev.Core.Governance.PullRequestExecution;

public static class ControlledDraftPullRequestExecutor
{
    private static readonly string[] ReceiptForbiddenActions =
    [
        "do not mark ready for review from draft PR receipt",
        "do not request reviewers from draft PR receipt",
        "do not merge from draft PR receipt",
        "do not release from draft PR receipt",
        "do not deploy from draft PR receipt",
        "do not continue workflow from draft PR receipt",
        "do not promote memory from draft PR receipt",
        "do not use PR URL as release candidate ref",
        "draft PR receipt does not satisfy policy",
        "draft PR receipt does not approve the next mutation"
    ];

    private static readonly string[] BlockedForbiddenActions =
    [
        "do not create PR from blocked draft PR execution",
        "do not mark ready for review from blocked draft PR execution",
        "do not request reviewers from blocked draft PR execution",
        "do not merge from blocked draft PR execution",
        "do not release from blocked draft PR execution",
        "do not deploy from blocked draft PR execution",
        "do not continue workflow from blocked draft PR execution",
        "do not promote memory from blocked draft PR execution"
    ];

    public static async Task<ControlledDraftPullRequestExecutionResult> ExecuteAsync(
        ControlledDraftPullRequestExecutionRequest? request,
        IDraftPullRequestInspector inspector,
        IControlledDraftPullRequestGateway gateway,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (request is null)
        {
            return BuildResult(
                null,
                ControlledDraftPullRequestExecutionVerdict.Blocked,
                ControlledDraftPullRequestFailureKind.MissingRequest,
                isMutated: false,
                receipt: null,
                preObservation: null,
                postObservation: null,
                issues: ["ControlledDraftPullRequestExecutionRequestRequired"],
                now);
        }

        now = request.ObservedAtUtc == default ? now : request.ObservedAtUtc;
        if (inspector is null)
        {
            return BuildResult(
                request,
                ControlledDraftPullRequestExecutionVerdict.Blocked,
                ControlledDraftPullRequestFailureKind.RemoteObservationFailed,
                isMutated: false,
                receipt: null,
                preObservation: null,
                postObservation: null,
                issues: ["DraftPullRequestInspectorRequired"],
                now);
        }

        if (gateway is null)
        {
            return BuildResult(
                request,
                ControlledDraftPullRequestExecutionVerdict.Blocked,
                ControlledDraftPullRequestFailureKind.GatewayFailed,
                isMutated: false,
                receipt: null,
                preObservation: null,
                postObservation: null,
                issues: ["ControlledDraftPullRequestGatewayRequired"],
                now);
        }

        var preflightIssues = new List<string>();
        ValidateRequestEnvelope(request, preflightIssues);
        ValidatePushReceipt(request, preflightIssues);
        ValidateDraftPullRequestAuthority(request, preflightIssues);
        ValidateTextPackage(request, preflightIssues);

        if (preflightIssues.Count > 0)
        {
            return BuildResult(
                request,
                ControlledDraftPullRequestExecutionVerdict.Blocked,
                Classify(preflightIssues),
                isMutated: false,
                receipt: null,
                preObservation: null,
                postObservation: null,
                preflightIssues,
                now);
        }

        var preObservation = await inspector.ObservePreMutationAsync(request, cancellationToken).ConfigureAwait(false);
        var preObservationIssues = new List<string>();
        ValidatePreObservation(request, preObservation, preObservationIssues);
        if (preObservationIssues.Count > 0)
        {
            return BuildResult(
                request,
                ControlledDraftPullRequestExecutionVerdict.Blocked,
                Classify(preObservationIssues),
                isMutated: false,
                receipt: null,
                preObservation,
                postObservation: null,
                preObservationIssues,
                now);
        }

        ControlledDraftPullRequestReceipt? receipt;
        try
        {
            receipt = await gateway.CreateOrUpdateDraftPullRequestAsync(BuildGatewayRequest(request), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return BuildResult(
                request,
                ControlledDraftPullRequestExecutionVerdict.Failed,
                ControlledDraftPullRequestFailureKind.GatewayFailed,
                isMutated: false,
                receipt: null,
                preObservation,
                postObservation: null,
                issues: ["ControlledDraftPullRequestGatewayFailed", ex.GetType().Name],
                now);
        }

        var receiptIssues = new List<string>();
        ValidateReceipt(request, receipt, receiptIssues);
        var isMutated = receipt is not null && receipt.PullRequestNumber > 0;
        if (receiptIssues.Count > 0 || receipt is null)
        {
            return BuildResult(
                request,
                ControlledDraftPullRequestExecutionVerdict.Failed,
                Classify(receiptIssues.Count == 0 ? ["ControlledDraftPullRequestReceiptRequired"] : receiptIssues),
                isMutated,
                receipt,
                preObservation,
                postObservation: null,
                issues: receiptIssues.Count == 0 ? ["ControlledDraftPullRequestReceiptRequired"] : receiptIssues,
                now);
        }

        var postObservation = await inspector.ObservePostMutationAsync(request, receipt, cancellationToken).ConfigureAwait(false);
        var postIssues = new List<string>();
        ValidatePostObservation(request, receipt, postObservation, postIssues);
        if (postIssues.Count > 0)
        {
            return BuildResult(
                request,
                ControlledDraftPullRequestExecutionVerdict.Failed,
                ControlledDraftPullRequestFailureKind.PostStateInvalid,
                isMutated: true,
                receipt,
                preObservation,
                postObservation,
                postIssues,
                now);
        }

        return BuildResult(
            request,
            ControlledDraftPullRequestExecutionVerdict.Completed,
            ControlledDraftPullRequestFailureKind.None,
            isMutated: true,
            receipt,
            preObservation,
            postObservation,
            issues: [],
            now);
    }

    private static void ValidateRequestEnvelope(
        ControlledDraftPullRequestExecutionRequest request,
        ICollection<string> issues)
    {
        RequireText(request.ExecutionId, "ExecutionIdRequired", issues);
        ValidateSingleExplicitScope(request.Repository, "Repository", issues);
        ValidateSingleExplicitScope(request.HeadBranch, "HeadBranch", issues);
        ValidateSingleExplicitScope(request.BaseBranch, "BaseBranch", issues);
        ValidateSingleExplicitScope(request.RunId, "RunId", issues);
        if (string.IsNullOrWhiteSpace(request.PatchHash))
            issues.Add("PatchHashRequired");
        else if (!OperationEligibilityPatchHashRules.IsSafePatchHash(request.PatchHash))
            issues.Add("PatchHashInvalid");
        RequireText(request.HeadCommitId, "HeadCommitIdRequired", issues);
        if (request.ExistingPullRequestNumber is <= 0)
            issues.Add("ExistingPullRequestNumberInvalid");
        if (request.ObservedAtUtc == default)
            issues.Add("ObservedAtUtcRequired");
    }

    private static void ValidatePushReceipt(
        ControlledDraftPullRequestExecutionRequest request,
        ICollection<string> issues)
    {
        var receipt = request.PushReceipt;
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
        Match(receipt.Branch, request.HeadBranch, "PushReceiptHeadBranchMismatch", issues);
        Match(receipt.RunId, request.RunId, "PushReceiptRunIdMismatch", issues);
        Match(receipt.PatchHash, request.PatchHash, "PushReceiptPatchHashMismatch", issues);
        Match(receipt.PushedCommitId, request.HeadCommitId, "PushReceiptHeadCommitIdMismatch", issues);
        Match(receipt.NewRemoteHeadCommitId, request.HeadCommitId, "PushReceiptRemoteHeadMismatch", issues);
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

    private static void ValidateDraftPullRequestAuthority(
        ControlledDraftPullRequestExecutionRequest request,
        ICollection<string> issues)
    {
        var authority = request.DraftPullRequestAuthority;
        if (authority is null)
        {
            issues.Add("DraftPullRequestAuthorityRequired");
            return;
        }

        if (string.IsNullOrWhiteSpace(authority.EvidenceRef) ||
            !(authority.EvidenceRef.StartsWith("draft-pull-request-authority:", StringComparison.OrdinalIgnoreCase) ||
              authority.EvidenceRef.StartsWith("operation-eligibility-decision:", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("DraftPullRequestAuthorityEvidenceRefInvalid");
        }

        Match(authority.Repository, request.Repository, "DraftPullRequestAuthorityRepositoryMismatch", issues);
        Match(authority.HeadBranch, request.HeadBranch, "DraftPullRequestAuthorityHeadBranchMismatch", issues);
        Match(authority.BaseBranch, request.BaseBranch, "DraftPullRequestAuthorityBaseBranchMismatch", issues);
        Match(authority.RunId, request.RunId, "DraftPullRequestAuthorityRunIdMismatch", issues);
        Match(authority.PatchHash, request.PatchHash, "DraftPullRequestAuthorityPatchHashMismatch", issues);
        Match(authority.HeadCommitId, request.HeadCommitId, "DraftPullRequestAuthorityHeadCommitIdMismatch", issues);
        if (authority.Decision is null ||
            authority.Decision.OperationKind != RunAuthorityOperationKind.DraftPullRequest ||
            !authority.Decision.IsEligibleUnderProfileAndGrant ||
            authority.Decision.BlockedReasons.Count > 0 ||
            authority.Decision.MissingEvidence.Count > 0)
        {
            issues.Add("DraftPullRequestAuthorityRequired");
        }
    }

    private static void ValidateTextPackage(
        ControlledDraftPullRequestExecutionRequest request,
        ICollection<string> issues)
    {
        var text = request.TextPackage;
        if (text is null)
        {
            issues.Add("DraftPullRequestTextPackageRequired");
            return;
        }

        RequireText(text.TextPackageId, "DraftPullRequestTextPackageIdRequired", issues);
        Match(text.Repository, request.Repository, "DraftPullRequestTextPackageRepositoryMismatch", issues);
        Match(text.HeadBranch, request.HeadBranch, "DraftPullRequestTextPackageHeadBranchMismatch", issues);
        Match(text.BaseBranch, request.BaseBranch, "DraftPullRequestTextPackageBaseBranchMismatch", issues);
        Match(text.RunId, request.RunId, "DraftPullRequestTextPackageRunIdMismatch", issues);
        Match(text.PatchHash, request.PatchHash, "DraftPullRequestTextPackagePatchHashMismatch", issues);
        Match(text.HeadCommitId, request.HeadCommitId, "DraftPullRequestTextPackageHeadCommitIdMismatch", issues);
        RequireSafeText(text.Title, "DraftPullRequestTitle", issues, allowMultiline: false);
        RequireSafeText(text.Body, "DraftPullRequestBody", issues, allowMultiline: true);
        if (!Same(text.TextSource, "HumanProvided") && !Same(text.TextSource, "ReviewedProposal"))
            issues.Add("DraftPullRequestTextSourceNotAllowed");
    }

    private static void ValidatePreObservation(
        ControlledDraftPullRequestExecutionRequest request,
        DraftPullRequestRemoteStateObservation? observation,
        ICollection<string> issues)
    {
        if (observation is null)
        {
            issues.Add("DraftPullRequestPreObservationRequired");
            return;
        }

        if (!observation.IsRepositoryReachable)
            issues.Add("DraftPullRequestRepositoryUnreachable");
        if (!observation.HeadBranchExists)
            issues.Add("DraftPullRequestHeadBranchMissing");
        if (!observation.BaseBranchExists)
            issues.Add("DraftPullRequestBaseBranchMissing");
        Match(observation.Repository, request.Repository, "DraftPullRequestObservationRepositoryMismatch", issues);
        Match(observation.HeadBranch, request.HeadBranch, "DraftPullRequestObservationHeadBranchMismatch", issues);
        Match(observation.BaseBranch, request.BaseBranch, "DraftPullRequestObservationBaseBranchMismatch", issues);
        Match(observation.HeadCommitId, request.HeadCommitId, "DraftPullRequestObservationHeadCommitIdMismatch", issues);

        if (request.ExistingPullRequestNumber.HasValue)
        {
            if (observation.ExistingPullRequestNumber != request.ExistingPullRequestNumber)
                issues.Add("ExistingPullRequestNumberMismatch");
            if (observation.ExistingPullRequestIsDraft != true)
                issues.Add("ExistingPullRequestNotDraft");
        }
        else if (observation.ExistingPullRequestNumber.HasValue)
        {
            issues.Add("ExistingPullRequestNumberRequiredForUpdate");
        }

        if (observation.ExistingPullRequestIsDraft == false)
            issues.Add("ExistingPullRequestNotDraft");
    }

    private static void ValidateReceipt(
        ControlledDraftPullRequestExecutionRequest request,
        ControlledDraftPullRequestReceipt? receipt,
        ICollection<string> issues)
    {
        if (receipt is null)
        {
            issues.Add("ControlledDraftPullRequestReceiptRequired");
            return;
        }

        if (string.IsNullOrWhiteSpace(receipt.ReceiptRef) ||
            !receipt.ReceiptRef.StartsWith("controlled-draft-pr-receipt:", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("ControlledDraftPullRequestReceiptRefInvalid");
        }

        Match(receipt.Repository, request.Repository, "DraftPullRequestReceiptRepositoryMismatch", issues);
        Match(receipt.HeadBranch, request.HeadBranch, "DraftPullRequestReceiptHeadBranchMismatch", issues);
        Match(receipt.BaseBranch, request.BaseBranch, "DraftPullRequestReceiptBaseBranchMismatch", issues);
        Match(receipt.RunId, request.RunId, "DraftPullRequestReceiptRunIdMismatch", issues);
        Match(receipt.PatchHash, request.PatchHash, "DraftPullRequestReceiptPatchHashMismatch", issues);
        Match(receipt.HeadCommitId, request.HeadCommitId, "DraftPullRequestReceiptHeadCommitIdMismatch", issues);
        if (receipt.PullRequestNumber <= 0)
            issues.Add("DraftPullRequestReceiptNumberInvalid");
        if (request.ExistingPullRequestNumber.HasValue && receipt.PullRequestNumber != request.ExistingPullRequestNumber)
            issues.Add("DraftPullRequestReceiptExistingNumberMismatch");
        if (!IsSafeUrl(receipt.PullRequestUrl))
            issues.Add("DraftPullRequestReceiptUrlInvalid");
        if (!receipt.IsDraft)
            issues.Add("DraftPullRequestReceiptNotDraft");
        if (receipt.WasCreated == receipt.WasUpdated)
            issues.Add("DraftPullRequestReceiptMutationModeInvalid");
        if (request.ExistingPullRequestNumber.HasValue && !receipt.WasUpdated)
            issues.Add("DraftPullRequestReceiptMutationModeMismatch");
        if (!request.ExistingPullRequestNumber.HasValue && !receipt.WasCreated)
            issues.Add("DraftPullRequestReceiptMutationModeMismatch");
        if (receipt.CreatedOrUpdatedAtUtc == default)
            issues.Add("DraftPullRequestReceiptTimestampRequired");
        if (receipt.ReadyForReviewAttempted || receipt.ReviewerRequestAttempted || receipt.MergeAttempted ||
            receipt.ReleaseAttempted || receipt.DeploymentAttempted || receipt.MemoryWriteAttempted ||
            receipt.ContinuationAttempted)
        {
            issues.Add("DraftPullRequestReceiptDownstreamAuthorityAttempted");
        }
    }

    private static void ValidatePostObservation(
        ControlledDraftPullRequestExecutionRequest request,
        ControlledDraftPullRequestReceipt receipt,
        DraftPullRequestPostStateObservation? observation,
        ICollection<string> issues)
    {
        if (observation is null)
        {
            issues.Add("DraftPullRequestPostObservationRequired");
            return;
        }

        if (!observation.IsObservedAfterMutation)
            issues.Add("DraftPullRequestPostObservationRequired");
        Match(observation.Repository, request.Repository, "DraftPullRequestPostObservationRepositoryMismatch", issues);
        Match(observation.HeadBranch, request.HeadBranch, "DraftPullRequestPostObservationHeadBranchMismatch", issues);
        Match(observation.BaseBranch, request.BaseBranch, "DraftPullRequestPostObservationBaseBranchMismatch", issues);
        Match(observation.HeadCommitId, request.HeadCommitId, "DraftPullRequestPostObservationHeadCommitIdMismatch", issues);
        if (observation.PullRequestNumber != receipt.PullRequestNumber)
            issues.Add("DraftPullRequestPostObservationNumberMismatch");
        if (!Same(observation.PullRequestUrl, receipt.PullRequestUrl))
            issues.Add("DraftPullRequestPostObservationUrlMismatch");
        if (!observation.IsDraft)
            issues.Add("DraftPullRequestPostObservationNotDraft");
    }

    private static ControlledDraftPullRequestGatewayRequest BuildGatewayRequest(
        ControlledDraftPullRequestExecutionRequest request) =>
        new()
        {
            Repository = request.Repository.Trim(),
            HeadBranch = request.HeadBranch.Trim(),
            BaseBranch = request.BaseBranch.Trim(),
            HeadCommitId = request.HeadCommitId.Trim(),
            Title = request.TextPackage!.Title.Trim(),
            Body = request.TextPackage.Body.Trim(),
            ExistingPullRequestNumber = request.ExistingPullRequestNumber,
            DraftOnly = true,
            ReadyForReviewDisabled = true,
            ReviewerRequestsDisabled = true,
            MergeDisabled = true,
            RunId = request.RunId.Trim(),
            PatchHash = request.PatchHash.Trim()
        };

    private static ControlledDraftPullRequestExecutionResult BuildResult(
        ControlledDraftPullRequestExecutionRequest? request,
        ControlledDraftPullRequestExecutionVerdict verdict,
        ControlledDraftPullRequestFailureKind failureKind,
        bool isMutated,
        ControlledDraftPullRequestReceipt? receipt,
        DraftPullRequestRemoteStateObservation? preObservation,
        DraftPullRequestPostStateObservation? postObservation,
        IReadOnlyCollection<string> issues,
        DateTimeOffset now)
    {
        var status = BuildStatus(request, verdict, receipt, issues, now);
        var validation = GovernedOperationStatusValidator.Validate(status);
        return new ControlledDraftPullRequestExecutionResult
        {
            IsDraftPullRequestMutated = isMutated,
            Verdict = verdict,
            FailureKind = failureKind,
            Receipt = receipt,
            PreMutationObservation = preObservation,
            PostMutationObservation = postObservation,
            OperationStatus = status,
            StatusValidation = validation,
            Issues = Clean(issues.Concat(validation.Issues).Concat(validation.RedFlags))
        };
    }

    private static GovernedOperationStatus BuildStatus(
        ControlledDraftPullRequestExecutionRequest? request,
        ControlledDraftPullRequestExecutionVerdict verdict,
        ControlledDraftPullRequestReceipt? receipt,
        IReadOnlyCollection<string> issues,
        DateTimeOffset now)
    {
        var state = verdict switch
        {
            ControlledDraftPullRequestExecutionVerdict.Completed => GovernedOperationState.Completed,
            ControlledDraftPullRequestExecutionVerdict.Failed => GovernedOperationState.Failed,
            _ => GovernedOperationState.Blocked
        };

        return new GovernedOperationStatus
        {
            OperationId = CleanText(request?.ExecutionId, "controlled-draft-pr-execution-blocked"),
            OperationKind = RunAuthorityOperationKind.DraftPullRequest.ToString(),
            Subject = request is null
                ? "controlled draft PR execution"
                : $"controlled draft PR for {CleanText(request.Repository, "unknown-repository")} {CleanText(request.HeadBranch, "unknown-head")} {CleanText(request.BaseBranch, "unknown-base")} {CleanText(request.HeadCommitId, "unknown-head-commit")}",
            State = state,
            BlockedReasons = state == GovernedOperationState.Blocked ? Clean(issues) : [],
            MissingEvidence = state == GovernedOperationState.Blocked ? BuildMissingEvidence(issues) : [],
            NextSafeActions = state switch
            {
                GovernedOperationState.Completed => ["inspect controlled draft PR receipt", "request ready-for-review authority separately if needed"],
                GovernedOperationState.Failed => ["inspect draft PR execution failure", "request fresh draft PR authority and PR observation before retry"],
                _ => ["collect corrected draft PR execution evidence", "request controlled draft PR execution only after preflight issues are fixed"]
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
            if (issue.Contains("Request", StringComparison.OrdinalIgnoreCase)) missing.Add("controlled-draft-pr-execution-request");
            if (issue.Contains("PushReceipt", StringComparison.OrdinalIgnoreCase)) missing.Add("controlled-push-receipt");
            if (issue.Contains("Authority", StringComparison.OrdinalIgnoreCase)) missing.Add("draft-pull-request-authority");
            if (issue.Contains("TextPackage", StringComparison.OrdinalIgnoreCase) || issue.Contains("Title", StringComparison.OrdinalIgnoreCase) || issue.Contains("Body", StringComparison.OrdinalIgnoreCase)) missing.Add("draft-pull-request-text-package");
            if (issue.Contains("Observation", StringComparison.OrdinalIgnoreCase) || issue.Contains("Branch", StringComparison.OrdinalIgnoreCase)) missing.Add("fresh-draft-pr-remote-observation");
        }

        return Clean(missing);
    }

    private static IReadOnlyList<string> BuildEvidenceRefs(ControlledDraftPullRequestExecutionRequest? request) =>
        Clean(
        [
            Ref("controlled-draft-pr-execution-request", request?.ExecutionId),
            Ref("repo", request?.Repository),
            Ref("head-branch", request?.HeadBranch),
            Ref("base-branch", request?.BaseBranch),
            Ref("run", request?.RunId),
            Ref("patch-hash", request?.PatchHash),
            request?.DraftPullRequestAuthority?.EvidenceRef,
            Ref("draft-pull-request-text-package", request?.TextPackage?.TextPackageId),
            .. ValuesOrEmpty(request?.EvidenceRefs)
        ]);

    private static IReadOnlyList<string> BuildReceiptRefs(ControlledDraftPullRequestExecutionRequest? request) =>
        Clean(
        [
            request?.PushReceipt?.ReceiptRef,
            .. ValuesOrEmpty(request?.ReceiptRefs)
        ]);

    private static ControlledDraftPullRequestFailureKind Classify(IEnumerable<string> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Contains("PushReceipt", StringComparison.OrdinalIgnoreCase)) return ControlledDraftPullRequestFailureKind.PushReceiptMismatch;
            if (issue.Contains("Authority", StringComparison.OrdinalIgnoreCase)) return ControlledDraftPullRequestFailureKind.DraftPullRequestAuthorityMismatch;
            if (issue.Contains("TextPackage", StringComparison.OrdinalIgnoreCase) || issue.Contains("Title", StringComparison.OrdinalIgnoreCase) || issue.Contains("Body", StringComparison.OrdinalIgnoreCase)) return ControlledDraftPullRequestFailureKind.TextPackageInvalid;
            if (issue.Contains("Unreachable", StringComparison.OrdinalIgnoreCase)) return ControlledDraftPullRequestFailureKind.RemoteObservationFailed;
            if (issue.Contains("Observation", StringComparison.OrdinalIgnoreCase) || issue.Contains("Branch", StringComparison.OrdinalIgnoreCase) || issue.Contains("Repository", StringComparison.OrdinalIgnoreCase)) return ControlledDraftPullRequestFailureKind.RemoteStateMismatch;
            if (issue.Contains("Receipt", StringComparison.OrdinalIgnoreCase)) return ControlledDraftPullRequestFailureKind.ReceiptInvalid;
            if (issue.Contains("Post", StringComparison.OrdinalIgnoreCase)) return ControlledDraftPullRequestFailureKind.PostStateInvalid;
            if (issue.Contains("Downstream", StringComparison.OrdinalIgnoreCase)) return ControlledDraftPullRequestFailureKind.BoundaryViolation;
        }

        return ControlledDraftPullRequestFailureKind.RequestInvalid;
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

    private static void RequireSafeText(
        string? value,
        string label,
        ICollection<string> issues,
        bool allowMultiline)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{label}Required");
            return;
        }

        if (value.Contains('\0', StringComparison.Ordinal) ||
            (!allowMultiline && (value.Contains('\r', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal))))
        {
            issues.Add($"{label}Unsafe");
        }
    }

    private static bool IsSafeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (value.Contains('\r', StringComparison.Ordinal) ||
            value.Contains('\n', StringComparison.Ordinal) ||
            value.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
               (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase));
    }

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
