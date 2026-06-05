using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;
using IronDev.Infrastructure.Services.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ConscienceAgentTests
{
    private static AgentRequest CreateRequest(string actionType, string requestedTools, string safetyBoundaryRefs = "disposable workspace")
    {
        var request = new AgentRequest
        {
            AgentName = "ConscienceAgent",
            GoalId = "test-goal",
            DogfoodRunId = "test-run",
            Inputs = new Dictionary<string, string>
            {
                ["action_type"] = actionType,
                ["observed_project"] = "TestProject",
                ["affected_project"] = "TestProject",
                ["evidence"] = "memory returned successfully",
                ["requested_tools"] = requestedTools,
                ["memory_authority_refs"] = "WeightedContextBundle",
                ["safety_boundary_refs"] = safetyBoundaryRefs
            }
        };
        return request;
    }

    [TestMethod]
    [DataRow("self-approve")]
    [DataRow("self approve")]
    [DataRow("approve itself")]
    [DataRow("auto-merge")]
    [DataRow("automerge")]
    public async Task ConscienceAgent_SelfApproval_ShouldBlock(string triggerWord)
    {
        var definition = new AgentDefinition
        {
            Name = "ConscienceAgent",
            Purpose = "Test safety rules.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new ConscienceAgent(definition, resolver);

        var request = CreateRequest(triggerWord, "git");
        var result = await agent.RunAsync(request);

        // ConscienceAgent execution itself succeeds, but decision in JSON output should be "Block"
        Assert.AreEqual(AgentRunStatus.Succeeded, result.Status);
        Assert.AreEqual(0, result.ExitCode);
        Assert.IsNotNull(result.OutputJson);

        using var doc = JsonDocument.Parse(result.OutputJson);
        var root = doc.RootElement;
        
        Assert.AreEqual("Block", root.GetProperty("decision").GetString());
        
        var violatedBoundaries = new List<string>();
        foreach (var item in root.GetProperty("violatedBoundaries").EnumerateArray())
        {
            violatedBoundaries.Add(item.GetString()!);
        }
        CollectionAssert.Contains(violatedBoundaries, "NoAgentSelfApproval");

        var blockingFactors = new List<string>();
        foreach (var item in root.GetProperty("blockingFactors").EnumerateArray())
        {
            blockingFactors.Add(item.GetString()!);
        }
        Assert.IsTrue(blockingFactors.Exists(f => f.Contains("implies self-approval") || f.Contains("auto-merge")));

        // Confidence should scale
        var confidence = root.GetProperty("confidence").GetDecimal();
        Assert.IsTrue(confidence > 0.88m);
    }

    [TestMethod]
    [DataRow("bypass")]
    [DataRow("skip conscience")]
    [DataRow("skip thoughtledger")]
    [DataRow("override governance")]
    public async Task ConscienceAgent_GovernanceBypass_ShouldBlock(string triggerWord)
    {
        var definition = new AgentDefinition
        {
            Name = "ConscienceAgent",
            Purpose = "Test safety rules.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new ConscienceAgent(definition, resolver);

        var request = CreateRequest(triggerWord, "git");
        var result = await agent.RunAsync(request);

        Assert.AreEqual(AgentRunStatus.Succeeded, result.Status);
        Assert.AreEqual(0, result.ExitCode);
        Assert.IsNotNull(result.OutputJson);

        using var doc = JsonDocument.Parse(result.OutputJson);
        var root = doc.RootElement;
        
        Assert.AreEqual("Block", root.GetProperty("decision").GetString());
        
        var violatedBoundaries = new List<string>();
        foreach (var item in root.GetProperty("violatedBoundaries").EnumerateArray())
        {
            violatedBoundaries.Add(item.GetString()!);
        }
        CollectionAssert.Contains(violatedBoundaries, "GovernanceGatesCannotBeBypassed");

        var blockingFactors = new List<string>();
        foreach (var item in root.GetProperty("blockingFactors").EnumerateArray())
        {
            blockingFactors.Add(item.GetString()!);
        }
        Assert.IsTrue(blockingFactors.Exists(f => f.Contains("bypassing a governance gate")));
    }
}
