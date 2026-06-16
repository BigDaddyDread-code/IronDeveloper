using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public static class RollbackExecutionBoundaryText
{
    public const string Boundary = """
        RollbackExecutionReceipt records a controlled rollback execution attempt.
        RollbackExecutionReceipt is mutation evidence, not release approval.
        RollbackExecutionReceipt is not workflow continuation.
        RollbackExecutionReceipt is not policy satisfaction.
        RollbackExecutionReceipt is not source apply.
        RollbackExecutionReceipt does not authorize further source mutation.
        RollbackExecutionReceipt does not promote memory or activate retrieval.
        RollbackExecutionReceipt does not create repository commits, pushes, merges, branches, or pull requests.
        Rollback execution pulls the emergency brake. It does not declare the crash cleaned up.
        """;
}

public static class ControlledRollbackExecutionStatuses
{
    public const string RolledBack = "RolledBack";
    public const string Rejected = "Rejected";
    public const string PartialFailure = "PartialFailure";
}

public static class RollbackPlanFileActionKinds
{
    public const string RestoreModifiedFile = "RestoreModifiedFile";
    public const string DeleteCreatedFile = "DeleteCreatedFile";
    public const string RecreateDeletedFile = "RecreateDeletedFile";
    public const string RenameBack = "RenameBack";
    public const string Noop = "Noop";

    public static IReadOnlyList<string> Known { get; } =
    [
        RestoreModifiedFile,
        DeleteCreatedFile,
        RecreateDeletedFile,
        RenameBack,
        Noop
    ];
}

