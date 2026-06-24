using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class SourceApplyReceiptPersistenceValidator
{
    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "source apply receipt persistence is reference only",
        "persisted source apply receipt is witness evidence only",
        "persisted source apply receipt does not grant downstream authority"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "receipt persistence is not source apply",
        "receipt persistence is not source authority",
        "receipt persistence is not approval",
        "receipt persistence is not policy satisfaction",
        "receipt persistence is not validation freshness",
        "receipt persistence is not patch freshness",
        "receipt persistence is not source state proof",
        "receipt persistence is not execution proof by itself",
        "receipt persistence is not commit authority",
        "receipt persistence is not push authority",
        "receipt persistence is not pull request authority",
        "receipt persistence is not merge readiness",
        "receipt persistence is not release readiness",
        "receipt persistence is not deployment readiness",
        "receipt persistence is not retry authority",
        "receipt persistence is not rollback authority",
        "receipt persistence is not recovery authority",
        "receipt persistence is not workflow continuation"
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
        "ready to commit",
        "ready to push",
        "ready to pr",
        "ready to merge",
        "continue workflow",
        "retry now",
        "resume now",
        "rollback now"
    ];

    public static SourceApplyReceiptPersistenceValidationResult ValidateRequest(
        PersistSourceApplyReceiptRequest? request)
    {
        if (request is null)
        {
            return Invalid(["SourceApplyReceiptPersistenceRequestRequired"], hasUnsafePayload: false);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddScopeIssues(request.TenantId, "SourceApplyReceiptPersistenceTenantId", issues, ref unsafePayload);
        AddScopeIssues(request.ProjectId, "SourceApplyReceiptPersistenceProjectId", issues, ref unsafePayload);
        AddOperationIdIssues(request.OperationId, "SourceApplyReceiptPersistenceOperationId", issues);
        AddCorrelationIdIssues(request, request.CorrelationId, "SourceApplyReceiptPersistenceCorrelationId", issues, ref unsafePayload);

        if (request.AsOfUtc == default)
        {
            issues.Add("SourceApplyReceiptPersistenceAsOfUtcRequired");
        }

        if (request.Receipt is null)
        {
            issues.Add("SourceApplyReceiptPersistenceRecordRequired");
            return Result(issues, unsafePayload);
        }

        AddRecordIssues(request, request.Receipt, issues, ref unsafePayload);

        return Result(issues, unsafePayload);
    }

    public static SourceApplyReceiptPersistenceValidationResult ValidateRecord(
        SourceApplyReceiptPersistenceRecord? record)
    {
        if (record is null)
        {
            return Invalid(["SourceApplyReceiptPersistenceRecordRequired"], hasUnsafePayload: false);
        }

        var issues = new List<string>();
        var unsafePayload = false;
        AddRecordIssues(null, record, issues, ref unsafePayload);
        return Result(issues, unsafePayload);
    }

    private static void AddRecordIssues(
        PersistSourceApplyReceiptRequest? request,
        SourceApplyReceiptPersistenceRecord record,
        List<string> issues,
        ref bool unsafePayload)
    {
        AddScopeIssues(record.TenantId, "SourceApplyReceiptPersistenceRecordTenantId", issues, ref unsafePayload);
        AddScopeIssues(record.ProjectId, "SourceApplyReceiptPersistenceRecordProjectId", issues, ref unsafePayload);
        AddOperationIdIssues(record.OperationId, "SourceApplyReceiptPersistenceRecordOperationId", issues);
        AddCorrelationIdIssues(request, record.CorrelationId, "SourceApplyReceiptPersistenceRecordCorrelationId", issues, ref unsafePayload);

        AddRequiredReferenceIssues(record.ReceiptId, "SourceApplyReceiptPersistenceReceiptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.SourceApplyAttemptId, "SourceApplyReceiptPersistenceAttemptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.PatchArtifactId, "SourceApplyReceiptPersistencePatchArtifactId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.PatchArtifactHash, "SourceApplyReceiptPersistencePatchArtifactHash", issues, ref unsafePayload);

        AddOptionalReferenceIssues(record.PatchBaseRef, "SourceApplyReceiptPersistencePatchBaseRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.ValidationResultRef, "SourceApplyReceiptPersistenceValidationResultRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.AcceptedApprovalRef, "SourceApplyReceiptPersistenceAcceptedApprovalRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.PolicySatisfactionRef, "SourceApplyReceiptPersistencePolicySatisfactionRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.DryRunRef, "SourceApplyReceiptPersistenceDryRunRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.WorktreeBeforeRef, "SourceApplyReceiptPersistenceWorktreeBeforeRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.WorktreeAfterRef, "SourceApplyReceiptPersistenceWorktreeAfterRef", issues, ref unsafePayload);

        if (record.OutcomeKind == SourceApplyReceiptOutcomeKind.Unknown ||
            !Enum.IsDefined(record.OutcomeKind))
        {
            issues.Add("SourceApplyReceiptPersistenceOutcomeKindRequired");
        }

        AddOptionalSafeTextIssues(record.OutcomeReasonCode, "SourceApplyReceiptPersistenceOutcomeReasonCode", issues, ref unsafePayload);

        if (record.StartedAtUtc == default)
        {
            issues.Add("SourceApplyReceiptPersistenceStartedAtUtcRequired");
        }

        if (record.RecordedAtUtc == default)
        {
            issues.Add("SourceApplyReceiptPersistenceRecordedAtUtcRequired");
        }

        if (IsTerminal(record.OutcomeKind) && record.CompletedAtUtc is null)
        {
            issues.Add("SourceApplyReceiptPersistenceCompletedAtUtcRequired");
        }

        if (record.CompletedAtUtc is not null &&
            record.StartedAtUtc != default &&
            record.CompletedAtUtc.Value < record.StartedAtUtc)
        {
            issues.Add("SourceApplyReceiptPersistenceCompletedBeforeStarted");
        }

        AddRequiredSafeTextIssues(record.Source, "SourceApplyReceiptPersistenceSource", issues, ref unsafePayload);

        if (record.IsRedacted)
        {
            AddRequiredSafeTextIssues(record.RedactionReason, "SourceApplyReceiptPersistenceRedactionReason", issues, ref unsafePayload);
        }
        else
        {
            AddOptionalSafeTextIssues(record.RedactionReason, "SourceApplyReceiptPersistenceRedactionReason", issues, ref unsafePayload);
        }

        AddOptionalReferenceIssues(record.RecordFingerprint, "SourceApplyReceiptPersistenceRecordFingerprint", issues, ref unsafePayload);

        if (request is not null)
        {
            AddBindingIssue(request.TenantId, record.TenantId, "SourceApplyReceiptPersistenceTenantMismatch", issues);
            AddBindingIssue(request.ProjectId, record.ProjectId, "SourceApplyReceiptPersistenceProjectMismatch", issues);
            AddBindingIssue(request.OperationId, record.OperationId, "SourceApplyReceiptPersistenceOperationMismatch", issues);
            AddBindingIssue(request.CorrelationId, record.CorrelationId, "SourceApplyReceiptPersistenceCorrelationMismatch", issues);
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
        PersistSourceApplyReceiptRequest? request,
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
                SurfaceKind = OperationCorrelationSurfaceKind.SourceApplyReceipt,
                SurfaceId = "source-apply-receipt-persistence",
                ObservedAtUtc = DateTimeOffset.UnixEpoch,
                Source = "e01-validator"
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

        if (ContainsUnsafeText(value, ref unsafePayload) ||
            value.Length > 180 ||
            value.Any(char.IsWhiteSpace) ||
            value.Contains('/') ||
            value.Contains('\\') ||
            value.Contains("://", StringComparison.Ordinal) ||
            !ReferencePattern().IsMatch(value))
        {
            issues.Add($"{prefix}Invalid");
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

    private static bool IsTerminal(SourceApplyReceiptOutcomeKind outcomeKind) =>
        outcomeKind is SourceApplyReceiptOutcomeKind.Succeeded or
            SourceApplyReceiptOutcomeKind.Failed or
            SourceApplyReceiptOutcomeKind.Interrupted or
            SourceApplyReceiptOutcomeKind.Cancelled;

    private static SourceApplyReceiptPersistenceValidationResult Invalid(
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

    private static SourceApplyReceiptPersistenceValidationResult Result(
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
}
