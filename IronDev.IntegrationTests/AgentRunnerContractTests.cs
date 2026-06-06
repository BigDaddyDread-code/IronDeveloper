using IronDev.Core.Agents;
using IronDev.Core.Interfaces;
using IronDev.Infrastructure.Services.Agents;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentRunnerContractTests
{
    [TestMethod]
    public async Task RunAsync_ShouldStampRunMetadata()
    {
        var definition = new AgentDefinition
        {
            Name = "ContractAgent",
            Purpose = "Exercise AgentRunner contract stamping.",
            DefaultModelProfile = "cheap-runner",
            AllowedTools = ["memory.search"]
        };
        var agent = new FakeAgent(definition, AgentRunStatus.Succeeded);
        var runner = new AgentRunner(new AgentRegistry([agent], [definition]));

        var result = await runner.RunAsync(new AgentRequest
        {
            AgentName = "ContractAgent",
            GoalId = "goal-123",
            DogfoodRunId = "run-456",
            RequestedTools = ["memory.search"]
        });

        Assert.AreEqual(AgentRunStatus.Succeeded, result.Status);
        Assert.AreEqual("goal-123", result.GoalId);
        Assert.AreEqual("run-456", result.DogfoodRunId);
        CollectionAssert.AreEqual(new[] { "memory.search" }, result.RequestedTools.ToArray());
        CollectionAssert.AreEqual(new[] { "memory.search" }, result.AllowedTools.ToArray());
        Assert.IsTrue(result.DurationMs >= 0);
        Assert.IsTrue(result.StartedAtUtc <= result.CompletedAtUtc);
        Assert.AreEqual(1, agent.RunCount);
    }

    [TestMethod]
    public async Task RunAsync_ShouldBlockToolsOutsideAgentDefinition()
    {
        var definition = new AgentDefinition
        {
            Name = "ContractAgent",
            Purpose = "Exercise AgentRunner tool enforcement.",
            DefaultModelProfile = "cheap-runner",
            AllowedTools = ["memory.search"]
        };
        var agent = new FakeAgent(definition, AgentRunStatus.Succeeded);
        var runner = new AgentRunner(new AgentRegistry([agent], [definition]));

        var result = await runner.RunAsync(new AgentRequest
        {
            AgentName = "ContractAgent",
            GoalId = "goal-123",
            DogfoodRunId = "run-456",
            RequestedTools = ["memory.search", "repo.write"]
        });

        Assert.AreEqual(AgentRunStatus.Blocked, result.Status);
        Assert.AreEqual(1, result.ExitCode);
        StringAssert.Contains(result.Summary, "repo.write");
        StringAssert.Contains(result.OutputJson, "AgentRunner enforces declared AgentDefinition.AllowedTools");
        Assert.AreEqual(0, agent.RunCount);
    }

    [TestMethod]
    public async Task RunAsync_ShouldBlockDisabledAgents()
    {
        var definition = new AgentDefinition
        {
            Name = "ContractAgent",
            Purpose = "Exercise AgentRunner enabled enforcement.",
            DefaultModelProfile = "cheap-runner",
            Enabled = false,
            AllowedTools = ["memory.search"]
        };
        var agent = new FakeAgent(definition, AgentRunStatus.Succeeded);
        var runner = new AgentRunner(new AgentRegistry([agent], [definition]));

        var result = await runner.RunAsync(new AgentRequest
        {
            AgentName = "ContractAgent",
            GoalId = "goal-123",
            DogfoodRunId = "run-456",
            RequestedTools = ["memory.search"]
        });

        Assert.AreEqual(AgentRunStatus.Blocked, result.Status);
        StringAssert.Contains(result.Summary, "disabled");
        Assert.AreEqual(0, agent.RunCount);
    }

    [TestMethod]
    public async Task RunAsync_ShouldAllowReadOnlyTypedToolWithoutApproval()
    {
        var definition = new AgentDefinition
        {
            Name = "ContractAgent",
            Purpose = "Exercise read-only typed tool enforcement.",
            DefaultModelProfile = "cheap-runner",
            AllowedTools = ["memory.search"]
        };
        var agent = new FakeAgent(definition, AgentRunStatus.Succeeded);
        var runner = new AgentRunner(new AgentRegistry([agent], [definition]));

        var result = await runner.RunAsync(new AgentRequest
        {
            AgentName = "ContractAgent",
            RequestedToolCalls =
            [
                new AgentToolCallRequest
                {
                    ToolName = "memory.search",
                    Impact = AgentActionImpact.ReadOnly
                }
            ]
        });

        Assert.AreEqual(AgentRunStatus.Succeeded, result.Status);
        Assert.AreEqual(AgentApprovalDecision.NotRequired, result.ApprovalDecision);
        Assert.IsTrue(result.WasDryRun);
        Assert.AreEqual(1, agent.RunCount);
    }

    [TestMethod]
    public async Task RunAsync_ShouldBlockHighImpactActionWithoutApproval()
    {
        var (runner, agent) = BuildRunner(["test.run"]);

        var result = await runner.RunAsync(new AgentRequest
        {
            AgentName = "ContractAgent",
            RequestedToolCalls =
            [
                new AgentToolCallRequest
                {
                    ToolName = "test.run",
                    Impact = AgentActionImpact.ProcessExecution,
                    RequiresApproval = true,
                    AllowsProcessExecution = true,
                    EvidenceRequired = true,
                    ApprovalScope = "test-plan"
                }
            ],
            DryRunOnly = false
        });

        Assert.AreEqual(AgentRunStatus.Blocked, result.Status);
        Assert.AreEqual(AgentApprovalDecision.Missing, result.ApprovalDecision);
        StringAssert.Contains(result.Summary, "requires typed approval evidence");
        Assert.AreEqual(0, agent.RunCount);
    }

    [TestMethod]
    public async Task RunAsync_ShouldBlockHighImpactActionWhileDryRunOnly()
    {
        var (runner, agent) = BuildRunner(["test.run"]);

        var result = await runner.RunAsync(new AgentRequest
        {
            AgentName = "ContractAgent",
            ProposalId = "proposal-1",
            ProposalHash = "hash-1",
            RequestedToolCalls =
            [
                new AgentToolCallRequest
                {
                    ToolName = "test.run",
                    Impact = AgentActionImpact.ProcessExecution,
                    RequiresApproval = true,
                    AllowsProcessExecution = true
                }
            ],
            ApprovalEvidence = ApprovedEvidence("proposal-1", "hash-1")
        });

        Assert.AreEqual(AgentRunStatus.Blocked, result.Status);
        StringAssert.Contains(result.Summary, "DryRunOnly");
        Assert.AreEqual(0, agent.RunCount);
    }

    [TestMethod]
    public async Task RunAsync_ShouldBlockApprovalHashMismatch()
    {
        var (runner, agent) = BuildRunner(["test.run"]);

        var result = await runner.RunAsync(new AgentRequest
        {
            AgentName = "ContractAgent",
            ProposalId = "proposal-1",
            ProposalHash = "hash-1",
            DryRunOnly = false,
            RequestedToolCalls = [HighImpactTestRun()],
            ApprovalEvidence = ApprovedEvidence("proposal-1", "different-hash")
        });

        Assert.AreEqual(AgentRunStatus.Blocked, result.Status);
        Assert.AreEqual(AgentApprovalDecision.Invalid, result.ApprovalDecision);
        StringAssert.Contains(result.Summary, "does not match");
        Assert.AreEqual(0, agent.RunCount);
    }

    [TestMethod]
    public async Task RunAsync_ShouldBlockExpiredApproval()
    {
        var (runner, agent) = BuildRunner(["test.run"]);

        var result = await runner.RunAsync(new AgentRequest
        {
            AgentName = "ContractAgent",
            ProposalId = "proposal-1",
            ProposalHash = "hash-1",
            DryRunOnly = false,
            RequestedToolCalls = [HighImpactTestRun()],
            ApprovalEvidence = ApprovedEvidence(
                "proposal-1",
                "hash-1",
                expiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1))
        });

        Assert.AreEqual(AgentRunStatus.Blocked, result.Status);
        Assert.AreEqual(AgentApprovalDecision.Expired, result.ApprovalDecision);
        Assert.AreEqual(0, agent.RunCount);
    }

    [TestMethod]
    public async Task RunAsync_ShouldBlockApprovalScopeMismatch()
    {
        var (runner, agent) = BuildRunner(["test.run"]);

        var result = await runner.RunAsync(new AgentRequest
        {
            AgentName = "ContractAgent",
            ProposalId = "proposal-1",
            ProposalHash = "hash-1",
            DryRunOnly = false,
            RequestedToolCalls = [HighImpactTestRun()],
            ApprovalEvidence = ApprovedEvidence("proposal-1", "hash-1", scope: "wrong-scope")
        });

        Assert.AreEqual(AgentRunStatus.Blocked, result.Status);
        Assert.AreEqual(AgentApprovalDecision.Invalid, result.ApprovalDecision);
        StringAssert.Contains(result.Summary, "scope");
        Assert.AreEqual(0, agent.RunCount);
    }

    [TestMethod]
    public async Task RunAsync_ShouldBlockMissingEvidenceHash()
    {
        var (runner, agent) = BuildRunner(["test.run"]);

        var result = await runner.RunAsync(new AgentRequest
        {
            AgentName = "ContractAgent",
            ProposalId = "proposal-1",
            ProposalHash = "hash-1",
            DryRunOnly = false,
            RequestedToolCalls = [HighImpactTestRun()],
            ApprovalEvidence = ApprovedEvidence("proposal-1", "hash-1", evidenceSha256: "")
        });

        Assert.AreEqual(AgentRunStatus.Blocked, result.Status);
        Assert.AreEqual(AgentApprovalDecision.Invalid, result.ApprovalDecision);
        StringAssert.Contains(result.Summary, "hashed evidence");
        Assert.AreEqual(0, agent.RunCount);
    }

    [TestMethod]
    public void AgentRequest_DryRunOnlyDefaultsTrue()
    {
        var request = new AgentRequest { AgentName = "ContractAgent" };

        Assert.IsTrue(request.DryRunOnly);
    }

    [TestMethod]
    public void AgentEvidenceItem_RequiresSha256Integrity()
    {
        var evidence = new AgentEvidenceItem
        {
            EvidenceId = "evidence-1",
            Kind = "test-report",
            Path = "tools/dogfood/runs/run-1/report.json",
            Sha256 = "d5277a68a38f28a4288f2a868a577d38",
            ProducedBy = "TesterAgent"
        };

        Assert.IsFalse(string.IsNullOrWhiteSpace(evidence.Sha256));
    }

    private static (AgentRunner Runner, FakeAgent Agent) BuildRunner(IReadOnlyList<string> allowedTools)
    {
        var definition = new AgentDefinition
        {
            Name = "ContractAgent",
            Purpose = "Exercise AgentRunner typed governance.",
            DefaultModelProfile = "cheap-runner",
            AllowedTools = allowedTools
        };
        var agent = new FakeAgent(definition, AgentRunStatus.Succeeded);
        return (new AgentRunner(new AgentRegistry([agent], [definition])), agent);
    }

    private static AgentToolCallRequest HighImpactTestRun() => new()
    {
        ToolName = "test.run",
        Impact = AgentActionImpact.ProcessExecution,
        RequiresApproval = true,
        AllowsProcessExecution = true,
        EvidenceRequired = true,
        ApprovalScope = "test-plan"
    };

    private static AgentApprovalEvidence ApprovedEvidence(
        string proposalId,
        string proposalHash,
        string scope = "test-plan",
        DateTimeOffset? expiresAtUtc = null,
        string evidenceSha256 = "d5277a68a38f28a4288f2a868a577d38") => new()
    {
        ApprovalId = "approval-1",
        ProposalId = proposalId,
        ProposalHash = proposalHash,
        ApprovedBy = "human-reviewer",
        Scope = scope,
        ExpiresAtUtc = expiresAtUtc ?? DateTimeOffset.UtcNow.AddMinutes(5),
        Evidence =
        [
            new AgentEvidenceItem
            {
                EvidenceId = "evidence-1",
                Kind = "test-report",
                Path = "tools/dogfood/runs/run-1/report.json",
                Sha256 = evidenceSha256,
                ProducedBy = "TesterAgent"
            }
        ]
    };

    private sealed class FakeAgent : IIronDevAgent
    {
        private readonly AgentDefinition _definition;
        private readonly AgentRunStatus _status;

        public FakeAgent(AgentDefinition definition, AgentRunStatus status)
        {
            _definition = definition;
            _status = status;
        }

        public string AgentName => _definition.Name;
        public string Purpose => _definition.Purpose;
        public string DefaultModelProfile => _definition.DefaultModelProfile;
        public IReadOnlyList<string> AllowedTools => _definition.AllowedTools;
        public int RunCount { get; private set; }

        public Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
        {
            RunCount++;
            return Task.FromResult(new AgentResult
            {
                AgentName = AgentName,
                Status = _status,
                Summary = "Fake agent completed.",
                ModelProfileName = DefaultModelProfile,
                Provider = "OpenAI",
                Model = "test-model",
                ExitCode = 0,
                CompletedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }
}
