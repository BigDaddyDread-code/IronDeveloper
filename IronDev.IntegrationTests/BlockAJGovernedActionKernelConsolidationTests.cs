using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Cli;
using IronDev.Core.Governance;
using IronDev.Core.Memory;
using IronDev.Core.SourceApply;
using IronDev.Core.Tools;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockAJGovernedActionKernelConsolidationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [TestMethod]
    public void BlockAJ_GovernedActionEnvelope_CoversAuthorityInventoryAndCannotApproveItself()
    {
        foreach (var actionKind in new[]
                 {
                     GovernedActionKind.MemoryPromotion,
                     GovernedActionKind.AcceptedMemoryMutation,
                     GovernedActionKind.ToolExecution,
                     GovernedActionKind.WorkspaceToolExecution,
                     GovernedActionKind.SourceApply,
                     GovernedActionKind.SourceRollback,
                     GovernedActionKind.WorkflowContinuation,
                     GovernedActionKind.ReleaseReadinessDecision,
                     GovernedActionKind.ReleaseApproval,
                     GovernedActionKind.DeploymentApproval,
                     GovernedActionKind.MergeApproval,
                     GovernedActionKind.TicketCreation,
                     GovernedActionKind.SchedulerRunCreation,
                     GovernedActionKind.AgentHandoffAuthorityClaim
                 })
        {
            var entry = AuthorityActionInventory.Get(actionKind);
            Assert.AreEqual(actionKind, entry.ActionKind);
            Assert.IsTrue(entry.RequiredEvidenceKinds.Length > 0, actionKind.ToString());
            if (entry.Classification == GovernedActionClassification.AuthorityBearing)
            {
                Assert.IsTrue(entry.RequiresConscience, actionKind.ToString());
                Assert.IsTrue(entry.RequiresThoughtLedger, actionKind.ToString());
            }
        }

        var envelope = GovernedActionEnvelope.FromInventory(
            GovernedActionKind.SourceApply,
            "PatchRun",
            "run-aj",
            "human-reviewer",
            "source-apply-request.json",
            [Evidence("patch-artifact", "Patch artifact hash evidence.")]);

        Assert.AreEqual(GovernedActionStatus.Requested, envelope.Status);
        Assert.IsFalse(envelope.GrantsApproval);
        Assert.IsFalse(envelope.AllowsExecution);
        Assert.IsFalse(envelope.SatisfiesPolicy);

        var roundTrip = JsonSerializer.Deserialize<GovernedActionEnvelope>(JsonSerializer.Serialize(envelope, JsonOptions), JsonOptions);
        Assert.IsNotNull(roundTrip);
        Assert.AreEqual(envelope.ActionId, roundTrip.ActionId);

        var forbiddenStatusNames = Enum.GetNames<GovernedActionStatus>();
        foreach (var forbidden in new[] { "ApprovedByMemory", "ApprovedByGate", "ApprovedByUI", "ApprovedByAgent", "AutoContinued", "Released", "Deployed", "Merged" })
            Assert.IsFalse(forbiddenStatusNames.Contains(forbidden), forbidden);
    }

    [TestMethod]
    public void BlockAJ_GateEvidence_AdaptsCurrentGatesWithoutAuthorityWords()
    {
        var action = GovernedActionEnvelope.FromInventory(
            GovernedActionKind.SourceApply,
            "PatchRun",
            "run-aj-gate",
            "human-reviewer",
            "source-apply-request.json",
            [Evidence("source-apply-request", "Source apply request evidence.")]);

        var memoryGate = new MemoryKeyGateResult
        {
            MemoryKeyGateResultId = "mem-key-gate-1",
            MemoryProposalId = "proposal-1",
            ProposedKey = "project:lesson",
            ProposedScope = MemoryScope.Project,
            Decision = MemoryKeyGateDecision.Allow,
            Reasons = [],
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = MemoryBoundary.None
        };
        var memoryEvidence = GateEvidenceWriter.FromMemoryKeyGate(action.ActionId, memoryGate);
        Assert.AreEqual(GateEvidenceDecision.Satisfied, memoryEvidence.Decision);
        Assert.IsFalse(memoryEvidence.GrantsAuthority);
        Assert.IsFalse(memoryEvidence.AllowsExecution);
        Assert.IsFalse(memoryEvidence.ApprovesAction);

        var toolGate = new WorkspaceToolGateDecision
        {
            ToolGateDecisionId = "tool-gate-1",
            ToolRequestId = "tool-request-1",
            RunId = "run-aj-gate",
            Decision = WorkspaceToolGateDecisionOutcome.Block,
            Reasons = ["WorkingDirectoryIsSourceRepo"],
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            WorkingDirectory = "workspace",
            WorkspacePath = "workspace",
            SourceRepoPath = "source",
            Command = "git push",
            RiskClassification = ToolRiskClassification.SourceControlDangerous,
            Boundary = ToolCommandBoundary.None
        };
        Assert.AreEqual(GateEvidenceDecision.NotSatisfied, GateEvidenceWriter.FromWorkspaceToolGate(action.ActionId, toolGate).Decision);

        Assert.IsFalse(Enum.GetNames<GateEvidenceDecision>().Any(name => name.Contains("Allow", StringComparison.OrdinalIgnoreCase) || name.Contains("Approved", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void BlockAJ_ConscienceDecisionService_RequiresEvidenceGateAndThoughtLedger()
    {
        var action = GovernedActionEnvelope.FromInventory(
            GovernedActionKind.SourceApply,
            "PatchRun",
            "run-aj-decision",
            "human-reviewer",
            "source-apply-request.json",
            [Evidence("source-apply-request", "Source apply request evidence.")]);
        var gate = new GateEvidence
        {
            GateEvidenceId = "gate-1",
            ActionId = action.ActionId,
            GateKind = GateEvidenceKind.SourceApplyExecutionGate,
            SubjectKind = "PatchRun",
            SubjectId = "run-aj-decision",
            Decision = GateEvidenceDecision.Satisfied,
            Reasons = [],
            EvidenceRefs = ["source-apply-execution-gate.json"],
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = GovernedActionBoundary.None
        };

        var writer = new InMemoryThoughtLedgerWriter();
        var service = new ConscienceDecisionService(writer);
        var allow = service.Decide(new ConscienceDecisionRequest
        {
            Action = action,
            GateEvidenceRefs = [gate],
            PolicyRefs = ["human-review"],
            RequestedBy = "human-reviewer",
            ReasoningSummary = "Human-reviewed source apply evidence is complete for this controlled operation.",
            RequestedDecision = ConscienceDecisionValue.Allow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10)
        });
        Assert.AreEqual(ConscienceDecisionValue.Allow, allow.Decision);
        Assert.IsNotNull(allow.ThoughtLedgerEntryId);
        Assert.IsTrue(allow.AllowsExecution);
        Assert.IsTrue(ConscienceDecisionValidator.Validate(allow).IsValidForFutureExecution);
        Assert.AreEqual(ConscienceDecisionRecordHash.Compute(allow), allow.DecisionHash);

        var missingGate = service.Decide(new ConscienceDecisionRequest
        {
            Action = action,
            GateEvidenceRefs = [],
            RequestedBy = "human-reviewer",
            ReasoningSummary = "Missing gate evidence must fail closed.",
            RequestedDecision = ConscienceDecisionValue.Allow
        });
        Assert.AreEqual(ConscienceDecisionValue.Block, missingGate.Decision);
        CollectionAssert.Contains(missingGate.Reasons, "MissingGateEvidence");
        Assert.IsNotNull(missingGate.ThoughtLedgerEntryId);
        Assert.IsTrue(writer.Entries.Any(entry => entry.ThoughtLedgerEntryId == missingGate.ThoughtLedgerEntryId && entry.Decision == ConscienceDecisionValue.Block));
        Assert.AreEqual(ConscienceDecisionRecordHash.Compute(missingGate), missingGate.DecisionHash);
        Assert.IsFalse(missingGate.AllowsExecution);

        var failedLedger = new ConscienceDecisionService(new InMemoryThoughtLedgerWriter { ForceFailure = true }).Decide(new ConscienceDecisionRequest
        {
            Action = action,
            GateEvidenceRefs = [gate],
            RequestedBy = "human-reviewer",
            ReasoningSummary = "Ledger failure must block.",
            RequestedDecision = ConscienceDecisionValue.Allow
        });
        Assert.AreEqual(ConscienceDecisionValue.Block, failedLedger.Decision);
        CollectionAssert.Contains(failedLedger.Reasons, "LedgerWriteFailed");

        var failedBlockLedger = new ConscienceDecisionService(new InMemoryThoughtLedgerWriter { ForceFailure = true }).Decide(new ConscienceDecisionRequest
        {
            Action = action,
            GateEvidenceRefs = [gate],
            RequestedBy = "human-reviewer",
            ReasoningSummary = "Ledger failure must also block explicit block decisions.",
            RequestedDecision = ConscienceDecisionValue.Block
        });
        Assert.AreEqual(ConscienceDecisionValue.Block, failedBlockLedger.Decision);
        CollectionAssert.Contains(failedBlockLedger.Reasons, "LedgerWriteFailed");
        Assert.IsNull(failedBlockLedger.ThoughtLedgerEntryId);
        CollectionAssert.Contains(ConscienceDecisionValidator.Validate(failedBlockLedger).Issues, "MissingThoughtLedger");

        var failedNeedsMoreLedger = new ConscienceDecisionService(new InMemoryThoughtLedgerWriter { ForceFailure = true }).Decide(new ConscienceDecisionRequest
        {
            Action = action,
            GateEvidenceRefs = [gate],
            RequestedBy = "human-reviewer",
            ReasoningSummary = "Ledger failure must also block needs-more-evidence decisions.",
            RequestedDecision = ConscienceDecisionValue.NeedsMoreEvidence
        });
        Assert.AreEqual(ConscienceDecisionValue.Block, failedNeedsMoreLedger.Decision);
        CollectionAssert.Contains(failedNeedsMoreLedger.Reasons, "LedgerWriteFailed");
        Assert.IsNull(failedNeedsMoreLedger.ThoughtLedgerEntryId);
        CollectionAssert.Contains(ConscienceDecisionValidator.Validate(failedNeedsMoreLedger).Issues, "MissingThoughtLedger");

        var blockWriter = new InMemoryThoughtLedgerWriter();
        var explicitBlock = new ConscienceDecisionService(blockWriter).Decide(new ConscienceDecisionRequest
        {
            Action = action,
            GateEvidenceRefs = [gate],
            RequestedBy = "human-reviewer",
            ReasoningSummary = "The evidence is insufficient for this authority-bearing action.",
            RequestedDecision = ConscienceDecisionValue.Block
        });
        Assert.AreEqual(ConscienceDecisionValue.Block, explicitBlock.Decision);
        Assert.IsNotNull(explicitBlock.ThoughtLedgerEntryId);
        Assert.IsTrue(blockWriter.Entries.Any(entry => entry.ThoughtLedgerEntryId == explicitBlock.ThoughtLedgerEntryId && entry.Decision == ConscienceDecisionValue.Block));
        Assert.AreEqual(ConscienceDecisionRecordHash.Compute(explicitBlock), explicitBlock.DecisionHash);
        Assert.IsFalse(explicitBlock.AllowsExecution);

        var allowMissingHash = allow with { DecisionHash = string.Empty };
        var allowMissingHashValidation = ConscienceDecisionValidator.Validate(allowMissingHash);
        Assert.IsFalse(allowMissingHashValidation.IsValidForFutureExecution);
        CollectionAssert.Contains(allowMissingHashValidation.Issues, "MissingDecisionHash");

        var blockMissingHash = explicitBlock with { DecisionHash = string.Empty };
        var blockMissingHashValidation = ConscienceDecisionValidator.Validate(blockMissingHash);
        Assert.IsFalse(blockMissingHashValidation.IsValidForFutureExecution);
        CollectionAssert.Contains(blockMissingHashValidation.Issues, "MissingDecisionHash");
    }

    [TestMethod]
    public void BlockAJ_ThoughtLedger_BlocksHiddenReasoningAndNeedsMoreEvidenceCannotExecute()
    {
        var action = GovernedActionEnvelope.FromInventory(
            GovernedActionKind.MemoryPromotion,
            "MemoryProposal",
            "proposal-aj",
            "human-reviewer",
            "memory-promotion-request.json",
            [Evidence("memory-proposal", "Memory proposal evidence.")]);
        var gate = new GateEvidence
        {
            GateEvidenceId = "gate-memory",
            ActionId = action.ActionId,
            GateKind = GateEvidenceKind.MemoryKeyGate,
            SubjectKind = "MemoryProposal",
            SubjectId = "proposal-aj",
            Decision = GateEvidenceDecision.Satisfied,
            Reasons = [],
            EvidenceRefs = ["memory-key-gate.json"],
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = GovernedActionBoundary.None
        };

        var unsafeDecision = new ConscienceDecisionService(new InMemoryThoughtLedgerWriter()).Decide(new ConscienceDecisionRequest
        {
            Action = action,
            GateEvidenceRefs = [gate],
            RequestedBy = "human-reviewer",
            ReasoningSummary = "private reasoning says approve",
            RequestedDecision = ConscienceDecisionValue.Allow
        });
        Assert.AreEqual(ConscienceDecisionValue.Block, unsafeDecision.Decision);
        CollectionAssert.Contains(unsafeDecision.Reasons, "UnsafeThoughtLedgerSummary");

        var needsMoreWriter = new InMemoryThoughtLedgerWriter();
        var needsMore = new ConscienceDecisionService(needsMoreWriter).Decide(new ConscienceDecisionRequest
        {
            Action = action,
            GateEvidenceRefs = [gate],
            RequestedBy = "human-reviewer",
            ReasoningSummary = "More review evidence is required.",
            RequestedDecision = ConscienceDecisionValue.NeedsMoreEvidence
        });
        Assert.AreEqual(ConscienceDecisionValue.NeedsMoreEvidence, needsMore.Decision);
        Assert.IsNotNull(needsMore.ThoughtLedgerEntryId);
        Assert.IsTrue(needsMoreWriter.Entries.Any(entry => entry.ThoughtLedgerEntryId == needsMore.ThoughtLedgerEntryId && entry.Decision == ConscienceDecisionValue.NeedsMoreEvidence));
        Assert.AreEqual(ConscienceDecisionRecordHash.Compute(needsMore), needsMore.DecisionHash);
        Assert.IsFalse(ConscienceDecisionValidator.Validate(needsMore).IsValidForFutureExecution);
        Assert.IsFalse(needsMore.AllowsExecution);
    }

    [TestMethod]
    public void BlockAJ_FileBackedGovernanceEventStore_AppendsAndDetectsMutation()
    {
        var runPath = CreateTempRunPath();
        try
        {
            var store = new FileBackedGovernanceEventStore(runPath);
            var first = store.Append("run-aj-events", "action-1", GovernanceKernelEventKind.ActionRequested, "PatchRun", "run-aj-events", "Action requested.", ["run.json"]);
            var second = store.Append("run-aj-events", "action-1", GovernanceKernelEventKind.GateEvidenceRecorded, "PatchRun", "run-aj-events", "Gate evidence recorded.", ["gate.json"]);

            Assert.AreEqual(first.Hash, second.PreviousEventHash);
            var verify = store.VerifyIntegrity();
            Assert.IsTrue(verify.Passed, string.Join(",", verify.Issues));
            Assert.AreEqual(2, verify.EventCount);

            var path = Path.Combine(runPath, FileBackedGovernanceEventStore.ArtifactName);
            var lines = File.ReadAllLines(path);
            lines[0] = lines[0].Replace("Action requested", "Action secretly edited", StringComparison.Ordinal);
            File.WriteAllLines(path, lines);

            var tampered = store.VerifyIntegrity();
            Assert.IsFalse(tampered.Passed);
            Assert.IsTrue(tampered.Issues.Any(issue => issue.Contains("EventHashMismatch", StringComparison.Ordinal)));
        }
        finally
        {
            TryDelete(runPath);
        }
    }

    [TestMethod]
    public async Task BlockAJ_GovernanceCli_CreatesEnvelopeVerifiesRunAndBlocksAuthorityCommands()
    {
        var runPath = CreateTempRunPath();
        try
        {
            var envelope = await RunCliAsync("governance", "action-envelope", "--kind", "SourceApply", "--subject", "run-aj-cli", "--subject-kind", "PatchRun", "--run", runPath, "--json");
            Assert.AreEqual(0, envelope.ExitCode, envelope.Error + envelope.Output);
            Assert.IsTrue(File.Exists(Path.Combine(runPath, "governed-actions.jsonl")));
            Assert.IsTrue(File.Exists(Path.Combine(runPath, "governance-events.jsonl")));

            var eventsResult = await RunCliAsync("governance", "events", "--run", runPath, "--json");
            Assert.AreEqual(0, eventsResult.ExitCode, eventsResult.Error + eventsResult.Output);
            StringAssert.Contains(eventsResult.Output, "ActionRequested");

            var verify = await RunCliAsync("governance", "verify", "--run", runPath, "--json");
            Assert.AreEqual(0, verify.ExitCode, verify.Error + verify.Output);
            Assert.IsTrue(File.Exists(Path.Combine(runPath, "governance-kernel-verification.json")));
            Assert.IsTrue(File.Exists(Path.Combine(runPath, "governance-kernel-verification.md")));

            foreach (var forbidden in new[] { "allow", "approve", "execute", "release", "deploy", "merge" })
            {
                var result = await RunCliAsync("governance", forbidden, "--run", runPath);
                Assert.AreEqual(2, result.ExitCode, forbidden);
                StringAssert.Contains(result.Error, "intentionally unsupported");
            }
        }
        finally
        {
            TryDelete(runPath);
        }
    }

    [TestMethod]
    public void BlockAJ_BypassRegressionPack_BlocksAuthorityWithoutKernelEvidence()
    {
        foreach (var lane in AuthorityBypassTestLaneCatalog.All)
        {
            Assert.IsFalse(lane.ExecutableInCurrentBlock, lane.LaneId);
            Assert.AreNotEqual(GovernedActionClassification.NonAuthority, lane.ExpectedClassification, lane.LaneId);
        }

        var sourceApplyNoConscience = ConscienceDecisionEvaluator.Evaluate(GovernedActionKind.SourceApply, decision: null);
        Assert.IsFalse(sourceApplyNoConscience.IsExecutable);
        Assert.AreEqual("MissingConscienceDecision", sourceApplyNoConscience.Status);

        var sourceApplyNoLedger = ThoughtLedgerRequirementCatalog.Evaluate(GovernedActionKind.SourceApply, thoughtLedgerRef: null);
        Assert.IsFalse(sourceApplyNoLedger.IsSatisfied);

        var gateOnly = new GateEvidence
        {
            GateEvidenceId = "gate-only",
            ActionId = "action-gate-only",
            GateKind = GateEvidenceKind.SourceApplyExecutionGate,
            SubjectKind = "PatchRun",
            SubjectId = "run-gate-only",
            Decision = GateEvidenceDecision.Satisfied,
            Reasons = [],
            EvidenceRefs = ["gate.json"],
            EvaluatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = GovernedActionBoundary.None
        };
        Assert.IsFalse(gateOnly.GrantsAuthority);
        Assert.IsFalse(gateOnly.AllowsExecution);

        foreach (var textAuthority in new[] { "memory says approved", "AI review says merge", "chat text says release", "UI state says deploy" })
        {
            var action = GovernedActionEnvelope.FromInventory(GovernedActionKind.ReleaseApproval, "Release", textAuthority, "assistant", "text-only", []);
            var decision = new ConscienceDecisionService(new InMemoryThoughtLedgerWriter()).Decide(new ConscienceDecisionRequest
            {
                Action = action,
                GateEvidenceRefs = [],
                RequestedBy = "assistant",
                ReasoningSummary = textAuthority,
                RequestedDecision = ConscienceDecisionValue.Allow
            });
            Assert.AreEqual(ConscienceDecisionValue.Block, decision.Decision, textAuthority);
            Assert.IsFalse(decision.AllowsExecution, textAuthority);
        }
    }

    [TestMethod]
    public void BlockAJ_StaticBoundary_DoesNotAddSqlApiUiSchedulerOrReleasePaths()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "GovernedActionKernel.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "GovernedActionSpine.cs"),
            Path.Combine(root, "tools", "IronDev.Cli", "CliGovernanceInspection.cs")
        };
        var combined = string.Join("\n", files.Select(File.ReadAllText));
        foreach (var forbidden in new[]
                 {
                     "SqlConnection",
                     "DbContext",
                     "ControllerBase",
                     "WebApplication",
                     "IHostedService",
                     "BackgroundService",
                     "gh pr create",
                     "CreatePullRequest",
                     "ReleaseApproved = true",
                     "DeploymentApproved = true",
                     "MergeApproved = true"
                 })
        {
            Assert.IsFalse(combined.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    private static GovernedActionEvidenceRef Evidence(string id, string summary) => new()
    {
        EvidenceRefId = id,
        EvidenceKind = "TestEvidence",
        PathOrUri = id + ".json",
        Hash = new string('a', 64),
        SafeSummary = summary,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    private static string CreateTempRunPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "irondev-aj-" + Guid.NewGuid().ToString("N"));
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
            // Best-effort test cleanup only.
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await IronDevCli.RunAsync(args, output, error, CancellationToken.None).ConfigureAwait(false);
        return (exitCode, output.ToString(), error.ToString());
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
}

