using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Agent.Models;
using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.IntegrationTests;

[TestClass]
public class DraftPersistenceIntegrationTests : IntegrationTestBase
{
    [TestMethod]
    [Description("Verify that Save Ticket (ApproveDraftAsync) persists all draft fields to the real database.")]
    public async Task ApproveDraftAsync_PersistsAllExtendedFields()
    {
        // 1. Setup VM with real services (scoped from ServiceProvider)
        using var scope = ServiceProvider.CreateScope();
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();
        var memoryService = scope.ServiceProvider.GetRequiredService<IProjectMemoryService>();
        
        var vm = new TicketsWorkspaceViewModel(
            ticketService,
            memoryService,
            new StubOrchestrator(),
            new StubDraftTicketService(),
            null!);

        // 2. Setup project context
        var projectId = await SeedProjectAsync();
        // Use reflection to set private project fields
        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, projectId);
        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, "IronDev");

        // 3. Initiate draft
        var ctx = new ChatTicketContext
        {
            SessionId = 1,
            MessageId = 10,
            MessageText = "We need to fix the login button styling.",
            ProposedTitle = "Fix Login Button",
            LinkedFilePaths = "LoginView.xaml;LoginView.xaml.cs",
            LinkedSymbols = "LoginView"
        };
        await vm.BeginDraftFromChatAsync(ctx);

        // 4. Manually enrich the draft in the VM editor to verify full persistence
        vm.EditTestsUnitTests = "Check background color.";
        vm.EditTestsIntegrationTests = "Verify login still works.";
        vm.EditTestsManualTests = "Click button manually.";
        vm.EditTestsBuildValidation = "dotnet build";

        // 5. Save the ticket
        await vm.ApproveDraftCommand.ExecuteAsync(null);

        // 6. Verify persistence
        Assert.IsFalse(vm.IsDraftMode, "Should have exited draft mode.");
        StringAssert.Contains(vm.SaveStatus, "Ticket created");

        var tickets = await ticketService.GetRecentTicketsAsync(projectId, 1);
        var savedTicket = tickets.FirstOrDefault();

        Assert.IsNotNull(savedTicket);
        Assert.AreEqual("Fix Login Button", savedTicket.Title);
        Assert.AreEqual("Check background color.", savedTicket.UnitTests);
        Assert.AreEqual("Verify login still works.", savedTicket.IntegrationTests);
        Assert.AreEqual("dotnet build", savedTicket.BuildValidation);
        Assert.IsTrue(savedTicket.IsGenerated);
    }

    [TestMethod]
    [Description("Verify that Save Ticket + Plan persists both the ticket and the implementation plan.")]
    public async Task ApproveDraftWithPlanAsync_PersistsBothTicketAndPlan()
    {
        using var scope = ServiceProvider.CreateScope();
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();
        var memoryService = scope.ServiceProvider.GetRequiredService<IProjectMemoryService>();
        
        var vm = new TicketsWorkspaceViewModel(
            ticketService,
            memoryService,
            new StubOrchestrator(),
            new StubDraftTicketService(),
            null!);

        var projectId = await SeedProjectAsync();
        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, projectId);

        await vm.BeginDraftFromChatAsync(new ChatTicketContext 
        { 
            ProposedTitle = "Ticket with Plan", 
            MessageText = "Do A then B." 
        });

        // Ensure a plan is generated
        await vm.GenerateImplementationPlanCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.HasPlan);
        vm.PlanTitle = "My Custom Plan Title";

        // Save both
        await vm.ApproveDraftWithPlanCommand.ExecuteAsync(null);

        // Verify ticket
        var tickets = await ticketService.GetRecentTicketsAsync(projectId, 1);
        var savedTicket = tickets.FirstOrDefault();
        Assert.IsNotNull(savedTicket);

        // Verify linked plan
        var savedPlan = await memoryService.GetPlanByTicketIdAsync(savedTicket.Id);
        Assert.IsNotNull(savedPlan, "Linked implementation plan should have been persisted.");
        Assert.AreEqual("My Custom Plan Title", savedPlan.Title);
        Assert.AreEqual(savedTicket.Id, savedPlan.TicketId);
        Assert.AreEqual(projectId, savedPlan.ProjectId);
    }
}
