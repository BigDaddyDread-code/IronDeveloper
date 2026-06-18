using System.Diagnostics;
using System.Text.Json;
using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.Tools;
using WorkspaceToolRequest = IronDev.Core.Tools.ToolRequest;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockACGovernedWorkspaceToolsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void BlockAC_ToolRequestContractAndActionSpinePreserveBoundaries()
    {
        var request = new WorkspaceToolRequest
        {
            ToolRequestId = "tool-req-1",
            RunId = "run-1",
            RequestKind = ToolRequestKind.PatchRunTest,
            ToolName = "workspace-shell",
            ToolKind = ToolKind.DotNetCommand,
            Command = "dotnet --version",
            ResolvedCommand = "dotnet --version",
            WorkingDirectory = "workspace",
            WorkspacePath = "workspace",
            SourceRepoPath = "source",
            RequestedBy = "IronDevCli",
            SourceComponent = "IronDev.Cli.patch",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            RiskClassification = ToolRiskClassification.WorkspaceReadOnly,
            EvidenceRefs = [new ToolEvidenceRef { RefId = "run.json", EvidenceKind = "PatchProposalRun", SafeSummary = "Patch run evidence." }]
        };

        var roundTrip = JsonSerializer.Deserialize<WorkspaceToolRequest>(JsonSerializer.Serialize(request, JsonOptions), JsonOptions);
        Assert.IsNotNull(roundTrip);
        Assert.AreEqual(ToolRequestKind.PatchRunTest, roundTrip.RequestKind);
        Assert.IsFalse(roundTrip.Boundary.SourceApplied);
        Assert.IsFalse(roundTrip.Boundary.MemoryPromoted);

        foreach (var action in new[]
                 {
                     GovernedActionKind.WorkspaceToolRequestCreated,
                     GovernedActionKind.WorkspaceToolGateEvaluated,
                     GovernedActionKind.WorkspaceCommandExecuted,
                     GovernedActionKind.WorkspaceToolResultRecorded
                 })
        {
            var entry = AuthorityActionInventory.Get(action);
            Assert.AreEqual(GovernedActionClassification.NonAuthority, entry.Classification);
            Assert.IsTrue(entry.AllowedInCurrentBlock);
            Assert.IsFalse(entry.RequiresConscience);
            Assert.IsFalse(entry.RequiresThoughtLedger);
        }

        var genericToolExecution = AuthorityActionInventory.Get(GovernedActionKind.ToolExecution);
        Assert.AreEqual(GovernedActionClassification.AuthorityBearing, genericToolExecution.Classification);
        Assert.IsFalse(genericToolExecution.AllowedInCurrentBlock);

        var directGitPush = AuthorityActionInventory.Get(GovernedActionKind.DirectGitPush);
        Assert.AreEqual(GovernedActionClassification.ForbiddenOrUnsupported, directGitPush.Classification);
        Assert.IsFalse(directGitPush.AllowedInCurrentBlock);
    }

    [TestMethod]
    public void BlockAC_WorkspaceToolGate_AllowsWorkspaceCommandAndBlocksDangerousShapes()
    {
        using var fixture = BlockACFixture.Create();
        var request = fixture.ToolRequest("dotnet --version", fixture.WorkspacePath);

        var allowed = WorkspaceToolGateEvaluator.Evaluate(request);
        Assert.AreEqual(WorkspaceToolGateDecisionOutcome.Allow, allowed.Decision);
        Assert.AreEqual(0, allowed.Reasons.Length);

        var outside = WorkspaceToolGateEvaluator.Evaluate(request with { WorkingDirectory = fixture.SourceRepoPath });
        Assert.AreEqual(WorkspaceToolGateDecisionOutcome.Block, outside.Decision);
        CollectionAssert.Contains(outside.Reasons, "WorkingDirectoryOutsideWorkspace");
        CollectionAssert.Contains(outside.Reasons, "WorkingDirectoryIsSourceRepo");

        foreach (var item in new Dictionary<string, string>
                 {
                     ["git push origin main"] = "CommandRequestsGitPush",
                     ["git commit -m nope"] = "CommandRequestsGitCommit",
                     ["gh pr create"] = "CommandRequestsPullRequest",
                     ["source apply patch.diff"] = "CommandRequestsSourceApply",
                     ["promote memory now"] = "CommandRequestsMemoryMutation",
                     ["continue workflow"] = "CommandRequestsWorkflowContinuation",
                     ["release approve"] = "CommandRequestsReleaseOrDeployment"
                 })
        {
            var blocked = WorkspaceToolGateEvaluator.Evaluate(fixture.ToolRequest(item.Key, fixture.WorkspacePath));
            Assert.AreEqual(WorkspaceToolGateDecisionOutcome.Block, blocked.Decision, item.Key);
            CollectionAssert.Contains(blocked.Reasons, item.Value, item.Key);
        }
    }

    [TestMethod]
    public async Task BlockAC_PatchTest_UsesToolRequestGateAndResultArtifacts()
    {
        using var fixture = await BlockACFixture.StartPatchRunAsync().ConfigureAwait(false);

        var result = await RunPatchCliAsync("patch", "test", "--run", fixture.RunPath, "--test", "dotnet --version", "--json").ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Error);
        var requests = ReadJsonLines<WorkspaceToolRequest>(Path.Combine(fixture.RunPath, "tool-requests.jsonl"));
        var gates = ReadJsonLines<WorkspaceToolGateDecision>(Path.Combine(fixture.RunPath, "tool-gate-decisions.jsonl"));
        var results = ReadJsonLines<ToolExecutionResult>(Path.Combine(fixture.RunPath, "tool-results.jsonl"));
        Assert.IsTrue(requests.Any(item => item.RequestKind == ToolRequestKind.PatchRunTest));
        Assert.IsTrue(gates.Any(item => item.Decision == WorkspaceToolGateDecisionOutcome.Allow));
        Assert.IsTrue(results.Any(item => item.WasExecuted && item.ExitCode == 0));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.RunPath, "test-results.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.RunPath, "test-output-summary.md")));
        Assert.IsTrue(Directory.Exists(Path.Combine(fixture.RunPath, "tool-output")));

        var toolInspection = await RunPatchCliAsync("patch", "tools", "--run", fixture.RunPath, "--json").ConfigureAwait(false);
        Assert.AreEqual(0, toolInspection.ExitCode, toolInspection.Error);
        StringAssert.Contains(toolInspection.Output, "requests");
    }

    [TestMethod]
    public async Task BlockAC_PatchTest_BlocksForbiddenCommandBeforeExecutionAndKeepsSourceRepoClean()
    {
        using var fixture = await BlockACFixture.StartPatchRunAsync().ConfigureAwait(false);

        var result = await RunPatchCliAsync("patch", "test", "--run", fixture.RunPath, "--test", "git push origin main", "--json").ConfigureAwait(false);

        Assert.AreEqual(1, result.ExitCode);
        var gates = ReadJsonLines<WorkspaceToolGateDecision>(Path.Combine(fixture.RunPath, "tool-gate-decisions.jsonl"));
        var results = ReadJsonLines<ToolExecutionResult>(Path.Combine(fixture.RunPath, "tool-results.jsonl"));
        Assert.IsTrue(gates.Any(item => item.Decision == WorkspaceToolGateDecisionOutcome.Block && item.Reasons.Contains("CommandRequestsGitPush")));
        Assert.IsTrue(results.Any(item => !item.WasExecuted && item.ExitCode is null));
        var toolOutputPath = Path.Combine(fixture.RunPath, "tool-output");
        Assert.IsTrue(!Directory.Exists(toolOutputPath) || !Directory.EnumerateFiles(toolOutputPath, "*", SearchOption.AllDirectories).Any());
        StringAssert.Contains(File.ReadAllText(Path.Combine(fixture.RunPath, "test-results.txt")), "blocked by workspace tool gate");

        var sourceStatus = RunProcess("git", ["status", "--porcelain=v1"], fixture.SourceRepoPath);
        Assert.AreEqual(0, sourceStatus.ExitCode);
        Assert.AreEqual(string.Empty, sourceStatus.Stdout.Trim());
    }

    [TestMethod]
    public async Task BlockAC_PatchFinish_TestPathUsesToolGateAndResultEvidence()
    {
        using var fixture = await BlockACFixture.StartPatchRunAsync().ConfigureAwait(false);
        await File.AppendAllTextAsync(Path.Combine(fixture.WorkspacePath, "README.md"), $"{Environment.NewLine}Block AC change.{Environment.NewLine}").ConfigureAwait(false);

        var result = await RunPatchCliAsync("patch", "finish", "--run", fixture.RunPath, "--test", "dotnet --version", "--json").ConfigureAwait(false);

        Assert.AreEqual(0, result.ExitCode, result.Error);
        var requests = ReadJsonLines<WorkspaceToolRequest>(Path.Combine(fixture.RunPath, "tool-requests.jsonl"));
        var gates = ReadJsonLines<WorkspaceToolGateDecision>(Path.Combine(fixture.RunPath, "tool-gate-decisions.jsonl"));
        var results = ReadJsonLines<ToolExecutionResult>(Path.Combine(fixture.RunPath, "tool-results.jsonl"));
        Assert.IsTrue(requests.Any(item => item.RequestKind == ToolRequestKind.PatchRunFinishTest));
        Assert.IsTrue(gates.Any(item => item.Decision == WorkspaceToolGateDecisionOutcome.Allow));
        Assert.IsTrue(results.Any(item => item.WasExecuted && item.ExitCode == 0));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.RunPath, "patch.diff")));
        StringAssert.Contains(File.ReadAllText(Path.Combine(fixture.RunPath, "review-summary.md")), "Tests passed: `True`");
    }

    [TestMethod]
    public void BlockAC_StaticBoundary_PatchTestAndFinishDoNotUseLooseUserShellPath()
    {
        var repoRoot = FindRepositoryRoot();
        var usability = File.ReadAllText(Path.Combine(repoRoot, "tools", "IronDev.Cli", "CliPatchProposalUsability.cs"));
        Assert.IsFalse(usability.Contains("RunShellAsync(resolution.Command!", StringComparison.Ordinal));
        Assert.IsFalse(usability.Contains("RunShellAsync(testResolution.Command!", StringComparison.Ordinal));
        Assert.IsTrue(usability.Contains("RunWorkspaceToolCommandAsync", StringComparison.Ordinal));

        var tools = File.ReadAllText(Path.Combine(repoRoot, "tools", "IronDev.Cli", "CliPatchProposalTools.cs"));
        Assert.IsFalse(tools.Contains("git push", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(tools.Contains("gh pr create", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunPatchCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCliPatchProposal.HandleAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static T[] ReadJsonLines<T>(string path) =>
        File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<T>(line, JsonOptions))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();

    private static ProcessResult RunProcess(string fileName, string[] args, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

    private sealed class BlockACFixture : IDisposable
    {
        public required string RootPath { get; set; }
        public required string SourceRepoPath { get; set; }
        public required string RunsRootPath { get; set; }
        public required string RunPath { get; set; }
        public required string WorkspacePath { get; set; }

        public static BlockACFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"irondev-block-ac-{Guid.NewGuid():N}");
            var source = Path.Combine(root, "source");
            var workspace = Path.Combine(root, "workspace");
            var runs = Path.Combine(root, "runs");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(runs);
            return new BlockACFixture
            {
                RootPath = root,
                SourceRepoPath = source,
                RunsRootPath = runs,
                RunPath = Path.Combine(runs, "run-ac"),
                WorkspacePath = workspace
            };
        }

        public static async Task<BlockACFixture> StartPatchRunAsync()
        {
            var fixture = Create();
            Directory.CreateDirectory(fixture.RunPath);
            Directory.Delete(fixture.RunPath);
            File.WriteAllText(Path.Combine(fixture.SourceRepoPath, "README.md"), "Block AC fixture." + Environment.NewLine);
            RunProcess("git", ["init"], fixture.SourceRepoPath).EnsureSuccess();
            RunProcess("git", ["config", "user.email", "irondev@example.test"], fixture.SourceRepoPath).EnsureSuccess();
            RunProcess("git", ["config", "user.name", "IronDev Test"], fixture.SourceRepoPath).EnsureSuccess();
            RunProcess("git", ["add", "README.md"], fixture.SourceRepoPath).EnsureSuccess();
            RunProcess("git", ["commit", "-m", "initial"], fixture.SourceRepoPath).EnsureSuccess();

            var taskPath = Path.Combine(fixture.RootPath, "task.md");
            File.WriteAllText(taskPath, "# Task" + Environment.NewLine + "Make a safe patch." + Environment.NewLine);

            var start = await RunPatchCliAsync(
                "patch",
                "start",
                "--repo",
                fixture.SourceRepoPath,
                "--task",
                taskPath,
                "--test",
                "dotnet --version",
                "--runs-root",
                fixture.RunsRootPath,
                "--run-id",
                "run-ac",
                "--json").ConfigureAwait(false);

            if (start.ExitCode != 0)
                throw new InvalidOperationException(start.Error + start.Output);

            fixture.RunPath = Path.Combine(fixture.RunsRootPath, "run-ac");
            fixture.WorkspacePath = Path.Combine(fixture.RunsRootPath, "run-ac", "workspace");
            return fixture;
        }

        public WorkspaceToolRequest ToolRequest(string command, string workingDirectory) =>
            new()
            {
                ToolRequestId = "tool-req-test",
                RunId = "run-ac",
                RequestKind = ToolRequestKind.PatchRunTest,
                ToolName = "workspace-shell",
                ToolKind = ToolCommandRiskClassifier.DetectKind(command),
                Command = command,
                ResolvedCommand = command,
                WorkingDirectory = workingDirectory,
                WorkspacePath = WorkspacePath,
                SourceRepoPath = SourceRepoPath,
                RequestedBy = "IronDevCli",
                SourceComponent = "IronDev.Cli.patch",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                RiskClassification = ToolCommandRiskClassifier.Classify(command)
            };

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                    Directory.Delete(RootPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

internal static class BlockACProcessResultExtensions
{
    public static void EnsureSuccess(this BlockACGovernedWorkspaceToolsTests.ProcessResult result)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException(result.Stderr);
    }
}
