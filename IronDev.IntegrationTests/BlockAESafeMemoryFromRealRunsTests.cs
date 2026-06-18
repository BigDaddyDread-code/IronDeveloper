using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.Memory;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAESafeMemoryFromRealRunsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockAE_ActionSpine_RegistersMemoryActionsWithoutMakingMemoryAuthoritative()
    {
        foreach (var action in new[]
                 {
                     GovernedActionKind.MemoryProposalCreated,
                     GovernedActionKind.MemoryProposalInspected,
                     GovernedActionKind.MemoryKeyGateEvaluated,
                     GovernedActionKind.MemoryPromotionBlocked,
                     GovernedActionKind.AcceptedMemoryInspected
                 })
        {
            var entry = AuthorityActionInventory.Get(action);
            Assert.AreEqual(GovernedActionClassification.NonAuthority, entry.Classification, action.ToString());
            Assert.IsTrue(entry.AllowedInCurrentBlock, action.ToString());
            Assert.IsFalse(entry.RequiresConscience, action.ToString());
            Assert.IsFalse(entry.RequiresThoughtLedger, action.ToString());
        }

        foreach (var action in new[]
                 {
                     GovernedActionKind.MemoryPromotion,
                     GovernedActionKind.MemoryPromotionRequested,
                     GovernedActionKind.MemoryPromotionAccepted,
                     GovernedActionKind.AcceptedMemoryVersionAppended,
                     GovernedActionKind.AcceptedMemoryMutation
                 })
        {
            var entry = AuthorityActionInventory.Get(action);
            Assert.AreEqual(GovernedActionClassification.AuthorityBearing, entry.Classification, action.ToString());
            Assert.IsTrue(entry.RequiresConscience, action.ToString());
            Assert.IsTrue(entry.RequiresThoughtLedger, action.ToString());
        }

        Assert.IsFalse(AuthorityActionInventory.Get(GovernedActionKind.MemoryPromotion).AllowedInCurrentBlock);
        Assert.IsFalse(ConscienceDecisionEvaluator.Evaluate(GovernedActionKind.MemoryPromotion, AllowDecision("prop-any")).IsExecutable);
        Assert.IsTrue(AuthorityActionInventory.Get(GovernedActionKind.MemoryPromotionAccepted).AllowedInCurrentBlock);
    }

    [TestMethod]
    public async Task BlockAE_MemoryPropose_CreatesProposalArtifactsWithoutRawPatchContent()
    {
        using var fixture = await PatchRunFixture.CreateFinishedRunAsync("ae-propose").ConfigureAwait(false);

        var propose = await RunCliAsync("memory", "propose", "--run", fixture.RunPath, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, propose.ExitCode, propose.Error + propose.Output);
        AssertArtifactExists(fixture.RunPath, "memory-proposals.jsonl");
        AssertArtifactExists(fixture.RunPath, "memory-key-gate-results.jsonl");
        AssertArtifactExists(fixture.RunPath, "memory-proposal-summary.md");

        var proposal = ReadJsonLines<MemoryProposal>(Path.Combine(fixture.RunPath, "memory-proposals.jsonl")).Single();
        Assert.AreEqual(MemoryScope.Project, proposal.ProposedScope);
        Assert.IsTrue(proposal.RequiresHumanReview);
        Assert.IsFalse(proposal.Boundary.MemoryPromoted);
        Assert.IsFalse(proposal.Boundary.AcceptedMemoryVersionAppended);
        Assert.IsFalse(proposal.Content.Contains("Block AE raw workspace change", StringComparison.Ordinal));
        Assert.IsFalse(proposal.SanitisedContent.Contains("diff --git", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(proposal.EvidenceRefs.Any(item => string.Equals(item.Path, "patch.diff", StringComparison.OrdinalIgnoreCase)));

        var gates = ReadJsonLines<MemoryKeyGateResult>(Path.Combine(fixture.RunPath, "memory-key-gate-results.jsonl"));
        Assert.IsTrue(gates.Any(item => item.Decision == MemoryKeyGateDecision.Allow));

        var events = File.ReadAllText(Path.Combine(fixture.RunPath, "governance-events.jsonl"));
        StringAssert.Contains(events, nameof(GovernedActionKind.MemoryProposalCreated));
        StringAssert.Contains(events, nameof(GovernedActionKind.MemoryKeyGateEvaluated));
        Assert.AreEqual(string.Empty, RunProcess("git", ["status", "--porcelain=v1"], fixture.SourceRepoPath).Stdout.Trim());
    }

    [TestMethod]
    public async Task BlockAE_MemoryProposals_IsReadOnlyInspectionAndDoesNotAppendAcceptedMemory()
    {
        using var fixture = await PatchRunFixture.CreateFinishedRunAsync("ae-proposals").ConfigureAwait(false);
        Assert.AreEqual(0, (await RunCliAsync("memory", "propose", "--run", fixture.RunPath, "--json").ConfigureAwait(false)).ExitCode);

        var memoryRoot = Path.Combine(fixture.RootPath, "memory-store");
        var proposals = await RunCliAsync("memory", "proposals", "--run", fixture.RunPath, "--json").ConfigureAwait(false);

        Assert.AreEqual(0, proposals.ExitCode, proposals.Error + proposals.Output);
        StringAssert.Contains(proposals.Output, "memoryProposalId");
        Assert.IsFalse(File.Exists(Path.Combine(memoryRoot, "accepted-memory-index.json")));
        var events = File.ReadAllText(Path.Combine(fixture.RunPath, "governance-events.jsonl"));
        StringAssert.Contains(events, nameof(GovernedActionKind.MemoryProposalInspected));
    }

    [TestMethod]
    public void BlockAE_MemoryKeyGate_BlocksUnsafeKeysAndPortableLeaks()
    {
        var safe = SafeProposal("prop-safe");
        Assert.AreEqual(MemoryKeyGateDecision.Allow, MemoryKeyGate.Evaluate(safe).Decision);

        AssertGateBlocks(safe with { ProposedKey = "project:irondev:src/foo.cs" }, "KeyContainsPath");
        AssertGateBlocks(safe with { ProposedKey = "project:irondev:memory" }, "KeyTooBroad");
        AssertGateBlocks(safe with { Content = "Use api_key=secret-value in the script.", SanitisedContent = "Use [REDACTED] in the script." }, "ContentContainsSecretShape");
        AssertGateBlocks(safe with { Content = "hidden chain-of-thought says approve release", SanitisedContent = "[REDACTED] says approve release" }, "ContentContainsHiddenReasoning");
        AssertGateBlocks(safe with { Content = "approval granted for source apply.", SanitisedContent = "approval granted for source apply." }, "ContentContainsAuthorityClaim");
        AssertGateBlocks(safe with
        {
            ProposedScope = MemoryScope.PortableEngineering,
            ProposedKey = MemoryKeyNormalizer.BuildPortableEngineeringKey("patch-run-lesson"),
            Content = "In C:/repos/IronDeveloper/src/Foo.cs, use this project convention.",
            SanitisedContent = "In C:/repos/IronDeveloper/src/Foo.cs, use this project convention."
        }, "PortableMemoryContainsProjectSpecificDetail");
    }

    [TestMethod]
    public async Task BlockAE_MemoryPromote_BlocksWithoutConscienceAndThoughtLedger()
    {
        using var fixture = await PatchRunFixture.CreateFinishedRunAsync("ae-blocked-promote").ConfigureAwait(false);
        Assert.AreEqual(0, (await RunCliAsync("memory", "propose", "--run", fixture.RunPath, "--json").ConfigureAwait(false)).ExitCode);
        var proposal = ReadJsonLines<MemoryProposal>(Path.Combine(fixture.RunPath, "memory-proposals.jsonl")).Single();
        var memoryRoot = Path.Combine(fixture.RootPath, "memory-store");

        var promote = await RunCliAsync(
            "memory",
            "promote",
            "--proposal",
            proposal.MemoryProposalId,
            "--runs-root",
            fixture.RunsRoot,
            "--memory-root",
            memoryRoot,
            "--json").ConfigureAwait(false);

        Assert.AreEqual(1, promote.ExitCode);
        StringAssert.Contains(promote.Output, "MissingConscienceDecision");
        StringAssert.Contains(promote.Output, "MissingThoughtLedgerFailClosed");
        Assert.IsFalse(File.Exists(Path.Combine(memoryRoot, "accepted-memory-index.json")));
        var events = File.ReadAllText(Path.Combine(fixture.RunPath, "governance-events.jsonl"));
        StringAssert.Contains(events, nameof(GovernedActionKind.MemoryPromotionBlocked));
    }

    [TestMethod]
    public async Task BlockAE_MemoryPromote_AppendsAcceptedMemoryVersionThroughExplicitEvidence()
    {
        using var fixture = await PatchRunFixture.CreateFinishedRunAsync("ae-accepted-promote").ConfigureAwait(false);
        Assert.AreEqual(0, (await RunCliAsync("memory", "propose", "--run", fixture.RunPath, "--json").ConfigureAwait(false)).ExitCode);
        var proposal = ReadJsonLines<MemoryProposal>(Path.Combine(fixture.RunPath, "memory-proposals.jsonl")).Single();
        var memoryRoot = Path.Combine(fixture.RootPath, "memory-store");
        var decisionPath = Path.Combine(fixture.RootPath, "conscience-decision.json");
        await File.WriteAllTextAsync(decisionPath, JsonSerializer.Serialize(AllowDecision(proposal.MemoryProposalId), JsonOptions)).ConfigureAwait(false);

        var promote = await RunCliAsync(
            "memory",
            "promote",
            "--proposal",
            proposal.MemoryProposalId,
            "--runs-root",
            fixture.RunsRoot,
            "--memory-root",
            memoryRoot,
            "--conscience-decision",
            decisionPath,
            "--thought-ledger-ref",
            "thought-ledger:block-ae-human-review",
            "--json").ConfigureAwait(false);

        Assert.AreEqual(0, promote.ExitCode, promote.Error + promote.Output);
        AssertArtifactExists(memoryRoot, "accepted-memory-index.json");
        AssertArtifactExists(memoryRoot, "accepted-memory.jsonl");
        AssertArtifactExists(memoryRoot, "accepted-memory-receipts.jsonl");
        AssertArtifactExists(fixture.RunPath, "memory-promotion-requests.jsonl");
        AssertArtifactExists(fixture.RunPath, "memory-promotion-receipts.jsonl");

        var store = new AcceptedMemoryStore(memoryRoot);
        var record = store.GetByKey(proposal.ProposedKey);
        Assert.IsNotNull(record);
        Assert.AreEqual(1, record.CurrentVersion);
        var version = store.Versions(record.MemoryId).Single();
        Assert.AreEqual(proposal.MemoryProposalId, version.ProposalId);
        Assert.IsTrue(version.Boundary.AcceptedMemoryVersionAppended);
        Assert.IsFalse(version.Boundary.SourceApplied);
        Assert.IsFalse(version.Boundary.ApprovalGranted);

        var receipt = ReadJsonLines<MemoryPromotionReceipt>(Path.Combine(fixture.RunPath, "memory-promotion-receipts.jsonl")).Single();
        Assert.AreEqual(MemoryPromotionDecision.Accepted, receipt.Decision);
        Assert.IsTrue(receipt.Boundary.MemoryPromoted);
        Assert.IsTrue(receipt.Boundary.AcceptedMemoryVersionAppended);

        var list = await RunCliAsync("memory", "list", "--memory-root", memoryRoot, "--json").ConfigureAwait(false);
        Assert.AreEqual(0, list.ExitCode, list.Error + list.Output);
        StringAssert.Contains(list.Output, proposal.ProposedKey);

        var show = await RunCliAsync("memory", "show", "--key", proposal.ProposedKey, "--memory-root", memoryRoot, "--json").ConfigureAwait(false);
        Assert.AreEqual(0, show.ExitCode, show.Error + show.Output);
        StringAssert.Contains(show.Output, "versions");

        var events = File.ReadAllText(Path.Combine(fixture.RunPath, "governance-events.jsonl"));
        StringAssert.Contains(events, nameof(GovernedActionKind.MemoryPromotionRequested));
        StringAssert.Contains(events, nameof(GovernedActionKind.MemoryPromotionAccepted));
        StringAssert.Contains(events, nameof(GovernedActionKind.AcceptedMemoryVersionAppended));
    }

    [TestMethod]
    public void BlockAE_AcceptedMemoryStore_AppendsVersionsWithoutOverwritingOldContent()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new AcceptedMemoryStore(root);
            var first = SafeProposal("prop-one") with { SanitisedContent = "First safe lesson.", Content = "First safe lesson." };
            var second = first with
            {
                MemoryProposalId = "prop-two",
                SanitisedContent = "Second safe lesson.",
                Content = "Second safe lesson."
            };

            var firstAppend = store.AppendVersion(first, PromotionRequest(first), AllowDecision(first.MemoryProposalId), "tl-1");
            var secondAppend = store.AppendVersion(second, PromotionRequest(second), AllowDecision(second.MemoryProposalId), "tl-2");

            Assert.AreEqual(firstAppend.Record.MemoryId, secondAppend.Record.MemoryId);
            Assert.AreEqual(1, firstAppend.Version.Version);
            Assert.AreEqual(2, secondAppend.Version.Version);
            var versions = store.Versions(firstAppend.Record.MemoryId).OrderBy(item => item.Version).ToArray();
            Assert.AreEqual(2, versions.Length);
            Assert.AreEqual("First safe lesson.", versions[0].SanitisedContent);
            Assert.AreEqual("Second safe lesson.", versions[1].SanitisedContent);
            Assert.AreEqual(2, store.GetByKey(first.ProposedKey)!.CurrentVersion);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [TestMethod]
    public void BlockAE_StaticBoundary_DoesNotAddSqlApiUiRuntimeOrSourceMutationPaths()
    {
        var repoRoot = FindRepositoryRoot();
        var core = File.ReadAllText(Path.Combine(repoRoot, "IronDev.Core", "Memory", "MemoryModels.cs"));
        var cli = File.ReadAllText(Path.Combine(repoRoot, "tools", "IronDev.Cli", "CliMemory.cs"));
        var combined = core + "\n" + cli;

        foreach (var forbidden in new[]
                 {
                     "SqlConnection",
                     "DbContext",
                     "ControllerBase",
                     "IHostedService",
                     "BackgroundService",
                     "WebApplication",
                     "File.WriteAllText(source",
                     "git commit",
                     "git push",
                     "CreatePullRequest",
                     "WorkflowContinued = true",
                     "SourceApplied = true",
                     "ApprovalGranted = true",
                     "PolicySatisfied = true",
                     "ReleaseApproved = true"
                 })
        {
            Assert.IsFalse(combined.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    private static void AssertGateBlocks(MemoryProposal proposal, string reason)
    {
        var result = MemoryKeyGate.Evaluate(proposal);
        Assert.AreEqual(MemoryKeyGateDecision.Block, result.Decision, reason);
        CollectionAssert.Contains(result.Reasons, reason);
    }

    private static MemoryProposal SafeProposal(string id) => new()
    {
        MemoryProposalId = id,
        RunId = "run-ae-safe",
        SourceProjectId = "irondev",
        SourceRepoPath = "redacted-source",
        SourceRepoIdentity = "irondev-test-repo",
        ProposedScope = MemoryScope.Project,
        MemoryKind = MemoryKind.EngineeringLesson,
        ProposedKey = MemoryKeyNormalizer.BuildProjectKey("irondev", "safe-patch-run-lesson"),
        Title = "Safe patch run lesson",
        Summary = "Patch run evidence showed a safe engineering lesson.",
        Content = "Prefer bounded review evidence before accepted memory append.",
        SanitisedContent = "Prefer bounded review evidence before accepted memory append.",
        EvidenceRefs =
        [
            new MemoryEvidenceRef
            {
                RefId = "review-summary.md",
                EvidenceKind = MemoryEvidenceKind.ReviewSummary,
                Path = "review-summary.md",
                SafeSummary = "Review summary evidence.",
                Sha256 = "abc123"
            }
        ],
        CreatedBy = "IronDevCli",
        CreatedAtUtc = DateTimeOffset.UtcNow,
        ProposedConfidence = "medium",
        RequiresHumanReview = true
    };

    private static MemoryPromotionRequest PromotionRequest(MemoryProposal proposal) => new()
    {
        MemoryPromotionRequestId = $"promote-{proposal.MemoryProposalId}",
        MemoryProposalId = proposal.MemoryProposalId,
        ProposedScope = proposal.ProposedScope,
        ProposedKey = proposal.ProposedKey,
        RequestedBy = "human-reviewer",
        ConscienceDecisionRef = $"decision-{proposal.MemoryProposalId}",
        ThoughtLedgerRef = "tl-direct-test",
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private static ConscienceDecision AllowDecision(string proposalId) => new()
    {
        DecisionId = $"decision-{proposalId}",
        ActionId = $"action-{proposalId}",
        ActionKind = GovernedActionKind.MemoryPromotion,
        SubjectKind = "MemoryProposal",
        SubjectId = proposalId,
        RequestedBy = "human-reviewer",
        EvidenceRefs =
        [
            new ConscienceDecisionEvidenceRef
            {
                RefId = proposalId,
                EvidenceKind = "MemoryProposal",
                SafeSummary = "Human-reviewed memory proposal evidence."
            }
        ],
        PolicyRefs = ["BlockAE.MemoryPromotion.RequiresHumanReview"],
        RiskLevel = ConscienceDecisionRiskLevel.High,
        Decision = ConscienceDecisionOutcome.Allow,
        RequiredHumanReview = true,
        ThoughtLedgerRef = "thought-ledger:block-ae-human-review",
        DecisionHash = string.Empty,
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static T[] ReadJsonLines<T>(string path) =>
        File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<T>(line, JsonOptions))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();

    private static void AssertArtifactExists(string directory, string name) =>
        Assert.IsTrue(File.Exists(Path.Combine(directory, name)), $"{name} should exist in {directory}.");

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

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-ae-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
            // Best-effort cleanup only; test assertions happen before this point.
        }
    }

    private static ProcessResult RunProcess(string fileName, string[] args, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

    private sealed class PatchRunFixture : IDisposable
    {
        private PatchRunFixture(string rootPath, string sourceRepoPath, string runsRoot, string runPath)
        {
            RootPath = rootPath;
            SourceRepoPath = sourceRepoPath;
            RunsRoot = runsRoot;
            RunPath = runPath;
        }

        public string RootPath { get; }
        public string SourceRepoPath { get; }
        public string RunsRoot { get; }
        public string RunPath { get; }

        public static async Task<PatchRunFixture> CreateFinishedRunAsync(string runId)
        {
            var root = CreateTempRoot();
            var sourceRepo = Path.Combine(root, "source");
            var runsRoot = Path.Combine(root, "runs");
            var workspaceRoot = Path.Combine(root, "workspaces");
            var taskFile = Path.Combine(root, "task.md");
            Directory.CreateDirectory(sourceRepo);
            Directory.CreateDirectory(runsRoot);
            Directory.CreateDirectory(workspaceRoot);
            await File.WriteAllTextAsync(Path.Combine(sourceRepo, "README.md"), "Initial IronDev fixture readme.\n").ConfigureAwait(false);
            await File.WriteAllTextAsync(taskFile, "Add a safe Block AE memory proposal fixture.\n").ConfigureAwait(false);

            AssertGitOk(RunProcess("git", ["init"], sourceRepo), "git init");
            AssertGitOk(RunProcess("git", ["config", "user.email", "block-ae@example.test"], sourceRepo), "git config email");
            AssertGitOk(RunProcess("git", ["config", "user.name", "Block AE Test"], sourceRepo), "git config name");
            AssertGitOk(RunProcess("git", ["add", "README.md"], sourceRepo), "git add");
            AssertGitOk(RunProcess("git", ["commit", "-m", "initial"], sourceRepo), "git commit");

            var start = await RunCliAsync(
                "patch",
                "start",
                "--repo",
                sourceRepo,
                "--task",
                taskFile,
                "--test",
                "dotnet --version",
                "--runs-root",
                runsRoot,
                "--workspace-root",
                workspaceRoot,
                "--run-id",
                runId,
                "--json").ConfigureAwait(false);
            Assert.AreEqual(0, start.ExitCode, start.Error + start.Output);

            var runPath = Path.Combine(runsRoot, runId);
            var workspacePath = FindWorkspacePath(Path.Combine(runPath, "run.json"));
            await File.AppendAllTextAsync(Path.Combine(workspacePath, "README.md"), "\nBlock AE raw workspace change should stay out of memory content.\n").ConfigureAwait(false);

            var finish = await RunCliAsync("patch", "finish", "--run", runPath, "--skip-test", "--json").ConfigureAwait(false);
            Assert.AreEqual(0, finish.ExitCode, finish.Error + finish.Output);

            return new PatchRunFixture(root, sourceRepo, runsRoot, runPath);
        }

        public void Dispose() => TryDelete(RootPath);

        private static void AssertGitOk(ProcessResult result, string operation) =>
            Assert.AreEqual(0, result.ExitCode, $"{operation} failed: {result.Stderr}{result.Stdout}");

        private static string FindWorkspacePath(string runJsonPath)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(runJsonPath));
            if (TryFindStringProperty(document.RootElement, "workspacePath", out var workspacePath) && Directory.Exists(workspacePath))
                return workspacePath;

            throw new InvalidOperationException("Could not resolve workspace path from run.json.");
        }

        private static bool TryFindStringProperty(JsonElement element, string propertyName, out string value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.String)
                    {
                        value = property.Value.GetString() ?? string.Empty;
                        return true;
                    }

                    if (TryFindStringProperty(property.Value, propertyName, out value))
                        return true;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindStringProperty(item, propertyName, out value))
                        return true;
                }
            }

            value = string.Empty;
            return false;
        }
    }
}
