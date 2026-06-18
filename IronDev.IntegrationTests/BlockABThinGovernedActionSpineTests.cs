using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockABThinGovernedActionSpineTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockAB_GovernedActionClassifier_KeepsPatchEventsNonAuthorityAndHighRiskLocked()
    {
        Assert.AreEqual(GovernedActionClassification.NonAuthority, GovernedActionClassifier.Classify(GovernedActionKind.PatchArtifactExported));
        Assert.AreEqual(GovernedActionClassification.AuthorityBearing, GovernedActionClassifier.Classify(GovernedActionKind.SourceApply));
        Assert.AreEqual(GovernedActionClassification.AuthorityBearing, GovernedActionClassifier.Classify(GovernedActionKind.MemoryPromotion));
        Assert.AreEqual(GovernedActionClassification.ForbiddenOrUnsupported, GovernedActionClassifier.Classify(GovernedActionKind.DirectGitPush));
        Assert.AreEqual(GovernedActionClassification.ForbiddenOrUnsupported, GovernedActionClassifier.Classify("NotARealAction"));

        Assert.IsTrue(GovernedActionClassifier.IsAllowedInCurrentBlock(GovernedActionKind.PatchArtifactExported));
        Assert.IsFalse(GovernedActionClassifier.IsAllowedInCurrentBlock(GovernedActionKind.SourceApply));
        Assert.IsTrue(GovernedActionClassifier.RequiresConscience(GovernedActionKind.SourceApply));
        Assert.IsTrue(GovernedActionClassifier.RequiresThoughtLedger(GovernedActionKind.SourceApply));
    }

    [TestMethod]
    public void BlockAB_AuthorityActionInventory_RegistersEveryKnownActionAndDoesNotGrantAuthority()
    {
        var knownKinds = Enum.GetValues<GovernedActionKind>()
            .Where(kind => kind != GovernedActionKind.Unknown)
            .ToArray();

        foreach (var kind in knownKinds)
        {
            var entry = AuthorityActionInventory.Get(kind);
            Assert.AreEqual(kind, entry.ActionKind);
            Assert.AreEqual(GovernedActionClassifier.Classify(kind), entry.Classification);
        }

        foreach (var entry in AuthorityActionInventory.All.Where(entry => entry.Classification == GovernedActionClassification.AuthorityBearing))
        {
            Assert.IsFalse(entry.AllowedInCurrentBlock, entry.ActionKind.ToString());
            Assert.IsTrue(entry.RequiresConscience, entry.ActionKind.ToString());
            Assert.IsTrue(entry.RequiresThoughtLedger, entry.ActionKind.ToString());
            CollectionAssert.Contains(entry.RequiredEvidenceKinds, "ConscienceDecision");
            CollectionAssert.Contains(entry.RequiredEvidenceKinds, "ThoughtLedger");
        }

        foreach (var entry in AuthorityActionInventory.All.Where(entry => entry.Classification == GovernedActionClassification.NonAuthority))
        {
            Assert.IsTrue(entry.AllowedInCurrentBlock, entry.ActionKind.ToString());
            Assert.IsFalse(entry.RequiresConscience, entry.ActionKind.ToString());
            Assert.IsFalse(entry.RequiresThoughtLedger, entry.ActionKind.ToString());
        }
    }

    [TestMethod]
    public void BlockAB_ConscienceAndThoughtLedgerContracts_FailClosedForAuthorityBearingActions()
    {
        var missingDecision = ConscienceDecisionEvaluator.Evaluate(GovernedActionKind.SourceApply, decision: null);
        Assert.IsFalse(missingDecision.IsExecutable);
        Assert.AreEqual("MissingConscienceDecision", missingDecision.Status);

        var nonAuthority = ConscienceDecisionEvaluator.Evaluate(GovernedActionKind.PatchArtifactExported, decision: null);
        Assert.IsTrue(nonAuthority.IsExecutable);

        var sourceApplyThoughtLedger = ThoughtLedgerRequirementCatalog.Evaluate(GovernedActionKind.SourceApply, thoughtLedgerRef: null);
        Assert.IsFalse(sourceApplyThoughtLedger.IsSatisfied);
        Assert.AreEqual("MissingThoughtLedgerFailClosed", sourceApplyThoughtLedger.Status);

        var patchThoughtLedger = ThoughtLedgerRequirementCatalog.Evaluate(GovernedActionKind.PatchArtifactExported, thoughtLedgerRef: null);
        Assert.IsTrue(patchThoughtLedger.IsSatisfied);

        var draft = new ConscienceDecision
        {
            DecisionId = "decision-1",
            ActionId = "action-1",
            ActionKind = GovernedActionKind.SourceApply,
            SubjectKind = "PatchArtifact",
            SubjectId = "patch-1",
            RequestedBy = "human-reviewer",
            EvidenceRefs = [new ConscienceDecisionEvidenceRef { RefId = "evidence-1", EvidenceKind = "PatchArtifact", SafeSummary = "Patch artifact hash evidence." }],
            PolicyRefs = ["policy-1"],
            RiskLevel = ConscienceDecisionRiskLevel.High,
            Decision = ConscienceDecisionOutcome.Block,
            BlockReasons = ["Source apply is not allowed in Block AB."],
            RequiredHumanReview = true,
            ThoughtLedgerRef = "thought-ledger-1",
            DecisionHash = "pending",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var decision = draft with { DecisionHash = ConscienceDecisionHash.Compute(draft) };
        var json = JsonSerializer.Serialize(decision, JsonOptions);
        var roundTrip = JsonSerializer.Deserialize<ConscienceDecision>(json, JsonOptions);

        Assert.IsNotNull(roundTrip);
        Assert.AreEqual(ConscienceDecisionOutcome.Block, roundTrip.Decision);
        Assert.IsTrue(roundTrip.BlockReasons.Length > 0);
        Assert.AreEqual(decision.DecisionHash, roundTrip.DecisionHash);
    }

    [TestMethod]
    public void BlockAB_BypassLanes_RegisterFutureBypassesWithoutCreatingImplementationPaths()
    {
        var lanes = AuthorityBypassTestLaneCatalog.All;

        foreach (var required in new[]
                 {
                     "memory-promotion-without-conscience",
                     "memory-promotion-without-thoughtledger",
                     "tool-execution-without-gate",
                     "source-apply-without-accepted-approval",
                     "source-apply-without-policy-satisfaction",
                     "source-apply-without-patch-artifact",
                     "source-apply-without-dry-run",
                     "source-apply-without-rollback-plan",
                     "workflow-continuation-from-receipt-text",
                     "release-readiness-decision-from-report-text",
                     "ui-approval-creation",
                     "agent-self-approval",
                     "direct-git-push-from-irondev-action-path"
                 })
        {
            Assert.IsTrue(lanes.Any(lane => string.Equals(lane.LaneId, required, StringComparison.Ordinal)), required);
        }

        foreach (var lane in lanes)
        {
            Assert.IsFalse(lane.ExecutableInCurrentBlock, lane.LaneId);
            Assert.AreNotEqual(GovernedActionClassification.NonAuthority, lane.ExpectedClassification, lane.LaneId);
            Assert.IsTrue(lane.RequiredFutureEvidence.Length > 0, lane.LaneId);
        }
    }

    [TestMethod]
    public async Task BlockAB_GovernanceCli_ListsInventoryAndClassifiesHighRiskActionsAsNotExecutable()
    {
        var inventory = await RunCliAsync("governance", "inventory", "--json");
        Assert.AreEqual(0, inventory.ExitCode, inventory.StandardOutput + inventory.StandardError);
        StringAssert.Contains(inventory.StandardOutput, "SourceApply");
        StringAssert.Contains(inventory.StandardOutput, "PatchArtifactExported");

        var classify = await RunCliAsync("governance", "classify", "--action", "SourceApply", "--json");
        Assert.AreEqual(0, classify.ExitCode, classify.StandardOutput + classify.StandardError);
        StringAssert.Contains(classify.StandardOutput, "AuthorityBearing");
        StringAssert.Contains(classify.StandardOutput, "\"requiresConscience\": true");
        StringAssert.Contains(classify.StandardOutput, "\"requiresThoughtLedger\": true");
        StringAssert.Contains(classify.StandardOutput, "\"executableInCurrentBlock\": false");

        var forbidden = await RunCliAsync("governance", "classify", "--action", "DirectGitPush", "--json");
        Assert.AreEqual(0, forbidden.ExitCode, forbidden.StandardOutput + forbidden.StandardError);
        StringAssert.Contains(forbidden.StandardOutput, "ForbiddenOrUnsupported");
    }

    [TestMethod]
    public async Task BlockAB_PatchLoop_WritesParseableRunScopedGovernanceEventsWithoutAuthority()
    {
        var root = CreateTempRoot();
        try
        {
            var repo = await CreateSourceRepoAsync(root);
            var runsRoot = Path.Combine(root, "runs");
            var workspaceRoot = Path.Combine(root, "workspaces");

            var start = await RunCliAsync(
                "patch", "start",
                "--repo", repo,
                "--task", Path.Combine(root, "task.md"),
                "--test", "dotnet --version",
                "--runs-root", runsRoot,
                "--workspace-root", workspaceRoot,
                "--run-id", "ab-events",
                "--json");

            Assert.AreEqual(0, start.ExitCode, start.StandardOutput + start.StandardError);
            var runPath = Path.Combine(runsRoot, "ab-events");
            var workspace = ReadRunString(runPath, "workspacePath");

            await File.AppendAllTextAsync(Path.Combine(workspace, "README.md"), "patch-loop governed event change\n");

            var finish = await RunCliAsync("patch", "finish", "--run", "ab-events", "--runs-root", runsRoot, "--skip-test", "--json");
            Assert.AreEqual(0, finish.ExitCode, finish.StandardOutput + finish.StandardError);

            var status = await RunCliAsync("patch", "status", "--run", "ab-events", "--runs-root", runsRoot, "--json");
            Assert.AreEqual(0, status.ExitCode, status.StandardOutput + status.StandardError);

            var governance = await RunCliAsync("patch", "governance", "--run", "ab-events", "--runs-root", runsRoot, "--json");
            Assert.AreEqual(0, governance.ExitCode, governance.StandardOutput + governance.StandardError);
            StringAssert.Contains(governance.StandardOutput, "PatchArtifactExported");

            var cleanup = await RunCliAsync("patch", "cleanup", "--run", "ab-events", "--runs-root", runsRoot, "--delete-workspace", "--json");
            Assert.AreEqual(0, cleanup.ExitCode, cleanup.StandardOutput + cleanup.StandardError);

            var events = ReadGovernanceEvents(runPath);
            var kinds = events.Select(evt => evt.ActionKind).ToArray();
            CollectionAssert.Contains(kinds, nameof(GovernedActionKind.PatchProposalRunStarted));
            CollectionAssert.Contains(kinds, nameof(GovernedActionKind.DisposableWorkspaceCreated));
            CollectionAssert.Contains(kinds, nameof(GovernedActionKind.ChangedFilesDetected));
            CollectionAssert.Contains(kinds, nameof(GovernedActionKind.WorkspaceTestsExecuted));
            CollectionAssert.Contains(kinds, nameof(GovernedActionKind.PatchArtifactExported));
            CollectionAssert.Contains(kinds, nameof(GovernedActionKind.ReviewPackageCreated));
            CollectionAssert.Contains(kinds, nameof(GovernedActionKind.PatchRunStatusRead));
            CollectionAssert.Contains(kinds, nameof(GovernedActionKind.PatchWorkspaceCleaned));

            foreach (var evt in events)
            {
                Assert.AreEqual(nameof(GovernedActionClassification.NonAuthority), evt.Classification, evt.ActionKind);
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
                Assert.IsFalse(evt.Boundary.ModelCalled, evt.ActionKind);
            }

            var sourceReadme = await File.ReadAllTextAsync(Path.Combine(repo, "README.md"));
            Assert.IsFalse(sourceReadme.Contains("patch-loop governed event change", StringComparison.Ordinal));
            Assert.IsTrue(File.Exists(Path.Combine(runPath, "governance-events.jsonl")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static RunScopedGovernanceEvent[] ReadGovernanceEvents(string runPath)
    {
        return File.ReadAllLines(Path.Combine(runPath, "governance-events.jsonl"))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<RunScopedGovernanceEvent>(line, JsonOptions))
            .Where(evt => evt is not null)
            .Cast<RunScopedGovernanceEvent>()
            .ToArray();
    }

    private static async Task<string> CreateSourceRepoAsync(string root)
    {
        var repo = Path.Combine(root, "source");
        Directory.CreateDirectory(repo);
        await File.WriteAllTextAsync(Path.Combine(repo, "README.md"), "initial source\n");
        await File.WriteAllTextAsync(Path.Combine(root, "task.md"), "# Patch task\n\nMake the safest small patch.\n");

        await GitAsync(repo, "init");
        await GitAsync(repo, "config", "user.email", "block-ab@example.test");
        await GitAsync(repo, "config", "user.name", "Block AB Test");
        await GitAsync(repo, "add", ".");
        await GitAsync(repo, "commit", "-m", "initial");
        return repo;
    }

    private static async Task<CommandResult> RunCliAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = FindRepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine(FindRepoRoot(), "tools", "IronDev.Cli", "IronDev.Cli.csproj"));
        startInfo.ArgumentList.Add("--");

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        return await RunProcessAsync(startInfo);
    }

    private static async Task GitAsync(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        var result = await RunProcessAsync(startInfo);
        Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
    }

    private static async Task<CommandResult> RunProcessAsync(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {startInfo.FileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new CommandResult(process.ExitCode, await stdout, await stderr);
    }

    private static string ReadRunString(string runPath, string propertyName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(runPath, "run.json")));
        return document.RootElement.GetProperty(propertyName).GetString() ?? string.Empty;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "irondev-block-ab-" + Guid.NewGuid().ToString("N"));
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
            // Test cleanup best effort only.
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")) &&
                File.Exists(Path.Combine(directory.FullName, "tools", "IronDev.Cli", "IronDev.Cli.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IronDev solution root.");
    }

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
}
