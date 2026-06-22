namespace IronDev.Core.Governance;

public enum FrontendControlledActionRequestKind
{
    Unknown = 0,
    SourceApply,
    Commit,
    Push,
    DraftPullRequest,
    Rollback
}

public sealed record ControlledActionRequestCreateRequest
{
    public required string RequestId { get; init; }
    public required string OperationId { get; init; }
    public required string RequestKind { get; init; }

    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public required string RunId { get; init; }

    public string? PatchPackageId { get; init; }
    public string? PatchHash { get; init; }
    public IReadOnlyCollection<string>? ProposedFilePaths { get; init; }
    public string? SourceApplyReceiptRef { get; init; }
    public string? CommitPackageId { get; init; }
    public string? CommitMessageEvidenceRef { get; init; }
    public string? CommitSha { get; init; }
    public string? RemoteTarget { get; init; }
    public string? PushIntent { get; init; }
    public string? HeadBranch { get; init; }
    public string? BaseBranch { get; init; }
    public string? PushedCommitSha { get; init; }
    public string? DraftPullRequestPackageId { get; init; }
    public string? PullRequestTextPackageRef { get; init; }
    public string? RollbackTargetReceiptRef { get; init; }
    public IReadOnlyCollection<string>? RollbackScopePaths { get; init; }

    public required string HumanIntent { get; init; }
    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }

    public required DateTimeOffset RequestedAtUtc { get; init; }
}

public sealed record ControlledActionRequestCreateResponse
{
    public required string RequestId { get; init; }
    public required string OperationId { get; init; }
    public required string RequestKind { get; init; }

    public required string State { get; init; }
    public required IReadOnlyCollection<string> BlockedReasons { get; init; }
    public required IReadOnlyCollection<string> MissingEvidence { get; init; }
    public required IReadOnlyCollection<string> NextSafeActions { get; init; }
    public required IReadOnlyCollection<string> ForbiddenActions { get; init; }
    public required IReadOnlyCollection<string> EvidenceRefs { get; init; }
    public required IReadOnlyCollection<string> ReceiptRefs { get; init; }
    public required IReadOnlyCollection<string> AuthorityWarnings { get; init; }

    public required FrontendActionRequestBoundary Boundary { get; init; }
    public required bool RequestCreated { get; init; }
    public required bool ExecutionStarted { get; init; }
    public required bool SourceMutated { get; init; }
    public required bool WorkflowContinued { get; init; }
}

public sealed record FrontendActionRequestBoundary
{
    public bool CanCreateRequest { get; init; } = true;

