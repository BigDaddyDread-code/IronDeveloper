using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class ConcurrentMutationGuardValidator
{
    public const int MaxObservations = 25;
    public const int ObservationFreshnessWindowSeconds = 900;

    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "concurrent mutation guard is read only",
        "concurrent mutation guard prevents overlap only",
        "no conflict found is not authority to mutate",
        "idempotency match is not replay authority"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "concurrent mutation guard is not mutation authority",
        "concurrent mutation guard is not lease authority",
        "concurrent mutation guard is not lock acquisition",
        "concurrent mutation guard is not lock release",
        "concurrent mutation guard is not lock renewal",
        "concurrent mutation guard is not lock enforcement",
        "concurrent mutation guard is not approval",
        "concurrent mutation guard is not policy satisfaction",
        "concurrent mutation guard is not validation freshness",
        "concurrent mutation guard is not patch freshness",
        "concurrent mutation guard is not source safety",
        "concurrent mutation guard is not source apply authority",
        "concurrent mutation guard is not commit authority",
        "concurrent mutation guard is not push authority",
        "concurrent mutation guard is not pull request authority",
        "concurrent mutation guard is not ready-for-review authority",
        "concurrent mutation guard is not reviewer-request authority",
        "concurrent mutation guard is not retry authority",
        "concurrent mutation guard is not resume authority",
        "concurrent mutation guard is not rollback authority",
        "concurrent mutation guard is not recovery authority",
        "concurrent mutation guard is not merge readiness",
        "concurrent mutation guard is not release readiness",
        "concurrent mutation guard is not deployment readiness",
        "concurrent mutation guard is not workflow continuation",
        "lease token is not mutation authority",
        "fence token is not mutation authority",
        "idempotency key match is not permission to replay mutation",
        "allowed to proceed to next gate is not allowed to mutate"
    ];

    private static readonly string[] RawPayloadMarkers =
    [
        "diff --git",
        "@@",
        "patch",
        "source code",
        "commit body",
        string.Concat("gi", "t output"),
        string.Concat("git", "hub response"),
        string.Concat("access_", "token"),
        "authorization:",
        string.Concat("-----", "BEGIN"),
        "secret",
        "password",
        "raw payload",
        "raw evidence",
        "raw receipt"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "approved",
        "approval granted",
        "policy satisfied",
        "safe to mutate",
        "execute",
        "apply patch",
        "commit now",
        "push now",
        "open pr",
        "merge",
        "release",
        "deploy",
        "retry authorized",
        "resume authorized",
        "rollback authorized"
    ];

    public static ConcurrentMutationGuardValidationResult ValidateRequest(
        ConcurrentMutationGuardRequest? request)
    {
        if (request is null)
        {
            return Result(["ConcurrentMutationGuardRequestRequired"], true);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddScopeIssues(
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            request.CorrelationId,
            request.MutationSurface,
            request.MutationTargetRef,
            issues,
            ref unsafePayload);

        AddRequiredReferenceIssues(request.RequestedAttemptRef, "ConcurrentMutationGuardRequestedAttemptRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.IdempotencyKeyRef, "ConcurrentMutationGuardIdempotencyKeyRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.IdempotencyFingerprint, "ConcurrentMutationGuardIdempotencyFingerprint", issues, ref unsafePayload);
        AddRequiredTimestampIssues(request.NowUtc, "ConcurrentMutationGuardNowUtc", issues);
        AddObservedLeaseMetadataIssues(request, issues, ref unsafePayload);

        return Result(issues, unsafePayload);
    }

    public static ConcurrentMutationGuardValidationResult ValidateObservation(
        ConcurrentMutationObservation? observation,
        DateTimeOffset nowUtc)
    {
        if (observation is null)
        {
            return Result(["ConcurrentMutationObservationRequired"], true);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddScopeIssues(
            observation.TenantId,
            observation.ProjectId,
            string.Empty,
            "corr_0000000000000000",
            observation.MutationSurface,
            observation.MutationTargetRef,
            issues,
            ref unsafePayload,
            requireOperationAndCorrelation: false);

        AddRequiredReferenceIssues(observation.AttemptRef, "ConcurrentMutationObservationAttemptRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(observation.IdempotencyKeyRef, "ConcurrentMutationObservationIdempotencyKeyRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(observation.IdempotencyFingerprint, "ConcurrentMutationObservationIdempotencyFingerprint", issues, ref unsafePayload);
        AddObservationStateIssues(observation.ObservedState, issues);
        AddRequiredTimestampIssues(observation.ObservedStartedAtUtc, "ConcurrentMutationObservationStartedAtUtc", issues);
        AddObservationUpdatedTimestampIssues(observation, nowUtc, issues);
        AddOptionalTimestampIssues(observation.ObservedExpiresAtUtc, "ConcurrentMutationObservationExpiresAtUtc", issues);

        AddOptionalReferenceIssues(observation.ObservedLeaseRef, "ConcurrentMutationObservationLeaseRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(observation.ObservedLeaseOwnerRef, "ConcurrentMutationObservationLeaseOwnerRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(observation.ObservedFenceRef, "ConcurrentMutationObservationFenceRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(observation.ObservedSequenceRef, "ConcurrentMutationObservationSequenceRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(observation.TerminalOutcomeRef, "ConcurrentMutationObservationTerminalOutcomeRef", issues, ref unsafePayload);
        AddStateSpecificObservationIssues(observation, issues, ref unsafePayload);

        return Result(issues, unsafePayload);
    }

    public static bool IsStaleObservationIssue(string issue) =>
        issue.Contains("Stale", StringComparison.Ordinal) ||
        issue.Contains("Timestamp", StringComparison.Ordinal) ||
        issue.Contains("AtUtc", StringComparison.Ordinal) ||
        issue.Contains("ObservationWindow", StringComparison.Ordinal);

    public static string BuildRecordFingerprint(
        ConcurrentMutationGuardRequest request,
        ConcurrentMutationGuardDecisionKind decision,
        string conflictRef,
        string matchedAttemptRef,
        string matchedIdempotencyKeyRef,
        string matchedLeaseRef,
        string matchedFenceRef,
        string matchedSequenceRef) =>
        string.Join(
            "|",
            "concurrent-mutation-guard",
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            request.CorrelationId,
            request.MutationSurface,
            request.MutationTargetRef,
            request.RequestedAttemptRef,
            request.IdempotencyKeyRef,
            request.IdempotencyFingerprint,
            decision,
            conflictRef,
            matchedAttemptRef,
            matchedIdempotencyKeyRef,
            matchedLeaseRef,
            matchedFenceRef,
            matchedSequenceRef);

    private static ConcurrentMutationGuardValidationResult Result(
        IReadOnlyList<string> issues,
        bool unsafePayload)
    {
        var normalized = issues
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static issue => issue, StringComparer.Ordinal)
            .ToArray();

        return new ConcurrentMutationGuardValidationResult
        {
            IsValid = normalized.Length == 0,
            Issues = normalized,
            Warnings = RequiredWarnings,
            ForbiddenAuthorityImplications = RequiredForbiddenAuthorityImplications,
            HasUnsafePayload = unsafePayload
        };
    }

    private static void AddScopeIssues(
        string? tenantId,
        string? projectId,
        string? operationId,
        string? correlationId,
        MutationLeaseSurfaceKind mutationSurface,
        string? mutationTargetRef,
        ICollection<string> issues,
        ref bool unsafePayload,
        bool requireOperationAndCorrelation = true)
    {
        AddScopeIdIssues(tenantId, "ConcurrentMutationGuardTenantIdRequired", "ConcurrentMutationGuardTenantIdInvalid", issues, ref unsafePayload);
        AddScopeIdIssues(projectId, "ConcurrentMutationGuardProjectIdRequired", "ConcurrentMutationGuardProjectIdInvalid", issues, ref unsafePayload);

        if (requireOperationAndCorrelation)
        {
            var operationValidation = OperationIdentityValidator.ValidateOperationId(operationId);
            if (!operationValidation.IsValid)
            {
                issues.Add(string.IsNullOrWhiteSpace(operationId)
                    ? "ConcurrentMutationGuardOperationIdRequired"
                    : "ConcurrentMutationGuardOperationIdInvalid");
            }

            if (string.IsNullOrWhiteSpace(correlationId))
            {
                issues.Add("ConcurrentMutationGuardCorrelationIdRequired");
            }
            else if (!CorrelationIdPattern().IsMatch(correlationId) || ContainsUnsafeText(correlationId))
            {
                issues.Add("ConcurrentMutationGuardCorrelationIdInvalid");
            }
        }

        if (mutationSurface == MutationLeaseSurfaceKind.Unknown || !Enum.IsDefined(mutationSurface))
        {
            issues.Add("ConcurrentMutationGuardMutationSurfaceRequired");
        }

        AddRequiredReferenceIssues(mutationTargetRef, "ConcurrentMutationGuardMutationTargetRef", issues, ref unsafePayload);
    }

    private static void AddScopeIdIssues(
        string? value,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (ContainsUnsafeText(value))
        {
            issues.Add(invalidIssue);
            unsafePayload = true;
            return;
        }

        if (!ScopeIdPattern().IsMatch(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddRequiredReferenceIssues(
        string? value,
        string issuePrefix,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{issuePrefix}Required");
            return;
        }

        AddReferenceIssues(value, $"{issuePrefix}Invalid", issues, ref unsafePayload);
    }

    private static void AddOptionalReferenceIssues(
        string? value,
        string issuePrefix,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        AddReferenceIssues(value, $"{issuePrefix}Invalid", issues, ref unsafePayload);
    }

    private static void AddReferenceIssues(
        string value,
        string invalidIssue,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        if (ContainsUnsafeText(value))
        {
            issues.Add("ConcurrentMutationGuardUnsafePayloadRejected");
            unsafePayload = true;
            return;
        }

        if (value.Any(char.IsWhiteSpace) ||
            value.Length > 256 ||
            !ReferencePattern().IsMatch(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddRequiredTimestampIssues(
        DateTimeOffset value,
        string issuePrefix,
        ICollection<string> issues)
    {
        if (value == default)
        {
            issues.Add($"{issuePrefix}Required");
            return;
        }

        if (value.Offset != TimeSpan.Zero)
        {
            issues.Add($"{issuePrefix}MustBeUtc");
        }
    }

    private static void AddOptionalTimestampIssues(
        DateTimeOffset? value,
        string issuePrefix,
        ICollection<string> issues)
    {
        if (value is null)
        {
            return;
        }

        if (value.Value == default)
        {
            issues.Add($"{issuePrefix}Required");
            return;
        }

        if (value.Value.Offset != TimeSpan.Zero)
        {
            issues.Add($"{issuePrefix}MustBeUtc");
        }
    }

    private static void AddObservedLeaseMetadataIssues(
        ConcurrentMutationGuardRequest request,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        var hasObservedLeaseMetadata =
            !string.IsNullOrWhiteSpace(request.ObservedLeaseRef) ||
            request.ObservedLeaseState is not null ||
            !string.IsNullOrWhiteSpace(request.ObservedLeaseOwnerRef) ||
            !string.IsNullOrWhiteSpace(request.ObservedFenceRef) ||
            !string.IsNullOrWhiteSpace(request.ObservedSequenceRef) ||
            request.ObservedExpiresAtUtc is not null;

        if (!hasObservedLeaseMetadata)
        {
            return;
        }

        AddRequiredReferenceIssues(request.ObservedLeaseRef, "ConcurrentMutationGuardObservedLeaseRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedLeaseOwnerRef, "ConcurrentMutationGuardObservedLeaseOwnerRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedFenceRef, "ConcurrentMutationGuardObservedFenceRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedSequenceRef, "ConcurrentMutationGuardObservedSequenceRef", issues, ref unsafePayload);
        AddOptionalTimestampIssues(request.ObservedExpiresAtUtc, "ConcurrentMutationGuardObservedExpiresAtUtc", issues);

        if (request.ObservedLeaseState is null ||
            request.ObservedLeaseState == MutationLeaseState.Unknown ||
            !Enum.IsDefined(request.ObservedLeaseState.Value))
        {
            issues.Add("ConcurrentMutationGuardObservedLeaseStateRequired");
        }

        if (request.ObservedLeaseState == MutationLeaseState.ObservedHeld)
        {
            if (string.IsNullOrWhiteSpace(request.ObservedLeaseOwnerRef))
            {
                issues.Add("ConcurrentMutationGuardObservedLeaseOwnerRefRequired");
            }

            if (string.IsNullOrWhiteSpace(request.ObservedFenceRef))
            {
                issues.Add("ConcurrentMutationGuardObservedFenceRefRequired");
            }

            if (string.IsNullOrWhiteSpace(request.ObservedSequenceRef))
            {
                issues.Add("ConcurrentMutationGuardObservedSequenceRefRequired");
            }

            if (request.ObservedExpiresAtUtc is null)
            {
                issues.Add("ConcurrentMutationGuardObservedExpiresAtUtcRequired");
            }
        }
    }

    private static void AddObservationStateIssues(
        ConcurrentMutationObservationState observedState,
        ICollection<string> issues)
    {
        if (observedState == ConcurrentMutationObservationState.Unknown || !Enum.IsDefined(observedState))
        {
            issues.Add("ConcurrentMutationObservationStateUnknown");
        }
    }

    private static void AddObservationUpdatedTimestampIssues(
        ConcurrentMutationObservation observation,
        DateTimeOffset nowUtc,
        ICollection<string> issues)
    {
        AddRequiredTimestampIssues(nowUtc, "ConcurrentMutationGuardNowUtc", issues);

        if (observation.ObservedUpdatedAtUtc is null)
        {
            issues.Add("ConcurrentMutationObservationUpdatedAtUtcRequired");
            return;
        }

        AddOptionalTimestampIssues(observation.ObservedUpdatedAtUtc, "ConcurrentMutationObservationUpdatedAtUtc", issues);

        if (observation.ObservedStartedAtUtc != default &&
            observation.ObservedUpdatedAtUtc.Value < observation.ObservedStartedAtUtc)
        {
            issues.Add("ConcurrentMutationObservationTimestampOrderingInvalid");
        }

        if (nowUtc != default && observation.ObservedUpdatedAtUtc.Value > nowUtc)
        {
            issues.Add("ConcurrentMutationObservationTimestampInFuture");
        }

        if (nowUtc != default &&
            observation.ObservedUpdatedAtUtc.Value <= nowUtc &&
            nowUtc - observation.ObservedUpdatedAtUtc.Value > TimeSpan.FromSeconds(ObservationFreshnessWindowSeconds))
        {
            issues.Add("ConcurrentMutationObservationStale");
        }
    }

    private static void AddStateSpecificObservationIssues(
        ConcurrentMutationObservation observation,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        if (observation.ObservedState == ConcurrentMutationObservationState.ObservedHeld)
        {
            AddRequiredReferenceIssues(observation.ObservedLeaseRef, "ConcurrentMutationObservationLeaseRef", issues, ref unsafePayload);
            AddRequiredReferenceIssues(observation.ObservedLeaseOwnerRef, "ConcurrentMutationObservationLeaseOwnerRef", issues, ref unsafePayload);
            AddRequiredReferenceIssues(observation.ObservedFenceRef, "ConcurrentMutationObservationFenceRef", issues, ref unsafePayload);
            AddRequiredReferenceIssues(observation.ObservedSequenceRef, "ConcurrentMutationObservationSequenceRef", issues, ref unsafePayload);

            if (observation.ObservedExpiresAtUtc is null)
            {
                issues.Add("ConcurrentMutationObservationExpiresAtUtcRequired");
            }
        }

        if (IsTerminalObservationState(observation.ObservedState) &&
            string.IsNullOrWhiteSpace(observation.TerminalOutcomeRef))
        {
            issues.Add("ConcurrentMutationObservationTerminalOutcomeRefRequired");
        }
    }

    private static bool IsTerminalObservationState(ConcurrentMutationObservationState state) =>
        state is ConcurrentMutationObservationState.Succeeded
            or ConcurrentMutationObservationState.Failed
            or ConcurrentMutationObservationState.Cancelled
            or ConcurrentMutationObservationState.Interrupted
            or ConcurrentMutationObservationState.ObservedReleased
            or ConcurrentMutationObservationState.ObservedExpired;

    private static bool ContainsUnsafeText(string value)
    {
        var text = value.ToLowerInvariant();
        return RawPayloadMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)) ||
            AuthorityMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{2,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScopeIdPattern();

    [GeneratedRegex("^corr_[0-9a-z]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:/=@+-]{1,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();
}
