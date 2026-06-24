using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class OperationStatusAuthorityWarningFormatterValidator
{
    public const string RequiredBoundary = "Warning only. It grants no authority, denies no authority, and performs no workflow action.";
    public const int MaxLineCount = 10;
    public const int MaxTextLength = 180;

    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "authority warning formatting is not authority",
        "warning text does not grant or deny permission",
        "warning text does not execute workflow"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "warning text is display only",
        "warning text is not approval",
        "warning text is not policy satisfaction",
        "warning text is not source apply",
        "warning text is not rollback execution",
        "warning text is not recovery execution",
        "warning text is not retry permission",
        "warning text is not commit",
        "warning text is not push",
        "warning text is not pull request creation",
        "warning text is not merge",
        "warning text is not release",
        "warning text is not deployment",
        "warning text is not memory promotion",
        "warning text is not workflow continuation",
        "warning text is not workflow step selection"
    ];

    private static readonly string[] ForbiddenRenderedPhrases =
    [
        "approved",
        "authorized",
        "allowed",
        "denied",
        "forbidden by policy",
        "permission granted",
        "permission denied",
        "safe to execute",
        "ready to apply",
        "ready to commit",
        "ready to push",
        "ready to merge",
        "ready to release",
        "ready to deploy",
        "retry now",
        "resume now",
        "recover now",
        "rollback now",
        "continue workflow",
        "execute",
        "run this",
        "click",
        "press",
        "you can",
        "system can",
        "agent can",
        "must proceed",
        "go ahead"
    ];

    private static readonly string[] UnsafeRenderedMarkers =
    [
        "stack trace",
        "exception",
        "select ",
        "insert ",
        "update ",
        "delete ",
        "drop ",
        "powershell",
        "cmd.exe",
        "bash",
        "git ",
        "gh ",
        "dotnet ",
        "npm ",
        "raw payload",
        "raw request",
        "raw response",
        "authorization:",
        "bearer ",
        "private key",
        "prompt:",
        "diff --git",
        "@@"
    ];

    public static OperationStatusAuthorityWarningFormatterValidationResult ValidateRequest(
        OperationStatusAuthorityWarningFormatterRequest? request)
    {
        if (request is null)
        {
            return Invalid(["OperationStatusAuthorityWarningFormatterRequestRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(request.TenantId, "OperationStatusAuthorityWarningTenantId", issues);
        AddScopeIssues(request.ProjectId, "OperationStatusAuthorityWarningProjectId", issues);
        AddOperationIdIssues(request.OperationId, issues);
        AddCorrelationIdIssues(request.CorrelationId, request.OperationId, issues);
        AddSafeTextIssues(request.Source, "OperationStatusAuthorityWarningSource", issues);

        if (request.AsOfUtc == default)
        {
            issues.Add("OperationStatusAuthorityWarningAsOfUtcRequired");
        }

        if (request.ReadEnvelope is null &&
            request.NextSafeActionFormatterResult is null &&
            request.WarningFacts is null)
        {
            issues.Add("OperationStatusAuthorityWarningInputRequired");
        }

        if (request.ReadEnvelope is not null)
        {
            AddEnvelopeBindingIssues(request, request.ReadEnvelope, issues);
        }

        if (request.NextSafeActionFormatterResult is not null)
        {
            AddNextSafeActionBindingIssues(request, request.NextSafeActionFormatterResult, issues);
        }

        if (request.WarningFacts is not null)
        {
            AddWarningFactsIssues(request, request.WarningFacts, issues);
        }

        return Result(issues);
    }

    public static OperationStatusAuthorityWarningFormatterValidationResult ValidateResult(
        OperationStatusAuthorityWarningFormatterResult? result)
    {
        if (result is null)
        {
            return Invalid(["OperationStatusAuthorityWarningFormatterResultRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(result.TenantId, "OperationStatusAuthorityWarningTenantId", issues);
        AddScopeIssues(result.ProjectId, "OperationStatusAuthorityWarningProjectId", issues);

        if (!string.IsNullOrWhiteSpace(result.OperationId))
        {
            AddOperationIdIssues(result.OperationId, issues);
        }

        AddCorrelationIdIssues(result.CorrelationId, result.OperationId, issues);

        if (result.AsOfUtc == default)
        {
            issues.Add("OperationStatusAuthorityWarningAsOfUtcRequired");
        }

        if (result.FormatterStatus == OperationStatusAuthorityWarningFormatterStatus.Unknown ||
            !Enum.IsDefined(result.FormatterStatus))
        {
            issues.Add("OperationStatusAuthorityWarningFormatterStatusRequired");
        }

        if (result.Lines.Count > MaxLineCount)
        {
            issues.Add("OperationStatusAuthorityWarningLineCountExceeded");
        }

        foreach (var line in result.Lines)
        {
            AddLineIssues(line, issues);
        }

        AddRequiredListIssues(result.Warnings, RequiredWarnings, "OperationStatusAuthorityWarningRequiredWarningMissing", issues);
        AddRequiredListIssues(
            result.ForbiddenAuthorityImplications,
            RequiredForbiddenAuthorityImplications,
            "OperationStatusAuthorityWarningForbiddenAuthorityImplicationMissing",
            issues);

        return Result(issues);
    }

    private static void AddEnvelopeBindingIssues(
        OperationStatusAuthorityWarningFormatterRequest request,
        OperationStatusReadEnvelope envelope,
        List<string> issues)
    {
        if (!Same(request.TenantId, envelope.TenantId))
        {
            issues.Add("OperationStatusAuthorityWarningEnvelopeTenantMismatch");
        }

        if (!Same(request.ProjectId, envelope.ProjectId))
        {
            issues.Add("OperationStatusAuthorityWarningEnvelopeProjectMismatch");
        }

        if (!string.IsNullOrWhiteSpace(envelope.OperationId) &&
            !Same(request.OperationId, envelope.OperationId))
        {
            issues.Add("OperationStatusAuthorityWarningEnvelopeOperationMismatch");
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId) &&
            !string.IsNullOrWhiteSpace(envelope.CorrelationId) &&
            !Same(request.CorrelationId, envelope.CorrelationId))
        {
            issues.Add("OperationStatusAuthorityWarningEnvelopeCorrelationMismatch");
        }
    }

    private static void AddNextSafeActionBindingIssues(
        OperationStatusAuthorityWarningFormatterRequest request,
        OperationStatusNextSafeActionFormatterResult result,
        List<string> issues)
    {
        if (!Same(request.TenantId, result.TenantId))
        {
            issues.Add("OperationStatusAuthorityWarningNextSafeActionTenantMismatch");
        }

        if (!Same(request.ProjectId, result.ProjectId))
        {
            issues.Add("OperationStatusAuthorityWarningNextSafeActionProjectMismatch");
        }

        if (!Same(request.OperationId, result.OperationId))
        {
            issues.Add("OperationStatusAuthorityWarningNextSafeActionOperationMismatch");
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId) &&
            !string.IsNullOrWhiteSpace(result.CorrelationId) &&
            !Same(request.CorrelationId, result.CorrelationId))
        {
            issues.Add("OperationStatusAuthorityWarningNextSafeActionCorrelationMismatch");
        }
    }

    private static void AddWarningFactsIssues(
        OperationStatusAuthorityWarningFormatterRequest request,
        OperationStatusAuthorityWarningFacts facts,
        List<string> issues)
    {
        AddScopeIssues(facts.TenantId, "OperationStatusAuthorityWarningFactsTenantId", issues);
        AddScopeIssues(facts.ProjectId, "OperationStatusAuthorityWarningFactsProjectId", issues);
        AddOperationIdIssues(facts.OperationId, "OperationStatusAuthorityWarningFactsOperationId", issues);
        AddCorrelationIdIssues(facts.CorrelationId, facts.OperationId, issues, "OperationStatusAuthorityWarningFactsCorrelationId");
        AddSafeTextIssues(facts.Source, "OperationStatusAuthorityWarningFactsSource", issues);

        if (facts.RecordedAtUtc == default)
        {
            issues.Add("OperationStatusAuthorityWarningFactsRecordedAtUtcRequired");
        }

        if (!Same(request.TenantId, facts.TenantId))
        {
            issues.Add("OperationStatusAuthorityWarningFactsTenantMismatch");
        }

        if (!Same(request.ProjectId, facts.ProjectId))
        {
            issues.Add("OperationStatusAuthorityWarningFactsProjectMismatch");
        }

        if (!Same(request.OperationId, facts.OperationId))
        {
            issues.Add("OperationStatusAuthorityWarningFactsOperationMismatch");
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId) &&
            !string.IsNullOrWhiteSpace(facts.CorrelationId) &&
            !Same(request.CorrelationId, facts.CorrelationId))
        {
            issues.Add("OperationStatusAuthorityWarningFactsCorrelationMismatch");
        }
    }

    private static void AddLineIssues(OperationStatusAuthorityWarningLine line, List<string> issues)
    {
        if (line.WarningKind == OperationStatusAuthorityWarningKind.Unknown ||
            !Enum.IsDefined(line.WarningKind))
        {
            issues.Add("OperationStatusAuthorityWarningKindRequired");
        }

        if (line.Severity == OperationStatusAuthorityWarningSeverity.Unknown ||
            !Enum.IsDefined(line.Severity))
        {
            issues.Add("OperationStatusAuthorityWarningSeverityRequired");
        }

        AddRenderedTextIssues(line.Title, "OperationStatusAuthorityWarningTitle", issues);
        AddRenderedTextIssues(line.Detail, "OperationStatusAuthorityWarningDetail", issues);
        AddSafeTextIssues(line.Source, "OperationStatusAuthorityWarningLineSource", issues);

        if (!string.Equals(line.Boundary, RequiredBoundary, StringComparison.Ordinal))
        {
            issues.Add("OperationStatusAuthorityWarningBoundaryRequired");
        }
    }

    private static void AddScopeIssues(string? value, string prefix, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{prefix}Required");
            return;
        }

        if (ContainsUnsafeText(value) ||
            !ScopeIdPattern().IsMatch(value))
        {
            issues.Add($"{prefix}Invalid");
        }
    }

    private static void AddOperationIdIssues(string? operationId, List<string> issues)
    {
        AddOperationIdIssues(operationId, "OperationStatusAuthorityWarningOperationId", issues);
    }

    private static void AddOperationIdIssues(string? operationId, string prefix, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            issues.Add($"{prefix}Required");
            return;
        }

        var result = OperationIdentityValidator.ValidateOperationId(operationId);
        foreach (var issue in result.Issues)
        {
            issues.Add($"{prefix}Invalid");
        }
    }

    private static void AddCorrelationIdIssues(
        string? correlationId,
        string? operationId,
        List<string> issues,
        string prefix = "OperationStatusAuthorityWarningCorrelationId")
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return;
        }

        if (ContainsUnsafeText(correlationId) ||
            string.Equals(correlationId, operationId, StringComparison.OrdinalIgnoreCase) ||
            !CanonicalCorrelationIdPattern().IsMatch(correlationId))
        {
            issues.Add($"{prefix}Invalid");
        }
    }

    private static void AddSafeTextIssues(string? text, string prefix, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            issues.Add($"{prefix}Required");
            return;
        }

        if (text.Length > MaxTextLength ||
            ContainsUnsafeText(text))
        {
            issues.Add($"{prefix}Invalid");
        }
    }

    private static void AddRenderedTextIssues(string? text, string prefix, List<string> issues)
    {
        AddSafeTextIssues(text, prefix, issues);

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (ForbiddenRenderedPhrases.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
            UnsafeRenderedMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add($"{prefix}Unsafe");
        }
    }

    private static void AddRequiredListIssues(
        IReadOnlyList<string> values,
        IReadOnlyList<string> requiredValues,
        string issue,
        List<string> issues)
    {
        foreach (var required in requiredValues)
        {
            if (!values.Any(value => string.Equals(value, required, StringComparison.Ordinal)))
            {
                issues.Add(issue);
            }
        }
    }

    private static OperationStatusAuthorityWarningFormatterValidationResult Invalid(IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            Issues = issues,
            Warnings = RequiredWarnings,
            ForbiddenAuthorityImplications = RequiredForbiddenAuthorityImplications
        };

    private static OperationStatusAuthorityWarningFormatterValidationResult Result(IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = issues.Count == 0,
            Issues = issues,
            Warnings = RequiredWarnings,
            ForbiddenAuthorityImplications = RequiredForbiddenAuthorityImplications
        };

    private static bool ContainsUnsafeText(string value) =>
        value.Any(char.IsControl) ||
        value.Contains("://", StringComparison.Ordinal) ||
        value.Contains("..", StringComparison.Ordinal) ||
        value.Contains('\\', StringComparison.Ordinal) ||
        value.Contains('<', StringComparison.Ordinal) ||
        value.Contains('>', StringComparison.Ordinal) ||
        value.Contains('|', StringComparison.Ordinal);

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[a-z0-9][a-z0-9_.-]{1,80}$", RegexOptions.IgnoreCase)]
    private static partial Regex ScopeIdPattern();

    [GeneratedRegex("^corr_[a-z0-9]{16,64}$", RegexOptions.IgnoreCase)]
    private static partial Regex CanonicalCorrelationIdPattern();
}
