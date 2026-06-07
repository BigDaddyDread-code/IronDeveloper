using System.Net;

namespace IronDev.Core.RunReports;

public sealed record RunReportContractEnvelope
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required RunReportContractData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record RunReportContractReadResult
{
    public required string Status { get; init; }
    public required string Command { get; init; }
    public string? TraceId { get; init; }
    public required string Summary { get; init; }
    public required RunReportContractData Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public int ExitCode { get; init; }
}

public sealed record RunReportContractData
{
    public required string RunId { get; init; }
    public required string RunStatus { get; init; }
    public string? AgentName { get; init; }
    public string? TraceId { get; init; }
    public required RunReportGovernanceContractData Governance { get; init; }
    public IReadOnlyList<RunReportToolCallContractData> ToolCalls { get; init; } = [];
    public IReadOnlyList<RunReportProcessCommandContractData> ProcessCommands { get; init; } = [];
    public IReadOnlyList<RunReportEvidenceContractData> Evidence { get; init; } = [];
    public IReadOnlyDictionary<string, string> EvidenceSummaryByPath { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record RunReportGovernanceContractData
{
    public required string Decision { get; init; }
    public required string ApprovalDecision { get; init; }
    public string? BlockedReason { get; init; }
    public bool RequiresHumanApproval { get; init; }
}

public sealed record RunReportToolCallContractData
{
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }
    public string? Impact { get; init; }
    public required string Status { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
}

public sealed record RunReportProcessCommandContractData
{
    public string? Command { get; init; }
    public int? ExitCode { get; init; }
    public bool? TimedOut { get; init; }
    public string? Summary { get; init; }
    public string? StdoutPath { get; init; }
    public string? StderrPath { get; init; }
    public int? DurationMs { get; init; }
}

public sealed record RunReportEvidenceContractData
{
    public string? EvidenceId { get; init; }
    public required string Kind { get; init; }
    public string? Path { get; init; }
    public string? Sha256 { get; init; }
    public string? Summary { get; init; }
}

public static class RunReportContractMapper
{
    private static readonly string[] BlockedRunStatuses =
    [
        "PausedForApproval",
        "Cancelled"
    ];

    public static RunReportContractEnvelope MapFromApiReport(RunReportDto report) =>
        MapFromApiReport(report, includeDerivedWarnings: true);

    public static RunReportContractReadResult MapFromApiFailure(
        string runId,
        HttpStatusCode statusCode,
        string? responseBody)
    {
        var summaryPrefix = $"IronDev.Api runs report failed with {(int)statusCode} {statusCode}.";
        var failures = new List<string> { summaryPrefix };
        if (!string.IsNullOrWhiteSpace(responseBody))
            failures.Add(responseBody);

        var runStatus = statusCode == HttpStatusCode.NotFound ? "not_found" : "error";

        return new RunReportContractReadResult
        {
            Status = "failed",
            Command = "runs report",
            TraceId = null,
            Summary = "Run report could not be loaded.",
            Data = new RunReportContractData
            {
                RunId = runId,
                RunStatus = runStatus,
                TraceId = null,
                AgentName = null,
                Governance = new RunReportGovernanceContractData
                {
                    Decision = "not_available",
                    ApprovalDecision = "not_available",
                    BlockedReason = null,
                    RequiresHumanApproval = false
                },
                ToolCalls = [],
                ProcessCommands = [],
                Evidence = [],
                EvidenceSummaryByPath = new Dictionary<string, string>(),
                Warnings = []
            },
            Errors = failures,
            Warnings = [],
            ExitCode = 1
        };
    }

    public static RunReportContractReadResult MapToReadResult(RunReportContractEnvelope envelope) =>
        new()
        {
            Status = envelope.Status,
            Command = envelope.Command,
            TraceId = envelope.TraceId,
            Summary = envelope.Summary,
            Data = envelope.Data,
            Errors = envelope.Errors,
            Warnings = envelope.Warnings,
            ExitCode = envelope.Status == "succeeded" ? 0 : 1
        };

    public static RunReportContractEnvelope ToEnvelope(RunReportContractReadResult readResult) =>
        new()
        {
            Status = readResult.Status,
            Command = readResult.Command,
            TraceId = readResult.TraceId,
            Summary = readResult.Summary,
            Data = readResult.Data,
            Errors = readResult.Errors,
            Warnings = readResult.Warnings
        };

    private static RunReportContractEnvelope MapFromApiReport(RunReportDto report, bool includeDerivedWarnings)
    {
        var status = DetermineCommandStatus(report.Status.Status, report.Status.Recommendation);
        var traceId = report.Report?.TraceId ?? report.Status.TraceId;
        var reportWarnings = new List<string>(report.Report?.Warnings ?? []);
        var agentName = report.Report?.Stages.FirstOrDefault()?.AgentName;
        var governance = MapGovernance(report.Status.Status, report.Status.Recommendation);
        var evidenceEntries = report.Report?.Evidence.Select(item => new RunReportEvidenceContractData
        {
            EvidenceId = null,
            Kind = string.IsNullOrWhiteSpace(item.Type) ? "unknown" : item.Type,
            Path = string.IsNullOrWhiteSpace(item.Path) ? null : item.Path,
            Sha256 = null,
            Summary = string.IsNullOrWhiteSpace(item.Summary) ? null : item.Summary
        }).ToArray() ?? [];
        var evidenceByPath = evidenceEntries
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key!,
                group => group.First().Summary ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        if (includeDerivedWarnings)
        {
            if (!string.Equals(report.Status.Status, "NotStarted", StringComparison.OrdinalIgnoreCase))
                reportWarnings.Add("Run governance fields are derived from run status and recommendation.");

            reportWarnings.Add("Run report did not include typed tool-call data.");
            reportWarnings.Add("Run report did not include typed process command data.");
        }

        return new()
        {
            Status = status,
            Command = "runs report",
            TraceId = traceId,
            Summary = status is "succeeded"
                ? $"Run '{report.Status.RunId}' completed successfully."
                : $"Run '{report.Status.RunId}' is {status}.",
            Data = new RunReportContractData
            {
                RunId = report.Status.RunId,
                RunStatus = report.Status.Status,
                AgentName = agentName,
                TraceId = traceId,
                Governance = governance,
                ToolCalls = [],
                ProcessCommands = [],
                Evidence = evidenceEntries,
                EvidenceSummaryByPath = evidenceByPath,
                Warnings = reportWarnings
            },
            Errors = status == "failed" ? ["Run report indicates failure."] : [],
            Warnings = reportWarnings
        };
    }

    private static RunReportGovernanceContractData MapGovernance(string status, string recommendation)
    {
        if (IsApprovalBlocked(status, recommendation))
        {
            return new RunReportGovernanceContractData
            {
                Decision = "blocked",
                ApprovalDecision = "required",
                BlockedReason = status,
                RequiresHumanApproval = true
            };
        }

        if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return new RunReportGovernanceContractData
            {
                Decision = "derived",
                ApprovalDecision = "denied",
                BlockedReason = null,
                RequiresHumanApproval = false
            };
        }

        return new RunReportGovernanceContractData
        {
            Decision = "derived",
            ApprovalDecision = ResolveApprovalDecision(status, recommendation),
            BlockedReason = null,
            RequiresHumanApproval = false
        };
    }

    private static string DetermineCommandStatus(string runStatus, string recommendation)
    {
        if (string.Equals(runStatus, "Failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(recommendation, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return "failed";
        }

        if (IsApprovalBlocked(runStatus, recommendation))
            return "blocked";

        if (string.Equals(runStatus, "Cancelled", StringComparison.OrdinalIgnoreCase))
            return "blocked";

        return "succeeded";
    }

    private static bool IsApprovalBlocked(string runStatus, string recommendation) =>
        BlockedRunStatuses.Any(item => string.Equals(runStatus, item, StringComparison.OrdinalIgnoreCase)) ||
        recommendation.Contains("Approval", StringComparison.OrdinalIgnoreCase);

    private static string ResolveApprovalDecision(string status, string recommendation)
    {
        if (IsApprovalBlocked(status, recommendation))
            return "required";

        return string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase)
            ? "denied"
            : "not_required";
    }
}