public sealed record ControlledRollbackExecutionRequest
{
    public required Guid ControlledRollbackExecutionRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required RollbackPlan RollbackPlan { get; init; }
    public required RollbackSupportReceipt RollbackSupportReceipt { get; init; }
    public required SourceApplyRequest SourceApplyRequest { get; init; }
    public required SourceApplyReceipt SourceApplyReceipt { get; init; }
    public required PatchArtifact PatchArtifact { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required string ApprovedWorkspaceBoundaryHash { get; init; }
    public required string ObservedBranch { get; init; }
    public required string ObservedSourceBaselineHash { get; init; }
    public required string ObservedCleanWorktreeHashBeforeRollback { get; init; }
    public required IReadOnlyList<ControlledRollbackContent> ApprovedContents { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
}

public sealed record ControlledRollbackContent
{
    public required string Path { get; init; }
    public required string ContentHash { get; init; }
    public required string Content { get; init; }
}

public sealed record ControlledRollbackExecutionResult
{
    public required string Status { get; init; }
    public required bool Succeeded { get; init; }
    public required bool MutationOccurred { get; init; }
    public required bool PartialRollbackOccurred { get; init; }
    public required IReadOnlyList<ControlledRollbackExecutionIssue> Issues { get; init; }
    public required IReadOnlyList<RollbackExecutionReceiptFileResult> FileResults { get; init; }
    public RollbackExecutionReceipt? Receipt { get; init; }
}

public sealed record ControlledRollbackExecutionIssue(string Code, string Field, string Message);

public sealed record RollbackExecutionReceipt
{
    public required Guid RollbackExecutionReceiptId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ControlledRollbackExecutionRequestId { get; init; }
    public required Guid RollbackPlanId { get; init; }
    public required string RollbackPlanHash { get; init; }
    public required Guid RollbackSupportReceiptId { get; init; }
    public required string RollbackSupportReceiptHash { get; init; }
    public required Guid SourceApplyRequestId { get; init; }
    public required string SourceApplyRequestHash { get; init; }
    public required Guid SourceApplyReceiptId { get; init; }
    public required string SourceApplyReceiptHash { get; init; }
    public required Guid PatchArtifactId { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public required string ObservedBranch { get; init; }
    public required string ObservedSourceBaselineHash { get; init; }
    public required string ObservedCleanWorktreeHashBeforeRollback { get; init; }
    public required string ObservedCleanWorktreeHashAfterRollback { get; init; }
    public required bool MutationOccurred { get; init; }
    public required bool RollbackSucceeded { get; init; }
    public required bool PartialRollbackOccurred { get; init; }
    public required IReadOnlyList<RollbackExecutionReceiptFileResult> FileResults { get; init; }
    public required IReadOnlyList<string> IssueCodes { get; init; }
    public required DateTimeOffset RolledBackAtUtc { get; init; }
    public required string RollbackExecutionReceiptHash { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = RollbackExecutionBoundaryText.Boundary;
}

public sealed record RollbackExecutionReceiptFileResult
{
    public required string Path { get; init; }
    public string? PreviousPath { get; init; }
    public required string OperationKind { get; init; }
    public required string PatchArtifactChangeHash { get; init; }
    public required string RollbackActionHash { get; init; }
    public string? BeforeContentHash { get; init; }
    public string? AfterContentHash { get; init; }
    public required bool PreconditionsSatisfied { get; init; }
    public required bool MutationApplied { get; init; }
    public required bool Restored { get; init; }
    public required bool Deleted { get; init; }
    public required bool Recreated { get; init; }
    public required bool RenamedBack { get; init; }
    public required bool Noop { get; init; }
    public required IReadOnlyList<string> IssueCodes { get; init; }
    public required string FileResultHash { get; init; }
}

public sealed record RollbackExecutionReceiptValidationIssue(string Code, string Field, string Message);

public sealed record RollbackExecutionReceiptValidationResult(IReadOnlyList<RollbackExecutionReceiptValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public static class RollbackExecutionReceiptValidation
{
    private static readonly string[] UnsafeMarkers =
    [
        "private reasoning",
        "hidden reasoning",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "scratchpad",
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "entire patch",
        "entirepatch",
        "patch payload",
        "approval granted",
        "policy satisfied",
        "execution allowed",
        "workflow continued",
        "release approved",
        "release ready",
        "memory promoted",
        "retrieval activated",
        "gitcommitted",
        "gitpushed",
        "pull request created",
        "source applied"
    ];

    public static RollbackExecutionReceiptValidationResult Validate(RollbackExecutionReceipt? receipt)
    {
        var issues = new List<RollbackExecutionReceiptValidationIssue>();
        if (receipt is null)
        {
            issues.Add(new("ReceiptRequired", nameof(receipt), "Rollback execution receipt is required."));
            return new(issues);
        }

        RequireGuid(receipt.RollbackExecutionReceiptId, nameof(receipt.RollbackExecutionReceiptId), issues);
        RequireGuid(receipt.ProjectId, nameof(receipt.ProjectId), issues);
        RequireGuid(receipt.ControlledRollbackExecutionRequestId, nameof(receipt.ControlledRollbackExecutionRequestId), issues);
        RequireGuid(receipt.RollbackPlanId, nameof(receipt.RollbackPlanId), issues);
        RequireGuid(receipt.RollbackSupportReceiptId, nameof(receipt.RollbackSupportReceiptId), issues);
        RequireGuid(receipt.SourceApplyRequestId, nameof(receipt.SourceApplyRequestId), issues);
        RequireGuid(receipt.SourceApplyReceiptId, nameof(receipt.SourceApplyReceiptId), issues);
        RequireGuid(receipt.PatchArtifactId, nameof(receipt.PatchArtifactId), issues);

        RequireHash(receipt.RollbackPlanHash, nameof(receipt.RollbackPlanHash), issues);
        RequireHash(receipt.RollbackSupportReceiptHash, nameof(receipt.RollbackSupportReceiptHash), issues);
        RequireHash(receipt.SourceApplyRequestHash, nameof(receipt.SourceApplyRequestHash), issues);
        RequireHash(receipt.SourceApplyReceiptHash, nameof(receipt.SourceApplyReceiptHash), issues);
        RequireHash(receipt.PatchHash, nameof(receipt.PatchHash), issues);
        RequireHash(receipt.ChangeSetHash, nameof(receipt.ChangeSetHash), issues);
        RequireHash(receipt.SourceBaselineHash, nameof(receipt.SourceBaselineHash), issues);
        RequireHash(receipt.WorkspaceBoundaryHash, nameof(receipt.WorkspaceBoundaryHash), issues);
        RequireHash(receipt.ExpectedCleanWorktreeHash, nameof(receipt.ExpectedCleanWorktreeHash), issues);
        RequireHash(receipt.ObservedSourceBaselineHash, nameof(receipt.ObservedSourceBaselineHash), issues);
        RequireHash(receipt.ObservedCleanWorktreeHashBeforeRollback, nameof(receipt.ObservedCleanWorktreeHashBeforeRollback), issues);
        RequireHash(receipt.ObservedCleanWorktreeHashAfterRollback, nameof(receipt.ObservedCleanWorktreeHashAfterRollback), issues);
        RequireHash(receipt.RollbackExecutionReceiptHash, nameof(receipt.RollbackExecutionReceiptHash), issues);
        RequireText(receipt.ExpectedBranch, nameof(receipt.ExpectedBranch), issues);
        RequireText(receipt.ObservedBranch, nameof(receipt.ObservedBranch), issues);

        if (receipt.PartialRollbackOccurred && receipt.RollbackSucceeded)
        {
            issues.Add(new("PartialCannotBeSucceeded", nameof(receipt.PartialRollbackOccurred), "A partial rollback execution receipt cannot be successful."));
        }

        if (receipt.PartialRollbackOccurred && !receipt.MutationOccurred)
        {
            issues.Add(new("PartialRequiresMutation", nameof(receipt.PartialRollbackOccurred), "A partial rollback execution receipt must record that mutation started."));
        }

        if (receipt.FileResults.Count == 0)
        {
            issues.Add(new("FileResultsRequired", nameof(receipt.FileResults), "Rollback execution receipt must include file results."));
        }

        foreach (var result in receipt.FileResults.Select((value, index) => (value, index)))
        {
            ValidateFileResult(result.value, result.index, issues);
        }

        if (receipt.EvidenceReferences.Count == 0)
        {
            issues.Add(new("EvidenceReferencesRequired", nameof(receipt.EvidenceReferences), "Rollback execution receipt evidence references are required."));
        }

        if (receipt.BoundaryMaxims.Count == 0)
        {
            issues.Add(new("BoundaryMaximsRequired", nameof(receipt.BoundaryMaxims), "Rollback execution receipt boundary maxims are required."));
        }

        ValidateTexts(receipt.IssueCodes, nameof(receipt.IssueCodes), issues);
        ValidateTexts(receipt.EvidenceReferences, nameof(receipt.EvidenceReferences), issues);
        ValidateTexts(receipt.BoundaryMaxims, nameof(receipt.BoundaryMaxims), issues);
        ValidateText(receipt.Boundary, nameof(receipt.Boundary), issues);

        return new(issues);
    }

    private static void ValidateFileResult(RollbackExecutionReceiptFileResult result, int index, List<RollbackExecutionReceiptValidationIssue> issues)
    {
        var prefix = $"FileResults[{index}]";
        RequireText(result.Path, $"{prefix}.Path", issues);
        RequireText(result.OperationKind, $"{prefix}.OperationKind", issues);
        RequireHash(result.PatchArtifactChangeHash, $"{prefix}.PatchArtifactChangeHash", issues);
        RequireHash(result.RollbackActionHash, $"{prefix}.RollbackActionHash", issues);
        RequireHash(result.FileResultHash, $"{prefix}.FileResultHash", issues);
        if (!RollbackPlanFileActionKinds.Known.Contains(result.OperationKind, StringComparer.Ordinal))
        {
            issues.Add(new("InvalidOperationKind", $"{prefix}.OperationKind", "Rollback execution operation kind is invalid."));
        }

        if (!string.IsNullOrWhiteSpace(result.BeforeContentHash))
        {
            RequireHash(result.BeforeContentHash, $"{prefix}.BeforeContentHash", issues);
        }

        if (!string.IsNullOrWhiteSpace(result.AfterContentHash))
        {
            RequireHash(result.AfterContentHash, $"{prefix}.AfterContentHash", issues);
        }

        ValidateText(result.Path, $"{prefix}.Path", issues);
        ValidateText(result.PreviousPath, $"{prefix}.PreviousPath", issues);
        ValidateTexts(result.IssueCodes, $"{prefix}.IssueCodes", issues);
    }

    private static void RequireGuid(Guid value, string field, List<RollbackExecutionReceiptValidationIssue> issues)
    {
        if (value == Guid.Empty)
        {
            issues.Add(new("Required", field, "Value is required."));
        }
    }

    private static void RequireText(string? value, string field, List<RollbackExecutionReceiptValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new("Required", field, "Value is required."));
            return;
        }

        ValidateText(value, field, issues);
    }

    private static void RequireHash(string? value, string field, List<RollbackExecutionReceiptValidationIssue> issues)
    {
        RequireText(value, field, issues);
        if (!string.IsNullOrWhiteSpace(value) && !value.Trim().StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new("InvalidHash", field, "Hash must use sha256: prefix."));
        }
    }

    private static void ValidateTexts(IEnumerable<string> values, string field, List<RollbackExecutionReceiptValidationIssue> issues)
    {
        foreach (var value in values.Select((value, index) => (value, index)))
        {
            RequireText(value.value, $"{field}[{value.index}]", issues);
        }
    }

    private static void ValidateText(string? value, string field, List<RollbackExecutionReceiptValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (UnsafeMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
        {
            issues.Add(new("UnsafeText", field, "Rollback execution receipt text must not contain raw/private reasoning or authority markers."));
        }
    }
}

public static class RollbackExecutionReceiptHashing
{
    public static string ComputeContentHash(string content) => Sha256Hex(content ?? string.Empty);

    public static string ComputeFileResultHash(RollbackExecutionReceiptFileResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Sha256Hex(Canonicalize(
            ("Path", result.Path),
            ("PreviousPath", result.PreviousPath),
            ("OperationKind", result.OperationKind),
            ("PatchArtifactChangeHash", result.PatchArtifactChangeHash),
            ("RollbackActionHash", result.RollbackActionHash),
            ("BeforeContentHash", result.BeforeContentHash),
            ("AfterContentHash", result.AfterContentHash),
            ("PreconditionsSatisfied", result.PreconditionsSatisfied ? "true" : "false"),
            ("MutationApplied", result.MutationApplied ? "true" : "false"),
            ("Restored", result.Restored ? "true" : "false"),
            ("Deleted", result.Deleted ? "true" : "false"),
            ("Recreated", result.Recreated ? "true" : "false"),
            ("RenamedBack", result.RenamedBack ? "true" : "false"),
            ("Noop", result.Noop ? "true" : "false"),
            ("IssueCodes", string.Join("\u001f", result.IssueCodes.Select(Normalize).Order(StringComparer.Ordinal)))));
    }

    public static string ComputeReceiptHash(RollbackExecutionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return Sha256Hex(Canonicalize(
            ("RollbackExecutionReceiptId", receipt.RollbackExecutionReceiptId.ToString("D")),
            ("ProjectId", receipt.ProjectId.ToString("D")),
            ("ControlledRollbackExecutionRequestId", receipt.ControlledRollbackExecutionRequestId.ToString("D")),
            ("RollbackPlanId", receipt.RollbackPlanId.ToString("D")),
            ("RollbackPlanHash", receipt.RollbackPlanHash),
            ("RollbackSupportReceiptId", receipt.RollbackSupportReceiptId.ToString("D")),
            ("RollbackSupportReceiptHash", receipt.RollbackSupportReceiptHash),
            ("SourceApplyRequestId", receipt.SourceApplyRequestId.ToString("D")),
            ("SourceApplyRequestHash", receipt.SourceApplyRequestHash),
            ("SourceApplyReceiptId", receipt.SourceApplyReceiptId.ToString("D")),
            ("SourceApplyReceiptHash", receipt.SourceApplyReceiptHash),
            ("PatchArtifactId", receipt.PatchArtifactId.ToString("D")),
            ("PatchHash", receipt.PatchHash),
            ("ChangeSetHash", receipt.ChangeSetHash),
            ("SourceBaselineHash", receipt.SourceBaselineHash),
            ("WorkspaceBoundaryHash", receipt.WorkspaceBoundaryHash),
            ("ExpectedBranch", receipt.ExpectedBranch),
            ("ExpectedCleanWorktreeHash", receipt.ExpectedCleanWorktreeHash),
            ("ObservedBranch", receipt.ObservedBranch),
            ("ObservedSourceBaselineHash", receipt.ObservedSourceBaselineHash),
            ("ObservedCleanWorktreeHashBeforeRollback", receipt.ObservedCleanWorktreeHashBeforeRollback),
            ("ObservedCleanWorktreeHashAfterRollback", receipt.ObservedCleanWorktreeHashAfterRollback),
            ("MutationOccurred", receipt.MutationOccurred ? "true" : "false"),
            ("RollbackSucceeded", receipt.RollbackSucceeded ? "true" : "false"),
            ("PartialRollbackOccurred", receipt.PartialRollbackOccurred ? "true" : "false"),
            ("FileResults", string.Join("\u001f", receipt.FileResults.Select(result => result.FileResultHash).Order(StringComparer.Ordinal))),
            ("IssueCodes", string.Join("\u001f", receipt.IssueCodes.Select(Normalize).Order(StringComparer.Ordinal))),
            ("RolledBackAtUtc", receipt.RolledBackAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            ("EvidenceReferences", string.Join("\u001f", receipt.EvidenceReferences.Select(Normalize).Order(StringComparer.Ordinal))),
            ("BoundaryMaxims", string.Join("\u001f", receipt.BoundaryMaxims.Select(Normalize).Order(StringComparer.Ordinal))),
            ("Boundary", receipt.Boundary)));
    }

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string Canonicalize(params (string Key, string? Value)[] values) =>
        string.Join("\n", values.Select(value => $"{value.Key}={Normalize(value.Value)}"));

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}

public interface IRollbackExecutionReceiptStore
{
    Task SaveAsync(RollbackExecutionReceipt receipt, CancellationToken cancellationToken = default);

    Task<RollbackExecutionReceipt?> GetAsync(Guid projectId, Guid rollbackExecutionReceiptId, CancellationToken cancellationToken = default);

    Task<RollbackExecutionReceipt?> GetByReceiptHashAsync(Guid projectId, string rollbackExecutionReceiptHash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackExecutionReceipt>> ListBySourceApplyReceiptAsync(Guid projectId, Guid sourceApplyReceiptId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackExecutionReceipt>> ListByRollbackPlanAsync(Guid projectId, Guid rollbackPlanId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackExecutionReceipt>> ListByRollbackSupportReceiptAsync(Guid projectId, Guid rollbackSupportReceiptId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RollbackExecutionReceipt>> ListByPatchArtifactAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default);
}

public interface IControlledRollbackExecutor
{
    Task<ControlledRollbackExecutionResult> RollbackAsync(ControlledRollbackExecutionRequest request, CancellationToken cancellationToken = default);
}
