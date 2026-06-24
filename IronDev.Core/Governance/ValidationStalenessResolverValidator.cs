using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class ValidationStalenessResolverValidator
{
    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "validation staleness resolver is read-only",
        "validation staleness resolver is metadata-only",
        "validation staleness resolver uses supplied metadata only",
        "validation staleness resolver uses supplied AsOfUtc only",
        "validation staleness resolver is not operation identity",
        "validation staleness resolver is not operation lookup",
        "validation staleness resolver is not correlation authority",
        "validation staleness resolver is not timeline assembly",
        "validation staleness resolver is not status projection",
        "validation staleness resolver is not missing evidence calculation",
        "validation staleness resolver is not forbidden-action resolution",
        "validation staleness resolver is not receipt resolution",
        "validation staleness resolver is not evidence resolution",
        "validation staleness resolver is not validation execution",
        "validation staleness resolver is not raw validation log resolution",
        "validation staleness resolver is not patch or base freshness",
        "validation staleness resolver is not worktree or head freshness",
        "validation staleness resolver is not policy satisfaction",
        "validation staleness resolver is not approval",
        "validation staleness resolver is not next-safe-action formatting",
        "validation staleness resolver is not authority-warning formatting",
        "validation staleness resolver is not source apply",
        "validation staleness resolver is not rollback",
        "validation staleness resolver is not retry permission",
        "validation staleness resolver is not commit",
        "validation staleness resolver is not push",
        "validation staleness resolver is not PR creation",
        "validation staleness resolver is not merge readiness",
        "validation staleness resolver is not release readiness",
        "validation staleness resolver is not deployment readiness",
        "validation staleness resolver is not memory promotion",
        "validation staleness resolver is not workflow continuation",
        "fresh validation is not authority",
        "stale validation is not denial",
        "expired validation is not policy decision",
        "passed validation is not approval",
        "failed validation is not forbidden-action resolution",
        "redacted validation metadata is not raw log",
        "complete validation assessment is not action allowed"
    ];

    public static ValidationStalenessResolverResult ValidateRequest(ValidationStalenessResolverRequest? request)
    {
        if (request is null)
        {
            return Invalid(["ValidationStalenessResolverRequestRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(request.TenantId, "ValidationStalenessTenantIdRequired", "ValidationStalenessTenantIdInvalid", issues);
        AddScopeIssues(request.ProjectId, "ValidationStalenessProjectIdRequired", "ValidationStalenessProjectIdInvalid", issues);
        AddOperationIdIssues(request.OperationId, issues);

        if (request.AsOfUtc == default)
        {
            issues.Add("ValidationStalenessAsOfUtcRequired");
        }

        if (request.Rules is null)
        {
            issues.Add("ValidationStalenessRulesRequired");
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
                    issues.Add("ValidationStalenessRuleTenantMismatch");
                }

                if (!Same(request.ProjectId, rule.ProjectId))
                {
                    issues.Add("ValidationStalenessRuleProjectMismatch");
                }

                if (!Same(request.OperationId, rule.OperationId))
                {
                    issues.Add("ValidationStalenessRuleOperationMismatch");
                }
            }
        }

        if (request.ValidationResults is null)
        {
            issues.Add("ValidationResultsRequired");
        }
        else
        {
            foreach (var result in request.ValidationResults)
            {
                AddValidationResultIssues(result, issues);
                if (result is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, result.TenantId))
                {
                    issues.Add("ValidationResultTenantMismatch");
                }

                if (!Same(request.ProjectId, result.ProjectId))
                {
                    issues.Add("ValidationResultProjectMismatch");
                }

                if (!Same(request.OperationId, result.OperationId))
                {
                    issues.Add("ValidationResultOperationMismatch");
                }
            }
        }

        return Result(
            issues,
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            request.AsOfUtc,
            ValidationStalenessResolutionStatus.InvalidRequest);
    }

    private static void AddRuleIssues(
        ValidationStalenessRule? rule,
        ICollection<string> issues)
    {
        if (rule is null)
        {
            issues.Add("ValidationStalenessRuleRequired");
            return;
        }

        AddScopeIssues(rule.TenantId, "ValidationStalenessRuleTenantIdRequired", "ValidationStalenessRuleTenantIdInvalid", issues);
        AddScopeIssues(rule.ProjectId, "ValidationStalenessRuleProjectIdRequired", "ValidationStalenessRuleProjectIdInvalid", issues);
        AddOperationIdIssues(rule.OperationId, issues);
        AddIdIssues(rule.RuleId, "ValidationStalenessRuleIdRequired", "ValidationStalenessRuleIdInvalid", issues);
        AddValidationKindIssues(rule.ValidationKind, "ValidationStalenessRuleKindRequired", issues);
        AddSourceIssues(rule.Source, "ValidationStalenessRuleSourceRequired", "ValidationStalenessRuleSourceInvalid", issues);

        if (rule.FreshFor <= TimeSpan.Zero)
        {
            issues.Add("ValidationStalenessRuleFreshForInvalid");
        }

        if (rule.ExpiresAfter <= TimeSpan.Zero)
        {
            issues.Add("ValidationStalenessRuleExpiresAfterInvalid");
        }

        if (rule.FreshFor > TimeSpan.Zero &&
            rule.ExpiresAfter > TimeSpan.Zero &&
            rule.ExpiresAfter < rule.FreshFor)
        {
            issues.Add("ValidationStalenessRuleExpiresBeforeFreshWindow");
        }

        if (rule.CreatedAtUtc == default)
        {
            issues.Add("ValidationStalenessRuleCreatedAtRequired");
        }
    }

    private static void AddValidationResultIssues(
        ValidationResultMetadata? result,
        ICollection<string> issues)
    {
        if (result is null)
        {
            issues.Add("ValidationResultMetadataRequired");
            return;
        }

        AddScopeIssues(result.TenantId, "ValidationResultTenantIdRequired", "ValidationResultTenantIdInvalid", issues);
        AddScopeIssues(result.ProjectId, "ValidationResultProjectIdRequired", "ValidationResultProjectIdInvalid", issues);
        AddOperationIdIssues(result.OperationId, issues);
        AddCorrelationIdIssues(result.CorrelationId, result.OperationId, issues);
        AddIdIssues(result.ValidationResultId, "ValidationResultIdRequired", "ValidationResultIdInvalid", issues);
        AddValidationKindIssues(result.ValidationKind, "ValidationResultKindRequired", issues);
        AddValidationOutcomeIssues(result.Outcome, issues);
        AddSurfaceIssues(result.SurfaceKind, result.SurfaceId, issues);
        AddSourceIssues(result.Source, "ValidationResultSourceRequired", "ValidationResultSourceInvalid", issues);
        AddReferencePairIssues(result.ReferenceKind, result.ReferenceId, issues);

        if (result.CompletedAtUtc == default)
        {
            issues.Add("ValidationResultCompletedAtRequired");
        }

        if (result.RecordedAtUtc == default)
        {
            issues.Add("ValidationResultRecordedAtRequired");
        }

        if (result.CompletedAtUtc != default &&
            result.RecordedAtUtc != default &&
            result.RecordedAtUtc < result.CompletedAtUtc)
        {
            issues.Add("ValidationResultRecordedBeforeCompleted");
        }

        if (result.IsRedacted && string.IsNullOrWhiteSpace(result.RedactionReason))
        {
            issues.Add("ValidationResultRedactionReasonRequired");
        }

        if (!string.IsNullOrWhiteSpace(result.RedactionReason) &&
            ContainsUnsafeText(result.RedactionReason))
        {
            issues.Add("ValidationResultRedactionReasonInvalid");
        }
    }

    private static void AddValidationKindIssues(
        ValidationResultKind kind,
        string requiredIssue,
        ICollection<string> issues)
    {
        if (kind == ValidationResultKind.Unknown ||
            !Enum.IsDefined(kind))
        {
            issues.Add(requiredIssue);
        }
    }

    private static void AddValidationOutcomeIssues(
        ValidationResultOutcome outcome,
        ICollection<string> issues)
    {
        if (outcome == ValidationResultOutcome.Unknown ||
            !Enum.IsDefined(outcome))
        {
            issues.Add("ValidationResultOutcomeRequired");
        }
    }

    private static void AddSurfaceIssues(
        OperationCorrelationSurfaceKind surfaceKind,
        string? surfaceId,
        ICollection<string> issues)
    {
        if (surfaceKind == OperationCorrelationSurfaceKind.Unknown ||
            !Enum.IsDefined(surfaceKind))
        {
            issues.Add("ValidationResultSurfaceKindRequired");
        }

        AddIdIssues(surfaceId, "ValidationResultSurfaceIdRequired", "ValidationResultSurfaceIdInvalid", issues);
    }

    private static void AddReferencePairIssues(
        OperationReferenceKind referenceKind,
        string? referenceId,
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
            issues.Add("ValidationResultReferenceKindRequired");
        }

        if (!hasId)
        {
            issues.Add("ValidationResultReferenceIdRequired");
            return;
        }

        var safeReferenceId = referenceId!;
        if (ContainsUnsafeText(safeReferenceId) ||
            safeReferenceId.Any(char.IsWhiteSpace) ||
            IsUrl(safeReferenceId))
        {
            issues.Add("ValidationResultReferenceIdInvalid");
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
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            issues.Add("ValidationResultCorrelationIdRequired");
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
            issues.Add("ValidationResultCorrelationIdInvalid");
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

    private static ValidationStalenessResolverResult Invalid(IReadOnlyList<string> issues) =>
        Result(
            issues,
            string.Empty,
            string.Empty,
            string.Empty,
            default,
            ValidationStalenessResolutionStatus.InvalidRequest);

    private static ValidationStalenessResolverResult Result(
        IReadOnlyList<string> issues,
        string tenantId,
        string projectId,
        string operationId,
        DateTimeOffset asOfUtc,
        ValidationStalenessResolutionStatus status) =>
        new()
        {
            IsValid = issues.Count == 0,
            ResolutionStatus = issues.Count == 0 ? ValidationStalenessResolutionStatus.NoValidationResults : status,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            AsOfUtc = asOfUtc,
            Assessments = [],
            AmbiguousValidationResults = [],
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = issues.Count == 0 ? Warnings() : [],
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    internal static IReadOnlyList<string> Warnings() =>
    [
        "validation staleness is metadata-only",
        "fresh validation is not authority",
        "passed validation is not approval",
        "stale or expired validation does not choose next safe action",
        "ambiguous validation results do not choose a winner",
        "complete validation assessment is not action allowed"
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
}
