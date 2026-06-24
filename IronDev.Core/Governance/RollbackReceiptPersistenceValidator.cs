using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class RollbackReceiptPersistenceValidator
{
    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "rollback receipt persistence is reference only",
        "persisted rollback receipt is witness evidence only",
        "persisted rollback receipt does not grant downstream authority"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "rollback receipt persistence is not rollback execution",
        "rollback receipt persistence is not rollback authority",
        "rollback receipt persistence is not rollback planning",
        "rollback receipt persistence is not rollback approval",
        "rollback receipt persistence is not retry",
        "rollback receipt persistence is not retry authority",
        "rollback receipt persistence is not recovery",
        "rollback receipt persistence is not recovery authority",
        "rollback receipt persistence is not resume",
        "rollback receipt persistence is not resume authority",
        "rollback receipt persistence is not workflow continuation",
        "rollback receipt persistence is not source apply",
        "rollback receipt persistence is not commit execution",
        "rollback receipt persistence is not push execution",
        "rollback receipt persistence is not pr creation",
        "rollback receipt persistence is not ready-for-review",
        "rollback receipt persistence is not reviewer request",
        "rollback receipt persistence is not merge",
        "rollback receipt persistence is not release",
        "rollback receipt persistence is not deploy",
        "rollback receipt persistence is not source authority",
        "rollback receipt persistence is not approval",
        "rollback receipt persistence is not policy satisfaction",
        "rollback receipt persistence is not validation freshness",
        "rollback receipt persistence is not patch freshness",
        "rollback receipt persistence is not source state proof",
        "rollback receipt persistence is not execution proof by itself",
        "rollback receipt persistence is not source safety proof",
        "rollback receipt persistence is not merge readiness",
        "rollback receipt persistence is not release readiness",
        "rollback receipt persistence is not deployment readiness"
    ];

    private static readonly string[] RawPayloadMarkers =
    [
        "raw rollback plan",
        "rollback plan:",
        "raw rollback command",
        "rollback command:",
        "raw rollback output",
        "rollback output:",
        "raw recovery output",
        "recovery output:",
        "raw inverse patch",
        "inverse patch",
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
        string.Concat("raw ", "git", "hub output"),
        string.Concat("git", "hub output:"),
        "raw pr title",
        "raw pr body",
        "pull request title:",
        "pull request body:",
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
        "command text",
        "shell command",
        "api request body"
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
        "ready for review",
        "ready to merge",
        "ready to release",
        "ready to deploy",
        "mark ready",
        "request reviewers",
        "continue workflow",
        "retry now",
        "resume now",
        "recover now",
        "rollback now",
        "merge now",
        "release now",
        "deploy now"
    ];

    public static RollbackReceiptPersistenceValidationResult ValidateRequest(
        PersistRollbackReceiptRequest? request)
    {
        if (request is null)
        {
            return Invalid(["RollbackReceiptPersistenceRequestRequired"], hasUnsafePayload: false);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddScopeIssues(request.TenantId, "RollbackReceiptPersistenceTenantId", issues, ref unsafePayload);
        AddScopeIssues(request.ProjectId, "RollbackReceiptPersistenceProjectId", issues, ref unsafePayload);
        AddOperationIdIssues(request.OperationId, "RollbackReceiptPersistenceOperationId", issues);
        AddCorrelationIdIssues(request, request.CorrelationId, "RollbackReceiptPersistenceCorrelationId", issues, ref unsafePayload);

        if (request.AsOfUtc == default)
        {
            issues.Add("RollbackReceiptPersistenceAsOfUtcRequired");
        }

        if (request.Receipt is null)
        {
            issues.Add("RollbackReceiptPersistenceRecordRequired");
            return Result(issues, unsafePayload);
        }

        AddRecordIssues(request, request.Receipt, issues, ref unsafePayload);

        return Result(issues, unsafePayload);
    }

    public static RollbackReceiptPersistenceValidationResult ValidateRecord(
        RollbackReceiptPersistenceRecord? record)
    {
        if (record is null)
        {
            return Invalid(["RollbackReceiptPersistenceRecordRequired"], hasUnsafePayload: false);
        }

        var issues = new List<string>();
        var unsafePayload = false;
        AddRecordIssues(null, record, issues, ref unsafePayload);
        return Result(issues, unsafePayload);
    }

    private static void AddRecordIssues(
        PersistRollbackReceiptRequest? request,
        RollbackReceiptPersistenceRecord record,
        List<string> issues,
        ref bool unsafePayload)
    {
        AddScopeIssues(record.TenantId, "RollbackReceiptPersistenceRecordTenantId", issues, ref unsafePayload);
        AddScopeIssues(record.ProjectId, "RollbackReceiptPersistenceRecordProjectId", issues, ref unsafePayload);
        AddOperationIdIssues(record.OperationId, "RollbackReceiptPersistenceRecordOperationId", issues);
        AddCorrelationIdIssues(request, record.CorrelationId, "RollbackReceiptPersistenceRecordCorrelationId", issues, ref unsafePayload);

        AddRequiredReferenceIssues(record.ReceiptId, "RollbackReceiptPersistenceReceiptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.RollbackAttemptId, "RollbackReceiptPersistenceAttemptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.RollbackPlanRef, "RollbackReceiptPersistencePlanRef", issues, ref unsafePayload);
        AddRollbackTargetKindIssues(record.RollbackTargetKind, issues);
        AddRequiredReferenceIssues(record.RollbackTargetRef, "RollbackReceiptPersistenceTargetRef", issues, ref unsafePayload);
        AddOptionalSafeTextIssues(record.RollbackReasonCode, "RollbackReceiptPersistenceReasonCode", issues, ref unsafePayload);
        AddTargetSpecificIssues(record, issues, ref unsafePayload);
        AddCommonOptionalReferenceIssues(record, issues, ref unsafePayload);
        AddOutcomeIssues(record, issues, ref unsafePayload);

        AddRequiredSafeTextIssues(record.Source, "RollbackReceiptPersistenceSource", issues, ref unsafePayload);
        if (string.Equals(record.Source, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("RollbackReceiptPersistenceSourceUnknown");
        }

        if (record.IsRedacted)
        {
            AddRequiredSafeTextIssues(record.RedactionReason, "RollbackReceiptPersistenceRedactionReason", issues, ref unsafePayload);
        }
        else
        {
            AddOptionalSafeTextIssues(record.RedactionReason, "RollbackReceiptPersistenceRedactionReason", issues, ref unsafePayload);
        }

        AddOptionalReferenceIssues(record.RecordFingerprint, "RollbackReceiptPersistenceRecordFingerprint", issues, ref unsafePayload);

        if (request is not null)
        {
            AddBindingIssue(request.TenantId, record.TenantId, "RollbackReceiptPersistenceTenantMismatch", issues);
            AddBindingIssue(request.ProjectId, record.ProjectId, "RollbackReceiptPersistenceProjectMismatch", issues);
            AddBindingIssue(request.OperationId, record.OperationId, "RollbackReceiptPersistenceOperationMismatch", issues);
            AddBindingIssue(request.CorrelationId, record.CorrelationId, "RollbackReceiptPersistenceCorrelationMismatch", issues);
        }
    }

    private static void AddTargetSpecificIssues(
        RollbackReceiptPersistenceRecord record,
        List<string> issues,
        ref bool unsafePayload)
    {
        switch (record.RollbackTargetKind)
        {
            case RollbackTargetKind.SourceApply:
                AddRequiredReferenceIssues(record.SourceApplyReceiptId, "RollbackReceiptPersistenceSourceApplyReceiptId", issues, ref unsafePayload);
                break;

            case RollbackTargetKind.Commit:
                AddRequiredReferenceIssues(record.CommitReceiptId, "RollbackReceiptPersistenceCommitReceiptId", issues, ref unsafePayload);
                AddRequiredCommitHashIssues(record.CommitSha, "RollbackReceiptPersistenceCommitSha", issues, ref unsafePayload);
                break;

            case RollbackTargetKind.Push:
                AddRequiredReferenceIssues(record.PushReceiptId, "RollbackReceiptPersistencePushReceiptId", issues, ref unsafePayload);
                AddRequiredCommitHashIssues(record.CommitSha, "RollbackReceiptPersistenceCommitSha", issues, ref unsafePayload);
                break;

            case RollbackTargetKind.DraftPullRequest:
                AddRequiredReferenceIssues(record.DraftPullRequestReceiptId, "RollbackReceiptPersistenceDraftPullRequestReceiptId", issues, ref unsafePayload);
                AddRequiredReferenceIssues(record.PullRequestRef, "RollbackReceiptPersistencePullRequestRef", issues, ref unsafePayload);
                break;

            case RollbackTargetKind.OperationState:
                AddOperationIdIssues(record.OriginalOperationId, "RollbackReceiptPersistenceOriginalOperationId", issues);
                break;
        }

        AddOptionalReferenceIssues(record.OriginalAttemptId, "RollbackReceiptPersistenceOriginalAttemptId", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.SourceApplyReceiptId, "RollbackReceiptPersistenceSourceApplyReceiptId", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.CommitReceiptId, "RollbackReceiptPersistenceCommitReceiptId", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.PushReceiptId, "RollbackReceiptPersistencePushReceiptId", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.DraftPullRequestReceiptId, "RollbackReceiptPersistenceDraftPullRequestReceiptId", issues, ref unsafePayload);
        AddOptionalCommitHashIssues(record.CommitSha, "RollbackReceiptPersistenceCommitSha", issues, ref unsafePayload);
    }

    private static void AddCommonOptionalReferenceIssues(
        RollbackReceiptPersistenceRecord record,
        List<string> issues,
        ref bool unsafePayload)
    {
        AddOptionalReferenceIssues(record.RollbackResultRef, "RollbackReceiptPersistenceResultRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.RepositoryRef, "RollbackReceiptPersistenceRepositoryRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.TargetBranchRef, "RollbackReceiptPersistenceTargetBranchRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.PullRequestRef, "RollbackReceiptPersistencePullRequestRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.PullRequestNumberRef, "RollbackReceiptPersistencePullRequestNumberRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.WorktreeBeforeRef, "RollbackReceiptPersistenceWorktreeBeforeRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.WorktreeAfterRef, "RollbackReceiptPersistenceWorktreeAfterRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.ValidationResultRef, "RollbackReceiptPersistenceValidationResultRef", issues, ref unsafePayload);
    }

    private static void AddOutcomeIssues(
        RollbackReceiptPersistenceRecord record,
        List<string> issues,
        ref bool unsafePayload)
    {
        if (record.OutcomeKind == RollbackReceiptOutcomeKind.Unknown ||
            !Enum.IsDefined(record.OutcomeKind))
        {
            issues.Add("RollbackReceiptPersistenceOutcomeKindRequired");
        }

        if (record.OutcomeKind == RollbackReceiptOutcomeKind.Succeeded &&
            string.IsNullOrWhiteSpace(record.RollbackResultRef))
        {
            issues.Add("RollbackReceiptPersistenceResultRefRequired");
        }

        if (record.OutcomeKind == RollbackReceiptOutcomeKind.Started &&
            !string.IsNullOrWhiteSpace(record.RollbackResultRef))
        {
            issues.Add("RollbackReceiptPersistenceStartedResultRefUnexpected");
        }

        if (record.StartedAtUtc == default)
        {
            issues.Add("RollbackReceiptPersistenceStartedAtUtcRequired");
        }

        if (record.RecordedAtUtc == default)
        {
            issues.Add("RollbackReceiptPersistenceRecordedAtUtcRequired");
        }

        if (IsTerminal(record.OutcomeKind) && record.CompletedAtUtc is null)
        {
            issues.Add("RollbackReceiptPersistenceCompletedAtUtcRequired");
        }

        if (record.CompletedAtUtc is not null &&
            record.StartedAtUtc != default &&
            record.CompletedAtUtc.Value < record.StartedAtUtc)
        {
            issues.Add("RollbackReceiptPersistenceCompletedBeforeStarted");
        }

        if (!string.IsNullOrWhiteSpace(record.OutcomeReasonCode))
        {
            if (ContainsUnsafeText(record.OutcomeReasonCode, ref unsafePayload) ||
                record.OutcomeReasonCode.Length > 180)
            {
                issues.Add("RollbackReceiptPersistenceOutcomeReasonCodeInvalid");
            }
        }
    }

    private static void AddRollbackTargetKindIssues(
        RollbackTargetKind targetKind,
        List<string> issues)
    {
        if (targetKind == RollbackTargetKind.Unknown ||
            !Enum.IsDefined(targetKind))
        {
            issues.Add("RollbackReceiptPersistenceTargetKindRequired");
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
        PersistRollbackReceiptRequest? request,
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
                SurfaceId = "rollback-receipt-persistence",
                ObservedAtUtc = DateTimeOffset.UnixEpoch,
                Source = "e05-validator"
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

    private static bool IsTerminal(RollbackReceiptOutcomeKind outcomeKind) =>
        outcomeKind is RollbackReceiptOutcomeKind.Succeeded or
            RollbackReceiptOutcomeKind.Failed or
            RollbackReceiptOutcomeKind.Interrupted or
            RollbackReceiptOutcomeKind.Cancelled;

    private static RollbackReceiptPersistenceValidationResult Invalid(
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

    private static RollbackReceiptPersistenceValidationResult Result(
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
