namespace IronDev.Core.Governance;

public static class AuthorityEvidenceKinds
{
    public const string AcceptedApproval = "AcceptedApproval";
    public const string PolicySatisfaction = "PolicySatisfaction";
    public const string SourceApplyRequest = "SourceApplyRequest";
    public const string SourceApplyReceipt = "SourceApplyReceipt";
    public const string RollbackExecutionReceipt = "RollbackExecutionReceipt";
    public const string RollbackExecutionAudit = "RollbackExecutionAudit";
    public const string WorkflowContinuationGate = "WorkflowContinuationGate";
    public const string WorkflowTransitionRecord = "WorkflowTransitionRecord";
    public const string ReleaseReadinessReport = "ReleaseReadinessReport";
    public const string ReleaseReadinessDecisionRecord = "ReleaseReadinessDecisionRecord";
    public const string GovernedReleaseGateResult = "GovernedReleaseGateResult";
}

public static class StaleAuthorityFindingSeverities
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Blocking = "Blocking";
}

public static class StaleAuthorityDetectionBoundaryText
{
    public const string Boundary = """
        Stale authority detection inspects supplied governance evidence only.
        Stale authority detection is not authority.
        Stale authority detection is not release approval.
        Stale authority detection is not deployment approval.
        Stale authority detection is not merge approval.
        Stale authority detection is not release execution.
        Stale authority detection is not source apply.
        Stale authority detection is not rollback execution.
        Stale authority detection is not workflow continuation.
        Stale authority detection does not mutate workflow state.
        Stale authority detection does not refresh approval, policy, source, rollback, workflow, or release-readiness evidence.
        Stale authority detection does not run git.
        Stale authority detection does not create pull requests.
        Stale authority detection does not call agents, models, tools, UI, memory, or retrieval.
        Human review remains required for release approval, deployment, and merge.
        """;
}

