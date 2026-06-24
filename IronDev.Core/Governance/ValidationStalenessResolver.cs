namespace IronDev.Core.Governance;

public static class ValidationStalenessResolver
{
    public static ValidationStalenessResolverResult Resolve(ValidationStalenessResolverRequest? request)
    {
        var validation = ValidationStalenessResolverValidator.ValidateRequest(request);
        if (!validation.IsValid || request is null)
        {
            return InvalidResult(
                request?.TenantId ?? string.Empty,
                request?.ProjectId ?? string.Empty,
                request?.OperationId ?? string.Empty,
                request?.AsOfUtc ?? default,
                validation.Issues);
        }

        if (request.ValidationResults.Count == 0)
        {
            return Result(
                request,
                ValidationStalenessResolutionStatus.NoValidationResults,
                [],
                [],
                [],
                ValidationStalenessResolverValidator.Warnings());
        }

        var ambiguity = FindAmbiguity(request);
        if (ambiguity.Count > 0)
        {
            return Result(
                request,
                ValidationStalenessResolutionStatus.AmbiguousValidationResults,
                [],
                ambiguity,
                [],
                ValidationStalenessResolverValidator.Warnings());
        }

        var assessments = request.ValidationResults
            .Select(result => Assess(request, result))
            .OrderBy(static assessment => assessment.ValidationKind.ToString(), StringComparer.Ordinal)
            .ThenBy(static assessment => assessment.ValidationResultId, StringComparer.Ordinal)
            .ToArray();

        var status = DetermineStatus(assessments);
        var issues = assessments
            .Where(static assessment => assessment.StalenessState == ValidationStalenessState.Unassessable)
            .Select(static assessment => assessment.Reason)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static issue => issue, StringComparer.Ordinal)
            .ToArray();

