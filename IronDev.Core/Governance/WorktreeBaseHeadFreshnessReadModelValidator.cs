using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class WorktreeBaseHeadFreshnessReadModelValidator
{
    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "worktree/base/head freshness read model is read-only",
        "worktree/base/head freshness read model is metadata-only",
        "worktree/base/head freshness read model uses supplied metadata only",
        "worktree/base/head freshness read model uses supplied AsOfUtc only",
        "worktree/base/head freshness read model is not operation identity",
        "worktree/base/head freshness read model is not operation lookup",
        "worktree/base/head freshness read model is not correlation authority",
        "worktree/base/head freshness read model is not timeline assembly",
        "worktree/base/head freshness read model is not status projection",
        "worktree/base/head freshness read model is not missing evidence calculation",
        "worktree/base/head freshness read model is not forbidden-action resolution",
        "worktree/base/head freshness read model is not receipt resolution",
        "worktree/base/head freshness read model is not evidence resolution",
        "worktree/base/head freshness read model is not validation staleness resolution",
        "worktree/base/head freshness read model is not patch/base freshness resolution",
        "worktree/base/head freshness read model is not validation execution",
        "worktree/base/head freshness read model is not raw patch resolution",
        "worktree/base/head freshness read model is not raw diff resolution",
        "worktree/base/head freshness read model is not source inspection",
        "worktree/base/head freshness read model is not Git inspection",
        "worktree/base/head freshness read model is not policy satisfaction",
        "worktree/base/head freshness read model is not approval",
        "worktree/base/head freshness read model is not next-safe-action formatting",
        "worktree/base/head freshness read model is not authority-warning formatting",
        "worktree/base/head freshness read model is not source apply",
        "worktree/base/head freshness read model is not rollback",
        "worktree/base/head freshness read model is not retry permission",
        "worktree/base/head freshness read model is not commit",
        "worktree/base/head freshness read model is not push",
        "worktree/base/head freshness read model is not PR creation",
        "worktree/base/head freshness read model is not merge readiness",
        "worktree/base/head freshness read model is not release readiness",
        "worktree/base/head freshness read model is not deployment readiness",
        "worktree/base/head freshness read model is not memory promotion",
        "worktree/base/head freshness read model is not workflow continuation",
        "fresh worktree/base/head metadata is not authority",
        "clean worktree metadata is not commit permission",
        "matching head metadata is not push permission",
        "matching base metadata is not merge readiness",
        "dirty worktree metadata is not policy denial",
        "detached head metadata is not next-safe-action selection",
        "complete worktree/base/head assessment is not action allowed"
    ];

    public static WorktreeBaseHeadFreshnessReadModel ValidateRequest(WorktreeBaseHeadFreshnessReadModelRequest? request)
    {
        if (request is null)
        {
            return Invalid(["WorktreeBaseHeadFreshnessReadModelRequestRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(request.TenantId, "WorktreeBaseHeadFreshnessTenantIdRequired", "WorktreeBaseHeadFreshnessTenantIdInvalid", issues);
        AddScopeIssues(request.ProjectId, "WorktreeBaseHeadFreshnessProjectIdRequired", "WorktreeBaseHeadFreshnessProjectIdInvalid", issues);
        AddOperationIdIssues(request.OperationId, issues);

        if (request.AsOfUtc == default)
        {
            issues.Add("WorktreeBaseHeadFreshnessAsOfUtcRequired");
        }

        if (request.Rules is null)
        {
            issues.Add("WorktreeBaseHeadFreshnessRulesRequired");
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
                    issues.Add("WorktreeBaseHeadFreshnessRuleTenantMismatch");
                }

                if (!Same(request.ProjectId, rule.ProjectId))
                {
                    issues.Add("WorktreeBaseHeadFreshnessRuleProjectMismatch");
                }

                if (!Same(request.OperationId, rule.OperationId))
                {
                    issues.Add("WorktreeBaseHeadFreshnessRuleOperationMismatch");
                }
            }
        }

        if (request.Expectations is null)
        {
            issues.Add("WorktreeBaseHeadExpectationsRequired");
        }
        else
        {
            foreach (var expectation in request.Expectations)
            {
                AddExpectationIssues(expectation, issues);
                if (expectation is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, expectation.TenantId))
                {
                    issues.Add("WorktreeBaseHeadExpectationTenantMismatch");
                }

                if (!Same(request.ProjectId, expectation.ProjectId))
                {
                    issues.Add("WorktreeBaseHeadExpectationProjectMismatch");
                }

                if (!Same(request.OperationId, expectation.OperationId))
                {
                    issues.Add("WorktreeBaseHeadExpectationOperationMismatch");
                }
            }
        }

        if (request.Observations is null)
        {
            issues.Add("WorktreeBaseHeadObservationsRequired");
        }
        else
        {
            foreach (var observation in request.Observations)
            {
                AddObservationIssues(observation, issues);
                if (observation is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, observation.TenantId))
                {
                    issues.Add("WorktreeBaseHeadObservationTenantMismatch");
                }

                if (!Same(request.ProjectId, observation.ProjectId))
                {
                    issues.Add("WorktreeBaseHeadObservationProjectMismatch");
                }

                if (!Same(request.OperationId, observation.OperationId))
                {
                    issues.Add("WorktreeBaseHeadObservationOperationMismatch");
                }
            }
        }

        return Result(
            issues,
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            request.AsOfUtc,
            WorktreeBaseHeadFreshnessResolutionStatus.InvalidRequest);
    }

    private static void AddRuleIssues(
        WorktreeBaseHeadFreshnessRule? rule,
        ICollection<string> issues)
    {
        if (rule is null)
        {
            issues.Add("WorktreeBaseHeadFreshnessRuleRequired");
            return;
        }

        AddScopeIssues(rule.TenantId, "WorktreeBaseHeadFreshnessRuleTenantIdRequired", "WorktreeBaseHeadFreshnessRuleTenantIdInvalid", issues);
        AddScopeIssues(rule.ProjectId, "WorktreeBaseHeadFreshnessRuleProjectIdRequired", "WorktreeBaseHeadFreshnessRuleProjectIdInvalid", issues);
        AddOperationIdIssues(rule.OperationId, issues);
        AddIdIssues(rule.RuleId, "WorktreeBaseHeadFreshnessRuleIdRequired", "WorktreeBaseHeadFreshnessRuleIdInvalid", issues);
        AddSourceIssues(rule.Source, "WorktreeBaseHeadFreshnessRuleSourceRequired", "WorktreeBaseHeadFreshnessRuleSourceInvalid", issues);

        if (rule.ObservationFreshFor <= TimeSpan.Zero)
        {
            issues.Add("WorktreeBaseHeadFreshnessRuleFreshForInvalid");
        }

        if (rule.ObservationExpiresAfter <= TimeSpan.Zero)
        {
            issues.Add("WorktreeBaseHeadFreshnessRuleExpiresAfterInvalid");
        }

        if (rule.ObservationFreshFor > TimeSpan.Zero &&
            rule.ObservationExpiresAfter > TimeSpan.Zero &&
            rule.ObservationExpiresAfter < rule.ObservationFreshFor)
        {
            issues.Add("WorktreeBaseHeadFreshnessRuleExpiresBeforeFreshWindow");
        }

        if (rule.CreatedAtUtc == default)
        {
            issues.Add("WorktreeBaseHeadFreshnessRuleCreatedAtRequired");
        }
    }

    private static void AddExpectationIssues(
        ExpectedWorktreeBaseHeadMetadata? expectation,
        ICollection<string> issues)
    {
        if (expectation is null)
        {
            issues.Add("ExpectedWorktreeBaseHeadMetadataRequired");
            return;
        }

        AddScopeIssues(expectation.TenantId, "WorktreeBaseHeadExpectationTenantIdRequired", "WorktreeBaseHeadExpectationTenantIdInvalid", issues);
        AddScopeIssues(expectation.ProjectId, "WorktreeBaseHeadExpectationProjectIdRequired", "WorktreeBaseHeadExpectationProjectIdInvalid", issues);
        AddOperationIdIssues(expectation.OperationId, issues);
        AddCorrelationIdIssues(expectation.CorrelationId, expectation.OperationId, "WorktreeBaseHeadExpectationCorrelationIdRequired", "WorktreeBaseHeadExpectationCorrelationIdInvalid", issues);
        AddIdIssues(expectation.ExpectationId, "WorktreeBaseHeadExpectationIdRequired", "WorktreeBaseHeadExpectationIdInvalid", issues);
        AddRepositoryIdentityIssues(expectation.RepositoryIdentity, "WorktreeBaseHeadExpectationRepositoryIdentityRequired", "WorktreeBaseHeadExpectationRepositoryIdentityInvalid", issues);
        AddBranchIssues(expectation.BaseBranch, "WorktreeBaseHeadExpectationBaseBranchRequired", "WorktreeBaseHeadExpectationBaseBranchInvalid", issues);
        AddCommitShaIssues(expectation.BaseCommitSha, "WorktreeBaseHeadExpectationBaseCommitShaRequired", "WorktreeBaseHeadExpectationBaseCommitShaInvalid", issues);
        AddWorktreeStateIssues(expectation.ExpectedWorktreeState, "WorktreeBaseHeadExpectationWorktreeStateRequired", issues);
        AddHeadStateIssues(expectation.ExpectedHeadState, "WorktreeBaseHeadExpectationHeadStateRequired", issues);
        AddHeadIdentityIssues(
            expectation.ExpectedHeadState,
            expectation.HeadBranch,
            expectation.HeadCommitSha,
            "WorktreeBaseHeadExpectationHeadBranchRequired",
            "WorktreeBaseHeadExpectationHeadBranchInvalid",
            "WorktreeBaseHeadExpectationHeadCommitShaRequired",
            "WorktreeBaseHeadExpectationHeadCommitShaInvalid",
            issues);
        AddSurfaceIssues(expectation.SurfaceKind, expectation.SurfaceId, "WorktreeBaseHeadExpectationSurfaceKindRequired", "WorktreeBaseHeadExpectationSurfaceIdRequired", "WorktreeBaseHeadExpectationSurfaceIdInvalid", issues);
        AddReferencePairIssues(expectation.ReferenceKind, expectation.ReferenceId, "WorktreeBaseHeadExpectationReferenceKindRequired", "WorktreeBaseHeadExpectationReferenceIdRequired", "WorktreeBaseHeadExpectationReferenceIdInvalid", issues);
        AddSourceIssues(expectation.Source, "WorktreeBaseHeadExpectationSourceRequired", "WorktreeBaseHeadExpectationSourceInvalid", issues);

        if (expectation.CapturedAtUtc == default)
        {
            issues.Add("WorktreeBaseHeadExpectationCapturedAtRequired");
        }

        if (expectation.RecordedAtUtc == default)
        {
            issues.Add("WorktreeBaseHeadExpectationRecordedAtRequired");
        }

        if (expectation.CapturedAtUtc != default &&
            expectation.RecordedAtUtc != default &&
            expectation.RecordedAtUtc < expectation.CapturedAtUtc)
        {
            issues.Add("WorktreeBaseHeadExpectationRecordedBeforeCaptured");
        }

        AddRedactionIssues(
            expectation.IsRedacted,
            expectation.RedactionReason,
            "WorktreeBaseHeadExpectationRedactionReasonRequired",
            "WorktreeBaseHeadExpectationRedactionReasonInvalid",
            issues);
    }

    private static void AddObservationIssues(
        ObservedWorktreeBaseHeadMetadata? observation,
        ICollection<string> issues)
    {
        if (observation is null)
        {
            issues.Add("ObservedWorktreeBaseHeadMetadataRequired");
            return;
        }

        AddScopeIssues(observation.TenantId, "WorktreeBaseHeadObservationTenantIdRequired", "WorktreeBaseHeadObservationTenantIdInvalid", issues);
        AddScopeIssues(observation.ProjectId, "WorktreeBaseHeadObservationProjectIdRequired", "WorktreeBaseHeadObservationProjectIdInvalid", issues);
        AddOperationIdIssues(observation.OperationId, issues);
        AddCorrelationIdIssues(observation.CorrelationId, observation.OperationId, "WorktreeBaseHeadObservationCorrelationIdRequired", "WorktreeBaseHeadObservationCorrelationIdInvalid", issues);
        AddIdIssues(observation.ObservationId, "WorktreeBaseHeadObservationIdRequired", "WorktreeBaseHeadObservationIdInvalid", issues);
        AddRepositoryIdentityIssues(observation.RepositoryIdentity, "WorktreeBaseHeadObservationRepositoryIdentityRequired", "WorktreeBaseHeadObservationRepositoryIdentityInvalid", issues);
        AddWorktreeStateIssues(observation.WorktreeState, "WorktreeBaseHeadObservationWorktreeStateRequired", issues);
        AddHeadStateIssues(observation.HeadState, "WorktreeBaseHeadObservationHeadStateRequired", issues);
        AddBranchIssues(observation.BaseBranch, "WorktreeBaseHeadObservationBaseBranchRequired", "WorktreeBaseHeadObservationBaseBranchInvalid", issues);
        AddCommitShaIssues(observation.BaseCommitSha, "WorktreeBaseHeadObservationBaseCommitShaRequired", "WorktreeBaseHeadObservationBaseCommitShaInvalid", issues);
        AddHeadIdentityIssues(
            observation.HeadState,
            observation.HeadBranch,
            observation.HeadCommitSha,
            "WorktreeBaseHeadObservationHeadBranchRequired",
            "WorktreeBaseHeadObservationHeadBranchInvalid",
            "WorktreeBaseHeadObservationHeadCommitShaRequired",
            "WorktreeBaseHeadObservationHeadCommitShaInvalid",
            issues);
        AddSurfaceIssues(observation.SurfaceKind, observation.SurfaceId, "WorktreeBaseHeadObservationSurfaceKindRequired", "WorktreeBaseHeadObservationSurfaceIdRequired", "WorktreeBaseHeadObservationSurfaceIdInvalid", issues);
        AddReferencePairIssues(observation.ReferenceKind, observation.ReferenceId, "WorktreeBaseHeadObservationReferenceKindRequired", "WorktreeBaseHeadObservationReferenceIdRequired", "WorktreeBaseHeadObservationReferenceIdInvalid", issues);
        AddSourceIssues(observation.Source, "WorktreeBaseHeadObservationSourceRequired", "WorktreeBaseHeadObservationSourceInvalid", issues);

        if (observation.ObservedAtUtc == default)
        {
            issues.Add("WorktreeBaseHeadObservationObservedAtRequired");
        }

        if (observation.RecordedAtUtc == default)
        {
            issues.Add("WorktreeBaseHeadObservationRecordedAtRequired");
        }

        if (observation.ObservedAtUtc != default &&
            observation.RecordedAtUtc != default &&
            observation.RecordedAtUtc < observation.ObservedAtUtc)
        {
            issues.Add("WorktreeBaseHeadObservationRecordedBeforeObserved");
        }

        AddRedactionIssues(
            observation.IsRedacted,
            observation.RedactionReason,
            "WorktreeBaseHeadObservationRedactionReasonRequired",
            "WorktreeBaseHeadObservationRedactionReasonInvalid",
            issues);
    }

    private static void AddWorktreeStateIssues(
        WorktreeStateKind state,
        string requiredIssue,
        ICollection<string> issues)
    {
        if (state == WorktreeStateKind.Unknown ||
            !Enum.IsDefined(state))
        {
            issues.Add(requiredIssue);
        }
    }

    private static void AddHeadStateIssues(
        HeadStateKind state,
        string requiredIssue,
        ICollection<string> issues)
    {
        if (state == HeadStateKind.Unknown ||
            !Enum.IsDefined(state))
        {
            issues.Add(requiredIssue);
        }
    }

    private static void AddHeadIdentityIssues(
        HeadStateKind headState,
        string? headBranch,
        string? headCommitSha,
        string branchRequiredIssue,
        string branchInvalidIssue,
        string commitRequiredIssue,
        string commitInvalidIssue,
        ICollection<string> issues)
    {
        if (headState == HeadStateKind.Attached)
        {
            AddBranchIssues(headBranch, branchRequiredIssue, branchInvalidIssue, issues);
        }
        else if (!string.IsNullOrWhiteSpace(headBranch))
        {
            AddBranchIssues(headBranch, branchRequiredIssue, branchInvalidIssue, issues);
        }

        if (headState != HeadStateKind.Missing)
        {
            AddCommitShaIssues(headCommitSha, commitRequiredIssue, commitInvalidIssue, issues);
        }
        else if (!string.IsNullOrWhiteSpace(headCommitSha))
        {
            AddCommitShaIssues(headCommitSha, commitRequiredIssue, commitInvalidIssue, issues);
        }
    }

    private static void AddRepositoryIdentityIssues(
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
            IsUrl(value))
        {
            issues.Add(invalidIssue);
        }
    }

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

    private static WorktreeBaseHeadFreshnessReadModel Invalid(IReadOnlyList<string> issues) =>
        Result(
            issues,
            string.Empty,
            string.Empty,
            string.Empty,
            default,
            WorktreeBaseHeadFreshnessResolutionStatus.InvalidRequest);

    private static WorktreeBaseHeadFreshnessReadModel Result(
        IReadOnlyList<string> issues,
        string tenantId,
        string projectId,
        string operationId,
        DateTimeOffset asOfUtc,
        WorktreeBaseHeadFreshnessResolutionStatus status) =>
        new()
        {
            IsValid = issues.Count == 0,
            ResolutionStatus = issues.Count == 0 ? WorktreeBaseHeadFreshnessResolutionStatus.NoObservations : status,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            AsOfUtc = asOfUtc,
            Assessments = [],
            AmbiguousObservations = [],
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = issues.Count == 0 ? Warnings() : [],
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    internal static IReadOnlyList<string> Warnings() =>
    [
        "worktree/base/head freshness is metadata-only",
        "worktree/base/head freshness uses supplied AsOfUtc only",
        "fresh worktree/base/head metadata is not authority",
        "clean worktree metadata is not commit permission",
        "matching head metadata is not push permission",
        "matching base metadata is not merge readiness",
        "stale or expired observation metadata does not choose next safe action",
        "ambiguous worktree/base/head observations do not choose a winner",
        "complete worktree/base/head assessment is not action allowed"
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
            "ready to apply",
            "ready to commit",
            "ready to push",
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
            "raw source",
            "source content",
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
}
