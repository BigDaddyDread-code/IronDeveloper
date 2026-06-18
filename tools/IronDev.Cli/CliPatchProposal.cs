using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IronDev.Cli;

public static partial class IronDevCliPatchProposal
{
    private const string RunSchemaVersion = "irondev.patch-run.v1";
    private const string DefaultRunsFolderName = "irondev-patch-runs";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static bool IsPatchCommand(string[] args) =>
        args.Length >= 1 &&
        string.Equals(args[0], "patch", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return WriteUsageError(error, "patch requires a subcommand: start, finish, or status.");

        return args[1].ToLowerInvariant() switch
        {
            "start" => await HandleStartAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "finish" => await HandleFinishAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "status" => await HandleStatusAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "test" => await HandleTestAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "list" => await HandleListAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "cleanup" => await HandleCleanupAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "governance" => await HandlePatchGovernanceAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            _ => WriteUsageError(error, $"unsupported patch subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandleStartAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var parsed = ParseStart(args);
        if (parsed.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch start", parsed.Error);

        var requestedRepoPath = NormalizeFullPath(parsed.RepoPath!);
        var taskPath = NormalizeFullPath(parsed.TaskPath!);
        var runsRoot = NormalizeFullPath(parsed.RunsRootPath ?? DefaultRunsRoot());
        var runId = string.IsNullOrWhiteSpace(parsed.RunId)
            ? GenerateRunId()
            : parsed.RunId.Trim();

        if (!IsSafeRunId(runId))
            return WriteFailure(output, error, parsed.Json, "patch start", "run id must contain only letters, numbers, '.', '-', or '_'.");

        if (!Directory.Exists(requestedRepoPath))
            return WriteFailure(output, error, parsed.Json, "patch start", $"repository path does not exist: {requestedRepoPath}");

        if (!File.Exists(taskPath))
            return WriteFailure(output, error, parsed.Json, "patch start", $"task file does not exist: {taskPath}");

        var repoRootResult = await RunGitAsync(requestedRepoPath, ["rev-parse", "--show-toplevel"], cancellationToken).ConfigureAwait(false);
        if (repoRootResult.ExitCode != 0)
            return WriteFailure(output, error, parsed.Json, "patch start", "repository path must be a git repository.");

        var sourceRepoRoot = NormalizeFullPath(repoRootResult.Stdout.Trim());
        if (IsSameOrUnderPath(runsRoot, sourceRepoRoot))
            return WriteFailure(output, error, parsed.Json, "patch start", "runs root must be outside the source repository.");

        var runPath = Path.Combine(runsRoot, runId);
        if (Directory.Exists(runPath) || File.Exists(runPath))
            return WriteFailure(output, error, parsed.Json, "patch start", $"run already exists: {runPath}");

        var workspacePath = parsed.WorkspaceRootPath is null
            ? Path.Combine(runPath, "workspace")
            : Path.Combine(NormalizeFullPath(parsed.WorkspaceRootPath), runId, "workspace");

        if (IsSameOrUnderPath(workspacePath, sourceRepoRoot))
            return WriteFailure(output, error, parsed.Json, "patch start", "workspace path must be outside the source repository.");

        var resolvedTest = await ResolveStartTestCommandAsync(sourceRepoRoot, parsed.TestCommand, parsed.TestProfileName, cancellationToken).ConfigureAwait(false);
        if (resolvedTest.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch start", resolvedTest.Error);

        Directory.CreateDirectory(runPath);

        var baseCommit = await ReadGitValueAsync(sourceRepoRoot, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(baseCommit))
            return WriteFailure(output, error, parsed.Json, "patch start", "could not resolve source repository HEAD.");

        var baseBranch = await ReadGitValueAsync(sourceRepoRoot, ["branch", "--show-current"], cancellationToken).ConfigureAwait(false);
        var sourceStatus = await ReadGitValueAsync(sourceRepoRoot, ["status", "--porcelain=v1"], cancellationToken).ConfigureAwait(false);
        var sourceIdentity = await ReadSourceIdentityAsync(sourceRepoRoot, cancellationToken).ConfigureAwait(false);

        var clone = await RunProcessAsync(
            "git",
            ["clone", "--no-hardlinks", "--quiet", sourceRepoRoot, workspacePath],
            Directory.GetCurrentDirectory(),
            cancellationToken).ConfigureAwait(false);

        if (clone.ExitCode != 0)
            return WriteFailure(output, error, parsed.Json, "patch start", $"could not create disposable workspace: {clone.Stderr.Trim()}");

        var copiedTaskPath = Path.Combine(runPath, "task.md");
        File.Copy(taskPath, copiedTaskPath, overwrite: false);
        var workspaceCommit = await ReadGitValueAsync(workspacePath, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
        var workspaceBranch = await ReadGitValueAsync(workspacePath, ["branch", "--show-current"], cancellationToken).ConfigureAwait(false);
        var workspaceRef = await ReadGitValueAsync(workspacePath, ["rev-parse", "--abbrev-ref", "HEAD"], cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var run = new PatchProposalRunDocument
        {
            SchemaVersion = RunSchemaVersion,
            RunId = runId,
            Status = "Started",
            SourceRepoPath = sourceRepoRoot,
            SourceRepoIdentity = sourceIdentity,
            RunsRootPath = runsRoot,
            RunPath = runPath,
            WorkspacePath = workspacePath,
            TaskSourcePath = taskPath,
            TaskPath = copiedTaskPath,
            BaseBranch = string.IsNullOrWhiteSpace(baseBranch) ? "detached-or-unknown" : baseBranch.Trim(),
            BaseCommit = baseCommit.Trim(),
            SourceStatusPorcelainAtStart = sourceStatus,
            SourceRepoDirtyAtStart = !string.IsNullOrWhiteSpace(sourceStatus),
            WorkspaceCommitAtStart = workspaceCommit.Trim(),
            WorkspaceBranchAtStart = string.IsNullOrWhiteSpace(workspaceBranch) ? "detached-or-unknown" : workspaceBranch.Trim(),
            WorkspaceRefAtStart = string.IsNullOrWhiteSpace(workspaceRef) ? "detached-or-unknown" : workspaceRef.Trim(),
            TestCommand = resolvedTest.Command!,
            TestProfileName = resolvedTest.ProfileName,
            TestStatus = "NotRun",
            AllowedFileGlobs = parsed.AllowPatterns.ToArray(),
            ForbiddenFileGlobs = parsed.ForbidPatterns.ToArray(),
            StartedUtc = now,
            LastUpdatedUtc = now,
            CleanupStatus = "NotCleaned",
            SourceRepoHadUncommittedChanges = !string.IsNullOrWhiteSpace(sourceStatus),
            SourceRepoMutated = false,
            SourceApplied = false,
            GitCommitCreated = false,
            GitPushPerformed = false,
            ApprovalGranted = false,
            PolicySatisfied = false,
            ReleaseApproved = false,
            WorkflowContinued = false,
            MemoryPromoted = false,
            AgentDispatched = false,
            Artifacts = ["task.md", "run.json", "run-log.txt"]
        };

        await AppendRunLogAsync(run, "patch start created disposable workspace", cancellationToken).ConfigureAwait(false);
        await RecordPatchRunStartedGovernanceEventAsync(run, cancellationToken).ConfigureAwait(false);
        await RecordDisposableWorkspaceCreatedGovernanceEventAsync(run, cancellationToken).ConfigureAwait(false);
        await SaveRunAsync(run, cancellationToken).ConfigureAwait(false);

        var data = new
        {
            run.RunId,
            run.Status,
            run.RunPath,
            run.WorkspacePath,
            run.TaskPath,
            run.BaseBranch,
            run.BaseCommit,
            run.WorkspaceCommitAtStart,
            run.TestProfileName,
            run.TestCommand,
            run.AllowedFileGlobs,
            run.ForbiddenFileGlobs,
            run.SourceRepoMutated,
            run.SourceApplied,
            run.GitCommitCreated,
            run.GitPushPerformed,
            run.SourceRepoHadUncommittedChanges,
            boundary = Boundary()
        };

        if (parsed.Json)
            WriteJsonEnvelope(output, "patch start", "succeeded", data, []);
        else
        {
            output.WriteLine($"Patch run started: {run.RunId}");
            output.WriteLine($"Run path: {run.RunPath}");
            output.WriteLine($"Workspace: {run.WorkspacePath}");
            output.WriteLine("Boundary: disposable workspace only; source repository was not modified.");
            if (run.SourceRepoHadUncommittedChanges)
                output.WriteLine("Warning: source repository had uncommitted changes; workspace was created from committed HEAD only.");
        }

        return 0;
    }

    private static async Task<int> HandleFinishAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var parsed = ParseFinish(args);
        if (parsed.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch finish", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!, parsed.RunsRootPath);
        var run = await LoadRunAsync(runPath, cancellationToken).ConfigureAwait(false);
        if (run is null)
            return WriteFailure(output, error, parsed.Json, "patch finish", $"run metadata was not found: {Path.Combine(runPath, "run.json")}");

        if (!Directory.Exists(run.WorkspacePath))
            return WriteFailure(output, error, parsed.Json, "patch finish", $"workspace path does not exist: {run.WorkspacePath}");

        var resolvedTest = await ResolveExistingRunTestCommandAsync(run, parsed.TestCommand, parsed.TestProfileName, cancellationToken).ConfigureAwait(false);
        if (resolvedTest.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch finish", resolvedTest.Error);

        var effectiveTestCommand = resolvedTest.Command!;

        if (!parsed.SkipTest && IsForbiddenCommand(effectiveTestCommand))
            return WriteFailure(output, error, parsed.Json, "patch finish", "test command contains a forbidden source-control or release action.");

        var package = await WritePatchPackageAsync(run, resolvedTest, parsed.SkipTest, cancellationToken).ConfigureAwait(false);

        await SaveRunAsync(run, cancellationToken).ConfigureAwait(false);

        var data = new
        {
            run.RunId,
            run.Status,
            run.RunPath,
            run.WorkspacePath,
            run.PatchSha256,
            run.ChangedFileCount,
            run.BlockedFileCount,
            TestsPassed = package.TestsPassed,
            run.TestExitCode,
            run.TestStatus,
            run.SourceHeadChangedSinceStart,
            run.SourceRepoDirtyAtStart,
            run.CleanupStatus,
            run.SourceRepoMutated,
            run.SourceApplied,
            run.GitCommitCreated,
            run.GitPushPerformed,
            Artifacts = run.Artifacts,
            boundary = Boundary()
        };

        if (parsed.Json)
        {
            var status = package.ScopeResult.BlockedFiles.Length > 0
                ? "blocked"
                : package.TestsPassed ? "succeeded" : "test_failed";
            WriteJsonEnvelope(output, "patch finish", status, data, package.Warnings);
        }
        else
        {
            output.WriteLine($"Patch run finished: {run.RunId}");
            output.WriteLine($"Status: {run.Status}");
            output.WriteLine($"Patch: {Path.Combine(run.RunPath, "patch.diff")}");
            output.WriteLine($"Changed files: {Path.Combine(run.RunPath, "changed-files.txt")}");
            output.WriteLine($"Tests passed: {package.TestsPassed}");
            if (package.ScopeResult.BlockedFiles.Length > 0)
                output.WriteLine("File scope: blocked");
            output.WriteLine("Boundary: review package only; source repository was not modified.");
        }

        return package.ScopeResult.BlockedFiles.Length > 0 || !package.TestsPassed ? 1 : 0;
    }

    private static async Task<int> HandleStatusAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var parsed = ParseStatus(args);
        if (parsed.Error is not null)
            return WriteFailure(output, error, parsed.Json, "patch status", parsed.Error);

        var runPath = ResolveRunPath(parsed.Run!, parsed.RunsRootPath);
        var run = await LoadRunAsync(runPath, cancellationToken).ConfigureAwait(false);
        if (run is null)
            return WriteFailure(output, error, parsed.Json, "patch status", $"run metadata was not found: {Path.Combine(runPath, "run.json")}");

        await RecordPatchRunStatusReadGovernanceEventAsync(run, cancellationToken).ConfigureAwait(false);
        await SaveRunAsync(run, cancellationToken).ConfigureAwait(false);

        var data = new
        {
            run.RunId,
            run.Status,
            run.RunPath,
            run.WorkspacePath,
            run.BaseBranch,
            run.BaseCommit,
            run.PatchSha256,
            run.ChangedFileCount,
            run.BlockedFileCount,
            run.TestExitCode,
            run.TestStatus,
            run.SourceHeadChangedSinceStart,
            run.SourceRepoDirtyAtStart,
            run.CleanupStatus,
            run.SourceRepoMutated,
            run.SourceApplied,
            run.GitCommitCreated,
            run.GitPushPerformed,
            Artifacts = run.Artifacts,
            boundary = Boundary()
        };

        if (parsed.Json)
            WriteJsonEnvelope(output, "patch status", "succeeded", data, []);
        else
        {
            output.WriteLine($"Patch run: {run.RunId}");
            output.WriteLine($"Status: {run.Status}");
            output.WriteLine($"Run path: {run.RunPath}");
            output.WriteLine($"Workspace: {run.WorkspacePath}");
            output.WriteLine($"Changed files: {run.ChangedFileCount}");
            output.WriteLine($"Test status: {run.TestStatus}");
            if (run.SourceHeadChangedSinceStart)
                output.WriteLine("Warning: source HEAD changed since run start.");
            if (run.BlockedFileCount > 0)
                output.WriteLine("Warning: file scope blocked one or more files.");
        }

        return 0;
    }

    private static ParsedStartCommand ParseStart(string[] args)
    {
        string? repoPath = null;
        string? taskPath = null;
        string? testCommand = null;
        string? testProfileName = null;
        string? runsRootPath = null;
        string? workspaceRootPath = null;
        string? runId = null;
        var allowPatterns = new List<string>();
        var forbidPatterns = new List<string>();
        var json = HasJson(args);

        for (var index = 2; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--repo":
                    if (!TryReadValue(args, ref index, out repoPath))
                        return ParsedStartCommand.Fail(json, "--repo requires a value.");
                    break;
                case "--task":
                    if (!TryReadValue(args, ref index, out taskPath))
                        return ParsedStartCommand.Fail(json, "--task requires a value.");
                    break;
                case "--test":
                    if (!TryReadValue(args, ref index, out testCommand))
                        return ParsedStartCommand.Fail(json, "--test requires a value.");
                    break;
                case "--test-profile":
                    if (!TryReadValue(args, ref index, out testProfileName))
                        return ParsedStartCommand.Fail(json, "--test-profile requires a value.");
                    break;
                case "--allow":
                    if (!TryReadValue(args, ref index, out var allowPattern))
                        return ParsedStartCommand.Fail(json, "--allow requires a value.");
                    allowPatterns.Add(allowPattern!);
                    break;
                case "--forbid":
                    if (!TryReadValue(args, ref index, out var forbidPattern))
                        return ParsedStartCommand.Fail(json, "--forbid requires a value.");
                    forbidPatterns.Add(forbidPattern!);
                    break;
                case "--runs-root":
                    if (!TryReadValue(args, ref index, out runsRootPath))
                        return ParsedStartCommand.Fail(json, "--runs-root requires a value.");
                    break;
                case "--workspace-root":
                    if (!TryReadValue(args, ref index, out workspaceRootPath))
                        return ParsedStartCommand.Fail(json, "--workspace-root requires a value.");
                    break;
                case "--run-id":
                    if (!TryReadValue(args, ref index, out runId))
                        return ParsedStartCommand.Fail(json, "--run-id requires a value.");
                    break;
                case "--json":
                    break;
                default:
                    return ParsedStartCommand.Fail(json, $"unsupported patch start option: {arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(repoPath))
            return ParsedStartCommand.Fail(json, "--repo is required.");
        if (string.IsNullOrWhiteSpace(taskPath))
            return ParsedStartCommand.Fail(json, "--task is required.");
        if (!string.IsNullOrWhiteSpace(testCommand) && !string.IsNullOrWhiteSpace(testProfileName))
            return ParsedStartCommand.Fail(json, "--test and --test-profile are mutually exclusive.");

        return new ParsedStartCommand(repoPath, taskPath, testCommand, testProfileName, runsRootPath, workspaceRootPath, runId, allowPatterns, forbidPatterns, json, null);
    }

    private static ParsedFinishCommand ParseFinish(string[] args)
    {
        string? run = null;
        string? runsRootPath = null;
        string? testCommand = null;
        string? testProfileName = null;
        var skipTest = false;
        var json = HasJson(args);

        for (var index = 2; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--run":
                    if (!TryReadValue(args, ref index, out run))
                        return ParsedFinishCommand.Fail(json, "--run requires a value.");
                    break;
                case "--runs-root":
                    if (!TryReadValue(args, ref index, out runsRootPath))
                        return ParsedFinishCommand.Fail(json, "--runs-root requires a value.");
                    break;
                case "--test":
                    if (!TryReadValue(args, ref index, out testCommand))
                        return ParsedFinishCommand.Fail(json, "--test requires a value.");
                    break;
                case "--test-profile":
                    if (!TryReadValue(args, ref index, out testProfileName))
                        return ParsedFinishCommand.Fail(json, "--test-profile requires a value.");
                    break;
                case "--skip-test":
                    skipTest = true;
                    break;
                case "--json":
                    break;
                default:
                    return ParsedFinishCommand.Fail(json, $"unsupported patch finish option: {arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(run))
            return ParsedFinishCommand.Fail(json, "--run is required.");
        if (!string.IsNullOrWhiteSpace(testCommand) && !string.IsNullOrWhiteSpace(testProfileName))
            return ParsedFinishCommand.Fail(json, "--test and --test-profile are mutually exclusive.");

        return new ParsedFinishCommand(run, runsRootPath, testCommand, testProfileName, skipTest, json, null);
    }

    private static ParsedStatusCommand ParseStatus(string[] args)
    {
        string? run = null;
        string? runsRootPath = null;
        var json = HasJson(args);

        for (var index = 2; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--run":
                    if (!TryReadValue(args, ref index, out run))
                        return ParsedStatusCommand.Fail(json, "--run requires a value.");
                    break;
                case "--runs-root":
                    if (!TryReadValue(args, ref index, out runsRootPath))
                        return ParsedStatusCommand.Fail(json, "--runs-root requires a value.");
                    break;
                case "--json":
                    break;
                default:
                    return ParsedStatusCommand.Fail(json, $"unsupported patch status option: {arg}");
            }
        }

        if (string.IsNullOrWhiteSpace(run))
            return ParsedStatusCommand.Fail(json, "--run is required.");

        return new ParsedStatusCommand(run, runsRootPath, json, null);
    }

    private static async Task SaveRunAsync(PatchProposalRunDocument run, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(run.RunPath);
        var json = JsonSerializer.Serialize(run, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(run.RunPath, "run.json"), json, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PatchProposalRunDocument?> LoadRunAsync(string runPath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(runPath, "run.json");
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<PatchProposalRunDocument>(json, JsonOptions);
    }

    private static async Task<string> ReadGitValueAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.Stdout.Trim() : string.Empty;
    }

    private static Task<ProcessResult> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        RunProcessAsync("git", arguments, workingDirectory, cancellationToken);

    private static async Task<ProcessResult> RunShellAsync(string command, string workingDirectory, CancellationToken cancellationToken)
    {
        var fileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        string[] arguments = OperatingSystem.IsWindows()
            ? ["/d", "/s", "/c", command]
            : ["-c", command];

        return await RunProcessAsync(fileName, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.OutputDataReceived += (_, item) =>
        {
            if (item.Data is not null)
                output.AppendLine(item.Data);
        };
        process.ErrorDataReceived += (_, item) =>
        {
            if (item.Data is not null)
                error.AppendLine(item.Data);
        };

        try
        {
            if (!process.Start())
                return new ProcessResult(-1, string.Empty, $"could not start process: {fileName}");
        }
        catch (Win32Exception ex)
        {
            return new ProcessResult(-1, string.Empty, ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }

    private static string RenderTestResults(string testCommand, bool skipped, ProcessResult? result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Test Results");
        builder.AppendLine();
        builder.AppendLine("Boundary: tests were run only inside the disposable workspace. The source repository was not modified.");
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

        builder.AppendLine($"Command: `{testCommand}`");
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

    private static string RenderReviewSummary(PatchProposalRunDocument run, string[] changedFiles, string patchHash, bool testsPassed)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Manual Patch Proposal Review Summary");
        builder.AppendLine();
        builder.AppendLine($"Run ID: `{run.RunId}`");
        builder.AppendLine($"Base branch: `{run.BaseBranch}`");
        builder.AppendLine($"Base commit: `{run.BaseCommit}`");
        builder.AppendLine($"Source HEAD at finish: `{run.SourceHeadCommitAtFinish}`");
        builder.AppendLine(run.SourceHeadChangedSinceStart
            ? "Source HEAD changed since run start."
            : "Source HEAD did not change since run start.");
        builder.AppendLine($"Patch SHA-256: `{patchHash}`");
        builder.AppendLine($"Tests passed: `{testsPassed}`");
        builder.AppendLine($"File scope: {(run.BlockedFileCount == 0 ? "allowed" : "blocked")}");
        builder.AppendLine();
        builder.AppendLine("## Boundary");
        builder.AppendLine();
        builder.AppendLine("- This package is review evidence only.");
        builder.AppendLine("- The real source repository was not modified.");
        builder.AppendLine("- No source apply, approval, release decision, memory promotion, workflow continuation, or repository publication was performed.");
        builder.AppendLine();
        builder.AppendLine("## Changed files");
        builder.AppendLine();
        if (changedFiles.Length == 0)
        {
            builder.AppendLine("- No file changes detected.");
        }
        else
        {
            foreach (var item in changedFiles)
                builder.AppendLine($"- `{item}`");
        }

        return builder.ToString();
    }

    private static string RenderKnownRisks(PatchProposalRunDocument run, int changedFileCount, string patchHash, bool testsPassed, bool testsSkipped)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Known Risks");
        builder.AppendLine();
        builder.AppendLine("- Human review remains required before any source apply.");
        builder.AppendLine("- This package does not prove release readiness.");
        builder.AppendLine("- This package does not satisfy approval or policy requirements.");
        builder.AppendLine("- The disposable workspace was created from committed source repository state.");
        builder.AppendLine($"- Changed file count: `{changedFileCount}`.");
        builder.AppendLine($"- Patch SHA-256: `{patchHash}`.");

        if (run.SourceRepoHadUncommittedChanges)
            builder.AppendLine("- Source repository had uncommitted changes when the workspace was created; those changes were not copied into the disposable workspace.");

        if (testsSkipped)
            builder.AppendLine("- Tests were skipped for this package.");
        else if (!testsPassed)
            builder.AppendLine("- Test command returned a non-zero exit code.");

        return builder.ToString();
    }

    private static string RenderManualApplyInstructions(PatchProposalRunDocument run, string patchHash)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Manual Apply Instructions");
        builder.AppendLine();
        builder.AppendLine("Human review is required.");
        builder.AppendLine();
        builder.AppendLine("IronDev did not apply this patch to the real repository.");
        builder.AppendLine("IronDev did not commit.");
        builder.AppendLine("IronDev did not push.");
        builder.AppendLine();
        builder.AppendLine("From the source repository root, inspect whether the patch can be applied cleanly:");
        builder.AppendLine();
        builder.AppendLine("```powershell");
        builder.AppendLine($"git apply --check \"{Path.Combine(run.RunPath, "patch.diff")}\"");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("To apply manually after human review:");
        builder.AppendLine();
        builder.AppendLine("```powershell");
        builder.AppendLine($"git apply \"{Path.Combine(run.RunPath, "patch.diff")}\"");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("After applying, run the relevant tests independently from the source repository.");
        builder.AppendLine();
        builder.AppendLine("Recommended review steps before applying:");
        builder.AppendLine();
        builder.AppendLine("1. Read `review-summary.md`.");
        builder.AppendLine("2. Inspect `patch.diff` and `changed-files.txt`.");
        builder.AppendLine("3. Read `test-results.txt` and `known-risks.md`.");
        builder.AppendLine("4. Decide manually whether to apply, reject, or revise the patch.");
        builder.AppendLine();
        builder.AppendLine($"Patch SHA-256: `{patchHash}`");
        builder.AppendLine($"Base commit: `{run.BaseCommit}`");
        builder.AppendLine();
        builder.AppendLine("Boundary: this is manual human application outside IronDev. It is not IronDev source apply, not approval, not release readiness, and not merge/deploy authority.");
        return builder.ToString();
    }

    private static object Boundary() => new
    {
        reviewPackageOnly = true,
        disposableWorkspaceOnly = true,
        sourceRepositoryMutated = false,
        sourceApplied = false,
        gitCommitCreated = false,
        gitPushPerformed = false,
        pullRequestCreated = false,
        approvalGranted = false,
        policySatisfied = false,
        releaseApproved = false,
        workflowContinued = false,
        memoryPromoted = false,
        agentDispatched = false,
        modelCalled = false
    };

    private static void WriteJsonEnvelope(TextWriter output, string command, string status, object data, IReadOnlyList<string> warnings)
    {
        output.WriteLine(JsonSerializer.Serialize(
            new
            {
                ok = status is "succeeded",
                command,
                status,
                data,
                warnings,
                errors = Array.Empty<object>()
            },
            JsonOptions));
    }

    private static int WriteFailure(TextWriter output, TextWriter error, bool json, string command, string message)
    {
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(
                new
                {
                    ok = false,
                    command,
                    status = "failed",
                    data = (object?)null,
                    warnings = Array.Empty<string>(),
                    errors = new[] { new { code = "IRONDEV_PATCH_ERROR", message } }
                },
                JsonOptions));
        }
        else
        {
            error.WriteLine($"IRONDEV_PATCH_ERROR: {message}");
        }

        return 1;
    }

    private static int WriteUsageError(TextWriter error, string message)
    {
        error.WriteLine($"IRONDEV_PATCH_USAGE: {message}");
        error.WriteLine("Usage:");
        error.WriteLine("  irondev patch start --repo <repo-path> --task <task-file> (--test <command> | --test-profile <name>) [--allow <glob>] [--forbid <glob>] [--runs-root <path>] [--workspace-root <path>] [--run-id <id>] [--json]");
        error.WriteLine("  irondev patch finish --run <run-id-or-path> [--runs-root <path>] [--test <command> | --test-profile <name>] [--skip-test] [--json]");
        error.WriteLine("  irondev patch test --run <run-id-or-path> [--runs-root <path>] [--test <command> | --test-profile <name>] [--json]");
        error.WriteLine("  irondev patch status --run <run-id-or-path> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev patch list [--runs-root <path>] [--json]");
        error.WriteLine("  irondev patch cleanup --run <run-id-or-path> [--runs-root <path>] (--delete-workspace | --delete-run) [--json]");
        error.WriteLine("  irondev patch cleanup --older-than-days <n> --delete-workspaces [--runs-root <path>] [--json]");
        error.WriteLine("  irondev patch governance --run <run-id-or-path> [--runs-root <path>] [--json]");
        error.WriteLine("  irondev patch governance --inventory [--json]");
        return 2;
    }

    private static string ResolveRunPath(string run, string? runsRootPath)
    {
        var candidate = NormalizeFullPath(run);
        if (Directory.Exists(candidate) || File.Exists(Path.Combine(candidate, "run.json")) || Path.IsPathRooted(run))
            return candidate;

        var root = NormalizeFullPath(runsRootPath ?? DefaultRunsRoot());
        return Path.Combine(root, run.Trim());
    }

    private static bool TryReadValue(string[] args, ref int index, out string? value)
    {
        value = null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
            return false;

        value = args[++index];
        return true;
    }

    private static bool HasJson(string[] args) =>
        args.Any(arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase));

    private static string DefaultRunsRoot() =>
        Path.Combine(Path.GetTempPath(), DefaultRunsFolderName);

    private static string GenerateRunId() =>
        $"patch-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..29];

    private static bool IsSafeRunId(string runId) =>
        !string.IsNullOrWhiteSpace(runId) &&
        runId.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static bool IsForbiddenCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var normalized = command.Trim().ToLowerInvariant();
        var forbidden = new[]
        {
            "git push",
            "git commit",
            "git merge",
            "git tag",
            "gh pr",
            "release approve",
            "source apply"
        };

        return forbidden.Any(item => normalized.Contains(item, StringComparison.Ordinal));
    }

    private static string NormalizeFullPath(string path) =>
        Path.GetFullPath(path.Trim());

    private static bool IsSameOrUnderPath(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedCandidate = NormalizeForPathComparison(candidate);
        var normalizedRoot = NormalizeForPathComparison(root);
        return string.Equals(normalizedCandidate, normalizedRoot, comparison) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static string NormalizeForPathComparison(string path) =>
        NormalizeFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static string[] SplitLines(string value) =>
        value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record ParsedStartCommand(
        string? RepoPath,
        string? TaskPath,
        string? TestCommand,
        string? TestProfileName,
        string? RunsRootPath,
        string? WorkspaceRootPath,
        string? RunId,
        List<string> AllowPatterns,
        List<string> ForbidPatterns,
        bool Json,
        string? Error)
    {
        public static ParsedStartCommand Fail(bool json, string error) =>
            new(null, null, null, null, null, null, null, [], [], json, error);
    }

    private sealed record ParsedFinishCommand(
        string? Run,
        string? RunsRootPath,
        string? TestCommand,
        string? TestProfileName,
        bool SkipTest,
        bool Json,
        string? Error)
    {
        public static ParsedFinishCommand Fail(bool json, string error) =>
            new(null, null, null, null, false, json, error);
    }

    private sealed record ParsedStatusCommand(
        string? Run,
        string? RunsRootPath,
        bool Json,
        string? Error)
    {
        public static ParsedStatusCommand Fail(bool json, string error) =>
            new(null, null, json, error);
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

    public sealed class PatchProposalRunDocument
    {
        public string SchemaVersion { get; set; } = RunSchemaVersion;
        public string RunId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string SourceRepoPath { get; set; } = string.Empty;
        public string SourceRepoIdentity { get; set; } = string.Empty;
        public string RunsRootPath { get; set; } = string.Empty;
        public string RunPath { get; set; } = string.Empty;
        public string WorkspacePath { get; set; } = string.Empty;
        public string TaskSourcePath { get; set; } = string.Empty;
        public string TaskPath { get; set; } = string.Empty;
        public string BaseBranch { get; set; } = string.Empty;
        public string BaseCommit { get; set; } = string.Empty;
        public string SourceStatusPorcelainAtStart { get; set; } = string.Empty;
        public string SourceStatusPorcelainAtFinish { get; set; } = string.Empty;
        public bool SourceRepoDirtyAtStart { get; set; }
        public bool SourceRepoDirtyAtFinish { get; set; }
        public bool SourceHeadChangedSinceStart { get; set; }
        public bool SourceDirtyStateChangedSinceStart { get; set; }
        public string SourceHeadCommitAtFinish { get; set; } = string.Empty;
        public string WorkspaceCommitAtStart { get; set; } = string.Empty;
        public string WorkspaceCommitAtFinish { get; set; } = string.Empty;
        public string WorkspaceBranchAtStart { get; set; } = string.Empty;
        public string WorkspaceBranchAtFinish { get; set; } = string.Empty;
        public string WorkspaceRefAtStart { get; set; } = string.Empty;
        public string WorkspaceRefAtFinish { get; set; } = string.Empty;
        public string TestCommand { get; set; } = string.Empty;
        public string? TestProfileName { get; set; }
        public string TestStatus { get; set; } = "NotRun";
        public string[] AllowedFileGlobs { get; set; } = [];
        public string[] ForbiddenFileGlobs { get; set; } = [];
        public DateTimeOffset StartedUtc { get; set; }
        public DateTimeOffset? FinishedUtc { get; set; }
        public DateTimeOffset LastUpdatedUtc { get; set; }
        public string CleanupStatus { get; set; } = "NotCleaned";
        public DateTimeOffset? CleanupUpdatedUtc { get; set; }
        public bool SourceRepoHadUncommittedChanges { get; set; }
        public bool SourceRepoMutated { get; set; }
        public bool SourceApplied { get; set; }
        public bool GitCommitCreated { get; set; }
        public bool GitPushPerformed { get; set; }
        public bool ApprovalGranted { get; set; }
        public bool PolicySatisfied { get; set; }
        public bool ReleaseApproved { get; set; }
        public bool WorkflowContinued { get; set; }
        public bool MemoryPromoted { get; set; }
        public bool AgentDispatched { get; set; }
        public int? TestExitCode { get; set; }
        public string? PatchSha256 { get; set; }
        public string[] ChangedFiles { get; set; } = [];
        public int ChangedFileCount { get; set; }
        public int BlockedFileCount { get; set; }
        public string[] Artifacts { get; set; } = [];
    }
}