        return Result(
            request,
            status,
            assessments,
            [],
            issues,
            ValidationStalenessResolverValidator.Warnings());
    }

    private static IReadOnlyList<string> FindAmbiguity(ValidationStalenessResolverRequest request)
    {
        var ambiguous = new List<string>();

        AddDuplicateIds(
            request.Rules.Select(static rule => rule.RuleId),
            "DuplicateValidationStalenessRuleId",
            ambiguous);
        AddDuplicateIds(
            request.ValidationResults.Select(static result => result.ValidationResultId),
            "DuplicateValidationResultId",
            ambiguous);

        foreach (var group in request.Rules.GroupBy(static rule => rule.RuleId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(RuleFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingValidationStalenessRuleMetadata:{group.Key}");
            }
        }

        foreach (var group in request.ValidationResults.GroupBy(static result => result.ValidationResultId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(ValidationResultFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingValidationResultMetadata:{group.Key}");
            }
        }

        foreach (var group in request.Rules.GroupBy(static rule => rule.ValidationKind))
        {
            if (group.Count() > 1)
            {
                ambiguous.Add($"MultipleValidationStalenessRulesForKind:{group.Key}");
            }
        }

        foreach (var group in request.ValidationResults.GroupBy(ValidationResultDistinctionKey, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                ambiguous.Add($"IndistinguishableValidationResults:{group.Key}");
            }
        }

        return ambiguous
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static ValidationResultStalenessAssessment Assess(
        ValidationStalenessResolverRequest request,
        ValidationResultMetadata result)
    {
        var rule = request.Rules.SingleOrDefault(candidate => candidate.ValidationKind == result.ValidationKind);
        var age = request.AsOfUtc - result.CompletedAtUtc;

        if (request.AsOfUtc < result.CompletedAtUtc)
        {
            return Assessment(
                result,
                ValidationStalenessState.Unassessable,
                age,
                null,
                null,
                null,
                "ValidationResultCompletedAfterAsOf");
        }

        if (rule is null)
        {
            return Assessment(
                result,
                ValidationStalenessState.MissingRule,
                age,
                null,
                null,
                null,
                "ValidationStalenessRuleMissing");
        }

        var freshUntilUtc = result.CompletedAtUtc + rule.FreshFor;
        var expiresAtUtc = result.CompletedAtUtc + rule.ExpiresAfter;
        var state = age <= rule.FreshFor
            ? ValidationStalenessState.Fresh
            : age <= rule.ExpiresAfter
                ? ValidationStalenessState.Stale
                : ValidationStalenessState.Expired;

        return Assessment(
            result,
            state,
            age,
            freshUntilUtc,
            expiresAtUtc,
            rule.RuleId,
            state switch
            {
                ValidationStalenessState.Fresh => "ValidationResultFresh",
                ValidationStalenessState.Stale => "ValidationResultStale",
                _ => "ValidationResultExpired"
            });
    }

    private static ValidationStalenessResolutionStatus DetermineStatus(
        IReadOnlyCollection<ValidationResultStalenessAssessment> assessments)
    {
        if (assessments.Any(static assessment => assessment.StalenessState == ValidationStalenessState.Unassessable))
        {
            return ValidationStalenessResolutionStatus.Unassessable;
        }

        if (assessments.Any(static assessment => assessment.StalenessState == ValidationStalenessState.MissingRule))
        {
            return ValidationStalenessResolutionStatus.MissingRules;
        }

        var distinctStates = assessments
            .Select(static assessment => assessment.StalenessState)
            .Distinct()
            .Count();

        return distinctStates > 1
            ? ValidationStalenessResolutionStatus.MixedStaleness
            : ValidationStalenessResolutionStatus.Assessed;
    }

    private static ValidationResultStalenessAssessment Assessment(
        ValidationResultMetadata result,
        ValidationStalenessState state,
        TimeSpan age,
        DateTimeOffset? freshUntilUtc,
        DateTimeOffset? expiresAtUtc,
        string? ruleId,
        string reason) =>
        new()
        {
            ValidationResultId = result.ValidationResultId,
            ValidationKind = result.ValidationKind,
            Outcome = result.Outcome,
            StalenessState = state,
            CompletedAtUtc = result.CompletedAtUtc,
            RecordedAtUtc = result.RecordedAtUtc,
            Age = age,
            FreshUntilUtc = freshUntilUtc,
            ExpiresAtUtc = expiresAtUtc,
            RuleId = ruleId,
            SurfaceKind = result.SurfaceKind,
            SurfaceId = result.SurfaceId,
            ReferenceKind = result.ReferenceKind,
            ReferenceId = result.ReferenceId,
            IsRedacted = result.IsRedacted,
            Reason = reason
        };

    private static void AddDuplicateIds(
        IEnumerable<string> ids,
        string issue,
        ICollection<string> ambiguous)
    {
        foreach (var duplicate in ids
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key))
        {
            ambiguous.Add($"{issue}:{duplicate}");
        }
    }

    private static string RuleFingerprint(ValidationStalenessRule rule) =>
        string.Join(
            "|",
            rule.TenantId,
            rule.ProjectId,
            rule.OperationId,
            rule.RuleId,
            rule.ValidationKind,
            rule.FreshFor,
            rule.ExpiresAfter,
            rule.Source,
            rule.CreatedAtUtc);

    private static string ValidationResultFingerprint(ValidationResultMetadata result) =>
        string.Join(
            "|",
            result.TenantId,
            result.ProjectId,
            result.OperationId,
            result.CorrelationId,
            result.ValidationResultId,
            result.ValidationKind,
            result.Outcome,
            result.CompletedAtUtc,
            result.RecordedAtUtc,
            result.SurfaceKind,
            result.SurfaceId,
            result.ReferenceKind,
            result.ReferenceId,
            result.Source,
            result.IsRedacted,
            result.RedactionReason);

    private static string ValidationResultDistinctionKey(ValidationResultMetadata result) =>
        string.Join(
            ":",
            result.ValidationKind,
            result.CorrelationId,
            result.Source,
            result.SurfaceKind,
            result.SurfaceId,
            result.ReferenceKind,
            result.ReferenceId);

    private static ValidationStalenessResolverResult InvalidResult(
        string tenantId,
        string projectId,
        string operationId,
        DateTimeOffset asOfUtc,
        IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            ResolutionStatus = ValidationStalenessResolutionStatus.InvalidRequest,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            AsOfUtc = asOfUtc,
            Assessments = [],
            AmbiguousValidationResults = [],
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = [],
            ForbiddenAuthorityImplications = ValidationStalenessResolverValidator.ForbiddenAuthorityImplications
        };

    private static ValidationStalenessResolverResult Result(
        ValidationStalenessResolverRequest request,
        ValidationStalenessResolutionStatus status,
        IReadOnlyList<ValidationResultStalenessAssessment> assessments,
        IReadOnlyList<string> ambiguous,
        IReadOnlyList<string> issues,
        IReadOnlyList<string> warnings) =>
        new()
        {
            IsValid = issues.Count == 0,
            ResolutionStatus = status,
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            OperationId = request.OperationId,
            AsOfUtc = request.AsOfUtc,
            Assessments = assessments,
            AmbiguousValidationResults = ambiguous,
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = warnings,
            ForbiddenAuthorityImplications = ValidationStalenessResolverValidator.ForbiddenAuthorityImplications
        };
}