    public bool CanApprove { get; init; }
    public bool CanAcceptApproval { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanExecute { get; init; }
    public bool CanMutateSource { get; init; }
    public bool CanRollback { get; init; }
    public bool CanCommit { get; init; }
    public bool CanPush { get; init; }
    public bool CanCreatePullRequest { get; init; }
    public bool CanMarkReadyForReview { get; init; }
    public bool CanMerge { get; init; }
    public bool CanRelease { get; init; }
    public bool CanDeploy { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanContinueWorkflow { get; init; }

    public static FrontendActionRequestBoundary RequestOnly { get; } = new();
}

public interface IFrontendControlledActionRequestService
{
    ControlledActionRequestCreateResponse Create(ControlledActionRequestCreateRequest request);
}

public sealed class FrontendControlledActionRequestService : IFrontendControlledActionRequestService
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private static readonly string[] ForbiddenActions =
    [
        "do not treat request creation as approval",
        "do not treat request creation as policy satisfaction",
        "do not execute source apply from this request",
        "do not execute rollback from this request",
        "do not commit from this request",
        "do not push from this request",
        "do not create or update PRs from this request",
        "do not mark ready for review from this request",
        "do not merge, release, or deploy from this request",
        "do not promote memory from this request",
        "do not continue workflow from this request"
    ];

    private static readonly string[] AuthorityWarnings =
    [
        "UI may request authority. It cannot be authority.",
        "A request is not approval.",
        "A request is not policy satisfaction.",
        "A request is not execution.",
        "A request is not source mutation.",
        "Backend eligibility decides.",
        "Evidence refs are not approval.",
        "Receipt refs are not authority.",
        "Memory is not authority.",
        "UI text is not authority.",
        "Workflow continuation is forbidden in this PR."
    ];

    private static readonly string[] UnsupportedMarkers =
    [
        "readyforreview",
        "ready-for-review",
        "ready for review",
        "merge",
        "release",
        "deploy",
        "deployment",
        "memorypromotion",
        "memory promotion",
        "workflowcontinuation",
        "workflow continuation",
        "shell",
        "shell command",
        "provider",
        "provider operation",
        "git push",
        "gh pr",
        "continue anyway",
        "arbitrary executor",
        "executor"
    ];

    public ControlledActionRequestCreateResponse Create(ControlledActionRequestCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<string>();
        RequireText(request.RequestId, "ControlledActionRequestIdRequired", issues);
        RequireText(request.OperationId, "ControlledActionOperationIdRequired", issues);
        RequireText(request.Repository, "ControlledActionRepositoryRequired", issues);
        RequireText(request.Branch, "ControlledActionBranchRequired", issues);
        RequireText(request.RunId, "ControlledActionRunIdRequired", issues);
        RequireText(request.HumanIntent, "ControlledActionHumanIntentRequired", issues);

        if (!TryParseKind(request.RequestKind, out var kind))
        {
            issues.Add("UnsupportedControlledActionRequestKind");
            return Response(request, "Rejected", requestCreated: false, issues, MissingForRejected(request.RequestKind), NextActionForRejected(request.RequestKind));
        }

        AddKindSpecificIssues(kind, request, issues);

        var state = issues.Count == 0 ? "EligibleForBackendReview" : "Blocked";
        IReadOnlyCollection<string> nextActions = issues.Count == 0
            ? ["request record created; backend eligibility still decides; no execution started"]
            : [$"complete missing {kind} request evidence before backend eligibility review"];

        return Response(
            request,
            state,
            requestCreated: true,
            blockedReasons: issues,
            missingEvidence: issues,
            nextSafeActions: nextActions,
            normalizedKind: kind.ToString());
    }

    private static void AddKindSpecificIssues(
        FrontendControlledActionRequestKind kind,
        ControlledActionRequestCreateRequest request,
        List<string> issues)
    {
        switch (kind)
        {
            case FrontendControlledActionRequestKind.SourceApply:
                RequireText(request.PatchPackageId, "SourceApplyRequestPatchPackageIdRequired", issues);
                RequireText(request.PatchHash, "SourceApplyRequestPatchHashRequired", issues);
                RequireAny(request.ProposedFilePaths, "SourceApplyRequestProposedFileScopeRequired", issues);
                break;
            case FrontendControlledActionRequestKind.Commit:
                RequireText(request.SourceApplyReceiptRef, "CommitRequestSourceApplyReceiptRequired", issues);
                if (string.IsNullOrWhiteSpace(request.CommitPackageId) && !HasValues(request.ProposedFilePaths))
                    issues.Add("CommitRequestExpectedChangedFilesOrPackageRequired");
                break;
            case FrontendControlledActionRequestKind.Push:
                RequireText(request.CommitSha, "PushRequestCommitShaRequired", issues);
                RequireText(request.RemoteTarget, "PushRequestRemoteTargetRequired", issues);
                RequireText(request.PushIntent, "PushRequestIntentRequired", issues);
                break;
            case FrontendControlledActionRequestKind.DraftPullRequest:
                RequireText(request.HeadBranch, "DraftPrRequestHeadBranchRequired", issues);
                RequireText(request.BaseBranch, "DraftPrRequestBaseBranchRequired", issues);
                RequireText(request.PushedCommitSha, "DraftPrRequestPushedCommitShaRequired", issues);
                RequireText(request.PullRequestTextPackageRef, "DraftPrRequestTextPackageRefRequired", issues);
                break;
            case FrontendControlledActionRequestKind.Rollback:
                RequireText(request.RollbackTargetReceiptRef, "RollbackRequestTargetReceiptRequired", issues);
                RequireText(request.SourceApplyReceiptRef, "RollbackRequestSourceApplyReceiptRequired", issues);
                RequireAny(request.RollbackScopePaths, "RollbackRequestScopeRequired", issues);
                break;
            case FrontendControlledActionRequestKind.Unknown:
            default:
                issues.Add("UnsupportedControlledActionRequestKind");
                break;
        }
    }

    private static ControlledActionRequestCreateResponse Response(
        ControlledActionRequestCreateRequest request,
        string state,
        bool requestCreated,
        IReadOnlyCollection<string> blockedReasons,
        IReadOnlyCollection<string> missingEvidence,
        IReadOnlyCollection<string> nextSafeActions,
        string? normalizedKind = null) =>
        new()
        {
            RequestId = CleanText(request.RequestId),
            OperationId = CleanText(request.OperationId),
            RequestKind = CleanText(normalizedKind ?? request.RequestKind),
            State = state,
            BlockedReasons = Clean(blockedReasons),
            MissingEvidence = Clean(missingEvidence),
            NextSafeActions = Clean(nextSafeActions),
            ForbiddenActions = Clean(ForbiddenActions),
            EvidenceRefs = Clean(request.EvidenceRefs),
            ReceiptRefs = Clean(request.ReceiptRefs),
            AuthorityWarnings = Clean(AuthorityWarnings),
            Boundary = FrontendActionRequestBoundary.RequestOnly,
            RequestCreated = requestCreated,
            ExecutionStarted = false,
            SourceMutated = false,
            WorkflowContinued = false
        };

    private static IReadOnlyCollection<string> MissingForRejected(string requestKind) =>
    [
        $"unsupported-request-kind:{CleanText(requestKind)}",
        "supported-request-kind:SourceApply|Commit|Push|DraftPullRequest|Rollback"
    ];

    private static IReadOnlyCollection<string> NextActionForRejected(string requestKind) =>
    [
        $"submit one supported request kind instead of {CleanText(requestKind)}"
    ];

    private static bool TryParseKind(string? value, out FrontendControlledActionRequestKind kind)
    {
        kind = FrontendControlledActionRequestKind.Unknown;
        var normalized = CleanText(value).Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (UnsupportedMarkers.Any(marker => CleanText(value).Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return false;

        return Enum.TryParse(normalized, ignoreCase: true, out kind) && kind is not FrontendControlledActionRequestKind.Unknown;
    }

    private static void RequireText(string? value, string issue, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(issue);
    }

    private static void RequireAny(IReadOnlyCollection<string>? values, string issue, List<string> issues)
    {
        if (!HasValues(values))
            issues.Add(issue);
    }

    private static bool HasValues(IReadOnlyCollection<string>? values) =>
        values?.Any(value => !string.IsNullOrWhiteSpace(value)) == true;

    private static string CleanText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static IReadOnlyList<string> Clean(IEnumerable<string?>? values) =>
        (values ?? [])
            .Select(CleanText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(Comparer)
            .ToArray();
}
