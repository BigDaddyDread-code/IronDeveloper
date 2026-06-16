namespace IronDev.Core.Governance;

public static class PatchArtifactCreationBoundaryText
{
    public const string Boundary = """
        Patch artifact creation is not source apply.
        Patch artifact creation is not rollback.
        Patch artifact creation is not workflow continuation.
        Patch artifact creation is not release readiness.
        Patch artifact creation does not authorize source mutation by itself.
        Patch artifact creation creates a proposed change package only.
        Created patch artifacts must still be reviewed before source apply.
        Created patch artifacts must remain bound to dry-run evidence and source baseline.
        """;

    public static readonly IReadOnlyList<string> Warnings =
    [
        "Patch artifact creation creates a proposed change package only.",
        "Patch artifact creation does not apply source.",
        "Patch artifact creation does not authorize source mutation.",
        "Patch artifact must still be reviewed before source apply."
    ];

    public static readonly IReadOnlyList<string> CreationBoundaryMaxims =
    [
        "Patch artifact creation is not source apply.",
        "Patch artifact creation creates a proposed change package only.",
        "Created patch artifacts must still be reviewed before source apply."
    ];
}

public sealed record PatchArtifactCreationRequest
{
    public required Guid ProjectId { get; init; }
    public required Guid DryRunExecutionAuditId { get; init; }
    public required string DryRunAuditHash { get; init; }
    public required string DryRunReceiptHash { get; init; }
    public required string PatchArtifactKind { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required IReadOnlyList<PatchArtifactFileChange> FileChanges { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = PatchArtifactCreationBoundaryText.Boundary;
}

public sealed record PatchArtifactCreationResult
{
    public required Guid PatchArtifactId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid DryRunExecutionAuditId { get; init; }
    public required string DryRunAuditHash { get; init; }
    public required string DryRunReceiptHash { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required bool Stored { get; init; }
    public required PatchArtifact PatchArtifact { get; init; }
    public required string Boundary { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed record PatchArtifactCreationIssue(string Code, string Field, string Message);

public sealed class PatchArtifactCreationException : InvalidOperationException
{
    public PatchArtifactCreationException(IReadOnlyList<PatchArtifactCreationIssue> issues)
        : base($"Patch artifact creation failed: {string.Join(", ", issues.Select(issue => issue.Code))}")
    {
        Issues = issues;
    }

    public IReadOnlyList<PatchArtifactCreationIssue> Issues { get; }
}

public static class PatchArtifactCreationValidation
{
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
        "continues workflow",
        "workflow continued",
        "approves release",
        "release approved",
        "release ready"
    ];

    public static IReadOnlyList<PatchArtifactCreationIssue> ValidateRequest(PatchArtifactCreationRequest? request)
    {
        var issues = new List<PatchArtifactCreationIssue>();

        if (request is null)
        {
            Add(issues, "REQUEST_REQUIRED", "request", "Patch artifact creation request is required.");
            return issues;
        }

        if (request.ProjectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", nameof(request.ProjectId), "Project ID is required.");
        }

        if (request.DryRunExecutionAuditId == Guid.Empty)
        {
            Add(issues, "DRY_RUN_EXECUTION_AUDIT_ID_REQUIRED", nameof(request.DryRunExecutionAuditId), "Dry-run execution audit ID is required.");
        }

        RequireText(issues, request.DryRunAuditHash, nameof(request.DryRunAuditHash), "DRY_RUN_AUDIT_HASH_REQUIRED", "Dry-run audit hash is required.");
        RequireText(issues, request.DryRunReceiptHash, nameof(request.DryRunReceiptHash), "DRY_RUN_RECEIPT_HASH_REQUIRED", "Dry-run receipt hash is required.");
        RequireText(issues, request.PatchArtifactKind, nameof(request.PatchArtifactKind), "PATCH_ARTIFACT_KIND_REQUIRED", "Patch artifact kind is required.");
        RequireText(issues, request.SourceBaselineHash, nameof(request.SourceBaselineHash), "SOURCE_BASELINE_HASH_REQUIRED", "Source baseline hash is required.");
        RequireText(issues, request.Boundary, nameof(request.Boundary), "BOUNDARY_REQUIRED", "Boundary text is required.");
        RequireList(issues, request.FileChanges, nameof(request.FileChanges), "FILE_CHANGES_REQUIRED", "At least one file change is required.");
        RequireStringList(issues, request.EvidenceReferences, nameof(request.EvidenceReferences), "EVIDENCE_REFERENCES_REQUIRED", "At least one evidence reference is required.");
        RequireStringList(issues, request.BoundaryMaxims, nameof(request.BoundaryMaxims), "BOUNDARY_MAXIMS_REQUIRED", "At least one boundary maxim is required.");

        ValidateSafeText(issues, request.DryRunAuditHash, nameof(request.DryRunAuditHash));
        ValidateSafeText(issues, request.DryRunReceiptHash, nameof(request.DryRunReceiptHash));
        ValidateSafeText(issues, request.PatchArtifactKind, nameof(request.PatchArtifactKind));
        ValidateSafeText(issues, request.SourceBaselineHash, nameof(request.SourceBaselineHash));
        ValidateSafeText(issues, request.Boundary, nameof(request.Boundary));
        ValidateSafeList(issues, request.EvidenceReferences, nameof(request.EvidenceReferences));
        ValidateSafeList(issues, request.BoundaryMaxims, nameof(request.BoundaryMaxims));

        if (request.FileChanges is not null)
        {
            for (var index = 0; index < request.FileChanges.Count; index++)
            {
                ValidateFileChange(issues, request.FileChanges[index], $"{nameof(request.FileChanges)}[{index}]");
            }
        }

        return issues;
    }

    private static void ValidateFileChange(List<PatchArtifactCreationIssue> issues, PatchArtifactFileChange change, string fieldPrefix)
    {
        ValidateSafeText(issues, change.Path, $"{fieldPrefix}.{nameof(change.Path)}");
        ValidateSafeText(issues, change.PreviousPath, $"{fieldPrefix}.{nameof(change.PreviousPath)}");
        ValidateSafeText(issues, change.ChangeKind, $"{fieldPrefix}.{nameof(change.ChangeKind)}");
        ValidateSafeText(issues, change.BeforeContentHash, $"{fieldPrefix}.{nameof(change.BeforeContentHash)}");
        ValidateSafeText(issues, change.AfterContentHash, $"{fieldPrefix}.{nameof(change.AfterContentHash)}");
        ValidateSafeText(issues, change.DiffHash, $"{fieldPrefix}.{nameof(change.DiffHash)}");
        ValidateSafeText(issues, change.NormalizedDiff, $"{fieldPrefix}.{nameof(change.NormalizedDiff)}");
    }

    private static void RequireText(List<PatchArtifactCreationIssue> issues, string? value, string field, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireList<T>(List<PatchArtifactCreationIssue> issues, IReadOnlyList<T>? values, string field, string code, string message)
    {
        if (values is null || values.Count == 0)
        {
            Add(issues, code, field, message);
        }
    }

    private static void RequireStringList(List<PatchArtifactCreationIssue> issues, IReadOnlyList<string>? values, string field, string code, string message)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, code, field, message);
        }
    }

    private static void ValidateSafeList(List<PatchArtifactCreationIssue> issues, IReadOnlyList<string>? values, string field)
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

    private static void ValidateSafeText(List<PatchArtifactCreationIssue> issues, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var marker in PrivateMaterialMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "PRIVATE_OR_RAW_MATERIAL_REJECTED", field, $"Patch artifact creation request must not contain private or raw material: {marker}.");
            }
        }

        foreach (var marker in AuthorityClaimMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "AUTHORITY_CLAIM_REJECTED", field, $"Patch artifact creation request must not claim authority: {marker}.");
            }
        }
    }

    private static void Add(List<PatchArtifactCreationIssue> issues, string code, string field, string message) =>
        issues.Add(new PatchArtifactCreationIssue(code, field, message));
}
