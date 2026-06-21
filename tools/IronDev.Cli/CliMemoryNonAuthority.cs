using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

public static class IronDevCliMemoryNonAuthority
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "approve",
        "satisfy-policy",
        "execute",
        "retry",
        "release",
        "deploy",
        "rollback",
        "merge",
        "source-apply",
        "commit",
        "push",
        "publish",
        "publish-package",
        "promote-memory",
        "continue",
        "continue-workflow",
        "dispatch",
        "trigger-pipeline",
        "mutate",
        "mutate-source",
        "mutate-environment",
        "write-memory",
        "promote",
        "remember-as-authority"
    ];

    public static bool IsMemoryNonAuthorityCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "memory-non-authority", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "memory-non-authority requires a subcommand: evaluate-scenarios, evaluate, inspect, red-findings, or amber-findings.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"memory-non-authority {args[1]} is intentionally unsupported; Block BI treats memory as context only.");

        return subcommand switch
        {
            "evaluate-scenarios" => await HandleEvaluateScenariosAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "evaluate" => await HandleEvaluateAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inspect" => HandleInspect(args, output, error),
            "red-findings" => HandleFindings(args, output, error, "red"),
            "amber-findings" => HandleFindings(args, output, error, "amber"),
            _ => Usage(error, $"unsupported memory-non-authority subcommand: {args[1]}")
        };
    }

    public static int ExitCodeForSummary(MemoryNonAuthoritySummary summary) =>
        summary.ReportPassed ? 0 : 1;

    private static async Task<int> HandleEvaluateScenariosAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var parsed = ParseEvaluateScenarios(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        MemoryNonAuthorityArtifacts artifacts;
        try
        {
            artifacts = MemoryNonAuthorityReportBuilder.EvaluateScenarioSet(parsed.ScenarioSet!, parsed.ReportId!, DateTimeOffset.UtcNow);
        }
        catch (ArgumentException exception)
        {
            return Failure(output, error, parsed.Json, "memory-non-authority evaluate-scenarios", exception.Message, usageFailure: true);
        }

        await WriteArtifactsAsync(Path.GetFullPath(parsed.OutPath!), artifacts, cancellationToken).ConfigureAwait(false);
        WriteResult(output, parsed.Json, "memory-non-authority evaluate-scenarios", parsed.OutPath!, artifacts);
        return ExitCodeForSummary(artifacts.Summary);
    }

    private static async Task<int> HandleEvaluateAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var parsed = ParseEvaluate(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        MemoryNonAuthorityArtifacts artifacts;
        try
        {
            artifacts = MemoryNonAuthorityReportBuilder.EvaluateAttemptsFromJsonl(parsed.AttemptsPath!, parsed.ReportId!, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or ArgumentException)
        {
            return Failure(output, error, parsed.Json, "memory-non-authority evaluate", exception.Message, usageFailure: true);
        }

        await WriteArtifactsAsync(Path.GetFullPath(parsed.OutPath!), artifacts, cancellationToken).ConfigureAwait(false);
        WriteResult(output, parsed.Json, "memory-non-authority evaluate", parsed.OutPath!, artifacts);
        return ExitCodeForSummary(artifacts.Summary);
    }

    private static int HandleInspect(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = ParseReportRead(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var summary = ReadSummary(parsed.ReportPath!);
        if (summary is null)
            return Failure(output, error, parsed.Json, "memory-non-authority inspect", "memory-non-authority-summary.json is missing or invalid.", usageFailure: true);

        if (parsed.Json)
            WriteJson(output, "memory-non-authority inspect", "succeeded", new { summary, boundary = MemoryNonAuthorityBoundary.ReadOnly }, [], MemoryNonAuthorityBoundary.ReadOnly);
        else
        {
            output.WriteLine($"Report: {summary.ReportId}");
            output.WriteLine($"Attempts: {summary.TotalAttempts}");
            output.WriteLine($"Blocked as authority: {summary.BlockedAsAuthorityCount}");
            output.WriteLine("Boundary: inspect is read-only; memory remains context only.");
        }

        return 0;
    }

    private static int HandleFindings(string[] args, TextWriter output, TextWriter error, string severity)
    {
        var parsed = ParseReportRead(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var fileName = string.Equals(severity, "red", StringComparison.OrdinalIgnoreCase)
            ? "memory-non-authority-red-findings.jsonl"
            : "memory-non-authority-amber-findings.jsonl";
        var path = Path.Combine(Path.GetFullPath(parsed.ReportPath!), fileName);
        if (!File.Exists(path))
            return Failure(output, error, parsed.Json, $"memory-non-authority {severity}-findings", $"{fileName} is missing.", usageFailure: true);

        var findings = ReadFindings(path);
        if (parsed.Json)
            WriteJson(output, $"memory-non-authority {severity}-findings", "succeeded", new { findings, boundary = MemoryNonAuthorityBoundary.ReadOnly }, [], MemoryNonAuthorityBoundary.ReadOnly);
        else
        {
            output.WriteLine(findings.Length == 0 ? $"No {severity} findings." : string.Join(Environment.NewLine, findings.Select(item => item.Message)));
            output.WriteLine($"Boundary: {severity}-findings is read-only and cannot authorize action.");
        }

        return 0;
    }

    private static async Task WriteArtifactsAsync(
        string outDirectory,
        MemoryNonAuthorityArtifacts artifacts,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outDirectory);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "memory-non-authority-decisions.jsonl"), MemoryNonAuthorityReportBuilder.ToDecisionJsonl(artifacts.Decisions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "memory-non-authority-summary.json"), MemoryNonAuthorityReportBuilder.ToSummaryJson(artifacts.Summary), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "memory-non-authority-report.md"), artifacts.MarkdownReport, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "memory-non-authority-red-findings.jsonl"), MemoryNonAuthorityReportBuilder.ToRedFindingsJsonl(artifacts.RedFindings), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "memory-non-authority-amber-findings.jsonl"), MemoryNonAuthorityReportBuilder.ToAmberFindingsJsonl(artifacts.AmberFindings), cancellationToken).ConfigureAwait(false);
    }

    private static void WriteResult(
        TextWriter output,
        bool json,
        string command,
        string outPath,
        MemoryNonAuthorityArtifacts artifacts)
    {
        if (json)
        {
            WriteJson(
                output,
                command,
                artifacts.Summary.ReportPassed ? "succeeded" : "red-findings",
                new { outDirectory = Path.GetFullPath(outPath), artifacts.Summary, boundary = MemoryNonAuthorityBoundary.Context },
                [],
                MemoryNonAuthorityBoundary.Context);
            return;
        }

        output.WriteLine($"Memory non-authority report: {artifacts.Summary.ReportId}");
        output.WriteLine($"Attempts: {artifacts.Summary.TotalAttempts}");
        output.WriteLine($"Report passed: {artifacts.Summary.ReportPassed}");
        output.WriteLine("Boundary: memory may explain context; memory must not authorize action.");
    }

    private static ParsedScenarioEvaluation ParseEvaluateScenarios(string[] args)
    {
        string? scenarioSet = null;
        string? reportId = null;
        string? outPath = null;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--scenario-set": if (!TryRead(args, ref index, out scenarioSet)) return ParsedScenarioEvaluation.Fail(json, "--scenario-set requires a value."); break;
                case "--report-id": if (!TryRead(args, ref index, out reportId)) return ParsedScenarioEvaluation.Fail(json, "--report-id requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedScenarioEvaluation.Fail(json, "--out requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedScenarioEvaluation.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(scenarioSet)) return ParsedScenarioEvaluation.Fail(json, "Missing required option: --scenario-set <default|full>.");
        if (string.IsNullOrWhiteSpace(reportId)) return ParsedScenarioEvaluation.Fail(json, "Missing required option: --report-id <report-id>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedScenarioEvaluation.Fail(json, "Missing required option: --out <memory-non-authority-output-dir>.");
        return new ParsedScenarioEvaluation(scenarioSet, reportId, outPath, json, null);
    }

    private static ParsedAttemptEvaluation ParseEvaluate(string[] args)
    {
        string? attempts = null;
        string? reportId = null;
        string? outPath = null;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--attempts": if (!TryRead(args, ref index, out attempts)) return ParsedAttemptEvaluation.Fail(json, "--attempts requires a value."); break;
                case "--report-id": if (!TryRead(args, ref index, out reportId)) return ParsedAttemptEvaluation.Fail(json, "--report-id requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedAttemptEvaluation.Fail(json, "--out requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedAttemptEvaluation.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(attempts)) return ParsedAttemptEvaluation.Fail(json, "Missing required option: --attempts <memory-authority-attempts.jsonl>.");
        if (string.IsNullOrWhiteSpace(reportId)) return ParsedAttemptEvaluation.Fail(json, "Missing required option: --report-id <report-id>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedAttemptEvaluation.Fail(json, "Missing required option: --out <memory-non-authority-output-dir>.");
        return new ParsedAttemptEvaluation(attempts, reportId, outPath, json, null);
    }

    private static ParsedReportRead ParseReportRead(string[] args)
    {
        string? report = null;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--report": if (!TryRead(args, ref index, out report)) return ParsedReportRead.Fail(json, "--report requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedReportRead.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(report)
            ? ParsedReportRead.Fail(json, "Missing required option: --report <memory-non-authority-output-dir>.")
            : new ParsedReportRead(report, json, null);
    }

    private static MemoryNonAuthoritySummary? ReadSummary(string reportPath)
    {
        try
        {
            var path = Path.Combine(Path.GetFullPath(reportPath), "memory-non-authority-summary.json");
            return File.Exists(path)
                ? JsonSerializer.Deserialize<MemoryNonAuthoritySummary>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static MemoryNonAuthorityFinding[] ReadFindings(string path)
    {
        var findings = new List<MemoryNonAuthorityFinding>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var finding = JsonSerializer.Deserialize<MemoryNonAuthorityFinding>(line, JsonOptions);
            if (finding is not null)
                findings.Add(finding);
        }

        return findings.ToArray();
    }

    private static bool TryRead(string[] args, ref int index, out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Length)
            return false;
        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Usage:");
        error.WriteLine("  irondev memory-non-authority evaluate-scenarios --scenario-set <default|full> --report-id <report-id> --out <path> [--json]");
        error.WriteLine("  irondev memory-non-authority evaluate --attempts <memory-authority-attempts.jsonl> --report-id <report-id> --out <path> [--json]");
        error.WriteLine("  irondev memory-non-authority inspect --report <path> [--json]");
        error.WriteLine("  irondev memory-non-authority red-findings --report <path> [--json]");
        error.WriteLine("  irondev memory-non-authority amber-findings --report <path> [--json]");
        return 2;
    }

    private static int Failure(
        TextWriter output,
        TextWriter error,
        bool json,
        string command,
        string message,
        bool usageFailure)
    {
        if (json)
            WriteJson(output, command, "failed", null, [message], MemoryNonAuthorityBoundary.ReadOnly);
        else
            error.WriteLine(message);
        return usageFailure ? 2 : 1;
    }

    private static void WriteJson(
        TextWriter output,
        string command,
        string status,
        object? data,
        string[] errors,
        MemoryNonAuthorityBoundary boundary)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ok = errors.Length == 0 && status != "failed",
            command,
            status,
            data,
            errors,
            boundary
        }, JsonOptions));
    }

    private sealed record ParsedScenarioEvaluation(string? ScenarioSet, string? ReportId, string? OutPath, bool Json, string? Error)
    {
        public static ParsedScenarioEvaluation Fail(bool json, string error) => new(null, null, null, json, error);
    }

    private sealed record ParsedAttemptEvaluation(string? AttemptsPath, string? ReportId, string? OutPath, bool Json, string? Error)
    {
        public static ParsedAttemptEvaluation Fail(bool json, string error) => new(null, null, null, json, error);
    }

    private sealed record ParsedReportRead(string? ReportPath, bool Json, string? Error)
    {
        public static ParsedReportRead Fail(bool json, string error) => new(null, json, error);
    }
}
