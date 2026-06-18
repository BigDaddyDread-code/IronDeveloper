using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Ai;
using IronDev.Core.Governance;
using IronDev.Core.Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockADAiPatchAssistanceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockAD_ActionSpine_RegistersModelAssistanceAsNonAuthority()
    {
        foreach (var action in new[]
                 {
                     GovernedActionKind.PatchContextBundleCreated,
                     GovernedActionKind.ModelPatchSuggestionRequested,
                     GovernedActionKind.ModelPatchSuggestionReceived,
                     GovernedActionKind.WorkspacePatchEditApplied,
                     GovernedActionKind.ModelTestFailureAnalysisRequested,
                     GovernedActionKind.ModelTestFailureAnalysisReceived,
                     GovernedActionKind.ModelPatchReviewRequested,
                     GovernedActionKind.ModelPatchReviewReceived,
                     GovernedActionKind.PatchRefinementIterationCompleted
                 })
        {
            var entry = AuthorityActionInventory.Get(action);
            Assert.AreEqual(GovernedActionClassification.NonAuthority, entry.Classification, action.ToString());
            Assert.IsTrue(entry.AllowedInCurrentBlock, action.ToString());
            Assert.IsFalse(entry.RequiresConscience, action.ToString());
            Assert.IsFalse(entry.RequiresThoughtLedger, action.ToString());
        }

        Assert.AreEqual(GovernedActionClassification.AuthorityBearing, GovernedActionClassifier.Classify(GovernedActionKind.ToolExecution));
        Assert.AreEqual(GovernedActionClassification.AuthorityBearing, GovernedActionClassifier.Classify(GovernedActionKind.SourceApply));
        Assert.AreEqual(GovernedActionClassification.AuthorityBearing, GovernedActionClassifier.Classify(GovernedActionKind.MemoryPromotion));
        Assert.IsFalse(AuthorityActionInventory.Get(GovernedActionKind.ToolExecution).AllowedInCurrentBlock);
    }

    [TestMethod]
    public void BlockAD_ContextBundle_IsBoundedAndNonAuthoritative()
    {
        var root = CreateTempRoot();
        try
        {
            var workspace = Path.Combine(root, "workspace");
            Directory.CreateDirectory(workspace);
            File.WriteAllText(Path.Combine(root, "task.md"), "Make a bounded AI-assisted patch.");
            File.WriteAllText(Path.Combine(workspace, "000-large.md"), new string('a', 200));
            File.WriteAllText(Path.Combine(workspace, "notes.txt"), "extra context");

            var bundle = PatchTaskContextBundleBuilder.Build(
                "run-ad",
                Path.Combine(root, "task.md"),
                Path.Combine(root, "source"),
                workspace,
                "main",
                "abc123",
                "dotnet --version",
                evidenceRefs: ["run.json"],
                options: new PatchTaskContextBuildOptions { MaxFiles = 1, MaxBytesPerFile = 20, MaxTotalBytes = 20 });

            Assert.AreEqual("run-ad", bundle.RunId);
            StringAssert.Contains(bundle.TaskText, "bounded AI-assisted patch");
            Assert.AreEqual(1, bundle.FileSnapshots.Length);
            Assert.IsTrue(bundle.FileSnapshots[0].Truncated);
            Assert.IsTrue(bundle.FileSnapshotLimitHit || bundle.ByteLimitHit);
            Assert.IsFalse(bundle.Boundary.SourceApplied);
            Assert.IsFalse(bundle.Boundary.ApprovalGranted);
            Assert.IsFalse(bundle.Boundary.HiddenChainOfThoughtStored);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public async Task BlockAD_PatchAssist_CreatesModelArtifactsAndAppliesOnlyWorkspaceEdit()
    {
        using var fixture = await BlockADFixture.StartPatchRunAsync("ad-assist", "Add AI assisted note.", "dotnet --version").ConfigureAwait(false);

        var assist = await RunPatchCliAsync("patch", "assist", "--run", fixture.RunPath, "--provider", "deterministic", "--json").ConfigureAwait(false);

        Assert.AreEqual(0, assist.ExitCode, assist.Error + assist.Output);
        AssertArtifactExists(fixture.RunPath, "task-context.md");
        AssertArtifactExists(fixture.RunPath, "task-context.json");
        AssertArtifactExists(fixture.RunPath, "model-requests.jsonl");
        AssertArtifactExists(fixture.RunPath, "model-responses.jsonl");
        AssertArtifactExists(fixture.RunPath, "patch-suggestions.jsonl");
        AssertArtifactExists(fixture.RunPath, "model-edit-plan.json");
        AssertArtifactExists(fixture.RunPath, "model-response.md");
        AssertArtifactExists(fixture.RunPath, "ai-assist-summary.md");

        var workspaceReadme = await File.ReadAllTextAsync(Path.Combine(fixture.WorkspacePath, "README.md")).ConfigureAwait(false);
        StringAssert.Contains(workspaceReadme, "AI assistance suggestion from deterministic provider.");
        var sourceReadme = await File.ReadAllTextAsync(Path.Combine(fixture.SourceRepoPath, "README.md")).ConfigureAwait(false);
        Assert.IsFalse(sourceReadme.Contains("AI assistance suggestion", StringComparison.Ordinal));
        Assert.AreEqual(string.Empty, RunProcess("git", ["status", "--porcelain=v1"], fixture.SourceRepoPath).Stdout.Trim());

        var requestText = await File.ReadAllTextAsync(Path.Combine(fixture.RunPath, "model-requests.jsonl")).ConfigureAwait(false);
        StringAssert.Contains(requestText, "PatchSuggestion");
        Assert.IsFalse(requestText.Contains("chain-of-thought", StringComparison.OrdinalIgnoreCase));

        var events = ReadGovernanceEvents(fixture.RunPath);
        Assert.IsTrue(events.Any(item => item.ActionKind == nameof(GovernedActionKind.PatchContextBundleCreated)));
        Assert.IsTrue(events.Any(item => item.ActionKind == nameof(GovernedActionKind.ModelPatchSuggestionRequested) && item.Boundary.ModelCalled));
        Assert.IsTrue(events.Any(item => item.ActionKind == nameof(GovernedActionKind.WorkspacePatchEditApplied)));
        AssertNoAuthority(events);
    }

    [TestMethod]
    public void BlockAD_WorkspacePatchEditor_BlocksUnsafeEditsBeforeMutation()
    {
        var root = CreateTempRoot();
        try
        {
            var workspace = Path.Combine(root, "workspace");
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(Path.Combine(workspace, ".git"));
            File.WriteAllText(Path.Combine(workspace, "README.md"), "safe");
            File.WriteAllText(Path.Combine(source, "README.md"), "source");

            var plan = new PatchEditPlan
            {
                PatchEditPlanId = "edit-plan-test",
                RunId = "run-ad",
                ModelResponseId = "model-response-test",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Edits =
                [
                    new PatchEdit { Path = "README.md", Operation = PatchEditOperation.AppendText, NewContent = "\nsafe edit\n", Rationale = "workspace only", Risk = "low" },
                    new PatchEdit { Path = Path.Combine(source, "README.md"), Operation = PatchEditOperation.AppendText, NewContent = "\nsource edit\n", Rationale = "outside", Risk = "blocked" },
                    new PatchEdit { Path = ".git/config", Operation = PatchEditOperation.AppendText, NewContent = "bad", Rationale = "git", Risk = "blocked" },
                    new PatchEdit { Path = "README.md", Operation = PatchEditOperation.DeleteFile, Rationale = "delete", Risk = "blocked" }
                ]
            };

            var result = WorkspacePatchEditor.Apply(workspace, source, plan);
            Assert.AreEqual(4, result.Results.Length);
            Assert.AreEqual(1, result.Results.Count(item => item.Applied));
            Assert.IsTrue(result.Results.Any(item => item.Reasons.Contains("PathUnderSourceRepository")));
            Assert.IsTrue(result.Results.Any(item => item.Reasons.Contains("PathUnderGitDirectory")));
            Assert.IsTrue(result.Results.Any(item => item.Reasons.Contains("UnsupportedEditOperation")));
            Assert.IsFalse(File.ReadAllText(Path.Combine(source, "README.md")).Contains("source edit", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }
    [TestMethod]
    public async Task BlockAD_Refine_IsBoundedAndRunsTestsThroughToolGate()
    {
        using var fixture = await BlockADFixture.StartPatchRunAsync("ad-refine", "Refine after a test run.", "dotnet --version").ConfigureAwait(false);

        var tooMany = await RunPatchCliAsync("patch", "refine", "--run", fixture.RunPath, "--max-iterations", "4", "--json").ConfigureAwait(false);
        Assert.AreEqual(1, tooMany.ExitCode);
        StringAssert.Contains(tooMany.Output, "cannot exceed 3");

        var refine = await RunPatchCliAsync("patch", "refine", "--run", fixture.RunPath, "--max-iterations", "2", "--json").ConfigureAwait(false);

        Assert.AreEqual(0, refine.ExitCode, refine.Error + refine.Output);
        AssertArtifactExists(fixture.RunPath, "test-failure-analysis.md");
        AssertArtifactExists(fixture.RunPath, "refinement-iterations.jsonl");
        AssertArtifactExists(fixture.RunPath, "tool-requests.jsonl");
        AssertArtifactExists(fixture.RunPath, "tool-gate-decisions.jsonl");
        AssertArtifactExists(fixture.RunPath, "tool-results.jsonl");

        var requests = ReadJsonLines<IronDev.Core.Tools.ToolRequest>(Path.Combine(fixture.RunPath, "tool-requests.jsonl"));
        Assert.IsTrue(requests.Any(item => item.RequestKind == ToolRequestKind.PatchRunTest));
        var iterations = ReadJsonLines<RefinementIterationRecord>(Path.Combine(fixture.RunPath, "refinement-iterations.jsonl"));
        Assert.AreEqual(1, iterations.Length);
        Assert.IsTrue(iterations[0].TestCommandExecutedThroughToolGate);
        Assert.IsTrue(iterations[0].TestsPassed);
    }

    [TestMethod]
    public async Task BlockAD_PatchReview_CreatesReviewEvidenceWithoutApprovalVerdict()
    {
        using var fixture = await BlockADFixture.StartPatchRunAsync("ad-review", "Create reviewable AI patch evidence.", "dotnet --version").ConfigureAwait(false);
        var assist = await RunPatchCliAsync("patch", "assist", "--run", fixture.RunPath, "--json").ConfigureAwait(false);
        Assert.AreEqual(0, assist.ExitCode, assist.Error + assist.Output);

        var finish = await RunPatchCliAsync("patch", "finish", "--run", fixture.RunPath, "--skip-test", "--json").ConfigureAwait(false);
        Assert.AreEqual(0, finish.ExitCode, finish.Error + finish.Output);

        var review = await RunPatchCliAsync("patch", "review", "--run", fixture.RunPath, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, review.ExitCode, review.Error + review.Output);
        AssertArtifactExists(fixture.RunPath, "ai-review.md");
        AssertArtifactExists(fixture.RunPath, "ai-review.json");
        var reviewText = await File.ReadAllTextAsync(Path.Combine(fixture.RunPath, "ai-review.md")).ConfigureAwait(false);
        StringAssert.Contains(reviewText, "Human review remains required");
        Assert.IsFalse(reviewText.Contains("Approved", StringComparison.Ordinal));
        Assert.IsFalse(reviewText.Contains("ReleaseReady", StringComparison.Ordinal));
        Assert.IsFalse(reviewText.Contains("SafeToMerge", StringComparison.Ordinal));
        Assert.IsFalse(reviewText.Contains("ApplyAutomatically", StringComparison.Ordinal));

        var parsed = JsonSerializer.Deserialize<AiPatchReview>(await File.ReadAllTextAsync(Path.Combine(fixture.RunPath, "ai-review.json")).ConfigureAwait(false), JsonOptions);
        Assert.IsNotNull(parsed);
        Assert.IsTrue(AiPatchReviewValidator.IsAllowedVerdict(parsed.Verdict));
        Assert.IsTrue(parsed.RequiresHumanReview);
        Assert.IsFalse(parsed.Boundary.SourceApplied);
    }

    [TestMethod]
    public async Task BlockAD_AiInspection_IsReadOnlyAndReportsArtifacts()
    {
        using var fixture = await BlockADFixture.StartPatchRunAsync("ad-ai-inspect", "Inspect AI artifacts.", "dotnet --version").ConfigureAwait(false);
        var assist = await RunPatchCliAsync("patch", "assist", "--run", fixture.RunPath, "--json").ConfigureAwait(false);
        Assert.AreEqual(0, assist.ExitCode, assist.Error + assist.Output);

        var inspect = await RunPatchCliAsync("patch", "ai", "--run", fixture.RunPath, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, inspect.ExitCode, inspect.Error + inspect.Output);
        StringAssert.Contains(inspect.Output, "task-context.md");
        StringAssert.Contains(inspect.Output, "model-requests.jsonl");
    }

    [TestMethod]
    public void BlockAD_StaticBoundary_DoesNotAddApiSqlUiOrBroadRuntimePaths()
    {
        var repoRoot = FindRepositoryRoot();
        var core = File.ReadAllText(Path.Combine(repoRoot, "IronDev.Core", "Ai", "PatchModelContracts.cs"));
        var cli = File.ReadAllText(Path.Combine(repoRoot, "tools", "IronDev.Cli", "CliPatchProposalAi.cs"));
        var receipt = File.ReadAllText(Path.Combine(repoRoot, "Docs", "receipts", "PR262_266_AI_PATCH_ASSISTANCE.md"));

        foreach (var text in new[] { core, cli })
        {
            Assert.IsFalse(text.Contains("Controller", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("SqlConnection", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("DbContext", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("IHostedService", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("BackgroundService", StringComparison.Ordinal));
            Assert.IsFalse(text.Contains("git push", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(text.Contains("gh pr create", StringComparison.OrdinalIgnoreCase));
        }

        Assert.IsFalse(cli.Contains("irondev ai run", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cli.Contains("irondev agent run", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(receipt, "Model output is proposal evidence only.");
        StringAssert.Contains(receipt, "It does not promote memory.");
    }

    private static void AssertNoAuthority(IEnumerable<RunScopedGovernanceEvent> events)
    {
        foreach (var evt in events)
        {
            Assert.IsFalse(evt.Boundary.SourceRepoMutated, evt.ActionKind);
            Assert.IsFalse(evt.Boundary.SourceApplied, evt.ActionKind);
            Assert.IsFalse(evt.Boundary.GitCommitCreated, evt.ActionKind);
            Assert.IsFalse(evt.Boundary.GitPushPerformed, evt.ActionKind);
            Assert.IsFalse(evt.Boundary.PullRequestCreated, evt.ActionKind);
            Assert.IsFalse(evt.Boundary.ApprovalGranted, evt.ActionKind);
            Assert.IsFalse(evt.Boundary.PolicySatisfied, evt.ActionKind);
            Assert.IsFalse(evt.Boundary.ReleaseApproved, evt.ActionKind);
            Assert.IsFalse(evt.Boundary.WorkflowContinued, evt.ActionKind);
            Assert.IsFalse(evt.Boundary.MemoryPromoted, evt.ActionKind);
            Assert.IsFalse(evt.Boundary.AgentDispatched, evt.ActionKind);
        }
    }

    private static RunScopedGovernanceEvent[] ReadGovernanceEvents(string runPath) =>
        File.ReadAllLines(Path.Combine(runPath, "governance-events.jsonl"))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<RunScopedGovernanceEvent>(line, JsonOptions))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();

    private static T[] ReadJsonLines<T>(string path) =>
        File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<T>(line, JsonOptions))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();

    private static async Task<(int ExitCode, string Output, string Error)> RunPatchCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCliPatchProposal.HandleAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static void AssertArtifactExists(string runPath, string artifact) =>
        Assert.IsTrue(File.Exists(Path.Combine(runPath, artifact)), $"Missing artifact {artifact} in {runPath}.");

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

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "irondev-block-ad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup only.
        }
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

    public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

    private sealed class BlockADFixture : IDisposable
    {
        public required string RootPath { get; init; }
        public required string SourceRepoPath { get; init; }
        public required string RunsRootPath { get; init; }
        public required string RunPath { get; set; }
        public required string WorkspacePath { get; set; }

        public static async Task<BlockADFixture> StartPatchRunAsync(string runId, string taskText, string testCommand)
        {
            var root = CreateTempRoot();
            var source = Path.Combine(root, "source");
            var runs = Path.Combine(root, "runs");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(runs);
            File.WriteAllText(Path.Combine(source, "README.md"), "Initial readme." + Environment.NewLine);
            RunProcess("git", ["init"], source).EnsureSuccess();
            RunProcess("git", ["config", "user.email", "block-ad@example.test"], source).EnsureSuccess();
            RunProcess("git", ["config", "user.name", "Block AD Test"], source).EnsureSuccess();
            RunProcess("git", ["add", "README.md"], source).EnsureSuccess();
            RunProcess("git", ["commit", "-m", "initial"], source).EnsureSuccess();

            var task = Path.Combine(root, "task.md");
            File.WriteAllText(task, "# Task" + Environment.NewLine + taskText + Environment.NewLine);
            var start = await RunPatchCliAsync("patch", "start", "--repo", source, "--task", task, "--test", testCommand, "--runs-root", runs, "--run-id", runId, "--json").ConfigureAwait(false);
            if (start.ExitCode != 0)
                throw new InvalidOperationException(start.Error + start.Output);

            return new BlockADFixture
            {
                RootPath = root,
                SourceRepoPath = source,
                RunsRootPath = runs,
                RunPath = Path.Combine(runs, runId),
                WorkspacePath = Path.Combine(runs, runId, "workspace")
            };
        }

        public void Dispose() => TryDelete(RootPath);
    }
}

internal static class BlockADProcessResultExtensions
{
    public static void EnsureSuccess(this BlockADAiPatchAssistanceTests.ProcessResult result)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException(result.Stderr + result.Stdout);
    }
}
