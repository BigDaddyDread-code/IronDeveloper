using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class DraftPullRequestReceiptPersistenceValidator
{
    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "draft pr receipt persistence is reference only",
        "persisted draft pr receipt is witness evidence only",
        "persisted draft pr receipt does not grant downstream authority"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "draft pr receipt persistence is not pr creation",
        "draft pr receipt persistence is not ready-for-review",
        "draft pr receipt persistence is not reviewer request",
        "draft pr receipt persistence is not merge",
        "draft pr receipt persistence is not release",
        "draft pr receipt persistence is not deploy",
        "draft pr receipt persistence is not push execution",
        "draft pr receipt persistence is not commit execution",
        "draft pr receipt persistence is not source apply",
        "draft pr receipt persistence is not source authority",
        "draft pr receipt persistence is not approval",
        "draft pr receipt persistence is not policy satisfaction",
        "draft pr receipt persistence is not validation freshness",
        "draft pr receipt persistence is not patch freshness",
        "draft pr receipt persistence is not source state proof",
        "draft pr receipt persistence is not execution proof by itself",
        "draft pr receipt persistence is not merge readiness",
        "draft pr receipt persistence is not release readiness",
        "draft pr receipt persistence is not deployment readiness",
        "draft pr receipt persistence is not retry authority",
        "draft pr receipt persistence is not rollback authority",
        "draft pr receipt persistence is not recovery authority",
        "draft pr receipt persistence is not workflow continuation"
    ];

    private static readonly string[] RawPayloadMarkers =
    [
        "raw pr title",
        "raw pr body",
        "pull request title:",
        "pull request body:",
        "raw api request",
        "raw api response",
        "api request body",
        "api response body",
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
        "rollback now"
    ];

    public static DraftPullRequestReceiptPersistenceValidationResult ValidateRequest(
        PersistDraftPullRequestReceiptRequest? request)
    {
        if (request is null)
        {
            return Invalid(["DraftPullRequestReceiptPersistenceRequestRequired"], hasUnsafePayload: false);
        }

        var issues = new List<string>();
        var unsafePayload = false;

        AddScopeIssues(request.TenantId, "DraftPullRequestReceiptPersistenceTenantId", issues, ref unsafePayload);
        AddScopeIssues(request.ProjectId, "DraftPullRequestReceiptPersistenceProjectId", issues, ref unsafePayload);
        AddOperationIdIssues(request.OperationId, "DraftPullRequestReceiptPersistenceOperationId", issues);
        AddCorrelationIdIssues(request, request.CorrelationId, "DraftPullRequestReceiptPersistenceCorrelationId", issues, ref unsafePayload);

        if (request.AsOfUtc == default)
        {
            issues.Add("DraftPullRequestReceiptPersistenceAsOfUtcRequired");
        }

        if (request.Receipt is null)
        {
            issues.Add("DraftPullRequestReceiptPersistenceRecordRequired");
            return Result(issues, unsafePayload);
        }

        AddRecordIssues(request, request.Receipt, issues, ref unsafePayload);

        return Result(issues, unsafePayload);
    }

    public static DraftPullRequestReceiptPersistenceValidationResult ValidateRecord(
        DraftPullRequestReceiptPersistenceRecord? record)
    {
        if (record is null)
        {
            return Invalid(["DraftPullRequestReceiptPersistenceRecordRequired"], hasUnsafePayload: false);
        }

        var issues = new List<string>();
        var unsafePayload = false;
        AddRecordIssues(null, record, issues, ref unsafePayload);
        return Result(issues, unsafePayload);
    }

    private static void AddRecordIssues(
        PersistDraftPullRequestReceiptRequest? request,
        DraftPullRequestReceiptPersistenceRecord record,
        List<string> issues,
        ref bool unsafePayload)
    {
        AddScopeIssues(record.TenantId, "DraftPullRequestReceiptPersistenceRecordTenantId", issues, ref unsafePayload);
        AddScopeIssues(record.ProjectId, "DraftPullRequestReceiptPersistenceRecordProjectId", issues, ref unsafePayload);
        AddOperationIdIssues(record.OperationId, "DraftPullRequestReceiptPersistenceRecordOperationId", issues);
        AddCorrelationIdIssues(request, record.CorrelationId, "DraftPullRequestReceiptPersistenceRecordCorrelationId", issues, ref unsafePayload);

        AddRequiredReferenceIssues(record.ReceiptId, "DraftPullRequestReceiptPersistenceReceiptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.DraftPullRequestAttemptId, "DraftPullRequestReceiptPersistenceAttemptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.PushReceiptId, "DraftPullRequestReceiptPersistencePushReceiptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.PushAttemptId, "DraftPullRequestReceiptPersistencePushAttemptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.CommitReceiptId, "DraftPullRequestReceiptPersistenceCommitReceiptId", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.CommitAttemptId, "DraftPullRequestReceiptPersistenceCommitAttemptId", issues, ref unsafePayload);
        AddRequiredCommitHashIssues(record.CommitSha, "DraftPullRequestReceiptPersistenceCommitSha", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.RepositoryRef, "DraftPullRequestReceiptPersistenceRepositoryRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.ProviderRef, "DraftPullRequestReceiptPersistenceProviderRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.BaseBranchRef, "DraftPullRequestReceiptPersistenceBaseBranchRef", issues, ref unsafePayload);
        AddRequiredReferenceIssues(record.HeadBranchRef, "DraftPullRequestReceiptPersistenceHeadBranchRef", issues, ref unsafePayload);

        AddPullRequestReferenceIssues(record, issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.PullRequestTitleHash, "DraftPullRequestReceiptPersistenceTitleHash", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.PullRequestBodyHash, "DraftPullRequestReceiptPersistenceBodyHash", issues, ref unsafePayload);

        if (record.OutcomeKind == DraftPullRequestReceiptOutcomeKind.Unknown ||
            !Enum.IsDefined(record.OutcomeKind))
        {
            issues.Add("DraftPullRequestReceiptPersistenceOutcomeKindRequired");
        }

        AddObservedDraftStateIssues(record.ObservedDraftState, record.OutcomeKind, issues);
        AddOptionalSafeTextIssues(record.OutcomeReasonCode, "DraftPullRequestReceiptPersistenceOutcomeReasonCode", issues, ref unsafePayload);

        if (record.StartedAtUtc == default)
        {
            issues.Add("DraftPullRequestReceiptPersistenceStartedAtUtcRequired");
        }

        if (record.RecordedAtUtc == default)
        {
            issues.Add("DraftPullRequestReceiptPersistenceRecordedAtUtcRequired");
        }

        if (IsTerminal(record.OutcomeKind) && record.CompletedAtUtc is null)
        {
            issues.Add("DraftPullRequestReceiptPersistenceCompletedAtUtcRequired");
        }

        if (record.CompletedAtUtc is not null &&
            record.StartedAtUtc != default &&
            record.CompletedAtUtc.Value < record.StartedAtUtc)
        {
            issues.Add("DraftPullRequestReceiptPersistenceCompletedBeforeStarted");
        }

        AddRequiredSafeTextIssues(record.Source, "DraftPullRequestReceiptPersistenceSource", issues, ref unsafePayload);
        if (string.Equals(record.Source, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("DraftPullRequestReceiptPersistenceSourceUnknown");
        }

        if (record.IsRedacted)
        {
            AddRequiredSafeTextIssues(record.RedactionReason, "DraftPullRequestReceiptPersistenceRedactionReason", issues, ref unsafePayload);
        }
        else
        {
            AddOptionalSafeTextIssues(record.RedactionReason, "DraftPullRequestReceiptPersistenceRedactionReason", issues, ref unsafePayload);
        }

        AddOptionalReferenceIssues(record.RecordFingerprint, "DraftPullRequestReceiptPersistenceRecordFingerprint", issues, ref unsafePayload);

        if (request is not null)
        {
            AddBindingIssue(request.TenantId, record.TenantId, "DraftPullRequestReceiptPersistenceTenantMismatch", issues);
            AddBindingIssue(request.ProjectId, record.ProjectId, "DraftPullRequestReceiptPersistenceProjectMismatch", issues);
            AddBindingIssue(request.OperationId, record.OperationId, "DraftPullRequestReceiptPersistenceOperationMismatch", issues);
            AddBindingIssue(request.CorrelationId, record.CorrelationId, "DraftPullRequestReceiptPersistenceCorrelationMismatch", issues);
        }
    }

    private static void AddPullRequestReferenceIssues(
        DraftPullRequestReceiptPersistenceRecord record,
        List<string> issues,
        ref bool unsafePayload)
    {
        if (record.OutcomeKind == DraftPullRequestReceiptOutcomeKind.Succeeded)
        {
            AddRequiredReferenceIssues(record.PullRequestRef, "DraftPullRequestReceiptPersistencePullRequestRef", issues, ref unsafePayload);
            AddRequiredReferenceIssues(record.PullRequestNumberRef, "DraftPullRequestReceiptPersistencePullRequestNumberRef", issues, ref unsafePayload);
        }
        else if (IsNonSucceededTerminal(record.OutcomeKind) &&
                 HasAnyPullRequestReference(record))
        {
            issues.Add("DraftPullRequestReceiptPersistencePullRequestRefUnexpected");
        }

        AddOptionalReferenceIssues(record.PullRequestRef, "DraftPullRequestReceiptPersistencePullRequestRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.PullRequestNumberRef, "DraftPullRequestReceiptPersistencePullRequestNumberRef", issues, ref unsafePayload);
        AddOptionalReferenceIssues(record.PullRequestWebRef, "DraftPullRequestReceiptPersistencePullRequestWebRef", issues, ref unsafePayload);
    }

    private static void AddObservedDraftStateIssues(
        DraftPullRequestObservedState observedState,
        DraftPullRequestReceiptOutcomeKind outcomeKind,
        List<string> issues)
    {
        if (!Enum.IsDefined(observedState))
        {
            issues.Add("DraftPullRequestReceiptPersistenceObservedDraftStateInvalid");
            return;
        }

        if (outcomeKind == DraftPullRequestReceiptOutcomeKind.Succeeded &&
            observedState != DraftPullRequestObservedState.Draft)
        {
            issues.Add(observedState == DraftPullRequestObservedState.NotDraft
                ? "DraftPullRequestReceiptPersistenceObservedDraftStateNotDraft"
                : "DraftPullRequestReceiptPersistenceObservedDraftStateDraftRequired");
        }
    }

    private static bool HasAnyPullRequestReference(DraftPullRequestReceiptPersistenceRecord record) =>
        !string.IsNullOrWhiteSpace(record.PullRequestRef) ||
        !string.IsNullOrWhiteSpace(record.PullRequestNumberRef) ||
        !string.IsNullOrWhiteSpace(record.PullRequestWebRef);

    private static bool IsNonSucceededTerminal(DraftPullRequestReceiptOutcomeKind outcomeKind) =>
        outcomeKind is DraftPullRequestReceiptOutcomeKind.Failed or
            DraftPullRequestReceiptOutcomeKind.Interrupted or
            DraftPullRequestReceiptOutcomeKind.Cancelled;

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
        PersistDraftPullRequestReceiptRequest? request,
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
                SurfaceKind = OperationCorrelationSurfaceKind.PullRequestReceipt,
                SurfaceId = "draft-pr-receipt-persistence",
                ObservedAtUtc = DateTimeOffset.UnixEpoch,
                Source = "e04-validator"
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

    private static bool IsTerminal(DraftPullRequestReceiptOutcomeKind outcomeKind) =>
        outcomeKind is DraftPullRequestReceiptOutcomeKind.Succeeded or
            DraftPullRequestReceiptOutcomeKind.Failed or
            DraftPullRequestReceiptOutcomeKind.Interrupted or
            DraftPullRequestReceiptOutcomeKind.Cancelled;

    private static DraftPullRequestReceiptPersistenceValidationResult Invalid(
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

    private static DraftPullRequestReceiptPersistenceValidationResult Result(
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
