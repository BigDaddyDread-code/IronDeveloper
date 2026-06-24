namespace IronDev.Core.Governance;

public static class OperationStatusReadEnvelopeFactory
{
    public const string GenericNotFoundMessage = "Operation status was not found for the supplied scope.";

    public static OperationStatusReadEnvelope Success(
        OperationStatusReadContext context,
        OperationStatusSafeSummary safeSummary,
        OperationStatusPageEnvelopeSummary? pageSummary = null) =>
        Envelope(
            context,
            OperationStatusReadEnvelopeKind.Success,
            OperationStatusReadErrorCode.None,
            isValid: true,
            safeSummary,
            pageSummary,
            issues: []);

    public static OperationStatusReadEnvelope PageSuccess(
        OperationStatusReadContext context,
        OperationStatusPageEnvelopeSummary pageSummary) =>
        Envelope(
            context,
            OperationStatusReadEnvelopeKind.Success,
            OperationStatusReadErrorCode.None,
            isValid: true,
            safeSummary: null,
            pageSummary,
            issues: []);

    public static OperationStatusReadEnvelope NotFound(
        OperationStatusReadContext context,
        OperationStatusReadErrorCode errorCode = OperationStatusReadErrorCode.OperationStatusNotFound) =>
        Envelope(
            context,
            OperationStatusReadEnvelopeKind.NotFound,
            errorCode,
            isValid: true,
            safeSummary: null,
            pageSummary: null,
            issues:
            [
                Issue(errorCode, GenericNotFoundMessage, OperationStatusReadIssueSeverity.Info, isUserCorrectable: false)
            ]);

    public static OperationStatusReadEnvelope InvalidRequest(
        OperationStatusReadContext context,
        IReadOnlyList<OperationStatusReadIssue>? issues = null,
        OperationStatusReadErrorCode errorCode = OperationStatusReadErrorCode.OperationStatusRequestInvalid) =>
        Envelope(
            context,
            OperationStatusReadEnvelopeKind.InvalidRequest,
            errorCode,
            isValid: false,
            safeSummary: null,
            pageSummary: null,
            issues: NormalizeIssues(issues, errorCode, "Operation status read request was invalid."));

    public static OperationStatusReadEnvelope Ambiguous(
        OperationStatusReadContext context,
        OperationStatusReadErrorCode errorCode = OperationStatusReadErrorCode.OperationStatusInputAmbiguous) =>
        Envelope(
            context,
            OperationStatusReadEnvelopeKind.Ambiguous,
            errorCode,
            isValid: false,
            safeSummary: null,
            pageSummary: null,
            issues:
            [
                Issue(errorCode, "Operation status read input was ambiguous.", OperationStatusReadIssueSeverity.Error, isUserCorrectable: true)
            ]);

    public static OperationStatusReadEnvelope Unassessable(OperationStatusReadContext context) =>
        Envelope(
            context,
            OperationStatusReadEnvelopeKind.Unassessable,
            OperationStatusReadErrorCode.OperationStatusUnassessable,
            isValid: false,
            safeSummary: null,
            pageSummary: null,
            issues:
            [
                Issue(OperationStatusReadErrorCode.OperationStatusUnassessable, "Operation status read model state could not be safely assessed.", OperationStatusReadIssueSeverity.Warning, isUserCorrectable: false)
            ]);

    public static OperationStatusReadEnvelope Redacted(
        OperationStatusReadContext context,
        OperationStatusSafeSummary safeSummary) =>
        Envelope(
            context,
            OperationStatusReadEnvelopeKind.Redacted,
            OperationStatusReadErrorCode.OperationStatusRedacted,
            isValid: true,
            safeSummary,
            pageSummary: null,
            issues:
            [
                Issue(OperationStatusReadErrorCode.OperationStatusRedacted, "Operation status summary was redacted.", OperationStatusReadIssueSeverity.Info, isUserCorrectable: false)
            ]);

    public static OperationStatusReadEnvelope Error(
        OperationStatusReadContext context,
        IReadOnlyList<OperationStatusReadIssue>? issues = null) =>
        Envelope(
            context,
            OperationStatusReadEnvelopeKind.Error,
            OperationStatusReadErrorCode.OperationStatusReadModelError,
            isValid: false,
            safeSummary: null,
            pageSummary: null,
            issues: NormalizeIssues(issues, OperationStatusReadErrorCode.OperationStatusReadModelError, "Operation status read model error was safely redacted."));

    public static OperationStatusReadIssue Issue(
        OperationStatusReadErrorCode code,
        string message,
        OperationStatusReadIssueSeverity severity,
        string? field = null,
        bool isUserCorrectable = false) =>
        new()
        {
            Code = code,
            Message = message,
            Severity = severity,
            Field = field,
            IsUserCorrectable = isUserCorrectable
        };

    private static OperationStatusReadEnvelope Envelope(
        OperationStatusReadContext context,
        OperationStatusReadEnvelopeKind envelopeKind,
        OperationStatusReadErrorCode errorCode,
        bool isValid,
        OperationStatusSafeSummary? safeSummary,
        OperationStatusPageEnvelopeSummary? pageSummary,
        IReadOnlyList<OperationStatusReadIssue> issues) =>
        new()
        {
            IsValid = isValid,
            EnvelopeKind = envelopeKind,
            ErrorCode = errorCode,
            TenantId = context.TenantId,
            ProjectId = context.ProjectId,
            OperationId = context.OperationId,
            CorrelationId = context.CorrelationId,
            ReadKind = context.ReadKind,
            AsOfUtc = context.AsOfUtc,
            Source = context.Source,
            SafeSummary = safeSummary,
            PageSummary = pageSummary,
            Issues = issues,
            Warnings = OperationStatusReadEnvelopeValidator.RequiredWarnings,
            ForbiddenAuthorityImplications = OperationStatusReadEnvelopeValidator.ForbiddenAuthorityImplications
        };

    private static IReadOnlyList<OperationStatusReadIssue> NormalizeIssues(
        IReadOnlyList<OperationStatusReadIssue>? issues,
        OperationStatusReadErrorCode fallbackCode,
        string fallbackMessage) =>
        issues is { Count: > 0 }
            ? issues
            :
            [
                Issue(fallbackCode, fallbackMessage, OperationStatusReadIssueSeverity.Error, isUserCorrectable: true)
            ];
}
