using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public static class PatchArtifactHashing
{
    public static string ComputeFileChangeHash(PatchArtifactFileChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return Sha256Hex(Canonicalize(
            ("Path", change.Path),
            ("PreviousPath", change.PreviousPath),
            ("ChangeKind", change.ChangeKind),
            ("BeforeContentHash", change.BeforeContentHash),
            ("AfterContentHash", change.AfterContentHash),
            ("DiffHash", change.DiffHash),
            ("NormalizedDiff", change.NormalizedDiff),
            ("IsBinary", change.IsBinary ? "true" : "false")));
    }

    public static string ComputeChangeSetHash(IReadOnlyList<PatchArtifactFileChange> fileChanges)
    {
        ArgumentNullException.ThrowIfNull(fileChanges);

        var canonicalChanges = fileChanges
            .OrderBy(change => Normalize(change.Path), StringComparer.Ordinal)
            .ThenBy(change => Normalize(change.PreviousPath), StringComparer.Ordinal)
            .ThenBy(change => Normalize(change.ChangeKind), StringComparer.Ordinal)
            .Select(ComputeFileChangeHash);

        return Sha256Hex(Canonicalize(canonicalChanges.Select((hash, index) => ($"FileChange[{index}]", hash)).ToArray()));
    }

    public static string ComputePatchHash(PatchArtifact patchArtifact, string computedChangeSetHash)
    {
        ArgumentNullException.ThrowIfNull(patchArtifact);

        return Sha256Hex(Canonicalize(
            ("PatchArtifactId", patchArtifact.PatchArtifactId.ToString("D")),
            ("ProjectId", patchArtifact.ProjectId.ToString("D")),
            ("PatchArtifactKind", patchArtifact.PatchArtifactKind),
            ("ControlledDryRunRequestId", patchArtifact.ControlledDryRunRequestId.ToString("D")),
            ("DryRunExecutionAuditId", patchArtifact.DryRunExecutionAuditId.ToString("D")),
            ("DryRunAuditHash", patchArtifact.DryRunAuditHash),
            ("DryRunReceiptHash", patchArtifact.DryRunReceiptHash),
            ("PolicySatisfactionId", patchArtifact.PolicySatisfactionId.ToString("D")),
            ("PolicySatisfactionHash", patchArtifact.PolicySatisfactionHash),
            ("SubjectKind", patchArtifact.SubjectKind),
            ("SubjectId", patchArtifact.SubjectId),
            ("SubjectHash", patchArtifact.SubjectHash),
            ("SourceSnapshotReference", patchArtifact.SourceSnapshotReference),
            ("SourceBaselineHash", patchArtifact.SourceBaselineHash),
            ("WorkspaceBoundaryHash", patchArtifact.WorkspaceBoundaryHash),
            ("ValidationPlanId", patchArtifact.ValidationPlanId),
            ("ValidationPlanHash", patchArtifact.ValidationPlanHash),
            ("ComputedChangeSetHash", computedChangeSetHash),
            ("CreatedAtUtc", FormatTimestamp(patchArtifact.CreatedAtUtc)),
            ("ExpiresAtUtc", patchArtifact.ExpiresAtUtc.HasValue ? FormatTimestamp(patchArtifact.ExpiresAtUtc.Value) : string.Empty),
            ("EvidenceReferences", string.Join("\u001f", patchArtifact.EvidenceReferences.Select(Normalize).Order(StringComparer.Ordinal))),
            ("BoundaryMaxims", string.Join("\u001f", patchArtifact.BoundaryMaxims.Select(Normalize).Order(StringComparer.Ordinal))),
            ("Boundary", patchArtifact.Boundary)));
    }

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string Canonicalize(params (string Key, string? Value)[] values) =>
        string.Join("\n", values.Select(value => $"{value.Key}={Normalize(value.Value)}"));

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
