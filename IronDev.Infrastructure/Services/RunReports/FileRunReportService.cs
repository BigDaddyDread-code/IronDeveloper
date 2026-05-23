using System.Text.Json;
using IronDev.Core.RunReports;

namespace IronDev.Infrastructure.Services.RunReports;

public sealed class FileRunReportService : IRunReportService, IRunEvidenceService
{
    private readonly string _runsRoot;

    public FileRunReportService()
        : this(Path.Combine(FindRepositoryRoot(), "tools", "dogfood", "runs"))
    {
    }

    public FileRunReportService(string runsRoot)
    {
        _runsRoot = Path.GetFullPath(runsRoot);
    }

    public async Task<IReadOnlyList<RunReportSummary>> GetRecentRunsAsync(
        string? project = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_runsRoot))
            return [];

        var summaries = new List<RunReportSummary>();
        foreach (var directory in Directory.EnumerateDirectories(_runsRoot)
                     .Select(path => new DirectoryInfo(path))
                     .OrderByDescending(directory => directory.LastWriteTimeUtc)
                     .Take(100))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = await GetRunAsync(directory.Name, cancellationToken);
            if (detail is null)
                continue;

            if (!string.IsNullOrWhiteSpace(project) &&
                !string.Equals(detail.Project, project, StringComparison.OrdinalIgnoreCase))
                continue;

            summaries.Add(new RunReportSummary
            {
                RunId = detail.RunId,
                TraceId = detail.TraceId,
                Project = detail.Project,
                Title = detail.Title,
                Status = detail.Status,
                Recommendation = detail.Recommendation,
                CompletedUtc = directory.LastWriteTimeUtc,
                RealRepoMutationCount = detail.RealRepoMutationCount,
                DisposableFilesChanged = detail.DisposableFilesChanged
            });
        }

        return summaries;
    }

    public async Task<RunReportDetail?> GetRunAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        var runDirectory = GetRunDirectory(runId);
        if (!Directory.Exists(runDirectory))
            return null;

        var reportPath = FindReportPath(runDirectory);
        if (reportPath is null)
        {
            return new RunReportDetail
            {
                RunId = runId,
                Title = runId,
                Status = "Invalid",
                Summary = "No supported report JSON file was found for this run.",
                Evidence = await GetEvidenceAsync(runId, cancellationToken),
                Warnings = ["Missing report JSON."]
            };
        }

        try
        {
            await using var stream = File.OpenRead(reportPath);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement.Clone();
            var evidence = await GetEvidenceAsync(runId, cancellationToken);

            return new RunReportDetail
            {
                RunId = runId,
                TraceId = ReadString(root, "TraceId") ?? ReadString(root, "traceId"),
                Project = ReadString(root, "Project") ?? ReadString(root, "project") ?? InferProject(root),
                Title = ReadString(root, "Title") ?? ReadString(root, "title") ?? ReadString(root, "goal_id") ?? runId,
                Status = ReadString(root, "Status") ?? ReadString(root, "status") ?? "Unknown",
                Summary = ReadString(root, "Summary") ?? ReadString(root, "summary") ?? "",
                Recommendation = ReadString(root, "Recommendation") ?? ReadString(root, "recommendation") ?? "",
                RealRepoMutationCount = ReadInt(root, "RealRepoMutationCount", "realRepoMutationCount"),
                DisposableFilesChanged = ReadInt(root, "DisposableFilesChanged", "disposableFilesChanged"),
                Stages = ReadStages(root),
                Attempts = ReadAttempts(root),
                Repairs = ReadRepairs(root),
                Evidence = evidence.Count > 0 ? evidence : ReadEvidenceRefs(root),
                Boundary = ReadString(root, "Boundary") ?? ReadString(root, "boundary") ?? "",
                WorkspacePath = ReadWorkspacePath(root),
                Warnings = evidence.Count == 0 ? ["No evidence files were found for this run."] : [],
                ReportPath = reportPath
            };
        }
        catch (JsonException ex)
        {
            return new RunReportDetail
            {
                RunId = runId,
                Title = runId,
                Status = "Invalid",
                Summary = $"Malformed report JSON: {ex.Message}",
                Evidence = await GetEvidenceAsync(runId, cancellationToken),
                Warnings = ["Malformed report JSON."]
            };
        }
    }

    public Task<IReadOnlyList<RunEvidenceItem>> GetEvidenceAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        var runDirectory = GetRunDirectory(runId);
        if (!Directory.Exists(runDirectory))
            return Task.FromResult<IReadOnlyList<RunEvidenceItem>>([]);

        var items = new List<RunEvidenceItem>();
        foreach (var path in Directory.EnumerateFiles(runDirectory, "*", SearchOption.AllDirectories)
                     .Where(path => IsEvidenceLike(runDirectory, path))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(new RunEvidenceItem
            {
                Type = ClassifyEvidence(path),
                Path = path,
                Summary = Path.GetFileName(path)
            });
        }

        return Task.FromResult<IReadOnlyList<RunEvidenceItem>>(items);
    }

    public async Task<string?> ReadEvidenceTextAsync(
        string runId,
        string evidencePath,
        CancellationToken cancellationToken = default)
    {
        var runDirectory = GetRunDirectory(runId);
        if (!Directory.Exists(runDirectory) || string.IsNullOrWhiteSpace(evidencePath))
            return null;

        var fullPath = Path.GetFullPath(Path.IsPathRooted(evidencePath)
            ? evidencePath
            : Path.Combine(runDirectory, evidencePath));
        if (!IsUnderDirectory(runDirectory, fullPath) || !File.Exists(fullPath))
            return null;

        return await File.ReadAllTextAsync(fullPath, cancellationToken);
    }

    private string GetRunDirectory(string runId)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeRunId = new string(runId
            .Select(ch => invalidChars.Contains(ch) || ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar ? '-' : ch)
            .ToArray());
        if (string.IsNullOrWhiteSpace(safeRunId) || safeRunId is "." or "..")
            safeRunId = "_";

        return Path.GetFullPath(Path.Combine(_runsRoot, safeRunId));
    }

    private static bool IsUnderDirectory(string directory, string candidatePath)
    {
        var relative = Path.GetRelativePath(directory, candidatePath);
        return relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    private static string? FindReportPath(string runDirectory)
    {
        var candidates = new[]
        {
            "builder-repair-loop-report.json",
            "build-run-report.json",
            "report.json",
            "test-agent-report.json"
        };

        return candidates.Select(name => Path.Combine(runDirectory, name)).FirstOrDefault(File.Exists);
    }

    private static IReadOnlyList<RunStageStatus> ReadStages(JsonElement root)
    {
        var stages = ReadArray(root, "StageStatuses", "stageStatuses", "stage_statuses");
        return stages.Select(stage => new RunStageStatus
        {
            StageName = ReadString(stage, "StageName") ?? ReadString(stage, "stageName") ?? "",
            AgentName = ReadString(stage, "AgentName") ?? ReadString(stage, "agentName") ?? "",
            Status = ReadString(stage, "Status") ?? ReadString(stage, "status") ?? "",
            Summary = ReadString(stage, "Summary") ?? ReadString(stage, "summary") ?? ""
        }).ToArray();
    }

    private static IReadOnlyList<RunAttemptSummary> ReadAttempts(JsonElement root)
    {
        var buildAttempts = ReadArray(root, "BuildAttempts", "buildAttempts")
            .Select(attempt => MapAttempt(attempt, "Build"));
        var testAttempts = ReadArray(root, "TestAttempts", "testAttempts")
            .Select(attempt => MapAttempt(attempt, "Test"));

        return buildAttempts.Concat(testAttempts)
            .OrderBy(attempt => attempt.AttemptNumber)
            .ThenBy(attempt => attempt.Type)
            .ToArray();
    }

    private static RunAttemptSummary MapAttempt(JsonElement attempt, string type) => new()
    {
        AttemptNumber = ReadInt(attempt, "AttemptNumber", "attemptNumber"),
        Type = type,
        Status = ReadString(attempt, "Status") ?? ReadString(attempt, "status") ?? "",
        FailureClassification = ReadString(attempt, "FailureClassification") ?? ReadString(attempt, "failureClassification") ?? "",
        Summary = BuildAttemptSummary(attempt, type)
    };

    private static IReadOnlyList<RunRepairSummary> ReadRepairs(JsonElement root)
    {
        return ReadArray(root, "RepairAttempts", "repairAttempts")
            .Select(repair => new RunRepairSummary
            {
                RepairAttemptNumber = ReadInt(repair, "RepairAttemptNumber", "repairAttemptNumber"),
                TriggerFailureClassification = ReadString(repair, "TriggerFailureClassification") ?? ReadString(repair, "triggerFailureClassification") ?? "",
                PlannedFix = ReadString(repair, "PlannedFix") ?? ReadString(repair, "plannedFix") ?? "",
                Status = ReadString(repair, "Status") ?? ReadString(repair, "status") ?? "",
                RetryBudgetRemaining = ReadInt(repair, "RetryBudgetRemaining", "retryBudgetRemaining")
            })
            .ToArray();
    }

    private static IReadOnlyList<RunEvidenceItem> ReadEvidenceRefs(JsonElement root)
    {
        return ReadArray(root, "EvidenceRefs", "evidenceRefs", "evidence")
            .Select(item => new RunEvidenceItem
            {
                Type = ReadString(item, "Type") ?? ReadString(item, "type") ?? "",
                Path = ReadString(item, "Path") ?? ReadString(item, "path") ?? "",
                Summary = ReadString(item, "Summary") ?? ReadString(item, "summary") ?? ""
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Path) || !string.IsNullOrWhiteSpace(item.Summary))
            .ToArray();
    }

    private static string BuildAttemptSummary(JsonElement attempt, string type)
    {
        var status = ReadString(attempt, "Status") ?? ReadString(attempt, "status") ?? "Unknown";
        var classification = ReadString(attempt, "FailureClassification") ?? ReadString(attempt, "failureClassification") ?? "";
        var command = ReadString(attempt, "Command") ?? ReadString(attempt, "command") ?? type;
        return string.IsNullOrWhiteSpace(classification)
            ? $"{type} {status}: {command}"
            : $"{type} {status}: {classification}";
    }

    private static string InferProject(JsonElement root)
    {
        var title = ReadString(root, "Title") ?? ReadString(root, "title") ?? "";
        return title.Contains("Solitaire", StringComparison.OrdinalIgnoreCase) ? "Solitaire" : "IronDev";
    }

    private static string ReadWorkspacePath(JsonElement root)
    {
        var mutation = ReadElement(root, "WorkspaceMutation", "workspaceMutation", "mutation");
        return ReadString(mutation, "WorkspacePath") ??
               ReadString(mutation, "workspacePath") ??
               ReadString(mutation, "disposableWorkspacePath") ??
               "";
    }

    private static bool IsEvidenceLike(string runDirectory, string path)
    {
        var relative = Path.GetRelativePath(runDirectory, path);
        return relative.StartsWith("evidence", StringComparison.OrdinalIgnoreCase) ||
               relative.StartsWith("logs", StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileName(path).EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileName(path).EndsWith(".md", StringComparison.OrdinalIgnoreCase);
    }

    private static string ClassifyEvidence(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return "ReportMarkdown";
        if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            return "Log";
        if (name.Contains("hash", StringComparison.OrdinalIgnoreCase))
            return "HashProof";
        return "Evidence";
    }

    private static IReadOnlyList<JsonElement> ReadArray(JsonElement root, params string[] names)
    {
        var element = ReadElement(root, names);
        return element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().Select(item => item.Clone()).ToArray() : [];
    }

    private static JsonElement ReadElement(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return default;

        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var direct))
                return direct.Clone();
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    return property.Value.Clone();
            }
        }

        return default;
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        var element = ReadElement(root, names);
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int ReadInt(JsonElement root, params string[] names)
    {
        var element = ReadElement(root, names);
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) ? value : 0;
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
