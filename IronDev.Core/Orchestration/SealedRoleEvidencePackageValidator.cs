namespace IronDev.Core.Orchestration;

public static class SealedRoleEvidencePackageValidator
{
    public const string PackageRequired = "SEALED_ROLE_PACKAGE_REQUIRED";
    public const string IdentityRequired = "SEALED_ROLE_PACKAGE_IDENTITY_REQUIRED";
    public const string BoundaryMissing = "SEALED_ROLE_PACKAGE_BOUNDARY_MISSING";
    public const string ArtifactRequired = "SEALED_ROLE_PACKAGE_ARTIFACT_REQUIRED";
    public const string ArtifactHashRequired = "SEALED_ROLE_PACKAGE_ARTIFACT_HASH_REQUIRED";
    public const string ArtifactRoleMismatch = "SEALED_ROLE_PACKAGE_ARTIFACT_ROLE_MISMATCH";
    public const string PreCriticHashRequired = "SEALED_ROLE_PACKAGE_PRECRITIC_HASH_REQUIRED";
    public const string CriticReviewRequired = "SEALED_ROLE_PACKAGE_CRITIC_REVIEW_REQUIRED";
    public const string CriticReviewHashMismatch = "SEALED_ROLE_PACKAGE_CRITIC_REVIEW_HASH_MISMATCH";
    public const string FindingDispositionMissing = "SEALED_ROLE_PACKAGE_FINDING_DISPOSITION_MISSING";
    public const string UnknownFindingDisposition = "SEALED_ROLE_PACKAGE_UNKNOWN_FINDING_DISPOSITION";
    public const string DispositionReasonRequired = "SEALED_ROLE_PACKAGE_DISPOSITION_REASON_REQUIRED";
    public const string FinalSealHashRequired = "SEALED_ROLE_PACKAGE_FINAL_SEAL_HASH_REQUIRED";
    public const string AuthorityClaim = "SEALED_ROLE_PACKAGE_AUTHORITY_CLAIM";
    public const string RuntimeSurfaceForbidden = "SEALED_ROLE_PACKAGE_RUNTIME_SURFACE_FORBIDDEN";

    private static readonly string[] BoundaryFragments =
    [
        "tamper-evident review bundle",
        "not approval",
        "not test proof",
        "not critic authority",
        "not policy satisfaction",
        "not workflow continuation",
        "not source apply permission",
        "not release readiness",
        "not deployment readiness"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        string.Concat("Approval", "Granted"),
        string.Concat("Policy", "Satisfied"),
        string.Concat("Ready", "To", "Apply"),
        string.Concat("Ready", "To", "Release"),
        string.Concat("Ready", "To", "Deploy"),
        string.Concat("Release", "Ready"),
        string.Concat("Deployment", "Ready"),
        string.Concat("Source", "Apply", "Authorized"),
        string.Concat("Workflow", "Continuation", "Authorized"),
        string.Concat("Critic", "Satisfied"),
        string.Concat("Tests", "Passed"),
        string.Concat("Test", "Proof"),
        string.Concat("Contract", "Satisfied"),
        "approved",
        "approval granted",
        "policy satisfied",
        "ready to apply",
        "ready to release",
        "ready to deploy",
        "release ready",
        "deployment ready",
        "source apply authorized",
        "workflow continuation authorized",
        "critic satisfied",
        "tests passed",
        "test proof",
        "contract satisfied"
    ];

    public static SealedRoleEvidencePackageValidationResult Validate(SealedRoleEvidencePackage? package)
    {
        var result = new SealedRoleEvidencePackageValidationResult();
        if (package is null)
        {
            AddIssue(result, PackageRequired);
            return result;
        }

        ValidateIdentity(package, result);
        ValidateBoundary(package.Boundary, result);
        ValidateAuthorityClaims(result,
            package.PackageId,
            package.RunId,
            package.ContractId,
            package.ContractHash,
            package.PreCriticEvidenceHash,
            package.FinalSealHash);
        ValidateAuthorityClaims(result, package.KnownRisks, package.KnownGaps);

        ValidateArtifact(
            package.OrchestratorContract,
            SealedRoleArtifactKinds.OrchestratorContract,
            SealedRoleArtifactRoles.Orchestrator,
            result);
        ValidateArtifact(
            package.TesterCoveragePackage,
            SealedRoleArtifactKinds.TesterCoveragePackage,
            SealedRoleArtifactRoles.Tester,
            result);
        ValidateArtifact(
            package.BuilderPatchPackage,
            SealedRoleArtifactKinds.BuilderPatchPackage,
            SealedRoleArtifactRoles.Builder,
            result);

        if (string.IsNullOrWhiteSpace(package.PreCriticEvidenceHash))
            AddIssue(result, PreCriticHashRequired);

        ValidateCriticReviews(package, result);
        ValidateFindingDispositions(package, result);

        if (string.IsNullOrWhiteSpace(package.FinalSealHash))
            AddIssue(result, FinalSealHashRequired);

        return result;
    }

