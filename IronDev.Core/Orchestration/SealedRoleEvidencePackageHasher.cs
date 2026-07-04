using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Orchestration;

public static class SealedRoleEvidencePackageHasher
{
    public static string ComputePreCriticEvidenceHash(SealedRoleEvidencePackage package) =>
        ComputePreCriticEvidenceHash(
            package.OrchestratorContract,
            package.TesterCoveragePackage,
            package.BuilderPatchPackage);

    public static string ComputePreCriticEvidenceHash(
        RoleArtifactRef orchestratorContract,
        RoleArtifactRef testerCoveragePackage,
        RoleArtifactRef builderPatchPackage)
    {
        var lines = new List<string>();

        AppendArtifact(lines, "orchestrator", orchestratorContract);
        AppendArtifact(lines, "tester", testerCoveragePackage);
        AppendArtifact(lines, "builder", builderPatchPackage);

        return HashLines(lines);
    }

    public static string ComputeFinalSealHash(SealedRoleEvidencePackage package)
    {
        var lines = new List<string>
        {
            Canonical("package.id", package.PackageId),
            Canonical("ticket.id", package.TicketId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Canonical("project.id", package.ProjectId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Canonical("run.id", package.RunId),
            Canonical("contract.id", package.ContractId),
            Canonical("contract.hash", package.ContractHash)
        };

        AppendArtifact(lines, "orchestrator", package.OrchestratorContract);
        AppendArtifact(lines, "tester", package.TesterCoveragePackage);
        AppendArtifact(lines, "builder", package.BuilderPatchPackage);
        lines.Add(Canonical("precritic.hash", package.PreCriticEvidenceHash));

        foreach (var review in package.CriticReviews.OrderBy(review => review.ReviewId, StringComparer.Ordinal))
        {
            lines.Add(Canonical("critic.review.id", review.ReviewId));
            lines.Add(Canonical("critic.review.hash", review.Sha256));
            lines.Add(Canonical("critic.reviewed.package.hash", review.ReviewedPackageHash));
            foreach (var findingId in review.FindingIds.OrderBy(id => id, StringComparer.Ordinal))
            {
                lines.Add(Canonical("critic.finding.id", findingId));
            }
        }

        foreach (var disposition in package.FindingDispositions
            .OrderBy(disposition => disposition.FindingId, StringComparer.Ordinal)
            .ThenBy(disposition => disposition.Sha256, StringComparer.Ordinal))
        {
            lines.Add(Canonical("disposition.finding.id", disposition.FindingId));
            lines.Add(Canonical("disposition.kind", disposition.Disposition));
            lines.Add(Canonical("disposition.reason", disposition.Reason));
            lines.Add(Canonical("disposition.user", disposition.DecidedByUserId));
            lines.Add(Canonical("disposition.hash", disposition.Sha256));
        }

        foreach (var risk in package.KnownRisks.OrderBy(risk => risk, StringComparer.Ordinal))
        {
            lines.Add(Canonical("known.risk", risk));
        }

        foreach (var gap in package.KnownGaps.OrderBy(gap => gap, StringComparer.Ordinal))
        {
            lines.Add(Canonical("known.gap", gap));
        }

        return HashLines(lines);
    }

    private static void AppendArtifact(List<string> lines, string prefix, RoleArtifactRef artifact)
    {
        lines.Add(Canonical($"{prefix}.artifact.id", artifact.ArtifactId));
        lines.Add(Canonical($"{prefix}.artifact.kind", artifact.ArtifactKind));
        lines.Add(Canonical($"{prefix}.artifact.role", artifact.ProducedByRole));
        lines.Add(Canonical($"{prefix}.artifact.agent", artifact.ProducedByAgentId));
        lines.Add(Canonical($"{prefix}.artifact.hash", artifact.Sha256));
        lines.Add(Canonical($"{prefix}.artifact.evidence", artifact.EvidenceRef));
    }

    private static string HashLines(IReadOnlyList<string> lines)
    {
        var canonical = string.Join('\n', lines);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Canonical(string key, string? value)
    {
        value ??= string.Empty;
        return string.Concat(key, ":", value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), ":", value);
    }
}
