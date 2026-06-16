using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public static class SourceApplyReceiptBoundaryText
{
    public const string Boundary = """
        SourceApplyReceipt records a controlled real source apply attempt.
        SourceApplyReceipt is mutation evidence, not release approval.
        SourceApplyReceipt is not workflow continuation.
        SourceApplyReceipt is not policy satisfaction.
        SourceApplyReceipt is not rollback execution.
        SourceApplyReceipt does not authorize further source mutation.
        SourceApplyReceipt does not promote memory or activate retrieval.
        SourceApplyReceipt does not create git commits, pushes, merges, branches, or pull requests.
        """;
}

public static class ControlledSourceApplyStatuses
{
    public const string Applied = "Applied";
    public const string Rejected = "Rejected";
    public const string PartialFailure = "PartialFailure";
}

public sealed record ControlledSourceApplyRequest
{
    public required Guid ControlledSourceApplyRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required SourceApplyRequest SourceApplyRequest { get; init; }
    public required SourceApplyDryRunReceipt SourceApplyDryRunReceipt { get; init; }
    public required PatchArtifact PatchArtifact { get; init; }
    public required RollbackSupportReceipt RollbackSupportReceipt { get; init; }
    public required string WorkspaceRoot { get; init; }
    public required string ApprovedWorkspaceBoundaryHash { get; init; }
    public required string ObservedBranch { get; init; }
    public required string ObservedSourceBaselineHash { get; init; }
    public required string ObservedCleanWorktreeHashBeforeApply { get; init; }
    public required IReadOnlyList<ControlledSourceApplyContent> ApprovedContents { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
}

public sealed record ControlledSourceApplyContent
{
    public required string Path { get; init; }
    public required string AfterContentHash { get; init; }
    public required string Content { get; init; }
}

public sealed record ControlledSourceApplyResult
{
    public required string Status { get; init; }
    public required bool Succeeded { get; init; }
    public required bool MutationOccurred { get; init; }
    public required bool PartialApplyOccurred { get; init; }
    public required IReadOnlyList<ControlledSourceApplyIssue> Issues { get; init; }
    public required IReadOnlyList<SourceApplyReceiptFileResult> FileResults { get; init; }
    public SourceApplyReceipt? Receipt { get; init; }
}

public sealed record ControlledSourceApplyIssue(string Code, string Field, string Message);

public sealed record SourceApplyReceipt
{
    public required Guid SourceApplyReceiptId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid ControlledSourceApplyRequestId { get; init; }
    public required Guid SourceApplyRequestId { get; init; }
    public required string SourceApplyRequestHash { get; init; }
    public required Guid SourceApplyDryRunReceiptId { get; init; }
    public required string SourceApplyDryRunReceiptHash { get; init; }
    public required Guid SourceApplyGateEvaluationId { get; init; }
    public required string SourceApplyGateEvaluationHash { get; init; }
    public required Guid PatchArtifactId { get; init; }
    public required string PatchHash { get; init; }
    public required string ChangeSetHash { get; init; }
    public required Guid RollbackSupportReceiptId { get; init; }
    public required string RollbackSupportReceiptHash { get; init; }
    public required string SourceBaselineHash { get; init; }
    public required string WorkspaceBoundaryHash { get; init; }
    public required string ExpectedBranch { get; init; }
    public required string ExpectedCleanWorktreeHash { get; init; }
    public required string ObservedBranch { get; init; }
    public required string ObservedCleanWorktreeHashBeforeApply { get; init; }
    public required string ObservedCleanWorktreeHashAfterApply { get; init; }
    public required bool MutationOccurred { get; init; }
    public required bool ApplySucceeded { get; init; }
    public required bool PartialApplyOccurred { get; init; }
    public required IReadOnlyList<SourceApplyReceiptFileResult> FileResults { get; init; }
    public required IReadOnlyList<string> IssueCodes { get; init; }
    public required DateTimeOffset AppliedAtUtc { get; init; }
    public required string SourceApplyReceiptHash { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = SourceApplyReceiptBoundaryText.Boundary;
}

public sealed record SourceApplyReceiptFileResult
{
    public required string Path { get; init; }
    public string? PreviousPath { get; init; }
    public required string OperationKind { get; init; }
    public required string PatchArtifactChangeHash { get; init; }
    public required string OperationHash { get; init; }
    public string? BeforeContentHash { get; init; }
    public string? AfterContentHash { get; init; }
    public required bool PreconditionsSatisfied { get; init; }
    public required bool MutationApplied { get; init; }
    public required bool Created { get; init; }
    public required bool Modified { get; init; }
    public required bool Deleted { get; init; }
    public required bool Renamed { get; init; }
    public required bool Noop { get; init; }
    public required IReadOnlyList<string> IssueCodes { get; init; }
    public required string FileResultHash { get; init; }
}

public sealed record SourceApplyReceiptValidationIssue(string Code, string Field, string Message);

public sealed record SourceApplyReceiptValidationResult(IReadOnlyList<SourceApplyReceiptValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public static class SourceApplyReceiptValidation
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
        "git committed",
        "git pushed",
        "pull request created",
        "rollback executed"
    ];

