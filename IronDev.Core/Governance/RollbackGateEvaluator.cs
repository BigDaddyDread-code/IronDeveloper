namespace IronDev.Core.Governance;

public static class RollbackGateEvaluator
{
    private const string CreateChangeKind = "Create";
    private const string ModifyChangeKind = "Modify";
    private const string DeleteChangeKind = "Delete";
    private const string RenameChangeKind = "Rename";

    private const string DeleteCreatedFileActionKind = "DeleteCreatedFile";
    private const string RestoreModifiedFileActionKind = "RestoreModifiedFile";
    private const string RecreateDeletedFileActionKind = "RecreateDeletedFile";
    private const string RenameBackActionKind = "RenameBack";
    private const string NoopActionKind = "Noop";

    private static readonly string[] PrivateMaterialMarkers =
    [
        "raw prompt",
        "raw completion",
        "raw tool output",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "private reasoning",
        "hidden reasoning",
        "scratchpad",
        "system prompt",
        "developer prompt",
        "password",
        "api_key",
        "secret",
        "private key",
        "bearer"
    ];

    private static readonly string[] AuthorityClaimMarkers =
    [
        "source applied",
        "applies source",
        "rollback executed",
        "rollback succeeded",
        "continues workflow",
        "workflow continued",
        "approves release",
        "release approved",
        "release ready"
    ];

    public static RollbackGateEvaluationResult Evaluate(RollbackGateEvaluationRequest? request)
    {
        var issues = new List<RollbackGateEvaluationIssue>();

        if (request is null)
        {
            Add(issues, "REQUEST_REQUIRED", "request", "Rollback gate evaluation request is required.");
            return BuildResult(null, issues);
        }

        ValidateRequestShape(request, issues);
        ValidatePatchArtifact(request.PatchArtifact, issues);
        ValidateRollbackPlan(request.RollbackPlan, issues);
        ValidateRequestSafety(request, issues);

        if (request.PatchArtifact is not null && request.RollbackPlan is not null)
        {
            ValidateBindings(request, issues);
            ValidateRollbackCoverage(request.PatchArtifact, request.RollbackPlan, issues);
        }

        return BuildResult(request, issues);
    }

