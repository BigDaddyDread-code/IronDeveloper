using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

public static class IronDevCliAuthorityUx
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
        "mutate-environment"
    ];

    public static bool IsAuthorityUxCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "authority-ux", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "authority-ux requires a subcommand: explain-campaign, inspect, red-findings, or amber-findings.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"authority-ux {args[1]} is intentionally unsupported; Block BH explains authority state and grants no authority.");

        return subcommand switch
        {
            "explain-campaign" => await HandleExplainCampaignAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inspect" => HandleInspect(args, output, error),
            "red-findings" => HandleFindings(args, output, error, "red"),
            "amber-findings" => HandleFindings(args, output, error, "amber"),
            _ => Usage(error, $"unsupported authority-ux subcommand: {args[1]}")
        };
    }

    public static int ExitCodeForSummary(AuthorityUxSummary summary) =>
        summary.ReportPassed ? 0 : 1;

    private static async Task<int> HandleExplainCampaignAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var parsed = ParseExplainCampaign(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        AuthorityUxArtifacts artifacts;
        try
        {
            artifacts = AuthorityUxReportBuilder.BuildFromCampaignDirectory(parsed.CampaignPath!, createdAtUtc: DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or ArgumentException)
        {
            return Failure(output, error, parsed.Json, "authority-ux explain-campaign", exception.Message, usageFailure: true);
        }

        var outDirectory = Path.GetFullPath(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        await WriteArtifactsAsync(outDirectory, artifacts, cancellationToken).ConfigureAwait(false);

        if (parsed.Json)
        {
            WriteJson(
                output,
                "authority-ux explain-campaign",
                artifacts.Summary.ReportPassed ? "succeeded" : "red-findings",
                new { outDirectory, artifacts.Summary, boundary = AuthorityUxBoundary.Explanation },
                [],
                AuthorityUxBoundary.Explanation);
        }
        else
        {
            output.WriteLine($"Authority UX report: {artifacts.Summary.ReportId}");
            output.WriteLine($"Explanations: {artifacts.Summary.TotalExplanations}");
            output.WriteLine($"Report passed: {artifacts.Summary.ReportPassed}");
            output.WriteLine("Boundary: explanation is not permission; interpretability is not authority.");
        }

        return ExitCodeForSummary(artifacts.Summary);
    }

    private static int HandleInspect(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = ParseReportRead(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var summary = ReadSummary(parsed.ReportPath!);
        if (summary is null)
            return Failure(output, error, parsed.Json, "authority-ux inspect", "authority-ux-summary.json is missing or invalid.", usageFailure: true);

        if (parsed.Json)
            WriteJson(output, "authority-ux inspect", "succeeded", new { summary, boundary = AuthorityUxBoundary.ReadOnly }, [], AuthorityUxBoundary.ReadOnly);
        else
        {
            output.WriteLine($"Report: {summary.ReportId}");
            output.WriteLine($"Explanations: {summary.TotalExplanations}");
            output.WriteLine($"Red findings: {summary.RedFindings.Length}");
            output.WriteLine("Boundary: inspect is read-only and cannot approve, satisfy policy, execute, retry, release, deploy, rollback, mutate, or continue workflow.");
        }

        return 0;
    }

    private static int HandleFindings(string[] args, TextWriter output, TextWriter error, string severity)
    {
        var parsed = ParseReportRead(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var fileName = string.Equals(severity, "red", StringComparison.OrdinalIgnoreCase)
            ? "authority-ux-red-findings.jsonl"
            : "authority-ux-amber-findings.jsonl";
        var path = Path.Combine(Path.GetFullPath(parsed.ReportPath!), fileName);
        if (!File.Exists(path))
            return Failure(output, error, parsed.Json, $"authority-ux {severity}-findings", $"{fileName} is missing.", usageFailure: true);

        var findings = ReadFindings(path);
        if (parsed.Json)
            WriteJson(output, $"authority-ux {severity}-findings", "succeeded", new { findings, boundary = AuthorityUxBoundary.ReadOnly }, [], AuthorityUxBoundary.ReadOnly);
        else
        {
            output.WriteLine(findings.Length == 0 ? $"No {severity} findings." : string.Join(Environment.NewLine, findings.Select(item => item.Message)));
            output.WriteLine($"Boundary: {severity}-findings is read-only and cannot create authority or mutate.");
        }

        return 0;
    }

    private static async Task WriteArtifactsAsync(
        string outDirectory,
        AuthorityUxArtifacts artifacts,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "authority-ux-explanations.jsonl"), AuthorityUxReportBuilder.ToExplanationJsonl(artifacts.Explanations), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "authority-ux-summary.json"), AuthorityUxReportBuilder.ToSummaryJson(artifacts.Summary), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "authority-ux-report.md"), artifacts.MarkdownReport, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "authority-ux-red-findings.jsonl"), AuthorityUxReportBuilder.ToRedFindingsJsonl(artifacts.RedFindings), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "authority-ux-amber-findings.jsonl"), AuthorityUxReportBuilder.ToAmberFindingsJsonl(artifacts.AmberFindings), cancellationToken).ConfigureAwait(false);
    }

    private static AuthorityUxSummary? ReadSummary(string reportPath)
    {
        try
        {
            var path = Path.Combine(Path.GetFullPath(reportPath), "authority-ux-summary.json");
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AuthorityUxSummary>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static AuthorityUxFinding[] ReadFindings(string path)
    {
        var findings = new List<AuthorityUxFinding>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var finding = JsonSerializer.Deserialize<AuthorityUxFinding>(line, JsonOptions);
            if (finding is not null)
                findings.Add(finding);
        }

        return findings.ToArray();
    }

    private static ParsedExplain ParseExplainCampaign(string[] args)
    {
        string? campaign = null;
        string? outPath = null;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--campaign": if (!TryRead(args, ref index, out campaign)) return ParsedExplain.Fail(json, "--campaign requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedExplain.Fail(json, "--out requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedExplain.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(campaign)) return ParsedExplain.Fail(json, "Missing required option: --campaign <campaign-output-dir>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedExplain.Fail(json, "Missing required option: --out <authority-ux-output-dir>.");
        return new ParsedExplain(campaign, outPath, json, null);
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
            ? ParsedReportRead.Fail(json, "Missing required option: --report <authority-ux-output-dir>.")
            : new ParsedReportRead(report, json, null);
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
        error.WriteLine("  irondev authority-ux explain-campaign --campaign <campaign-output-dir> --out <authority-ux-output-dir> [--json]");
        error.WriteLine("  irondev authority-ux inspect --report <authority-ux-output-dir> [--json]");
        error.WriteLine("  irondev authority-ux red-findings --report <authority-ux-output-dir> [--json]");
        error.WriteLine("  irondev authority-ux amber-findings --report <authority-ux-output-dir> [--json]");
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
            WriteJson(output, command, "failed", null, [message], AuthorityUxBoundary.ReadOnly);
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
        AuthorityUxBoundary boundary)
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

    private sealed record ParsedExplain(string? CampaignPath, string? OutPath, bool Json, string? Error)
    {
        public static ParsedExplain Fail(bool json, string error) => new(null, null, json, error);
    }

    private sealed record ParsedReportRead(string? ReportPath, bool Json, string? Error)
    {
        public static ParsedReportRead Fail(bool json, string error) => new(null, json, error);
    }
}
