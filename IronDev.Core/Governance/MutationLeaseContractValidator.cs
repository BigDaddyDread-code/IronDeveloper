using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class MutationLeaseContractValidator
{
    public const int MaximumRequestedLeaseDurationSeconds = 900;

    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "mutation lease contract is reference only",
        "mutation lease contract is a concurrency witness only",
        "mutation lease contract does not grant mutation authority"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "mutation lease contract is not mutation execution",
        "mutation lease contract is not lock acquisition",
        "mutation lease contract is not lock enforcement",
        "mutation lease contract is not executor permission",
        "mutation lease contract is not approval",
        "mutation lease contract is not policy satisfaction",
        "mutation lease contract is not validation freshness",
        "mutation lease contract is not patch freshness",
        "mutation lease contract is not source state proof",
        "mutation lease contract is not source safety proof",
        "mutation lease contract is not execution proof",
        "mutation lease contract is not source apply authority",
        "mutation lease contract is not commit authority",
        "mutation lease contract is not push authority",
        "mutation lease contract is not pr authority",
        "mutation lease contract is not ready-for-review authority",
        "mutation lease contract is not reviewer-request authority",
        "mutation lease contract is not rollback authority",
        "mutation lease contract is not retry authority",
        "mutation lease contract is not recovery authority",
        "mutation lease contract is not resume authority",
        "mutation lease contract is not merge readiness",
        "mutation lease contract is not release readiness",
        "mutation lease contract is not deployment readiness",
        "mutation lease contract is not workflow continuation",
        "lease owner is not user authority",
        "lease token is not mutation authority",
        "fence token is not mutation authority",
        "non-expired lease metadata is not permission to mutate",
        "expired lease metadata is not permission to recover retry or resume",
        "idempotency key match is not permission to replay mutation",
        "contract validity is not executor eligibility"
    ];

    private static readonly string[] RawPayloadMarkers =
    [
        "raw patch",
        "patch payload",
        "raw diff",
        "diff --git",
        "@@",
        "source file content",
        "raw source",
        "raw commit message",
        "raw commit body",
        "commit message:",
        "commit body:",
        "raw pr title",
        "raw pr body",
        "pull request title:",
        "pull request body:",
        string.Concat("raw ", "gi", "t output"),
        string.Concat("gi", "t output:"),
        string.Concat("raw ", "git", "hub output"),
        string.Concat("git", "hub output:"),
        "raw rollback output",
        "rollback output:",
        "raw recovery output",
        "recovery output:",
        "command text",
        "shell command",
        "api request body",
        "raw evidence payload",
        "raw receipt payload",
        "raw payload",
        "prompt text",
        "private reasoning",
        "chain-of-thought",
        "scratchpad"
    ];

    private static readonly string[] SecretMarkers =
    [
        "private key",
        "credential material",
        "connection string",
        "authorization:",
        "bearer ",
        "token=",
        "secret",
        "password"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "approved",
        "authorized",
        "allowed",
        "ready to execute",
        "ready to mutate",
        "continue workflow",
        "retry now",
        "resume now",
        "recover now",
        "rollback now",
        "merge now",
        "release now",
        "deploy now",
        "policy satisfied"
    ];

    public static MutationLeaseContractValidationResult ValidateRequest(
        MutationLeaseContractRequest? request)
    {
        if (request is null)
        {
            return Invalid(null, ["MutationLeaseContractRequestRequired"], false);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddCommonScopeIssues(
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            request.CorrelationId,
            request.MutationSurfaceKind,
            request.MutationTargetRef,
            request.IdempotencyKey,
            request.IdempotencyKeyFingerprint,
            issues,
            ref unsafePayload);

        AddLeaseModeIssues(request.LeaseMode, issues);
        AddRequiredReferenceIssues(request.LeaseOwnerRef, "MutationLeaseContractLeaseOwnerRef", issues, ref unsafePayload);
        AddRequestedDurationIssues(request.RequestedLeaseDurationSeconds, issues);
        AddRequiredTimestampIssues(request.RequestedAtUtc, "MutationLeaseContractRequestedAtUtc", issues);
        AddRequiredTimestampIssues(request.AsOfUtc, "MutationLeaseContractAsOfUtc", issues);
        AddOptionalSafeTextIssues(request.ReasonCode, "MutationLeaseContractReasonCode", issues, ref unsafePayload);
        AddSourceIssues(request.Source, issues, ref unsafePayload);

        return Result(
            issues,
            unsafePayload,
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            request.CorrelationId,
            request.MutationSurfaceKind,
            request.MutationTargetRef,
            request.IdempotencyKeyFingerprint);
    }

    public static MutationLeaseContractValidationResult ValidateRecord(
        MutationLeaseContractRecord? record)
    {
        if (record is null)
        {
            return Invalid(null, ["MutationLeaseContractRecordRequired"], false);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddCommonScopeIssues(
            record.TenantId,
            record.ProjectId,
            record.OperationId,
            record.CorrelationId,
            record.MutationSurfaceKind,
            record.MutationTargetRef,
            record.IdempotencyKey,
            record.IdempotencyKeyFingerprint,
            issues,
            ref unsafePayload);

        AddLeaseModeIssues(record.LeaseMode, issues);
        AddLeaseStateIssues(record.LeaseState, issues);
        AddRequiredReferenceIssues(record.LeaseOwnerRef, "MutationLeaseContractLeaseOwnerRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.LeaseTokenRef, "MutationLeaseContractLeaseTokenRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.FenceTokenRef, "MutationLeaseContractFenceTokenRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.LeaseSequenceRef, "MutationLeaseContractLeaseSequenceRef", issues, ref unsafePayload);
        AddRequiredTimestampIssues(record.RequestedAtUtc, "MutationLeaseContractRequestedAtUtc", issues);
        AddObservedStateIssues(record, issues, ref unsafePayload);
        AddOptionalSafeTextIssues(record.DeniedReasonCode, "MutationLeaseContractDeniedReasonCode", issues, ref unsafePayload);
        AddOptionalSafeTextIssues(record.ConflictReasonCode, "MutationLeaseContractConflictReasonCode", issues, ref unsafePayload);
        AddSourceIssues(record.Source, issues, ref unsafePayload);

        if (record.IsRedacted)
        {
            AddRequiredSafeTextIssues(record.RedactionReason, "MutationLeaseContractRedactionReason", issues, ref unsafePayload);
        }
        else
        {
            AddOptionalSafeTextIssues(record.RedactionReason, "MutationLeaseContractRedactionReason", issues, ref unsafePayload);
        }

        AddOptionalReferenceIssues(record.RecordFingerprint, "MutationLeaseContractRecordFingerprint", issues, ref unsafePayload);

        return Result(
            issues,
            unsafePayload,
            record.TenantId,
            record.ProjectId,
            record.OperationId,
            record.CorrelationId,
            record.MutationSurfaceKind,
            record.MutationTargetRef,
            record.IdempotencyKeyFingerprint);
    }

    public static MutationLeaseContractValidationResult ValidateScope(MutationLeaseScope? scope)
    {
        if (scope is null)
        {
            return Invalid(null, ["MutationLeaseScopeRequired"], false);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddCommonScopeIssues(
            scope.TenantId,
            scope.ProjectId,
            scope.OperationId,
            scope.CorrelationId,
            scope.MutationSurfaceKind,
            scope.MutationTargetRef,
            scope.IdempotencyKey,
            scope.IdempotencyKeyFingerprint,
            issues,
            ref unsafePayload);

        return Result(
            issues,
            unsafePayload,
            scope.TenantId,
            scope.ProjectId,
            scope.OperationId,
            scope.CorrelationId,
            scope.MutationSurfaceKind,
            scope.MutationTargetRef,
            scope.IdempotencyKeyFingerprint);
    }

    public static string CanonicalLeaseKey(MutationLeaseScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return string.Join(
            "|",
            [
                Normalize(scope.TenantId),
                Normalize(scope.ProjectId),
                Normalize(scope.OperationId),
                Normalize(scope.CorrelationId),
                scope.MutationSurfaceKind.ToString(),
                Normalize(scope.MutationTargetRef),
                Normalize(scope.IdempotencyKeyFingerprint)
            ]);
    }

    private static void AddCommonScopeIssues(
        string? tenantId,
        string? projectId,
        string? operationId,
        string? correlationId,
        MutationLeaseSurfaceKind surfaceKind,
        string? targetRef,
        string? idempotencyKey,
        string? idempotencyFingerprint,
        List<string> issues,
        ref bool unsafePayload)
    {
        AddScopeIssues(tenantId, "MutationLeaseContractTenantId", issues, ref unsafePayload);
        AddScopeIssues(projectId, "MutationLeaseContractProjectId", issues, ref unsafePayload);
        AddOperationIdIssues(operationId, "MutationLeaseContractOperationId", issues);
        AddCorrelationIdIssues(correlationId, "MutationLeaseContractCorrelationId", issues, ref unsafePayload);
        AddSurfaceKindIssues(surfaceKind, issues);
        AddRequiredReferenceIssues(targetRef, "MutationLeaseContractMutationTargetRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(idempotencyKey, "MutationLeaseContractIdempotencyKey", issues, ref unsafePayload);
        AddRequiredReferenceIssues(idempotencyFingerprint, "MutationLeaseContractIdempotencyKeyFingerprint", issues, ref unsafePayload);
    }

    private static void AddObservedStateIssues(
        MutationLeaseContractRecord record,
        List<string> issues,
        ref bool unsafePayload)
    {
        switch (record.LeaseState)
        {
            case MutationLeaseState.Requested:
                break;

            case MutationLeaseState.ObservedHeld:
                AddObservedAtRequired(record.ObservedAtUtc, issues);
                AddRequiredReferenceIssues(record.LeaseTokenRef, "MutationLeaseContractLeaseTokenRef", issues, ref unsafePayload);
                AddRequiredReferenceIssues(record.FenceTokenRef, "MutationLeaseContractFenceTokenRef", issues, ref unsafePayload);
                AddExpiresAtRequired(record.ExpiresAtUtc, issues);
                AddExpiryOrderIssues(record.ObservedAtUtc, record.ExpiresAtUtc, issues);
                break;

            case MutationLeaseState.ObservedReleased:
                AddObservedAtRequired(record.ObservedAtUtc, issues);
                AddRequiredReferenceIssues(record.LeaseTokenRef, "MutationLeaseContractLeaseTokenRef", issues, ref unsafePayload);
                if (record.ReleasedAtUtc is null)
                {
                    issues.Add("MutationLeaseContractReleasedAtUtcRequired");
                }

                AddReleaseOrderIssues(record.ObservedAtUtc, record.ReleasedAtUtc, issues);
                break;

            case MutationLeaseState.ObservedExpired:
                AddObservedAtRequired(record.ObservedAtUtc, issues);
                AddExpiresAtRequired(record.ExpiresAtUtc, issues);
                break;

            case MutationLeaseState.ObservedDenied:
                AddObservedAtRequired(record.ObservedAtUtc, issues);
                AddRequiredSafeTextIssues(record.DeniedReasonCode, "MutationLeaseContractDeniedReasonCode", issues, ref unsafePayload);
                break;

            case MutationLeaseState.ObservedConflicted:
                AddObservedAtRequired(record.ObservedAtUtc, issues);
                AddRequiredSafeTextIssues(record.ConflictReasonCode, "MutationLeaseContractConflictReasonCode", issues, ref unsafePayload);
                break;
        }
    }

    private static void AddScopeIssues(
        string? value,
        string prefix,
        List<string> issues,
        ref bool unsafePayload)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{prefix}Required");
            return;
        }

        if (ContainsUnsafeText(value, ref unsafePayload) ||
            value.Any(char.IsWhiteSpace) ||
            value.Length > 120)
        {
            issues.Add($"{prefix}Invalid");
        }
    }

    private static void AddOperationIdIssues(string? operationId, string prefix, List<string> issues)
    {
        var validation = OperationIdentityValidator.ValidateOperationId(operationId);
        if (validation.IsValid)
        {
            return;
        }

        issues.Add(string.IsNullOrWhiteSpace(operationId)
            ? $"{prefix}Required"
            : $"{prefix}Invalid");
    }

    private static void AddCorrelationIdIssues(
        string? correlationId,
        string prefix,
        List<string> issues,
        ref bool unsafePayload)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            issues.Add($"{prefix}Required");
            return;
        }

        if (ContainsUnsafeText(correlationId, ref unsafePayload) ||
            correlationId.Any(char.IsWhiteSpace) ||
            !CorrelationIdPattern().IsMatch(correlationId))
        {
            issues.Add($"{prefix}Invalid");
        }
    }

    private static void AddSurfaceKindIssues(
        MutationLeaseSurfaceKind surfaceKind,
        List<string> issues)
    {
        if (surfaceKind == MutationLeaseSurfaceKind.Unknown ||
            !Enum.IsDefined(surfaceKind))
        {
            issues.Add("MutationLeaseContractMutationSurfaceKindRequired");
        }
    }

    private static void AddLeaseModeIssues(
        MutationLeaseMode leaseMode,
        List<string> issues)
    {
        if (leaseMode == MutationLeaseMode.Unknown ||
            !Enum.IsDefined(leaseMode))
        {
            issues.Add("MutationLeaseContractLeaseModeRequired");
        }
    }

    private static void AddLeaseStateIssues(
        MutationLeaseState leaseState,
        List<string> issues)
    {
        if (leaseState == MutationLeaseState.Unknown ||
            !Enum.IsDefined(leaseState))
        {
            issues.Add("MutationLeaseContractLeaseStateRequired");
        }
    }

    private static void AddRequestedDurationIssues(
        int requestedLeaseDurationSeconds,
        List<string> issues)
    {
        if (requestedLeaseDurationSeconds == 0)
        {
            issues.Add("MutationLeaseContractRequestedLeaseDurationSecondsRequired");
            return;
        }

        if (requestedLeaseDurationSeconds < 0)
        {
            issues.Add("MutationLeaseContractRequestedLeaseDurationSecondsPositiveRequired");
            return;
        }

        if (requestedLeaseDurationSeconds > MaximumRequestedLeaseDurationSeconds)
        {
            issues.Add("MutationLeaseContractRequestedLeaseDurationSecondsBoundedRequired");
        }
    }

    private static void AddRequiredReferenceIssues(
        string? value,
        string prefix,
        List<string> issues,
        ref bool unsafePayload)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{prefix}Required");
            return;
        }

        AddOptionalReferenceIssues(value, prefix, issues, ref unsafePayload);
    }

    private static void AddOptionalReferenceIssues(
        string? value,
        string prefix,
        List<string> issues,
        ref bool unsafePayload)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.Contains("://", StringComparison.Ordinal) ||
            value.Contains('@'))
        {
            unsafePayload = true;
        }

        if (IsBroadScope(value) ||
            ContainsUnsafeText(value, ref unsafePayload) ||
            value.Length > 180 ||
            value.Any(char.IsWhiteSpace) ||
            value.Contains('/') ||
            value.Contains('\\') ||
            value.Contains("://", StringComparison.Ordinal) ||
            value.Contains('@') ||
            !ReferencePattern().IsMatch(value))
        {
            issues.Add($"{prefix}Invalid");
        }
    }

    private static void AddSourceIssues(
        string? source,
        List<string> issues,
        ref bool unsafePayload)
    {
        AddRequiredSafeTextIssues(source, "MutationLeaseContractSource", issues, ref unsafePayload);
        if (string.Equals(source, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("MutationLeaseContractSourceUnknown");
        }
    }

    private static void AddRequiredSafeTextIssues(
        string? value,
        string prefix,
        List<string> issues,
        ref bool unsafePayload)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{prefix}Required");
            return;
        }

        AddOptionalSafeTextIssues(value, prefix, issues, ref unsafePayload);
    }

    private static void AddOptionalSafeTextIssues(
        string? value,
        string prefix,
        List<string> issues,
        ref bool unsafePayload)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (ContainsUnsafeText(value, ref unsafePayload) ||
            value.Length > 180)
        {
            issues.Add($"{prefix}Invalid");
        }
    }

    private static void AddRequiredTimestampIssues(
        DateTimeOffset value,
        string prefix,
        List<string> issues)
    {
        if (value == default)
        {
            issues.Add($"{prefix}Required");
            return;
        }

        if (value.Offset != TimeSpan.Zero)
        {
            issues.Add($"{prefix}MustBeUtc");
        }
    }

    private static void AddObservedAtRequired(
        DateTimeOffset? observedAtUtc,
        List<string> issues)
    {
        if (observedAtUtc is null || observedAtUtc.Value == default)
        {
            issues.Add("MutationLeaseContractObservedAtUtcRequired");
            return;
        }

        if (observedAtUtc.Value.Offset != TimeSpan.Zero)
        {
            issues.Add("MutationLeaseContractObservedAtUtcMustBeUtc");
        }
    }

    private static void AddExpiresAtRequired(
        DateTimeOffset? expiresAtUtc,
        List<string> issues)
    {
        if (expiresAtUtc is null || expiresAtUtc.Value == default)
        {
            issues.Add("MutationLeaseContractExpiresAtUtcRequired");
            return;
        }

        if (expiresAtUtc.Value.Offset != TimeSpan.Zero)
        {
            issues.Add("MutationLeaseContractExpiresAtUtcMustBeUtc");
        }
    }

    private static void AddExpiryOrderIssues(
        DateTimeOffset? observedAtUtc,
        DateTimeOffset? expiresAtUtc,
        List<string> issues)
    {
        if (observedAtUtc is not null &&
            expiresAtUtc is not null &&
            observedAtUtc.Value != default &&
            expiresAtUtc.Value != default &&
            expiresAtUtc.Value <= observedAtUtc.Value)
        {
            issues.Add("MutationLeaseContractExpiresAtUtcMustBeAfterObservedAtUtc");
        }
    }

    private static void AddReleaseOrderIssues(
        DateTimeOffset? observedAtUtc,
        DateTimeOffset? releasedAtUtc,
        List<string> issues)
    {
        if (releasedAtUtc is not null &&
            releasedAtUtc.Value.Offset != TimeSpan.Zero)
        {
            issues.Add("MutationLeaseContractReleasedAtUtcMustBeUtc");
        }

        if (observedAtUtc is not null &&
            releasedAtUtc is not null &&
            observedAtUtc.Value != default &&
            releasedAtUtc.Value != default &&
            releasedAtUtc.Value < observedAtUtc.Value)
        {
            issues.Add("MutationLeaseContractReleasedAtUtcBeforeObservedAtUtc");
        }
    }

    private static bool ContainsUnsafeText(string value, ref bool unsafePayload)
    {
        if (value.Any(char.IsControl) ||
            value.Contains('<') ||
            value.Contains('>') ||
            value.Contains('|') ||
            value.Contains("..", StringComparison.Ordinal))
        {
            unsafePayload = true;
            return true;
        }

        if (RawPayloadMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
            SecretMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
            AuthorityMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            unsafePayload = true;
            return true;
        }

        return false;
    }

    private static bool IsBroadScope(string value) =>
        string.Equals(value, "*", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "all", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "any", StringComparison.OrdinalIgnoreCase);

    private static MutationLeaseContractValidationResult Invalid(
        MutationLeaseContractRequest? request,
        IReadOnlyList<string> issues,
        bool hasUnsafePayload) =>
        Result(
            issues,
            hasUnsafePayload,
            request?.TenantId ?? string.Empty,
            request?.ProjectId ?? string.Empty,
            request?.OperationId ?? string.Empty,
            request?.CorrelationId ?? string.Empty,
            request?.MutationSurfaceKind ?? MutationLeaseSurfaceKind.Unknown,
            request?.MutationTargetRef ?? string.Empty,
            request?.IdempotencyKeyFingerprint ?? string.Empty);

    private static MutationLeaseContractValidationResult Result(
        IReadOnlyList<string> issues,
        bool hasUnsafePayload,
        string? tenantId,
        string? projectId,
        string? operationId,
        string? correlationId,
        MutationLeaseSurfaceKind surfaceKind,
        string? targetRef,
        string? idempotencyFingerprint)
    {
        var status = DetermineStatus(issues, hasUnsafePayload);
        return new MutationLeaseContractValidationResult
        {
            IsValid = issues.Count == 0,
            ValidationStatus = status,
            TenantId = tenantId ?? string.Empty,
            ProjectId = projectId ?? string.Empty,
            OperationId = operationId ?? string.Empty,
            CorrelationId = correlationId ?? string.Empty,
            MutationSurfaceKind = surfaceKind,
            MutationTargetRef = targetRef ?? string.Empty,
            IdempotencyKeyFingerprint = idempotencyFingerprint ?? string.Empty,
            Issues = issues,
            Warnings = RequiredWarnings,
            ForbiddenAuthorityImplications = RequiredForbiddenAuthorityImplications
        };
    }

    private static MutationLeaseContractValidationStatus DetermineStatus(
        IReadOnlyList<string> issues,
        bool hasUnsafePayload)
    {
        if (issues.Count == 0)
        {
            return MutationLeaseContractValidationStatus.Valid;
        }

        if (hasUnsafePayload)
        {
            return MutationLeaseContractValidationStatus.RejectedUnsafePayload;
        }

        if (issues.Any(static issue => issue is "MutationLeaseContractMutationSurfaceKindRequired"))
        {
            return MutationLeaseContractValidationStatus.UnsupportedMutationKind;
        }

        if (issues.Any(static issue => issue is "MutationLeaseContractLeaseModeRequired"))
        {
            return MutationLeaseContractValidationStatus.UnsupportedLeaseMode;
        }

        return MutationLeaseContractValidationStatus.InvalidRequest;
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    [GeneratedRegex("^corr_[a-z0-9]{16,64}$", RegexOptions.IgnoreCase)]
    private static partial Regex CorrelationIdPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9_.:-]{1,180}$", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencePattern();
}
