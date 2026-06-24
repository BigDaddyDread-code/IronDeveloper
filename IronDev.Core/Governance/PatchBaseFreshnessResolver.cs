namespace IronDev.Core.Governance;

public static class PatchBaseFreshnessResolver
{
    public static PatchBaseFreshnessResolverResult Resolve(PatchBaseFreshnessResolverRequest? request)
    {
        var validation = PatchBaseFreshnessResolverValidator.ValidateRequest(request);
        if (!validation.IsValid || request is null)
        {
            return InvalidResult(
                request?.TenantId ?? string.Empty,
                request?.ProjectId ?? string.Empty,
                request?.OperationId ?? string.Empty,
                request?.AsOfUtc ?? default,
                validation.Issues);
        }

        if (request.PatchArtifacts.Count == 0)
        {
            return Result(
                request,
                PatchBaseFreshnessResolutionStatus.NoPatchArtifacts,
                [],
                [],
                [],
                PatchBaseFreshnessResolverValidator.Warnings());
        }

        var ambiguity = FindAmbiguity(request);
        if (ambiguity.Count > 0)
        {
            return Result(
                request,
                PatchBaseFreshnessResolutionStatus.AmbiguousPatchBaseMetadata,
                [],
                ambiguity,
                [],
                PatchBaseFreshnessResolverValidator.Warnings());
        }

        var assessments = request.PatchArtifacts
            .Select(patch => Assess(request, patch))
            .OrderBy(static assessment => assessment.PatchKind.ToString(), StringComparer.Ordinal)
            .ThenBy(static assessment => assessment.BaseBranch, StringComparer.Ordinal)
            .ThenBy(static assessment => assessment.PatchArtifactId, StringComparer.Ordinal)
            .ToArray();

        var status = DetermineStatus(assessments);
        var issues = assessments
            .Where(static assessment => assessment.FreshnessState == PatchBaseFreshnessState.Unassessable)
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
            PatchBaseFreshnessResolverValidator.Warnings());
    }

    private static IReadOnlyList<string> FindAmbiguity(PatchBaseFreshnessResolverRequest request)
    {
        var ambiguous = new List<string>();

        AddDuplicateIds(
            request.Rules.Select(static rule => rule.RuleId),
            "DuplicatePatchBaseFreshnessRuleId",
            ambiguous);
        AddDuplicateIds(
            request.PatchArtifacts.Select(static patch => patch.PatchArtifactId),
            "DuplicatePatchArtifactId",
            ambiguous);

        foreach (var group in request.Rules.GroupBy(static rule => rule.RuleId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(RuleFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingPatchBaseFreshnessRuleMetadata:{group.Key}");
            }
        }

        foreach (var group in request.PatchArtifacts.GroupBy(static patch => patch.PatchArtifactId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(PatchArtifactFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingPatchArtifactMetadata:{group.Key}");
            }
        }

        foreach (var group in request.BaseBranchObservations.GroupBy(BaseObservationDistinctionKey, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                ambiguous.Add($"DuplicateBaseBranchObservation:{group.Key}");
            }

            if (group.Select(BaseObservationFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingBaseBranchObservationMetadata:{group.Key}");
            }
        }

        foreach (var group in request.Rules.GroupBy(static rule => rule.PatchKind))
        {
            if (group.Count() > 1)
            {
                ambiguous.Add($"MultiplePatchBaseFreshnessRulesForKind:{group.Key}");
            }
        }

        foreach (var group in request.PatchArtifacts.GroupBy(PatchArtifactDistinctionKey, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                ambiguous.Add($"IndistinguishablePatchArtifacts:{group.Key}");
            }
        }

        foreach (var patch in request.PatchArtifacts)
        {
            var matchingObservations = MatchingObservations(request, patch).ToArray();
            if (matchingObservations.Length > 1)
            {
                ambiguous.Add($"MultipleBaseBranchObservationsForPatch:{patch.PatchArtifactId}");
            }
        }

        return ambiguous
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static PatchBaseFreshnessAssessment Assess(
        PatchBaseFreshnessResolverRequest request,
        PatchArtifactFreshnessMetadata patch)
    {
        var age = request.AsOfUtc - patch.CreatedAtUtc;
        if (request.AsOfUtc < patch.CreatedAtUtc)
        {
            return Assessment(
                patch,
                PatchBaseFreshnessState.Unassessable,
                null,
                age,
                null,
                null,
                null,
                "PatchArtifactCreatedAfterAsOf");
        }

        var rule = request.Rules.SingleOrDefault(candidate => candidate.PatchKind == patch.PatchKind);
        if (rule is null)
        {
            return Assessment(
                patch,
                PatchBaseFreshnessState.MissingRule,
                null,
                age,
                null,
                null,
                null,
                "PatchBaseFreshnessRuleMissing");
        }

        var observation = MatchingObservations(request, patch).SingleOrDefault();
        if (observation is null)
        {
            return Assessment(
                patch,
                PatchBaseFreshnessState.MissingBaseObservation,
                null,
                age,
                null,
                null,
                rule.RuleId,
                "BaseBranchObservationMissing");
        }

        if (rule.RequirePatchHashMatch &&
            !string.IsNullOrWhiteSpace(observation.ObservedPatchHash) &&
            !string.Equals(patch.PatchHash, observation.ObservedPatchHash, StringComparison.OrdinalIgnoreCase))
        {
            return Assessment(
                patch,
                PatchBaseFreshnessState.PatchHashMismatch,
                observation,
                age,
                null,
                null,
                rule.RuleId,
                "PatchHashMismatch");
        }

        if (rule.RequireBaseBranchMatch &&
            !string.Equals(patch.BaseCommitSha, observation.ObservedBaseCommitSha, StringComparison.OrdinalIgnoreCase))
        {
            return Assessment(
                patch,
                PatchBaseFreshnessState.BaseBranchMoved,
                observation,
                age,
                null,
                null,
                rule.RuleId,
                "BaseBranchMoved");
        }

        var freshUntilUtc = patch.CreatedAtUtc + rule.FreshFor;
        var expiresAtUtc = patch.CreatedAtUtc + rule.ExpiresAfter;
        var state = age <= rule.FreshFor
            ? PatchBaseFreshnessState.Fresh
            : age <= rule.ExpiresAfter
                ? PatchBaseFreshnessState.PatchStale
                : PatchBaseFreshnessState.PatchExpired;

        return Assessment(
            patch,
            state,
            observation,
            age,
            freshUntilUtc,
            expiresAtUtc,
            rule.RuleId,
            state switch
            {
                PatchBaseFreshnessState.Fresh => "PatchFresh",
                PatchBaseFreshnessState.PatchStale => "PatchStale",
                _ => "PatchExpired"
            });
    }

    private static IEnumerable<BaseBranchObservationMetadata> MatchingObservations(
        PatchBaseFreshnessResolverRequest request,
        PatchArtifactFreshnessMetadata patch) =>
        request.BaseBranchObservations.Where(observation =>
            string.Equals(observation.TenantId, patch.TenantId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observation.ProjectId, patch.ProjectId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observation.OperationId, patch.OperationId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observation.BaseBranch, patch.BaseBranch, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observation.CorrelationId, patch.CorrelationId, StringComparison.OrdinalIgnoreCase));

    private static PatchBaseFreshnessResolutionStatus DetermineStatus(
        IReadOnlyCollection<PatchBaseFreshnessAssessment> assessments)
    {
        if (assessments.Any(static assessment => assessment.FreshnessState == PatchBaseFreshnessState.Unassessable))
        {
            return PatchBaseFreshnessResolutionStatus.Unassessable;
        }

        if (assessments.Any(static assessment => assessment.FreshnessState == PatchBaseFreshnessState.MissingRule))
        {
            return PatchBaseFreshnessResolutionStatus.MissingRules;
        }

        if (assessments.Any(static assessment => assessment.FreshnessState == PatchBaseFreshnessState.MissingBaseObservation))
        {
            return PatchBaseFreshnessResolutionStatus.MissingBaseObservations;
        }

        var distinctStates = assessments
            .Select(static assessment => assessment.FreshnessState)
            .Distinct()
            .Count();

        return distinctStates > 1
            ? PatchBaseFreshnessResolutionStatus.MixedFreshness
            : PatchBaseFreshnessResolutionStatus.Assessed;
    }

    private static PatchBaseFreshnessAssessment Assessment(
        PatchArtifactFreshnessMetadata patch,
        PatchBaseFreshnessState state,
        BaseBranchObservationMetadata? observation,
        TimeSpan age,
        DateTimeOffset? freshUntilUtc,
        DateTimeOffset? expiresAtUtc,
        string? ruleId,
        string reason) =>
        new()
        {
            PatchArtifactId = patch.PatchArtifactId,
            PatchKind = patch.PatchKind,
            FreshnessState = state,
            PatchHash = patch.PatchHash,
            HashAlgorithm = patch.HashAlgorithm,
            BaseBranch = patch.BaseBranch,
            PatchBaseCommitSha = patch.BaseCommitSha,
            ObservedBaseCommitSha = observation?.ObservedBaseCommitSha,
            PatchCreatedAtUtc = patch.CreatedAtUtc,
            BaseObservedAtUtc = observation?.ObservedAtUtc,
            Age = age,
            FreshUntilUtc = freshUntilUtc,
            ExpiresAtUtc = expiresAtUtc,
            RuleId = ruleId,
            SurfaceKind = patch.SurfaceKind,
            SurfaceId = patch.SurfaceId,
            ReferenceKind = patch.ReferenceKind,
            ReferenceId = patch.ReferenceId,
            IsRedacted = patch.IsRedacted || observation?.IsRedacted == true,
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

    private static string RuleFingerprint(PatchBaseFreshnessRule rule) =>
        string.Join(
            "|",
            rule.TenantId,
            rule.ProjectId,
            rule.OperationId,
            rule.RuleId,
            rule.PatchKind,
            rule.FreshFor,
            rule.ExpiresAfter,
            rule.RequireBaseBranchMatch,
            rule.RequirePatchHashMatch,
            rule.Source,
            rule.CreatedAtUtc);

    private static string PatchArtifactFingerprint(PatchArtifactFreshnessMetadata patch) =>
        string.Join(
            "|",
            patch.TenantId,
            patch.ProjectId,
            patch.OperationId,
            patch.CorrelationId,
            patch.PatchArtifactId,
            patch.PatchKind,
            patch.PatchHash,
            patch.HashAlgorithm,
            patch.BaseBranch,
            patch.BaseCommitSha,
            patch.CreatedAtUtc,
            patch.RecordedAtUtc,
            patch.SurfaceKind,
            patch.SurfaceId,
            patch.ReferenceKind,
            patch.ReferenceId,
            patch.Source,
            patch.IsRedacted,
            patch.RedactionReason);

    private static string BaseObservationFingerprint(BaseBranchObservationMetadata observation) =>
        string.Join(
            "|",
            observation.TenantId,
            observation.ProjectId,
            observation.OperationId,
            observation.CorrelationId,
            observation.BaseBranch,
            observation.ObservedBaseCommitSha,
            observation.ObservedPatchHash,
            observation.ObservedAtUtc,
            observation.RecordedAtUtc,
            observation.SurfaceKind,
            observation.SurfaceId,
            observation.ReferenceKind,
            observation.ReferenceId,
            observation.Source,
            observation.IsRedacted,
            observation.RedactionReason);

    private static string PatchArtifactDistinctionKey(PatchArtifactFreshnessMetadata patch) =>
        string.Join(
            ":",
            patch.PatchKind,
            patch.BaseBranch,
            patch.CorrelationId,
            patch.Source,
            patch.SurfaceKind,
            patch.SurfaceId,
            patch.ReferenceKind,
            patch.ReferenceId);

    private static string BaseObservationDistinctionKey(BaseBranchObservationMetadata observation) =>
        string.Join(
            ":",
            observation.BaseBranch,
            observation.CorrelationId,
            observation.SurfaceKind,
            observation.SurfaceId,
            observation.ReferenceKind,
            observation.ReferenceId);

    private static PatchBaseFreshnessResolverResult InvalidResult(
        string tenantId,
        string projectId,
        string operationId,
        DateTimeOffset asOfUtc,
        IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            ResolutionStatus = PatchBaseFreshnessResolutionStatus.InvalidRequest,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            AsOfUtc = asOfUtc,
            Assessments = [],
            AmbiguousPatchBaseMetadata = [],
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = [],
            ForbiddenAuthorityImplications = PatchBaseFreshnessResolverValidator.ForbiddenAuthorityImplications
        };

    private static PatchBaseFreshnessResolverResult Result(
        PatchBaseFreshnessResolverRequest request,
        PatchBaseFreshnessResolutionStatus status,
        IReadOnlyList<PatchBaseFreshnessAssessment> assessments,
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
            AmbiguousPatchBaseMetadata = ambiguous,
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = warnings,
            ForbiddenAuthorityImplications = PatchBaseFreshnessResolverValidator.ForbiddenAuthorityImplications
        };
}
