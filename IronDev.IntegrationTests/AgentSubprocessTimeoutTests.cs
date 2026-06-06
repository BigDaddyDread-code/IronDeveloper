using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;
using IronDev.Infrastructure.Services.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentSubprocessTimeoutTests
{
    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "IronDev.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }

    [TestMethod]
    public async Task AgentProcessRunner_Timeout_ShouldKillProcessAndReturnTimedOut()
    {
        var runner = new AgentProcessRunner();
        var repoRoot = GetRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "tools", "dogfood", $"timeout-test-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(scriptPath, "Start-Sleep -Seconds 5");
        Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", "1");
        try
        {
            var result = await runner.RunAsync(
                "powershell",
                ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath],
                repoRoot);

            Assert.IsTrue(result.TimedOut);
            Assert.AreEqual(-1, result.ExitCode);
            StringAssert.Contains(result.Stderr, "powershell subprocess timed out after 1s and was killed");
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", null);
            if (File.Exists(scriptPath))
                File.Delete(scriptPath);
        }
    }

    [TestMethod]
    public async Task AgentProcessRunner_ShouldRejectPowerShellCommandArgument()
    {
        var runner = new AgentProcessRunner();

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            runner.RunAsync(
                "powershell",
                ["-NoProfile", "-Command", "Write-Output unsafe"],
                GetRepoRoot()));

        StringAssert.Contains(ex.Message, "-Command is not allowed");
    }

    [TestMethod]
    public async Task AgentProcessRunner_ShouldRejectPowerShellEncodedCommandArgument()
    {
        var runner = new AgentProcessRunner();

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            runner.RunAsync(
                "powershell",
                ["-NoProfile", "-EncodedCommand", "VwByAGkAdABlAC0ASABvAHMAdAAgAHUAbgBzAGEAZgBlAA=="],
                GetRepoRoot()));

        StringAssert.Contains(ex.Message, "-EncodedCommand is not allowed");
    }

    [TestMethod]
    public async Task SupervisorAgent_Timeout_ShouldKillProcessAndFailGracefully()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "SupervisorAgent",
            Purpose = "Test SupervisorAgent timeout behavior.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new SupervisorAgent(definition, resolver, repoRoot);

        var request = new AgentRequest
        {
            AgentName = "SupervisorAgent",
            GoalId = "test-goal",
            DogfoodRunId = "test-run",
            Inputs = new Dictionary<string, string>
            {
                ["project"] = "TestProject",
                ["query"] = "TestQuery",
                ["plan_path"] = "TestPlan.md",
                ["live_llm"] = "false"
            }
        };

        Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", "1");
        try
        {
            var result = await agent.RunAsync(request);

            Assert.AreEqual(AgentRunStatus.Failed, result.Status);
            Assert.AreEqual(1, result.ExitCode); // SupervisorAgent maps failure to 1
            Assert.IsNotNull(result.OutputJson);
            StringAssert.Contains(result.Summary, "subprocess timeout");
            Assert.IsTrue(result.CommandsRun.Count > 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", null);
        }
    }

    [TestMethod]
    public async Task RetrieverAgent_Timeout_ShouldKillProcessAndFailGracefully()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "RetrieverAgent",
            Purpose = "Test RetrieverAgent timeout behavior.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new RetrieverAgent(definition, resolver, repoRoot);

        var request = new AgentRequest
        {
            AgentName = "RetrieverAgent",
            GoalId = "test-goal",
            DogfoodRunId = "test-run",
            Inputs = new Dictionary<string, string>
            {
                ["project"] = "TestProject",
                ["query"] = "TestQuery",
                ["live_llm"] = "false"
            }
        };

        Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", "1");
        try
        {
            var result = await agent.RunAsync(request);

            Assert.AreEqual(AgentRunStatus.Failed, result.Status);
            Assert.AreEqual(-1, result.ExitCode);
            Assert.IsNotNull(result.OutputJson);
            StringAssert.Contains(result.Summary, "RetrieverAgent subprocess timed out after 1s.");
            StringAssert.Contains(result.OutputJson, "dotnet subprocess timed out after 1s and was killed.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", null);
        }
    }

    [TestMethod]
    public async Task QualityAgent_Timeout_ShouldKillProcessAndFailGracefully()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "QualityAgent",
            Purpose = "Test QualityAgent timeout behavior.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new QualityAgent(definition, resolver, repoRoot);

        var request = new AgentRequest
        {
            AgentName = "QualityAgent",
            GoalId = "test-goal",
            DogfoodRunId = "test-run",
            Inputs = new Dictionary<string, string>
            {
                ["project"] = "TestProject",
                ["plan_path"] = "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                ["live_llm"] = "false"
            }
        };

        Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", "1");
        try
        {
            var result = await agent.RunAsync(request);

            Assert.AreEqual(AgentRunStatus.Failed, result.Status);
            Assert.AreEqual(-1, result.ExitCode);
            Assert.IsNotNull(result.OutputJson);
            StringAssert.Contains(result.Summary, "QualityAgent subprocess timed out after 1s.");
            StringAssert.Contains(result.OutputJson, "QualityAgent subprocess timed out after 1s.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", null);
        }
    }

    [TestMethod]
    public async Task TesterAgent_ShouldRejectAbsolutePlanPath()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "TesterAgent",
            Purpose = "Test TesterAgent plan path boundary.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new TesterAgent(definition, resolver, repoRoot);

        var request = new AgentRequest
        {
            AgentName = "TesterAgent",
            Inputs = new Dictionary<string, string>
            {
                ["plan_path"] = Path.Combine(repoRoot, "tools", "dogfood", "test-agent-plans", "irondev-code-standards-alpha.json")
            }
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => agent.RunAsync(request));
        StringAssert.Contains(ex.Message, "must be relative");
    }

    [TestMethod]
    public async Task TesterAgent_ShouldRejectPlanTraversal()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "TesterAgent",
            Purpose = "Test TesterAgent plan path boundary.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new TesterAgent(definition, resolver, repoRoot);

        var request = new AgentRequest
        {
            AgentName = "TesterAgent",
            Inputs = new Dictionary<string, string>
            {
                ["plan_path"] = "../outside.json"
            }
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => agent.RunAsync(request));
        StringAssert.Contains(ex.Message, "must stay under");
    }

    [TestMethod]
    public async Task QualityAgent_ShouldRejectUnsafePlanPath()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "QualityAgent",
            Purpose = "Test QualityAgent plan path boundary.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new QualityAgent(definition, resolver, repoRoot);

        var request = new AgentRequest
        {
            AgentName = "QualityAgent",
            Inputs = new Dictionary<string, string>
            {
                ["plan_path"] = "TestPlan.md",
                ["live_llm"] = "false"
            }
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => agent.RunAsync(request));
        StringAssert.Contains(ex.Message, "approved JSON test-plan");
    }

    [TestMethod]
    public async Task TesterAgent_ShouldUseGovernedProcessExecutor()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "TesterAgent",
            Purpose = "Test TesterAgent should use governed process executor.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var executor = new FakeGovernedAgentProcessExecutor(request => new GovernedAgentProcessResult
        {
            ToolCallId = request.ToolCallId,
            Command = "powershell \"-NoProfile\" \"-ExecutionPolicy\" \"Bypass\" \"-File\" \"stub\"",
            ExitCode = 0,
            Stdout = """{"summary":"TesterExecutorPath","evidence":[{"path":"stub.evidence.json"}]}""",
            Stderr = string.Empty,
            TimedOut = false,
            EvidencePaths = ["test-results/tester-executor-stdout.log"],
            DurationMs = 0
        });
        var agent = new TesterAgent(definition, resolver, repoRoot, executor);

        var request = new AgentRequest
        {
            AgentName = "TesterAgent",
            GoalId = "test-goal",
            DogfoodRunId = "test-run",
            Inputs = new Dictionary<string, string>
            {
                ["plan_path"] = "irondev-code-standards-alpha.json"
            }
        };

        var result = await agent.RunAsync(request);

        Assert.AreEqual(1, executor.InvocationCount);
        Assert.AreEqual("powershell", executor.Calls[0].FileName);
        StringAssert.Contains(result.Summary, "TesterExecutorPath");
        Assert.AreEqual(1, result.CommandsRun.Count);
        StringAssert.Contains(result.CommandsRun[0], "powershell");
        CollectionAssert.Contains(result.EvidencePaths.ToArray(), "test-results/tester-executor-stdout.log");
    }

    [TestMethod]
    public async Task QualityAgent_ShouldUseGovernedProcessExecutor()
    {
        var repoRoot = GetRepoRoot();
        var definition = new AgentDefinition
        {
            Name = "QualityAgent",
            Purpose = "Test QualityAgent should use governed process executor.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var executor = new FakeGovernedAgentProcessExecutor(request => new GovernedAgentProcessResult
        {
            ToolCallId = request.ToolCallId,
            Command = "powershell \"-NoProfile\" \"-ExecutionPolicy\" \"Bypass\" \"-File\" \"stub\"",
            ExitCode = 0,
            Stdout = """{"status":"passed","summary":"QualityExecutorPath","steps":[]}""",
            Stderr = string.Empty,
            TimedOut = false,
            EvidencePaths = ["test-results/quality-executor-stdout.log"],
            DurationMs = 0
        });
        var agent = new QualityAgent(definition, resolver, repoRoot, null, executor);

        var request = new AgentRequest
        {
            AgentName = "QualityAgent",
            GoalId = "test-goal",
            DogfoodRunId = "test-run",
            Inputs = new Dictionary<string, string>
            {
                ["plan_path"] = "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
                ["live_llm"] = "false"
            }
        };

        var result = await agent.RunAsync(request);

        Assert.AreEqual(1, executor.InvocationCount);
        Assert.AreEqual("powershell", executor.Calls[0].FileName);
        Assert.AreEqual(AgentRunStatus.Succeeded, result.Status);
        StringAssert.Contains(result.CommandsRun[0], "powershell");
        CollectionAssert.Contains(result.EvidencePaths.ToArray(), "test-results/quality-executor-stdout.log");
    }

    [TestMethod]
    public void AgentProcessBoundaryTests_ShouldNotUseRawProcessInAgents()
    {
        var testerSource = File.ReadAllText(
            Path.Combine(GetRepoRoot(), "IronDev.Infrastructure", "Services", "Agents", "TesterAgent.cs"));
        var qualitySource = File.ReadAllText(
            Path.Combine(GetRepoRoot(), "IronDev.Infrastructure", "Services", "Agents", "QualityAgent.cs"));

        Assert.IsFalse(testerSource.Contains("new Process", StringComparison.Ordinal), "TesterAgent should not instantiate raw Process.");
        Assert.IsFalse(qualitySource.Contains("new Process", StringComparison.Ordinal), "QualityAgent should not instantiate raw Process.");
        Assert.IsFalse(testerSource.Contains("ProcessStartInfo", StringComparison.Ordinal), "TesterAgent should not configure raw ProcessStartInfo.");
        Assert.IsFalse(qualitySource.Contains("ProcessStartInfo", StringComparison.Ordinal), "QualityAgent should not configure raw ProcessStartInfo.");
    }

    private sealed class FakeGovernedAgentProcessExecutor : IGovernedAgentProcessExecutor
    {
        private readonly Func<GovernedAgentProcessRequest, GovernedAgentProcessResult> _resultFactory;
        private readonly List<GovernedAgentProcessRequest> _calls = [];

        public FakeGovernedAgentProcessExecutor(Func<GovernedAgentProcessRequest, GovernedAgentProcessResult> resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public int InvocationCount => _calls.Count;
        public IReadOnlyList<GovernedAgentProcessRequest> Calls => _calls;

        public Task<GovernedAgentProcessResult> ExecuteAsync(
            GovernedAgentProcessRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _calls.Add(request);
            return Task.FromResult(_resultFactory(request));
        }
    }
}