    public static SourceApplyReceiptValidationResult Validate(SourceApplyReceipt? receipt)
    {
        var issues = new List<SourceApplyReceiptValidationIssue>();
        if (receipt is null)
        {
            issues.Add(new("ReceiptRequired", nameof(receipt), "Source apply receipt is required."));
            return new(issues);
        }

        RequireGuid(receipt.SourceApplyReceiptId, nameof(receipt.SourceApplyReceiptId), issues);
        RequireGuid(receipt.ProjectId, nameof(receipt.ProjectId), issues);
        RequireGuid(receipt.ControlledSourceApplyRequestId, nameof(receipt.ControlledSourceApplyRequestId), issues);
        RequireGuid(receipt.SourceApplyRequestId, nameof(receipt.SourceApplyRequestId), issues);
        RequireGuid(receipt.SourceApplyDryRunReceiptId, nameof(receipt.SourceApplyDryRunReceiptId), issues);
        RequireGuid(receipt.SourceApplyGateEvaluationId, nameof(receipt.SourceApplyGateEvaluationId), issues);
        RequireGuid(receipt.PatchArtifactId, nameof(receipt.PatchArtifactId), issues);
        RequireGuid(receipt.RollbackSupportReceiptId, nameof(receipt.RollbackSupportReceiptId), issues);

        RequireHash(receipt.SourceApplyRequestHash, nameof(receipt.SourceApplyRequestHash), issues);
        RequireHash(receipt.SourceApplyDryRunReceiptHash, nameof(receipt.SourceApplyDryRunReceiptHash), issues);
        RequireHash(receipt.SourceApplyGateEvaluationHash, nameof(receipt.SourceApplyGateEvaluationHash), issues);
        RequireHash(receipt.PatchHash, nameof(receipt.PatchHash), issues);
        RequireHash(receipt.ChangeSetHash, nameof(receipt.ChangeSetHash), issues);
        RequireHash(receipt.RollbackSupportReceiptHash, nameof(receipt.RollbackSupportReceiptHash), issues);
        RequireHash(receipt.SourceBaselineHash, nameof(receipt.SourceBaselineHash), issues);
        RequireHash(receipt.WorkspaceBoundaryHash, nameof(receipt.WorkspaceBoundaryHash), issues);
        RequireHash(receipt.ExpectedCleanWorktreeHash, nameof(receipt.ExpectedCleanWorktreeHash), issues);
        RequireHash(receipt.ObservedCleanWorktreeHashBeforeApply, nameof(receipt.ObservedCleanWorktreeHashBeforeApply), issues);
        RequireHash(receipt.ObservedCleanWorktreeHashAfterApply, nameof(receipt.ObservedCleanWorktreeHashAfterApply), issues);
        RequireHash(receipt.SourceApplyReceiptHash, nameof(receipt.SourceApplyReceiptHash), issues);
        RequireText(receipt.ExpectedBranch, nameof(receipt.ExpectedBranch), issues);
        RequireText(receipt.ObservedBranch, nameof(receipt.ObservedBranch), issues);

        if (receipt.PartialApplyOccurred && receipt.ApplySucceeded)
        {
            issues.Add(new("PartialCannotBeSucceeded", nameof(receipt.PartialApplyOccurred), "A partial source apply receipt cannot be successful."));
        }

        if (receipt.PartialApplyOccurred && !receipt.MutationOccurred)
        {
            issues.Add(new("PartialRequiresMutation", nameof(receipt.PartialApplyOccurred), "A partial source apply receipt must record that mutation started."));
        }

        if (receipt.FileResults.Count == 0)
        {
            issues.Add(new("FileResultsRequired", nameof(receipt.FileResults), "Source apply receipt must include file results."));
        }

        foreach (var result in receipt.FileResults.Select((value, index) => (value, index)))
        {
            ValidateFileResult(result.value, result.index, issues);
        }

        if (receipt.EvidenceReferences.Count == 0)
        {
            issues.Add(new("EvidenceReferencesRequired", nameof(receipt.EvidenceReferences), "Source apply receipt evidence references are required."));
        }

        if (receipt.BoundaryMaxims.Count == 0)
        {
            issues.Add(new("BoundaryMaximsRequired", nameof(receipt.BoundaryMaxims), "Source apply receipt boundary maxims are required."));
        }

        ValidateTexts(receipt.IssueCodes, nameof(receipt.IssueCodes), issues);
        ValidateTexts(receipt.EvidenceReferences, nameof(receipt.EvidenceReferences), issues);
        ValidateTexts(receipt.BoundaryMaxims, nameof(receipt.BoundaryMaxims), issues);

        return new(issues);
    }

