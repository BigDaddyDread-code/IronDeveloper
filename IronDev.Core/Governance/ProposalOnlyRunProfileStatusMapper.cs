namespace IronDev.Core.Governance;

public static class ProposalOnlyRunProfileStatusMapper
{
    public static GovernedOperationStatus MapAllowed(ProposalOnlyRunProfileEvaluationRequest request) =>
        new()
        {
            OperationId = request.OperationId,
            OperationKind = request.OperationKind,
            Subject = request.Subject,
            State = GovernedOperationState.Eligible,
            BlockedReasons = [],
            MissingEvidence = [],
            NextSafeActions = BuildAllowedNextSafeActions(request.OperationKind),
            ForbiddenActions = BuildForbiddenActions(),
            EvidenceRefs = BuildEvidenceRefs(request),
            ReceiptRefs = [],
            ExpiresAtUtc = request.ExpiresAtUtc,
            ObservedAtUtc = request.ObservedAtUtc
        };

    public static GovernedOperationStatus MapBlocked(
        ProposalOnlyRunProfileEvaluationRequest request,
        string blockedReason,
        string missingAuthority) =>
        new()
        {
            OperationId = request.OperationId,
            OperationKind = request.OperationKind,
            Subject = request.Subject,
            State = GovernedOperationState.Blocked,
            BlockedReasons = [blockedReason],
            MissingEvidence = [missingAuthority],
            NextSafeActions = [$"request explicit governed authority for {DisplayOperationKind(request.OperationKind)} outside ProposalOnly mode"],
            ForbiddenActions = BuildForbiddenActions(),
            EvidenceRefs = BuildEvidenceRefs(request),
            ReceiptRefs = [],
            ExpiresAtUtc = request.ExpiresAtUtc,
            ObservedAtUtc = request.ObservedAtUtc
        };

    private static IReadOnlyList<string> BuildAllowedNextSafeActions(string operationKind)
    {
        var action = operationKind switch
        {
            ProposalOnlyOperationKinds.RepoInspect => "inspect repository state for proposal context",
            ProposalOnlyOperationKinds.TaskInterpretation => "prepare task interpretation evidence",
            ProposalOnlyOperationKinds.DisposableWorkspaceCreate => "prepare disposable workspace request evidence",
            ProposalOnlyOperationKinds.DisposableWorkspaceModify => "prepare disposable workspace changes only",
            ProposalOnlyOperationKinds.DisposableWorkspaceValidate => "prepare disposable workspace validation evidence",
            ProposalOnlyOperationKinds.PatchProposal => "prepare patch proposal evidence in disposable workspace",
            ProposalOnlyOperationKinds.PatchPackageWrite => "package patch proposal artifacts for review",
            ProposalOnlyOperationKinds.GovernedStatusInspect => "inspect governed operation status",
            _ => "review proposal-only profile boundary"
        };

        return [action];
    }

    private static IReadOnlyList<string> BuildForbiddenActions() =>
    [
        "do not infer durable authority from ProposalOnly profile",
        "do not use proposal evidence as approval",
        "do not change durable source from ProposalOnly mode",
        "do not carry ProposalOnly evidence into a later authority boundary"
    ];

    private static IReadOnlyList<string> BuildEvidenceRefs(ProposalOnlyRunProfileEvaluationRequest request) =>
        Clean(
        [
            Ref("run-profile", RunProfileKind.ProposalOnly.ToString()),
            Ref("repo", request.RepoId),
            Ref("branch", request.Branch),
            .. ValuesOrEmpty(request.EvidenceRefs),
            .. ValuesOrEmpty(request.ArtifactRefs),
            .. ValuesOrEmpty(request.RequestedPaths).Select(path => Ref("requested-path", path))
        ]);

    private static string Ref(string prefix, string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $"{prefix}:{value.Trim()}";

    private static string DisplayOperationKind(string operationKind) =>
        string.IsNullOrWhiteSpace(operationKind) ? "requested operation" : operationKind.Trim();

    private static IReadOnlyList<string> Clean(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> ValuesOrEmpty(IReadOnlyList<string>? values) =>
        values ?? [];
}
