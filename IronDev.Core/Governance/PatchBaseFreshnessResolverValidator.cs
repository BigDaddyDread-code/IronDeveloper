using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class PatchBaseFreshnessResolverValidator
{
    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "patch/base freshness resolver is read-only",
        "patch/base freshness resolver is metadata-only",
        "patch/base freshness resolver uses supplied metadata only",
        "patch/base freshness resolver uses supplied AsOfUtc only",
        "patch/base freshness resolver is not operation identity",
        "patch/base freshness resolver is not operation lookup",
        "patch/base freshness resolver is not correlation authority",
        "patch/base freshness resolver is not timeline assembly",
        "patch/base freshness resolver is not status projection",
        "patch/base freshness resolver is not missing evidence calculation",
        "patch/base freshness resolver is not forbidden-action resolution",
        "patch/base freshness resolver is not receipt resolution",
        "patch/base freshness resolver is not evidence resolution",
        "patch/base freshness resolver is not validation staleness resolution",
        "patch/base freshness resolver is not validation execution",
        "patch/base freshness resolver is not raw patch resolution",
        "patch/base freshness resolver is not raw diff resolution",
        "patch/base freshness resolver is not source inspection",
        "patch/base freshness resolver is not Git inspection",
        "patch/base freshness resolver is not worktree/base/head freshness read model",
        "patch/base freshness resolver is not policy satisfaction",
        "patch/base freshness resolver is not approval",
        "patch/base freshness resolver is not next-safe-action formatting",
        "patch/base freshness resolver is not authority-warning formatting",
        "patch/base freshness resolver is not source apply",
        "patch/base freshness resolver is not rollback",
        "patch/base freshness resolver is not retry permission",
        "patch/base freshness resolver is not commit",
        "patch/base freshness resolver is not push",
        "patch/base freshness resolver is not PR creation",
        "patch/base freshness resolver is not merge readiness",
        "patch/base freshness resolver is not release readiness",
        "patch/base freshness resolver is not deployment readiness",
        "patch/base freshness resolver is not memory promotion",
        "patch/base freshness resolver is not workflow continuation",
        "fresh patch metadata is not authority",
        "matching base branch metadata is not source apply permission",
        "patch hash match is not approval",
        "base moved is not policy denial",
        "patch expired is not next-safe-action selection",
        "redacted patch/base metadata is not raw patch",
        "complete patch/base assessment is not action allowed"
    ];

    public static PatchBaseFreshnessResolverResult ValidateRequest(PatchBaseFreshnessResolverRequest? request)
    {
        if (request is null)
        {
            return Invalid(["PatchBaseFreshnessResolverRequestRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(request.TenantId, "PatchBaseFreshnessTenantIdRequired", "PatchBaseFreshnessTenantIdInvalid", issues);
        AddScopeIssues(request.ProjectId, "PatchBaseFreshnessProjectIdRequired", "PatchBaseFreshnessProjectIdInvalid", issues);
        AddOperationIdIssues(request.OperationId, issues);

        if (request.AsOfUtc == default)
        {
            issues.Add("PatchBaseFreshnessAsOfUtcRequired");
        }

        if (request.Rules is null)
        {
            issues.Add("PatchBaseFreshnessRulesRequired");
        }
        else
        {
            foreach (var rule in request.Rules)
            {
                AddRuleIssues(rule, issues);
                if (rule is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, rule.TenantId))
                {
                    issues.Add("PatchBaseFreshnessRuleTenantMismatch");
                }

                if (!Same(request.ProjectId, rule.ProjectId))
                {
                    issues.Add("PatchBaseFreshnessRuleProjectMismatch");
                }

                if (!Same(request.OperationId, rule.OperationId))
                {
                    issues.Add("PatchBaseFreshnessRuleOperationMismatch");
                }
            }
        }

        if (request.PatchArtifacts is null)
        {
            issues.Add("PatchArtifactsRequired");
        }
        else
        {
            foreach (var patch in request.PatchArtifacts)
            {
                AddPatchArtifactIssues(patch, issues);
                if (patch is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, patch.TenantId))
                {
                    issues.Add("PatchArtifactTenantMismatch");
                }

                if (!Same(request.ProjectId, patch.ProjectId))
                {
                    issues.Add("PatchArtifactProjectMismatch");
                }

                if (!Same(request.OperationId, patch.OperationId))
                {
                    issues.Add("PatchArtifactOperationMismatch");
                }
            }
        }

        if (request.BaseBranchObservations is null)
        {
            issues.Add("BaseBranchObservationsRequired");
        }
        else
        {
            foreach (var observation in request.BaseBranchObservations)
            {
                AddBaseObservationIssues(observation, issues);
                if (observation is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, observation.TenantId))
                {
                    issues.Add("BaseBranchObservationTenantMismatch");
                }

                if (!Same(request.ProjectId, observation.ProjectId))
                {
                    issues.Add("BaseBranchObservationProjectMismatch");
                }

                if (!Same(request.OperationId, observation.OperationId))
                {
                    issues.Add("BaseBranchObservationOperationMismatch");
                }
            }
        }

        return Result(
            issues,
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            request.AsOfUtc,
            PatchBaseFreshnessResolutionStatus.InvalidRequest);
    }

    private static void AddRuleIssues(
        PatchBaseFreshnessRule? rule,
        ICollection<string> issues)
    {
        if (rule is null)
        {
            issues.Add("PatchBaseFreshnessRuleRequired");
            return;
        }

        AddScopeIssues(rule.TenantId, "PatchBaseFreshnessRuleTenantIdRequired", "PatchBaseFreshnessRuleTenantIdInvalid", issues);
        AddScopeIssues(rule.ProjectId, "PatchBaseFreshnessRuleProjectIdRequired", "PatchBaseFreshnessRuleProjectIdInvalid", issues);
        AddOperationIdIssues(rule.OperationId, issues);
        AddIdIssues(rule.RuleId, "PatchBaseFreshnessRuleIdRequired", "PatchBaseFreshnessRuleIdInvalid", issues);
        AddPatchKindIssues(rule.PatchKind, "PatchBaseFreshnessRulePatchKindRequired", issues);
        AddSourceIssues(rule.Source, "PatchBaseFreshnessRuleSourceRequired", "PatchBaseFreshnessRuleSourceInvalid", issues);

        if (rule.FreshFor <= TimeSpan.Zero)
        {
            issues.Add("PatchBaseFreshnessRuleFreshForInvalid");
        }

        if (rule.ExpiresAfter <= TimeSpan.Zero)
        {
            issues.Add("PatchBaseFreshnessRuleExpiresAfterInvalid");
        }

        if (rule.FreshFor > TimeSpan.Zero &&
            rule.ExpiresAfter > TimeSpan.Zero &&
            rule.ExpiresAfter < rule.FreshFor)
        {
            issues.Add("PatchBaseFreshnessRuleExpiresBeforeFreshWindow");
        }

        if (rule.CreatedAtUtc == default)
        {
            issues.Add("PatchBaseFreshnessRuleCreatedAtRequired");
        }
    }

    private static void AddPatchArtifactIssues(
        PatchArtifactFreshnessMetadata? patch,
        ICollection<string> issues)
    {
        if (patch is null)
        {
            issues.Add("PatchArtifactFreshnessMetadataRequired");
            return;
        }

        AddScopeIssues(patch.TenantId, "PatchArtifactTenantIdRequired", "PatchArtifactTenantIdInvalid", issues);
        AddScopeIssues(patch.ProjectId, "PatchArtifactProjectIdRequired", "PatchArtifactProjectIdInvalid", issues);
        AddOperationIdIssues(patch.OperationId, issues);
        AddCorrelationIdIssues(patch.CorrelationId, patch.OperationId, "PatchArtifactCorrelationIdRequired", "PatchArtifactCorrelationIdInvalid", issues);
        AddIdIssues(patch.PatchArtifactId, "PatchArtifactIdRequired", "PatchArtifactIdInvalid", issues);
        AddPatchKindIssues(patch.PatchKind, "PatchArtifactKindRequired", issues);
        AddHashIssues(patch.PatchHash, patch.HashAlgorithm, "PatchArtifactHashRequired", "PatchArtifactHashInvalid", "PatchArtifactHashAlgorithmRequired", issues);
        AddBranchIssues(patch.BaseBranch, "PatchArtifactBaseBranchRequired", "PatchArtifactBaseBranchInvalid", issues);
        AddCommitShaIssues(patch.BaseCommitSha, "PatchArtifactBaseCommitShaRequired", "PatchArtifactBaseCommitShaInvalid", issues);
        AddSurfaceIssues(patch.SurfaceKind, patch.SurfaceId, "PatchArtifactSurfaceKindRequired", "PatchArtifactSurfaceIdRequired", "PatchArtifactSurfaceIdInvalid", issues);
        AddReferencePairIssues(patch.ReferenceKind, patch.ReferenceId, "PatchArtifactReferenceKindRequired", "PatchArtifactReferenceIdRequired", "PatchArtifactReferenceIdInvalid", issues);
        AddSourceIssues(patch.Source, "PatchArtifactSourceRequired", "PatchArtifactSourceInvalid", issues);

        if (patch.CreatedAtUtc == default)
        {
            issues.Add("PatchArtifactCreatedAtRequired");
        }

        if (patch.RecordedAtUtc == default)
        {
            issues.Add("PatchArtifactRecordedAtRequired");
        }

        if (patch.CreatedAtUtc != default &&
            patch.RecordedAtUtc != default &&
            patch.RecordedAtUtc < patch.CreatedAtUtc)
        {
            issues.Add("PatchArtifactRecordedBeforeCreated");
        }

        AddRedactionIssues(
            patch.IsRedacted,
            patch.RedactionReason,
            "PatchArtifactRedactionReasonRequired",
            "PatchArtifactRedactionReasonInvalid",
            issues);
    }

    private static void AddBaseObservationIssues(
        BaseBranchObservationMetadata? observation,
        ICollection<string> issues)
    {
        if (observation is null)
        {
            issues.Add("BaseBranchObservationMetadataRequired");
            return;
        }

        AddScopeIssues(observation.TenantId, "BaseBranchObservationTenantIdRequired", "BaseBranchObservationTenantIdInvalid", issues);
        AddScopeIssues(observation.ProjectId, "BaseBranchObservationProjectIdRequired", "BaseBranchObservationProjectIdInvalid", issues);
        AddOperationIdIssues(observation.OperationId, issues);
        AddCorrelationIdIssues(observation.CorrelationId, observation.OperationId, "BaseBranchObservationCorrelationIdRequired", "BaseBranchObservationCorrelationIdInvalid", issues);
        AddBranchIssues(observation.BaseBranch, "BaseBranchObservationBaseBranchRequired", "BaseBranchObservationBaseBranchInvalid", issues);
        AddCommitShaIssues(observation.ObservedBaseCommitSha, "BaseBranchObservationCommitShaRequired", "BaseBranchObservationCommitShaInvalid", issues);
        AddSurfaceIssues(observation.SurfaceKind, observation.SurfaceId, "BaseBranchObservationSurfaceKindRequired", "BaseBranchObservationSurfaceIdRequired", "BaseBranchObservationSurfaceIdInvalid", issues);
        AddReferencePairIssues(observation.ReferenceKind, observation.ReferenceId, "BaseBranchObservationReferenceKindRequired", "BaseBranchObservationReferenceIdRequired", "BaseBranchObservationReferenceIdInvalid", issues);
        AddSourceIssues(observation.Source, "BaseBranchObservationSourceRequired", "BaseBranchObservationSourceInvalid", issues);

        if (!string.IsNullOrWhiteSpace(observation.ObservedPatchHash) &&
            !IsSafeHashShape(observation.ObservedPatchHash))
        {
            issues.Add("BaseBranchObservationPatchHashInvalid");
        }

        if (observation.ObservedAtUtc == default)
        {
            issues.Add("BaseBranchObservationObservedAtRequired");
        }

        if (observation.RecordedAtUtc == default)
        {
            issues.Add("BaseBranchObservationRecordedAtRequired");
        }

        if (observation.ObservedAtUtc != default &&
            observation.RecordedAtUtc != default &&
            observation.RecordedAtUtc < observation.ObservedAtUtc)
        {
            issues.Add("BaseBranchObservationRecordedBeforeObserved");
        }

        AddRedactionIssues(
            observation.IsRedacted,
            observation.RedactionReason,
            "BaseBranchObservationRedactionReasonRequired",
            "BaseBranchObservationRedactionReasonInvalid",
            issues);
    }

    private static void AddPatchKindIssues(
        PatchArtifactKind kind,
        string requiredIssue,
        ICollection<string> issues)
    {
        if (kind == PatchArtifactKind.Unknown ||
            !Enum.IsDefined(kind))
        {
            issues.Add(requiredIssue);
        }
    }

    private static void AddHashIssues(
        string? hash,
        PatchHashAlgorithm algorithm,
        string requiredIssue,
        string invalidIssue,
        string algorithmIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            issues.Add(requiredIssue);
        }
        else if (!IsSafeHashShape(hash) ||
            algorithm == PatchHashAlgorithm.Sha256 && hash.Length != 64 ||
            algorithm == PatchHashAlgorithm.Sha512 && hash.Length != 128)
        {
            issues.Add(invalidIssue);
        }

        if (algorithm == PatchHashAlgorithm.Unknown ||
            !Enum.IsDefined(algorithm))
        {
            issues.Add(algorithmIssue);
        }
    }

    private static bool IsSafeHashShape(string value) =>
        HexHashPattern().IsMatch(value);

    private static void AddBranchIssues(
        string? value,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (ContainsUnsafeText(value) ||
            value.Any(char.IsWhiteSpace) ||
            value.Contains('\\') ||
            value.Contains("..", StringComparison.Ordinal) ||
            value.StartsWith("/", StringComparison.Ordinal) ||
            IsUrl(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddCommitShaIssues(
        string? value,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (!CommitShaPattern().IsMatch(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddSurfaceIssues(
        OperationCorrelationSurfaceKind surfaceKind,
        string? surfaceId,
        string kindIssue,
        string idRequiredIssue,
        string idInvalidIssue,
        ICollection<string> issues)
    {
        if (surfaceKind == OperationCorrelationSurfaceKind.Unknown ||
            !Enum.IsDefined(surfaceKind))
        {
            issues.Add(kindIssue);
        }

        AddIdIssues(surfaceId, idRequiredIssue, idInvalidIssue, issues);
    }

    private static void AddReferencePairIssues(
        OperationReferenceKind referenceKind,
        string? referenceId,
        string kindIssue,
        string idRequiredIssue,
        string idInvalidIssue,
        ICollection<string> issues)
    {
        var hasKind = referenceKind != OperationReferenceKind.Unknown;
        var hasId = !string.IsNullOrWhiteSpace(referenceId);

        if (!hasKind && !hasId)
        {
            return;
        }

        if (!hasKind)
        {
            issues.Add(kindIssue);
        }

        if (!hasId)
        {
            issues.Add(idRequiredIssue);
            return;
        }

        var safeReferenceId = referenceId!;
        if (ContainsUnsafeText(safeReferenceId) ||
            safeReferenceId.Any(char.IsWhiteSpace) ||
            IsUrl(safeReferenceId))
        {
            issues.Add(idInvalidIssue);
        }
    }

    private static void AddScopeIssues(
        string? value,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (ContainsUnsafeText(value) ||
            value.Any(char.IsWhiteSpace))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddOperationIdIssues(string? operationId, ICollection<string> issues)
    {
        var result = OperationIdentityValidator.ValidateOperationId(operationId);
        foreach (var issue in result.Issues)
        {
            issues.Add(issue);
        }
    }

    private static void AddCorrelationIdIssues(
        string? correlationId,
        string? operationId,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (ContainsUnsafeText(correlationId) ||
            correlationId.Any(char.IsWhiteSpace) ||
            correlationId.Contains('/') ||
            correlationId.Contains('\\') ||
            IsUrl(correlationId) ||
            !string.IsNullOrWhiteSpace(operationId) &&
            Same(correlationId, operationId) ||
            !CanonicalCorrelationIdPattern().IsMatch(correlationId))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddIdIssues(
        string? value,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (ContainsUnsafeText(value) ||
            value.Any(char.IsWhiteSpace) ||
            IsUrl(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddSourceIssues(
        string? value,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (ContainsUnsafeText(value) ||
            value.Any(char.IsWhiteSpace))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddRedactionIssues(
        bool isRedacted,
        string? redactionReason,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (isRedacted && string.IsNullOrWhiteSpace(redactionReason))
        {
            issues.Add(requiredIssue);
        }

        if (!string.IsNullOrWhiteSpace(redactionReason) &&
            ContainsUnsafeText(redactionReason))
        {
            issues.Add(invalidIssue);
        }
    }

    private static PatchBaseFreshnessResolverResult Invalid(IReadOnlyList<string> issues) =>
        Result(
            issues,
            string.Empty,
            string.Empty,
            string.Empty,
            default,
            PatchBaseFreshnessResolutionStatus.InvalidRequest);

    private static PatchBaseFreshnessResolverResult Result(
        IReadOnlyList<string> issues,
        string tenantId,
        string projectId,
        string operationId,
        DateTimeOffset asOfUtc,
        PatchBaseFreshnessResolutionStatus status) =>
        new()
        {
            IsValid = issues.Count == 0,
            ResolutionStatus = issues.Count == 0 ? PatchBaseFreshnessResolutionStatus.NoPatchArtifacts : status,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            AsOfUtc = asOfUtc,
            Assessments = [],
            AmbiguousPatchBaseMetadata = [],
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = issues.Count == 0 ? Warnings() : [],
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    internal static IReadOnlyList<string> Warnings() =>
    [
        "patch/base freshness is metadata-only",
        "patch/base freshness uses supplied AsOfUtc only",
        "fresh patch metadata is not authority",
        "patch hash match is not approval",
        "matching base branch metadata is not source apply permission",
        "stale or expired patch metadata does not choose next safe action",
        "ambiguous patch/base metadata does not choose a winner",
        "complete patch/base assessment is not action allowed"
    ];

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsUnsafeText(string value) =>
        value.Any(char.IsControl) ||
        value.Length > 512 ||
        ContainsAuthorityText(value) ||
        ContainsSecretMarker(value) ||
        ContainsRawPayloadMarker(value);

    private static bool ContainsAuthorityText(string value)
    {
        var markers = new[]
        {
            "approval granted",
            "approved for",
            "policy satisfied",
            "authority granted",
            "action allowed",
            "allowed to",
            "fresh enough to apply",
            "fresh enough to proceed",
            "ready for review",
            "merge ready",
            "release ready",
            "deploy now",
            "continue workflow",
            "retry authorized",
            "rollback authorized",
            "ship it"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsRawPayloadMarker(string value)
    {
        var markers = new[]
        {
            "raw patch",
            "raw diff",
            "raw validation log",
            "raw evidence payload",
            "raw receipt payload",
            "raw request body",
            "raw response body",
            "full patch",
            "full diff",
            "hidden chain-of-thought",
            "private reasoning"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsSecretMarker(string value)
    {
        var markers = new[]
        {
            "authorization:",
            "bearer ",
            "api key",
            "apikey",
            "password",
            "secret",
            "token=",
            "connection string",
            "private key"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUrl(string value) =>
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^corr_([0-9a-f]{16,64}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$")]
    private static partial Regex CanonicalCorrelationIdPattern();

    [GeneratedRegex("^[0-9a-fA-F]{7,64}$")]
    private static partial Regex CommitShaPattern();

    [GeneratedRegex("^[0-9a-fA-F]{64}([0-9a-fA-F]{64})?$")]
    private static partial Regex HexHashPattern();
}