public sealed record StaleAuthorityDetectionRequest
{
    public required Guid StaleAuthorityDetectionRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string CurrentSubjectHash { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public required IReadOnlyList<AuthorityEvidenceSnapshot> Evidence { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = StaleAuthorityDetectionBoundaryText.Boundary;
}

public sealed record AuthorityEvidenceSnapshot
{
    public required string EvidenceKind { get; init; }
    public required string EvidenceId { get; init; }
    public required string EvidenceHash { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public string? PolicyVersion { get; init; }
    public string? ApprovalVersion { get; init; }
    public string? SourceBaselineHash { get; init; }
    public bool Superseded { get; init; }
    public string? SupersededByEvidenceId { get; init; }
    public string? SupersededByEvidenceHash { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
}

public sealed record StaleAuthorityDetectionResult
{
    public required Guid StaleAuthorityDetectionRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string CurrentSubjectHash { get; init; }
    public required bool IsCurrent { get; init; }
    public required bool HasStaleAuthority { get; init; }
    public required bool HasExpiredEvidence { get; init; }
    public required bool HasSupersededEvidence { get; init; }
    public required bool HasSubjectHashMismatch { get; init; }
    public required bool HasWorkflowMismatch { get; init; }
    public required bool HasUnsafeMaterial { get; init; }
    public required IReadOnlyList<StaleAuthorityFinding> Findings { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool DeploymentApproved { get; init; }
    public required bool MergeApproved { get; init; }
    public required bool ReleaseExecuted { get; init; }
    public required bool SourceApplyExecuted { get; init; }
    public required bool RollbackExecuted { get; init; }
    public required bool WorkflowContinued { get; init; }
    public required bool WorkflowMutated { get; init; }
    public required bool GitOperationExecuted { get; init; }
    public required bool AuthorityRefreshed { get; init; }
    public required bool EvidenceReissued { get; init; }
    public required bool HumanReviewRequired { get; init; }
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public string Boundary { get; init; } = StaleAuthorityDetectionBoundaryText.Boundary;
}

public sealed record StaleAuthorityFinding
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string EvidenceKind { get; init; }
    public required string EvidenceId { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public sealed class StaleAuthorityDetector
{
    private static readonly HashSet<string> SupportedEvidenceKinds = new(StringComparer.Ordinal)
    {
        AuthorityEvidenceKinds.AcceptedApproval,
        AuthorityEvidenceKinds.PolicySatisfaction,
        AuthorityEvidenceKinds.SourceApplyRequest,
        AuthorityEvidenceKinds.SourceApplyReceipt,
        AuthorityEvidenceKinds.RollbackExecutionReceipt,
        AuthorityEvidenceKinds.RollbackExecutionAudit,
        AuthorityEvidenceKinds.WorkflowContinuationGate,
        AuthorityEvidenceKinds.WorkflowTransitionRecord,
        AuthorityEvidenceKinds.ReleaseReadinessReport,
        AuthorityEvidenceKinds.ReleaseReadinessDecisionRecord,
        AuthorityEvidenceKinds.GovernedReleaseGateResult
    };

    private static readonly string[] PrivateOrRawMarkers =
    [
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "private reasoning",
        "hidden reasoning",
        "scratchpad",
        "entire patch",
        "entirepatch",
        "patch payload",
        "patchpayload",
        "password",
        "api_key",
        "apikey",
        "secret",
        "private key",
        "bearer"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "release approved",
        "approved for release",
        "deployment approved",
        "merge approved",
        "safe to deploy",
        "safe to merge",
        "can deploy",
        "can merge",
        "green to ship",
        "release executed",
        "source applied by decision",
        "rollback executed by decision",
        "workflow continued by decision",
        "git " + "committed",
        "git " + "pushed",
        "tag created",
        "pull request created",
        "memory promoted",
        "retrieval activated",
        "agent dispatched",
        "tool executed",
        "model called"
    ];

    public StaleAuthorityDetectionResult Detect(StaleAuthorityDetectionRequest? request)
    {
        var findings = new List<StaleAuthorityFinding>();
        ValidateRequestShape(request, findings);

        if (request is not null && request.Evidence is not null)
        {
            for (var index = 0; index < request.Evidence.Count; index++)
                InspectEvidence(request, request.Evidence[index], index, findings);
        }

        var hasExpiredEvidence = findings.Any(finding => finding.Code == "EvidenceExpired");
        var hasSupersededEvidence = findings.Any(finding => finding.Code == "EvidenceSuperseded" || finding.Code == "SupersedingEvidenceMissing");
        var hasSubjectHashMismatch = findings.Any(finding => finding.Code == "SubjectBindingMismatch");
        var hasWorkflowMismatch = findings.Any(finding => finding.Code == "WorkflowBindingMismatch");
        var hasUnsafeMaterial = findings.Any(finding => finding.Code is "PrivateRawMaterialRejected" or "AuthorityClaimRejected");
        var hasBlocking = findings.Any(finding => finding.Severity == StaleAuthorityFindingSeverities.Blocking);

        return new StaleAuthorityDetectionResult
        {
            StaleAuthorityDetectionRequestId = request?.StaleAuthorityDetectionRequestId ?? Guid.Empty,
            ProjectId = request?.ProjectId ?? Guid.Empty,
            SubjectKind = SafeText(request?.SubjectKind),
            SubjectId = SafeText(request?.SubjectId),
            CurrentSubjectHash = SafeText(request?.CurrentSubjectHash),
            IsCurrent = !hasBlocking,
            HasStaleAuthority = hasBlocking,
            HasExpiredEvidence = hasExpiredEvidence,
            HasSupersededEvidence = hasSupersededEvidence,
            HasSubjectHashMismatch = hasSubjectHashMismatch,
            HasWorkflowMismatch = hasWorkflowMismatch,
            HasUnsafeMaterial = hasUnsafeMaterial,
            Findings = findings,
            EvidenceReferences = SafeList(request?.EvidenceReferences),
            BoundaryMaxims = SafeList(request?.BoundaryMaxims),
            ReleaseApproved = false,
            DeploymentApproved = false,
            MergeApproved = false,
            ReleaseExecuted = false,
            SourceApplyExecuted = false,
            RollbackExecuted = false,
            WorkflowContinued = false,
            WorkflowMutated = false,
            GitOperationExecuted = false,
            AuthorityRefreshed = false,
            EvidenceReissued = false,
            HumanReviewRequired = true,
            EvaluatedAtUtc = request?.EvaluatedAtUtc == default ? DateTimeOffset.UtcNow : request!.EvaluatedAtUtc,
            Boundary = StaleAuthorityDetectionBoundaryText.Boundary
        };
    }

    private static void ValidateRequestShape(StaleAuthorityDetectionRequest? request, List<StaleAuthorityFinding> findings)
    {
        if (request is null)
        {
            Add(findings, "RequestRequired", "Request", "", "", "request", "Stale authority detection request is required.");
            return;
        }

        if (request.StaleAuthorityDetectionRequestId == Guid.Empty)
            Add(findings, "RequestIdRequired", "Request", "", "", nameof(request.StaleAuthorityDetectionRequestId), "Request id is required.");
        if (request.ProjectId == Guid.Empty)
            Add(findings, "ProjectIdRequired", "Request", "", "", nameof(request.ProjectId), "Project id is required.");
        if (request.EvaluatedAtUtc == default)
            Add(findings, "EvaluatedAtRequired", "Request", "", "", nameof(request.EvaluatedAtUtc), "Evaluated timestamp is required.");

        RequireText(request.SubjectKind, nameof(request.SubjectKind), findings);
        RequireText(request.SubjectId, nameof(request.SubjectId), findings);
        RequireText(request.CurrentSubjectHash, nameof(request.CurrentSubjectHash), findings);
        RequireText(request.WorkflowRunId, nameof(request.WorkflowRunId), findings);
        RequireText(request.WorkflowStepId, nameof(request.WorkflowStepId), findings);
        RequireText(request.Boundary, nameof(request.Boundary), findings);
        RequireList(request.EvidenceReferences, nameof(request.EvidenceReferences), findings);
        RequireList(request.BoundaryMaxims, nameof(request.BoundaryMaxims), findings);

        if (!string.IsNullOrWhiteSpace(request.CurrentSubjectHash) && !IsSupportedHash(request.CurrentSubjectHash))
            Add(findings, "InvalidCurrentSubjectHash", "Request", "", "", nameof(request.CurrentSubjectHash), "Current subject hash must be raw or prefixed SHA-256.");

        if (request.Evidence is null || request.Evidence.Count == 0)
            Add(findings, "EvidenceRequired", "Request", "", "", nameof(request.Evidence), "At least one evidence snapshot is required.");
    }

    private static void InspectEvidence(
        StaleAuthorityDetectionRequest request,
        AuthorityEvidenceSnapshot? evidence,
        int index,
        List<StaleAuthorityFinding> findings)
    {
        var fieldPrefix = $"Evidence[{index}]";
        if (evidence is null)
        {
            Add(findings, "EvidenceSnapshotRequired", "Evidence", "", "", fieldPrefix, "Evidence snapshot is required.");
            return;
        }

        var evidenceKind = SafeText(evidence.EvidenceKind);
        var evidenceId = SafeText(evidence.EvidenceId);

        RequireEvidenceText(evidence.EvidenceKind, evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.EvidenceKind)}", findings);
        RequireEvidenceText(evidence.EvidenceId, evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.EvidenceId)}", findings);
        RequireEvidenceText(evidence.EvidenceHash, evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.EvidenceHash)}", findings);
        RequireEvidenceText(evidence.SubjectKind, evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.SubjectKind)}", findings);
        RequireEvidenceText(evidence.SubjectId, evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.SubjectId)}", findings);
        RequireEvidenceText(evidence.SubjectHash, evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.SubjectHash)}", findings);
        RequireEvidenceText(evidence.WorkflowRunId, evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.WorkflowRunId)}", findings);
        RequireEvidenceText(evidence.WorkflowStepId, evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.WorkflowStepId)}", findings);
        RequireEvidenceList(evidence.EvidenceReferences, evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.EvidenceReferences)}", findings);

        if (!SupportedEvidenceKinds.Contains(evidence.EvidenceKind))
            Add(findings, "UnsupportedEvidenceKind", evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.EvidenceKind)}", "Evidence kind is not supported.");

        if (!string.IsNullOrWhiteSpace(evidence.EvidenceHash) && !IsSupportedHash(evidence.EvidenceHash))
            Add(findings, "InvalidEvidenceHash", evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.EvidenceHash)}", "Evidence hash must be raw or prefixed SHA-256.");

        if (!string.IsNullOrWhiteSpace(evidence.SubjectHash) && !IsSupportedHash(evidence.SubjectHash))
            Add(findings, "InvalidSubjectHash", evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.SubjectHash)}", "Evidence subject hash must be raw or prefixed SHA-256.");

        if (!string.IsNullOrWhiteSpace(evidence.SupersededByEvidenceHash) && !IsSupportedHash(evidence.SupersededByEvidenceHash))
            Add(findings, "InvalidSupersedingEvidenceHash", evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.SupersededByEvidenceHash)}", "Superseding evidence hash must be raw or prefixed SHA-256.");

        if (!string.IsNullOrWhiteSpace(evidence.SourceBaselineHash) && !IsSupportedHash(evidence.SourceBaselineHash))
            Add(findings, "InvalidSourceBaselineHash", evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.SourceBaselineHash)}", "Source baseline hash must be raw or prefixed SHA-256.");

        if (evidence.CreatedAtUtc == default)
            Add(findings, "CreatedAtRequired", evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.CreatedAtUtc)}", "Evidence creation timestamp is required.");
        else if (request.EvaluatedAtUtc != default && evidence.CreatedAtUtc > request.EvaluatedAtUtc)
            Add(findings, "EvidenceCreatedInFuture", evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.CreatedAtUtc)}", "Evidence was created after the stale-authority evaluation timestamp.");

        if (evidence.ExpiresAtUtc is not null && request.EvaluatedAtUtc != default && evidence.ExpiresAtUtc <= request.EvaluatedAtUtc)
            Add(findings, "EvidenceExpired", evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.ExpiresAtUtc)}", "Evidence is expired.");

        if (evidence.Superseded)
        {
            Add(findings, "EvidenceSuperseded", evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.Superseded)}", "Evidence has been superseded.");
            if (string.IsNullOrWhiteSpace(evidence.SupersededByEvidenceId) || string.IsNullOrWhiteSpace(evidence.SupersededByEvidenceHash))
                Add(findings, "SupersedingEvidenceMissing", evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.Superseded)}", "Superseded evidence must identify replacement evidence id and hash.");
        }

        if (!string.Equals(evidence.SubjectKind, request.SubjectKind, StringComparison.Ordinal) ||
            !string.Equals(evidence.SubjectId, request.SubjectId, StringComparison.Ordinal) ||
            !string.Equals(NormalizeHash(evidence.SubjectHash), NormalizeHash(request.CurrentSubjectHash), StringComparison.OrdinalIgnoreCase))
        {
            Add(findings, "SubjectBindingMismatch", evidenceKind, evidenceId, fieldPrefix, "Evidence subject binding does not match the requested subject.");
        }

        if (!string.Equals(evidence.WorkflowRunId, request.WorkflowRunId, StringComparison.Ordinal) ||
            !string.Equals(evidence.WorkflowStepId, request.WorkflowStepId, StringComparison.Ordinal))
        {
            Add(findings, "WorkflowBindingMismatch", evidenceKind, evidenceId, fieldPrefix, "Evidence workflow binding does not match the requested workflow run and step.");
        }

        ScanEvidenceTexts(evidenceKind, evidenceId, fieldPrefix, findings,
            evidence.EvidenceKind,
            evidence.EvidenceId,
            evidence.EvidenceHash,
            evidence.SubjectKind,
            evidence.SubjectId,
            evidence.SubjectHash,
            evidence.WorkflowRunId,
            evidence.WorkflowStepId,
            evidence.PolicyVersion,
            evidence.ApprovalVersion,
            evidence.SourceBaselineHash,
            evidence.SupersededByEvidenceId,
            evidence.SupersededByEvidenceHash);
        ScanEvidenceTexts(evidenceKind, evidenceId, $"{fieldPrefix}.{nameof(evidence.EvidenceReferences)}", findings, evidence.EvidenceReferences);
    }

    private static void RequireText(string? value, string field, List<StaleAuthorityFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(findings, $"{field}Required", "Request", "", "", field, $"{field} is required.");
            return;
        }

        ScanRequestText(value, field, findings);
    }

    private static void RequireList(IReadOnlyList<string>? values, string field, List<StaleAuthorityFinding> findings)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(findings, $"{field}Required", "Request", "", "", field, $"{field} is required.");
            return;
        }

        foreach (var value in values)
            ScanRequestText(value, field, findings);
    }

    private static void RequireEvidenceText(string? value, string evidenceKind, string evidenceId, string field, List<StaleAuthorityFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(value))
            Add(findings, $"{field[(field.LastIndexOf('.') + 1)..]}Required", evidenceKind, evidenceId, field, $"{field} is required.");
    }

    private static void RequireEvidenceList(IReadOnlyList<string>? values, string evidenceKind, string evidenceId, string field, List<StaleAuthorityFinding> findings)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
            Add(findings, "EvidenceReferencesRequired", evidenceKind, evidenceId, field, "Evidence references are required.");
    }

    private static void ScanRequestText(string? value, string field, List<StaleAuthorityFinding> findings)
    {
        if (ContainsPrivateOrRaw(value))
            Add(findings, "PrivateRawMaterialRejected", "Request", "", "", field, "Private, raw, prompt, scratchpad, patch, or secret-like material is not allowed.");
        if (ContainsAuthorityClaim(value))
            Add(findings, "AuthorityClaimRejected", "Request", "", "", field, "Authority claims are not allowed.");
    }

    private static void ScanEvidenceTexts(
        string evidenceKind,
        string evidenceId,
        string field,
        List<StaleAuthorityFinding> findings,
        params string?[] values)
    {
        foreach (var value in values)
        {
            if (ContainsPrivateOrRaw(value))
                Add(findings, "PrivateRawMaterialRejected", evidenceKind, evidenceId, field, "Private, raw, prompt, scratchpad, patch, or secret-like material is not allowed.");
            if (ContainsAuthorityClaim(value))
                Add(findings, "AuthorityClaimRejected", evidenceKind, evidenceId, field, "Authority claims are not allowed.");
        }
    }

    private static void ScanEvidenceTexts(
        string evidenceKind,
        string evidenceId,
        string field,
        List<StaleAuthorityFinding> findings,
        IEnumerable<string>? values)
    {
        if (values is null)
            return;
        foreach (var value in values)
            ScanEvidenceTexts(evidenceKind, evidenceId, field, findings, value);
    }

    private static bool ContainsPrivateOrRaw(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        PrivateOrRawMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAuthorityClaim(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        AuthorityMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsSupportedHash(string? value)
    {
        var normalized = NormalizeHash(value);
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit);
    }

    private static string NormalizeHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var trimmed = value.Trim();
        return trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? trimmed[7..] : trimmed;
    }

    private static IReadOnlyList<string> SafeList(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
            return [];

        return values.Select(SafeText).Where(value => value.Length > 0).ToArray();
    }

    private static string SafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return ContainsPrivateOrRaw(value) || ContainsAuthorityClaim(value) ? "[redacted]" : value.Trim();
    }

    private static void Add(
        List<StaleAuthorityFinding> findings,
        string code,
        string evidenceKind,
        string evidenceId,
        string field,
        string message) =>
        findings.Add(new StaleAuthorityFinding
        {
            Code = code,
            Severity = StaleAuthorityFindingSeverities.Blocking,
            EvidenceKind = SafeText(evidenceKind),
            EvidenceId = SafeText(evidenceId),
            Field = SafeText(field),
            Message = message
        });

    private static void Add(
        List<StaleAuthorityFinding> findings,
        string code,
        string evidenceKind,
        string evidenceId,
        string ignored,
        string field,
        string message) =>
        Add(findings, code, evidenceKind, evidenceId, field, message);
}
