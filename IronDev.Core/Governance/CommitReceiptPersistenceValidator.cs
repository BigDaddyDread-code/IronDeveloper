using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class CommitReceiptPersistenceValidator
{
    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "commit receipt persistence is reference only",
        "persisted commit receipt is witness evidence only",
        "persisted commit receipt does not grant downstream authority"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "commit receipt persistence is not commit execution",
        "commit receipt persistence is not commit package creation",
        "commit receipt persistence is not source apply",
        "commit receipt persistence is not source authority",
        "commit receipt persistence is not approval",
        "commit receipt persistence is not policy satisfaction",
        "commit receipt persistence is not validation freshness",
        "commit receipt persistence is not patch freshness",
        "commit receipt persistence is not source state proof",
        "commit receipt persistence is not execution proof by itself",
        "commit receipt persistence is not push authority",
        "commit receipt persistence is not pull request authority",
        "commit receipt persistence is not merge readiness",
        "commit receipt persistence is not release readiness",
        "commit receipt persistence is not deployment readiness",
        "commit receipt persistence is not retry authority",
        "commit receipt persistence is not rollback authority",
        "commit receipt persistence is not recovery authority",
        "commit receipt persistence is not workflow continuation"
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
        "ready to push",
        "ready to pr",
        "ready to merge",
        "mark ready",
        "request reviewers",
        "continue workflow",
        "retry now",
        "resume now",
        "rollback now"
    ];

    public static CommitReceiptPersistenceValidationResult ValidateRequest(
        PersistCommitReceiptRequest? request)
    {
        if (request is null)
        {
            return Invalid(["CommitReceiptPersistenceRequestRequired"], hasUnsafePayload: false);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddScopeIssues(request.TenantId, "CommitReceiptPersistenceTenantId", issues, ref unsafePayload);
        AddScopeIssues(request.ProjectId, "CommitReceiptPersistenceProjectId", issues, ref unsafePayload);
        AddOperationIdIssues(request.OperationId, "CommitReceiptPersistenceOperationId", issues);
        AddCorrelationIdIssues(request, request.CorrelationId, "CommitReceiptPersistenceCorrelationId", issues, ref unsafePayload);

        if (request.AsOfUtc == default)
        {
            issues.Add("CommitReceiptPersistenceAsOfUtcRequired");
        }

        if (request.Receipt is null)
        {
            issues.Add("CommitReceiptPersistenceRecordRequired");
            return Result(issues, unsafePayload);
        }

        AddRecordIssues(request, request.Receipt, issues, ref unsafePayload);

        return Result(issues, unsafePayload);
    }

    public static CommitReceiptPersistenceValidationResult ValidateRecord(
        CommitReceiptPersistenceRecord? record)
    {
        if (record is null)
        {
            return Invalid(["CommitReceiptPersistenceRecordRequired"], hasUnsafePayload: false);
        }

        var issues = new List<string>();
        var unsafePayload = false;
        AddRecordIssues(null, record, issues, ref unsafePayload);
        return Result(issues, unsafePayload);
    }

    private static void AddRecordIssues(
        PersistCommitReceiptRequest? request,
        CommitReceiptPersistenceRecord record,
        List<string> issues,
        ref bool unsafePayload)
    {
        AddScopeIssues(record.TenantId, "CommitReceiptPersistenceRecordTenantId", issues, ref unsafePayload);
        AddScopeIssues(record.ProjectId, "CommitReceiptPersistenceRecordProjectId", issues, ref unsafePayload);
        AddOperationIdIssues(record.OperationId, "CommitReceiptPersistenceRecordOperationId", issues);
        AddCorrelationIdIssues(request, record.CorrelationId, "CommitReceiptPersistenceRecordCorrelationId", issues, ref unsafePayload);

        AddRequiredReferenceIssues(record.ReceiptId, "CommitReceiptPersistenceReceiptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.CommitAttemptId, "CommitReceiptPersistenceAttemptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.CommitPackageId, "CommitReceiptPersistencePackageId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.CommitPackageHash, "CommitReceiptPersistencePackageHash", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.SourceApplyReceiptId, "CommitReceiptPersistenceSourceApplyReceiptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.SourceApplyAttemptId, "CommitReceiptPersistenceSourceApplyAttemptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.PatchArtifactId, "CommitReceiptPersistencePatchArtifactId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.PatchArtifactHash, "CommitReceiptPersistencePatchArtifactHash", issues, ref unsafePayload);

        AddOptionalReferenceIssues(record.PatchBaseRef, "CommitReceiptPersistencePatchBaseRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.ValidationResultRef, "CommitReceiptPersistenceValidationResultRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.AcceptedApprovalRef, "CommitReceiptPersistenceAcceptedApprovalRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.PolicySatisfactionRef, "CommitReceiptPersistencePolicySatisfactionRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.DryRunRef, "CommitReceiptPersistenceDryRunRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.WorktreeBeforeRef, "CommitReceiptPersistenceWorktreeBeforeRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.WorktreeAfterRef, "CommitReceiptPersistenceWorktreeAfterRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.RepositoryRef, "CommitReceiptPersistenceRepositoryRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.TargetBranchRef, "CommitReceiptPersistenceTargetBranchRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.BaseCommitRef, "CommitReceiptPersistenceBaseCommitRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.ParentCommitRef, "CommitReceiptPersistenceParentCommitRef", issues, ref unsafePayload);

        if (record.OutcomeKind == CommitReceiptOutcomeKind.Unknown ||
            !Enum.IsDefined(record.OutcomeKind))
        {
            issues.Add("CommitReceiptPersistenceOutcomeKindRequired");
        }

        AddCommitShaIssues(record.CommitSha, record.OutcomeKind, issues, ref unsafePayload);
        AddOptionalCommitHashIssues(record.CommitTreeHash, "CommitReceiptPersistenceCommitTreeHash", issues, ref unsafePayload);
        AddOptionalSafeTextIssues(record.OutcomeReasonCode, "CommitReceiptPersistenceOutcomeReasonCode", issues, ref unsafePayload);

        if (record.StartedAtUtc == default)
        {
            issues.Add("CommitReceiptPersistenceStartedAtUtcRequired");
        }

        if (record.RecordedAtUtc == default)
        {
            issues.Add("CommitReceiptPersistenceRecordedAtUtcRequired");
        }

        if (IsTerminal(record.OutcomeKind) && record.CompletedAtUtc is null)
        {
            issues.Add("CommitReceiptPersistenceCompletedAtUtcRequired");
        }

        if (record.CompletedAtUtc is not null &&
            record.StartedAtUtc != default &&
            record.CompletedAtUtc.Value < record.StartedAtUtc)
        {
            issues.Add("CommitReceiptPersistenceCompletedBeforeStarted");
        }

        AddRequiredSafeTextIssues(record.Source, "CommitReceiptPersistenceSource", issues, ref unsafePayload);

        if (record.IsRedacted)
        {
            AddRequiredSafeTextIssues(record.RedactionReason, "CommitReceiptPersistenceRedactionReason", issues, ref unsafePayload);
        }
        else
        {
            AddOptionalSafeTextIssues(record.RedactionReason, "CommitReceiptPersistenceRedactionReason", issues, ref unsafePayload);
        }

        AddOptionalReferenceIssues(record.RecordFingerprint, "CommitReceiptPersistenceRecordFingerprint", issues, ref unsafePayload);

        if (request is not null)
        {
            AddBindingIssue(request.TenantId, record.TenantId, "CommitReceiptPersistenceTenantMismatch", issues);
            AddBindingIssue(request.ProjectId, record.ProjectId, "CommitReceiptPersistenceProjectMismatch", issues);
            AddBindingIssue(request.OperationId, record.OperationId, "CommitReceiptPersistenceOperationMismatch", issues);
            AddBindingIssue(request.CorrelationId, record.CorrelationId, "CommitReceiptPersistenceCorrelationMismatch", issues);
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
        PersistCommitReceiptRequest? request,
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
                SurfaceKind = OperationCorrelationSurfaceKind.ReceiptMetadata,
                SurfaceId = "commit-receipt-persistence",
                ObservedAtUtc = DateTimeOffset.UnixEpoch,
                Source = "e02-validator"
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

    private static void AddCommitShaIssues(
        string? value,
        CommitReceiptOutcomeKind outcomeKind,
        List<string> issues,
        ref bool unsafePayload)
    {
        if (outcomeKind == CommitReceiptOutcomeKind.Succeeded &&
            string.IsNullOrWhiteSpace(value))
        {
            issues.Add("CommitReceiptPersistenceCommitShaRequired");
            return;
        }

        if (outcomeKind is CommitReceiptOutcomeKind.Failed or CommitReceiptOutcomeKind.Interrupted or CommitReceiptOutcomeKind.Cancelled &&
            !string.IsNullOrWhiteSpace(value))
        {
            issues.Add("CommitReceiptPersistenceCommitShaUnexpected");
        }

        AddOptionalCommitHashIssues(value, "CommitReceiptPersistenceCommitSha", issues, ref unsafePayload);
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

    private static bool IsTerminal(CommitReceiptOutcomeKind outcomeKind) =>
        outcomeKind is CommitReceiptOutcomeKind.Succeeded or
            CommitReceiptOutcomeKind.Failed or
            CommitReceiptOutcomeKind.Interrupted or
            CommitReceiptOutcomeKind.Cancelled;

    private static CommitReceiptPersistenceValidationResult Invalid(
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

    private static CommitReceiptPersistenceValidationResult Result(
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
