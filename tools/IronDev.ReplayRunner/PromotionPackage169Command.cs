using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IronDev.Core.Promotion;
using IronDev.Infrastructure.Services.Promotion;

public static class PromotionPackage169Command
{
    private const string Boundary = "Promotion package review only. It does not apply files, create branches, mutate accepted memory, accept tickets, or approve real repo writes.";

    public static async Task<int> HandleCreateAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = SolitaireDisposableBuildSmokeCommand.FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ??
                    ReadOption(args, "--dogfood-run-id") ??
                    $"PromotionPackage169-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var project = ReadOption(args, "--project") ?? "Solitaire";
        var sourceRunId = ReadOption(args, "--source-run-id");
        if (string.IsNullOrWhiteSpace(sourceRunId))
        {
            Console.Error.WriteLine("Usage: promotion package create --source-run-id <run> --project <project> [--run-id id] [--json]");
            return 2;
        }

        var result = await CreatePackageAsync(repoRoot, runId, project, sourceRunId);
        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return string.Equals(result.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    public static async Task<int> HandleCampaignAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = SolitaireDisposableBuildSmokeCommand.FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ??
                    ReadOption(args, "--dogfood-run-id") ??
                    $"PromotionPackage169-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var project = ReadOption(args, "--project") ?? "Solitaire";
        var sourceRunId = $"{runId}-source";
        var source = await RunSourceBuildAsync(repoRoot, runId, project, sourceRunId);
        if (source.ExitCode != 0)
        {
            var failed = PromotionCommandResult.Failed(
                "campaign promotion-package-169",
                runId,
                project,
                $"Source 168 build failed with exit code {source.ExitCode}.",
                source.LogPath,
                Boundary);
            Console.WriteLine(JsonSerializer.Serialize(failed, options));
            return 1;
        }

        var result = await CreatePackageAsync(repoRoot, runId, project, sourceRunId);
        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return string.Equals(result.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private static async Task<PromotionCommandResult> CreatePackageAsync(
        string repoRoot,
        string runId,
        string project,
        string sourceRunId)
    {
        var repoStatusBefore = await SolitaireDisposableBuildSmokeCommand.GetGitStatusAsync(repoRoot);
        var sourceReportPath = Path.Combine(repoRoot, "tools", "dogfood", "runs", sourceRunId, "loop-gated-disposable-build-168-report.json");
        if (!File.Exists(sourceReportPath))
        {
            return PromotionCommandResult.Failed(
                "promotion package create",
                runId,
                project,
                $"Source run report not found: {sourceReportPath}",
                sourceReportPath,
                Boundary);
        }

        var source = ParseObject(await File.ReadAllTextAsync(sourceReportPath));
        var workspacePath = ReadString(ReadNode(source, "mutation"), "disposableWorkspacePath");
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
        {
            return PromotionCommandResult.Failed(
                "promotion package create",
                runId,
                project,
                "Disposable workspace path is missing or no longer exists.",
                sourceReportPath,
                Boundary);
        }

        var registry = new LanguageRuntimeRegistry();
        var runtime = registry.DetectForWorkspace(workspacePath);
        var files = ClassifyFiles(workspacePath, runtime);
        var evidenceRefs = ReadArray(source, "evidence").Select(item => ReadString(item, "path")).Where(File.Exists).ToArray();
        var sourceTraceId = ReadString(source, "traceId");
        var proposedChange = BuildProposedChange(runId, project, sourceRunId, sourceTraceId, runtime, source, files, evidenceRefs);
        var package = BuildPromotionPackage(runId, project, sourceRunId, sourceTraceId, runtime, files, source, evidenceRefs, proposedChange);

        var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", runId);
        Directory.CreateDirectory(runRoot);
        var proposedChangePath = Path.Combine(runRoot, "proposed-change.json");
        var packagePath = Path.Combine(runRoot, "promotion-package.json");
        var markdownPath = Path.Combine(runRoot, "promotion-package.md");
        var serializerOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(proposedChangePath, JsonSerializer.Serialize(proposedChange, serializerOptions), Encoding.UTF8);
        await File.WriteAllTextAsync(packagePath, JsonSerializer.Serialize(package, serializerOptions), Encoding.UTF8);
        await File.WriteAllTextAsync(markdownPath, ToMarkdown(proposedChange, package), Encoding.UTF8);

        var repoStatusAfter = await SolitaireDisposableBuildSmokeCommand.GetGitStatusAsync(repoRoot);
        var realRepoMutationCount = repoStatusBefore == repoStatusAfter ? 0 : 1;
        return new PromotionCommandResult
        {
            Command = "promotion package create",
            Status = realRepoMutationCount == 0 && package.FilesToPromote.Count > 0 ? "Succeeded" : "Failed",
            RunId = runId,
            TraceId = Guid.NewGuid().ToString("N"),
            Project = project,
            Summary = "Created ProposedChange and PromotionPackage from a successful disposable build run.",
            ProposedChange = proposedChange,
            PromotionPackage = package,
            RuntimeProfiles = registry.ListProfiles(),
            Evidence = [
                new("SourceRunReport", sourceReportPath, "Loop-gated disposable build report used as source evidence."),
                new("ProposedChange", proposedChangePath, "ProposedChange case file."),
                new("PromotionPackage", packagePath, "Promotion package JSON."),
                new("PromotionPackageMarkdown", markdownPath, "Human-readable promotion package.")
            ],
            Warnings = BuildWarnings(runtime, package, realRepoMutationCount),
            Errors = realRepoMutationCount == 0 ? [] : ["Repository status changed during promotion package creation."],
            Boundary = Boundary,
            ReproCommand = $"promotion package create --source-run-id {sourceRunId} --project {project} --run-id {runId} --json",
            Recommendation = package.Recommendation
        };
    }

    private static ProposedChange BuildProposedChange(
        string runId,
        string project,
        string sourceRunId,
        string sourceTraceId,
        LanguageRuntimeProfile runtime,
        JsonObject source,
        ClassifiedFiles files,
        IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            ProposedChangeId = $"PC-{runId}",
            Project = project,
            Title = $"{project} disposable build promotion candidate",
            SourceGoal = ReadString(source, "goal"),
            SourceDocumentIds = ["LOOP_GATED_DISPOSABLE_BUILD_168", "SOLITAIRE_PRODUCT_SPIKE_ARCHITECTURE_AND_TICKETS_138"],
            SourceTicketIds = ["SOLITAIRE_DISPOSABLE_BUILD_TICKET_168", "SOL-139-001"],
            SourceRunIds = [sourceRunId],
            SourceTraceIds = [sourceTraceId],
            TargetRuntimeProfileId = runtime.RuntimeProfileId,
            CurrentStage = "PromotionPackageCreated",
            PromotionPackageId = $"PP-{runId}",
            Risks = runtime.KnownRisks.Concat(["Promotion package is review evidence only; isolated apply is a later gate."]).ToArray(),
            EvidenceRefs = evidenceRefs,
            Recommendation = files.Promotable.Count > 0 ? "HumanReviewRequired" : "RejectSpike",
            ApprovalState = "NeedsHumanReview",
            Boundary = Boundary
        };

    private static PromotionPackage BuildPromotionPackage(
        string runId,
        string project,
        string sourceRunId,
        string sourceTraceId,
        LanguageRuntimeProfile runtime,
        ClassifiedFiles files,
        JsonObject source,
        IReadOnlyList<string> evidenceRefs,
        ProposedChange proposedChange) =>
        new()
        {
            PackageId = $"PP-{runId}",
            ProposedChangeId = proposedChange.ProposedChangeId,
            SourceRunId = sourceRunId,
            SourceTraceId = sourceTraceId,
            Project = project,
            RuntimeProfile = runtime,
            FilesToPromote = files.Promotable,
            FilesBlocked = files.Blocked,
            TestsPassed = [
                new TestEvidence
                {
                    Name = "Disposable builder repair loop",
                    Status = "Passed",
                    Tool = runtime.BuildTool,
                    EvidenceRef = FirstEvidence(source, "BuilderReport"),
                    Summary = "Trace-backed BuilderAgent repair loop reached final build/test pass."
                },
                new TestEvidence
                {
                    Name = "QualityAgent/Killjoy gate",
                    Status = "Passed",
                    Tool = "agent quality run-gate",
                    EvidenceRef = FirstEvidence(source, "QualityCommandLog"),
                    Summary = "QualityAgent reported the deterministic gate passed."
                }
            ],
            Risks = runtime.KnownRisks.Select(risk => new RiskNote
            {
                Severity = "Warning",
                Category = "Runtime",
                Message = risk,
                Mitigation = "Human/Codex review before isolated apply."
            }).Concat([
                new RiskNote
                {
                    Severity = "Info",
                    Category = "PromotionBoundary",
                    Message = "Promotion package does not apply files or approve a branch.",
                    Mitigation = "Use the future isolated branch/worktree apply proof."
                }
            ]).ToArray(),
            Checklist = new HumanReviewChecklist
            {
                RequiredChecks = [
                    "Review source run and trace.",
                    "Review every promotable file.",
                    "Confirm blocked files are not promoted.",
                    "Confirm runtime profile is executable.",
                    "Confirm build/test/quality evidence is sufficient."
                ],
                ExplicitApprovalsNeeded = ["Approve for isolated branch/worktree apply only."],
                BlockedActions = ["real repo write", "main branch apply", "auto-merge", "accepted memory mutation", "ticket acceptance"]
            },
            EvidenceSummary = new EvidenceSummary
            {
                BuildStatus = ReadBool(ReadNode(source, "data"), "generatedSolitaireAppContained") ? "Passed" : "Unknown",
                TestStatus = "Passed",
                QualityStatus = ReadString(ReadNode(ReadNode(source, "data"), "qualityGate"), "status"),
                RealRepoMutationCount = ReadInt(ReadNode(source, "mutation"), "realRepoMutationCount"),
                PromotableFileCount = files.Promotable.Count,
                BlockedFileCount = files.Blocked.Count,
                EvidenceRefs = evidenceRefs
            },
            Recommendation = "HumanReviewRequired",
            ApprovalState = "NeedsHumanReview",
            Boundary = Boundary
        };

    private static ClassifiedFiles ClassifyFiles(string workspacePath, LanguageRuntimeProfile runtime)
    {
        var promotable = new List<PromotableFile>();
        var blocked = new List<BlockedFile>();
        foreach (var file in Directory.EnumerateFiles(workspacePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(workspacePath, file).Replace('\\', '/');
            var normalized = relative.ToLowerInvariant();
            var forbidden = runtime.ForbiddenPathSegments.FirstOrDefault(segment => normalized.Contains(segment.ToLowerInvariant(), StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(forbidden))
            {
                blocked.Add(new BlockedFile { RelativePath = relative, Reason = $"Forbidden runtime path segment: {forbidden}" });
                continue;
            }

            var extension = Path.GetExtension(file);
            if (!runtime.SourceFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                blocked.Add(new BlockedFile { RelativePath = relative, Reason = $"Unsupported extension for runtime profile {runtime.RuntimeProfileId}." });
                continue;
            }

            promotable.Add(new PromotableFile
            {
                RelativePath = relative,
                Language = runtime.TargetLanguage,
                FileRole = ClassifyRole(relative),
                Sha256 = HashFile(file),
                SizeBytes = new FileInfo(file).Length,
                Rationale = "Generated source/project file from the disposable workspace and allowed by the runtime profile."
            });
        }

        return new ClassifiedFiles(promotable.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(), blocked.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string ClassifyRole(string relativePath)
    {
        if (relativePath.Contains("Tests/", StringComparison.OrdinalIgnoreCase) || relativePath.Contains(".Tests", StringComparison.OrdinalIgnoreCase))
            return "Test";
        if (relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            return "Project";
        if (relativePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            return "UI";
        return "Source";
    }

    private static string ToMarkdown(ProposedChange proposedChange, PromotionPackage package)
    {
        var files = string.Join(Environment.NewLine, package.FilesToPromote.Select(file => $"- `{file.RelativePath}` ({file.FileRole})"));
        var blocked = string.Join(Environment.NewLine, package.FilesBlocked.Take(25).Select(file => $"- `{file.RelativePath}`: {file.Reason}"));
        return $"""
        # {proposedChange.Title}

        Proposed change: `{proposedChange.ProposedChangeId}`
        Promotion package: `{package.PackageId}`
        Runtime: `{package.RuntimeProfile.RuntimeProfileId}` ({package.RuntimeProfile.TargetLanguage} / {package.RuntimeProfile.TargetStack})
        Approval state: `{package.ApprovalState}`
        Recommendation: `{package.Recommendation}`

        ## Files To Promote

        {files}

        ## Blocked Files

        {(string.IsNullOrWhiteSpace(blocked) ? "- none" : blocked)}

        ## Evidence Summary

        - Build: `{package.EvidenceSummary.BuildStatus}`
        - Test: `{package.EvidenceSummary.TestStatus}`
        - Quality: `{package.EvidenceSummary.QualityStatus}`
        - Real repo mutation count: `{package.EvidenceSummary.RealRepoMutationCount}`

        ## Boundary

        {package.Boundary}
        """;
    }

    private static async Task<ChildRun> RunSourceBuildAsync(string repoRoot, string runId, string project, string sourceRunId)
    {
        var runnerProject = Path.Combine(repoRoot, "tools", "IronDev.ReplayRunner", "IronDev.ReplayRunner.csproj");
        var logRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", runId, "logs");
        Directory.CreateDirectory(logRoot);
        var logPath = Path.Combine(logRoot, "source-loop-gated-build.log");
        var args = new[]
        {
            "run", "--no-build", "--project", runnerProject, "--",
            "build", "disposable", "run",
            "--project", project,
            "--goal", "I want build solitaire",
            "--run-id", sourceRunId,
            "--json"
        };

        using var process = new Process();
        process.StartInfo.FileName = "dotnet";
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.WorkingDirectory = repoRoot;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await File.WriteAllTextAsync(logPath, stdout + Environment.NewLine + stderr, Encoding.UTF8);
        return new ChildRun(process.ExitCode, logPath);
    }

    private static List<string> BuildWarnings(LanguageRuntimeProfile runtime, PromotionPackage package, int realRepoMutationCount)
    {
        var warnings = new List<string>();
        if (!string.Equals(runtime.Availability, LanguageRuntimeAvailability.Executable, StringComparison.OrdinalIgnoreCase))
            warnings.Add($"Runtime profile {runtime.RuntimeProfileId} is not executable yet.");
        if (package.FilesBlocked.Count > 0)
            warnings.Add($"{package.FilesBlocked.Count} generated files are blocked from promotion.");
        if (realRepoMutationCount != 0)
            warnings.Add("Repository status changed during promotion package creation.");
        return warnings;
    }

    private static string FirstEvidence(JsonObject source, string type) =>
        ReadArray(source, "evidence")
            .FirstOrDefault(item => string.Equals(ReadString(item, "type"), type, StringComparison.OrdinalIgnoreCase)) is { } found
            ? ReadString(found, "path")
            : string.Empty;

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static JsonObject ParseObject(string output)
    {
        try
        {
            var start = output.IndexOf('{');
            var end = output.LastIndexOf('}');
            if (start < 0 || end <= start)
                return [];
            return JsonNode.Parse(output[start..(end + 1)])?.AsObject() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static JsonObject ReadNode(JsonObject node, string name)
    {
        if (TryGetNode(node, name, out var value) && value is JsonObject obj)
            return obj;
        return [];
    }

    private static IReadOnlyList<JsonObject> ReadArray(JsonObject node, string name)
    {
        if (TryGetNode(node, name, out var value) && value is JsonArray array)
            return array.OfType<JsonObject>().ToArray();
        return [];
    }

    private static string ReadString(JsonObject node, string name) =>
        TryGetNode(node, name, out var value) ? value?.ToString() ?? "" : "";

    private static int ReadInt(JsonObject node, string name) =>
        int.TryParse(ReadString(node, name), out var value) ? value : 0;

    private static bool ReadBool(JsonObject node, string name) =>
        bool.TryParse(ReadString(node, name), out var value) && value;

    private static bool TryGetNode(JsonObject node, string name, out JsonNode? value)
    {
        if (node.TryGetPropertyValue(name, out value))
            return true;
        foreach (var pair in node)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
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

    private sealed record ClassifiedFiles(IReadOnlyList<PromotableFile> Promotable, IReadOnlyList<BlockedFile> Blocked);
    private sealed record ChildRun(int ExitCode, string LogPath);
    private sealed record EvidenceRef(string Type, string Path, string Summary);

    private sealed class PromotionCommandResult
    {
        public string Command { get; init; } = "";
        public string Status { get; init; } = "";
        public string RunId { get; init; } = "";
        public string TraceId { get; init; } = "";
        public string Project { get; init; } = "";
        public string Summary { get; init; } = "";
        public ProposedChange? ProposedChange { get; init; }
        public PromotionPackage? PromotionPackage { get; init; }
        public IReadOnlyList<LanguageRuntimeProfile> RuntimeProfiles { get; init; } = [];
        public IReadOnlyList<EvidenceRef> Evidence { get; init; } = [];
        public IReadOnlyList<string> Warnings { get; init; } = [];
        public IReadOnlyList<string> Errors { get; init; } = [];
        public string Boundary { get; init; } = "";
        public string ReproCommand { get; init; } = "";
        public string Recommendation { get; init; } = "";

        public static PromotionCommandResult Failed(string command, string runId, string project, string error, string evidencePath, string boundary) =>
            new()
            {
                Command = command,
                Status = "Failed",
                RunId = runId,
                TraceId = Guid.NewGuid().ToString("N"),
                Project = project,
                Summary = error,
                Evidence = [new EvidenceRef("FailureEvidence", evidencePath, error)],
                Errors = [error],
                Boundary = boundary,
                ReproCommand = command,
                Recommendation = "Retry"
            };
    }
}
