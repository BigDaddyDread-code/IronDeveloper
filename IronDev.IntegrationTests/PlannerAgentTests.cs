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
public sealed class PlannerAgentTests
{
    private static AgentRequest CreateProductSpikeRequest(string prompt)
    {
        return new AgentRequest
        {
            AgentName = "PlannerAgent",
            GoalId = "test-goal",
            DogfoodRunId = "test-run",
            Inputs = new Dictionary<string, string>
            {
                ["mode"] = "product_spike_intake",
                ["prompt"] = prompt,
                ["live_llm"] = "false"
            }
        };
    }

    [TestMethod]
    [DataRow("Build Solitaire game.", "Solitaire")]
    [DataRow("Build Solitare helper.", "Solitare")] // Typo check is removed, Solitare is detected as capitalized candidate
    [DataRow("Build IronDev helper.", "IronDev")]
    [DataRow("Create BookSeller web app.", "BookSeller")]
    [DataRow("build something simple please.", "UnspecifiedProductSpike")]
    [DataRow("Please help build the Console app.", "Console")]
    public async Task PlannerAgent_DetectProjectName_Heuristics(string prompt, string expectedProject)
    {
        var definition = new AgentDefinition
        {
            Name = "PlannerAgent",
            Purpose = "Test project name detection.",
            DefaultModelProfile = "cheap-runner"
        };
        var resolver = new AgentModelResolver();
        var agent = new PlannerAgent(definition, resolver);

        var request = CreateProductSpikeRequest(prompt);
        var result = await agent.RunAsync(request);

        Assert.AreEqual(AgentRunStatus.Succeeded, result.Status);
        Assert.IsNotNull(result.OutputJson);

        using var doc = JsonDocument.Parse(result.OutputJson);
        var root = doc.RootElement;
        
        Assert.AreEqual(expectedProject, root.GetProperty("detectedProject").GetString());
    }
}
