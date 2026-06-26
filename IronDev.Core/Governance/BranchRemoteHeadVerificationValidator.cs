using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class BranchRemoteHeadVerificationValidator
{
    public const int MaxObservationAgeSeconds = 900;

    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "branch remote head verification guard is read only",
        "branch remote head verification guard does not call git",
        "branch remote head verification guard does not call github",
        "branch remote head verification guard does not fetch remotes",
        "branch remote head verification guard does not inspect worktrees",
        "branch remote head verification guard does not read source",
        "branch remote head verification guard does not parse raw provider output",
        "the branch you meant is not automatically the branch you are on",
        "matching branch head evidence is not source safety",
        "matching branch head evidence is not validation freshness",
        "matching branch head evidence is not approval",
        "matching branch head evidence is not policy satisfaction",
        "matching branch head evidence is not mutation authority",
        "fresh authority is required before any mutation"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "branch remote head verification guard is not mutation execution",
        "branch remote head verification guard is not source apply authority",
        "branch remote head verification guard is not commit authority",
        "branch remote head verification guard is not push authority",
        "branch remote head verification guard is not pull request authority",
        "branch remote head verification guard is not merge authority",
        "branch remote head verification guard is not release authority",
        "branch remote head verification guard is not deployment authority",
        "branch remote head verification guard is not retry authority",
        "branch remote head verification guard is not recovery authority",
        "branch remote head verification guard is not rollback authority",
        "branch remote head verification guard is not approval",
        "branch remote head verification guard is not policy satisfaction",
        "branch remote head verification guard is not validation freshness",
        "branch remote head verification guard is not source safety",
        "branch remote head verification guard is not workflow continuation"
    ];

    private static readonly string[] RawPayloadMarkers =
    [
        string.Concat("raw ", "git output"),
        string.Concat("raw ", "github response"),
        "raw provider response",
        string.Concat("raw ", "ci output"),
        "raw console output",
        "raw command line",
        "raw patch",
        "patch payload",
        "raw diff",
        "diff --git",
        "raw source",
        "source file content",
        "raw log",
        "stack trace",
        "raw remote url",
        "remote url with credentials",
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
        string.Concat("-----", "BEGIN"),
        "://",
        "@github.com",
        "@gitlab.com",
        "@bitbucket.org"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "approval granted",
        "policy satisfied",
        "safe to apply",
        "safe to commit",
        "safe to push",
        "safe to merge",
        "safe to mutate",
        "ready to mutate",
        "mutation authorized",
        "apply authorized",
        "commit authorized",
        "push authorized",
        "merge authorized",
        "release authorized",
        "deploy authorized"
    ];

    public static BranchRemoteHeadVerificationValidationResult ValidateRequest(
        BranchRemoteHeadVerificationRequest? request)
    {
        if (request is null)
        {
            return Result(["BranchRemoteHeadVerificationRequestRequired"], [], false);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddScopeIssues(request, issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.AttemptRef, "BranchRemoteHeadAttemptRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.TargetRef, "BranchRemoteHeadTargetRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(request.GuardRef, "BranchRemoteHeadGuardRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.BranchObservationRef, "BranchRemoteHeadBranchObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.RemoteObservationRef, "BranchRemoteHeadRemoteObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.HeadObservationRef, "BranchRemoteHeadHeadObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.CompositeObservationRef, "BranchRemoteHeadCompositeObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ProviderBranchStateRef, "BranchRemoteHeadProviderBranchStateRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.OperatorObservationRef, "BranchRemoteHeadOperatorObservationRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedBranchRef, "BranchRemoteHeadExpectedBranchRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedBranchRef, "BranchRemoteHeadObservedBranchRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedRemoteRef, "BranchRemoteHeadExpectedRemoteRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedRemoteRef, "BranchRemoteHeadObservedRemoteRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedRemoteUrlFingerprint, "BranchRemoteHeadExpectedRemoteUrlFingerprint", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedRemoteUrlFingerprint, "BranchRemoteHeadObservedRemoteUrlFingerprint", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedLocalHeadRef, "BranchRemoteHeadExpectedLocalHeadRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedLocalHeadRef, "BranchRemoteHeadObservedLocalHeadRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedRemoteHeadRef, "BranchRemoteHeadExpectedRemoteHeadRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedRemoteHeadRef, "BranchRemoteHeadObservedRemoteHeadRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedBaseRef, "BranchRemoteHeadExpectedBaseRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedBaseRef, "BranchRemoteHeadObservedBaseRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedSourceStateRef, "BranchRemoteHeadExpectedSourceStateRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedSourceStateRef, "BranchRemoteHeadObservedSourceStateRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedPatchPackageRef, "BranchRemoteHeadExpectedPatchPackageRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedPatchPackageRef, "BranchRemoteHeadObservedPatchPackageRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ExpectedCommitRef, "BranchRemoteHeadExpectedCommitRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ObservedCommitRef, "BranchRemoteHeadObservedCommitRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.DirtyWorktreeGuardRef, "BranchRemoteHeadDirtyWorktreeGuardRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.MovedBaseGuardRef, "BranchRemoteHeadMovedBaseGuardRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.StaleValidationGuardRef, "BranchRemoteHeadStaleValidationGuardRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.ConcurrentGuardDecisionRef, "BranchRemoteHeadConcurrentGuardDecisionRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(request.PostStateObservationRef, "BranchRemoteHeadPostStateObservationRef", issues, ref unsafePayload);
        AddTimestampIssues(request.ObservedAtUtc, "BranchRemoteHeadObservedAtUtc", issues);
        AddTimestampIssues(request.RecordedAtUtc, "BranchRemoteHeadRecordedAtUtc", issues);
        AddOptionalExpiryIssues(request, issues);
        AddTimestampOrderingIssues(request, issues);
        AddSafeTextIssues(request.GuardVersion, "BranchRemoteHeadGuardVersion", issues, ref unsafePayload);
        AddSafeTextIssues(request.ReasonCode, "BranchRemoteHeadReasonCode", issues, ref unsafePayload);
        AddSafeTextIssues(request.Source, "BranchRemoteHeadSource", issues, ref unsafePayload);

        return Result(issues, [], unsafePayload);
    }

    public static string BuildRecordFingerprint(
        BranchRemoteHeadVerificationRequest request,
        BranchRemoteHeadVerificationDecisionKind decision,
        BranchRemoteHeadVerificationBlockKind blockKind) =>
        string.Join(
            "|",
            "branch-remote-head-verification",
            SafeFingerprintValue(request.TenantId),
            SafeFingerprintValue(request.ProjectId),
            SafeFingerprintValue(request.OperationId),
            SafeFingerprintValue(request.CorrelationId),
            request.MutationSurface,
            SafeFingerprintValue(request.AttemptRef),
            SafeFingerprintValue(request.TargetRef),
            SafeFingerprintValue(request.GuardRef),
            request.SubjectKind,
            request.EvidenceKind,
            request.EvidenceTrustLevel,
            request.ObservationFreshness,
            request.VerificationOutcome,
            SafeFingerprintValue(request.BranchObservationRef),
            SafeFingerprintValue(request.RemoteObservationRef),
            SafeFingerprintValue(request.HeadObservationRef),
            SafeFingerprintValue(request.CompositeObservationRef),
            SafeFingerprintValue(request.ProviderBranchStateRef),
            SafeFingerprintValue(request.OperatorObservationRef),
            SafeFingerprintValue(request.ExpectedBranchRef),
            SafeFingerprintValue(request.ObservedBranchRef),
            SafeFingerprintValue(request.ExpectedRemoteRef),
            SafeFingerprintValue(request.ObservedRemoteRef),
            SafeFingerprintValue(request.ExpectedRemoteUrlFingerprint),
            SafeFingerprintValue(request.ObservedRemoteUrlFingerprint),
            SafeFingerprintValue(request.ExpectedLocalHeadRef),
            SafeFingerprintValue(request.ObservedLocalHeadRef),
            SafeFingerprintValue(request.ExpectedRemoteHeadRef),
            SafeFingerprintValue(request.ObservedRemoteHeadRef),
            SafeFingerprintValue(request.ExpectedBaseRef),
            SafeFingerprintValue(request.ObservedBaseRef),
            SafeFingerprintValue(request.ExpectedSourceStateRef),
            SafeFingerprintValue(request.ObservedSourceStateRef),
            SafeFingerprintValue(request.ExpectedPatchPackageRef),
            SafeFingerprintValue(request.ObservedPatchPackageRef),
            SafeFingerprintValue(request.ExpectedCommitRef),
            SafeFingerprintValue(request.ObservedCommitRef),
            SafeFingerprintValue(request.DirtyWorktreeGuardRef),
            SafeFingerprintValue(request.MovedBaseGuardRef),
            SafeFingerprintValue(request.StaleValidationGuardRef),
            SafeFingerprintValue(request.ConcurrentGuardDecisionRef),
            SafeFingerprintValue(request.PostStateObservationRef),
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

    private static BranchRemoteHeadVerificationValidationResult Result(
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

        return new BranchRemoteHeadVerificationValidationResult
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
        BranchRemoteHeadVerificationRequest request,
        ICollection<string> issues,
        ref bool unsafePayload)
    {
        AddScopeIdIssues(request.TenantId, "BranchRemoteHeadTenantIdRequired", "BranchRemoteHeadTenantIdInvalid", issues, ref unsafePayload);
        AddScopeIdIssues(request.ProjectId, "BranchRemoteHeadProjectIdRequired", "BranchRemoteHeadProjectIdInvalid", issues, ref unsafePayload);

        var operationValidation = OperationIdentityValidator.ValidateOperationId(request.OperationId);
        if (!operationValidation.IsValid)
        {
            issues.Add(string.IsNullOrWhiteSpace(request.OperationId)
                ? "BranchRemoteHeadOperationIdRequired"
                : "BranchRemoteHeadOperationIdInvalid");
        }

        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            issues.Add("BranchRemoteHeadCorrelationIdRequired");
        }
        else if (!CorrelationIdPattern().IsMatch(request.CorrelationId) ||
            ContainsUnsafeText(request.CorrelationId))
        {
            issues.Add("BranchRemoteHeadCorrelationIdInvalid");
            unsafePayload = unsafePayload || ContainsUnsafeText(request.CorrelationId);
        }

        if (request.MutationSurface == MutationLeaseSurfaceKind.Unknown ||
            !Enum.IsDefined(request.MutationSurface))
        {
            issues.Add("BranchRemoteHeadMutationSurfaceRequired");
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
            issues.Add("BranchRemoteHeadUnsafePayloadRejected");
            unsafePayload = true;
            return;
        }

        if (value.Any(char.IsWhiteSpace) ||
            value.Any(static ch => char.IsControl(ch) || ch is ';' or '|' or '&' or '`' or '$' or '<' or '>' or '"' or '\'') ||
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
        BranchRemoteHeadVerificationRequest request,
        ICollection<string> issues)
    {
        if (request.EvidenceExpiresAtUtc is not { } expiresAt)
        {
            return;
        }

        if (expiresAt.Offset != TimeSpan.Zero)
        {
            issues.Add("BranchRemoteHeadEvidenceExpiresAtUtcMustBeUtc");
        }

        if (request.ObservedAtUtc != default &&
            request.ObservedAtUtc.Offset == TimeSpan.Zero &&
            expiresAt.Offset == TimeSpan.Zero &&
            expiresAt <= request.ObservedAtUtc)
        {
            issues.Add("BranchRemoteHeadEvidenceExpiresAtUtcBeforeObservedAtUtc");
        }
    }

    private static void AddTimestampOrderingIssues(
        BranchRemoteHeadVerificationRequest request,
        ICollection<string> issues)
    {
        if (request.ObservedAtUtc != default &&
            request.RecordedAtUtc != default &&
            request.ObservedAtUtc.Offset == TimeSpan.Zero &&
            request.RecordedAtUtc.Offset == TimeSpan.Zero &&
            request.RecordedAtUtc < request.ObservedAtUtc)
        {
            issues.Add("BranchRemoteHeadRecordedAtUtcBeforeObservedAtUtc");
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
            issues.Add("BranchRemoteHeadUnsafePayloadRejected");
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