    private static void ValidateIdentity(SealedRoleEvidencePackage package, SealedRoleEvidencePackageValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(package.PackageId) ||
            package.TicketId <= 0 ||
            package.ProjectId <= 0 ||
            string.IsNullOrWhiteSpace(package.RunId) ||
            string.IsNullOrWhiteSpace(package.ContractId) ||
            string.IsNullOrWhiteSpace(package.ContractHash))
        {
            AddIssue(result, IdentityRequired);
        }
    }

    private static void ValidateArtifact(
        RoleArtifactRef artifact,
        string expectedKind,
        string expectedRole,
        SealedRoleEvidencePackageValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(artifact.ArtifactId) ||
            string.IsNullOrWhiteSpace(artifact.ArtifactKind) ||
            string.IsNullOrWhiteSpace(artifact.ProducedByRole) ||
            string.IsNullOrWhiteSpace(artifact.ProducedByAgentId) ||
            string.IsNullOrWhiteSpace(artifact.EvidenceRef))
        {
            AddIssue(result, ArtifactRequired);
        }

        if (string.IsNullOrWhiteSpace(artifact.Sha256))
            AddIssue(result, ArtifactHashRequired);

        if (!string.IsNullOrWhiteSpace(artifact.ArtifactKind) &&
            !string.Equals(artifact.ArtifactKind, expectedKind, StringComparison.Ordinal))
        {
            AddIssue(result, ArtifactRoleMismatch);
        }

        if (!string.IsNullOrWhiteSpace(artifact.ProducedByRole) &&
            !string.Equals(artifact.ProducedByRole, expectedRole, StringComparison.Ordinal))
        {
            AddIssue(result, ArtifactRoleMismatch);
        }

        ValidateAuthorityClaims(
            result,
            artifact.ArtifactId,
            artifact.ArtifactKind,
            artifact.ProducedByRole,
            artifact.ProducedByAgentId,
            artifact.Sha256,
            artifact.EvidenceRef);
    }

    private static void ValidateCriticReviews(
        SealedRoleEvidencePackage package,
        SealedRoleEvidencePackageValidationResult result)
    {
        if (package.CriticReviews.Count == 0)
        {
            AddIssue(result, CriticReviewRequired);
            return;
        }

        foreach (var review in package.CriticReviews)
        {
            if (string.IsNullOrWhiteSpace(review.ReviewId) ||
                string.IsNullOrWhiteSpace(review.CriticAgentRunId) ||
                string.IsNullOrWhiteSpace(review.CriticAgentId) ||
                string.IsNullOrWhiteSpace(review.EvidenceRef) ||
                string.IsNullOrWhiteSpace(review.Sha256) ||
                string.IsNullOrWhiteSpace(review.Verdict))
            {
                AddIssue(result, CriticReviewRequired);
            }

            if (!string.Equals(review.ReviewedPackageHash, package.PreCriticEvidenceHash, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(review.ReviewedPackageHash))
            {
                AddIssue(result, CriticReviewHashMismatch);
            }

            ValidateNestedBoundary(review.Boundary, result);
            ValidateAuthorityClaims(
                result,
                review.ReviewId,
                review.CriticAgentRunId,
                review.CriticAgentId,
                review.ReviewedPackageHash,
                review.Verdict,
                review.EvidenceRef,
                review.Sha256);
            ValidateAuthorityClaims(result, review.FindingIds);
        }
    }

    private static void ValidateFindingDispositions(
        SealedRoleEvidencePackage package,
        SealedRoleEvidencePackageValidationResult result)
    {
        var findingIds = package.CriticReviews
            .SelectMany(review => review.FindingIds)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        var dispositionFindingIds = package.FindingDispositions
            .Where(disposition => !string.IsNullOrWhiteSpace(disposition.FindingId))
            .Select(disposition => disposition.FindingId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var findingId in findingIds)
        {
            if (!dispositionFindingIds.Contains(findingId))
                AddIssue(result, FindingDispositionMissing);
        }

        foreach (var disposition in package.FindingDispositions)
        {
            if (string.IsNullOrWhiteSpace(disposition.FindingId) || !findingIds.Contains(disposition.FindingId))
                AddIssue(result, UnknownFindingDisposition);

            if (string.IsNullOrWhiteSpace(disposition.Disposition) ||
                string.IsNullOrWhiteSpace(disposition.Reason) ||
                string.IsNullOrWhiteSpace(disposition.DecidedByUserId) ||
                string.IsNullOrWhiteSpace(disposition.EvidenceRef) ||
                string.IsNullOrWhiteSpace(disposition.Sha256))
            {
                AddIssue(result, DispositionReasonRequired);
            }

            ValidateNestedBoundary(disposition.Boundary, result);
            ValidateAuthorityClaims(
                result,
                disposition.FindingId,
                disposition.Disposition,
                disposition.Reason,
                disposition.DecidedByUserId,
                disposition.EvidenceRef,
                disposition.Sha256);
        }
    }

    private static void ValidateBoundary(string boundary, SealedRoleEvidencePackageValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(boundary))
        {
            AddIssue(result, BoundaryMissing);
            return;
        }

        foreach (var fragment in BoundaryFragments)
        {
            if (!boundary.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                AddIssue(result, BoundaryMissing);
        }
    }

    private static void ValidateNestedBoundary(string boundary, SealedRoleEvidencePackageValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(boundary))
            AddIssue(result, BoundaryMissing);
    }

    private static void ValidateAuthorityClaims(SealedRoleEvidencePackageValidationResult result, params IReadOnlyList<string>[] valueSets)
    {
        foreach (var valueSet in valueSets)
        {
            ValidateAuthorityClaims(result, valueSet.ToArray());
        }
    }

    private static void ValidateAuthorityClaims(SealedRoleEvidencePackageValidationResult result, params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            foreach (var marker in AuthorityMarkers)
            {
                if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    AddIssue(result, AuthorityClaim);
            }
        }
    }

    private static void AddIssue(SealedRoleEvidencePackageValidationResult result, string code)
    {
        if (!result.Issues.Contains(code, StringComparer.Ordinal))
            result.Issues.Add(code);
    }
}
