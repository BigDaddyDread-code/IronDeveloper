using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.Governance;

public static class DisposableWorkspacePatchPackageBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static DisposableWorkspacePatchPackageResult Build(DisposableWorkspacePatchPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = ProposalOnlyRunProfileEvaluator.Evaluate(new ProposalOnlyRunProfileEvaluationRequest
        {
            OperationId = request.OperationId,
            OperationKind = ProposalOnlyOperationKinds.PatchPackageWrite,
            Subject = Subject(request),
            RepoId = request.RepoId,
            Branch = request.Branch,
            EvidenceRefs = request.ValidationRefs,
            RequestedPaths = [request.WorkspacePath, request.OutputPath],
            ExpiresAtUtc = request.ExpiresAtUtc,
            ObservedAtUtc = request.ObservedAtUtc
        });
        var validation = DisposableWorkspacePatchPackageValidator.Validate(request);
        var preIssues = profile.Issues
            .Concat(profile.RedFlags)
            .Concat(validation.Issues)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!profile.IsAllowed || !validation.CanPackage)
        {
            var blocked = BuildBlockedStatus(request, "Disposable workspace patch package cannot be completed.", preIssues);
            return Result(
                isPackageCreated: false,
                statusMapping: blocked,
                packageId: EmptyPackageId(request),
                patchHash: "sha256:missing-patch-diff",
                packagePath: string.Empty,
                artifactRefs: [],
                validationRefs: request.ValidationRefs,
                issues: preIssues,
                redFlags: profile.RedFlags);
        }

        var patchText = File.ReadAllText(validation.PatchPath, Encoding.UTF8);
        var patchHash = HashText(patchText);
        var packageId = PackageId(request.ProposalId, patchHash);
        var packagePath = Path.Combine(validation.OutputRootPath, packageId);
        Directory.CreateDirectory(packagePath);

        var packagePatchPath = Path.Combine(packagePath, "patch.diff");
        File.WriteAllText(packagePatchPath, patchText, Encoding.UTF8);

        var artifactRefs = ArtifactRefs(packageId, patchHash);
        var hasValidation = request.ValidationRefs.Any(value => !string.IsNullOrWhiteSpace(value));
        var statusMapping = hasValidation
            ? BuildCompletedStatus(request, patchHash, artifactRefs)
            : BuildBlockedMissingValidationStatus(request, patchHash, artifactRefs);

        var manifest = new DisposableWorkspacePatchPackageManifest
        {
            PackageId = packageId,
            ProposalId = request.ProposalId,
            RepoId = request.RepoId,
            Branch = request.Branch,
            WorkspaceId = validation.Marker?.WorkspaceId ?? "unknown-workspace",
            PatchHash = patchHash,
            ArtifactRefs = artifactRefs,
            ValidationRefs = Clean(request.ValidationRefs),
            ForbiddenActions = statusMapping.Status.ForbiddenActions,
            CreatedAtUtc = request.ObservedAtUtc
        };

        File.WriteAllText(Path.Combine(packagePath, "review-summary.md"), RenderReviewSummary(request, manifest), Encoding.UTF8);
        File.WriteAllText(Path.Combine(packagePath, "known-risks.md"), RenderKnownRisks(request), Encoding.UTF8);
        File.WriteAllText(Path.Combine(packagePath, "validation-summary.md"), RenderValidationSummary(request), Encoding.UTF8);
        File.WriteAllText(Path.Combine(packagePath, "patch-package-manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8);
        File.WriteAllText(Path.Combine(packagePath, "operation-status.json"), JsonSerializer.Serialize(statusMapping.Status, JsonOptions), Encoding.UTF8);

        var issues = preIssues
            .Concat(statusMapping.Issues)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var redFlags = profile.RedFlags
            .Concat(statusMapping.RedFlags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Result(
            isPackageCreated: true,
            statusMapping: statusMapping,
            packageId: packageId,
            patchHash: patchHash,
            packagePath: packagePath,
            artifactRefs: artifactRefs,
            validationRefs: request.ValidationRefs,
            issues: issues,
            redFlags: redFlags);
    }

    private static PatchProposalGovernedOperationStatusMappingResult BuildCompletedStatus(
        DisposableWorkspacePatchPackageRequest request,
        string patchHash,
        IReadOnlyList<string> artifactRefs) =>
        PatchProposalGovernedOperationStatusMapper.Map(new PatchProposalStatusInput
        {
            OperationId = request.OperationId,
            ProposalId = request.ProposalId,
            PatchHash = patchHash,
            Subject = Subject(request),
            StatusKind = PatchProposalStatusKind.ReadyForReview,
            ArtifactRefs = artifactRefs,
            ValidationRefs = Clean(request.ValidationRefs),
            BlockedReasons = [],
            MissingEvidence = [],
            ForbiddenActions = [],
            ExpiresAtUtc = request.ExpiresAtUtc,
            ObservedAtUtc = request.ObservedAtUtc
        });

    private static PatchProposalGovernedOperationStatusMappingResult BuildBlockedMissingValidationStatus(
        DisposableWorkspacePatchPackageRequest request,
        string patchHash,
        IReadOnlyList<string> artifactRefs) =>
        PatchProposalGovernedOperationStatusMapper.Map(new PatchProposalStatusInput
        {
            OperationId = request.OperationId,
            ProposalId = request.ProposalId,
            PatchHash = patchHash,
            Subject = Subject(request),
            StatusKind = PatchProposalStatusKind.Blocked,
            ArtifactRefs = artifactRefs,
            ValidationRefs = [],
            BlockedReasons = ["Validation evidence is missing."],
            MissingEvidence = ["validation-result:proposal-only"],
            ForbiddenActions = [],
            ExpiresAtUtc = request.ExpiresAtUtc,
            ObservedAtUtc = request.ObservedAtUtc
        });

    private static PatchProposalGovernedOperationStatusMappingResult BuildBlockedStatus(
        DisposableWorkspacePatchPackageRequest request,
        string blockedReason,
        IReadOnlyList<string> missingEvidence) =>
        PatchProposalGovernedOperationStatusMapper.Map(new PatchProposalStatusInput
        {
            OperationId = request.OperationId,
            ProposalId = string.IsNullOrWhiteSpace(request.ProposalId) ? "missing-proposal-id" : request.ProposalId,
            PatchHash = "missing-patch-diff",
            Subject = Subject(request),
            StatusKind = PatchProposalStatusKind.Blocked,
            ArtifactRefs = [],
            ValidationRefs = Clean(request.ValidationRefs),
            BlockedReasons = [blockedReason],
            MissingEvidence = missingEvidence.Count == 0 ? ["disposable-workspace-package-evidence"] : missingEvidence,
            ForbiddenActions = [],
            ExpiresAtUtc = request.ExpiresAtUtc,
            ObservedAtUtc = request.ObservedAtUtc
        });

    private static DisposableWorkspacePatchPackageResult Result(
        bool isPackageCreated,
        PatchProposalGovernedOperationStatusMappingResult statusMapping,
        string packageId,
        string patchHash,
        string packagePath,
        IReadOnlyList<string> artifactRefs,
        IReadOnlyList<string> validationRefs,
        IReadOnlyList<string> issues,
        IReadOnlyList<string> redFlags) =>
        new()
        {
            IsPackageCreated = isPackageCreated,
            Status = statusMapping.Status,
            StatusValidation = statusMapping.CanonicalValidation,
            PackageId = packageId,
            PatchHash = patchHash,
            PackagePath = packagePath,
            ArtifactRefs = artifactRefs,
            ValidationRefs = Clean(validationRefs),
            Issues = issues
                .Concat(statusMapping.Issues)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            RedFlags = redFlags
                .Concat(statusMapping.RedFlags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

    private static IReadOnlyList<string> ArtifactRefs(string packageId, string patchHash) =>
    [
        $"patch-package:{packageId}",
        $"patch-artifact:{packageId}",
        $"patch-hash:{patchHash}",
        $"review-summary:{packageId}",
        $"known-risks:{packageId}",
        $"validation-summary:{packageId}",
        $"patch-package-manifest:{packageId}",
        $"operation-status:{packageId}"
    ];

    private static string RenderReviewSummary(DisposableWorkspacePatchPackageRequest request, DisposableWorkspacePatchPackageManifest manifest) =>
        string.Join(Environment.NewLine,
        [
            "# Disposable Workspace Patch Package Review Summary",
            string.Empty,
            $"Task: {request.TaskSummary}",
            $"Repository: {request.RepoId}",
            $"Branch: {request.Branch}",
            $"Proposal: {request.ProposalId}",
            $"Package: {manifest.PackageId}",
            $"Patch hash: {manifest.PatchHash}",
            string.Empty,
            "Artifact refs:",
            .. manifest.ArtifactRefs.Select(value => $"- {value}"),
            string.Empty,
            "Validation refs:",
            .. (manifest.ValidationRefs.Count == 0
                ? ["- Validation not supplied for this package."]
                : manifest.ValidationRefs.Select(value => $"- {value}")),
            string.Empty,
            "Next safe action:",
            $"- request controlled source apply for patch hash {manifest.PatchHash}",
            string.Empty,
            "Forbidden actions:",
            .. manifest.ForbiddenActions.Select(value => $"- {value}")
        ]);

    private static string RenderKnownRisks(DisposableWorkspacePatchPackageRequest request) =>
        string.Join(Environment.NewLine,
        [
            "# Known Risks",
            string.Empty,
            $"Proposal: {request.ProposalId}",
            "- source apply not performed",
            "- commit not performed",
            "- push not performed",
            "- PR not created",
            "- validation missing or validation refs listed",
            "- manual review required",
            "- ProposalOnly evidence is not approval",
            "- patch package is not policy approval",
            "- memory promotion not performed",
            "- workflow continuation not performed"
        ]);

    private static string RenderValidationSummary(DisposableWorkspacePatchPackageRequest request) =>
        Clean(request.ValidationRefs).Count == 0
            ? string.Join(Environment.NewLine,
            [
                "# Validation Summary",
                string.Empty,
                "Validation not supplied for this package.",
                "Patch package is reviewable evidence only.",
                "Controlled source apply must not infer validation from package existence."
            ])
            : string.Join(Environment.NewLine,
            [
                "# Validation Summary",
                string.Empty,
                .. Clean(request.ValidationRefs).Select(value => $"- {value}"),
                string.Empty,
                "Validation refs are evidence only."
            ]);

    private static string HashText(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string PackageId(string proposalId, string patchHash) =>
        $"patch-package-{Sanitize(proposalId)}-{patchHash[^12..]}";

    private static string EmptyPackageId(DisposableWorkspacePatchPackageRequest request) =>
        $"patch-package-{Sanitize(string.IsNullOrWhiteSpace(request.ProposalId) ? "missing-proposal" : request.ProposalId)}-blocked";

    private static string Subject(DisposableWorkspacePatchPackageRequest request) =>
        $"repo:{request.RepoId} branch:{request.Branch} proposal:{request.ProposalId}";

    private static string Sanitize(string value)
    {
        var chars = value
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')
            .ToArray();
        var sanitized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "proposal" : sanitized.ToLowerInvariant();
    }

    private static IReadOnlyList<string> Clean(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
