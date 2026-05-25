using IronDev.Client.Chat;
using IronDev.Client.CodeIndex;
using IronDev.Client.Memory;
using IronDev.Client.Projects;
using IronDev.Client.Tickets;
using IronDev.Client.Traces;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.Testing;
using IronDev.Core.Time;

namespace IronDev.Agent.Services.Testing;

public sealed class TestingCompanionAgent : ITestingCompanionAgent
{
    private static readonly string AppDataRunRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IronDev",
        "TestRuns");

    private readonly IScreenshotCaptureService _screenshotCaptureService;
    private readonly ITraceApiClient _llmTraceService;
    private readonly List<TestMoment> _moments = [];
    private TestRun? _currentRun;

    public TestingCompanionAgent(
        IScreenshotCaptureService screenshotCaptureService,
        ITraceApiClient llmTraceService)
    {
        _screenshotCaptureService = screenshotCaptureService;
        _llmTraceService = llmTraceService;
    }

    public TestRun? CurrentRun => _currentRun;
    public IReadOnlyList<TestMoment> CurrentMoments => _moments;

    public async Task<TestRun> StartSessionAsync(StartTestRunRequest request, CancellationToken ct = default)
    {
        var runId = Guid.NewGuid();
        var projectPath = ResolveProjectPath(request.ProjectPath, request.Target.WorkingDirectory);
        var projectName = string.IsNullOrWhiteSpace(request.ProjectName)
            ? "IronDev"
            : request.ProjectName.Trim();
        var targetName = string.IsNullOrWhiteSpace(request.Target.Name)
            ? projectName
            : request.Target.Name.Trim();
        var logPath = string.IsNullOrWhiteSpace(request.Target.LogPath)
            ? ResolveDefaultIronDevLogPath()
            : request.Target.LogPath;
        var root = ResolveRunRoot(projectPath, runId);

        var run = new TestRun
        {
            Id = runId,
            ProjectId = request.ProjectId,
            ProjectName = projectName,
            StartedAt = DateTimeOffset.UtcNow,
            Status = TestRunStatus.Running,
            TargetId = request.Target.Id,
            TargetName = targetName,
            TargetType = request.Target.TargetType,
            TargetExecutablePath = request.Target.LaunchCommand,
            TargetProcessName = request.Target.ProcessName,
            TargetProcessId = request.Target.ProcessId,
            LogFilePath = logPath,
            GitBranch = TryGit(projectPath, "rev-parse --abbrev-ref HEAD"),
            GitCommit = TryGit(projectPath, "rev-parse --short HEAD"),
            RunFolderPath = root,
            ScreenshotFolderPath = Path.Combine(root, "screenshots"),
            AudioFolderPath = Path.Combine(root, "audio"),
            ReportFolderPath = Path.Combine(root, "reports")
        };
        run.TraceCollectionStartedAt = run.StartedAt;

        Directory.CreateDirectory(run.ScreenshotFolderPath);
        Directory.CreateDirectory(run.AudioFolderPath);
        Directory.CreateDirectory(run.ReportFolderPath);

        _currentRun = run;
        _moments.Clear();

        await PersistJsonAsync(Path.Combine(root, "test-run.json"), run, ct);
        return run;
    }

    public Task AttachToProcessAsync(Guid testRunId, int processId, CancellationToken ct = default)
    {
        EnsureRun(testRunId).TargetProcessId = processId;
        return Task.CompletedTask;
    }

    public async Task<BrokenMomentCaptureDraft> BeginMarkMomentAsync(Guid testRunId, string? activeWorkspace, CancellationToken ct = default)
    {
        var run = EnsureRun(testRunId);
        var capturedAt = DateTimeOffset.UtcNow;
        var stamp = capturedAt.ToString("yyyyMMdd-HHmmss");
        var screenshotPath = Path.Combine(run.ScreenshotFolderPath!, $"screenshot-{stamp}.png");

        await _screenshotCaptureService.CaptureDesktopAsync(screenshotPath, ct);

        return new BrokenMomentCaptureDraft
        {
            TestRunId = testRunId,
            CapturedAt = capturedAt,
            ScreenshotPath = screenshotPath,
            ActiveWorkspace = activeWorkspace,
            ActiveProjectName = run.ProjectName,
            RelevantLogsText = CaptureLogWindow(run.LogFilePath, run.TraceCollectionStartedAt ?? run.StartedAt, capturedAt),
            RelevantTraceText = CaptureLlmTraceWindow(run.TraceCollectionStartedAt ?? run.StartedAt, capturedAt, "test start to marked moment")
        };
    }

    public async Task<TestMoment> SaveMarkedMomentAsync(Guid testRunId, SaveMarkedMomentRequest request, CancellationToken ct = default)
    {
        var run = EnsureRun(testRunId);
        var stamp = request.Draft.CapturedAt.ToString("yyyyMMdd-HHmmss");
        var annotatedPath = Path.Combine(run.ScreenshotFolderPath!, $"annotated-{stamp}.png");
        var markedArea = request.MarkedAreaX.HasValue &&
                         request.MarkedAreaY.HasValue &&
                         request.MarkedAreaWidth is > 0 &&
                         request.MarkedAreaHeight is > 0
            ? new Int32RectData(
                request.MarkedAreaX.Value,
                request.MarkedAreaY.Value,
                request.MarkedAreaWidth.Value,
                request.MarkedAreaHeight.Value)
            : (Int32RectData?)null;

        await _screenshotCaptureService.SaveAnnotatedCopyAsync(request.Draft.ScreenshotPath, annotatedPath, markedArea, ct);

        var moment = new TestMoment
        {
            TestRunId = testRunId,
            MarkedAt = request.Draft.CapturedAt,
            MomentType = request.MomentType,
            UserTextNote = request.UserTextNote,
            ExpectedBehavior = request.ExpectedBehavior,
            ActualBehavior = request.ActualBehavior,
            Severity = request.Severity,
            SuspectedArea = request.SuspectedArea,
            ScreenshotPath = request.Draft.ScreenshotPath,
            AnnotatedScreenshotPath = annotatedPath,
            MarkedAreaX = request.MarkedAreaX,
            MarkedAreaY = request.MarkedAreaY,
            MarkedAreaWidth = request.MarkedAreaWidth,
            MarkedAreaHeight = request.MarkedAreaHeight,
            ActiveWorkspace = request.Draft.ActiveWorkspace,
            ActiveProjectName = request.Draft.ActiveProjectName,
            RelevantLogsText = request.Draft.RelevantLogsText,
            RelevantTraceText = request.Draft.RelevantTraceText
        };

        _moments.Add(moment);
        await PersistJsonAsync(Path.Combine(run.RunFolderPath!, "moments.json"), _moments, ct);
        await File.WriteAllTextAsync(
            Path.Combine(run.RunFolderPath!, $"moment-{moment.Id:N}.md"),
            BuildPrompt(run, moment),
            ct);
        return moment;
    }

    public async Task<IReadOnlyList<TestMoment>> LoadPersistedMomentsAsync(string? projectPath, int take = 25, CancellationToken ct = default)
    {
        var runRoots = GetCandidateRunRoots(projectPath)
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(Directory.GetLastWriteTimeUtc);

        var moments = new List<TestMoment>();
        foreach (var runFolder in runRoots)
        {
            ct.ThrowIfCancellationRequested();
            var momentsPath = Path.Combine(runFolder, "moments.json");
            if (!File.Exists(momentsPath))
                continue;

            try
            {
                var json = await File.ReadAllTextAsync(momentsPath, ct);
                var runMoments = JsonSerializer.Deserialize<List<TestMoment>>(json) ?? [];
                moments.AddRange(runMoments);
            }
            catch
            {
                // Ignore malformed old captures; they should not block the testing workspace.
            }

            if (moments.Count >= take)
                break;
        }

        return moments
            .OrderByDescending(m => m.MarkedAt)
            .Take(take)
            .ToList();
    }

    public async Task<IReadOnlyList<TestRunRecord>> LoadPersistedRunsAsync(string? projectPath, int take = 25, CancellationToken ct = default)
    {
        var runRoots = GetCandidateRunRoots(projectPath)
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(Directory.GetLastWriteTimeUtc);

        var records = new List<TestRunRecord>();
        foreach (var runFolder in runRoots)
        {
            ct.ThrowIfCancellationRequested();
            var runPath = Path.Combine(runFolder, "test-run.json");
            if (!File.Exists(runPath))
                continue;

            try
            {
                var runJson = await File.ReadAllTextAsync(runPath, ct);
                var run = JsonSerializer.Deserialize<TestRun>(runJson);
                if (run == null)
                    continue;

                var momentsPath = Path.Combine(runFolder, "moments.json");
                var momentCount = 0;
                if (File.Exists(momentsPath))
                {
                    var momentsJson = await File.ReadAllTextAsync(momentsPath, ct);
                    momentCount = JsonSerializer.Deserialize<List<TestMoment>>(momentsJson)?.Count ?? 0;
                }

                var reportPath = Path.Combine(runFolder, "report.json");
                TestRunReport? report = null;
                if (File.Exists(reportPath))
                {
                    var reportJson = await File.ReadAllTextAsync(reportPath, ct);
                    report = JsonSerializer.Deserialize<TestRunReport>(reportJson);
                }

                records.Add(new TestRunRecord
                {
                    TestRunId = run.Id,
                    ProjectName = run.ProjectName,
                    TargetName = run.TargetName,
                    TargetType = run.TargetType,
                    Status = run.Status,
                    StartedAt = run.StartedAt,
                    EndedAt = run.EndedAt,
                    MomentCount = momentCount,
                    Summary = report?.Summary ?? run.Summary,
                    RunFolderPath = run.RunFolderPath ?? runFolder,
                    ReportPath = report?.ReportPath ?? Path.Combine(runFolder, "reports", "debug-package.md")
                });
            }
            catch
            {
                // Ignore malformed old captures; one bad run should not hide the rest.
            }

            if (records.Count >= take)
                break;
        }

        return records
            .OrderByDescending(r => r.StartedAt)
            .Take(take)
            .ToList();
    }

    public Task<string> BuildCombinedPromptAsync(string? projectPath, IReadOnlyList<TestMoment> moments, CancellationToken ct = default)
    {
        var projectName = string.IsNullOrWhiteSpace(projectPath)
            ? "IronDev"
            : Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var gitRoot = ResolveProjectPath(projectPath, null);
        var branch = TryGit(gitRoot, "rev-parse --abbrev-ref HEAD") ?? "Unknown";
        var commit = TryGit(gitRoot, "rev-parse --short HEAD") ?? "Unknown";

        var sb = new StringBuilder();
        sb.AppendLine("I captured multiple issues while testing with IronDev Testing Companion.");
        sb.AppendLine();
        sb.AppendLine($"Project: {projectName}");
        sb.AppendLine($"Branch: {branch}");
        sb.AppendLine($"Commit: {commit}");
        sb.AppendLine($"Issue count: {moments.Count}");
        sb.AppendLine();
        sb.AppendLine("Please triage these together, identify likely shared causes, group related bugs, suggest files/components to inspect, and propose a fix order.");
        sb.AppendLine();
        AppendBuildAgentGradingInstructions(sb);
        sb.AppendLine();

        for (var i = 0; i < moments.Count; i++)
        {
            var moment = moments[i];
            sb.AppendLine($"## Issue {i + 1}: {FirstUsefulLine(moment)}");
            sb.AppendLine();
            sb.AppendLine($"- MarkedUtc: {DateTimeDisplay.ToUtcMetadata(moment.MarkedAt)}");
            sb.AppendLine($"- Type: {moment.MomentType}");
            sb.AppendLine($"- Severity: {moment.Severity ?? "Unspecified"}");
            sb.AppendLine($"- Workspace: {moment.ActiveWorkspace ?? "Unknown"}");
            sb.AppendLine($"- Screenshot: {moment.ScreenshotPath}");
            sb.AppendLine($"- Annotated screenshot: {moment.AnnotatedScreenshotPath}");
            AppendImage(sb, "Screenshot image", moment.ScreenshotPath);
            AppendImage(sb, "Annotated screenshot image", moment.AnnotatedScreenshotPath);
            AppendOptionalSection(sb, "Tester note", moment.UserTextNote);
            AppendOptionalSection(sb, "Expected", moment.ExpectedBehavior);
            AppendOptionalSection(sb, "Actual", moment.ActualBehavior);
            AppendOptionalSection(sb, "Suspected area", moment.SuspectedArea);
            AppendCodeSection(sb, "Relevant logs", moment.RelevantLogsText);
            AppendCodeSection(sb, "Relevant traces", moment.RelevantTraceText);
        }

        return Task.FromResult(sb.ToString().Trim());
    }

    public async Task<TestRunReport> EndSessionAndGenerateReportAsync(Guid testRunId, CancellationToken ct = default)
    {
        var run = EnsureRun(testRunId);
        run.EndedAt = DateTimeOffset.UtcNow;
        run.TraceCollectionEndedAt = run.EndedAt;
        run.Status = TestRunStatus.Completed;
        run.Summary = $"{_moments.Count} captured test moments.";
        run.SessionLogPath = await PersistTextIfAnyAsync(
            Path.Combine(run.ReportFolderPath!, "session-logs.txt"),
            CaptureLogWindow(run.LogFilePath, run.TraceCollectionStartedAt ?? run.StartedAt, run.EndedAt.Value),
            ct);
        run.SessionTracePath = await PersistTextIfAnyAsync(
            Path.Combine(run.ReportFolderPath!, "session-llm-traces.txt"),
            CaptureLlmTraceWindow(run.TraceCollectionStartedAt ?? run.StartedAt, run.EndedAt.Value, "full test session"),
            ct);

        var markdown = BuildMarkdown(run, _moments);
        var reportPath = Path.Combine(run.ReportFolderPath!, "debug-package.md");
        await File.WriteAllTextAsync(reportPath, markdown, ct);

        var report = new TestRunReport
        {
            TestRunId = run.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            Markdown = markdown,
            Summary = run.Summary,
            ReportPath = reportPath
        };

        await PersistJsonAsync(Path.Combine(run.RunFolderPath!, "test-run.json"), run, ct);
        await PersistJsonAsync(Path.Combine(run.RunFolderPath!, "report.json"), report, ct);
        return report;
    }

    private TestRun EnsureRun(Guid testRunId)
    {
        if (_currentRun == null || _currentRun.Id != testRunId)
            throw new InvalidOperationException("No active test run is available.");

        return _currentRun;
    }

    private static string ResolveRunRoot(string? projectPath, Guid runId)
    {
        var basePath = !string.IsNullOrWhiteSpace(projectPath) && Directory.Exists(projectPath)
            ? Path.Combine(projectPath, "ProjectData", "TestRuns")
            : AppDataRunRoot;

        return Path.Combine(basePath, runId.ToString("N"));
    }

    private static IEnumerable<string> GetCandidateRunRoots(string? projectPath)
    {
        yield return AppDataRunRoot;

        var resolved = ResolveProjectPath(projectPath, null);
        if (!string.IsNullOrWhiteSpace(resolved))
            yield return Path.Combine(resolved, "ProjectData", "TestRuns");
    }

    private static string? ResolveProjectPath(string? projectPath, string? workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(projectPath) && Directory.Exists(projectPath))
            return projectPath;

        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            return FindGitRoot(workingDirectory) ?? workingDirectory;

        return FindGitRoot(AppContext.BaseDirectory);
    }

    private static string? FindGitRoot(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
            return null;

        var directory = Directory.Exists(startPath)
            ? new DirectoryInfo(startPath)
            : Directory.GetParent(startPath);

        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    private static async Task PersistJsonAsync<T>(string path, T value, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, ct);
    }

    private static async Task<string?> PersistTextIfAnyAsync(string path, string? value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, value.Trim(), ct);
        return path;
    }

    private static string? TryGit(string? workingDirectory, string arguments)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            return null;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            process?.WaitForExit(1500);
            return process?.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? CaptureLogWindow(string? logFilePath, DateTimeOffset startedAt, DateTimeOffset endedAt)
    {
        if (string.IsNullOrWhiteSpace(logFilePath) || !File.Exists(logFilePath))
            return null;

        try
        {
            var lines = File.ReadLines(logFilePath).TakeLast(160).ToList();
            if (lines.Count == 0)
                return null;

            var sb = new StringBuilder();
            sb.AppendLine($"Log window captured for test interval {DateTimeDisplay.ToUtcMetadata(startedAt)} -> {DateTimeDisplay.ToUtcMetadata(endedAt)}.");
            sb.AppendLine($"Source log: {logFilePath}");
            sb.AppendLine();
            foreach (var line in lines)
                sb.AppendLine(line);

            return sb.ToString().Trim();
        }
        catch
        {
            return null;
        }
    }

    private string? CaptureLlmTraceWindow(DateTimeOffset startedAt, DateTimeOffset endedAt, string windowName)
    {
        var startedUtc = startedAt.UtcDateTime;
        var endedUtc = endedAt.UtcDateTime;
        var traces = _llmTraceService
            .GetRecentTraces(250)
            .Where(t => t.CreatedAt >= startedUtc &&
                        t.CreatedAt <= endedUtc.AddSeconds(15))
            .OrderBy(t => t.CreatedAt)
            .Take(80)
            .ToList();

        if (traces.Count == 0)
            return $"No LLM traces were captured for the {windowName} window.";

        var sb = new StringBuilder();
        sb.AppendLine($"LLM traces captured for {windowName} ({traces.Count}):");
        sb.AppendLine($"WindowUtc: {DateTimeDisplay.ToUtcMetadata(startedAt)} -> {DateTimeDisplay.ToUtcMetadata(endedAt)}");
        sb.AppendLine();

        foreach (var trace in traces)
            AppendLlmTraceSummary(sb, trace);

        return sb.ToString().Trim();
    }

    private static void AppendLlmTraceSummary(StringBuilder sb, LlmTraceEntry trace)
    {
        sb.AppendLine($"- {DateTimeDisplay.ToUtcMetadata(trace.CreatedAt)} | {trace.FeatureName} | {trace.Model} | Success={trace.WasSuccessful} | Duration={trace.DurationMs}ms");

        if (trace.ProjectId.HasValue)
            sb.AppendLine($"  ProjectId: {trace.ProjectId}");
        if (trace.TicketId.HasValue)
            sb.AppendLine($"  TicketId: {trace.TicketId}");
        if (!string.IsNullOrWhiteSpace(trace.TraceGroupId))
            sb.AppendLine($"  TraceGroupId: {trace.TraceGroupId}");
        if (!string.IsNullOrWhiteSpace(trace.CurrentUserMessage))
            sb.AppendLine($"  User: {TrimForTrace(trace.CurrentUserMessage)}");
        if (!string.IsNullOrWhiteSpace(trace.ContextSummary))
            sb.AppendLine($"  Context: {TrimForTrace(trace.ContextSummary)}");
        if (!string.IsNullOrWhiteSpace(trace.ParsedResponseSummary))
            sb.AppendLine($"  Result: {TrimForTrace(trace.ParsedResponseSummary)}");
        if (!string.IsNullOrWhiteSpace(trace.Warnings))
            sb.AppendLine($"  Warnings: {TrimForTrace(trace.Warnings)}");
        if (!string.IsNullOrWhiteSpace(trace.ErrorMessage))
            sb.AppendLine($"  Error: {TrimForTrace(trace.ErrorMessage)}");

        if (trace.IndexWarnings.Count > 0)
            sb.AppendLine($"  Index warnings: {string.Join("; ", trace.IndexWarnings.Take(4))}");
        if (trace.FilesReferencedByGeneratedTickets.Count > 0)
            sb.AppendLine($"  Files: {string.Join(", ", trace.FilesReferencedByGeneratedTickets.Take(6))}");
        if (trace.SymbolsReferencedByGeneratedTickets.Count > 0)
            sb.AppendLine($"  Symbols: {string.Join(", ", trace.SymbolsReferencedByGeneratedTickets.Take(6))}");

        sb.AppendLine();
    }

    private static string TrimForTrace(string value)
    {
        var compact = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return compact.Length <= 420 ? compact : compact[..420] + "...";
    }

    private static string ResolveDefaultIronDevLogPath()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IronDev",
            "logs");

        if (!Directory.Exists(logDir))
            return "";

        return Directory
            .EnumerateFiles(logDir, "irondev-*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault() ?? "";
    }

    private static string BuildMarkdown(TestRun run, IReadOnlyList<TestMoment> moments)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# IronDev Test Run Debug Package");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine(moments.Count == 0
            ? "No moments were captured during this test run."
            : $"Captured {moments.Count} test moment(s) for review.");
        sb.AppendLine();
        sb.AppendLine("## Environment");
        sb.AppendLine();
        sb.AppendLine($"- Project: {run.ProjectName}");
        sb.AppendLine($"- Target: {run.TargetName} ({run.TargetType})");
        sb.AppendLine($"- Branch: {run.GitBranch ?? "Unknown"}");
        sb.AppendLine($"- Commit: {run.GitCommit ?? "Unknown"}");
        sb.AppendLine($"- StartedUtc: {DateTimeDisplay.ToUtcMetadata(run.StartedAt)}");
        sb.AppendLine($"- EndedUtc: {FormatUtcMetadata(run.EndedAt)}");
        sb.AppendLine($"- TraceCollectionStartedUtc: {FormatUtcMetadata(run.TraceCollectionStartedAt)}");
        sb.AppendLine($"- TraceCollectionEndedUtc: {FormatUtcMetadata(run.TraceCollectionEndedAt)}");
        if (!string.IsNullOrWhiteSpace(run.SessionLogPath))
            sb.AppendLine($"- Session logs: `{run.SessionLogPath}`");
        if (!string.IsNullOrWhiteSpace(run.SessionTracePath))
            sb.AppendLine($"- Session LLM traces: `{run.SessionTracePath}`");
        sb.AppendLine();
        sb.AppendLine("## Captured Moments");
        sb.AppendLine();

        for (var i = 0; i < moments.Count; i++)
        {
            var moment = moments[i];
            sb.AppendLine($"### {i + 1}. {FirstUsefulLine(moment)}");
            sb.AppendLine();
            sb.AppendLine($"**MarkedUtc:** {DateTimeDisplay.ToUtcMetadata(moment.MarkedAt)}  ");
            sb.AppendLine($"**Type:** {moment.MomentType}  ");
            sb.AppendLine($"**Workspace:** {moment.ActiveWorkspace ?? "Unknown"}  ");
            sb.AppendLine($"**Severity:** {moment.Severity ?? "Unspecified"}  ");
            sb.AppendLine();
            AppendOptionalSection(sb, "What the tester wrote", moment.UserTextNote);
            AppendOptionalSection(sb, "Expected", moment.ExpectedBehavior);
            AppendOptionalSection(sb, "Actual", moment.ActualBehavior);
            AppendOptionalSection(sb, "Suspected area", moment.SuspectedArea);
            sb.AppendLine("**Evidence:**");
            sb.AppendLine();
            sb.AppendLine($"- Screenshot: `{moment.ScreenshotPath}`");
            sb.AppendLine($"- Annotated screenshot: `{moment.AnnotatedScreenshotPath}`");
            AppendImage(sb, "Screenshot image", moment.ScreenshotPath);
            AppendImage(sb, "Annotated screenshot image", moment.AnnotatedScreenshotPath);
            if (moment.MarkedAreaWidth is > 0 && moment.MarkedAreaHeight is > 0)
                sb.AppendLine($"- Marked area: x={moment.MarkedAreaX}, y={moment.MarkedAreaY}, width={moment.MarkedAreaWidth}, height={moment.MarkedAreaHeight}");
            sb.AppendLine();
            AppendCodeSection(sb, "Relevant logs", moment.RelevantLogsText);
            AppendCodeSection(sb, "Relevant traces", moment.RelevantTraceText);
            sb.AppendLine("## Suggested LLM Debug Prompt");
            sb.AppendLine();
            sb.AppendLine(BuildPrompt(run, moment));
            sb.AppendLine();
            sb.AppendLine("## Suggested Ticket");
            sb.AppendLine();
            sb.AppendLine($"Fix: {FirstUsefulLine(moment)}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FirstUsefulLine(TestMoment moment)
    {
        var source = moment.ActualBehavior ?? moment.UserTextNote ?? moment.ExpectedBehavior ?? "Captured test moment";
        return source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Captured test moment";
    }

    private static void AppendOptionalSection(StringBuilder sb, string title, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.AppendLine($"**{title}:**");
        sb.AppendLine();
        sb.AppendLine(value.Trim());
        sb.AppendLine();
    }

    private static void AppendCodeSection(StringBuilder sb, string title, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.AppendLine($"**{title}:**");
        sb.AppendLine();
        sb.AppendLine("```text");
        sb.AppendLine(value.Trim());
        sb.AppendLine("```");
        sb.AppendLine();
    }

    private static string FormatUtcMetadata(DateTimeOffset? value)
        => value.HasValue ? DateTimeDisplay.ToUtcMetadata(value.Value) : "Not recorded";

    private static string BuildPrompt(TestRun run, TestMoment moment)
    {
        var sb = new StringBuilder();
        sb.AppendLine("I found this issue while manually testing an application with IronDev Testing Companion.");
        sb.AppendLine();
        sb.AppendLine($"Project: {run.ProjectName}");
        sb.AppendLine($"Target: {run.TargetName} ({run.TargetType})");
        sb.AppendLine($"Branch: {run.GitBranch ?? "Unknown"}");
        sb.AppendLine($"Commit: {run.GitCommit ?? "Unknown"}");
        sb.AppendLine($"Moment type: {moment.MomentType}");
        sb.AppendLine($"Workspace/context: {moment.ActiveWorkspace ?? "Unknown"}");
        sb.AppendLine();
        AppendOptionalSection(sb, "Tester note", moment.UserTextNote);
        AppendOptionalSection(sb, "Expected", moment.ExpectedBehavior);
        AppendOptionalSection(sb, "Actual", moment.ActualBehavior);
        AppendOptionalSection(sb, "Suspected area", moment.SuspectedArea);
        sb.AppendLine($"Screenshot: {moment.ScreenshotPath}");
        sb.AppendLine($"Annotated screenshot: {moment.AnnotatedScreenshotPath}");
        sb.AppendLine();
        AppendImage(sb, "Screenshot image", moment.ScreenshotPath);
        AppendImage(sb, "Annotated screenshot image", moment.AnnotatedScreenshotPath);
        AppendCodeSection(sb, "Relevant logs", moment.RelevantLogsText);
        AppendCodeSection(sb, "Relevant traces", moment.RelevantTraceText);
        AppendBuildAgentGradingInstructions(sb);
        sb.AppendLine("Please identify likely causes, suggest the files/components to inspect, and propose a fix plan.");
        return sb.ToString().Trim();
    }

    private static void AppendBuildAgentGradingInstructions(StringBuilder sb)
    {
        sb.AppendLine("## Build Agent dogfood check");
        sb.AppendLine();
        sb.AppendLine("Before applying fixes directly, use this bug evidence to run or mentally simulate the Build Agent workflow as a proposal generator:");
        sb.AppendLine();
        sb.AppendLine("1. Create or select a focused bug ticket from this evidence.");
        sb.AppendLine("2. Ask the Build Agent for an implementation plan and proposed affected files.");
        sb.AppendLine("3. Grade whether the Build Agent used the screenshots, notes, logs, LLM traces, and project memory correctly.");
        sb.AppendLine("4. Do not auto-apply broad code changes; treat the Build Agent output as a reviewable proposal.");
        sb.AppendLine("5. In the answer, include a short Build Agent grade: Useful / Partially useful / Weak, with why.");
        sb.AppendLine();
    }

    private static void AppendImage(StringBuilder sb, string title, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        sb.AppendLine($"{title}:");
        sb.AppendLine($"![{title}](<{path.Replace('\\', '/')}>)");
        sb.AppendLine();
    }
}
