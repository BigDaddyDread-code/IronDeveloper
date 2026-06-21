using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.Governance;

public static class ValidationResultPackageBuilder
{
    public const string OperationKind = "ValidationResultPackage";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ValidationResultPackageResult Build(ValidationResultPackageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profile = ProposalOnlyRunProfileEvaluator.Evaluate(new ProposalOnlyRunProfileEvaluationRequest
        {
            OperationId = request.OperationId,
            OperationKind = ProposalOnlyOperationKinds.DisposableWorkspaceValidate,
            Subject = Subject(request),
            RepoId = request.RepoId,
            Branch = request.Branch,
            EvidenceRefs = request.EvidenceFileNames,
            RequestedPaths = [request.WorkspacePath, request.OutputPath],
            ExpiresAtUtc = request.ExpiresAtUtc,
            ObservedAtUtc = request.ObservedAtUtc
        });
        var validation = ValidationResultPackageValidator.Validate(request);
        var preIssues = profile.Issues
            .Concat(profile.RedFlags)
            .Concat(validation.Issues)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!profile.IsAllowed || !validation.CanPackage)
        {
            var blockedStatus = BuildStatus(
                request,
                EmptyPackageId(request),
                validationRef: "validation-result:blocked",
                artifactRefs: [],
                evidenceFileNames: [],
                stateOverride: GovernedOperationState.Blocked,
                blockedReasons: ["Validation result package cannot be completed."],
                missingEvidence: preIssues.Length == 0 ? ["validation-result-package-evidence"] : preIssues);

            return Result(
                isPackageCreated: false,
                status: blockedStatus,
                packageId: EmptyPackageId(request),
                packagePath: string.Empty,
                validationRef: "validation-result:blocked",
                artifactRefs: [],
                issues: preIssues,
                redFlags: profile.RedFlags);
        }

        var packageId = PackageId(request, validation.EvidenceFiles);
        var validationRef = $"validation-result:{packageId}";
        var packagePath = Path.Combine(validation.OutputRootPath, packageId);
        var evidencePath = Path.Combine(packagePath, "evidence");
        Directory.CreateDirectory(evidencePath);

        CopyEvidenceFiles(validation.EvidenceFiles, evidencePath);

        var artifactRefs = ArtifactRefs(packageId, validationRef, validation.EvidenceFiles);
        var status = BuildStatus(
            request,
            packageId,
            validationRef,
            artifactRefs,
            validation.EvidenceFiles.Select(file => file.FileName).ToArray(),
            stateOverride: null,
            blockedReasons: [],
            missingEvidence: []);

        var manifest = new ValidationResultPackageManifest
        {
            PackageId = packageId,
            ValidationRunId = request.ValidationRunId,
            ValidationName = request.ValidationName,
            ProposalId = request.ProposalId,
            PatchHash = request.PatchHash,
            RepoId = request.RepoId,
            Branch = request.Branch,
            WorkspaceId = validation.Marker?.WorkspaceId ?? "unknown-workspace",
            Outcome = request.Outcome,
            ValidationRef = validationRef,
            ArtifactRefs = artifactRefs,
            EvidenceFileNames = validation.EvidenceFiles.Select(file => file.FileName).ToArray(),
            ValidationMessages = Clean(request.ValidationMessages),
            ForbiddenActions = status.ForbiddenActions,
            CreatedAtUtc = request.ObservedAtUtc
        };

