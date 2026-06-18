using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IronDev.Cli;

public static partial class IronDevCliPatchProposal
{
    private const int LargePatchChangedFileThreshold = 25;
    private const int FailureSummaryLineLimit = 40;

    private static readonly Dictionary<string, string> BuiltInTestProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["core"] = "dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-restore",
        ["build"] = "dotnet build IronDev.slnx --no-restore -v:minimal",
        ["block-z"] = "dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --filter BlockZManualPatchProposalProduct --no-restore"
    };

    private static readonly string[] BuiltInForbiddenGlobs =
    [
        ".git/**",
        ".github/workflows/**",
        "deployment/**",
        "secrets/**",
        "*.pfx",
        "*.key",
        "*.pem",
        "appsettings.Production.json"
    ];

    private static readonly string[] FailureMarkers =
    [
        "failed",
        "error",
        "exception",
        "assertion",
        "stack trace",
        "msb",
        "cs",
        "xunit",
        "nunit",
        "mstest",
        "test failed",
        "failed!"
    ];

    private static async Task<int> HandleTestAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseTest(args);
        if (parsed.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch test", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!, parsed.RunsRootPath);
        var run = await LoadRunAsync(runPath, cancellationToken).ConfigureAwait(false);
        if (run is null)
            return WriteFailure(output, error, parsed.Json, "patch test", $"run metadata was not found: {Path.Combine(runPath, "run.json")}");

        if (!Directory.Exists(run.WorkspacePath))
            return WriteFailure(output, error, parsed.Json, "patch test", $"workspace path does not exist: {run.WorkspacePath}");

        var resolution = await ResolveExistingRunTestCommandAsync(run, parsed.TestCommand, parsed.TestProfileName, cancellationToken).ConfigureAwait(false);
        if (resolution.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch test", resolution.Error);

        if (IsForbiddenCommand(resolution.Command!))
            return WriteFailure(output, error, parsed.Json, "patch test", "test command contains a forbidden source-control or release action.");

        var result = await RunShellAsync(resolution.Command!, run.WorkspacePath, cancellationToken).ConfigureAwait(false);
        WriteTestArtifacts(run, resolution.Command!, resolution.ProfileName, skipped: false, result);

        run.TestCommand = resolution.Command!;
        run.TestProfileName = resolution.ProfileName;
        run.TestExitCode = result.ExitCode;
        run.TestStatus = result.ExitCode == 0 ? "Passed" : "Failed";
        run.LastUpdatedUtc = DateTimeOffset.UtcNow;
        run.Artifacts = MergeArtifacts(run.Artifacts, ["test-results.txt", "test-output-summary.md", "run-log.txt"]);
        await AppendRunLogAsync(run, $"patch test completed with exit code {result.ExitCode}", cancellationToken).ConfigureAwait(false);
        await SaveRunAsync(run, cancellationToken).ConfigureAwait(false);

        var data = BuildRunData(run, result.ExitCode == 0);
        if (parsed.Json)
            WriteJsonEnvelope(output, "patch test", result.ExitCode == 0 ? "succeeded" : "test_failed", data, result.ExitCode == 0 ? [] : ["test command returned a non-zero exit code."]);
        else
        {
            output.WriteLine($"Patch test run: {run.RunId}");
            output.WriteLine($"Status: {run.TestStatus}");
            output.WriteLine($"Exit code: {result.ExitCode}");
            output.WriteLine($"Summary: {Path.Combine(run.RunPath, "test-output-summary.md")}");
        }

        return result.ExitCode == 0 ? 0 : 1;
    }

    private static async Task<int> HandleListAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseList(args);
        if (parsed.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch list", parsed.Error);

        var runsRoot = NormalizeFullPath(parsed.RunsRootPath ?? DefaultRunsRoot());
        var runs = new List<object>();
        if (Directory.Exists(runsRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(runsRoot).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            {
                var run = await LoadRunAsync(directory, cancellationToken).ConfigureAwait(false);
                if (run is null)
                    continue;

                runs.Add(new
                {
                    run.RunId,
                    run.Status,
                    run.StartedUtc,
                    run.FinishedUtc,
                    WorkspaceExists = Directory.Exists(run.WorkspacePath),
                    ArtifactExists = File.Exists(Path.Combine(run.RunPath, "patch.diff")),
                    run.CleanupStatus
                });
            }
        }

        if (parsed.Json)
            WriteJsonEnvelope(output, "patch list", "succeeded", new { RunsRoot = runsRoot, Runs = runs }, []);
        else
        {
            output.WriteLine($"Patch runs: {runsRoot}");
            foreach (var item in runs)
                output.WriteLine(JsonSerializer.Serialize(item, JsonOptions));
        }

        return 0;
    }

    private static async Task<int> HandleCleanupAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var parsed = ParseCleanup(args);
        if (parsed.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch cleanup", parsed.Error);

        if (parsed.OlderThanDays.HasValue)
            return await HandleCleanupOlderThanAsync(parsed, output, cancellationToken).ConfigureAwait(false);

        var runPath = ResolveRunPath(parsed.Run!, parsed.RunsRootPath);
        var run = await LoadRunAsync(runPath, cancellationToken).ConfigureAwait(false);
        if (run is null)
            return WriteFailure(output, error, parsed.Json, "patch cleanup", $"run metadata was not found: {Path.Combine(runPath, "run.json")}");

        if (!parsed.DeleteWorkspace && !parsed.DeleteRun)
            return WriteFailure(output, error, parsed.Json, "patch cleanup", "cleanup requires --delete-workspace or --delete-run.");

        if (parsed.DeleteRun)
        {
            if (!IsSafeRunDeleteTarget(run))
                return WriteFailure(output, error, parsed.Json, "patch cleanup", "run folder is not a safe cleanup target.");

            var deletedRunPath = run.RunPath;
            DeleteDirectoryTree(run.RunPath);
            if (parsed.Json)
                WriteJsonEnvelope(output, "patch cleanup", "succeeded", new { run.RunId, DeletedRunPath = deletedRunPath, boundary = Boundary() }, []);
            else
                output.WriteLine($"Deleted patch run: {deletedRunPath}");
            return 0;
        }

        if (!IsSafeWorkspaceDeleteTarget(run))
            return WriteFailure(output, error, parsed.Json, "patch cleanup", "workspace is not a safe cleanup target.");

        var workspaceExisted = Directory.Exists(run.WorkspacePath);
        if (workspaceExisted)
            DeleteDirectoryTree(run.WorkspacePath);

        run.CleanupStatus = workspaceExisted ? "WorkspaceDeleted" : "WorkspaceAlreadyMissing";
        run.CleanupUpdatedUtc = DateTimeOffset.UtcNow;
        run.LastUpdatedUtc = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(Path.Combine(run.RunPath, "cleanup-summary.md"), RenderCleanupSummary(run, workspaceExisted), cancellationToken).ConfigureAwait(false);
        run.Artifacts = MergeArtifacts(run.Artifacts, ["cleanup-summary.md", "run-log.txt"]);
        await AppendRunLogAsync(run, $"patch cleanup workspace status: {run.CleanupStatus}", cancellationToken).ConfigureAwait(false);
        await SaveRunAsync(run, cancellationToken).ConfigureAwait(false);

        if (parsed.Json)
            WriteJsonEnvelope(output, "patch cleanup", "succeeded", new { run.RunId, run.CleanupStatus, WorkspaceExists = Directory.Exists(run.WorkspacePath), boundary = Boundary() }, []);
        else
            output.WriteLine($"Cleanup complete: {run.CleanupStatus}");

        return 0;
    }

    private static async Task<int> HandleCleanupOlderThanAsync(ParsedCleanupCommand parsed, TextWriter output, CancellationToken cancellationToken)
    {
        var runsRoot = NormalizeFullPath(parsed.RunsRootPath ?? DefaultRunsRoot());
        var cutoff = DateTimeOffset.UtcNow.AddDays(-parsed.OlderThanDays!.Value);
        var candidates = new List<object>();

        if (Directory.Exists(runsRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(runsRoot))
            {
                var run = await LoadRunAsync(directory, cancellationToken).ConfigureAwait(false);
                if (run is null || run.StartedUtc >= cutoff)
                    continue;

                candidates.Add(new { run.RunId, run.RunPath, run.WorkspacePath, run.StartedUtc, DryRunOnly = true });
            }
        }

        if (parsed.Json)
            WriteJsonEnvelope(output, "patch cleanup", "succeeded", new { RunsRoot = runsRoot, CutoffUtc = cutoff, Candidates = candidates, DryRunOnly = true, boundary = Boundary() }, []);
        else
        {
            output.WriteLine($"Cleanup dry-run only. Runs older than {parsed.OlderThanDays.Value} day(s):");
            foreach (var item in candidates)
                output.WriteLine(JsonSerializer.Serialize(item, JsonOptions));
        }

        return 0;
    }

    private static async Task<PatchPackageResult> WritePatchPackageAsync(PatchProposalRunDocument run, TestCommandResolution testResolution, bool skipTest, CancellationToken cancellationToken)
    {
        var stage = await RunGitAsync(run.WorkspacePath, ["add", "-A"], cancellationToken).ConfigureAwait(false);
        if (stage.ExitCode != 0)
            throw new InvalidOperationException($"could not stage disposable workspace changes for diff export: {stage.Stderr.Trim()}");

        var patch = await RunGitAsync(run.WorkspacePath, ["diff", "--cached", "--binary", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (patch.ExitCode != 0)
            throw new InvalidOperationException($"could not export patch diff: {patch.Stderr.Trim()}");

        var changedFiles = await RunGitAsync(run.WorkspacePath, ["diff", "--cached", "--name-status", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (changedFiles.ExitCode != 0)
            throw new InvalidOperationException($"could not detect changed files: {changedFiles.Stderr.Trim()}");

        await UpdateFinishSnapshotsAsync(run, cancellationToken).ConfigureAwait(false);

        var entries = ParseChangedFiles(changedFiles.Stdout);
        var changedFileLines = SplitLines(changedFiles.Stdout);
        var patchHash = Sha256Hex(patch.Stdout);
        var scopeResult = EvaluateFileScope(run, entries);

        await File.WriteAllTextAsync(Path.Combine(run.RunPath, "patch.diff"), patch.Stdout, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(run.RunPath, "changed-files.txt"), changedFiles.Stdout, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(run.RunPath, "file-scope-result.md"), RenderFileScopeResult(run, scopeResult), cancellationToken).ConfigureAwait(false);

        ProcessResult? testResult = null;
        var testsBlockedByScope = scopeResult.BlockedFiles.Length > 0;
        if (!skipTest && !testsBlockedByScope && !string.IsNullOrWhiteSpace(testResolution.Command))
            testResult = await RunShellAsync(testResolution.Command!, run.WorkspacePath, cancellationToken).ConfigureAwait(false);

        WriteTestArtifacts(run, testResolution.Command!, testResolution.ProfileName, skipTest || testsBlockedByScope, testResult);

        var testsPassed = testsBlockedByScope || skipTest ? !testsBlockedByScope : testResult?.ExitCode == 0;
        var testStatus = testsBlockedByScope ? "BlockedByFileScope" : skipTest ? "Skipped" : testResult?.ExitCode == 0 ? "Passed" : "Failed";

        run.Status = testsBlockedByScope ? "BlockedByFileScope" : testsPassed ? "Finished" : "FinishedWithTestFailure";
        run.FinishedUtc = DateTimeOffset.UtcNow;
        run.LastUpdatedUtc = DateTimeOffset.UtcNow;
        run.TestCommand = testResolution.Command!;
        run.TestProfileName = testResolution.ProfileName;
        run.TestExitCode = testResult?.ExitCode;
        run.TestStatus = testStatus;
        run.PatchSha256 = patchHash;
        run.ChangedFiles = changedFileLines;
        run.ChangedFileCount = entries.Length;
        run.BlockedFileCount = scopeResult.BlockedFiles.Length;
        run.SourceRepoMutated = false;
        run.SourceApplied = false;
        run.GitCommitCreated = false;
        run.GitPushPerformed = false;
        run.ApprovalGranted = false;
        run.PolicySatisfied = false;
        run.ReleaseApproved = false;
        run.WorkflowContinued = false;
        run.MemoryPromoted = false;
        run.AgentDispatched = false;

        await File.WriteAllTextAsync(Path.Combine(run.RunPath, "review-summary.md"), RenderReviewSummary(run, changedFileLines, patchHash, testsPassed), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(run.RunPath, "known-risks.md"), RenderKnownRisks(run, entries.Length, patchHash, testsPassed, skipTest, scopeResult), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(run.RunPath, "manual-apply-instructions.md"), RenderManualApplyInstructions(run, patchHash), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(run.RunPath, "patch-risk-summary.md"), RenderPatchRiskSummary(run, entries, scopeResult, patch.Stdout, skipTest), cancellationToken).ConfigureAwait(false);
        await AppendRunLogAsync(run, $"patch finish wrote package status: {run.Status}", cancellationToken).ConfigureAwait(false);

        run.Artifacts =
        [
            "task.md",
            "run.json",
            "patch.diff",
            "changed-files.txt",
            "test-results.txt",
            "test-output-summary.md",
            "file-scope-result.md",
            "patch-risk-summary.md",
            "review-summary.md",
            "known-risks.md",
            "manual-apply-instructions.md",
            "run-log.txt"
        ];

        var warnings = BuildWarnings(run).Concat(scopeResult.BlockedFiles.Length > 0 ? ["file scope blocked one or more changed files."] : Array.Empty<string>()).ToArray();
        return new PatchPackageResult(scopeResult, testsPassed, warnings);
    }

    private static async Task UpdateFinishSnapshotsAsync(PatchProposalRunDocument run, CancellationToken cancellationToken)
    {
        var sourceHead = await ReadGitValueAsync(run.SourceRepoPath, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
        var sourceStatus = await ReadGitValueAsync(run.SourceRepoPath, ["status", "--porcelain=v1"], cancellationToken).ConfigureAwait(false);
        var workspaceHead = await ReadGitValueAsync(run.WorkspacePath, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
        var workspaceBranch = await ReadGitValueAsync(run.WorkspacePath, ["branch", "--show-current"], cancellationToken).ConfigureAwait(false);
        var workspaceRef = await ReadGitValueAsync(run.WorkspacePath, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken).ConfigureAwait(false);

        run.SourceHeadCommitAtFinish = sourceHead.Trim();
        run.SourceStatusPorcelainAtFinish = sourceStatus;
        run.SourceRepoDirtyAtFinish = !string.IsNullOrWhiteSpace(sourceStatus);
        run.SourceHeadChangedSinceStart = !string.Equals(run.BaseCommit, sourceHead.Trim(), StringComparison.OrdinalIgnoreCase);
        run.SourceDirtyStateChangedSinceStart = run.SourceRepoDirtyAtStart != run.SourceRepoDirtyAtFinish ||
                                                !string.Equals(run.SourceStatusPorcelainAtStart, sourceStatus, StringComparison.Ordinal);
        run.WorkspaceCommitAtFinish = workspaceHead.Trim();
        run.WorkspaceBranchAtFinish = string.IsNullOrWhiteSpace(workspaceBranch) ? "detached-or-unknown" : workspaceBranch.Trim();
        run.WorkspaceRefAtFinish = string.IsNullOrWhiteSpace(workspaceRef) ? "detached-or-unknown" : workspaceRef.Trim();
    }

    private static async Task<string> ReadSourceIdentityAsync(string repoPath, CancellationToken cancellationToken)
    {
        var origin = await ReadGitValueAsync(repoPath, ["remote", "get-url", "origin"], cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(origin) ? repoPath : origin.Trim();
    }

    private static async Task<TestCommandResolution> ResolveStartTestCommandAsync(string repoRoot, string? testCommand, string? testProfileName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(testCommand) && !string.IsNullOrWhiteSpace(testProfileName))
            return TestCommandResolution.Fail("--test and --test-profile are mutually exclusive.");
        if (!string.IsNullOrWhiteSpace(testCommand))
            return new TestCommandResolution(testCommand.Trim(), null, null);
        if (!string.IsNullOrWhiteSpace(testProfileName))
            return await ResolveTestProfileAsync(repoRoot, testProfileName.Trim(), cancellationToken).ConfigureAwait(false);
        return TestCommandResolution.Fail("--test or --test-profile is required.");
    }

    private static async Task<TestCommandResolution> ResolveExistingRunTestCommandAsync(PatchProposalRunDocument run, string? overrideCommand, string? overrideProfile, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(overrideCommand) && !string.IsNullOrWhiteSpace(overrideProfile))
            return TestCommandResolution.Fail("--test and --test-profile are mutually exclusive.");
        if (!string.IsNullOrWhiteSpace(overrideCommand))
            return new TestCommandResolution(overrideCommand.Trim(), null, null);
        if (!string.IsNullOrWhiteSpace(overrideProfile))
            return await ResolveTestProfileAsync(run.SourceRepoPath, overrideProfile.Trim(), cancellationToken).ConfigureAwait(false);
        return new TestCommandResolution(run.TestCommand, run.TestProfileName, null);
    }

    private static async Task<TestCommandResolution> ResolveTestProfileAsync(string repoRoot, string profileName, CancellationToken cancellationToken)
    {
        var profiles = new Dictionary<string, string>(BuiltInTestProfiles, StringComparer.OrdinalIgnoreCase);
        var configPath = Path.Combine(repoRoot, ".irondev", "test-profiles.json");
        if (File.Exists(configPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
                var config = JsonSerializer.Deserialize<TestProfilesFile>(json, JsonOptions);
                foreach (var item in config?.Profiles ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                        profiles[item.Key.Trim()] = item.Value.Trim();
                }
            }
            catch (JsonException ex)
            {
                return TestCommandResolution.Fail($"could not parse test profile config: {ex.Message}");
            }
        }

        return profiles.TryGetValue(profileName, out var command)
            ? new TestCommandResolution(command, profileName, null)
            : TestCommandResolution.Fail($"unknown test profile: {profileName}");
    }

    private static void WriteTestArtifacts(PatchProposalRunDocument run, string testCommand, string? profileName, bool skipped, ProcessResult? result)
    {
        File.WriteAllText(Path.Combine(run.RunPath, "test-results.txt"), RenderTestResults(testCommand, profileName, skipped, result));
        File.WriteAllText(Path.Combine(run.RunPath, "test-output-summary.md"), RenderTestOutputSummary(testCommand, profileName, run.WorkspacePath, skipped, result));
    }

    private static ScopeEvaluationResult EvaluateFileScope(PatchProposalRunDocument run, ChangedFileEntry[] entries)
    {
        var allowPatterns = run.AllowedFileGlobs.Where(item => !string.IsNullOrWhiteSpace(item)).Select(NormalizeGlob).ToArray();
        var forbiddenPatterns = run.ForbiddenFileGlobs
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(NormalizeGlob)
            .Concat(BuiltInForbiddenGlobs.Select(NormalizeGlob))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var allowed = new List<string>();
        var blocked = new List<BlockedFileResult>();

        foreach (var entry in entries)
        {
            var paths = entry.Kind.Equals("R", StringComparison.OrdinalIgnoreCase)
                ? new[] { entry.Path, entry.PreviousPath ?? string.Empty }.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray()
                : [entry.Path];
            var forbiddenPattern = forbiddenPatterns.FirstOrDefault(pattern => paths.Any(path => GlobMatches(pattern, path)));
            if (forbiddenPattern is not null)
            {
                blocked.Add(new BlockedFileResult(entry.Display, $"matched forbidden pattern '{forbiddenPattern}'"));
                continue;
            }
            if (allowPatterns.Length > 0 && !paths.Any(path => allowPatterns.Any(pattern => GlobMatches(pattern, path))))
            {
                blocked.Add(new BlockedFileResult(entry.Display, "did not match any allowed pattern"));
                continue;
            }
            allowed.Add(entry.Display);
        }

        return new ScopeEvaluationResult(allowPatterns, forbiddenPatterns, entries.Select(item => item.Display).ToArray(), allowed.ToArray(), blocked.ToArray());
    }

    private static ChangedFileEntry[] ParseChangedFiles(string value)
    {
        var entries = new List<ChangedFileEntry>();
        foreach (var line in SplitLines(value))
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                continue;
            var status = parts[0];
            var kind = status.StartsWith('R') ? "R" : status[..1];
            if (kind == "R" && parts.Length >= 3)
                entries.Add(new ChangedFileEntry(kind, NormalizeRepoPath(parts[2]), NormalizeRepoPath(parts[1]), line));
            else if (parts.Length >= 2)
                entries.Add(new ChangedFileEntry(kind, NormalizeRepoPath(parts[1]), null, line));
        }
        return entries.ToArray();
    }

    private static string RenderFileScopeResult(PatchProposalRunDocument run, ScopeEvaluationResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# File Scope Result");
        builder.AppendLine();
        builder.AppendLine($"Final result: {(result.BlockedFiles.Length == 0 ? "allowed" : "blocked")}");
        builder.AppendLine();
        WriteMarkdownList(builder, "Allowed patterns", result.AllowedPatterns.Length == 0 ? ["<none; default permissive>"] : result.AllowedPatterns);
        WriteMarkdownList(builder, "Forbidden patterns", result.ForbiddenPatterns);
        WriteMarkdownList(builder, "Changed files", result.ChangedFiles.Length == 0 ? ["<none>"] : result.ChangedFiles);
        WriteMarkdownList(builder, "Allowed files", result.AllowedFiles.Length == 0 ? ["<none>"] : result.AllowedFiles);
        builder.AppendLine("## Blocked files");
        builder.AppendLine();
        if (result.BlockedFiles.Length == 0)
            builder.AppendLine("- <none>");
        else
            foreach (var blocked in result.BlockedFiles)
                builder.AppendLine($"- `{blocked.Path}`: {blocked.Reason}");
        builder.AppendLine();
        builder.AppendLine("Boundary: file scope can block a package. It does not approve allowed files.");
        return builder.ToString();
    }

    private static string RenderTestResults(string testCommand, string? profileName, bool skipped, ProcessResult? result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Test Results");
        builder.AppendLine();
        builder.AppendLine("Boundary: tests were run only inside the disposable workspace. The source repository was not modified.");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(profileName))
            builder.AppendLine($"Profile: `{profileName}`");
        builder.AppendLine($"Command: `{testCommand}`");
        builder.AppendLine();
        if (skipped)
        {
            builder.AppendLine("Status: skipped");
            return builder.ToString();
        }
        if (result is null)
        {
            builder.AppendLine("Status: not run");
            return builder.ToString();
        }
        builder.AppendLine($"Status: {(result.ExitCode == 0 ? "passed" : "failed")}");
        builder.AppendLine($"Exit code: {result.ExitCode}");
        builder.AppendLine();
        builder.AppendLine("## stdout");
        builder.AppendLine("```text");
        builder.AppendLine(result.Stdout.TrimEnd());
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## stderr");
        builder.AppendLine("```text");
        builder.AppendLine(result.Stderr.TrimEnd());
        builder.AppendLine("```");
        return builder.ToString();
    }

    private static string RenderTestOutputSummary(string testCommand, string? profileName, string workingDirectory, bool skipped, ProcessResult? result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Test Output Summary");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(profileName))
            builder.AppendLine($"Profile: `{profileName}`");
        builder.AppendLine($"Command: `{testCommand}`");
        builder.AppendLine($"Working directory: `{workingDirectory}`");
        builder.AppendLine($"Status: {(skipped ? "skipped" : result is null ? "not run" : result.ExitCode == 0 ? "passed" : "failed")}");
        if (result is not null)
            builder.AppendLine($"Exit code: `{result.ExitCode}`");
        builder.AppendLine();
        builder.AppendLine("Full output: `test-results.txt`");
        builder.AppendLine();
        builder.AppendLine("## Likely failure lines");
        builder.AppendLine();
        var failureLines = result is null || skipped ? [] : ExtractFailureLines(result.Stdout, result.Stderr);
        if (failureLines.Length == 0)
            builder.AppendLine("- <none detected>");
        else
            foreach (var line in failureLines)
                builder.AppendLine($"- `{line}`");
        builder.AppendLine();
        builder.AppendLine("Boundary: this summary is diagnostic evidence only. It does not fix tests or decide sufficiency.");
        return builder.ToString();
    }

    private static string RenderPatchRiskSummary(PatchProposalRunDocument run, ChangedFileEntry[] entries, ScopeEvaluationResult scopeResult, string patchText, bool testsSkipped)
    {
        var added = entries.Count(item => item.Kind == "A");
        var modified = entries.Count(item => item.Kind == "M");
        var deleted = entries.Count(item => item.Kind == "D");
        var renamed = entries.Count(item => item.Kind == "R");
        var risks = new List<string>();
        if (entries.Length == 0)
            risks.Add("no changes detected");
        if (testsSkipped)
            risks.Add("tests skipped");
        if (run.TestStatus == "Failed")
            risks.Add("tests failed");
        if (run.SourceRepoDirtyAtStart)
            risks.Add("source repo dirty at start");
        if (run.SourceHeadChangedSinceStart)
            risks.Add("source HEAD changed since start");
        if (scopeResult.BlockedFiles.Length > 0)
            risks.Add("forbidden files changed");
        if (entries.Length > LargePatchChangedFileThreshold)
            risks.Add("large patch changed file count");
        if (string.IsNullOrWhiteSpace(patchText))
            risks.Add("patch.diff missing or empty");
        if (patchText.Contains("GIT binary patch", StringComparison.OrdinalIgnoreCase) || patchText.Contains("Binary files", StringComparison.OrdinalIgnoreCase))
            risks.Add("generated/binary files changed");

        var builder = new StringBuilder();
        builder.AppendLine("# Patch Risk Summary");
        builder.AppendLine();
        builder.AppendLine($"Changed file count: `{entries.Length}`");
        builder.AppendLine($"Added: `{added}`");
        builder.AppendLine($"Modified: `{modified}`");
        builder.AppendLine($"Deleted: `{deleted}`");
        builder.AppendLine($"Renamed: `{renamed}`");
        builder.AppendLine($"Patch SHA-256: `{run.PatchSha256}`");
        builder.AppendLine($"Test status: `{run.TestStatus}`");
        builder.AppendLine($"Forbidden file result: {(scopeResult.BlockedFiles.Length == 0 ? "allowed" : "blocked")}");
        builder.AppendLine($"Source dirty at start: `{run.SourceRepoDirtyAtStart}`");
        builder.AppendLine($"Source HEAD changed since start: `{run.SourceHeadChangedSinceStart}`");
        builder.AppendLine();
        WriteMarkdownList(builder, "Risk flags", risks.Count == 0 ? ["none detected"] : risks.ToArray());
        builder.AppendLine("Manual review required before applying this patch.");
        builder.AppendLine("Boundary: risk summary does not approve the patch.");
        return builder.ToString();
    }

    private static string RenderKnownRisks(PatchProposalRunDocument run, int changedFileCount, string patchHash, bool testsPassed, bool testsSkipped, ScopeEvaluationResult scopeResult)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Known Risks");
        builder.AppendLine();
        builder.AppendLine("- Human review remains required before any manual application.");
        builder.AppendLine("- This package does not prove release readiness.");
        builder.AppendLine("- This package does not satisfy approval or policy requirements.");
        builder.AppendLine("- The disposable workspace was created from committed source repository state.");
        builder.AppendLine($"- Changed file count: `{changedFileCount}`.");
        builder.AppendLine($"- Patch SHA-256: `{patchHash}`.");
        if (run.SourceRepoDirtyAtStart)
            builder.AppendLine("- Source repository had uncommitted changes when the workspace was created; those changes were not copied into the disposable workspace.");
        if (run.SourceHeadChangedSinceStart)
            builder.AppendLine("- Source HEAD changed since run start; review the patch base carefully before manual application.");
        else
            builder.AppendLine("- Source HEAD did not change since run start.");
        if (scopeResult.BlockedFiles.Length > 0)
            builder.AppendLine("- Forbidden or out-of-scope files changed; package is blocked.");
        if (testsSkipped)
            builder.AppendLine("- Tests were skipped for this package.");
        else if (!testsPassed)
            builder.AppendLine("- Test command returned a non-zero exit code.");
        return builder.ToString();
    }

    private static string RenderCleanupSummary(PatchProposalRunDocument run, bool workspaceExisted)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Cleanup Summary");
        builder.AppendLine();
        builder.AppendLine($"Run ID: `{run.RunId}`");
        builder.AppendLine($"Workspace path: `{run.WorkspacePath}`");
        builder.AppendLine($"Workspace existed before cleanup: `{workspaceExisted}`");
        builder.AppendLine($"Cleanup status: `{run.CleanupStatus}`");
        builder.AppendLine();
        builder.AppendLine("Boundary: cleanup only removes recorded disposable run/workspace material. It does not delete source history.");
        return builder.ToString();
    }

    private static string[] ExtractFailureLines(string stdout, string stderr) =>
        SplitLines(stdout + Environment.NewLine + stderr)
            .Where(line => FailureMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .Take(FailureSummaryLineLimit)
            .ToArray();

    private static async Task AppendRunLogAsync(PatchProposalRunDocument run, string message, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(run.RunPath);
        await File.AppendAllTextAsync(Path.Combine(run.RunPath, "run-log.txt"), $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}", cancellationToken).ConfigureAwait(false);
    }

    private static object BuildRunData(PatchProposalRunDocument run, bool testsPassed) => new
    {
        run.RunId,
        run.Status,
        run.RunPath,
        run.WorkspacePath,
        WorkspaceExists = Directory.Exists(run.WorkspacePath),
        run.BaseBranch,
        run.BaseCommit,
        run.SourceHeadCommitAtFinish,
        run.SourceHeadChangedSinceStart,
        run.SourceRepoDirtyAtStart,
        run.SourceRepoDirtyAtFinish,
        run.WorkspaceCommitAtStart,
        run.WorkspaceCommitAtFinish,
        run.TestProfileName,
        run.TestCommand,
        run.TestStatus,
        TestsPassed = testsPassed,
        run.TestExitCode,
        run.PatchSha256,
        run.ChangedFileCount,
        run.BlockedFileCount,
        run.CleanupStatus,
        run.SourceRepoMutated,
        run.SourceApplied,
        run.GitCommitCreated,
        run.GitPushPerformed,
        run.ApprovalGranted,
        run.PolicySatisfied,
        run.ReleaseApproved,
        run.WorkflowContinued,
        run.MemoryPromoted,
        run.AgentDispatched,
        Artifacts = run.Artifacts.Select(artifact => Path.Combine(run.RunPath, artifact)).ToArray(),
        boundary = Boundary()
    };

    private static string[] BuildWarnings(PatchProposalRunDocument run)
    {
        var warnings = new List<string>();
        if (run.SourceRepoDirtyAtStart)
            warnings.Add("source repository was dirty at run start; workspace excludes uncommitted source changes.");
        if (run.SourceHeadChangedSinceStart)
            warnings.Add("source HEAD changed since run start.");
        if (run.BlockedFileCount > 0)
            warnings.Add("file scope blocked one or more changed files.");
        if (run.TestStatus is "Failed" or "Skipped" or "BlockedByFileScope")
            warnings.Add($"test status is {run.TestStatus}.");
        return warnings.ToArray();
    }

    private static ParsedTestCommand ParseTest(string[] args)
    {
        string? run = null;
        string? runsRootPath = null;
        string? testCommand = null;
        string? testProfile = null;
        var json = HasJson(args);
        for (var index = 2; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--run":
                    if (!TryReadValue(args, ref index, out run))
                        return ParsedTestCommand.Fail(json, "--run requires a value.");
                    break;
                case "--runs-root":
                    if (!TryReadValue(args, ref index, out runsRootPath))
                        return ParsedTestCommand.Fail(json, "--runs-root requires a value.");
                    break;
                case "--test":
                    if (!TryReadValue(args, ref index, out testCommand))
                        return ParsedTestCommand.Fail(json, "--test requires a value.");
                    break;
                case "--test-profile":
                    if (!TryReadValue(args, ref index, out testProfile))
                        return ParsedTestCommand.Fail(json, "--test-profile requires a value.");
                    break;
                case "--json":
                    break;
                default:
                    return ParsedTestCommand.Fail(json, $"unsupported patch test option: {arg}");
            }
        }
        if (string.IsNullOrWhiteSpace(run))
            return ParsedTestCommand.Fail(json, "--run is required.");
        if (!string.IsNullOrWhiteSpace(testCommand) && !string.IsNullOrWhiteSpace(testProfile))
            return ParsedTestCommand.Fail(json, "--test and --test-profile are mutually exclusive.");
        return new ParsedTestCommand(run, runsRootPath, testCommand, testProfile, json, null);
    }

    private static ParsedListCommand ParseList(string[] args)
    {
        string? runsRootPath = null;
        var json = HasJson(args);
        for (var index = 2; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--runs-root":
                    if (!TryReadValue(args, ref index, out runsRootPath))
                        return ParsedListCommand.Fail(json, "--runs-root requires a value.");
                    break;
                case "--json":
                    break;
                default:
                    return ParsedListCommand.Fail(json, $"unsupported patch list option: {arg}");
            }
        }
        return new ParsedListCommand(runsRootPath, json, null);
    }

    private static ParsedCleanupCommand ParseCleanup(string[] args)
    {
        string? run = null;
        string? runsRootPath = null;
        int? olderThanDays = null;
        var deleteWorkspace = false;
        var deleteRun = false;
        var json = HasJson(args);
        for (var index = 2; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--run":
                    if (!TryReadValue(args, ref index, out run))
                        return ParsedCleanupCommand.Fail(json, "--run requires a value.");
                    break;
                case "--runs-root":
                    if (!TryReadValue(args, ref index, out runsRootPath))
                        return ParsedCleanupCommand.Fail(json, "--runs-root requires a value.");
                    break;
                case "--delete-workspace":
                case "--delete-workspaces":
                    deleteWorkspace = true;
                    break;
                case "--delete-run":
                    deleteRun = true;
                    break;
                case "--older-than-days":
                    if (!TryReadValue(args, ref index, out var daysValue) || !int.TryParse(daysValue, out var days) || days <= 0)
                        return ParsedCleanupCommand.Fail(json, "--older-than-days requires a positive integer.");
                    olderThanDays = days;
                    break;
                case "--json":
                    break;
                default:
                    return ParsedCleanupCommand.Fail(json, $"unsupported patch cleanup option: {arg}");
            }
        }
        if (deleteWorkspace && deleteRun)
            return ParsedCleanupCommand.Fail(json, "choose only one cleanup deletion mode.");
        if (olderThanDays.HasValue && !deleteWorkspace)
            return ParsedCleanupCommand.Fail(json, "--older-than-days currently supports --delete-workspaces dry-run only.");
        if (!olderThanDays.HasValue && string.IsNullOrWhiteSpace(run))
            return ParsedCleanupCommand.Fail(json, "--run is required unless --older-than-days is used.");
        return new ParsedCleanupCommand(run, runsRootPath, deleteWorkspace, deleteRun, olderThanDays, json, null);
    }

    private static string NormalizeRepoPath(string path) => path.Replace('\\', '/').Trim().TrimStart('/');

    private static string NormalizeGlob(string pattern) => NormalizeRepoPath(pattern.Trim());

    private static bool GlobMatches(string pattern, string path)
    {
        var regex = "^" + Regex.Escape(NormalizeGlob(pattern))
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(NormalizeRepoPath(path), regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string[] MergeArtifacts(string[] existing, string[] additions) =>
        existing.Concat(additions)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsSafeWorkspaceDeleteTarget(PatchProposalRunDocument run)
    {
        var workspace = NormalizeFullPath(run.WorkspacePath);
        var source = NormalizeFullPath(run.SourceRepoPath);
        var parent = Directory.GetParent(workspace);
        return Directory.Exists(workspace) &&
               !IsSameOrUnderPath(workspace, source) &&
               string.Equals(Path.GetFileName(workspace), "workspace", StringComparison.OrdinalIgnoreCase) &&
               parent is not null &&
               string.Equals(parent.Name, run.RunId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeRunDeleteTarget(PatchProposalRunDocument run)
    {
        var runPath = NormalizeFullPath(run.RunPath);
        var runsRoot = NormalizeFullPath(run.RunsRootPath);
        var source = NormalizeFullPath(run.SourceRepoPath);
        return Directory.Exists(runPath) &&
               !IsSameOrUnderPath(runPath, source) &&
               IsSameOrUnderPath(runPath, runsRoot) &&
               string.Equals(Path.GetFileName(runPath), run.RunId, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteDirectoryTree(string path)
    {
        if (!Directory.Exists(path))
            return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(entry, FileAttributes.Normal);
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }

    private static void WriteMarkdownList(StringBuilder builder, string title, string[] values)
    {
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        foreach (var value in values)
            builder.AppendLine($"- `{value}`");
        builder.AppendLine();
    }

    private sealed record TestCommandResolution(string? Command, string? ProfileName, string? Error)
    {
        public static TestCommandResolution Fail(string error) => new(null, null, error);
    }

    private sealed record ChangedFileEntry(string Kind, string Path, string? PreviousPath, string Display);
    private sealed record BlockedFileResult(string Path, string Reason);
    private sealed record ScopeEvaluationResult(string[] AllowedPatterns, string[] ForbiddenPatterns, string[] ChangedFiles, string[] AllowedFiles, BlockedFileResult[] BlockedFiles);
    private sealed record PatchPackageResult(ScopeEvaluationResult ScopeResult, bool TestsPassed, string[] Warnings);

    private sealed record ParsedTestCommand(string? Run, string? RunsRootPath, string? TestCommand, string? TestProfileName, bool Json, string? Error)
    {
        public static ParsedTestCommand Fail(bool json, string error) => new(null, null, null, null, json, error);
    }

    private sealed record ParsedListCommand(string? RunsRootPath, bool Json, string? Error)
    {
        public static ParsedListCommand Fail(bool json, string error) => new(null, json, error);
    }

    private sealed record ParsedCleanupCommand(string? Run, string? RunsRootPath, bool DeleteWorkspace, bool DeleteRun, int? OlderThanDays, bool Json, string? Error)
    {
        public static ParsedCleanupCommand Fail(bool json, string error) => new(null, null, false, false, null, json, error);
    }

    private sealed class TestProfilesFile
    {
        public Dictionary<string, string> Profiles { get; set; } = [];
    }
}
