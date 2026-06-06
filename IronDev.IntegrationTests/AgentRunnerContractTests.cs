using IronDev.Core.Agents;
using IronDev.Core.Interfaces;
using IronDev.Infrastructure.Services.Agents;
using System.Text.RegularExpressions;

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
            ProposalId = "proposal-abc",
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
        Assert.AreNotEqual("proposal-abc", result.TraceId);
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
    public async Task RunAsync_ShouldBlockLegacyRequestedHighImpactToolWithoutTypedMetadata()
    {
        var (runner, agent) = BuildRunner(["test.run"]);

        var result = await runner.RunAsync(new AgentRequest
        {
            AgentName = "ContractAgent",
            ProposalId = "proposal-1",
            ProposalHash = "hash-1",
            RequestedTools = ["test.run"],
            DryRunOnly = false,
            ApprovalEvidence = ApprovedEvidence("proposal-1", "hash-1")
        });

        Assert.AreEqual(AgentRunStatus.Blocked, result.Status);
        Assert.AreEqual(AgentApprovalDecision.Missing, result.ApprovalDecision);
        StringAssert.Contains(result.Summary, "legacy requested high-impact tools");
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
            RequestedToolCalls = [HighImpactTestRun()],
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
                HighImpactTestRun()
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
        StringAssert.Contains(result.Summary, "invalid evidence metadata");
        Assert.AreEqual(0, agent.RunCount);
    }

    [TestMethod]
    public async Task RunAsync_ShouldBlockInvalidEvidenceSha256()
    {
        var (runner, agent) = BuildRunner(["test.run"]);

        var result = await runner.RunAsync(new AgentRequest
        {
            AgentName = "ContractAgent",
            ProposalId = "proposal-1",
            ProposalHash = "hash-1",
            DryRunOnly = false,
            RequestedToolCalls = [HighImpactTestRun()],
            ApprovalEvidence = ApprovedEvidence("proposal-1", "hash-1", evidenceSha256: "abc")
        });

        Assert.AreEqual(AgentRunStatus.Blocked, result.Status);
        Assert.AreEqual(AgentApprovalDecision.Invalid, result.ApprovalDecision);
        Assert.IsTrue(result.Summary.Contains("invalid evidence metadata", StringComparison.Ordinal));
        Assert.AreEqual(0, agent.RunCount);
    }

    [TestMethod]
    public async Task RunAsync_ShouldBlockWhenHighImpactApprovalDecisionIsInvalid()
    {
        var (runner, agent) = BuildRunner(["test.run"]);

        var result = await runner.RunAsync(new AgentRequest
        {
            AgentName = "ContractAgent",
            ProposalId = "proposal-1",
            ProposalHash = "hash-1",
            DryRunOnly = false,
            RequestedToolCalls = [HighImpactTestRun()],
            ApprovalEvidence = new AgentApprovalEvidence
            {
                ApprovalId = "approval-1",
                ProposalId = "proposal-1",
                ProposalHash = "hash-1",
                Scope = "test-plan",
                ApprovedBy = "human-reviewer",
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
                ApprovedToolCallIds = ["test-run-tool-call-id"],
                Evidence =
                [
                    new AgentEvidenceItem
                    {
                        EvidenceId = "evidence-1",
                        Kind = "test-report",
                        Path = "tools/dogfood/runs/run-1/report.json",
                        Sha256 = "d5277a68a38f28a4288f2a868a577d3872f1ec4f5a9ccf8b6d67f8a5d7d2f4e3a1",
                        ProducedBy = "TesterAgent"
                    }
                ]
            }
        });

        Assert.AreEqual(AgentRunStatus.Blocked, result.Status);
        Assert.AreEqual(AgentApprovalDecision.Invalid, result.ApprovalDecision);
        StringAssert.Contains(result.Summary, "not approved");
        Assert.AreEqual(0, agent.RunCount);
    }

    [TestMethod]
    public async Task RunAsync_ShouldBlockHighImpactActionWithWrongToolCallId()
    {
        var (runner, agent) = BuildRunner(["test.run"]);

        var result = await runner.RunAsync(new AgentRequest
        {
            AgentName = "ContractAgent",
            ProposalId = "proposal-1",
            ProposalHash = "hash-1",
            DryRunOnly = false,
            RequestedToolCalls =
            [
                new AgentToolCallRequest
                {
                    ToolName = "test.run",
                    ToolCallId = "wrong-call-id",
                    Impact = AgentActionImpact.ProcessExecution,
                    RequiresApproval = true,
                    AllowsProcessExecution = true,
                    EvidenceRequired = true,
                    ApprovalScope = "test-plan"
                }
            ],
            ApprovalEvidence = ApprovedEvidence("proposal-1", "hash-1", approvedToolCallIds: ["expected-call-id"])
        });

        Assert.AreEqual(AgentRunStatus.Blocked, result.Status);
        Assert.AreEqual(AgentApprovalDecision.Invalid, result.ApprovalDecision);
        StringAssert.Contains(result.Summary, "ToolCallId");
        Assert.AreEqual(0, agent.RunCount);
    }

    [TestMethod]
    public void AgentRequest_DryRunOnlyDefaultsTrue()
    {
        var request = new AgentRequest { AgentName = "ContractAgent" };

        Assert.IsTrue(request.DryRunOnly);
    }

    [TestMethod]
    public void AgentEvidenceItem_RequiresValidSha256Integrity()
    {
        var evidence = new AgentEvidenceItem
        {
            EvidenceId = "evidence-1",
            Kind = "test-report",
            Path = "tools/dogfood/runs/run-1/report.json",
            Sha256 = "a5f0b4a2d9c8e7b6a1d2f3c4e5b6a798d12f8c3b6d4f5e7a8c9b0d1e2f3a4b5c6",
            ProducedBy = "TesterAgent"
        };

        Assert.IsTrue(Regex.IsMatch(evidence.Sha256, "^[A-Fa-f0-9]{64}$"));
        Assert.IsFalse(Regex.IsMatch("abc", "^[A-Fa-f0-9]{64}$"));
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
        ToolCallId = "test-run-tool-call-id",
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
        string evidenceSha256 = "a5f0b4a2d9c8e7b6a1d2f3c4e5b6a798d12f8c3b6d4f5e7a8c9b0d1e2f3a4b5c6",
        IEnumerable<string>? approvedToolCallIds = null) => new()
    {
        ApprovalId = "approval-1",
        ProposalId = proposalId,
        ProposalHash = proposalHash,
        ApprovedBy = "human-reviewer",
        Scope = scope,
        Decision = AgentApprovalDecision.Approved,
        ExpiresAtUtc = expiresAtUtc ?? DateTimeOffset.UtcNow.AddMinutes(5),
        ApprovedToolCallIds = approvedToolCallIds?.ToArray() ?? ["test-run-tool-call-id"],
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