        File.WriteAllText(Path.Combine(packagePath, "validation-summary.md"), RenderSummary(request, manifest), Encoding.UTF8);
        File.WriteAllText(Path.Combine(packagePath, "validation-evidence.md"), RenderEvidence(manifest), Encoding.UTF8);
        File.WriteAllText(Path.Combine(packagePath, "validation-result-package-manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8);
        File.WriteAllText(Path.Combine(packagePath, "operation-status.json"), JsonSerializer.Serialize(status, JsonOptions), Encoding.UTF8);

        return Result(
            isPackageCreated: true,
            status: status,
            packageId: packageId,
            packagePath: packagePath,
            validationRef: validationRef,
            artifactRefs: artifactRefs,
            issues: [],
            redFlags: profile.RedFlags);
    }

    private static GovernedOperationStatus BuildStatus(
        ValidationResultPackageRequest request,
        string packageId,
        string validationRef,
        IReadOnlyList<string> artifactRefs,
        IReadOnlyList<string> evidenceFileNames,
        GovernedOperationState? stateOverride,
        IReadOnlyList<string> blockedReasons,
        IReadOnlyList<string> missingEvidence)
    {
        var state = stateOverride ?? request.Outcome switch
        {
            ValidationOutcome.Passed => GovernedOperationState.Completed,
            ValidationOutcome.Failed => GovernedOperationState.Failed,
            ValidationOutcome.Inconclusive => GovernedOperationState.Blocked,
            _ => GovernedOperationState.Blocked
        };

        var evidenceRefs = Clean(
        [
            "run-profile:ProposalOnly",
            validationRef,
            $"validation-outcome:{OutcomeRef(request.Outcome)}",
            Ref("proposal", request.ProposalId),
            Ref("patch-hash", request.PatchHash),
            .. artifactRefs,
            .. evidenceFileNames.Select(file => $"validation-evidence-file:{file}")
        ]);

        return new GovernedOperationStatus
        {
            OperationId = request.OperationId,
            OperationKind = OperationKind,
            Subject = Subject(request),
            State = state,
            BlockedReasons = BuildBlockedReasons(request, state, blockedReasons),
            MissingEvidence = BuildMissingEvidence(request, state, missingEvidence),
            NextSafeActions = BuildNextSafeActions(request, state, validationRef),
            ForbiddenActions = BuildForbiddenActions(request, state),
            EvidenceRefs = evidenceRefs,
            ReceiptRefs = [Ref("validation-result-package", packageId)],
            ExpiresAtUtc = request.ExpiresAtUtc,
            ObservedAtUtc = request.ObservedAtUtc
        };
    }

    private static IReadOnlyList<string> BuildBlockedReasons(
        ValidationResultPackageRequest request,
        GovernedOperationState state,
        IReadOnlyList<string> blockedReasons)
    {
        if (blockedReasons.Count > 0)
            return Clean(blockedReasons);

        return state switch
        {
            GovernedOperationState.Failed => ["Validation failed."],
            GovernedOperationState.Blocked when request.Outcome == ValidationOutcome.Inconclusive =>
                ["Validation evidence is inconclusive."],
            GovernedOperationState.Blocked => ["Validation result package cannot be completed."],
            _ => []
        };
    }

    private static IReadOnlyList<string> BuildMissingEvidence(
        ValidationResultPackageRequest request,
        GovernedOperationState state,
        IReadOnlyList<string> missingEvidence)
    {
        if (missingEvidence.Count > 0)
            return Clean(missingEvidence);

        return state == GovernedOperationState.Blocked && request.Outcome == ValidationOutcome.Inconclusive
            ? [$"conclusive-validation-result:{Display(request.ValidationRunId, "missing-validation-run")}"]
            : [];
    }

    private static IReadOnlyList<string> BuildNextSafeActions(
        ValidationResultPackageRequest request,
        GovernedOperationState state,
        string validationRef) =>
        state switch
        {
            GovernedOperationState.Completed =>
            [
                $"package {validationRef} with the disposable workspace patch package"
            ],
            GovernedOperationState.Failed =>
            [
                "review validation evidence and prepare a new governed proposal"
            ],
            GovernedOperationState.Blocked when request.Outcome == ValidationOutcome.Inconclusive =>
            [
                "collect conclusive validation evidence"
            ],
            GovernedOperationState.Blocked =>
            [
                "collect missing validation result package evidence"
            ],
            _ => []
        };

    private static IReadOnlyList<string> BuildForbiddenActions(ValidationResultPackageRequest request, GovernedOperationState state)
    {
        var common = new[]
        {
            "do not treat validation result as approval",
            "do not treat validation result as policy satisfaction",
            "do not treat validation result as source apply authority",
            "do not commit, push, create PRs, merge, release, deploy, use memory promotion, or use workflow continuation from validation result"
        };

        return state switch
        {
            GovernedOperationState.Failed => Clean(
            [
                .. common,
                "do not request source apply from failed validation",
                "do not treat failed validation package as patch readiness",
                "do not treat validation failure as rollback authority"
            ]),
            GovernedOperationState.Blocked when request.Outcome == ValidationOutcome.Inconclusive => Clean(
            [
                .. common,
                "do not infer validation pass from inconclusive evidence",
                "do not use inconclusive validation as continuation authority"
            ]),
            _ => Clean(common)
        };
    }

    private static IReadOnlyList<string> ArtifactRefs(
        string packageId,
        string validationRef,
        IReadOnlyList<ValidationEvidenceFile> evidenceFiles) =>
        Clean(
        [
            validationRef,
            $"validation-result-package:{packageId}",
            $"validation-summary:{packageId}",
            $"validation-evidence:{packageId}",
            $"validation-result-package-manifest:{packageId}",
            $"operation-status:{packageId}",
            .. evidenceFiles.Select(file => $"validation-evidence-file:{file.FileName}")
        ]);

    private static void CopyEvidenceFiles(IReadOnlyList<ValidationEvidenceFile> evidenceFiles, string evidencePath)
    {
        foreach (var evidenceFile in evidenceFiles)
        {
            var destination = Path.Combine(evidencePath, evidenceFile.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(evidenceFile.FullPath, destination, overwrite: true);
        }
    }

    private static ValidationResultPackageResult Result(
        bool isPackageCreated,
        GovernedOperationStatus status,
        string packageId,
        string packagePath,
        string validationRef,
        IReadOnlyList<string> artifactRefs,
        IReadOnlyList<string> issues,
        IReadOnlyList<string> redFlags)
    {
        var validation = GovernedOperationStatusValidator.Validate(status);
        return new ValidationResultPackageResult
        {
            IsPackageCreated = isPackageCreated,
            Status = status,
            StatusValidation = validation,
            PackageId = packageId,
            PackagePath = packagePath,
            ValidationRef = validationRef,
            Outcome = status.EvidenceRefs.Any(value => value.Equals("validation-outcome:failed", StringComparison.OrdinalIgnoreCase))
                ? ValidationOutcome.Failed
                : status.EvidenceRefs.Any(value => value.Equals("validation-outcome:inconclusive", StringComparison.OrdinalIgnoreCase))
                    ? ValidationOutcome.Inconclusive
                    : ValidationOutcome.Passed,
            ArtifactRefs = artifactRefs,
            Issues = issues
                .Concat(validation.Issues)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            RedFlags = redFlags
                .Concat(validation.RedFlags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static string RenderSummary(ValidationResultPackageRequest request, ValidationResultPackageManifest manifest)
    {
        string[] outcomeLines = request.Outcome switch
        {
            ValidationOutcome.Passed =>
            new[]
            {
                "Validation passed is evidence only.",
                "Validation passed is not approval.",
                "Validation passed is not policy satisfaction.",
                "Validation passed is not source apply authority."
            },
            ValidationOutcome.Failed =>
            new[]
            {
                "Validation failed.",
                "Patch package must not be treated as ready for source apply."
            },
            ValidationOutcome.Inconclusive =>
            new[]
            {
                "Validation was inconclusive.",
                "Additional validation evidence is required before source apply can be requested."
            },
            _ => ["Validation outcome is invalid."]
        };

        return string.Join(Environment.NewLine,
        [
            "# Validation Summary",
            string.Empty,
            $"Validation name: {request.ValidationName}",
            $"Validation run id: {request.ValidationRunId}",
            $"Proposal id: {request.ProposalId}",
            $"Patch hash: {request.PatchHash}",
            $"Repository: {request.RepoId}",
            $"Branch: {request.Branch}",
            $"Outcome: {request.Outcome}",
            string.Empty,
            "Evidence files:",
            .. manifest.EvidenceFileNames.Select(file => $"- {file}"),
            string.Empty,
            "Validation messages:",
            .. (manifest.ValidationMessages.Count == 0
                ? ["- No validation messages supplied."]
                : manifest.ValidationMessages.Select(message => $"- {message}")),
            string.Empty,
            "Boundary reminders:",
            .. outcomeLines.Select(line => $"- {line}"),
            "- Validation result package is evidence only.",
            "- NextSafeActions are guidance only."
        ]);
    }

    private static string RenderEvidence(ValidationResultPackageManifest manifest) =>
        string.Join(Environment.NewLine,
        [
            "# Validation Evidence",
            string.Empty,
            "Copied evidence files:",
            .. manifest.EvidenceFileNames.Select(file => $"- evidence/{file}"),
            string.Empty,
            "Validation messages:",
            .. (manifest.ValidationMessages.Count == 0
                ? ["- No validation messages supplied."]
                : manifest.ValidationMessages.Select(message => $"- {message}")),
            string.Empty,
            "This file lists supplied evidence. It does not invent validation claims."
        ]);

    private static string PackageId(ValidationResultPackageRequest request, IReadOnlyList<ValidationEvidenceFile> evidenceFiles)
    {
        var seed = string.Join("\n",
        [
            request.ValidationRunId,
            request.ValidationName,
            request.ProposalId,
            request.PatchHash,
            request.Outcome.ToString(),
            .. evidenceFiles
                .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(file => $"{file.FileName}:{HashText(File.ReadAllText(file.FullPath, Encoding.UTF8))}")
        ]);
        var hash = HashText(seed);
        return $"validation-package-{Sanitize(request.ValidationRunId)}-{hash[^12..]}";
    }

    private static string EmptyPackageId(ValidationResultPackageRequest request) =>
        $"validation-package-{Sanitize(string.IsNullOrWhiteSpace(request.ValidationRunId) ? "missing-validation-run" : request.ValidationRunId)}-blocked";

    private static string HashText(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string Ref(string prefix, string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $"{prefix}:{value.Trim()}";

    private static string Subject(ValidationResultPackageRequest request) =>
        $"repo:{request.RepoId} branch:{request.Branch} proposal:{request.ProposalId} validation:{request.ValidationRunId}";

    private static string Display(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string OutcomeRef(ValidationOutcome outcome) =>
        outcome.ToString().ToLowerInvariant();

    private static string Sanitize(string value)
    {
        var chars = value
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')
            .ToArray();
        var sanitized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "validation" : sanitized.ToLowerInvariant();
    }

    private static IReadOnlyList<string> Clean(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
