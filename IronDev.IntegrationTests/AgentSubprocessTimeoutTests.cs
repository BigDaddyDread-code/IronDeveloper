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
        Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", "1");
        try
        {
            // powershell -Command Start-Sleep -Seconds 5 hangs reliably for 5 seconds.
            var result = await runner.RunAsync("powershell", new[] { "-Command", "Start-Sleep -Seconds 5" }, GetRepoRoot());

            Assert.IsTrue(result.TimedOut);
            Assert.AreEqual(-1, result.ExitCode);
            StringAssert.Contains(result.Stderr, "powershell subprocess timed out after 1s and was killed");
        }
        finally
        {
            Environment.SetEnvironmentVariable("IRONDEV_SUBPROCESS_TIMEOUT_SECONDS", null);
        }
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
                ["plan_path"] = "TestPlan.md",
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
}
