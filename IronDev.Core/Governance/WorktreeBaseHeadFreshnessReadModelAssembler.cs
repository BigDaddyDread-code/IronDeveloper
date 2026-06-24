namespace IronDev.Core.Governance;

public static class WorktreeBaseHeadFreshnessReadModelAssembler
{
    public static WorktreeBaseHeadFreshnessReadModel Assemble(WorktreeBaseHeadFreshnessReadModelRequest? request)
    {
        var validation = WorktreeBaseHeadFreshnessReadModelValidator.ValidateRequest(request);
        if (!validation.IsValid || request is null)
        {
            return InvalidResult(
                request?.TenantId ?? string.Empty,
                request?.ProjectId ?? string.Empty,
                request?.OperationId ?? string.Empty,
                request?.AsOfUtc ?? default,
                validation.Issues);
        }

        if (request.Observations.Count == 0)
        {
            return Result(
                request,
                WorktreeBaseHeadFreshnessResolutionStatus.NoObservations,
                [],
                [],
                [],
                WorktreeBaseHeadFreshnessReadModelValidator.Warnings());
        }

        var ambiguity = FindAmbiguity(request);
        if (ambiguity.Count > 0)
        {
            return Result(
                request,
                WorktreeBaseHeadFreshnessResolutionStatus.AmbiguousObservations,
                [],
                ambiguity,
                [],
                WorktreeBaseHeadFreshnessReadModelValidator.Warnings());
        }

        var assessments = BuildAssessments(request)
            .OrderBy(static assessment => assessment.RepositoryIdentity, StringComparer.Ordinal)
            .ThenBy(static assessment => assessment.ExpectationId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static assessment => assessment.ObservationId ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

        var status = DetermineStatus(assessments);
        var issues = assessments
            .Where(static assessment => assessment.FreshnessState == WorktreeBaseHeadFreshnessState.Unassessable)
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
            WorktreeBaseHeadFreshnessReadModelValidator.Warnings());
    }

