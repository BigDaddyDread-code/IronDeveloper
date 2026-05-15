using System;
using System.Linq;
using System.Threading.Tasks;
using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Core.Builder;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public class BuilderReconciliationTests : IntegrationTestBase
{
    [TestMethod]
    public async Task HandleReconciliationAction_UpdateProfileAndDecision()
    {
        // 1. Setup Test DB Project
        var projectId = await SeedProjectAsync(1, "ReconciliationTest");

        var profileSvc = ServiceProvider.GetRequiredService<IronDev.Core.Interfaces.IProjectProfileService>();
        var memSvc = ServiceProvider.GetRequiredService<IronDev.Services.IProjectMemoryService>();
        
        var vm = new BuilderWorkspaceViewModel(
            ServiceProvider.GetRequiredService<IronDev.Core.Interfaces.IBuilderProposalService>(),
            ServiceProvider.GetRequiredService<IronDev.Core.Interfaces.ILlmTraceService>(),
            profileSvc,
            memSvc,
            ServiceProvider.GetRequiredService<IronDev.Core.Interfaces.IBuilderReadinessService>(),
            ServiceProvider.GetRequiredService<IronDev.Services.IProjectService>(),
            ServiceProvider.GetRequiredService<IronDev.Services.ITicketService>());

        vm.CurrentProposal = new BuilderProposal { ProjectId = projectId, TicketId = 1 };
        vm.Reconciliation = new BuildArchitectureReconciliation();
        vm.HasReconciliation = true;

        // Act
        var action = new ReconciliationAction { ActionType = "AddPackage_xUnit" };
        await vm.HandleReconciliationActionCommand.ExecuteAsync(action);

        // Assert Profile Updated
        var profile = await profileSvc.GetProjectProfileAsync(projectId);
        Assert.IsNotNull(profile);
        Assert.AreEqual("xUnit", profile.TestFramework);

        // Assert Decision Saved
        var decisions = await memSvc.GetRecentDecisionsAsync(projectId);
        Assert.IsTrue(decisions.Any(d => d.Title == "Test Framework: xUnit" && d.Status == "Approved"));
    }
    
    [TestMethod]
    public void BuilderPrompt_IncludesDecisions()
    {
        var ctx = new TicketBuildContext
        {
            ProjectName = "TestProject",
            TicketTitle = "Test Ticket",
            Decisions = new[] { "Test Framework: xUnit - Use xUnit for unit tests." }
        };

        var prompt = CodeChangeProposalService.BuildPrompt(ctx);
        
        Assert.IsTrue(prompt.Contains("PROJECT ARCHITECTURE DECISIONS:"));
        Assert.IsTrue(prompt.Contains("Test Framework: xUnit - Use xUnit for unit tests."));
    }
}
