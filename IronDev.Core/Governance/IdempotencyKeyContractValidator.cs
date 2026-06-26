using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class IdempotencyKeyContractValidator
{
    public const int MaxIdempotencyKeyLength = 160;
    public const int MaxObservationAgeSeconds = 900;

    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "idempotency key contract is read only",
        "idempotency key contract does not execute mutation",
        "idempotency key contract does not retry mutation",
        "idempotency key contract does not recover mutation",
        "idempotency key contract does not continue workflow",
        "idempotency key is not authority",
        "idempotency key is not approval",
        "idempotency key is not policy satisfaction",
        "idempotency key is not validation freshness",
        "idempotency key is not source safety",
        "same key is not same authority",
        "duplicate completed evidence is not downstream authority",
        "fresh authority is required before mutation"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "idempotency key is not source apply authority",
        "idempotency key is not commit authority",
        "idempotency key is not push authority",
        "idempotency key is not pull request authority",
        "idempotency key is not ready-for-review authority",
        "idempotency key is not merge authority",
        "idempotency key is not release authority",
        "idempotency key is not deployment authority",
        "idempotency key is not retry authority",
        "idempotency key is not recovery authority",
        "idempotency key is not rollback authority",
        "idempotency key is not workflow continuation authority",
        "idempotency key is not approval",
        "idempotency key is not policy satisfaction",
        "idempotency key is not validation freshness",
        "idempotency key is not source safety",
        "idempotency key is not mutation authority"
    ];

    private static readonly string[] RawPayloadMarkers =
    [
        "raw patch",
        "patch payload",
        "raw diff",
        "diff --git",
        "raw source",
        "source file content",
        "raw command",
        "command text",
        "shell command",
        "raw commit body",
        string.Concat("raw ", "git output"),
        string.Concat("git", "hub response"),
        "provider response body",
        "api request body",
        "json payload",
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
        "safe to execute",
        "safe to retry",
        "safe to mutate",
        "retry authorized",
        "resume authorized",
        "recovery authorized",
        "rollback authorized",
        "workflow continuation authorized",
        "ready to execute",
        "ready to mutate"
    ];

    public static IdempotencyKeyContractValidationResult ValidateRequest(IdempotencyKeyContractRequest? request)
    {
        if (request is null)
        {
            return Result(["IdempotencyKeyContractRequestRequired"], [], unsafePayload: false, hasMalformedKey: false);
        }

        var issues = new List<string>();
        var unsafePayload = false;
        var malformedKey = false;

        AddScopeIssues(request, issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.AttemptRef, "IdempotencyAttemptRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.TargetRef, "IdempotencyTargetRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.RequestRef, "IdempotencyRequestRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.IdempotencyScopeRef, "IdempotencyScopeRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.IdempotencyObservationRef, "IdempotencyObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PriorAttemptRef, "IdempotencyPriorAttemptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PriorReceiptRef, "IdempotencyPriorReceiptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PriorOperationStatusRef, "IdempotencyPriorOperationStatusRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PriorLineageRef, "IdempotencyPriorLineageRef", issues, ref unsafePayload);
        AddRequiredFingerprintIssues(request.RequestFingerprint, "IdempotencyRequestFingerprint", issues, ref unsafePayload);
        AddOptionalFingerprintIssues(request.ExpectedRequestFingerprint, "IdempotencyExpectedRequestFingerprint", issues, ref unsafePayload);
        AddOptionalFingerprintIssues(request.ObservedRequestFingerprint, "IdempotencyObservedRequestFingerprint", issues, ref unsafePayload);
        AddOptionalFingerprintIssues(request.AuthorityFingerprint, "IdempotencyAuthorityFingerprint", issues, ref unsafePayload);
        AddOptionalFingerprintIssues(request.ExpectedAuthorityFingerprint, "IdempotencyExpectedAuthorityFingerprint", issues, ref unsafePayload);
        AddOptionalFingerprintIssues(request.ObservedAuthorityFingerprint, "IdempotencyObservedAuthorityFingerprint", issues, ref unsafePayload);
        AddOptionalFingerprintIssues(request.TargetFingerprint, "IdempotencyTargetFingerprint", issues, ref unsafePayload);
        AddOptionalFingerprintIssues(request.ExpectedTargetFingerprint, "IdempotencyExpectedTargetFingerprint", issues, ref unsafePayload);
        AddOptionalFingerprintIssues(request.ObservedTargetFingerprint, "IdempotencyObservedTargetFingerprint", issues, ref unsafePayload);
        AddOptionalFingerprintIssues(request.EffectFingerprint, "IdempotencyEffectFingerprint", issues, ref unsafePayload);
        AddOptionalFingerprintIssues(request.ExpectedEffectFingerprint, "IdempotencyExpectedEffectFingerprint", issues, ref unsafePayload);
        AddOptionalFingerprintIssues(request.ObservedEffectFingerprint, "IdempotencyObservedEffectFingerprint", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.AuthorityReceiptRef, "IdempotencyAuthorityReceiptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PolicySatisfactionRef, "IdempotencyPolicySatisfactionRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ValidationReceiptRef, "IdempotencyValidationReceiptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ConcurrentGuardDecisionRef, "IdempotencyConcurrentGuardDecisionRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.DirtyWorktreeGuardRef, "IdempotencyDirtyWorktreeGuardRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.MovedBaseGuardRef, "IdempotencyMovedBaseGuardRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.StaleValidationGuardRef, "IdempotencyStaleValidationGuardRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.BranchRemoteHeadVerificationRef, "IdempotencyBranchRemoteHeadVerificationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PostStateObservationRef, "IdempotencyPostStateObservationRef", issues, ref unsafePayload);
        AddTimestampIssues(request.ObservedAtUtc, "IdempotencyObservedAtUtc", issues);
        AddTimestampIssues(request.RecordedAtUtc, "IdempotencyRecordedAtUtc", issues);
        AddOptionalExpiryIssues(request, issues);
        AddTimestampOrderingIssues(request, issues);
        AddSafeTextIssues(request.ContractVersion, "IdempotencyContractVersion", issues, ref unsafePayload);
        AddSafeTextIssues(request.ReasonCode, "IdempotencyReasonCode", issues, ref unsafePayload);
        AddSafeTextIssues(request.Source, "IdempotencySource", issues, ref unsafePayload);
        AddKeyIssues(request.IdempotencyKey, issues, ref unsafePayload, ref malformedKey);

        return Result(issues, [], unsafePayload, malformedKey);
    }

    public static string BuildRecordFingerprint(
        IdempotencyKeyContractRequest? request,
        IdempotencyKeyDecisionKind decision,
        IdempotencyKeyBlockKind blockKind)
    {
        if (request is null)
        {
            return string.Join("|", "idempotency-key-contract", decision, blockKind, "null-request");
        }

        return string.Join(
            "|",
            "idempotency-key-contract",
            SafeFingerprintValue(request.TenantId),
            SafeFingerprintValue(request.ProjectId),
            SafeFingerprintValue(request.OperationId),
            SafeFingerprintValue(request.CorrelationId),
            request.MutationSurface,
            request.SubjectKind,
            SafeFingerprintValue(request.AttemptRef),
            SafeFingerprintValue(request.TargetRef),
            SafeFingerprintValue(request.RequestRef),
            SafeFingerprintValue(request.IdempotencyKey),
            SafeFingerprintValue(request.IdempotencyScopeRef),
            SafeFingerprintValue(request.IdempotencyObservationRef),
            SafeFingerprintValue(request.PriorAttemptRef),
            SafeFingerprintValue(request.PriorReceiptRef),
            SafeFingerprintValue(request.PriorOperationStatusRef),
            SafeFingerprintValue(request.PriorLineageRef),
            SafeFingerprintValue(request.RequestFingerprint),
            SafeFingerprintValue(request.ExpectedRequestFingerprint),
            SafeFingerprintValue(request.ObservedRequestFingerprint),
            SafeFingerprintValue(request.AuthorityFingerprint),
            SafeFingerprintValue(request.ExpectedAuthorityFingerprint),
            SafeFingerprintValue(request.ObservedAuthorityFingerprint),
            SafeFingerprintValue(request.TargetFingerprint),
            SafeFingerprintValue(request.ExpectedTargetFingerprint),
            SafeFingerprintValue(request.ObservedTargetFingerprint),
            SafeFingerprintValue(request.EffectFingerprint),
            SafeFingerprintValue(request.ExpectedEffectFingerprint),
            SafeFingerprintValue(request.ObservedEffectFingerprint),
            request.EvidenceKind,
            request.EvidenceTrustLevel,
            request.ObservationFreshness,
            request.PriorState,
            SafeFingerprintValue(request.AuthorityReceiptRef),
            SafeFingerprintValue(request.PolicySatisfactionRef),
            SafeFingerprintValue(request.ValidationReceiptRef),
            SafeFingerprintValue(request.ConcurrentGuardDecisionRef),
            SafeFingerprintValue(request.DirtyWorktreeGuardRef),
            SafeFingerprintValue(request.MovedBaseGuardRef),
            SafeFingerprintValue(request.StaleValidationGuardRef),
            SafeFingerprintValue(request.BranchRemoteHeadVerificationRef),
            SafeFingerprintValue(request.PostStateObservationRef),
            request.ObservedAtUtc.ToString("O"),
            request.RecordedAtUtc.ToString("O"),
            request.EvidenceExpiresAtUtc?.ToString("O") ?? string.Empty,
            SafeFingerprintValue(request.ContractVersion),
            decision,
            blockKind);
    }

    public static bool ContainsUnsafeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.ToLowerInvariant();
        return RawPayloadMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)) ||
            CredentialMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)) ||
            AuthorityMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    public static bool IsSafeIdempotencyKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaxIdempotencyKeyLength &&
        !value.Any(char.IsWhiteSpace) &&
        !ContainsUnsafeText(value) &&
        IdempotencyKeyPattern().IsMatch(value);

    public static string SafeDecisionValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : ContainsUnsafeText(value)
                ? "[unsafe-rejected]"
                : value;

    private static string SafeFingerprintValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : ContainsUnsafeText(value)
                ? "[unsafe]"
                : value;

    private static IdempotencyKeyContractValidationResult Result(
        IReadOnlyList<string> issues,
        IReadOnlyList<string> missingEvidence,
        bool unsafePayload,
        bool hasMalformedKey)
    {
        var normalizedIssues = issues
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static issue => issue, StringComparer.Ordinal)
            .ToArray();
        var normalizedMissing = missingEvidence
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static issue => issue, StringComparer.Ordinal)
            .ToArray();

        return new IdempotencyKeyContractValidationResult
        {
            IsValid = normalizedIssues.Length == 0 && normalizedMissing.Length == 0,
            Issues = normalizedIssues,
            MissingEvidence = normalizedMissing,
            HasUnsafePayload = unsafePayload,
            HasMalformedKey = hasMalformedKey,
            Warnings = RequiredWarnings,
            ForbiddenAuthorityImplications = RequiredForbiddenAuthorityImplications
        };
    }

    private static void AddScopeIssues(
        IdempotencyKeyContractRequest request,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        AddScopeIdIssues(request.TenantId, "IdempotencyTenantIdRequired", "IdempotencyTenantIdInvalid", issues, ref unsafePayload);
        AddScopeIdIssues(request.ProjectId, "IdempotencyProjectIdRequired", "IdempotencyProjectIdInvalid", issues, ref unsafePayload);

        var operationValidation = OperationIdentityValidator.ValidateOperationId(request.OperationId);
        if (!operationValidation.IsValid)
        {
            issues.Add(string.IsNullOrWhiteSpace(request.OperationId)
                ? "IdempotencyOperationIdRequired"
                : "IdempotencyOperationIdInvalid");
        }

        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            issues.Add("IdempotencyCorrelationIdRequired");
        }
        else if (!CorrelationIdPattern().IsMatch(request.CorrelationId) ||
            ContainsUnsafeText(request.CorrelationId))
        {
            issues.Add("IdempotencyCorrelationIdInvalid");
            unsafePayload = unsafePayload || ContainsUnsafeText(request.CorrelationId);
        }

        if (request.MutationSurface == MutationLeaseSurfaceKind.Unknown ||
            !Enum.IsDefined(request.MutationSurface))
        {
            issues.Add("IdempotencyMutationSurfaceRequired");
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
            issues.Add("IdempotencyUnsafePayloadRejected");
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
            issues.Add("IdempotencyUnsafePayloadRejected");
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

    private static void AddRequiredFingerprintIssues(
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

        AddFingerprintIssues(value, $"{issuePrefix}Invalid", issues, ref unsafePayload);
    }

    private static void AddOptionalFingerprintIssues(
        string? value,
        string issuePrefix,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        AddFingerprintIssues(value, $"{issuePrefix}Invalid", issues, ref unsafePayload);
    }

    private static void AddFingerprintIssues(
        string value,
        string invalidIssue,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        if (ContainsUnsafeText(value))
        {
            issues.Add("IdempotencyUnsafePayloadRejected");
            unsafePayload = true;
            return;
        }

        if (value.Any(char.IsWhiteSpace) ||
            value.Length > 256 ||
            !FingerprintPattern().IsMatch(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddKeyIssues(
        string? value,
        ICollection<string> issues,
        ref bool unsafePayload,
        ref bool malformedKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add("IdempotencyKeyRequired");
            return;
        }

        if (ContainsUnsafeText(value))
        {
            issues.Add("IdempotencyUnsafePayloadRejected");
            unsafePayload = true;
            malformedKey = true;
            return;
        }

        if (!IsSafeIdempotencyKey(value))
        {
            issues.Add("IdempotencyKeyMalformed");
            malformedKey = true;
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
        IdempotencyKeyContractRequest request,
        ICollection<string> issues)
    {
        if (request.EvidenceExpiresAtUtc is not { } expiresAt)
        {
            return;
        }

        if (expiresAt.Offset != TimeSpan.Zero)
        {
            issues.Add("IdempotencyEvidenceExpiresAtUtcMustBeUtc");
        }

        if (request.ObservedAtUtc != default &&
            request.ObservedAtUtc.Offset == TimeSpan.Zero &&
            expiresAt.Offset == TimeSpan.Zero &&
            expiresAt <= request.ObservedAtUtc)
        {
            issues.Add("IdempotencyEvidenceExpiresAtUtcBeforeObservedAtUtc");
        }
    }

    private static void AddTimestampOrderingIssues(
        IdempotencyKeyContractRequest request,
        ICollection<string> issues)
    {
        if (request.ObservedAtUtc != default &&
            request.RecordedAtUtc != default &&
            request.ObservedAtUtc.Offset == TimeSpan.Zero &&
            request.RecordedAtUtc.Offset == TimeSpan.Zero &&
            request.RecordedAtUtc < request.ObservedAtUtc)
        {
            issues.Add("IdempotencyRecordedAtUtcBeforeObservedAtUtc");
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
            issues.Add("IdempotencyUnsafePayloadRejected");
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

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:/=@+-]{1,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintPattern();

    [GeneratedRegex("^(idem|idempotency|idem-source-apply|idem-commit|idem-push):[A-Za-z0-9][A-Za-z0-9._:-]{1,150}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyKeyPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{1,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTextPattern();
}
