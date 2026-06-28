using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class MovedBaseGuardValidator
{
    public const int MaxObservationAgeSeconds = 900;

    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "moved-base guard is read only",
        "moved-base guard does not inspect git",
        "moved-base guard does not call github",
        "moved-base guard does not compare commits",
        "moved-base guard does not fetch remotes",
        "moved-base guard does not checkout branches",
        "moved-base guard does not merge branches",
        "matching base evidence is not source authority",
        "matching head evidence is not push authority",
        "matching branch evidence is not checkout authority",
        "matching merge-base evidence is not merge authority",
        "moved base evidence is not rebase authority",
        "fresh authority is required before any mutation"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "moved-base guard is not mutation execution",
        "moved-base guard is not source apply authority",
        "moved-base guard is not commit authority",
        "moved-base guard is not push authority",
        "moved-base guard is not pull request authority",
        "moved-base guard is not merge authority",
        "moved-base guard is not retry authority",
        "moved-base guard is not recovery authority",
        "moved-base guard is not rollback authority",
        "moved-base guard is not approval",
        "moved-base guard is not policy satisfaction",
        "moved-base guard is not validation freshness",
        "moved-base guard is not patch freshness",
        "moved-base guard is not source safety",
        "moved-base guard is not workflow continuation",
        "moved-base guard is not release readiness",
        "moved-base guard is not deployment readiness"
    ];

    private static readonly string[] RawPayloadMarkers =
    [
        string.Concat("raw ", "git log"),
        string.Concat("raw ", "git rev-parse"),
        string.Concat("raw ", "git merge-base"),
        string.Concat("raw ", "git output"),
        string.Concat("git ", "rev-parse"),
        string.Concat("git ", "merge-base"),
        "raw branch list",
        "raw commit graph",
        "raw provider branch response",
        "raw pull request response",
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
        "source safe",
        "base safe",
        "head safe",
        "ref safe",
        "safe to mutate",
        "safe to apply",
        "safe to commit",
        "safe to push",
        "safe to merge",
        "safe to retry",
        "safe to recover",
        "safe to rollback",
        "retry authorized",
        "recovery authorized",
        "rollback authorized",
        "merge authorized",
        "ready to execute",
        "ready to mutate",
        "merge now",
        "release now",
        "deploy now"
    ];

    public static MovedBaseGuardValidationResult ValidateRequest(MovedBaseGuardRequest? request)
    {
        if (request is null)
        {
            return Result(["MovedBaseGuardRequestRequired"], [], false);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddScopeIssues(request, issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.AttemptRef, "MovedBaseGuardAttemptRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.TargetRef, "MovedBaseGuardTargetRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.GuardRef, "MovedBaseGuardGuardRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.RefObservationRef, "MovedBaseGuardRefObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PostStateObservationRef, "MovedBaseGuardPostStateObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.DirtyWorktreeGuardRef, "MovedBaseGuardDirtyWorktreeGuardRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ValidationReceiptRef, "MovedBaseGuardValidationReceiptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PatchPackageRef, "MovedBaseGuardPatchPackageRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.CommitPackageRef, "MovedBaseGuardCommitPackageRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PushReceiptRef, "MovedBaseGuardPushReceiptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PullRequestProviderStateRef, "MovedBaseGuardPullRequestProviderStateRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ProviderStateRef, "MovedBaseGuardProviderStateRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.OperatorObservationRef, "MovedBaseGuardOperatorObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedBaseRef, "MovedBaseGuardExpectedBaseRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedBaseRef, "MovedBaseGuardObservedBaseRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedHeadRef, "MovedBaseGuardExpectedHeadRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedHeadRef, "MovedBaseGuardObservedHeadRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedRemoteHeadRef, "MovedBaseGuardExpectedRemoteHeadRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedRemoteHeadRef, "MovedBaseGuardObservedRemoteHeadRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedBranchRef, "MovedBaseGuardExpectedBranchRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedBranchRef, "MovedBaseGuardObservedBranchRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedMergeBaseFingerprint, "MovedBaseGuardExpectedMergeBaseFingerprint", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedMergeBaseFingerprint, "MovedBaseGuardObservedMergeBaseFingerprint", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedTargetFingerprint, "MovedBaseGuardExpectedTargetFingerprint", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedTargetFingerprint, "MovedBaseGuardObservedTargetFingerprint", issues, ref unsafePayload);
        AddTimestampIssues(request.ObservedAtUtc, "MovedBaseGuardObservedAtUtc", issues);
        AddTimestampIssues(request.RecordedAtUtc, "MovedBaseGuardRecordedAtUtc", issues);
        AddOptionalExpiryIssues(request, issues);
        AddTimestampOrderingIssues(request, issues);
        AddSafeTextIssues(request.GuardVersion, "MovedBaseGuardGuardVersion", issues, ref unsafePayload);
        AddSafeTextIssues(request.ReasonCode, "MovedBaseGuardReasonCode", issues, ref unsafePayload);
        AddSafeTextIssues(request.Source, "MovedBaseGuardSource", issues, ref unsafePayload);

        return Result(issues, [], unsafePayload);
    }

    public static string BuildRecordFingerprint(
        MovedBaseGuardRequest request,
        MovedBaseGuardDecisionKind decision,
        MovedBaseGuardBlockKind blockKind) =>
        string.Join(
            "|",
            "moved-base-guard",
            SafeFingerprintValue(request.TenantId),
            SafeFingerprintValue(request.ProjectId),
            SafeFingerprintValue(request.OperationId),
            SafeFingerprintValue(request.CorrelationId),
            request.MutationSurface,
            SafeFingerprintValue(request.AttemptRef),
            SafeFingerprintValue(request.TargetRef),
            SafeFingerprintValue(request.GuardRef),
            request.SubjectKind,
            request.ObservedState,
            request.EvidenceKind,
            request.EvidenceTrustLevel,
            request.ObservationFreshness,
            SafeFingerprintValue(request.RefObservationRef),
            SafeFingerprintValue(request.PostStateObservationRef),
            SafeFingerprintValue(request.DirtyWorktreeGuardRef),
            SafeFingerprintValue(request.ValidationReceiptRef),
            SafeFingerprintValue(request.PatchPackageRef),
            SafeFingerprintValue(request.CommitPackageRef),
            SafeFingerprintValue(request.PushReceiptRef),
            SafeFingerprintValue(request.PullRequestProviderStateRef),
            SafeFingerprintValue(request.ProviderStateRef),
            SafeFingerprintValue(request.OperatorObservationRef),
            SafeFingerprintValue(request.ExpectedBaseRef),
            SafeFingerprintValue(request.ObservedBaseRef),
            SafeFingerprintValue(request.ExpectedHeadRef),
            SafeFingerprintValue(request.ObservedHeadRef),
            SafeFingerprintValue(request.ExpectedRemoteHeadRef),
            SafeFingerprintValue(request.ObservedRemoteHeadRef),
            SafeFingerprintValue(request.ExpectedBranchRef),
            SafeFingerprintValue(request.ObservedBranchRef),
            SafeFingerprintValue(request.ExpectedMergeBaseFingerprint),
            SafeFingerprintValue(request.ObservedMergeBaseFingerprint),
            SafeFingerprintValue(request.ExpectedTargetFingerprint),
            SafeFingerprintValue(request.ObservedTargetFingerprint),
            request.ObservedAtUtc.ToString("O"),
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

    private static MovedBaseGuardValidationResult Result(
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

        return new MovedBaseGuardValidationResult
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
        MovedBaseGuardRequest request,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        AddScopeIdIssues(request.TenantId, "MovedBaseGuardTenantIdRequired", "MovedBaseGuardTenantIdInvalid", issues, ref unsafePayload);
        AddScopeIdIssues(request.ProjectId, "MovedBaseGuardProjectIdRequired", "MovedBaseGuardProjectIdInvalid", issues, ref unsafePayload);

        var operationValidation = OperationIdentityValidator.ValidateOperationId(request.OperationId);
        if (!operationValidation.IsValid)
        {
            issues.Add(string.IsNullOrWhiteSpace(request.OperationId)
                ? "MovedBaseGuardOperationIdRequired"
                : "MovedBaseGuardOperationIdInvalid");
        }

        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            issues.Add("MovedBaseGuardCorrelationIdRequired");
        }
        else if (!CorrelationIdPattern().IsMatch(request.CorrelationId) ||
            ContainsUnsafeText(request.CorrelationId))
        {
            issues.Add("MovedBaseGuardCorrelationIdInvalid");
            unsafePayload = unsafePayload || ContainsUnsafeText(request.CorrelationId);
        }

        if (request.MutationSurface == MutationLeaseSurfaceKind.Unknown ||
            !Enum.IsDefined(request.MutationSurface))
        {
            issues.Add("MovedBaseGuardMutationSurfaceRequired");
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
            issues.Add("MovedBaseGuardUnsafePayloadRejected");
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
        MovedBaseGuardRequest request,
        ICollection<string> issues)
    {
        if (request.EvidenceExpiresAtUtc is not { } expiresAt)
        {
            return;
        }

        if (expiresAt.Offset != TimeSpan.Zero)
        {
            issues.Add("MovedBaseGuardEvidenceExpiresAtUtcMustBeUtc");
        }

        if (request.ObservedAtUtc != default &&
            request.ObservedAtUtc.Offset == TimeSpan.Zero &&
            expiresAt.Offset == TimeSpan.Zero &&
            expiresAt <= request.ObservedAtUtc)
        {
            issues.Add("MovedBaseGuardEvidenceExpiresAtUtcBeforeObservedAtUtc");
        }
    }

    private static void AddTimestampOrderingIssues(
        MovedBaseGuardRequest request,
        ICollection<string> issues)
    {
        if (request.ObservedAtUtc != default &&
            request.RecordedAtUtc != default &&
            request.ObservedAtUtc.Offset == TimeSpan.Zero &&
            request.RecordedAtUtc.Offset == TimeSpan.Zero &&
            request.RecordedAtUtc < request.ObservedAtUtc)
        {
            issues.Add("MovedBaseGuardRecordedAtUtcBeforeObservedAtUtc");
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
            issues.Add("MovedBaseGuardUnsafePayloadRejected");
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