    private static void ValidateFileResult(SourceApplyReceiptFileResult result, int index, List<SourceApplyReceiptValidationIssue> issues)
    {
        var prefix = $"FileResults[{index}]";
        RequireText(result.Path, $"{prefix}.Path", issues);
        RequireText(result.OperationKind, $"{prefix}.OperationKind", issues);
        RequireHash(result.PatchArtifactChangeHash, $"{prefix}.PatchArtifactChangeHash", issues);
        RequireHash(result.OperationHash, $"{prefix}.OperationHash", issues);
        RequireHash(result.FileResultHash, $"{prefix}.FileResultHash", issues);
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

    private static void RequireGuid(Guid value, string field, List<SourceApplyReceiptValidationIssue> issues)
    {
        if (value == Guid.Empty)
        {
            issues.Add(new("Required", field, "Value is required."));
        }
    }

    private static void RequireText(string? value, string field, List<SourceApplyReceiptValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new("Required", field, "Value is required."));
            return;
        }

        ValidateText(value, field, issues);
    }

    private static void RequireHash(string? value, string field, List<SourceApplyReceiptValidationIssue> issues)
    {
        RequireText(value, field, issues);
        if (!string.IsNullOrWhiteSpace(value) && !value.Trim().StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new("InvalidHash", field, "Hash must use sha256: prefix."));
        }
    }

    private static void ValidateTexts(IEnumerable<string> values, string field, List<SourceApplyReceiptValidationIssue> issues)
    {
        foreach (var value in values.Select((value, index) => (value, index)))
        {
            RequireText(value.value, $"{field}[{value.index}]", issues);
        }
    }

    private static void ValidateText(string? value, string field, List<SourceApplyReceiptValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (UnsafeMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
        {
            issues.Add(new("UnsafeText", field, "Source apply receipt text must not contain raw/private reasoning or authority markers."));
        }
    }
}

public static class SourceApplyReceiptHashing
{
    public static string ComputeContentHash(string content) => Sha256Hex(content ?? string.Empty);

    public static string ComputeFileResultHash(SourceApplyReceiptFileResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Sha256Hex(Canonicalize(
            ("Path", result.Path),
            ("PreviousPath", result.PreviousPath),
            ("OperationKind", result.OperationKind),
            ("PatchArtifactChangeHash", result.PatchArtifactChangeHash),
            ("OperationHash", result.OperationHash),
            ("BeforeContentHash", result.BeforeContentHash),
            ("AfterContentHash", result.AfterContentHash),
            ("PreconditionsSatisfied", result.PreconditionsSatisfied ? "true" : "false"),
            ("MutationApplied", result.MutationApplied ? "true" : "false"),
            ("Created", result.Created ? "true" : "false"),
            ("Modified", result.Modified ? "true" : "false"),
            ("Deleted", result.Deleted ? "true" : "false"),
            ("Renamed", result.Renamed ? "true" : "false"),
            ("Noop", result.Noop ? "true" : "false"),
            ("IssueCodes", string.Join("\u001f", result.IssueCodes.Select(Normalize).Order(StringComparer.Ordinal)))));
    }

