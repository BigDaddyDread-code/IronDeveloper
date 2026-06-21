namespace IronDev.Core.Governance;

public static class ProposalOnlyRunProfileEvaluator
{
    private static readonly string[] AllowedOperationKinds =
    [
        ProposalOnlyOperationKinds.RepoInspect,
        ProposalOnlyOperationKinds.TaskInterpretation,
        ProposalOnlyOperationKinds.DisposableWorkspaceCreate,
        ProposalOnlyOperationKinds.DisposableWorkspaceModify,
        ProposalOnlyOperationKinds.DisposableWorkspaceValidate,
        ProposalOnlyOperationKinds.PatchProposal,
        ProposalOnlyOperationKinds.PatchPackageWrite,
        ProposalOnlyOperationKinds.GovernedStatusInspect
    ];

    private static readonly string[] BlockedOperationKinds =
    [
        ProposalOnlyOperationKinds.SourceApply,
        ProposalOnlyOperationKinds.Rollback,
        ProposalOnlyOperationKinds.Commit,
        ProposalOnlyOperationKinds.Push,
        ProposalOnlyOperationKinds.DraftPullRequest,
        ProposalOnlyOperationKinds.ReadyForReview,
        ProposalOnlyOperationKinds.Merge,
        ProposalOnlyOperationKinds.Release,
        ProposalOnlyOperationKinds.Deployment,
        ProposalOnlyOperationKinds.MemoryPromotion,
        ProposalOnlyOperationKinds.WorkflowContinuation,
        ProposalOnlyOperationKinds.ApprovalRequestCreate,
        ProposalOnlyOperationKinds.PolicySatisfaction,
        ProposalOnlyOperationKinds.ProviderMutation,
        ProposalOnlyOperationKinds.PackagePublication
    ];

    public static IReadOnlyList<string> AllowedOperations { get; } = AllowedOperationKinds;
    public static IReadOnlyList<string> BlockedOperations { get; } = BlockedOperationKinds;

    public static ProposalOnlyRunProfileEvaluationResult Evaluate(ProposalOnlyRunProfileEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = ValidateRequest(request).ToList();
        var redFlags = DetectAuthorityRedFlags(request).ToList();
        var isAllowedKind = Contains(AllowedOperationKinds, request.OperationKind);
        var isBlockedKind = Contains(BlockedOperationKinds, request.OperationKind);

        GovernedOperationStatus status;
        if (isAllowedKind)
        {
            status = ProposalOnlyRunProfileStatusMapper.MapAllowed(request);
        }
        else
        {
            var operationKind = DisplayOperationKind(request.OperationKind);
            var reason = isBlockedKind
                ? $"ProposalOnly does not allow {operationKind}."
                : $"ProposalOnly does not recognize {operationKind} as an allowed operation.";
            var missingAuthority = isBlockedKind
                ? $"explicit-authority:{operationKind}"
                : $"allowed-operation-kind:{operationKind}";

            if (isBlockedKind)
                issues.Add($"ProposalOnlyOperationBlocked:{operationKind}");
            else
                issues.Add("ProposalOnlyOperationKindNotAllowed");

            status = ProposalOnlyRunProfileStatusMapper.MapBlocked(request, reason, missingAuthority);
        }

        var validation = GovernedOperationStatusValidator.Validate(status);
        var allIssues = issues
            .Concat(validation.Issues)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var allRedFlags = redFlags
            .Concat(validation.RedFlags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProposalOnlyRunProfileEvaluationResult
        {
            IsAllowed = isAllowedKind && allIssues.Length == 0 && allRedFlags.Length == 0 && validation.IsValid,
            Status = status,
            StatusValidation = validation,
            Issues = allIssues,
            RedFlags = allRedFlags
        };
    }

    private static IEnumerable<string> ValidateRequest(ProposalOnlyRunProfileEvaluationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RepoId))
            yield return "ProposalOnlyRepoIdRequired";
        if (string.IsNullOrWhiteSpace(request.Branch))
            yield return "ProposalOnlyBranchRequired";
    }

    private static IEnumerable<string> DetectAuthorityRedFlags(ProposalOnlyRunProfileEvaluationRequest request)
    {
        var normalized = AllInputText(request)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim().ToLowerInvariant())
            .ToArray();

        if (ContainsAll(normalized, "proposal-only", "approves", "source", "apply"))
            yield return "ProposalOnlyCannotApproveSourceApply";
        if (ContainsAll(normalized, "proposal-only", "mutate", "source"))
            yield return "ProposalOnlyCannotMutateSource";
        if (ContainsAll(normalized, "proposal-only", "commit"))
            yield return "ProposalOnlyCannotCommit";
        if (ContainsAll(normalized, "proposal-only", "push"))
            yield return "ProposalOnlyCannotPush";
        if (ContainsAll(normalized, "proposal-only", "create", "prs"))
            yield return "ProposalOnlyCannotCreatePullRequests";
        if (ContainsAll(normalized, "proposal-only", "merge"))
            yield return "ProposalOnlyCannotMerge";
        if (ContainsAll(normalized, "proposal-only", "release"))
            yield return "ProposalOnlyCannotRelease";
        if (ContainsAll(normalized, "proposal-only", "deploy"))
            yield return "ProposalOnlyCannotDeploy";
        if (ContainsAll(normalized, "proposal-only", "promote", "memory"))
            yield return "ProposalOnlyCannotPromoteMemory";
        if (ContainsAll(normalized, "proposal-only", "continue", "workflow"))
            yield return "ProposalOnlyCannotContinueWorkflow";
        if (ContainsAll(normalized, "proposal-only", "satisfy", "policy"))
            yield return "ProposalOnlyCannotSatisfyPolicy";
        if (ContainsAll(normalized, "proposal-only", "create", "approval", "records"))
            yield return "ProposalOnlyCannotCreateAuthorityRecords";
        if (ContainsAll(normalized, "patch", "proposal", "authorizes", "apply"))
            yield return "PatchProposalCannotAuthorizeApply";
        if (ContainsAll(normalized, "tests", "passed", "approved"))
            yield return "ValidationSuccessCannotApprove";
        if (ContainsAll(normalized, "memory", "proposal-only", "approved"))
            yield return "MemoryReferenceCannotApproveProposalOnly";
        if (ContainsAll(normalized, "ui", "marked", "proposal-only", "approved"))
            yield return "UiStateCannotApproveProposalOnly";
    }

    private static bool ContainsAll(IEnumerable<string> values, params string[] tokens) =>
        values.Any(value => tokens.All(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)));

    private static IEnumerable<string?> AllInputText(ProposalOnlyRunProfileEvaluationRequest request)
    {
        yield return request.OperationId;
        yield return request.OperationKind;
        yield return request.Subject;
        yield return request.RepoId;
        yield return request.Branch;

        foreach (var value in ValuesOrEmpty(request.EvidenceRefs))
            yield return value;
        foreach (var value in ValuesOrEmpty(request.ArtifactRefs))
            yield return value;
        foreach (var value in ValuesOrEmpty(request.RequestedPaths))
            yield return value;
    }

    private static bool Contains(IEnumerable<string> values, string value) =>
        values.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static string DisplayOperationKind(string operationKind) =>
        string.IsNullOrWhiteSpace(operationKind) ? "requested operation" : operationKind.Trim();

    private static IEnumerable<string> ValuesOrEmpty(IReadOnlyList<string>? values) =>
        values ?? [];
}
