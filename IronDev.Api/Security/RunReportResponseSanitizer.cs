using IronDev.Core.RunReports;
using IronDev.Core.Security;

namespace IronDev.Api.Security;

public static class RunReportResponseSanitizer
{
    public static RunReportDetail Sanitize(RunReportDetail report)
    {
        var reportDirectory = GetReportDirectory(report.ReportPath);

        return report with
        {
            Title = Text(report.Title),
            Summary = Text(report.Summary),
            Recommendation = Text(report.Recommendation),
            Boundary = Text(report.Boundary),
            WorkspacePath = PathLabel(report.WorkspacePath),
            ReportPath = SafeReference(report.ReportPath, reportDirectory),
            Warnings = report.Warnings.Select(Text).ToArray(),
            Stages = report.Stages.Select(stage => stage with
            {
                StageName = Text(stage.StageName),
                AgentName = Text(stage.AgentName),
                Summary = Text(stage.Summary)
            }).ToArray(),
            Attempts = report.Attempts.Select(attempt => attempt with
            {
                FailureClassification = Text(attempt.FailureClassification),
                Summary = Text(attempt.Summary)
            }).ToArray(),
            Repairs = report.Repairs.Select(repair => repair with
            {
                TriggerFailureClassification = Text(repair.TriggerFailureClassification),
                PlannedFix = Text(repair.PlannedFix)
            }).ToArray(),
            Evidence = report.Evidence.Select(item => item with
            {
                Path = SafeReference(item.Path, reportDirectory) ?? string.Empty,
                Summary = Text(item.Summary)
            }).ToArray(),
            PromotionReview = Sanitize(report.PromotionReview),
            AdversarialReview = Sanitize(report.AdversarialReview),
            MemoryImprovement = Sanitize(report.MemoryImprovement)
        };
    }

    public static RunEventDto Sanitize(RunEventDto runEvent) => runEvent with
    {
        Message = Text(runEvent.Message),
        Payload = runEvent.Payload.ToDictionary(
            item => item.Key,
            item => IsPrivatePayload(item.Key)
                ? SensitiveDataRedactor.RedactedValue
                : item.Key.Contains("path", StringComparison.OrdinalIgnoreCase)
                    ? PathLabel(item.Value)
                    : Text(item.Value),
            StringComparer.Ordinal)
    };

    private static RunPromotionReview? Sanitize(RunPromotionReview? review) => review is null
        ? null
        : review with
        {
            Recommendation = Text(review.Recommendation),
            PromotableFiles = review.PromotableFiles.Select(Sanitize).ToArray(),
            BlockedFiles = review.BlockedFiles.Select(Sanitize).ToArray(),
            Risks = review.Risks.Select(risk => risk with
            {
                Message = Text(risk.Message),
                Mitigation = Text(risk.Mitigation)
            }).ToArray(),
            BlockedActions = review.BlockedActions.Select(Text).ToArray()
        };

    private static RunPromotionFile Sanitize(RunPromotionFile file) => file with
    {
        RelativePath = SafeReference(file.RelativePath, null) ?? string.Empty,
        Reason = Text(file.Reason)
    };

    private static RunAdversarialReview? Sanitize(RunAdversarialReview? review) => review is null
        ? null
        : review with
        {
            Findings = review.Findings.Select(finding => finding with
            {
                Title = Text(finding.Title),
                EvidenceCitation = Text(finding.EvidenceCitation),
                SuggestedFix = Text(finding.SuggestedFix)
            }).ToArray()
        };

    private static RunMemoryImprovementReview? Sanitize(RunMemoryImprovementReview? review) => review is null
        ? null
        : review with
        {
            Proposals = review.Proposals.Select(proposal => proposal with
            {
                Title = Text(proposal.Title),
                MemoryAuthorityImpact = Text(proposal.MemoryAuthorityImpact)
            }).ToArray()
        };

    private static string Text(string? value) => SensitiveDataRedactor.Redact(value);

    private static bool IsPrivatePayload(string key) =>
        key.Contains("prompt", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("completion", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("requestBody", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("rawRequest", StringComparison.OrdinalIgnoreCase);

    private static string PathLabel(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = Normalize(path).TrimEnd('/');
        var separator = normalized.LastIndexOf('/');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    private static string? GetReportDirectory(string? reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !IsAbsolute(reportPath))
            return null;

        var normalized = Normalize(reportPath).TrimEnd('/');
        var separator = normalized.LastIndexOf('/');
        return separator > 0 ? normalized[..separator] : null;
    }

    private static string? SafeReference(string? path, string? reportDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = Normalize(path);
        if (IsAbsolute(normalized))
        {
            if (!string.IsNullOrWhiteSpace(reportDirectory))
            {
                var prefix = $"{Normalize(reportDirectory).TrimEnd('/')}" + "/";
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return normalized[prefix.Length..];
            }

            return PathLabel(path);
        }

        return IsTraversal(normalized) ? PathLabel(normalized) : normalized;
    }

    private static bool IsTraversal(string path) =>
        Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == "..");

    private static bool IsAbsolute(string path)
    {
        var normalized = Normalize(path);
        return normalized.StartsWith("/", StringComparison.Ordinal) ||
               (normalized.Length >= 3 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '/');
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
