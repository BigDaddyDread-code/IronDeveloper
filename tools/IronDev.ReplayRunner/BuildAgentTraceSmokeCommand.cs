using System.Text;
using System.Text.Json;
using IronDev.Infrastructure.Builder;

public static class BuildAgentTraceSmokeCommand
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var runId = ReadOption(args, "--dogfood-run-id") ?? $"buildagent-trace-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var project = ReadOption(args, "--project") ?? "Solitaire";
        var repoRoot = FindRepositoryRoot();
        var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", runId);
        var evidenceRoot = Path.Combine(runRoot, "evidence");
        Directory.CreateDirectory(evidenceRoot);

        var spine = new TraceableBuildAgentSpine();
        var result = spine.CreateSyntheticTrace(project, runId, evidenceRoot);
        await WriteSyntheticEvidenceAsync(result.Trace.EvidenceArtifacts.Select(item => item.Path));

        var tracePath = Path.Combine(runRoot, "build-run-trace.json");
        var reportPath = Path.Combine(runRoot, "build-run-report.json");
        var markdownPath = Path.Combine(runRoot, "build-run-report.md");
        await File.WriteAllTextAsync(tracePath, JsonSerializer.Serialize(result.Trace, options), Encoding.UTF8);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(result.Report, options), Encoding.UTF8);
        await File.WriteAllTextAsync(markdownPath, ToMarkdown(result), Encoding.UTF8);

        var response = new BuildAgentTraceSmokeResult
        {
            Goal = "buildagent-traceable-disposable-build-spine-140",
            Passed = result.Trace.Status == "Succeeded" &&
                     result.Trace.RealRepoMutationCount == 0 &&
                     result.Trace.BuildAttempts.Any(attempt => attempt.Status == "Failed") &&
                     result.Trace.TestAttempts.Any(attempt => attempt.Status == "Failed") &&
                     result.Trace.RepairAttempts.Count >= 2,
            TraceId = result.Trace.TraceId,
            RunId = runId,
            Project = project,
            TracePath = tracePath,
            ReportPath = reportPath,
            MarkdownPath = markdownPath,
            Trace = result.Trace,
            Report = result.Report,
            Boundary = "Synthetic trace smoke only. It does not create a disposable workspace, generate app files, apply patches, mutate memory, or approve real repo writes."
        };

        Console.WriteLine(JsonSerializer.Serialize(response, options));
        return response.Passed ? 0 : 1;
    }

    private static async Task WriteSyntheticEvidenceAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, $"Synthetic evidence for {Path.GetFileName(path)}{Environment.NewLine}", Encoding.UTF8);
        }
    }

    private static string ToMarkdown(TraceableBuildAgentResult result)
    {
        var report = result.Report;
        var lines = new List<string>
        {
            $"# {report.Title}",
            string.Empty,
            $"Status: {report.Status}",
            $"Recommendation: {report.Recommendation}",
            $"Real repo mutation count: {report.RealRepoMutationCount}",
            $"Disposable files changed: {report.DisposableFilesChanged}",
            string.Empty,
            "## Timeline"
        };
        lines.AddRange(report.Timeline.Select(item => $"- {item}"));
        lines.Add(string.Empty);
        lines.Add("## Stages");
        lines.AddRange(report.StageStatuses.Select(stage => $"- {stage.StageName}: {stage.AgentName} {stage.Status} - {stage.Summary}"));
        lines.Add(string.Empty);
        lines.Add(report.Boundary);
        return string.Join(Environment.NewLine, lines);
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;
            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}

public sealed class BuildAgentTraceSmokeResult
{
    public string Goal { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public string TraceId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public string TracePath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string MarkdownPath { get; init; } = string.Empty;
    public object? Trace { get; init; }
    public object? Report { get; init; }
    public string Boundary { get; init; } = string.Empty;
}
