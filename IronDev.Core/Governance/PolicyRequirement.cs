using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public sealed record PolicyRequirement
{
    public required Guid ProjectId { get; init; }
    public required string PolicyCode { get; init; }
    public required string PolicyVersion { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }
    public required string CapabilityCode { get; init; }
    public required string ApprovalTargetKind { get; init; }
    public required string ApprovalTargetId { get; init; }
    public required string ApprovalTargetHash { get; init; }
    public required string ApprovalPurpose { get; init; }
    public required string ApprovalRequirementHash { get; init; }
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public IReadOnlyList<string> RequiredEvidenceReferences { get; init; } = [];
    public IReadOnlyList<string> RequiredBoundaryMaxims { get; init; } = [];
}

public static class PolicyRequirementHash
{
    public static string Compute(PolicyRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        var canonical = string.Join(
            "\n",
            requirement.ProjectId.ToString("D"),
            Normalize(requirement.PolicyCode),
            Normalize(requirement.PolicyVersion),
            Normalize(requirement.SubjectKind),
            Normalize(requirement.SubjectId),
            Normalize(requirement.SubjectHash),
            Normalize(requirement.CapabilityCode),
            Normalize(requirement.ApprovalTargetKind),
            Normalize(requirement.ApprovalTargetId),
            Normalize(requirement.ApprovalTargetHash),
            Normalize(requirement.ApprovalPurpose));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