    private static IReadOnlyList<string> FindAmbiguity(WorktreeBaseHeadFreshnessReadModelRequest request)
    {
        var ambiguous = new List<string>();

        AddDuplicateIds(
            request.Rules.Select(static rule => rule.RuleId),
            "DuplicateWorktreeBaseHeadFreshnessRuleId",
            ambiguous);
        AddDuplicateIds(
            request.Expectations.Select(static expectation => expectation.ExpectationId),
            "DuplicateWorktreeBaseHeadExpectationId",
            ambiguous);
        AddDuplicateIds(
            request.Observations.Select(static observation => observation.ObservationId),
            "DuplicateWorktreeBaseHeadObservationId",
            ambiguous);

        foreach (var group in request.Rules.GroupBy(static rule => rule.RuleId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(RuleFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingWorktreeBaseHeadFreshnessRuleMetadata:{group.Key}");
            }
        }

        foreach (var group in request.Expectations.GroupBy(static expectation => expectation.ExpectationId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(ExpectationFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingWorktreeBaseHeadExpectationMetadata:{group.Key}");
            }
        }

        foreach (var group in request.Observations.GroupBy(static observation => observation.ObservationId, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Select(ObservationFingerprint).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                ambiguous.Add($"ConflictingWorktreeBaseHeadObservationMetadata:{group.Key}");
            }
        }

        if (request.Rules.Count > 1)
        {
            ambiguous.Add("MultipleWorktreeBaseHeadFreshnessRules");
        }

        foreach (var group in request.Expectations.GroupBy(ExpectationDistinctionKey, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                ambiguous.Add($"IndistinguishableWorktreeBaseHeadExpectations:{group.Key}");
            }
        }

        foreach (var group in request.Observations.GroupBy(ObservationDistinctionKey, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                ambiguous.Add($"IndistinguishableWorktreeBaseHeadObservations:{group.Key}");
            }
        }

        foreach (var expectation in request.Expectations)
        {
            var matchingObservations = MatchingObservations(request, expectation).ToArray();
            if (matchingObservations.Length > 1)
            {
                ambiguous.Add($"MultipleWorktreeBaseHeadObservationsForExpectation:{expectation.ExpectationId}");
            }
        }

        return ambiguous
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<WorktreeBaseHeadFreshnessAssessment> BuildAssessments(WorktreeBaseHeadFreshnessReadModelRequest request)
    {
        var matchedObservationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var expectation in request.Expectations)
        {
            var observation = MatchingObservations(request, expectation).SingleOrDefault();
            if (observation is null)
            {
                yield return MissingObservationAssessment(expectation);
                continue;
            }

            matchedObservationIds.Add(observation.ObservationId);
            yield return AssessPair(request, expectation, observation);
        }

        foreach (var observation in request.Observations
            .Where(observation => !matchedObservationIds.Contains(observation.ObservationId)))
        {
            yield return MissingExpectationAssessment(observation);
        }
    }

    private static WorktreeBaseHeadFreshnessAssessment AssessPair(
        WorktreeBaseHeadFreshnessReadModelRequest request,
        ExpectedWorktreeBaseHeadMetadata expectation,
        ObservedWorktreeBaseHeadMetadata observation)
    {
        var age = request.AsOfUtc - observation.ObservedAtUtc;
        if (request.AsOfUtc < observation.ObservedAtUtc)
        {
            return Assessment(
                expectation,
                observation,
                WorktreeBaseHeadFreshnessState.Unassessable,
                age,
                null,
                null,
                null,
                "WorktreeBaseHeadObservationObservedAfterAsOf");
        }

        var rule = request.Rules.SingleOrDefault();
        if (rule is null)
        {
            return Assessment(
                expectation,
                observation,
                WorktreeBaseHeadFreshnessState.MissingRule,
                age,
                null,
                null,
                null,
                "WorktreeBaseHeadFreshnessRuleMissing");
        }

        var ruleId = rule.RuleId;
        if (rule.RequireRepositoryIdentityMatch &&
            !string.Equals(expectation.RepositoryIdentity, observation.RepositoryIdentity, StringComparison.OrdinalIgnoreCase))
        {
            return Assessment(expectation, observation, WorktreeBaseHeadFreshnessState.RepositoryMismatch, age, null, null, ruleId, "RepositoryMismatch");
        }

        if (observation.HasConflicts ||
            observation.WorktreeState == WorktreeStateKind.Conflicted)
        {
            return Assessment(expectation, observation, WorktreeBaseHeadFreshnessState.WorktreeConflicted, age, null, null, ruleId, "WorktreeConflicted");
        }

        if (rule.RequireWorktreeClean &&
            (observation.HasUncommittedChanges ||
             observation.HasUntrackedFiles ||
             observation.WorktreeState == WorktreeStateKind.Dirty ||
             observation.WorktreeState == WorktreeStateKind.UntrackedOnly))
        {
            return Assessment(expectation, observation, WorktreeBaseHeadFreshnessState.WorktreeChanged, age, null, null, ruleId, "WorktreeChanged");
        }

        if (observation.HeadState == HeadStateKind.Missing)
        {
            return Assessment(expectation, observation, WorktreeBaseHeadFreshnessState.HeadMissing, age, null, null, ruleId, "HeadMissing");
        }

        if (rule.RequireAttachedHead &&
            observation.HeadState == HeadStateKind.Detached)
        {
            return Assessment(expectation, observation, WorktreeBaseHeadFreshnessState.HeadDetached, age, null, null, ruleId, "HeadDetached");
        }

        if (rule.RequireBaseBranchMatch &&
            !string.Equals(expectation.BaseBranch, observation.BaseBranch, StringComparison.OrdinalIgnoreCase))
        {
            return Assessment(expectation, observation, WorktreeBaseHeadFreshnessState.BaseMoved, age, null, null, ruleId, "BaseBranchMoved");
        }

        if (rule.RequireBaseCommitMatch &&
            !string.Equals(expectation.BaseCommitSha, observation.BaseCommitSha, StringComparison.OrdinalIgnoreCase))
        {
            return Assessment(expectation, observation, WorktreeBaseHeadFreshnessState.BaseMoved, age, null, null, ruleId, "BaseCommitMoved");
        }

        if (rule.RequireHeadBranchMatch &&
            !string.Equals(expectation.HeadBranch, observation.HeadBranch, StringComparison.OrdinalIgnoreCase))
        {
            return Assessment(expectation, observation, WorktreeBaseHeadFreshnessState.HeadMoved, age, null, null, ruleId, "HeadBranchMoved");
        }

        if (rule.RequireHeadCommitMatch &&
            !string.Equals(expectation.HeadCommitSha, observation.HeadCommitSha, StringComparison.OrdinalIgnoreCase))
        {
            return Assessment(expectation, observation, WorktreeBaseHeadFreshnessState.HeadMoved, age, null, null, ruleId, "HeadCommitMoved");
        }

        var freshUntilUtc = observation.ObservedAtUtc + rule.ObservationFreshFor;
        var expiresAtUtc = observation.ObservedAtUtc + rule.ObservationExpiresAfter;
        var state = age <= rule.ObservationFreshFor
            ? WorktreeBaseHeadFreshnessState.Fresh
            : age <= rule.ObservationExpiresAfter
                ? WorktreeBaseHeadFreshnessState.ObservationStale
                : WorktreeBaseHeadFreshnessState.ObservationExpired;

        return Assessment(
            expectation,
            observation,
            state,
            age,
            freshUntilUtc,
            expiresAtUtc,
            ruleId,
            state switch
            {
                WorktreeBaseHeadFreshnessState.Fresh => "WorktreeBaseHeadFresh",
                WorktreeBaseHeadFreshnessState.ObservationStale => "WorktreeBaseHeadObservationStale",
                _ => "WorktreeBaseHeadObservationExpired"
            });
    }

    private static IEnumerable<ObservedWorktreeBaseHeadMetadata> MatchingObservations(
        WorktreeBaseHeadFreshnessReadModelRequest request,
        ExpectedWorktreeBaseHeadMetadata expectation) =>
        request.Observations.Where(observation =>
            string.Equals(observation.TenantId, expectation.TenantId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observation.ProjectId, expectation.ProjectId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observation.OperationId, expectation.OperationId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observation.CorrelationId, expectation.CorrelationId, StringComparison.OrdinalIgnoreCase));

    private static WorktreeBaseHeadFreshnessResolutionStatus DetermineStatus(
        IReadOnlyCollection<WorktreeBaseHeadFreshnessAssessment> assessments)
    {
        if (assessments.Any(static assessment => assessment.FreshnessState == WorktreeBaseHeadFreshnessState.Unassessable))
        {
            return WorktreeBaseHeadFreshnessResolutionStatus.Unassessable;
        }

        if (assessments.Any(static assessment => assessment.FreshnessState == WorktreeBaseHeadFreshnessState.MissingRule))
        {
            return WorktreeBaseHeadFreshnessResolutionStatus.MissingRules;
        }

        if (assessments.Any(static assessment => assessment.FreshnessState == WorktreeBaseHeadFreshnessState.MissingExpectation))
        {
            return WorktreeBaseHeadFreshnessResolutionStatus.MissingExpectations;
        }

        if (assessments.Any(static assessment => assessment.FreshnessState == WorktreeBaseHeadFreshnessState.MissingObservation))
        {
            return WorktreeBaseHeadFreshnessResolutionStatus.MissingObservations;
        }

        var distinctStates = assessments
            .Select(static assessment => assessment.FreshnessState)
            .Distinct()
            .Count();

        return distinctStates > 1
            ? WorktreeBaseHeadFreshnessResolutionStatus.MixedFreshness
            : WorktreeBaseHeadFreshnessResolutionStatus.Assessed;
    }

    private static WorktreeBaseHeadFreshnessAssessment MissingObservationAssessment(ExpectedWorktreeBaseHeadMetadata expectation) =>
        new()
        {
            ExpectationId = expectation.ExpectationId,
            ObservationId = null,
            FreshnessState = WorktreeBaseHeadFreshnessState.MissingObservation,
            RepositoryIdentity = expectation.RepositoryIdentity,
            ExpectedBaseBranch = expectation.BaseBranch,
            ObservedBaseBranch = null,
            ExpectedBaseCommitSha = expectation.BaseCommitSha,
            ObservedBaseCommitSha = null,
            ExpectedHeadBranch = expectation.HeadBranch,
            ObservedHeadBranch = null,
            ExpectedHeadCommitSha = expectation.HeadCommitSha,
            ObservedHeadCommitSha = null,
            ExpectedWorktreeState = expectation.ExpectedWorktreeState,
            ObservedWorktreeState = null,
            ExpectedHeadState = expectation.ExpectedHeadState,
            ObservedHeadState = null,
            HasUncommittedChanges = false,
            HasUntrackedFiles = false,
            HasConflicts = false,
            ObservedAtUtc = null,
            Age = TimeSpan.Zero,
            FreshUntilUtc = null,
            ExpiresAtUtc = null,
            RuleId = null,
            SurfaceKind = expectation.SurfaceKind,
            SurfaceId = expectation.SurfaceId,
            ReferenceKind = expectation.ReferenceKind,
            ReferenceId = expectation.ReferenceId,
            IsRedacted = expectation.IsRedacted,
            Reason = "WorktreeBaseHeadObservationMissing"
        };

    private static WorktreeBaseHeadFreshnessAssessment MissingExpectationAssessment(ObservedWorktreeBaseHeadMetadata observation) =>
        new()
        {
            ExpectationId = null,
            ObservationId = observation.ObservationId,
            FreshnessState = WorktreeBaseHeadFreshnessState.MissingExpectation,
            RepositoryIdentity = observation.RepositoryIdentity,
            ExpectedBaseBranch = null,
            ObservedBaseBranch = observation.BaseBranch,
            ExpectedBaseCommitSha = null,
            ObservedBaseCommitSha = observation.BaseCommitSha,
            ExpectedHeadBranch = null,
            ObservedHeadBranch = observation.HeadBranch,
            ExpectedHeadCommitSha = null,
            ObservedHeadCommitSha = observation.HeadCommitSha,
            ExpectedWorktreeState = null,
            ObservedWorktreeState = observation.WorktreeState,
            ExpectedHeadState = null,
            ObservedHeadState = observation.HeadState,
            HasUncommittedChanges = observation.HasUncommittedChanges,
            HasUntrackedFiles = observation.HasUntrackedFiles,
            HasConflicts = observation.HasConflicts,
            ObservedAtUtc = observation.ObservedAtUtc,
            Age = TimeSpan.Zero,
            FreshUntilUtc = null,
            ExpiresAtUtc = null,
            RuleId = null,
            SurfaceKind = observation.SurfaceKind,
            SurfaceId = observation.SurfaceId,
            ReferenceKind = observation.ReferenceKind,
            ReferenceId = observation.ReferenceId,
            IsRedacted = observation.IsRedacted,
            Reason = "WorktreeBaseHeadExpectationMissing"
        };

    private static WorktreeBaseHeadFreshnessAssessment Assessment(
        ExpectedWorktreeBaseHeadMetadata expectation,
        ObservedWorktreeBaseHeadMetadata observation,
        WorktreeBaseHeadFreshnessState state,
        TimeSpan age,
        DateTimeOffset? freshUntilUtc,
        DateTimeOffset? expiresAtUtc,
        string? ruleId,
        string reason) =>
        new()
        {
            ExpectationId = expectation.ExpectationId,
            ObservationId = observation.ObservationId,
            FreshnessState = state,
            RepositoryIdentity = expectation.RepositoryIdentity,
            ExpectedBaseBranch = expectation.BaseBranch,
            ObservedBaseBranch = observation.BaseBranch,
            ExpectedBaseCommitSha = expectation.BaseCommitSha,
            ObservedBaseCommitSha = observation.BaseCommitSha,
            ExpectedHeadBranch = expectation.HeadBranch,
            ObservedHeadBranch = observation.HeadBranch,
            ExpectedHeadCommitSha = expectation.HeadCommitSha,
            ObservedHeadCommitSha = observation.HeadCommitSha,
            ExpectedWorktreeState = expectation.ExpectedWorktreeState,
            ObservedWorktreeState = observation.WorktreeState,
            ExpectedHeadState = expectation.ExpectedHeadState,
            ObservedHeadState = observation.HeadState,
            HasUncommittedChanges = observation.HasUncommittedChanges,
            HasUntrackedFiles = observation.HasUntrackedFiles,
            HasConflicts = observation.HasConflicts,
            ObservedAtUtc = observation.ObservedAtUtc,
            Age = age,
            FreshUntilUtc = freshUntilUtc,
            ExpiresAtUtc = expiresAtUtc,
            RuleId = ruleId,
            SurfaceKind = observation.SurfaceKind,
            SurfaceId = observation.SurfaceId,
            ReferenceKind = observation.ReferenceKind,
            ReferenceId = observation.ReferenceId,
            IsRedacted = expectation.IsRedacted || observation.IsRedacted,
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

    private static string RuleFingerprint(WorktreeBaseHeadFreshnessRule rule) =>
        string.Join(
            "|",
            rule.TenantId,
            rule.ProjectId,
            rule.OperationId,
            rule.RuleId,
            rule.ObservationFreshFor,
            rule.ObservationExpiresAfter,
            rule.RequireRepositoryIdentityMatch,
            rule.RequireWorktreeClean,
            rule.RequireBaseBranchMatch,
            rule.RequireBaseCommitMatch,
            rule.RequireHeadBranchMatch,
            rule.RequireHeadCommitMatch,
            rule.RequireAttachedHead,
            rule.Source,
            rule.CreatedAtUtc);

    private static string ExpectationFingerprint(ExpectedWorktreeBaseHeadMetadata expectation) =>
        string.Join(
            "|",
            expectation.TenantId,
            expectation.ProjectId,
            expectation.OperationId,
            expectation.CorrelationId,
            expectation.ExpectationId,
            expectation.RepositoryIdentity,
            expectation.BaseBranch,
            expectation.BaseCommitSha,
            expectation.HeadBranch,
            expectation.HeadCommitSha,
            expectation.ExpectedWorktreeState,
            expectation.ExpectedHeadState,
            expectation.CapturedAtUtc,
            expectation.RecordedAtUtc,
            expectation.SurfaceKind,
            expectation.SurfaceId,
            expectation.ReferenceKind,
            expectation.ReferenceId,
            expectation.Source,
            expectation.IsRedacted,
            expectation.RedactionReason);

    private static string ObservationFingerprint(ObservedWorktreeBaseHeadMetadata observation) =>
        string.Join(
            "|",
            observation.TenantId,
            observation.ProjectId,
            observation.OperationId,
            observation.CorrelationId,
            observation.ObservationId,
            observation.RepositoryIdentity,
            observation.WorktreeState,
            observation.HeadState,
            observation.BaseBranch,
            observation.BaseCommitSha,
            observation.HeadBranch,
            observation.HeadCommitSha,
            observation.HasUncommittedChanges,
            observation.HasUntrackedFiles,
            observation.HasConflicts,
            observation.ObservedAtUtc,
            observation.RecordedAtUtc,
            observation.SurfaceKind,
            observation.SurfaceId,
            observation.ReferenceKind,
            observation.ReferenceId,
            observation.Source,
            observation.IsRedacted,
            observation.RedactionReason);

    private static string ExpectationDistinctionKey(ExpectedWorktreeBaseHeadMetadata expectation) =>
        string.Join(
            ":",
            expectation.RepositoryIdentity,
            expectation.BaseBranch,
            expectation.BaseCommitSha,
            expectation.HeadBranch,
            expectation.HeadCommitSha,
            expectation.CorrelationId,
            expectation.SurfaceKind,
            expectation.SurfaceId,
            expectation.ReferenceKind,
            expectation.ReferenceId);

    private static string ObservationDistinctionKey(ObservedWorktreeBaseHeadMetadata observation) =>
        string.Join(
            ":",
            observation.RepositoryIdentity,
            observation.BaseBranch,
            observation.BaseCommitSha,
            observation.HeadBranch,
            observation.HeadCommitSha,
            observation.CorrelationId,
            observation.SurfaceKind,
            observation.SurfaceId,
            observation.ReferenceKind,
            observation.ReferenceId);

    private static WorktreeBaseHeadFreshnessReadModel InvalidResult(
        string tenantId,
        string projectId,
        string operationId,
        DateTimeOffset asOfUtc,
        IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            ResolutionStatus = WorktreeBaseHeadFreshnessResolutionStatus.InvalidRequest,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            AsOfUtc = asOfUtc,
            Assessments = [],
            AmbiguousObservations = [],
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = [],
            ForbiddenAuthorityImplications = WorktreeBaseHeadFreshnessReadModelValidator.ForbiddenAuthorityImplications
        };

    private static WorktreeBaseHeadFreshnessReadModel Result(
        WorktreeBaseHeadFreshnessReadModelRequest request,
        WorktreeBaseHeadFreshnessResolutionStatus status,
        IReadOnlyList<WorktreeBaseHeadFreshnessAssessment> assessments,
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
            AmbiguousObservations = ambiguous,
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = warnings,
            ForbiddenAuthorityImplications = WorktreeBaseHeadFreshnessReadModelValidator.ForbiddenAuthorityImplications
        };
}
