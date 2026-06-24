using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class PushReceiptPersistenceValidator
{
    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "push receipt persistence is reference only",
        "persisted push receipt is witness evidence only",
        "persisted push receipt does not grant downstream authority"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "push receipt persistence is not push execution",
        "push receipt persistence is not commit execution",
        "push receipt persistence is not source apply",
        "push receipt persistence is not source authority",
        "push receipt persistence is not approval",
        "push receipt persistence is not policy satisfaction",
        "push receipt persistence is not validation freshness",
        "push receipt persistence is not patch freshness",
        "push receipt persistence is not source state proof",
        "push receipt persistence is not execution proof by itself",
        "push receipt persistence is not pull request authority",
        "push receipt persistence is not ready-for-review authority",
        "push receipt persistence is not reviewer-request authority",
        "push receipt persistence is not merge readiness",
        "push receipt persistence is not release readiness",
        "push receipt persistence is not deployment readiness",
        "push receipt persistence is not retry authority",
        "push receipt persistence is not rollback authority",
        "push receipt persistence is not recovery authority",
        "push receipt persistence is not workflow continuation"
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
        "raw push output",
        string.Concat("raw ", "gi", "t output"),
        string.Concat("gi", "t output:"),
        "author identity payload",
        "validation log",
        "raw evidence payload",
        "raw receipt payload",
        "raw request body",
        "raw response body",
        "prompt text",
        "private reasoning",
        "chain-of-thought",
        "scratchpad",
        "execution transcript",
        "command text"
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
        "policy satisfied",
        "ready to pr",
        "ready to merge",
        "ready to release",
        "ready to deploy",
        "mark ready",
        "request reviewers",
        "continue workflow",
        "retry now",
        "resume now",
        "recover now",
        "rollback now"
    ];

    public static PushReceiptPersistenceValidationResult ValidateRequest(
        PersistPushReceiptRequest? request)
    {
        if (request is null)
        {
            return Invalid(["PushReceiptPersistenceRequestRequired"], hasUnsafePayload: false);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddScopeIssues(request.TenantId, "PushReceiptPersistenceTenantId", issues, ref unsafePayload);
        AddScopeIssues(request.ProjectId, "PushReceiptPersistenceProjectId", issues, ref unsafePayload);
        AddOperationIdIssues(request.OperationId, "PushReceiptPersistenceOperationId", issues);
        AddCorrelationIdIssues(request, request.CorrelationId, "PushReceiptPersistenceCorrelationId", issues, ref unsafePayload);

        if (request.AsOfUtc == default)
        {
            issues.Add("PushReceiptPersistenceAsOfUtcRequired");
        }

        if (request.Receipt is null)
        {
            issues.Add("PushReceiptPersistenceRecordRequired");
            return Result(issues, unsafePayload);
        }

        AddRecordIssues(request, request.Receipt, issues, ref unsafePayload);

        return Result(issues, unsafePayload);
    }

    public static PushReceiptPersistenceValidationResult ValidateRecord(
        PushReceiptPersistenceRecord? record)
    {
        if (record is null)
        {
            return Invalid(["PushReceiptPersistenceRecordRequired"], hasUnsafePayload: false);
        }

        var issues = new List<string>();
        var unsafePayload = false;
        AddRecordIssues(null, record, issues, ref unsafePayload);
        return Result(issues, unsafePayload);
    }

    private static void AddRecordIssues(
        PersistPushReceiptRequest? request,
        PushReceiptPersistenceRecord record,
        List<string> issues,
        ref bool unsafePayload)
    {
        AddScopeIssues(record.TenantId, "PushReceiptPersistenceRecordTenantId", issues, ref unsafePayload);
        AddScopeIssues(record.ProjectId, "PushReceiptPersistenceRecordProjectId", issues, ref unsafePayload);
        AddOperationIdIssues(record.OperationId, "PushReceiptPersistenceRecordOperationId", issues);
        AddCorrelationIdIssues(request, record.CorrelationId, "PushReceiptPersistenceRecordCorrelationId", issues, ref unsafePayload);

        AddRequiredReferenceIssues(record.ReceiptId, "PushReceiptPersistenceReceiptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.PushAttemptId, "PushReceiptPersistenceAttemptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.CommitReceiptId, "PushReceiptPersistenceCommitReceiptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.CommitAttemptId, "PushReceiptPersistenceCommitAttemptId", issues, ref unsafePayload);
        AddRequiredCommitHashIssues(record.CommitSha, "PushReceiptPersistenceCommitSha", issues, ref unsafePayload);
        AddOptionalCommitHashIssues(record.CommitTreeHash, "PushReceiptPersistenceCommitTreeHash", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.RepositoryRef, "PushReceiptPersistenceRepositoryRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.RemoteRef, "PushReceiptPersistenceRemoteRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.TargetBranchRef, "PushReceiptPersistenceTargetBranchRef", issues, ref unsafePayload);

        AddOptionalReferenceIssues(record.ExpectedRemoteHeadRef, "PushReceiptPersistenceExpectedRemoteHeadRef", issues, ref unsafePayload);
        AddObservedRemoteHeadIssues(record.ObservedRemoteHeadRef, record.OutcomeKind, issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.PushResultRef, "PushReceiptPersistencePushResultRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.SourceApplyReceiptId, "PushReceiptPersistenceSourceApplyReceiptId", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.CommitPackageId, "PushReceiptPersistenceCommitPackageId", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.PatchArtifactId, "PushReceiptPersistencePatchArtifactId", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.PatchArtifactHash, "PushReceiptPersistencePatchArtifactHash", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.ValidationResultRef, "PushReceiptPersistenceValidationResultRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.AcceptedApprovalRef, "PushReceiptPersistenceAcceptedApprovalRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.PolicySatisfactionRef, "PushReceiptPersistencePolicySatisfactionRef", issues, ref unsafePayload);

        if (record.OutcomeKind == PushReceiptOutcomeKind.Unknown ||
            !Enum.IsDefined(record.OutcomeKind))
        {
            issues.Add("PushReceiptPersistenceOutcomeKindRequired");
        }

        AddOptionalSafeTextIssues(record.OutcomeReasonCode, "PushReceiptPersistenceOutcomeReasonCode", issues, ref unsafePayload);

        if (record.StartedAtUtc == default)
        {
            issues.Add("PushReceiptPersistenceStartedAtUtcRequired");
        }

        if (record.RecordedAtUtc == default)
        {
            issues.Add("PushReceiptPersistenceRecordedAtUtcRequired");
        }

        if (IsTerminal(record.OutcomeKind) && record.CompletedAtUtc is null)
        {
            issues.Add("PushReceiptPersistenceCompletedAtUtcRequired");
        }

        if (record.CompletedAtUtc is not null &&
            record.StartedAtUtc != default &&
            record.CompletedAtUtc.Value < record.StartedAtUtc)
        {
            issues.Add("PushReceiptPersistenceCompletedBeforeStarted");
        }

        AddRequiredSafeTextIssues(record.Source, "PushReceiptPersistenceSource", issues, ref unsafePayload);
        if (string.Equals(record.Source, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("PushReceiptPersistenceSourceUnknown");
        }

        if (record.IsRedacted)
        {
            AddRequiredSafeTextIssues(record.RedactionReason, "PushReceiptPersistenceRedactionReason", issues, ref unsafePayload);
        }
        else
        {
            AddOptionalSafeTextIssues(record.RedactionReason, "PushReceiptPersistenceRedactionReason", issues, ref unsafePayload);
        }

        AddOptionalReferenceIssues(record.RecordFingerprint, "PushReceiptPersistenceRecordFingerprint", issues, ref unsafePayload);

        if (request is not null)
        {
            AddBindingIssue(request.TenantId, record.TenantId, "PushReceiptPersistenceTenantMismatch", issues);
            AddBindingIssue(request.ProjectId, record.ProjectId, "PushReceiptPersistenceProjectMismatch", issues);
            AddBindingIssue(request.OperationId, record.OperationId, "PushReceiptPersistenceOperationMismatch", issues);
            AddBindingIssue(request.CorrelationId, record.CorrelationId, "PushReceiptPersistenceCorrelationMismatch", issues);
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
        PersistPushReceiptRequest? request,
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
            return;
        }

        if (request is not null &&
            !string.IsNullOrWhiteSpace(request.TenantId) &&
            !string.IsNullOrWhiteSpace(request.ProjectId) &&
            !string.IsNullOrWhiteSpace(request.OperationId))
        {
            var link = new OperationCorrelationLink
            {
                TenantId = request.TenantId,
                ProjectId = request.ProjectId,
                OperationId = request.OperationId,
                CorrelationId = correlationId,
                SurfaceKind = OperationCorrelationSurfaceKind.PushReceipt,
                SurfaceId = "push-receipt-persistence",
                ObservedAtUtc = DateTimeOffset.UnixEpoch,
                Source = "e03-validator"
            };

            if (!OperationCorrelationValidator.ValidateLink(link).IsValid)
            {
                issues.Add($"{prefix}Invalid");
            }
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

        if (ContainsUnsafeText(value, ref unsafePayload) ||
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

    private static void AddRequiredCommitHashIssues(
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

        AddOptionalCommitHashIssues(value, prefix, issues, ref unsafePayload);
    }

    private static void AddOptionalCommitHashIssues(
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
            value.Any(char.IsWhiteSpace) ||
            value.Contains('/') ||
            value.Contains('\\') ||
            value.Contains("://", StringComparison.Ordinal) ||
            !CommitHashPattern().IsMatch(value))
        {
            issues.Add($"{prefix}Invalid");
        }
    }

    private static void AddObservedRemoteHeadIssues(
        string? value,
        PushReceiptOutcomeKind outcomeKind,
        List<string> issues,
        ref bool unsafePayload)
    {
        if (outcomeKind == PushReceiptOutcomeKind.Succeeded &&
            string.IsNullOrWhiteSpace(value))
        {
            issues.Add("PushReceiptPersistenceObservedRemoteHeadRefRequired");
            return;
        }

        if (outcomeKind is PushReceiptOutcomeKind.Failed or PushReceiptOutcomeKind.Interrupted or PushReceiptOutcomeKind.Cancelled &&
            !string.IsNullOrWhiteSpace(value))
        {
            issues.Add("PushReceiptPersistenceObservedRemoteHeadRefUnexpected");
        }

        AddOptionalReferenceIssues(value, "PushReceiptPersistenceObservedRemoteHeadRef", issues, ref unsafePayload);
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

    private static void AddBindingIssue(
        string? expected,
        string? actual,
        string issue,
        List<string> issues)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(issue);
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

    private static bool IsTerminal(PushReceiptOutcomeKind outcomeKind) =>
        outcomeKind is PushReceiptOutcomeKind.Succeeded or
            PushReceiptOutcomeKind.Failed or
            PushReceiptOutcomeKind.Interrupted or
            PushReceiptOutcomeKind.Cancelled;

    private static PushReceiptPersistenceValidationResult Invalid(
        IReadOnlyList<string> issues,
        bool hasUnsafePayload) =>
        new()
        {
            IsValid = false,
            HasUnsafePayload = hasUnsafePayload,
            Issues = issues,
            Warnings = RequiredWarnings,
            ForbiddenAuthorityImplications = RequiredForbiddenAuthorityImplications
        };

    private static PushReceiptPersistenceValidationResult Result(
        IReadOnlyList<string> issues,
        bool hasUnsafePayload) =>
        new()
        {
            IsValid = issues.Count == 0,
            HasUnsafePayload = hasUnsafePayload,
            Issues = issues,
            Warnings = RequiredWarnings,
            ForbiddenAuthorityImplications = RequiredForbiddenAuthorityImplications
        };

    [GeneratedRegex("^corr_[a-z0-9]{16,64}$", RegexOptions.IgnoreCase)]
    private static partial Regex CorrelationIdPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9_.:-]{1,180}$", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencePattern();

    [GeneratedRegex("^(?:[a-f0-9]{40}|[a-f0-9]{64})$", RegexOptions.IgnoreCase)]
    private static partial Regex CommitHashPattern();
}
