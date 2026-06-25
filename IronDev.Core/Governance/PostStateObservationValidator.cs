using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class PostStateObservationValidator
{
    public const int MaxObservationAgeSeconds = 900;

    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "post-state observation is read only",
        "post-state observation is evidence only",
        "post-state observation is not source safety",
        "post-state observation is not retry authority",
        "post-state observation is not recovery authority",
        "post-state observation is not rollback authority",
        "post-state observation is not resume authority",
        "post-state observation is not mutation authority",
        "observation completeness is not authority",
        "observation trust level is not authority",
        "fresh authority is required before any mutation"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "post-state observation is not mutation execution",
        "post-state observation is not retry execution",
        "post-state observation is not recovery execution",
        "post-state observation is not rollback execution",
        "post-state observation is not approval",
        "post-state observation is not policy satisfaction",
        "post-state observation is not validation freshness",
        "post-state observation is not patch freshness",
        "post-state observation is not source apply authority",
        "post-state observation is not commit authority",
        "post-state observation is not push authority",
        "post-state observation is not pull request authority",
        "post-state observation is not workflow continuation",
        "post-state observation is not merge readiness",
        "post-state observation is not release readiness",
        "post-state observation is not deployment readiness"
    ];

    private static readonly string[] RawPayloadMarkers =
    [
        "raw patch",
        "patch payload",
        "raw diff",
        "diff --git",
        "raw source",
        "source file content",
        "raw commit body",
        "raw pr body",
        string.Concat("raw ", "gi", "t output"),
        string.Concat("git", "hub response"),
        "raw rollback output",
        "command text",
        "shell command",
        "api request body",
        "provider response body",
        "raw payload",
        "raw evidence",
        "raw receipt",
        "private reasoning",
        "chain-of-thought",
        "scratchpad"
    ];

    private static readonly string[] CredentialMarkers =
    [
        "authorization:",
        string.Concat("bear", "er "),
        string.Concat("to", "ken", "="),
        string.Concat("access_", "to", "ken", "="),
        string.Concat("sec", "ret", "="),
        string.Concat("pass", "word", "="),
        string.Concat("private ", "key"),
        "connection string",
        "credential material",
        string.Concat("-----", "BEGIN")
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "approval granted",
        "policy satisfied",
        "source safe",
        "safe to mutate",
        "safe to retry",
        "safe to recover",
        "safe to rollback",
        "retry authorized",
        "recovery authorized",
        "rollback authorized",
        "ready to execute",
        "ready to mutate",
        "merge now",
        "release now",
        "deploy now"
    ];

    public static PostStateObservationValidationResult ValidateRequest(PostStateObservationRequest? request)
    {
        if (request is null)
        {
            return Result(["PostStateObservationRequestRequired"], [], true);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddScopeIssues(request, issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.AttemptRef, "PostStateObservationAttemptRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.TargetRef, "PostStateObservationTargetRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.ObservationRef, "PostStateObservationObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PreStateRef, "PostStateObservationPreStateRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PreStateFingerprint, "PostStateObservationPreStateFingerprint", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedPostStateRef, "PostStateObservationExpectedPostStateRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedPostStateFingerprint, "PostStateObservationExpectedPostStateFingerprint", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedPostStateRef, "PostStateObservationObservedPostStateRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedPostStateFingerprint, "PostStateObservationObservedPostStateFingerprint", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.FailureClassificationRef, "PostStateObservationFailureClassificationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.FailureClassRef, "PostStateObservationFailureClassRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.FailureReceiptRef, "PostStateObservationFailureReceiptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.MutationReceiptRef, "PostStateObservationMutationReceiptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ProviderStateRef, "PostStateObservationProviderStateRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ReadModelStateRef, "PostStateObservationReadModelStateRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ConcurrentGuardDecisionRef, "PostStateObservationConcurrentGuardDecisionRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.LeaseObservationRef, "PostStateObservationLeaseObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.IdempotencyKeyRef, "PostStateObservationIdempotencyKeyRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.IdempotencyFingerprint, "PostStateObservationIdempotencyFingerprint", issues, ref unsafePayload);
        AddTimestampIssues(request.ObservedAtUtc, "PostStateObservationObservedAtUtc", issues);
        AddTimestampIssues(request.RecordedAtUtc, "PostStateObservationRecordedAtUtc", issues);
        AddOptionalExpiryIssues(request, issues);
        AddTimestampOrderingIssues(request, issues);
        AddSafeTextIssues(request.ObserverVersion, "PostStateObservationObserverVersion", issues, ref unsafePayload);
        AddSafeTextIssues(request.ReasonCode, "PostStateObservationReasonCode", issues, ref unsafePayload);
        AddSafeTextIssues(request.Source, "PostStateObservationSource", issues, ref unsafePayload);

        return Result(issues, [], unsafePayload);
    }

    public static string BuildRecordFingerprint(
        PostStateObservationRequest request,
        PostStateObservationDecisionKind decision,
        PostStateBoundarySignal boundarySignal,
        PostStateObservationBlockKind blockKind) =>
        string.Join(
            "|",
            "post-state-observation",
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            request.CorrelationId,
            request.MutationSurface,
            request.AttemptRef,
            request.TargetRef,
            request.ObservationRef,
            request.SubjectKind,
            request.ObservationMethod,
            request.TransitionExpectation,
            request.ObservedTransition,
            request.ObservationCompleteness,
            request.ObservationTrustLevel,
            request.PreStateRef ?? string.Empty,
            request.PreStateFingerprint ?? string.Empty,
            request.ExpectedPostStateRef ?? string.Empty,
            request.ExpectedPostStateFingerprint ?? string.Empty,
            request.ObservedPostStateRef ?? string.Empty,
            request.ObservedPostStateFingerprint ?? string.Empty,
            request.FailureClassificationRef ?? string.Empty,
            request.FailureReceiptRef ?? string.Empty,
            request.MutationReceiptRef ?? string.Empty,
            request.ProviderStateRef ?? string.Empty,
            request.ReadModelStateRef ?? string.Empty,
            request.ConcurrentGuardDecisionRef ?? string.Empty,
            request.LeaseObservationRef ?? string.Empty,
            request.IdempotencyKeyRef ?? string.Empty,
            request.IdempotencyFingerprint ?? string.Empty,
            request.ObservedAtUtc.ToString("O"),
            request.RecordedAtUtc.ToString("O"),
            request.ObservationExpiresAtUtc?.ToString("O") ?? string.Empty,
            request.ObserverVersion,
            decision,
            boundarySignal,
            blockKind);

    public static bool ContainsUnsafeText(string value)
    {
        var text = value.ToLowerInvariant();
        return RawPayloadMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)) ||
            CredentialMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)) ||
            AuthorityMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    private static PostStateObservationValidationResult Result(
        IReadOnlyList<string> issues,
        IReadOnlyList<string> missingEvidence,
        bool unsafePayload)
    {
        var normalizedIssues = issues
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static issue => issue, StringComparer.Ordinal)
            .ToArray();
        var normalizedMissing = missingEvidence
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static issue => issue, StringComparer.Ordinal)
            .ToArray();

        return new PostStateObservationValidationResult
        {
            IsValid = normalizedIssues.Length == 0 && normalizedMissing.Length == 0,
            Issues = normalizedIssues,
            MissingEvidence = normalizedMissing,
            HasUnsafePayload = unsafePayload,
            Warnings = RequiredWarnings,
            ForbiddenAuthorityImplications = RequiredForbiddenAuthorityImplications
        };
    }

    private static void AddScopeIssues(
        PostStateObservationRequest request,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        AddScopeIdIssues(request.TenantId, "PostStateObservationTenantIdRequired", "PostStateObservationTenantIdInvalid", issues, ref unsafePayload);
        AddScopeIdIssues(request.ProjectId, "PostStateObservationProjectIdRequired", "PostStateObservationProjectIdInvalid", issues, ref unsafePayload);

        var operationValidation = OperationIdentityValidator.ValidateOperationId(request.OperationId);
        if (!operationValidation.IsValid)
        {
            issues.Add(string.IsNullOrWhiteSpace(request.OperationId)
                ? "PostStateObservationOperationIdRequired"
                : "PostStateObservationOperationIdInvalid");
        }

        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            issues.Add("PostStateObservationCorrelationIdRequired");
        }
        else if (!CorrelationIdPattern().IsMatch(request.CorrelationId) ||
            ContainsUnsafeText(request.CorrelationId))
        {
            issues.Add("PostStateObservationCorrelationIdInvalid");
            unsafePayload = unsafePayload || ContainsUnsafeText(request.CorrelationId);
        }

        if (request.MutationSurface == MutationLeaseSurfaceKind.Unknown ||
            !Enum.IsDefined(request.MutationSurface))
        {
            issues.Add("PostStateObservationMutationSurfaceRequired");
        }
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
            issues.Add("PostStateObservationUnsafePayloadRejected");
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

    private static void AddTimestampIssues(
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

    private static void AddOptionalExpiryIssues(
        PostStateObservationRequest request,
        ICollection<string> issues)
    {
        if (request.ObservationExpiresAtUtc is not { } expiresAt)
        {
            return;
        }

        if (expiresAt.Offset != TimeSpan.Zero)
        {
            issues.Add("PostStateObservationExpiresAtUtcMustBeUtc");
        }

        if (request.ObservedAtUtc != default &&
            request.ObservedAtUtc.Offset == TimeSpan.Zero &&
            expiresAt.Offset == TimeSpan.Zero &&
            expiresAt <= request.ObservedAtUtc)
        {
            issues.Add("PostStateObservationExpiresAtUtcBeforeObservedAtUtc");
        }
    }

    private static void AddTimestampOrderingIssues(
        PostStateObservationRequest request,
        ICollection<string> issues)
    {
        if (request.ObservedAtUtc != default &&
            request.RecordedAtUtc != default &&
            request.ObservedAtUtc.Offset == TimeSpan.Zero &&
            request.RecordedAtUtc.Offset == TimeSpan.Zero &&
            request.RecordedAtUtc < request.ObservedAtUtc)
        {
            issues.Add("PostStateObservationRecordedAtUtcBeforeObservedAtUtc");
        }
    }

    private static void AddSafeTextIssues(
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

        if (ContainsUnsafeText(value))
        {
            issues.Add("PostStateObservationUnsafePayloadRejected");
            unsafePayload = true;
            return;
        }

        if (value.Length > 128 ||
            value.Any(static ch => char.IsControl(ch) || char.IsWhiteSpace(ch)) ||
            !SafeTextPattern().IsMatch(value))
        {
            issues.Add($"{issuePrefix}Invalid");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{2,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScopeIdPattern();

    [GeneratedRegex("^corr_[0-9a-z]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:/=@+-]{1,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{1,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTextPattern();
}
