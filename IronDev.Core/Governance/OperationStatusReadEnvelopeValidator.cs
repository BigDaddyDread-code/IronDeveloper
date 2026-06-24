using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class OperationStatusReadEnvelopeValidator
{
    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "not found is not denial",
        "error is not permission",
        "operation status read envelope is not authority"
    ];

    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "operation status read envelope is metadata only",
        "operation status read envelope is not operation lookup",
        "operation status read envelope is not tenant authorization",
        "operation status read envelope is not timeline assembly",
        "operation status read envelope is not status projection",
        "operation status read envelope is not missing evidence resolution",
        "operation status read envelope is not forbidden action resolution",
        "operation status read envelope is not receipt resolution",
        "operation status read envelope is not evidence resolution",
        "operation status read envelope is not validation freshness resolution",
        "operation status read envelope is not patch or base freshness resolution",
        "operation status read envelope is not worktree or head freshness resolution",
        "operation status read envelope is not interrupted run resolution",
        "operation status read envelope is not rollback or recovery resolution",
        "operation status read envelope is not pagination",
        "operation status read envelope is not approval",
        "operation status read envelope is not policy satisfaction",
        "operation status read envelope is not next safe action",
        "operation status read envelope is not source apply",
        "operation status read envelope is not rollback execution",
        "operation status read envelope is not retry permission",
        "operation status read envelope is not resume permission",
        "operation status read envelope is not commit",
        "operation status read envelope is not push",
        "operation status read envelope is not pull request creation",
        "operation status read envelope is not merge readiness",
        "operation status read envelope is not release readiness",
        "operation status read envelope is not deployment readiness",
        "operation status read envelope is not memory promotion",
        "operation status read envelope is not workflow continuation",
        "success envelope is not action allowed",
        "not found envelope is not denial",
        "invalid request envelope is not forbidden",
        "redacted envelope is not denied",
        "error envelope is not permission"
    ];

    public static OperationStatusReadEnvelopeValidationResult Validate(OperationStatusReadEnvelope? envelope)
    {
        if (envelope is null)
        {
            return Result(["OperationStatusReadEnvelopeRequired"]);
        }

        var issues = new List<string>();
        ValidateScope(envelope.TenantId, "OperationStatusReadTenantIdRequired", "OperationStatusReadTenantIdInvalid", issues);
        ValidateScope(envelope.ProjectId, "OperationStatusReadProjectIdRequired", "OperationStatusReadProjectIdInvalid", issues);
        ValidateReadKind(envelope.ReadKind, issues);
        ValidateEnvelopeKind(envelope.EnvelopeKind, issues);
        ValidateErrorCode(envelope.ErrorCode, issues);
        ValidateContextIds(envelope, issues);
        ValidateAsOf(envelope.AsOfUtc, issues);
        ValidateSource(envelope.Source, issues);
        ValidateSummary(envelope, issues);
        ValidatePageSummary(envelope.PageSummary, issues);
        ValidateIssues(envelope.Issues, issues);
        ValidateTextList(envelope.Warnings, "OperationStatusReadWarning", issues, requireRequiredWarnings: true);
        ValidateForbiddenAuthorityImplications(envelope.ForbiddenAuthorityImplications, issues);
        ValidateEnvelopeMapping(envelope, issues);

        return Result(issues);
    }

    private static void ValidateReadKind(
        OperationStatusReadKind readKind,
        ICollection<string> issues)
    {
        if (readKind == OperationStatusReadKind.Unknown || !Enum.IsDefined(readKind))
        {
            issues.Add("OperationStatusReadKindRequired");
        }
    }

    private static void ValidateEnvelopeKind(
        OperationStatusReadEnvelopeKind envelopeKind,
        ICollection<string> issues)
    {
        if (envelopeKind == OperationStatusReadEnvelopeKind.Unknown || !Enum.IsDefined(envelopeKind))
        {
            issues.Add("OperationStatusReadEnvelopeKindRequired");
        }
    }

    private static void ValidateErrorCode(
        OperationStatusReadErrorCode errorCode,
        ICollection<string> issues)
    {
        if (errorCode == OperationStatusReadErrorCode.Unknown || !Enum.IsDefined(errorCode))
        {
            issues.Add("OperationStatusReadErrorCodeRequired");
        }
    }

    private static void ValidateContextIds(
        OperationStatusReadEnvelope envelope,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(envelope.OperationId))
        {
            if (!AllowsMissingOperationId(envelope.ReadKind))
            {
                issues.Add("OperationStatusReadOperationIdRequired");
            }
        }
        else
        {
            var operationId = OperationIdentityValidator.ValidateOperationId(envelope.OperationId);
            foreach (var issue in operationId.Issues)
            {
                issues.Add($"OperationStatusReadOperationId:{issue}");
            }
        }

        if (string.IsNullOrWhiteSpace(envelope.CorrelationId))
        {
            if (!AllowsMissingOperationId(envelope.ReadKind))
            {
                issues.Add("OperationStatusReadCorrelationIdRequired");
            }
        }
        else
        {
            ValidateCorrelationId(envelope.CorrelationId, envelope.OperationId, issues);
        }
    }

    private static bool AllowsMissingOperationId(OperationStatusReadKind readKind) =>
        readKind is OperationStatusReadKind.StatusPage or OperationStatusReadKind.CursorPage;

    private static void ValidateAsOf(
        DateTimeOffset asOfUtc,
        ICollection<string> issues)
    {
        if (asOfUtc == default)
        {
            issues.Add("OperationStatusReadAsOfUtcRequired");
        }
    }

    private static void ValidateSource(
        string? source,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            issues.Add("OperationStatusReadSourceRequired");
            return;
        }

        if (IsUnsafeScopeText(source) || ContainsAuthorityGrantText(source))
        {
            issues.Add("OperationStatusReadSourceInvalid");
        }
    }

    private static void ValidateSummary(
        OperationStatusReadEnvelope envelope,
        ICollection<string> issues)
    {
        if (envelope.SafeSummary is null)
        {
            return;
        }

        var summary = envelope.SafeSummary;
        var operationId = OperationIdentityValidator.ValidateOperationId(summary.OperationId);
        foreach (var issue in operationId.Issues)
        {
            issues.Add($"OperationStatusSafeSummaryOperationId:{issue}");
        }

        ValidateCorrelationId(summary.CorrelationId, summary.OperationId, issues, "OperationStatusSafeSummaryCorrelationId");

        if (!string.IsNullOrWhiteSpace(envelope.OperationId) &&
            !Same(envelope.OperationId, summary.OperationId))
        {
            issues.Add("OperationStatusSafeSummaryOperationIdMismatch");
        }

        if (!string.IsNullOrWhiteSpace(envelope.CorrelationId) &&
            !Same(envelope.CorrelationId, summary.CorrelationId))
        {
            issues.Add("OperationStatusSafeSummaryCorrelationIdMismatch");
        }

        if (summary.ProjectedStatus == OperationProjectedStatusKind.Unknown ||
            !Enum.IsDefined(summary.ProjectedStatus))
        {
            issues.Add("OperationStatusSafeSummaryProjectedStatusRequired");
        }

        if (summary.CreatedAtUtc == default)
        {
            issues.Add("OperationStatusSafeSummaryCreatedAtRequired");
        }

        if (summary.UpdatedAtUtc == default)
        {
            issues.Add("OperationStatusSafeSummaryUpdatedAtRequired");
        }

        if (summary.LastEventAtUtc == default)
        {
            issues.Add("OperationStatusSafeSummaryLastEventAtRequired");
        }

        if (summary.TimelineEventCount < 0)
        {
            issues.Add("OperationStatusSafeSummaryTimelineEventCountInvalid");
        }

        if (summary.DiagnosticStatusSummary is null)
        {
            issues.Add("OperationStatusSafeSummaryDiagnosticStatusRequired");
        }

        if (summary.IsRedacted)
        {
            if (string.IsNullOrWhiteSpace(summary.RedactionReason))
            {
                issues.Add("OperationStatusSafeSummaryRedactionReasonRequired");
            }
            else if (IsUnsafeText(summary.RedactionReason) ||
                     ContainsAuthorityGrantText(summary.RedactionReason))
            {
                issues.Add("OperationStatusSafeSummaryRedactionReasonInvalid");
            }
        }

        ValidateSafeSummaryText(summary.RedactionReason, "OperationStatusSafeSummaryRedactionReason", issues);
    }

    private static void ValidatePageSummary(
        OperationStatusPageEnvelopeSummary? summary,
        ICollection<string> issues)
    {
        if (summary is null)
        {
            return;
        }

        if (summary.PageSize < 0)
        {
            issues.Add("OperationStatusPageEnvelopePageSizeInvalid");
        }

        if (summary.ItemCount < 0)
        {
            issues.Add("OperationStatusPageEnvelopeItemCountInvalid");
        }

        if (summary.MatchedCount < 0)
        {
            issues.Add("OperationStatusPageEnvelopeMatchedCountInvalid");
        }

        if (summary.ScannedCount < 0)
        {
            issues.Add("OperationStatusPageEnvelopeScannedCountInvalid");
        }

        if (summary.PageSize > 0 && summary.ItemCount > summary.PageSize)
        {
            issues.Add("OperationStatusPageEnvelopeItemCountExceedsPageSize");
        }

        if (summary.ItemCount > summary.MatchedCount)
        {
            issues.Add("OperationStatusPageEnvelopeItemCountExceedsMatchedCount");
        }

        if (summary.MatchedCount > summary.ScannedCount)
        {
            issues.Add("OperationStatusPageEnvelopeMatchedCountExceedsScannedCount");
        }

        if (summary.HasMore && !summary.HasNextCursor)
        {
            issues.Add("OperationStatusPageEnvelopeHasMoreRequiresCursor");
        }

        if (summary.HasNextCursor && string.IsNullOrWhiteSpace(summary.CursorState))
        {
            issues.Add("OperationStatusPageEnvelopeCursorStateRequired");
        }

        ValidateSafeSummaryText(summary.CursorState, "OperationStatusPageEnvelopeCursorState", issues);
    }

    private static void ValidateIssues(
        IReadOnlyList<OperationStatusReadIssue>? readIssues,
        ICollection<string> issues)
    {
        foreach (var issue in readIssues ?? [])
        {
            if (issue.Code == OperationStatusReadErrorCode.Unknown ||
                !Enum.IsDefined(issue.Code))
            {
                issues.Add("OperationStatusReadIssueCodeRequired");
            }

            if (issue.Severity == OperationStatusReadIssueSeverity.Unknown ||
                !Enum.IsDefined(issue.Severity))
            {
                issues.Add("OperationStatusReadIssueSeverityRequired");
            }

            if (string.IsNullOrWhiteSpace(issue.Message))
            {
                issues.Add("OperationStatusReadIssueMessageRequired");
            }
            else if (IsUnsafeText(issue.Message) ||
                     ContainsAuthorityGrantText(issue.Message) ||
                     ContainsTenantLeakText(issue.Message))
            {
                issues.Add("OperationStatusReadIssueMessageUnsafe");
            }

            ValidateSafeSummaryText(issue.Field, "OperationStatusReadIssueField", issues);
        }
    }

    private static void ValidateTextList(
        IReadOnlyList<string>? values,
        string issuePrefix,
        ICollection<string> issues,
        bool requireRequiredWarnings)
    {
        var list = values ?? [];
        foreach (var value in list)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                IsUnsafeText(value) ||
                ContainsAuthorityGrantText(value) ||
                ContainsTenantLeakText(value))
            {
                issues.Add($"{issuePrefix}Unsafe");
            }
        }

        if (requireRequiredWarnings)
        {
            foreach (var required in RequiredWarnings)
            {
                if (!list.Any(value => Same(value, required)))
                {
                    issues.Add($"OperationStatusReadWarningMissing:{required}");
                }
            }
        }
    }

    private static void ValidateForbiddenAuthorityImplications(
        IReadOnlyList<string>? values,
        ICollection<string> issues)
    {
        var list = values ?? [];
        foreach (var implication in list)
        {
            if (string.IsNullOrWhiteSpace(implication) || IsUnsafeText(implication))
            {
                issues.Add("OperationStatusReadForbiddenAuthorityImplicationUnsafe");
            }
        }

        foreach (var required in ForbiddenAuthorityImplications)
        {
            if (!list.Any(value => Same(value, required)))
            {
                issues.Add($"OperationStatusReadForbiddenAuthorityImplicationMissing:{required}");
            }
        }
    }

    private static void ValidateEnvelopeMapping(
        OperationStatusReadEnvelope envelope,
        ICollection<string> issues)
    {
        switch (envelope.EnvelopeKind)
        {
            case OperationStatusReadEnvelopeKind.Success:
                if (envelope.ErrorCode != OperationStatusReadErrorCode.None)
                {
                    issues.Add("OperationStatusReadSuccessRequiresNoneErrorCode");
                }

                if (envelope.SafeSummary is null && envelope.PageSummary is null)
                {
                    issues.Add("OperationStatusReadSuccessRequiresSummary");
                }

                break;

            case OperationStatusReadEnvelopeKind.NotFound:
                if (envelope.ErrorCode is not (OperationStatusReadErrorCode.OperationStatusNotFound or OperationStatusReadErrorCode.OperationStatusPageNotFound))
                {
                    issues.Add("OperationStatusReadNotFoundErrorCodeMismatch");
                }

                ValidateGenericNotFound(envelope, issues);
                break;

            case OperationStatusReadEnvelopeKind.InvalidRequest:
                if (envelope.ErrorCode is not (OperationStatusReadErrorCode.OperationStatusRequestInvalid or OperationStatusReadErrorCode.OperationStatusScopeInvalid or OperationStatusReadErrorCode.OperationStatusCursorInvalid))
                {
                    issues.Add("OperationStatusReadInvalidRequestErrorCodeMismatch");
                }

                break;

            case OperationStatusReadEnvelopeKind.Ambiguous:
                if (envelope.ErrorCode is not (OperationStatusReadErrorCode.OperationStatusInputAmbiguous or OperationStatusReadErrorCode.OperationStatusCursorAmbiguous))
                {
                    issues.Add("OperationStatusReadAmbiguousErrorCodeMismatch");
                }

                if (envelope.SafeSummary is not null)
                {
                    issues.Add("OperationStatusReadAmbiguousCannotSelectSafeSummary");
                }

                break;

            case OperationStatusReadEnvelopeKind.Unassessable:
                if (envelope.ErrorCode != OperationStatusReadErrorCode.OperationStatusUnassessable)
                {
                    issues.Add("OperationStatusReadUnassessableErrorCodeMismatch");
                }

                break;

            case OperationStatusReadEnvelopeKind.Redacted:
                if (envelope.ErrorCode != OperationStatusReadErrorCode.OperationStatusRedacted)
                {
                    issues.Add("OperationStatusReadRedactedErrorCodeMismatch");
                }

                if (envelope.SafeSummary?.IsRedacted != true)
                {
                    issues.Add("OperationStatusReadRedactedRequiresRedactedSummary");
                }

                break;

            case OperationStatusReadEnvelopeKind.Error:
                if (envelope.ErrorCode != OperationStatusReadErrorCode.OperationStatusReadModelError)
                {
                    issues.Add("OperationStatusReadErrorCodeMismatch");
                }

                break;
        }
    }

    private static void ValidateGenericNotFound(
        OperationStatusReadEnvelope envelope,
        ICollection<string> issues)
    {
        foreach (var issue in envelope.Issues ?? [])
        {
            if (!Same(issue.Message, OperationStatusReadEnvelopeFactory.GenericNotFoundMessage))
            {
                issues.Add("OperationStatusReadNotFoundMessageMustBeGeneric");
            }
        }
    }

    private static void ValidateScope(
        string? value,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (IsUnsafeScopeText(value) || ContainsAuthorityGrantText(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void ValidateCorrelationId(
        string? correlationId,
        string? operationId,
        ICollection<string> issues,
        string issuePrefix = "OperationStatusReadCorrelationId")
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            issues.Add($"{issuePrefix}Required");
            return;
        }

        if (correlationId.Any(char.IsWhiteSpace) ||
            IsUnsafeText(correlationId) ||
            !CanonicalCorrelationIdPattern().IsMatch(correlationId) ||
            (!string.IsNullOrWhiteSpace(operationId) && Same(operationId, correlationId)))
        {
            issues.Add($"{issuePrefix}Invalid");
        }
    }

    private static void ValidateSafeSummaryText(
        string? value,
        string issuePrefix,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (IsUnsafeText(value) ||
            ContainsAuthorityGrantText(value) ||
            ContainsTenantLeakText(value))
        {
            issues.Add($"{issuePrefix}Unsafe");
        }
    }

    private static bool IsUnsafeScopeText(string value) =>
        value.Any(char.IsControl) ||
        value.Any(char.IsWhiteSpace) ||
        value.Length > 128 ||
        IsUrl(value) ||
        LooksLikePath(value) ||
        ContainsRawPayloadText(value);

    private static bool IsUnsafeText(string value) =>
        value.Any(char.IsControl) ||
        value.Length > 240 ||
        LooksLikePath(value) ||
        IsUrl(value) ||
        ContainsRawPayloadText(value);

    private static bool ContainsRawPayloadText(string value)
    {
        var lower = value.ToLowerInvariant();
        return lower.Contains("stack trace") ||
            lower.Contains("exception") ||
            lower.Contains("select ") ||
            lower.Contains("insert ") ||
            lower.Contains("update ") ||
            lower.Contains("delete ") ||
            lower.Contains("connection string") ||
            lower.Contains("token") ||
            lower.Contains("secret") ||
            lower.Contains("raw payload") ||
            lower.Contains("raw request") ||
            lower.Contains("raw response") ||
            lower.Contains("raw patch") ||
            lower.Contains("raw diff") ||
            lower.Contains("source content") ||
            lower.Contains("chain-of-thought") ||
            lower.Contains("prompt text") ||
            lower.Contains("{") ||
            lower.Contains("}");
    }

    private static bool ContainsAuthorityGrantText(string value)
    {
        var lower = value.ToLowerInvariant();
        return lower.Contains("approved") ||
            lower.Contains("policy satisfied") ||
            lower.Contains("authorized") ||
            lower.Contains("authority granted") ||
            lower.Contains("can apply") ||
            lower.Contains("can commit") ||
            lower.Contains("can push") ||
            lower.Contains("can create pull request") ||
            lower.Contains("can rollback") ||
            lower.Contains("can recover") ||
            lower.Contains("can retry") ||
            lower.Contains("can resume") ||
            lower.Contains("can continue") ||
            lower.Contains("next safe action") ||
            (lower.Contains("action allowed") && !lower.Contains("not action allowed"));
    }

    private static bool ContainsTenantLeakText(string value)
    {
        var lower = value.ToLowerInvariant();
        return lower.Contains("another tenant") ||
            lower.Contains("another project") ||
            lower.Contains("wrong tenant") ||
            lower.Contains("found outside scope") ||
            lower.Contains("exists but") ||
            lower.Contains("not visible") ||
            lower.Contains("foreign tenant") ||
            lower.Contains("foreign project") ||
            lower.Contains("project mismatch revealed");
    }

    private static bool LooksLikePath(string value) =>
        WindowsPathPattern().IsMatch(value) ||
        UnixPathPattern().IsMatch(value) ||
        value.Contains(".cs:", StringComparison.OrdinalIgnoreCase) ||
        value.Contains(".sql:", StringComparison.OrdinalIgnoreCase);

    private static bool IsUrl(string value) =>
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static OperationStatusReadEnvelopeValidationResult Result(IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = issues.Count == 0,
            Issues = issues.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = RequiredWarnings,
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    [GeneratedRegex("^corr_([0-9a-f]{16,64}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$")]
    private static partial Regex CanonicalCorrelationIdPattern();

    [GeneratedRegex("^[A-Za-z]:[\\\\/]")]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex("(^|\\s)/(var|tmp|home|users|etc|mnt|root|workspace|source)/", RegexOptions.IgnoreCase)]
    private static partial Regex UnixPathPattern();
}