    private static void ValidateRequestShape(RollbackGateEvaluationRequest request, List<RollbackGateEvaluationIssue> issues)
    {
        if (request.ProjectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", nameof(request.ProjectId), "Project ID is required.");
        }

        if (request.PatchArtifact is null)
        {
            Add(issues, "PATCH_ARTIFACT_REQUIRED", nameof(request.PatchArtifact), "Patch artifact is required.");
        }

        if (request.RollbackPlan is null)
        {
            Add(issues, "ROLLBACK_PLAN_REQUIRED", nameof(request.RollbackPlan), "Rollback plan is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ExpectedBranch))
        {
            Add(issues, "EXPECTED_BRANCH_REQUIRED", nameof(request.ExpectedBranch), "Expected branch is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ExpectedCleanWorktreeHash))
        {
            Add(issues, "EXPECTED_CLEAN_WORKTREE_HASH_REQUIRED", nameof(request.ExpectedCleanWorktreeHash), "Expected clean worktree hash is required.");
        }

        if (request.EvidenceReferences is null || request.EvidenceReferences.Count == 0 || request.EvidenceReferences.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, "EVIDENCE_REFERENCES_REQUIRED", nameof(request.EvidenceReferences), "At least one evidence reference is required.");
        }

        if (request.BoundaryMaxims is null || request.BoundaryMaxims.Count == 0 || request.BoundaryMaxims.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, "BOUNDARY_MAXIMS_REQUIRED", nameof(request.BoundaryMaxims), "At least one boundary maxim is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Boundary))
        {
            Add(issues, "BOUNDARY_REQUIRED", nameof(request.Boundary), "Boundary text is required.");
        }
    }

    private static void ValidatePatchArtifact(PatchArtifact? artifact, List<RollbackGateEvaluationIssue> issues)
    {
        var validation = PatchArtifactValidation.Validate(artifact);
        if (!validation.IsValid)
        {
            Add(issues, "PATCH_ARTIFACT_INVALID", nameof(RollbackGateEvaluationRequest.PatchArtifact), "Patch artifact must be valid before rollback gate evaluation.");
        }
    }

    private static void ValidateRollbackPlan(RollbackPlan? plan, List<RollbackGateEvaluationIssue> issues)
    {
        var validation = RollbackPlanValidation.Validate(plan);
        if (!validation.IsValid)
        {
            Add(issues, "ROLLBACK_PLAN_INVALID", nameof(RollbackGateEvaluationRequest.RollbackPlan), "Rollback plan must be valid before rollback gate evaluation.");
        }
    }

    private static void ValidateRequestSafety(RollbackGateEvaluationRequest request, List<RollbackGateEvaluationIssue> issues)
    {
        ValidateSafeText(issues, request.ExpectedBranch, nameof(request.ExpectedBranch));
        ValidateSafeText(issues, request.ExpectedCleanWorktreeHash, nameof(request.ExpectedCleanWorktreeHash));
        ValidateSafeText(issues, request.Boundary, nameof(request.Boundary));
        ValidateSafeList(issues, request.EvidenceReferences, nameof(request.EvidenceReferences));
        ValidateSafeList(issues, request.BoundaryMaxims, nameof(request.BoundaryMaxims));
    }

    private static void ValidateBindings(RollbackGateEvaluationRequest request, List<RollbackGateEvaluationIssue> issues)
    {
        var artifact = request.PatchArtifact;
        var plan = request.RollbackPlan;

        if (request.ProjectId != artifact.ProjectId || request.ProjectId != plan.ProjectId || artifact.ProjectId != plan.ProjectId)
        {
            Add(issues, "PROJECT_ID_MISMATCH", nameof(request.ProjectId), "Rollback gate project, patch artifact project, and rollback plan project must match.");
        }

        AddMismatch(issues, artifact.PatchArtifactId, plan.PatchArtifactId, "PATCH_ARTIFACT_ID_MISMATCH", nameof(plan.PatchArtifactId), "Rollback plan must reference the evaluated patch artifact ID.");
        AddMismatch(issues, artifact.PatchHash, plan.PatchHash, "PATCH_HASH_MISMATCH", nameof(plan.PatchHash), "Rollback plan patch hash must match the patch artifact hash.");
        AddMismatch(issues, artifact.ChangeSetHash, plan.ChangeSetHash, "CHANGE_SET_HASH_MISMATCH", nameof(plan.ChangeSetHash), "Rollback plan change-set hash must match the patch artifact change-set hash.");
        AddMismatch(issues, artifact.ControlledDryRunRequestId, plan.ControlledDryRunRequestId, "CONTROLLED_DRY_RUN_REQUEST_ID_MISMATCH", nameof(plan.ControlledDryRunRequestId), "Rollback plan controlled dry-run request ID must match the patch artifact.");
        AddMismatch(issues, artifact.DryRunExecutionAuditId, plan.DryRunExecutionAuditId, "DRY_RUN_EXECUTION_AUDIT_ID_MISMATCH", nameof(plan.DryRunExecutionAuditId), "Rollback plan dry-run execution audit ID must match the patch artifact.");
        AddMismatch(issues, artifact.DryRunAuditHash, plan.DryRunAuditHash, "DRY_RUN_AUDIT_HASH_MISMATCH", nameof(plan.DryRunAuditHash), "Rollback plan dry-run audit hash must match the patch artifact.");
        AddMismatch(issues, artifact.DryRunReceiptHash, plan.DryRunReceiptHash, "DRY_RUN_RECEIPT_HASH_MISMATCH", nameof(plan.DryRunReceiptHash), "Rollback plan dry-run receipt hash must match the patch artifact.");
        AddMismatch(issues, artifact.PolicySatisfactionId, plan.PolicySatisfactionId, "POLICY_SATISFACTION_ID_MISMATCH", nameof(plan.PolicySatisfactionId), "Rollback plan policy satisfaction ID must match the patch artifact.");
        AddMismatch(issues, artifact.PolicySatisfactionHash, plan.PolicySatisfactionHash, "POLICY_SATISFACTION_HASH_MISMATCH", nameof(plan.PolicySatisfactionHash), "Rollback plan policy satisfaction hash must match the patch artifact.");
        AddMismatch(issues, artifact.SubjectKind, plan.SubjectKind, "SUBJECT_KIND_MISMATCH", nameof(plan.SubjectKind), "Rollback plan subject kind must match the patch artifact.");
        AddMismatch(issues, artifact.SubjectId, plan.SubjectId, "SUBJECT_ID_MISMATCH", nameof(plan.SubjectId), "Rollback plan subject ID must match the patch artifact.");
        AddMismatch(issues, artifact.SubjectHash, plan.SubjectHash, "SUBJECT_HASH_MISMATCH", nameof(plan.SubjectHash), "Rollback plan subject hash must match the patch artifact.");
        AddMismatch(issues, artifact.SourceSnapshotReference, plan.SourceSnapshotReference, "SOURCE_SNAPSHOT_REFERENCE_MISMATCH", nameof(plan.SourceSnapshotReference), "Rollback plan source snapshot reference must match the patch artifact.");
        AddMismatch(issues, artifact.SourceBaselineHash, plan.SourceBaselineHash, "SOURCE_BASELINE_HASH_MISMATCH", nameof(plan.SourceBaselineHash), "Rollback plan source baseline hash must match the patch artifact.");
        AddMismatch(issues, artifact.WorkspaceBoundaryHash, plan.WorkspaceBoundaryHash, "WORKSPACE_BOUNDARY_HASH_MISMATCH", nameof(plan.WorkspaceBoundaryHash), "Rollback plan workspace boundary hash must match the patch artifact.");
        AddMismatch(issues, request.ExpectedBranch, plan.ExpectedBranch, "EXPECTED_BRANCH_MISMATCH", nameof(request.ExpectedBranch), "Rollback gate expected branch must match the rollback plan expected branch.");
        AddMismatch(issues, request.ExpectedCleanWorktreeHash, plan.ExpectedCleanWorktreeHash, "EXPECTED_CLEAN_WORKTREE_HASH_MISMATCH", nameof(request.ExpectedCleanWorktreeHash), "Rollback gate expected clean worktree hash must match the rollback plan expected clean worktree hash.");
    }

    private static void ValidateRollbackCoverage(PatchArtifact artifact, RollbackPlan plan, List<RollbackGateEvaluationIssue> issues)
    {
        var fileChanges = artifact.FileChanges ?? [];
        var fileActions = plan.FileActions ?? [];

        foreach (var group in fileActions.GroupBy(action => (Path: Normalize(action.Path), Kind: Normalize(action.PlannedActionKind))))
        {
            if (group.Count() > 1)
            {
                Add(issues, "ROLLBACK_ACTION_DUPLICATE", nameof(plan.FileActions), "Rollback actions must not duplicate the same path and action kind.");
            }
        }

        foreach (var change in fileChanges)
        {
            ValidateCoverageForChange(change, fileActions, issues);
        }

        foreach (var action in fileActions)
        {
            if (StringEquals(action.PlannedActionKind, NoopActionKind))
            {
                continue;
            }

            if (!fileChanges.Any(change => StringEquals(change.Path, action.Path)))
            {
                Add(issues, "ROLLBACK_ACTION_NOT_BOUND_TO_PATCH_CHANGE", nameof(plan.FileActions), "Rollback action must be bound to a patch artifact file change unless it is Noop.");
            }
        }
    }

    private static void ValidateCoverageForChange(PatchArtifactFileChange change, IReadOnlyList<RollbackPlanFileAction> actions, List<RollbackGateEvaluationIssue> issues)
    {
        var requiredKind = RequiredRollbackActionKind(change.ChangeKind);
        if (requiredKind is null)
        {
            return;
        }

        var pathActions = actions
            .Where(action => StringEquals(action.Path, change.Path) && !StringEquals(action.PlannedActionKind, NoopActionKind))
            .ToArray();

        if (pathActions.Length == 0)
        {
            Add(issues, "ROLLBACK_COVERAGE_MISSING", nameof(RollbackPlan.FileActions), "Each patch artifact file change requires a rollback action. Noop does not satisfy coverage.");
            return;
        }

        var matchingActions = pathActions
            .Where(action => StringEquals(action.PlannedActionKind, requiredKind))
            .ToArray();

        if (matchingActions.Length == 0)
        {
            Add(issues, "ROLLBACK_ACTION_KIND_MISMATCH", nameof(RollbackPlan.FileActions), "Rollback action kind must match the patch artifact file change kind.");
            return;
        }

        foreach (var action in matchingActions)
        {
            ValidateRollbackActionHashes(change, action, issues);
        }
    }

    private static void ValidateRollbackActionHashes(PatchArtifactFileChange change, RollbackPlanFileAction action, List<RollbackGateEvaluationIssue> issues)
    {
        var mismatch = change.ChangeKind switch
        {
            CreateChangeKind =>
                !StringEquals(action.ExpectedCurrentContentHash, change.AfterContentHash) ||
                !StringEquals(action.DeleteContentHash, change.AfterContentHash),
            ModifyChangeKind =>
                !StringEquals(action.ExpectedCurrentContentHash, change.AfterContentHash) ||
                !StringEquals(action.RestoreContentHash, change.BeforeContentHash),
            DeleteChangeKind =>
                !StringEquals(action.RestoreContentHash, change.BeforeContentHash),
            RenameChangeKind =>
                !StringEquals(action.PreviousPath, change.PreviousPath) ||
                !StringEquals(action.ExpectedCurrentContentHash, change.AfterContentHash) ||
                !StringEquals(action.RestoreContentHash, change.BeforeContentHash),
            _ => false
        };

        if (mismatch)
        {
            Add(issues, "ROLLBACK_ACTION_HASH_MISMATCH", nameof(RollbackPlan.FileActions), "Rollback action hashes must match the patch artifact file change before/after content hashes.");
        }
    }

    private static string? RequiredRollbackActionKind(string? changeKind) => changeKind switch
    {
        CreateChangeKind => DeleteCreatedFileActionKind,
        ModifyChangeKind => RestoreModifiedFileActionKind,
        DeleteChangeKind => RecreateDeletedFileActionKind,
        RenameChangeKind => RenameBackActionKind,
        _ => null
    };

    private static RollbackGateEvaluationResult BuildResult(RollbackGateEvaluationRequest? request, IReadOnlyList<RollbackGateEvaluationIssue> issues)
    {
        var artifact = request?.PatchArtifact;
        var plan = request?.RollbackPlan;
        var boundary = SafeScalar(string.IsNullOrWhiteSpace(request?.Boundary) ? RollbackGateBoundaryText.Boundary : request.Boundary);

        return new RollbackGateEvaluationResult
        {
            Satisfied = issues.Count == 0,
            ProjectId = request?.ProjectId ?? Guid.Empty,
            PatchArtifactId = artifact?.PatchArtifactId ?? Guid.Empty,
            PatchHash = SafeScalar(artifact?.PatchHash),
            ChangeSetHash = SafeScalar(artifact?.ChangeSetHash),
            RollbackPlanId = plan?.RollbackPlanId ?? Guid.Empty,
            RollbackPlanHash = SafeScalar(plan?.RollbackPlanHash),
            SourceBaselineHash = SafeScalar(artifact?.SourceBaselineHash ?? plan?.SourceBaselineHash),
            ExpectedBranch = SafeScalar(request?.ExpectedBranch),
            ExpectedCleanWorktreeHash = SafeScalar(request?.ExpectedCleanWorktreeHash),
            Issues = issues.ToArray(),
            EvidenceReferences = SafeList(request?.EvidenceReferences),
            BoundaryMaxims = SafeList(request?.BoundaryMaxims),
            Boundary = string.IsNullOrWhiteSpace(boundary) ? RollbackGateBoundaryText.Boundary : boundary
        };
    }

    private static IReadOnlyList<string> SafeList(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value) && !ContainsUnsafeMarker(value))
            .Select(value => value.Trim())
            .ToArray();
    }

    private static string SafeScalar(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? string.Empty : value.Trim();

    private static void ValidateSafeList(List<RollbackGateEvaluationIssue> issues, IReadOnlyList<string>? values, string field)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            ValidateSafeText(issues, value, field);
        }
    }

    private static void ValidateSafeText(List<RollbackGateEvaluationIssue> issues, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var marker in PrivateMaterialMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "PRIVATE_OR_RAW_MATERIAL_REJECTED", field, $"Rollback gate evaluation text must not contain private or raw material: {marker}.");
            }
        }

        foreach (var marker in AuthorityClaimMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "AUTHORITY_CLAIM_REJECTED", field, $"Rollback gate evaluation text must not claim authority: {marker}.");
            }
        }
    }

    private static bool ContainsUnsafeMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return PrivateMaterialMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
            AuthorityClaimMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddMismatch(List<RollbackGateEvaluationIssue> issues, Guid left, Guid right, string code, string field, string message)
    {
        if (left != right)
        {
            Add(issues, code, field, message);
        }
    }

    private static void AddMismatch(List<RollbackGateEvaluationIssue> issues, string? left, string? right, string code, string field, string message)
    {
        if (!StringEquals(left, right))
        {
            Add(issues, code, field, message);
        }
    }

    private static bool StringEquals(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static void Add(List<RollbackGateEvaluationIssue> issues, string code, string field, string message) =>
        issues.Add(new RollbackGateEvaluationIssue(code, field, message));
}
