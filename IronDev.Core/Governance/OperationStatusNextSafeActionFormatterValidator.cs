using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class OperationStatusNextSafeActionFormatterValidator
{
    public const string RequiredAuthorityBoundary = "Display only. This does not grant authority or execute workflow.";
    public const int MaxLineCount = 8;
    public const int MaxTextLength = 180;

    public static readonly IReadOnlyList<string> RequiredWarnings =
    [
        "formatted guidance is not authority",
        "formatted guidance is not next safe action authority",
        "formatted guidance does not execute workflow"
    ];

    public static readonly IReadOnlyList<string> RequiredForbiddenAuthorityImplications =
    [
        "formatted guidance is display only",
        "formatted guidance is not approval",
        "formatted guidance is not policy satisfaction",
        "formatted guidance is not source apply",
        "formatted guidance is not rollback",
        "formatted guidance is not commit",
        "formatted guidance is not push",
        "formatted guidance is not pull request creation",
        "formatted guidance is not merge",
        "formatted guidance is not release",
        "formatted guidance is not deployment",
        "formatted guidance is not memory promotion",
        "formatted guidance is not workflow continuation"
    ];

    private static readonly string[] ForbiddenRenderedPhrases =
    [
        "approved",
        "authorized",
        "allowed",
        "permission granted",
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
        "stack trace",
        "exception",
        "connection string",
        "authorization:",
        "bearer ",
        "private key",
        "prompt:",
        "diff --git",
        "@@"
    ];

    public static OperationStatusNextSafeActionFormatterValidationResult ValidateRequest(
        OperationStatusNextSafeActionFormatterRequest? request)
    {
        if (request is null)
        {
            return Invalid(["OperationStatusNextSafeActionFormatterRequestRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(request.TenantId, "OperationStatusNextSafeActionTenantId", issues);
        AddScopeIssues(request.ProjectId, "OperationStatusNextSafeActionProjectId", issues);
        AddOperationIdIssues(request.OperationId, issues);
        AddCorrelationIdIssues(request.CorrelationId, request.OperationId, issues);
        AddSafeTextIssues(request.Source, "OperationStatusNextSafeActionSource", issues);

        if (request.AsOfUtc == default)
        {
            issues.Add("OperationStatusNextSafeActionAsOfUtcRequired");
        }

        if (request.ReadEnvelope is null && request.DiagnosticFacts is null)
        {
            issues.Add("OperationStatusNextSafeActionInputRequired");
        }

        if (request.ReadEnvelope is not null)
        {
            AddEnvelopeBindingIssues(request, request.ReadEnvelope, issues);
        }

        if (request.DiagnosticFacts is not null)
        {
            AddDiagnosticFactsIssues(request, request.DiagnosticFacts, issues);
        }

        return Result(issues);
    }

    public static OperationStatusNextSafeActionFormatterValidationResult ValidateResult(
        OperationStatusNextSafeActionFormatterResult? result)
    {
        if (result is null)
        {
            return Invalid(["OperationStatusNextSafeActionFormatterResultRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(result.TenantId, "OperationStatusNextSafeActionTenantId", issues);
        AddScopeIssues(result.ProjectId, "OperationStatusNextSafeActionProjectId", issues);

        if (!string.IsNullOrWhiteSpace(result.OperationId))
        {
            AddOperationIdIssues(result.OperationId, issues);
        }

        AddCorrelationIdIssues(result.CorrelationId, result.OperationId, issues);

        if (result.AsOfUtc == default)
        {
            issues.Add("OperationStatusNextSafeActionAsOfUtcRequired");
        }

        if (result.FormatterStatus == OperationStatusNextSafeActionFormatterStatus.Unknown ||
            !Enum.IsDefined(result.FormatterStatus))
        {
            issues.Add("OperationStatusNextSafeActionFormatterStatusRequired");
        }

        if (result.Lines.Count > MaxLineCount)
        {
            issues.Add("OperationStatusNextSafeActionLineCountExceeded");
        }

        foreach (var line in result.Lines)
        {
            AddLineIssues(line, issues);
        }

        AddRequiredListIssues(result.Warnings, RequiredWarnings, "OperationStatusNextSafeActionWarningMissing", issues);
        AddRequiredListIssues(
            result.ForbiddenAuthorityImplications,
            RequiredForbiddenAuthorityImplications,
            "OperationStatusNextSafeActionForbiddenAuthorityImplicationMissing",
            issues);

        return Result(issues);
    }

    private static void AddEnvelopeBindingIssues(
        OperationStatusNextSafeActionFormatterRequest request,
        OperationStatusReadEnvelope envelope,
        List<string> issues)
    {
        if (!Same(request.TenantId, envelope.TenantId))
        {
            issues.Add("OperationStatusNextSafeActionEnvelopeTenantMismatch");
        }

        if (!Same(request.ProjectId, envelope.ProjectId))
        {
            issues.Add("OperationStatusNextSafeActionEnvelopeProjectMismatch");
        }

        if (!string.IsNullOrWhiteSpace(envelope.OperationId) &&
            !Same(request.OperationId, envelope.OperationId))
        {
            issues.Add("OperationStatusNextSafeActionEnvelopeOperationMismatch");
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId) &&
            !string.IsNullOrWhiteSpace(envelope.CorrelationId) &&
            !Same(request.CorrelationId, envelope.CorrelationId))
        {
            issues.Add("OperationStatusNextSafeActionEnvelopeCorrelationMismatch");
        }
    }

    private static void AddDiagnosticFactsIssues(
        OperationStatusNextSafeActionFormatterRequest request,
        OperationStatusNextSafeActionDiagnosticFacts facts,
        List<string> issues)
    {
        AddScopeIssues(facts.TenantId, "OperationStatusNextSafeActionFactsTenantId", issues);
        AddScopeIssues(facts.ProjectId, "OperationStatusNextSafeActionFactsProjectId", issues);
        AddOperationIdIssues(facts.OperationId, "OperationStatusNextSafeActionFactsOperationId", issues);
        AddCorrelationIdIssues(facts.CorrelationId, facts.OperationId, issues, "OperationStatusNextSafeActionFactsCorrelationId");
        AddSafeTextIssues(facts.Source, "OperationStatusNextSafeActionFactsSource", issues);

        if (facts.RecordedAtUtc == default)
        {
            issues.Add("OperationStatusNextSafeActionFactsRecordedAtUtcRequired");
        }

        if (!Same(request.TenantId, facts.TenantId))
        {
            issues.Add("OperationStatusNextSafeActionFactsTenantMismatch");
        }

        if (!Same(request.ProjectId, facts.ProjectId))
        {
            issues.Add("OperationStatusNextSafeActionFactsProjectMismatch");
        }

        if (!Same(request.OperationId, facts.OperationId))
        {
            issues.Add("OperationStatusNextSafeActionFactsOperationMismatch");
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId) &&
            !string.IsNullOrWhiteSpace(facts.CorrelationId) &&
            !Same(request.CorrelationId, facts.CorrelationId))
        {
            issues.Add("OperationStatusNextSafeActionFactsCorrelationMismatch");
        }
    }

    private static void AddLineIssues(OperationStatusNextSafeActionLine line, List<string> issues)
    {
        if (line.DisplayKind == OperationStatusNextSafeActionDisplayKind.Unknown ||
            !Enum.IsDefined(line.DisplayKind))
        {
            issues.Add("OperationStatusNextSafeActionDisplayKindRequired");
        }

        if (line.Severity == OperationStatusNextSafeActionSeverity.Unknown ||
            !Enum.IsDefined(line.Severity))
        {
            issues.Add("OperationStatusNextSafeActionSeverityRequired");
        }

        AddRenderedTextIssues(line.Title, "OperationStatusNextSafeActionTitle", issues);
        AddRenderedTextIssues(line.Detail, "OperationStatusNextSafeActionDetail", issues);
        AddRenderedTextIssues(line.Rationale, "OperationStatusNextSafeActionRationale", issues);
        AddSafeTextIssues(line.Source, "OperationStatusNextSafeActionLineSource", issues);

        if (!string.Equals(line.AuthorityBoundary, RequiredAuthorityBoundary, StringComparison.Ordinal))
        {
            issues.Add("OperationStatusNextSafeActionAuthorityBoundaryRequired");
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

    private static void AddOperationIdIssues(
        string? operationId,
        List<string> issues)
    {
        AddOperationIdIssues(operationId, "OperationStatusNextSafeActionOperationId", issues);
    }

    private static void AddOperationIdIssues(
        string? operationId,
        string prefix,
        List<string> issues)
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
        string prefix = "OperationStatusNextSafeActionCorrelationId")
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

    private static OperationStatusNextSafeActionFormatterValidationResult Invalid(IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            Issues = issues,
            Warnings = RequiredWarnings,
            ForbiddenAuthorityImplications = RequiredForbiddenAuthorityImplications
        };

    private static OperationStatusNextSafeActionFormatterValidationResult Result(IReadOnlyList<string> issues) =>
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
