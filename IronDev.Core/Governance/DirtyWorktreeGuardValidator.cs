using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class DirtyWorktreeGuardValidator
{
    public const int MaxObservationAgeSeconds = 900;

    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "dirty worktree guard is read only",
        "dirty worktree guard does not inspect source",
        "dirty worktree guard does not call git",
        "dirty worktree guard does not clean the worktree",
        "dirty worktree guard does not reset the worktree",
        "dirty worktree guard does not stash changes",
        "dirty worktree guard does not rollback changes",
        "dirty worktree evidence is not source authority",
        "clean worktree evidence is not mutation authority",
        "head evidence is not push authority",
        "branch evidence is not checkout authority",
        "fresh authority is required before any mutation"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "dirty worktree guard is not mutation execution",
        "dirty worktree guard is not source apply authority",
        "dirty worktree guard is not commit authority",
        "dirty worktree guard is not push authority",
        "dirty worktree guard is not pull request authority",
        "dirty worktree guard is not retry authority",
        "dirty worktree guard is not recovery authority",
        "dirty worktree guard is not rollback authority",
        "dirty worktree guard is not approval",
        "dirty worktree guard is not policy satisfaction",
        "dirty worktree guard is not validation freshness",
        "dirty worktree guard is not patch freshness",
        "dirty worktree guard is not source safety",
        "dirty worktree guard is not workflow continuation",
        "dirty worktree guard is not merge readiness",
        "dirty worktree guard is not release readiness",
        "dirty worktree guard is not deployment readiness"
    ];

    private static readonly string[] RawPayloadMarkers =
    [
        string.Concat("raw ", "git status"),
        string.Concat("raw ", "git output"),
        string.Concat("git ", "status --porcelain"),
        "raw file list",
        "raw patch",
        "patch payload",
        "raw diff",
        "diff --git",
        "raw source",
        "source file content",
        "raw commit body",
        "raw pr body",
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
        "worktree safe",
        "safe to mutate",
        "safe to apply",
        "safe to commit",
        "safe to push",
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

    public static DirtyWorktreeGuardValidationResult ValidateRequest(DirtyWorktreeGuardRequest? request)
    {
        if (request is null)
        {
            return Result(["DirtyWorktreeGuardRequestRequired"], [], false);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddScopeIssues(request, issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.AttemptRef, "DirtyWorktreeGuardAttemptRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.TargetRef, "DirtyWorktreeGuardTargetRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.GuardRef, "DirtyWorktreeGuardGuardRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.WorktreeObservationRef, "DirtyWorktreeGuardWorktreeObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PostStateObservationRef, "DirtyWorktreeGuardPostStateObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.FailureClassificationRef, "DirtyWorktreeGuardFailureClassificationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.FailureReceiptRef, "DirtyWorktreeGuardFailureReceiptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.MutationReceiptRef, "DirtyWorktreeGuardMutationReceiptRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ProviderStateRef, "DirtyWorktreeGuardProviderStateRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.OperatorObservationRef, "DirtyWorktreeGuardOperatorObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedHeadRef, "DirtyWorktreeGuardExpectedHeadRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedHeadRef, "DirtyWorktreeGuardObservedHeadRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedBranchRef, "DirtyWorktreeGuardExpectedBranchRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedBranchRef, "DirtyWorktreeGuardObservedBranchRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedWorktreeFingerprint, "DirtyWorktreeGuardExpectedWorktreeFingerprint", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedWorktreeFingerprint, "DirtyWorktreeGuardObservedWorktreeFingerprint", issues, ref unsafePayload);
        AddTimestampIssues(request.ObservedAtUtc, "DirtyWorktreeGuardObservedAtUtc", issues);
        AddTimestampIssues(request.RecordedAtUtc, "DirtyWorktreeGuardRecordedAtUtc", issues);
        AddOptionalExpiryIssues(request, issues);
        AddTimestampOrderingIssues(request, issues);
        AddSafeTextIssues(request.GuardVersion, "DirtyWorktreeGuardGuardVersion", issues, ref unsafePayload);
        AddSafeTextIssues(request.ReasonCode, "DirtyWorktreeGuardReasonCode", issues, ref unsafePayload);
        AddSafeTextIssues(request.Source, "DirtyWorktreeGuardSource", issues, ref unsafePayload);

        return Result(issues, [], unsafePayload);
    }

    public static string BuildRecordFingerprint(
        DirtyWorktreeGuardRequest request,
        DirtyWorktreeGuardDecisionKind decision,
        DirtyWorktreeGuardBlockKind blockKind) =>
        string.Join(
            "|",
            "dirty-worktree-guard",
            SafeFingerprintValue(request.TenantId),
            SafeFingerprintValue(request.ProjectId),
            SafeFingerprintValue(request.OperationId),
            SafeFingerprintValue(request.CorrelationId),
            request.MutationSurface,
            SafeFingerprintValue(request.AttemptRef),
            SafeFingerprintValue(request.TargetRef),
            SafeFingerprintValue(request.GuardRef),
            request.SubjectKind,
            request.WorktreeState,
            request.EvidenceKind,
            request.EvidenceTrustLevel,
            request.ObservationFreshness,
            SafeFingerprintValue(request.WorktreeObservationRef),
            SafeFingerprintValue(request.PostStateObservationRef),
            SafeFingerprintValue(request.FailureClassificationRef),
            SafeFingerprintValue(request.FailureReceiptRef),
            SafeFingerprintValue(request.MutationReceiptRef),
            SafeFingerprintValue(request.ProviderStateRef),
            SafeFingerprintValue(request.OperatorObservationRef),
            SafeFingerprintValue(request.ExpectedHeadRef),
            SafeFingerprintValue(request.ObservedHeadRef),
            SafeFingerprintValue(request.ExpectedBranchRef),
            SafeFingerprintValue(request.ObservedBranchRef),
            SafeFingerprintValue(request.ExpectedWorktreeFingerprint),
            SafeFingerprintValue(request.ObservedWorktreeFingerprint),
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

    private static DirtyWorktreeGuardValidationResult Result(
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

        return new DirtyWorktreeGuardValidationResult
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
        DirtyWorktreeGuardRequest request,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        AddScopeIdIssues(request.TenantId, "DirtyWorktreeGuardTenantIdRequired", "DirtyWorktreeGuardTenantIdInvalid", issues, ref unsafePayload);
        AddScopeIdIssues(request.ProjectId, "DirtyWorktreeGuardProjectIdRequired", "DirtyWorktreeGuardProjectIdInvalid", issues, ref unsafePayload);

        var operationValidation = OperationIdentityValidator.ValidateOperationId(request.OperationId);
        if (!operationValidation.IsValid)
        {
            issues.Add(string.IsNullOrWhiteSpace(request.OperationId)
                ? "DirtyWorktreeGuardOperationIdRequired"
                : "DirtyWorktreeGuardOperationIdInvalid");
        }

        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            issues.Add("DirtyWorktreeGuardCorrelationIdRequired");
        }
        else if (!CorrelationIdPattern().IsMatch(request.CorrelationId) ||
            ContainsUnsafeText(request.CorrelationId))
        {
            issues.Add("DirtyWorktreeGuardCorrelationIdInvalid");
            unsafePayload = unsafePayload || ContainsUnsafeText(request.CorrelationId);
        }

        if (request.MutationSurface == MutationLeaseSurfaceKind.Unknown ||
            !Enum.IsDefined(request.MutationSurface))
        {
            issues.Add("DirtyWorktreeGuardMutationSurfaceRequired");
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
            issues.Add("DirtyWorktreeGuardUnsafePayloadRejected");
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
        DirtyWorktreeGuardRequest request,
        ICollection<string> issues)
    {
        if (request.EvidenceExpiresAtUtc is not { } expiresAt)
        {
            return;
        }

        if (expiresAt.Offset != TimeSpan.Zero)
        {
            issues.Add("DirtyWorktreeGuardEvidenceExpiresAtUtcMustBeUtc");
        }

        if (request.ObservedAtUtc != default &&
            request.ObservedAtUtc.Offset == TimeSpan.Zero &&
            expiresAt.Offset == TimeSpan.Zero &&
            expiresAt <= request.ObservedAtUtc)
        {
            issues.Add("DirtyWorktreeGuardEvidenceExpiresAtUtcBeforeObservedAtUtc");
        }
    }

    private static void AddTimestampOrderingIssues(
        DirtyWorktreeGuardRequest request,
        ICollection<string> issues)
    {
        if (request.ObservedAtUtc != default &&
            request.RecordedAtUtc != default &&
            request.ObservedAtUtc.Offset == TimeSpan.Zero &&
            request.RecordedAtUtc.Offset == TimeSpan.Zero &&
            request.RecordedAtUtc < request.ObservedAtUtc)
        {
            issues.Add("DirtyWorktreeGuardRecordedAtUtcBeforeObservedAtUtc");
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
            issues.Add("DirtyWorktreeGuardUnsafePayloadRejected");
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