    public static string ComputeReceiptHash(SourceApplyReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return Sha256Hex(Canonicalize(
            ("SourceApplyReceiptId", receipt.SourceApplyReceiptId.ToString("D")),
            ("ProjectId", receipt.ProjectId.ToString("D")),
            ("ControlledSourceApplyRequestId", receipt.ControlledSourceApplyRequestId.ToString("D")),
            ("SourceApplyRequestId", receipt.SourceApplyRequestId.ToString("D")),
            ("SourceApplyRequestHash", receipt.SourceApplyRequestHash),
            ("SourceApplyDryRunReceiptId", receipt.SourceApplyDryRunReceiptId.ToString("D")),
            ("SourceApplyDryRunReceiptHash", receipt.SourceApplyDryRunReceiptHash),
            ("SourceApplyGateEvaluationId", receipt.SourceApplyGateEvaluationId.ToString("D")),
            ("SourceApplyGateEvaluationHash", receipt.SourceApplyGateEvaluationHash),
            ("PatchArtifactId", receipt.PatchArtifactId.ToString("D")),
            ("PatchHash", receipt.PatchHash),
            ("ChangeSetHash", receipt.ChangeSetHash),
            ("RollbackSupportReceiptId", receipt.RollbackSupportReceiptId.ToString("D")),
            ("RollbackSupportReceiptHash", receipt.RollbackSupportReceiptHash),
            ("SourceBaselineHash", receipt.SourceBaselineHash),
            ("WorkspaceBoundaryHash", receipt.WorkspaceBoundaryHash),
            ("ExpectedBranch", receipt.ExpectedBranch),
            ("ExpectedCleanWorktreeHash", receipt.ExpectedCleanWorktreeHash),
            ("ObservedBranch", receipt.ObservedBranch),
            ("ObservedCleanWorktreeHashBeforeApply", receipt.ObservedCleanWorktreeHashBeforeApply),
            ("ObservedCleanWorktreeHashAfterApply", receipt.ObservedCleanWorktreeHashAfterApply),
            ("MutationOccurred", receipt.MutationOccurred ? "true" : "false"),
            ("ApplySucceeded", receipt.ApplySucceeded ? "true" : "false"),
            ("PartialApplyOccurred", receipt.PartialApplyOccurred ? "true" : "false"),
            ("FileResults", string.Join("\u001f", receipt.FileResults.Select(result => result.FileResultHash).Order(StringComparer.Ordinal))),
            ("IssueCodes", string.Join("\u001f", receipt.IssueCodes.Select(Normalize).Order(StringComparer.Ordinal))),
            ("AppliedAtUtc", receipt.AppliedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
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

public interface ISourceApplyReceiptStore
{
    Task SaveAsync(SourceApplyReceipt receipt, CancellationToken cancellationToken = default);

    Task<SourceApplyReceipt?> GetAsync(Guid projectId, Guid sourceApplyReceiptId, CancellationToken cancellationToken = default);

    Task<SourceApplyReceipt?> GetByReceiptHashAsync(Guid projectId, string sourceApplyReceiptHash, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceApplyReceipt>> ListBySourceApplyRequestAsync(Guid projectId, Guid sourceApplyRequestId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceApplyReceipt>> ListBySourceApplyDryRunReceiptAsync(Guid projectId, Guid sourceApplyDryRunReceiptId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceApplyReceipt>> ListByPatchArtifactAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceApplyReceipt>> ListByRollbackSupportReceiptAsync(Guid projectId, Guid rollbackSupportReceiptId, CancellationToken cancellationToken = default);
}

public interface IControlledSourceApplyExecutor
{
    Task<ControlledSourceApplyResult> ApplyAsync(ControlledSourceApplyRequest request, CancellationToken cancellationToken = default);
}
