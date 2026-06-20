using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

public static class IronDevCliTaskSwitchBoundaryCampaign
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "approve",
        "execute",
        "deploy",
        "rollback",
        "release",
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
        "mutate"
    ];

    public static bool IsTaskSwitchBoundaryCampaignCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "task-switch-boundary-campaign", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "task-switch-boundary-campaign requires a subcommand: run, inspect, summary, failures, or friction.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"task-switch-boundary-campaign {args[1]} is intentionally unsupported; Block BG runs an evidence-only test campaign.");

        return subcommand switch
        {
            "run" => await HandleRunAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inspect" => HandleInspect(args, output, error),
            "summary" => HandleSummary(args, output, error),
            "failures" => HandleFailures(args, output, error),
            "friction" => HandleFriction(args, output, error),
            _ => Usage(error, $"unsupported task-switch-boundary-campaign subcommand: {args[1]}")
        };
    }

    public static int ExitCodeForSummary(TaskSwitchBoundaryCampaignSummary summary) =>
        summary.CampaignPassed ? 0 : 1;

    private static async Task<int> HandleRunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseRun(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        TaskSwitchBoundaryCampaignArtifacts artifacts;
        try
        {
            artifacts = TaskSwitchBoundaryCampaignRunner.Run(new TaskSwitchBoundaryCampaignRunRequest
            {
                CampaignId = parsed.CampaignId!,
                ScenarioSet = parsed.ScenarioSet!,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        catch (ArgumentException exception)
        {
            return Usage(error, exception.Message);
        }

        var outDirectory = Path.GetFullPath(parsed.OutPath!);
        Directory.CreateDirectory(outDirectory);
        await WriteArtifactsAsync(outDirectory, artifacts, cancellationToken).ConfigureAwait(false);
        RecordEvent(outDirectory, artifacts.Summary);

        if (parsed.Json)
            WriteJson(output, "task-switch-boundary-campaign run", artifacts.Summary.CampaignPassed ? "succeeded" : "failed", new { outDirectory, artifacts.Summary, boundary = TaskSwitchBoundaryCampaignBoundary.Evidence }, [], TaskSwitchBoundaryCampaignBoundary.Evidence);
        else
        {
            output.WriteLine($"Task switch boundary campaign: {artifacts.Summary.CampaignId}");
            output.WriteLine($"Scenarios: {artifacts.Summary.PassedScenarios}/{artifacts.Summary.TotalScenarios}");
            output.WriteLine($"Campaign passed: {artifacts.Summary.CampaignPassed}");
            output.WriteLine("Boundary: context may transfer; authority must not transfer.");
        }

        return ExitCodeForSummary(artifacts.Summary);
    }

    private static int HandleInspect(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var summary = ReadSummary(parsed.CampaignPath!);
        if (summary is null)
            return Failure(output, error, parsed.Json, "task-switch-boundary-campaign inspect", "campaign summary is missing or invalid.");

        if (parsed.Json)
            WriteJson(output, "task-switch-boundary-campaign inspect", "succeeded", new { summary, boundary = TaskSwitchBoundaryCampaignBoundary.ReadOnly }, [], TaskSwitchBoundaryCampaignBoundary.ReadOnly);
        else
        {
            output.WriteLine($"Campaign: {summary.CampaignId}");
            output.WriteLine($"Scenarios: {summary.PassedScenarios}/{summary.TotalScenarios}");
            output.WriteLine($"Campaign passed: {summary.CampaignPassed}");
            output.WriteLine("Boundary: inspect is read-only and cannot approve, execute, deploy, rollback, release, publish, promote memory, mutate, dispatch, or continue workflow.");
        }

        return 0;
    }

    private static int HandleSummary(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var summary = ReadSummary(parsed.CampaignPath!);
        if (summary is null)
            return Failure(output, error, parsed.Json, "task-switch-boundary-campaign summary", "campaign summary is missing or invalid.");

        if (parsed.Json)
            WriteJson(output, "task-switch-boundary-campaign summary", "succeeded", new { summary, boundary = TaskSwitchBoundaryCampaignBoundary.ReadOnly }, [], TaskSwitchBoundaryCampaignBoundary.ReadOnly);
        else
        {
            output.WriteLine($"Total scenarios: {summary.TotalScenarios}");
            output.WriteLine($"Failed scenarios: {summary.FailedScenarios}");
            output.WriteLine($"Mutation leaks: {summary.MutationLeakCount}");
            output.WriteLine($"Old authority leaks: {summary.OldAuthorityPermissionLeakCount}");
            output.WriteLine($"Memory leaks: {summary.MemoryPermissionLeakCount}");
            output.WriteLine($"Workflow leaks: {summary.WorkflowContinuationLeakCount}");
        }

        return 0;
    }

    private static int HandleFailures(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var path = Path.Combine(Path.GetFullPath(parsed.CampaignPath!), "task-switch-boundary-failures.jsonl");
        if (!File.Exists(path))
            return Failure(output, error, parsed.Json, "task-switch-boundary-campaign failures", "campaign failures file is missing.");

        var lines = File.ReadAllLines(path);
        if (parsed.Json)
            WriteJson(output, "task-switch-boundary-campaign failures", "succeeded", new { failures = lines, boundary = TaskSwitchBoundaryCampaignBoundary.ReadOnly }, [], TaskSwitchBoundaryCampaignBoundary.ReadOnly);
        else
        {
            output.WriteLine(lines.Length == 0 ? "No failed scenarios." : string.Join(Environment.NewLine, lines));
            output.WriteLine("Boundary: failures is read-only and cannot mutate or continue workflow.");
        }

        return 0;
    }

    private static int HandleFriction(string[] args, TextWriter output, TextWriter error)
    {
        var parsed = ParseRead(args);
        if (parsed.Error is not null)
            return Usage(error, parsed.Error);

        var path = Path.Combine(Path.GetFullPath(parsed.CampaignPath!), "task-switch-boundary-friction.csv");
        if (!File.Exists(path))
            return Failure(output, error, parsed.Json, "task-switch-boundary-campaign friction", "campaign friction file is missing.");

        var text = File.ReadAllText(path);
        if (parsed.Json)
            WriteJson(output, "task-switch-boundary-campaign friction", "succeeded", new { frictionCsv = text, boundary = TaskSwitchBoundaryCampaignBoundary.ReadOnly }, [], TaskSwitchBoundaryCampaignBoundary.ReadOnly);
        else
        {
            output.Write(text);
            output.WriteLine("Boundary: friction is read-only and cannot approve, execute, mutate, deploy, rollback, release, publish, dispatch, or continue workflow.");
        }

        return 0;
    }

    private static async Task WriteArtifactsAsync(
        string outDirectory,
        TaskSwitchBoundaryCampaignArtifacts artifacts,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "task-switch-boundary-scenarios.jsonl"), TaskSwitchBoundaryCampaignRunner.ToScenarioJsonl(artifacts.ScenarioResults), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "task-switch-boundary-summary.json"), TaskSwitchBoundaryCampaignRunner.ToSummaryJson(artifacts.Summary), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "task-switch-boundary-failures.jsonl"), TaskSwitchBoundaryCampaignRunner.ToFailureJsonl(artifacts.ScenarioResults), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "task-switch-boundary-friction.csv"), artifacts.FrictionCsv, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outDirectory, "task-switch-boundary-report.md"), artifacts.MarkdownReport, cancellationToken).ConfigureAwait(false);
    }

    private static void RecordEvent(string outDirectory, TaskSwitchBoundaryCampaignSummary summary) =>
        new FileBackedGovernanceEventStore(outDirectory).Append(
            summary.CampaignId,
            summary.CampaignId,
            GovernanceKernelEventKind.TaskSwitchBoundaryCampaignCompleted,
            "TaskSwitchBoundaryCampaign",
            summary.CampaignId,
            $"Task-switch boundary campaign completed with {summary.PassedScenarios}/{summary.TotalScenarios} scenarios passing.",
            [
                "task-switch-boundary-scenarios.jsonl",
                "task-switch-boundary-summary.json",
                "task-switch-boundary-failures.jsonl",
                "task-switch-boundary-friction.csv",
                "task-switch-boundary-report.md"
            ]);

    private static ParsedRun ParseRun(string[] args)
    {
        string? campaignId = null;
        string? scenarioSet = null;
        string? outPath = null;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--campaign-id": if (!TryRead(args, ref index, out campaignId)) return ParsedRun.Fail(json, "--campaign-id requires a value."); break;
                case "--scenario-set": if (!TryRead(args, ref index, out scenarioSet)) return ParsedRun.Fail(json, "--scenario-set requires a value."); break;
                case "--out": if (!TryRead(args, ref index, out outPath)) return ParsedRun.Fail(json, "--out requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedRun.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        if (string.IsNullOrWhiteSpace(campaignId)) return ParsedRun.Fail(json, "Missing required option: --campaign-id <campaign-id>.");
        if (string.IsNullOrWhiteSpace(scenarioSet)) return ParsedRun.Fail(json, "Missing required option: --scenario-set <default|phase5|full>.");
        if (string.IsNullOrWhiteSpace(outPath)) return ParsedRun.Fail(json, "Missing required option: --out <path>.");
        return new ParsedRun(campaignId, scenarioSet, outPath, json, null);
    }

    private static ParsedRead ParseRead(string[] args)
    {
        string? campaign = null;
        var json = false;

        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--campaign": if (!TryRead(args, ref index, out campaign)) return ParsedRead.Fail(json, "--campaign requires a value."); break;
                case "--json": json = true; break;
                default: return ParsedRead.Fail(json, $"unsupported option: {args[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(campaign)
            ? ParsedRead.Fail(json, "Missing required option: --campaign <campaign-output-dir>.")
            : new ParsedRead(campaign, json, null);
    }

    private static bool TryRead(string[] args, ref int index, out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Length)
            return false;
        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static TaskSwitchBoundaryCampaignSummary? ReadSummary(string campaignPath)
    {
        try
        {
            var path = Path.Combine(Path.GetFullPath(campaignPath), "task-switch-boundary-summary.json");
            return File.Exists(path)
                ? JsonSerializer.Deserialize<TaskSwitchBoundaryCampaignSummary>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Usage:");
        error.WriteLine("  irondev task-switch-boundary-campaign run --campaign-id <campaign-id> --scenario-set <default|phase5|full> --out <path> [--json]");
        error.WriteLine("  irondev task-switch-boundary-campaign inspect --campaign <campaign-output-dir> [--json]");
        error.WriteLine("  irondev task-switch-boundary-campaign summary --campaign <campaign-output-dir> [--json]");
        error.WriteLine("  irondev task-switch-boundary-campaign failures --campaign <campaign-output-dir> [--json]");
        error.WriteLine("  irondev task-switch-boundary-campaign friction --campaign <campaign-output-dir> [--json]");
        return 2;
    }

    private static int Failure(TextWriter output, TextWriter error, bool json, string command, string message)
    {
        if (json)
            WriteJson(output, command, "failed", null, [message], TaskSwitchBoundaryCampaignBoundary.ReadOnly);
        else
            error.WriteLine(message);
        return 1;
    }

    private static void WriteJson(
        TextWriter output,
        string command,
        string status,
        object? data,
        string[] errors,
        TaskSwitchBoundaryCampaignBoundary boundary)
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

    private sealed record ParsedRun(string? CampaignId, string? ScenarioSet, string? OutPath, bool Json, string? Error)
    {
        public static ParsedRun Fail(bool json, string error) => new(null, null, null, json, error);
    }

    private sealed record ParsedRead(string? CampaignPath, bool Json, string? Error)
    {
        public static ParsedRead Fail(bool json, string error) => new(null, json, error);
    }
}
