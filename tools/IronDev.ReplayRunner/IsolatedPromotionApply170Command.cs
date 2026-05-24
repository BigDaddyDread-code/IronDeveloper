using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IronDev.Core.Promotion;
using IronDev.Infrastructure.Services.Promotion;

public static class IsolatedPromotionApply170Command
{
    private const string Boundary = "Isolated promotion apply proof only. It copies promotable files into an explicit isolated candidate workspace outside the active repo, runs build/test there, and does not write main, mutate accepted memory, accept tickets, auto-merge, or self-approve.";

    public static async Task<int> HandleApplyAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = SolitaireDisposableBuildSmokeCommand.FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ??
                    ReadOption(args, "--dogfood-run-id") ??
                    $"IsolatedPromotionApply170-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var packageRunId = ReadOption(args, "--package-run-id");
        if (string.IsNullOrWhiteSpace(packageRunId))
        {
            Console.Error.WriteLine("Usage: promotion apply isolated --package-run-id <run> [--run-id id] [--json]");
            return 2;
        }

        var result = await ApplyAsync(repoRoot, runId, packageRunId);
        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return string.Equals(result.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    public static async Task<int> HandleCampaignAsync(string[] args, JsonSerializerOptions options)
    {
        var repoRoot = SolitaireDisposableBuildSmokeCommand.FindRepositoryRoot();
        var runId = ReadOption(args, "--run-id") ??
                    ReadOption(args, "--dogfood-run-id") ??
                    $"IsolatedPromotionApply170-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var packageRunId = $"{runId}-package";
        var packageRun = await RunPackageCampaignAsync(repoRoot, runId, packageRunId);
        if (packageRun.ExitCode != 0)
        {
            var failed = Failed(runId, "Solitaire", $"Promotion package source campaign failed with exit code {packageRun.ExitCode}.", packageRun.LogPath);
            Console.WriteLine(JsonSerializer.Serialize(failed, options));
            return 1;
        }

        var result = await ApplyAsync(repoRoot, runId, packageRunId);
        Console.WriteLine(JsonSerializer.Serialize(result, options));
        return string.Equals(result.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private static async Task<IsolatedPromotionApplyReport> ApplyAsync(string repoRoot, string runId, string packageRunId)
    {
        var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", runId);
        var logsRoot = Path.Combine(runRoot, "logs");
        Directory.CreateDirectory(logsRoot);

        var packagePath = Path.Combine(repoRoot, "tools", "dogfood", "runs", packageRunId, "promotion-package.json");
        var proposedChangePath = Path.Combine(repoRoot, "tools", "dogfood", "runs", packageRunId, "proposed-change.json");
        if (!File.Exists(packagePath))
            return Failed(runId, "Unknown", $"Promotion package not found: {packagePath}", packagePath);

        var serializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
        var package = JsonSerializer.Deserialize<PromotionPackage>(await File.ReadAllTextAsync(packagePath), serializerOptions);
        if (package is null)
            return Failed(runId, "Unknown", $"Promotion package could not be parsed: {packagePath}", packagePath);

        var proposedChange = File.Exists(proposedChangePath)
            ? JsonSerializer.Deserialize<ProposedChange>(await File.ReadAllTextAsync(proposedChangePath), serializerOptions)
            : null;

        var sourceReportPath = Path.Combine(repoRoot, "tools", "dogfood", "runs", package.SourceRunId, "loop-gated-disposable-build-168-report.json");
        var sourceReport = File.Exists(sourceReportPath)
            ? ParseObject(await File.ReadAllTextAsync(sourceReportPath))
            : [];
        var sourceWorkspace = ReadString(ReadNode(sourceReport, "mutation"), "disposableWorkspacePath");
        if (string.IsNullOrWhiteSpace(sourceWorkspace) || !Directory.Exists(sourceWorkspace))
            return Failed(runId, package.Project, "Source disposable workspace is missing or no longer exists.", sourceReportPath);

        var runtime = new LanguageRuntimeRegistry().GetRequired(package.RuntimeProfile.RuntimeProfileId);
        if (!string.Equals(runtime.Availability, LanguageRuntimeAvailability.Executable, StringComparison.OrdinalIgnoreCase))
            return Failed(runId, package.Project, $"Runtime profile {runtime.RuntimeProfileId} is not executable yet.", packagePath);

        var activeRepoStatusBefore = await SolitaireDisposableBuildSmokeCommand.GetGitStatusAsync(repoRoot);
        var isolatedWorkspace = PrepareIsolatedWorkspace(runId);
        var branchName = $"isolated/{SanitizeBranchSegment(runId)}";
        var gitInit = await RunProcessAsync("git", ["init", "-b", branchName], isolatedWorkspace, Path.Combine(logsRoot, "isolated-git-init.log"));
        var appliedFiles = await CopyPromotableFilesAsync(package, sourceWorkspace, isolatedWorkspace);
        var forbiddenTouched = package.FilesBlocked
            .Select(file => Path.Combine(isolatedWorkspace, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Where(File.Exists)
            .Select(file => Path.GetRelativePath(isolatedWorkspace, file).Replace('\\', '/'))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var manifestPath = Path.Combine(runRoot, "isolated-workspace-manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            runId,
            packageRunId,
            isolatedWorkspace,
            branchName,
            package.PackageId,
            package.ProposedChangeId,
            filesApplied = appliedFiles.Count
        }, serializerOptions), Encoding.UTF8);

        var build = await RunDotNetBuildAsync(runtime, isolatedWorkspace, logsRoot);
        var test = await RunDotNetTestAsync(runtime, isolatedWorkspace, logsRoot);
        var activeRepoStatusAfter = await SolitaireDisposableBuildSmokeCommand.GetGitStatusAsync(repoRoot);
        var activeRepoMutationCount = activeRepoStatusBefore == activeRepoStatusAfter ? 0 : 1;
        var status = gitInit.ExitCode == 0 &&
                     build.ExitCode == 0 &&
                     test.ExitCode == 0 &&
                     activeRepoMutationCount == 0 &&
                     forbiddenTouched.Length == 0 &&
                     appliedFiles.Count == package.FilesToPromote.Count &&
                     appliedFiles.All(file => file.HashMatchesPackage)
            ? "Succeeded"
            : "Failed";

        var report = new IsolatedPromotionApplyReport
        {
            Command = "promotion apply isolated",
            Status = status,
            RunId = runId,
            TraceId = Guid.NewGuid().ToString("N"),
            Project = package.Project,
            PackageId = package.PackageId,
            ProposedChangeId = package.ProposedChangeId,
            SourceRunId = package.SourceRunId,
            SourceTraceId = package.SourceTraceId,
            IsolatedWorkspacePath = isolatedWorkspace,
            IsolatedBranchName = branchName,
            RuntimeProfile = runtime,
            AppliedFiles = appliedFiles,
            RejectedBlockedFiles = package.FilesBlocked,
            Build = build.ToEvidence(),
            Test = test.ToEvidence(),
            Mutation = new PromotionMutationReport
            {
                ActiveRepoMutationAllowed = false,
                ActiveRepoMutationCount = activeRepoMutationCount,
                IsolatedWorkspaceMutationAllowed = true,
                ActiveRepoStatusBefore = activeRepoStatusBefore,
                ActiveRepoStatusAfter = activeRepoStatusAfter,
                IsolatedWorkspacePath = isolatedWorkspace,
                IsolatedFilesChanged = appliedFiles.Count,
                ForbiddenPathsTouched = forbiddenTouched
            },
            Evidence = [
                new PromotionEvidenceRef("PromotionPackage", packagePath, "Promotion package consumed by isolated apply."),
                new PromotionEvidenceRef("ProposedChange", proposedChangePath, "ProposedChange case file."),
                new PromotionEvidenceRef("SourceRunReport", sourceReportPath, "Disposable build source report."),
                new PromotionEvidenceRef("IsolatedWorkspaceManifest", manifestPath, "Manifest for the isolated candidate workspace."),
                new PromotionEvidenceRef("BuildLog", build.LogPath, "Runtime build log from isolated workspace."),
                new PromotionEvidenceRef("TestLog", test.LogPath, "Runtime test log from isolated workspace.")
            ],
            Warnings = BuildWarnings(proposedChange, package, activeRepoMutationCount, forbiddenTouched),
            Errors = status == "Succeeded" ? [] : BuildErrors(gitInit, build, test, activeRepoMutationCount, forbiddenTouched, appliedFiles, package),
            Recommendation = status == "Succeeded" ? "ReviewIsolatedCandidate" : "Retry",
            ApprovalState = "NeedsHumanReview",
            Boundary = Boundary,
            ReproCommand = $"promotion apply isolated --package-run-id {packageRunId} --run-id {runId} --json"
        };

        var reportPath = Path.Combine(runRoot, "isolated-promotion-apply-report.json");
        var markdownPath = Path.Combine(runRoot, "isolated-promotion-apply-report.md");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, serializerOptions), Encoding.UTF8);
        await File.WriteAllTextAsync(markdownPath, ToMarkdown(report), Encoding.UTF8);

        return report;
    }

    private static async Task<IReadOnlyList<AppliedPromotionFile>> CopyPromotableFilesAsync(
        PromotionPackage package,
        string sourceWorkspace,
        string isolatedWorkspace)
    {
        var applied = new List<AppliedPromotionFile>();
        foreach (var file in package.FilesToPromote)
        {
            var source = Path.GetFullPath(Path.Combine(sourceWorkspace, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            var destination = Path.GetFullPath(Path.Combine(isolatedWorkspace, file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsUnderDirectory(source, sourceWorkspace))
                throw new InvalidOperationException($"Promotable source escapes source workspace: {file.RelativePath}");
            if (!IsUnderDirectory(destination, isolatedWorkspace))
                throw new InvalidOperationException($"Promotable destination escapes isolated workspace: {file.RelativePath}");
            if (!File.Exists(source))
                throw new FileNotFoundException($"Promotable source file not found: {source}", source);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            var appliedHash = HashFile(destination);
            applied.Add(new AppliedPromotionFile
            {
                RelativePath = file.RelativePath,
                Language = file.Language,
                FileRole = file.FileRole,
                SourceSha256 = file.Sha256,
                AppliedSha256 = appliedHash,
                SizeBytes = new FileInfo(destination).Length,
                HashMatchesPackage = string.Equals(file.Sha256, appliedHash, StringComparison.OrdinalIgnoreCase)
            });
        }

        return applied.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string PrepareIsolatedWorkspace(string runId)
    {
        var root = Path.Combine(Path.GetTempPath(), "IronDev-IsolatedPromotionApply");
        var workspace = Path.GetFullPath(Path.Combine(root, runId));
        var fullRoot = Path.GetFullPath(root);
        if (!IsUnderDirectory(workspace, fullRoot))
            throw new InvalidOperationException("Computed isolated workspace escaped the approved temp root.");
        if (Directory.Exists(workspace))
            Directory.Delete(workspace, recursive: true);
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static async Task<ProcessRun> RunDotNetBuildAsync(LanguageRuntimeProfile runtime, string workspace, string logsRoot)
    {
        var project = SelectBuildProject(workspace);
        var command = $"{runtime.BuildTool} {Path.GetRelativePath(workspace, project)}";
        return await RunProcessAsync("dotnet", ["build", project, "-p:UseSharedCompilation=false", "-nr:false"], workspace, Path.Combine(logsRoot, "isolated-build.log"), command);
    }

    private static async Task<ProcessRun> RunDotNetTestAsync(LanguageRuntimeProfile runtime, string workspace, string logsRoot)
    {
        var project = SelectTestProject(workspace);
        var command = $"{runtime.TestTool} {Path.GetRelativePath(workspace, project)}";
        return await RunProcessAsync("dotnet", ["test", project, "-p:UseSharedCompilation=false", "-nr:false"], workspace, Path.Combine(logsRoot, "isolated-test.log"), command);
    }

    private static string SelectBuildProject(string workspace)
    {
        var projects = Directory.EnumerateFiles(workspace, "*.csproj", SearchOption.AllDirectories).ToArray();
        return projects.FirstOrDefault(path => File.ReadAllText(path).Contains("<UseWPF>true</UseWPF>", StringComparison.OrdinalIgnoreCase)) ??
               projects.FirstOrDefault(path => !path.Contains(".Tests", StringComparison.OrdinalIgnoreCase)) ??
               projects.FirstOrDefault() ??
               throw new FileNotFoundException("No .csproj found in isolated workspace.");
    }

    private static string SelectTestProject(string workspace) =>
        Directory.EnumerateFiles(workspace, "*.csproj", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains(".Tests", StringComparison.OrdinalIgnoreCase)) ??
        throw new FileNotFoundException("No test .csproj found in isolated workspace.");

    private static async Task<ChildRun> RunPackageCampaignAsync(string repoRoot, string runId, string packageRunId)
    {
        var runnerProject = Path.Combine(repoRoot, "tools", "IronDev.ReplayRunner", "IronDev.ReplayRunner.csproj");
        var logRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", runId, "logs");
        Directory.CreateDirectory(logRoot);
        var logPath = Path.Combine(logRoot, "promotion-package-campaign.log");
        var run = await RunProcessAsync("dotnet",
            ["run", "--no-build", "--project", runnerProject, "--", "campaign", "promotion-package-169", "--run-id", packageRunId, "--json"],
            repoRoot,
            logPath);
        return new ChildRun(run.ExitCode, logPath);
    }

    private static async Task<ProcessRun> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        string logPath,
        string? displayCommand = null)
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        await File.WriteAllTextAsync(logPath, stdout + Environment.NewLine + stderr, Encoding.UTF8);
        return new ProcessRun(displayCommand ?? $"{fileName} {string.Join(" ", args)}", process.ExitCode, logPath, stdout, stderr);
    }

    private static IsolatedPromotionApplyReport Failed(string runId, string project, string error, string evidencePath) =>
        new()
        {
            Command = "promotion apply isolated",
            Status = "Failed",
            RunId = runId,
            TraceId = Guid.NewGuid().ToString("N"),
            Project = project,
            PackageId = "",
            ProposedChangeId = "",
            SourceRunId = "",
            SourceTraceId = "",
            IsolatedWorkspacePath = "",
            IsolatedBranchName = "",
            RuntimeProfile = new LanguageRuntimeRegistry().GetRequired("csharp-dotnet"),
            Build = new RuntimeCommandEvidence { Command = "", ExitCode = 1, Status = "Failed", LogPath = "", Summary = "Build not run." },
            Test = new RuntimeCommandEvidence { Command = "", ExitCode = 1, Status = "Failed", LogPath = "", Summary = "Test not run." },
            Mutation = new PromotionMutationReport
            {
                ActiveRepoMutationAllowed = false,
                ActiveRepoMutationCount = 0,
                IsolatedWorkspaceMutationAllowed = false,
                ActiveRepoStatusBefore = "",
                ActiveRepoStatusAfter = "",
                IsolatedWorkspacePath = "",
                IsolatedFilesChanged = 0
            },
            Evidence = [new PromotionEvidenceRef("FailureEvidence", evidencePath, error)],
            Errors = [error],
            Recommendation = "Retry",
            ApprovalState = "NeedsHumanReview",
            Boundary = Boundary,
            ReproCommand = "promotion apply isolated --package-run-id <run> --json"
        };

    private static List<string> BuildWarnings(
        ProposedChange? proposedChange,
        PromotionPackage package,
        int activeRepoMutationCount,
        IReadOnlyList<string> forbiddenTouched)
    {
        var warnings = new List<string>();
        if (proposedChange is null)
            warnings.Add("ProposedChange file was missing; package still carried the proposed change id.");
        if (!string.Equals(package.ApprovalState, "NeedsHumanReview", StringComparison.OrdinalIgnoreCase))
            warnings.Add($"Promotion package approval state was {package.ApprovalState}.");
        if (activeRepoMutationCount != 0)
            warnings.Add("Active repo status changed during isolated apply proof.");
        if (forbiddenTouched.Count > 0)
            warnings.Add("One or more blocked files appeared inside the isolated workspace.");
        return warnings;
    }

    private static List<string> BuildErrors(
        ProcessRun gitInit,
        ProcessRun build,
        ProcessRun test,
        int activeRepoMutationCount,
        IReadOnlyList<string> forbiddenTouched,
        IReadOnlyList<AppliedPromotionFile> appliedFiles,
        PromotionPackage package)
    {
        var errors = new List<string>();
        if (gitInit.ExitCode != 0)
            errors.Add("Isolated workspace git branch initialization failed.");
        if (build.ExitCode != 0)
            errors.Add("Isolated workspace build failed.");
        if (test.ExitCode != 0)
            errors.Add("Isolated workspace tests failed.");
        if (activeRepoMutationCount != 0)
            errors.Add("Active repo status changed.");
        if (forbiddenTouched.Count > 0)
            errors.Add("Blocked files were present in the isolated workspace.");
        if (appliedFiles.Count != package.FilesToPromote.Count)
            errors.Add("Not every promotable file was copied.");
        if (appliedFiles.Any(file => !file.HashMatchesPackage))
            errors.Add("One or more applied file hashes did not match the package.");
        return errors;
    }

    private static string ToMarkdown(IsolatedPromotionApplyReport report)
    {
        var files = string.Join(Environment.NewLine, report.AppliedFiles.Select(file => $"- `{file.RelativePath}` ({file.FileRole})"));
        return $"""
        # Isolated Promotion Apply 170

        Status: `{report.Status}`
        Project: `{report.Project}`
        Package: `{report.PackageId}`
        Proposed change: `{report.ProposedChangeId}`
        Workspace: `{report.IsolatedWorkspacePath}`
        Branch: `{report.IsolatedBranchName}`
        Recommendation: `{report.Recommendation}`

        ## Applied Files

        {files}

        ## Build/Test

        - Build: `{report.Build.Status}` (`{report.Build.ExitCode}`)
        - Test: `{report.Test.Status}` (`{report.Test.ExitCode}`)
        - Active repo mutation count: `{report.Mutation.ActiveRepoMutationCount}`
        - Blocked files rejected: `{report.RejectedBlockedFiles.Count}`

        ## Boundary

        {report.Boundary}
        """;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), fullDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
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

    private static string ReadString(JsonObject node, string name) =>
        TryGetNode(node, name, out var value) ? value?.ToString() ?? "" : "";

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

    private static string SanitizeBranchSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-');
        return builder.ToString().Trim('-');
    }

    private sealed record ChildRun(int ExitCode, string LogPath);

    private sealed record ProcessRun(string Command, int ExitCode, string LogPath, string Stdout, string Stderr)
    {
        public RuntimeCommandEvidence ToEvidence() =>
            new()
            {
                Command = Command,
                ExitCode = ExitCode,
                Status = ExitCode == 0 ? "Succeeded" : "Failed",
                LogPath = LogPath,
                Summary = ExitCode == 0 ? "Command succeeded." : "Command failed; inspect log evidence."
            };
    }
}
