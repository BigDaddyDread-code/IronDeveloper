using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class SafeRetryContractValidator
{
    public const int MaxPriorAttempts = 10;
    public const int MaxRetryCountHardCap = 3;

    public static readonly IReadOnlyList<SafeRetryFailureClass> CandidateFailureClasses =
    [
        SafeRetryFailureClass.PreMutationInfrastructureFailure,
        SafeRetryFailureClass.PreMutationDependencyUnavailable,
        SafeRetryFailureClass.PreMutationTimeout,
        SafeRetryFailureClass.PreMutationLeaseUnavailable,
        SafeRetryFailureClass.PreMutationConcurrentGuardBlocked
    ];

    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "safe retry contract is read only",
        "retry classification is not retry authority",
        "failed attempt is not permission to retry",
        "idempotency match is not retry authority",
        "new idempotency key is not new authority",
        "concurrent guard not blocking is not retry authority",
        "retry budget is not retry permission",
        "fresh authority is required before any retry",
        "fresh validation is required before any retry",
        "fresh post-state observation is required before any retry"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "safe retry contract is not mutation authority",
        "safe retry contract is not retry execution",
        "safe retry contract is not resume authority",
        "safe retry contract is not recovery authority",
        "safe retry contract is not rollback authority",
        "safe retry contract is not approval",
        "safe retry contract is not policy satisfaction",
        "safe retry contract is not validation freshness",
        "safe retry contract is not patch freshness",
        "safe retry contract is not source safety",
        "safe retry contract is not source apply authority",
        "safe retry contract is not commit authority",
        "safe retry contract is not push authority",
        "safe retry contract is not pull request authority",
        "safe retry contract is not workflow continuation"
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
        string.Concat("raw ", "gi", "t output"),
        string.Concat("git", "hub response"),
        "raw rollback output",
        "command text",
        "shell command",
        "api request body",
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
        string.Concat("token", "="),
        string.Concat("access_", "to", "ken", "="),
        string.Concat("secret", "="),
        string.Concat("password", "="),
        string.Concat("private ", "key"),
        "connection string",
        "credential material",
        string.Concat("-----", "BEGIN")
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "approval granted",
        "policy satisfied",
        "safe to retry",
        "retry authorized",
        "retry approved",
        "ready to retry",
        "ready to mutate",
        "ready to execute",
        "resume authorized",
        "recovery authorized",
        "rollback authorized",
        "merge now",
        "release now",
        "deploy now"
    ];

    public static SafeRetryContractValidationResult ValidateRequest(SafeRetryAssessmentRequest? request)
    {
        if (request is null)
        {
            return Result(["SafeRetryAssessmentRequestRequired"], [], true);
        }

        var issues = new List<string>();
        var missingReceiptEvidence = new List<string>();
        var unsafePayload = false;

        AddScopeIssues(request, issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.FailedAttemptRef, "SafeRetryFailedAttemptRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.PreviousIdempotencyKeyRef, "SafeRetryPreviousIdempotencyKeyRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.PreviousIdempotencyFingerprint, "SafeRetryPreviousIdempotencyFingerprint", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.ProposedRetryAttemptRef, "SafeRetryProposedRetryAttemptRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.ProposedRetryIdempotencyKeyRef, "SafeRetryProposedRetryIdempotencyKeyRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.ProposedRetryIdempotencyFingerprint, "SafeRetryProposedRetryIdempotencyFingerprint", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.RetryLineageRef, "SafeRetryRetryLineageRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.CurrentGuardDecisionRef, "SafeRetryCurrentGuardDecisionRef", issues, ref unsafePayload);
        AddReceiptReferenceIssues(request.FailureReceiptRef, "SafeRetryFailureReceiptRef", missingReceiptEvidence, ref unsafePayload);
        AddReceiptReferenceIssues(request.TerminalOutcomeRef, "SafeRetryTerminalOutcomeRef", missingReceiptEvidence, ref unsafePayload);
        AddReceiptReferenceIssues(request.PostStateObservationRef, "SafeRetryPostStateObservationRef", missingReceiptEvidence, ref unsafePayload);
        AddRetryBudgetIssues(request, issues);
        AddEnumShapeIssues(request, issues);
        AddTimestampIssues(request.AssessedAtUtc, "SafeRetryAssessedAtUtc", issues);
        AddTimestampIssues(request.NowUtc, "SafeRetryNowUtc", issues);
        AddTimestampOrderingIssues(request, issues);
        AddSafeTextIssues(request.ReasonCode, "SafeRetryReasonCode", issues, ref unsafePayload);
        AddSafeTextIssues(request.Source, "SafeRetrySource", issues, ref unsafePayload);

        return Result(issues, missingReceiptEvidence, unsafePayload);
    }

    public static SafeRetryContractValidationResult ValidateLineage(
        SafeRetryAssessmentRequest request,
        SafeRetryLineageReadResult? readResult)
    {
        if (readResult is null)
        {
            return Result([], ["SafeRetryLineageReadResultRequired"], false);
        }

        var issues = new List<string>();
        var missingReceiptEvidence = new List<string>();
        var unsafePayload = false;

        AddRequiredReferenceIssues(readResult.RetryLineageRef, "SafeRetryLineageReadResultRetryLineageRef", issues, ref unsafePayload);

        if (!Same(readResult.RetryLineageRef, request.RetryLineageRef))
        {
            missingReceiptEvidence.Add("SafeRetryLineageRefMismatch");
        }

        if (readResult.ObservedPriorRetryCount < 0)
        {
            issues.Add("SafeRetryObservedPriorRetryCountNonNegativeRequired");
        }

        if (readResult.WasTruncated ||
            readResult.PriorAttempts.Count > MaxPriorAttempts)
        {
            issues.Add("SafeRetryLineageReadWindowTruncated");
        }

        AddOptionalSafeTextIssues(readResult.TruncationReason, "SafeRetryLineageTruncationReason", issues, ref unsafePayload);

        foreach (var attempt in readResult.PriorAttempts.OrderBy(static attempt => attempt.AttemptRef, StringComparer.Ordinal))
        {
            var attemptIssues = ValidatePriorAttempt(request, attempt);
            issues.AddRange(attemptIssues.Issues);
            missingReceiptEvidence.AddRange(attemptIssues.MissingReceiptEvidence);
            unsafePayload = unsafePayload || attemptIssues.HasUnsafePayload;
        }

        return Result(issues, missingReceiptEvidence, unsafePayload);
    }

    public static string BuildRecordFingerprint(
        SafeRetryAssessmentRequest request,
        SafeRetryAssessmentDecisionKind decision,
        SafeRetryAssessmentBlockKind blockKind) =>
        string.Join(
            "|",
            "safe-retry-contract",
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            request.CorrelationId,
            request.MutationSurface,
            request.MutationTargetRef,
            request.FailedAttemptRef,
            request.FailureClass,
            request.MutationBoundaryState,
            request.FailureReceiptRef,
            request.TerminalOutcomeRef,
            request.PostStateObservationRef,
            request.PreviousIdempotencyKeyRef,
            request.PreviousIdempotencyFingerprint,
            request.ProposedRetryAttemptRef,
            request.ProposedRetryIdempotencyKeyRef,
            request.ProposedRetryIdempotencyFingerprint,
            request.RetryLineageRef,
            request.CurrentGuardDecision,
            request.CurrentGuardDecisionRef,
            decision,
            blockKind);

    public static bool IsCandidateFailureClass(SafeRetryFailureClass failureClass) =>
        CandidateFailureClasses.Contains(failureClass);

    public static bool ContainsUnsafeText(string value)
    {
        var text = value.ToLowerInvariant();
        return RawPayloadMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)) ||
            CredentialMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)) ||
            AuthorityMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    private static SafeRetryContractValidationResult ValidatePriorAttempt(
        SafeRetryAssessmentRequest request,
        SafeRetryPriorAttemptMetadata? attempt)
    {
        if (attempt is null)
        {
            return Result([], ["SafeRetryPriorAttemptRequired"], true);
        }

        var issues = new List<string>();
        var missingReceiptEvidence = new List<string>();
        var unsafePayload = false;

        AddScopeIdIssues(attempt.TenantId, "SafeRetryPriorAttemptTenantIdRequired", "SafeRetryPriorAttemptTenantIdInvalid", issues, ref unsafePayload);
        AddScopeIdIssues(attempt.ProjectId, "SafeRetryPriorAttemptProjectIdRequired", "SafeRetryPriorAttemptProjectIdInvalid", issues, ref unsafePayload);
        AddRequiredReferenceIssues(attempt.MutationTargetRef, "SafeRetryPriorAttemptMutationTargetRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(attempt.AttemptRef, "SafeRetryPriorAttemptAttemptRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(attempt.RetryLineageRef, "SafeRetryPriorAttemptRetryLineageRef", issues, ref unsafePayload);
        AddTimestampIssues(attempt.ObservedAtUtc, "SafeRetryPriorAttemptObservedAtUtc", issues);

        if (attempt.MutationSurface == MutationLeaseSurfaceKind.Unknown || !Enum.IsDefined(attempt.MutationSurface))
        {
            issues.Add("SafeRetryPriorAttemptMutationSurfaceRequired");
        }

        if (attempt.Outcome == SafeRetryAttemptOutcome.Unknown || !Enum.IsDefined(attempt.Outcome))
        {
            issues.Add("SafeRetryPriorAttemptOutcomeRequired");
        }

        if (attempt.FailureClass == SafeRetryFailureClass.Unknown || !Enum.IsDefined(attempt.FailureClass))
        {
            issues.Add("SafeRetryPriorAttemptFailureClassRequired");
        }

        if (!Same(attempt.TenantId, request.TenantId))
        {
            missingReceiptEvidence.Add("SafeRetryLineageTenantMismatch");
        }

        if (!Same(attempt.ProjectId, request.ProjectId))
        {
            missingReceiptEvidence.Add("SafeRetryLineageProjectMismatch");
        }

        if (attempt.MutationSurface != request.MutationSurface)
        {
            missingReceiptEvidence.Add("SafeRetryLineageMutationSurfaceMismatch");
        }

        if (!Same(attempt.MutationTargetRef, request.MutationTargetRef))
        {
            missingReceiptEvidence.Add("SafeRetryLineageMutationTargetMismatch");
        }

        if (!Same(attempt.RetryLineageRef, request.RetryLineageRef))
        {
            missingReceiptEvidence.Add("SafeRetryPriorAttemptLineageMismatch");
        }

        return Result(issues, missingReceiptEvidence, unsafePayload);
    }

    private static SafeRetryContractValidationResult Result(
        IReadOnlyList<string> issues,
        IReadOnlyList<string> missingReceiptEvidence,
        bool unsafePayload)
    {
        var normalizedIssues = issues
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static issue => issue, StringComparer.Ordinal)
            .ToArray();
        var normalizedMissing = missingReceiptEvidence
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static issue => issue, StringComparer.Ordinal)
            .ToArray();

        return new SafeRetryContractValidationResult
        {
            IsValid = normalizedIssues.Length == 0 && normalizedMissing.Length == 0,
            Issues = normalizedIssues,
            MissingReceiptEvidence = normalizedMissing,
            Warnings = RequiredWarnings,
            ForbiddenAuthorityImplications = RequiredForbiddenAuthorityImplications,
            HasUnsafePayload = unsafePayload
        };
    }

    private static void AddScopeIssues(
        SafeRetryAssessmentRequest request,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        AddScopeIdIssues(request.TenantId, "SafeRetryTenantIdRequired", "SafeRetryTenantIdInvalid", issues, ref unsafePayload);
        AddScopeIdIssues(request.ProjectId, "SafeRetryProjectIdRequired", "SafeRetryProjectIdInvalid", issues, ref unsafePayload);

        var operationValidation = OperationIdentityValidator.ValidateOperationId(request.OperationId);
        if (!operationValidation.IsValid)
        {
            issues.Add(string.IsNullOrWhiteSpace(request.OperationId)
                ? "SafeRetryOperationIdRequired"
                : "SafeRetryOperationIdInvalid");
        }

        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            issues.Add("SafeRetryCorrelationIdRequired");
        }
        else if (!CorrelationIdPattern().IsMatch(request.CorrelationId) || ContainsUnsafeText(request.CorrelationId))
        {
            issues.Add("SafeRetryCorrelationIdInvalid");
            unsafePayload = unsafePayload || ContainsUnsafeText(request.CorrelationId);
        }

        if (request.MutationSurface == MutationLeaseSurfaceKind.Unknown || !Enum.IsDefined(request.MutationSurface))
        {
            issues.Add("SafeRetryMutationSurfaceRequired");
        }

        AddRequiredReferenceIssues(request.MutationTargetRef, "SafeRetryMutationTargetRef", issues, ref unsafePayload);
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

    private static void AddReceiptReferenceIssues(
        string? value,
        string issuePrefix,
        ICollection<string> missingReceiptEvidence,
        ref bool unsafePayload)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missingReceiptEvidence.Add($"{issuePrefix}Required");
            return;
        }

        var issues = new List<string>();
        AddReferenceIssues(value, $"{issuePrefix}Invalid", issues, ref unsafePayload);
        foreach (var issue in issues)
        {
            missingReceiptEvidence.Add(issue);
        }
    }

    private static void AddReferenceIssues(
        string value,
        string invalidIssue,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        if (ContainsUnsafeText(value))
        {
            issues.Add("SafeRetryUnsafePayloadRejected");
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

    private static void AddRetryBudgetIssues(
        SafeRetryAssessmentRequest request,
        ICollection<string> issues)
    {
        if (request.PriorRetryCount < 0)
        {
            issues.Add("SafeRetryPriorRetryCountNonNegativeRequired");
        }

        if (request.MaxRetryCount <= 0)
        {
            issues.Add("SafeRetryMaxRetryCountPositiveRequired");
        }

        if (request.MaxRetryCount > MaxRetryCountHardCap)
        {
            issues.Add("SafeRetryMaxRetryCountHardCapExceeded");
        }
    }

    private static void AddEnumShapeIssues(
        SafeRetryAssessmentRequest request,
        ICollection<string> issues)
    {
        if (!Enum.IsDefined(request.FailedAttemptOutcome))
        {
            issues.Add("SafeRetryFailedAttemptOutcomeRequired");
        }

        if (!Enum.IsDefined(request.FailureClass))
        {
            issues.Add("SafeRetryFailureClassRequired");
        }

        if (!Enum.IsDefined(request.MutationBoundaryState))
        {
            issues.Add("SafeRetryMutationBoundaryStateRequired");
        }

        if (!Enum.IsDefined(request.CurrentGuardDecision))
        {
            issues.Add("SafeRetryCurrentGuardDecisionRequired");
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
        SafeRetryAssessmentRequest request,
        ICollection<string> issues)
    {
        if (request.AssessedAtUtc != default &&
            request.NowUtc != default &&
            request.AssessedAtUtc.Offset == TimeSpan.Zero &&
            request.NowUtc.Offset == TimeSpan.Zero &&
            request.AssessedAtUtc > request.NowUtc)
        {
            issues.Add("SafeRetryAssessedAtUtcAfterNowUtc");
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

        AddSafeTextShapeIssues(value, $"{issuePrefix}Invalid", issues, ref unsafePayload);
    }

    private static void AddOptionalSafeTextIssues(
        string? value,
        string issuePrefix,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        AddSafeTextShapeIssues(value, $"{issuePrefix}Invalid", issues, ref unsafePayload);
    }

    private static void AddSafeTextShapeIssues(
        string value,
        string invalidIssue,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        if (ContainsUnsafeText(value))
        {
            issues.Add("SafeRetryUnsafePayloadRejected");
            unsafePayload = true;
            return;
        }

        if (value.Length > 128 ||
            value.Any(static ch => char.IsControl(ch) || char.IsWhiteSpace(ch)) ||
            !SafeTextPattern().IsMatch(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{2,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScopeIdPattern();

    [GeneratedRegex("^corr_[0-9a-z]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:/=@+-]{1,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{1,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTextPattern();
}
