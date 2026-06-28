using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class GatewayFailureClassificationValidator
{
    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "gateway failure classification is read only",
        "failure classification is not retry authority",
        "failure classification is not recovery authority",
        "failure classification is not rollback authority",
        "failure classification is not resume authority",
        "failure classification is not mutation authority",
        "routing hint is not executor eligibility",
        "failure evidence is not approval",
        "failure receipt is not permission",
        "post-state observation is not source safety approval",
        "fresh authority is required before any mutation"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "gateway failure classification is not mutation execution",
        "gateway failure classification is not retry execution",
        "gateway failure classification is not recovery execution",
        "gateway failure classification is not rollback execution",
        "gateway failure classification is not approval",
        "gateway failure classification is not policy satisfaction",
        "gateway failure classification is not validation freshness",
        "gateway failure classification is not patch freshness",
        "gateway failure classification is not source safety",
        "gateway failure classification is not source apply authority",
        "gateway failure classification is not commit authority",
        "gateway failure classification is not push authority",
        "gateway failure classification is not pull request authority",
        "gateway failure classification is not workflow continuation",
        "gateway failure classification is not merge readiness",
        "gateway failure classification is not release readiness",
        "gateway failure classification is not deployment readiness"
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

    public static GatewayFailureClassificationValidationResult ValidateRequest(
        GatewayFailureClassificationRequest? request)
    {
        if (request is null)
        {
            return Result(["GatewayFailureClassificationRequestRequired"], [], true);
        }

        var issues = new List<string>();
        var missingEvidence = new List<string>();
        var unsafePayload = false;

        AddScopeIssues(request, issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.AttemptRef, "GatewayFailureAttemptRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.GatewayRef, "GatewayFailureGatewayRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.FailureRef, "GatewayFailureFailureRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.FailureEvidenceRef, "GatewayFailureEvidenceRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.FailureReceiptRef, "GatewayFailureReceiptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PostStateObservationRef, "GatewayFailurePostStateObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ConcurrentGuardDecisionRef, "GatewayFailureConcurrentGuardDecisionRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.LeaseObservationRef, "GatewayFailureLeaseObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.IdempotencyKeyRef, "GatewayFailureIdempotencyKeyRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.IdempotencyFingerprint, "GatewayFailureIdempotencyFingerprint", issues, ref unsafePayload);
        AddTimestampIssues(request.ObservedAtUtc, "GatewayFailureObservedAtUtc", issues);
        AddTimestampIssues(request.ClassifiedAtUtc, "GatewayFailureClassifiedAtUtc", issues);
        AddTimestampOrderingIssues(request, issues);
        AddSafeTextIssues(request.ClassifierVersion, "GatewayFailureClassifierVersion", issues, ref unsafePayload);
        AddSafeTextIssues(request.ReasonCode, "GatewayFailureReasonCode", issues, ref unsafePayload);
        AddSafeTextIssues(request.Source, "GatewayFailureSource", issues, ref unsafePayload);
        AddEvidencePresenceIssues(request, missingEvidence);

        return Result(issues, missingEvidence, unsafePayload);
    }

    public static string BuildRecordFingerprint(
        GatewayFailureClassificationRequest request,
        GatewayFailureClassificationDecisionKind decision,
        GatewayFailureRoutingHint routingHint,
        GatewayFailureClassificationBlockKind blockKind) =>
        string.Join(
            "|",
            "gateway-failure-classification",
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            request.CorrelationId,
            request.MutationSurface,
            request.AttemptRef,
            request.GatewayRef,
            request.FailureRef,
            request.FailurePhase,
            request.FailureClass,
            request.MutationBoundaryState,
            request.FailureEvidenceRef ?? string.Empty,
            request.FailureReceiptRef ?? string.Empty,
            request.PostStateObservationRef ?? string.Empty,
            request.ConcurrentGuardDecisionRef ?? string.Empty,
            request.LeaseObservationRef ?? string.Empty,
            request.IdempotencyKeyRef ?? string.Empty,
            request.IdempotencyFingerprint ?? string.Empty,
            request.ObservedAtUtc.ToString("O"),
            request.ClassifiedAtUtc.ToString("O"),
            request.ClassifierVersion,
            decision,
            routingHint,
            blockKind);

    public static bool ContainsUnsafeText(string value)
    {
        var text = value.ToLowerInvariant();
        return RawPayloadMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)) ||
            CredentialMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)) ||
            AuthorityMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    private static GatewayFailureClassificationValidationResult Result(
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

        return new GatewayFailureClassificationValidationResult
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
        GatewayFailureClassificationRequest request,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        AddScopeIdIssues(request.TenantId, "GatewayFailureTenantIdRequired", "GatewayFailureTenantIdInvalid", issues, ref unsafePayload);
        AddScopeIdIssues(request.ProjectId, "GatewayFailureProjectIdRequired", "GatewayFailureProjectIdInvalid", issues, ref unsafePayload);

        var operationValidation = OperationIdentityValidator.ValidateOperationId(request.OperationId);
        if (!operationValidation.IsValid)
        {
            issues.Add(string.IsNullOrWhiteSpace(request.OperationId)
                ? "GatewayFailureOperationIdRequired"
                : "GatewayFailureOperationIdInvalid");
        }

        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            issues.Add("GatewayFailureCorrelationIdRequired");
        }
        else if (!CorrelationIdPattern().IsMatch(request.CorrelationId) ||
            ContainsUnsafeText(request.CorrelationId))
        {
            issues.Add("GatewayFailureCorrelationIdInvalid");
            unsafePayload = unsafePayload || ContainsUnsafeText(request.CorrelationId);
        }

        if (request.MutationSurface == MutationLeaseSurfaceKind.Unknown ||
            !Enum.IsDefined(request.MutationSurface))
        {
            issues.Add("GatewayFailureMutationSurfaceRequired");
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
            issues.Add("GatewayFailureUnsafePayloadRejected");
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

    private static void AddTimestampOrderingIssues(
        GatewayFailureClassificationRequest request,
        ICollection<string> issues)
    {
        if (request.ObservedAtUtc != default &&
            request.ClassifiedAtUtc != default &&
            request.ObservedAtUtc.Offset == TimeSpan.Zero &&
            request.ClassifiedAtUtc.Offset == TimeSpan.Zero &&
            request.ClassifiedAtUtc < request.ObservedAtUtc)
        {
            issues.Add("GatewayFailureClassifiedAtUtcBeforeObservedAtUtc");
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
            issues.Add("GatewayFailureUnsafePayloadRejected");
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

    private static void AddEvidencePresenceIssues(
        GatewayFailureClassificationRequest request,
        ICollection<string> missingEvidence)
    {
        if (string.IsNullOrWhiteSpace(request.FailureEvidenceRef) &&
            string.IsNullOrWhiteSpace(request.FailureReceiptRef))
        {
            missingEvidence.Add("GatewayFailureEvidenceOrReceiptRefRequired");
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
