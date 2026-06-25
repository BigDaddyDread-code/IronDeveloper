using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class StaleValidationGuardValidator
{
    public const int MaxValidationAgeSeconds = 900;

    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "stale validation guard is read only",
        "stale validation guard does not run validation",
        "stale validation guard does not run tests",
        "stale validation guard does not run builds",
        "stale validation guard does not call ci",
        "stale validation guard does not call github",
        "stale validation guard does not read logs",
        "validation passed then is not validation fresh now",
        "validation passed is not approval",
        "validation passed is not policy satisfaction",
        "validation passed is not source safety",
        "validation target match is not mutation authority",
        "fresh authority is required before any mutation"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "stale validation guard is not mutation execution",
        "stale validation guard is not source apply authority",
        "stale validation guard is not commit authority",
        "stale validation guard is not push authority",
        "stale validation guard is not pull request authority",
        "stale validation guard is not merge authority",
        "stale validation guard is not retry authority",
        "stale validation guard is not recovery authority",
        "stale validation guard is not rollback authority",
        "stale validation guard is not approval",
        "stale validation guard is not policy satisfaction",
        "stale validation guard is not validation execution",
        "stale validation guard is not source safety",
        "stale validation guard is not workflow continuation",
        "stale validation guard is not release readiness",
        "stale validation guard is not deployment readiness"
    ];

    private static readonly string[] RawPayloadMarkers =
    [
        "raw test output",
        "raw build output",
        string.Concat("raw ", "ci log"),
        string.Concat("raw ", "ci output"),
        "raw console output",
        "raw failure log",
        "raw stack trace",
        "raw command line",
        string.Concat("raw ", "git output"),
        "raw provider response",
        string.Concat("git", "hub response"),
        "raw patch",
        "patch payload",
        "raw diff",
        "diff --git",
        "raw source",
        "source file content",
        "raw commit body",
        "raw pr body",
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
        "validation approved",
        "validation authorized",
        "validation satisfied",
        "source safe",
        "safe to mutate",
        "safe to apply",
        "safe to commit",
        "safe to push",
        "safe to merge",
        "safe to release",
        "safe to deploy",
        "safe to retry",
        "safe to recover",
        "safe to rollback",
        "retry authorized",
        "recovery authorized",
        "rollback authorized",
        "merge authorized",
        "release authorized",
        "deploy authorized",
        "ready to execute",
        "ready to mutate",
        "merge now",
        "release now",
        "deploy now"
    ];

    public static StaleValidationGuardValidationResult ValidateRequest(StaleValidationGuardRequest? request)
    {
        if (request is null)
        {
            return Result(["StaleValidationGuardRequestRequired"], [], false);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddScopeIssues(request, issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.AttemptRef, "StaleValidationGuardAttemptRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.TargetRef, "StaleValidationGuardTargetRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.GuardRef, "StaleValidationGuardGuardRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ValidationEvidenceRef, "StaleValidationGuardValidationEvidenceRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ValidationReceiptRef, "StaleValidationGuardValidationReceiptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.BuildReceiptRef, "StaleValidationGuardBuildReceiptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.TestReceiptRef, "StaleValidationGuardTestReceiptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.GovernanceReceiptRef, "StaleValidationGuardGovernanceReceiptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ProviderCiStateRef, "StaleValidationGuardProviderCiStateRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.OperatorObservationRef, "StaleValidationGuardOperatorObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PostStateObservationRef, "StaleValidationGuardPostStateObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.DirtyWorktreeGuardRef, "StaleValidationGuardDirtyWorktreeGuardRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.MovedBaseGuardRef, "StaleValidationGuardMovedBaseGuardRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ConcurrentGuardDecisionRef, "StaleValidationGuardConcurrentGuardDecisionRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedValidationTargetRef, "StaleValidationGuardExpectedValidationTargetRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedValidationTargetRef, "StaleValidationGuardObservedValidationTargetRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedValidationFingerprint, "StaleValidationGuardExpectedValidationFingerprint", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedValidationFingerprint, "StaleValidationGuardObservedValidationFingerprint", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedSourceStateRef, "StaleValidationGuardExpectedSourceStateRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedSourceStateRef, "StaleValidationGuardObservedSourceStateRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedPatchPackageRef, "StaleValidationGuardExpectedPatchPackageRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedPatchPackageRef, "StaleValidationGuardObservedPatchPackageRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedCommitRef, "StaleValidationGuardExpectedCommitRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedCommitRef, "StaleValidationGuardObservedCommitRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedHeadRef, "StaleValidationGuardExpectedHeadRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedHeadRef, "StaleValidationGuardObservedHeadRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedBaseRef, "StaleValidationGuardExpectedBaseRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedBaseRef, "StaleValidationGuardObservedBaseRef", issues, ref unsafePayload);
        AddTimestampIssues(request.ValidatedAtUtc, "StaleValidationGuardValidatedAtUtc", issues);
        AddTimestampIssues(request.RecordedAtUtc, "StaleValidationGuardRecordedAtUtc", issues);
        AddOptionalExpiryIssues(request, issues);
        AddTimestampOrderingIssues(request, issues);
        AddSafeTextIssues(request.GuardVersion, "StaleValidationGuardGuardVersion", issues, ref unsafePayload);
        AddSafeTextIssues(request.ReasonCode, "StaleValidationGuardReasonCode", issues, ref unsafePayload);
        AddSafeTextIssues(request.Source, "StaleValidationGuardSource", issues, ref unsafePayload);

        return Result(issues, [], unsafePayload);
    }

    public static string BuildRecordFingerprint(
        StaleValidationGuardRequest request,
        StaleValidationGuardDecisionKind decision,
        StaleValidationGuardBlockKind blockKind) =>
        string.Join(
            "|",
            "stale-validation-guard",
            SafeFingerprintValue(request.TenantId),
            SafeFingerprintValue(request.ProjectId),
            SafeFingerprintValue(request.OperationId),
            SafeFingerprintValue(request.CorrelationId),
            request.MutationSurface,
            SafeFingerprintValue(request.AttemptRef),
            SafeFingerprintValue(request.TargetRef),
            SafeFingerprintValue(request.GuardRef),
            request.SubjectKind,
            request.ValidationEvidenceKind,
            request.EvidenceTrustLevel,
            request.ObservationFreshness,
            request.ValidationOutcome,
            request.ValidationScope,
            SafeFingerprintValue(request.ValidationEvidenceRef),
            SafeFingerprintValue(request.ValidationReceiptRef),
            SafeFingerprintValue(request.BuildReceiptRef),
            SafeFingerprintValue(request.TestReceiptRef),
            SafeFingerprintValue(request.GovernanceReceiptRef),
            SafeFingerprintValue(request.ProviderCiStateRef),
            SafeFingerprintValue(request.OperatorObservationRef),
            SafeFingerprintValue(request.PostStateObservationRef),
            SafeFingerprintValue(request.DirtyWorktreeGuardRef),
            SafeFingerprintValue(request.MovedBaseGuardRef),
            SafeFingerprintValue(request.ConcurrentGuardDecisionRef),
            SafeFingerprintValue(request.ExpectedValidationTargetRef),
            SafeFingerprintValue(request.ObservedValidationTargetRef),
            SafeFingerprintValue(request.ExpectedValidationFingerprint),
            SafeFingerprintValue(request.ObservedValidationFingerprint),
            SafeFingerprintValue(request.ExpectedSourceStateRef),
            SafeFingerprintValue(request.ObservedSourceStateRef),
            SafeFingerprintValue(request.ExpectedPatchPackageRef),
            SafeFingerprintValue(request.ObservedPatchPackageRef),
            SafeFingerprintValue(request.ExpectedCommitRef),
            SafeFingerprintValue(request.ObservedCommitRef),
            SafeFingerprintValue(request.ExpectedHeadRef),
            SafeFingerprintValue(request.ObservedHeadRef),
            SafeFingerprintValue(request.ExpectedBaseRef),
            SafeFingerprintValue(request.ObservedBaseRef),
            request.ValidatedAtUtc.ToString("O"),
            request.RecordedAtUtc.ToString("O"),
            request.EvidenceExpiresAtUtc?.ToString("O") ?? string.Empty,
            SafeFingerprintValue(request.GuardVersion),
            decision,
            blockKind);

    public static bool ContainsUnsafeText(string value)
    {
        var text = value.ToLowerInvariant();
        return RawPayloadMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)) ||
            CredentialMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)) ||
            AuthorityMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    private static string SafeFingerprintValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : ContainsUnsafeText(value)
                ? "[unsafe]"
                : value;

    private static StaleValidationGuardValidationResult Result(
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

        return new StaleValidationGuardValidationResult
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
        StaleValidationGuardRequest request,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        AddScopeIdIssues(request.TenantId, "StaleValidationGuardTenantIdRequired", "StaleValidationGuardTenantIdInvalid", issues, ref unsafePayload);
        AddScopeIdIssues(request.ProjectId, "StaleValidationGuardProjectIdRequired", "StaleValidationGuardProjectIdInvalid", issues, ref unsafePayload);

        var operationValidation = OperationIdentityValidator.ValidateOperationId(request.OperationId);
        if (!operationValidation.IsValid)
        {
            issues.Add(string.IsNullOrWhiteSpace(request.OperationId)
                ? "StaleValidationGuardOperationIdRequired"
                : "StaleValidationGuardOperationIdInvalid");
        }

        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            issues.Add("StaleValidationGuardCorrelationIdRequired");
        }
        else if (!CorrelationIdPattern().IsMatch(request.CorrelationId) ||
            ContainsUnsafeText(request.CorrelationId))
        {
            issues.Add("StaleValidationGuardCorrelationIdInvalid");
            unsafePayload = unsafePayload || ContainsUnsafeText(request.CorrelationId);
        }

        if (request.MutationSurface == MutationLeaseSurfaceKind.Unknown ||
            !Enum.IsDefined(request.MutationSurface))
        {
            issues.Add("StaleValidationGuardMutationSurfaceRequired");
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
            issues.Add("StaleValidationGuardUnsafePayloadRejected");
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
        StaleValidationGuardRequest request,
        ICollection<string> issues)
    {
        if (request.EvidenceExpiresAtUtc is not { } expiresAt)
        {
            return;
        }

        if (expiresAt.Offset != TimeSpan.Zero)
        {
            issues.Add("StaleValidationGuardEvidenceExpiresAtUtcMustBeUtc");
        }

        if (request.ValidatedAtUtc != default &&
            request.ValidatedAtUtc.Offset == TimeSpan.Zero &&
            expiresAt.Offset == TimeSpan.Zero &&
            expiresAt <= request.ValidatedAtUtc)
        {
            issues.Add("StaleValidationGuardEvidenceExpiresAtUtcBeforeValidatedAtUtc");
        }
    }

    private static void AddTimestampOrderingIssues(
        StaleValidationGuardRequest request,
        ICollection<string> issues)
    {
        if (request.ValidatedAtUtc != default &&
            request.RecordedAtUtc != default &&
            request.ValidatedAtUtc.Offset == TimeSpan.Zero &&
            request.RecordedAtUtc.Offset == TimeSpan.Zero &&
            request.RecordedAtUtc < request.ValidatedAtUtc)
        {
            issues.Add("StaleValidationGuardRecordedAtUtcBeforeValidatedAtUtc");
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
            issues.Add("StaleValidationGuardUnsafePayloadRejected");
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
