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
