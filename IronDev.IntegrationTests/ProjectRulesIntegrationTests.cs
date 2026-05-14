using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IronDev.AI;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public class ProjectRulesIntegrationTests : IntegrationTestBase
{
    [TestMethod]
    [Description("ProjectRules can be saved and retrieved from the database.")]
    public async Task SaveAndGetProjectRules_Works()
    {
        var projectId = await SeedProjectAsync();
        var memoryService = ServiceProvider.GetRequiredService<IProjectMemoryService>();

        var rule = new ProjectRule
        {
            ProjectId = projectId,
            Name = "Integration Test Rule",
            Type = "CodeStandard",
            Description = "Rule for integration testing.",
            EnforcementLevel = "Required",
            AppliesTo = "Both",
            ValidationHint = "Check for tests."
        };

        var ruleId = await memoryService.SaveProjectRuleAsync(rule);
        Assert.IsTrue(ruleId > 0);

        var rules = await memoryService.GetProjectRulesAsync(projectId);
        Assert.AreEqual(1, rules.Count);
        Assert.AreEqual("Integration Test Rule", rules[0].Name);
        Assert.AreEqual("Required", rules[0].EnforcementLevel);
    }

    [TestMethod]
    [Description("PromptContextBuilder injects relevant rules into the prompt.")]
    public async Task BuildAsync_IncludesProjectRulesInPrompt()
    {
        var projectId = await SeedProjectAsync();
        var memoryService = ServiceProvider.GetRequiredService<IProjectMemoryService>();

        await memoryService.SaveProjectRuleAsync(new ProjectRule
        {
            ProjectId = projectId,
            Name = "Strict MVVM",
            Type = "CodeStandard",
            Description = "All ViewModels must be in ViewModels/ folder.",
            EnforcementLevel = "Blocking",
            AppliesTo = "Both"
        });

        await memoryService.SaveProjectRuleAsync(new ProjectRule
        {
            ProjectId = projectId,
            Name = "Build Only Rule",
            Type = "WorkflowRule",
            Description = "Only applies to build.",
            EnforcementLevel = "Advisory",
            AppliesTo = "Build"
        });

        var promptBuilder = ServiceProvider.GetRequiredService<IPromptContextBuilder>();
        
        // General query should include "Both" rules
        var prompt = await promptBuilder.BuildAsync(projectId, 0, "What is the status of my project?", cancellationToken: default);
        
        StringAssert.Contains(prompt, "Strict MVVM");
        Assert.IsFalse(prompt.Contains("Build Only Rule"), "Build-only rule should not be in general chat prompt.");
    }

    [TestMethod]
    [Description("BuilderContextService assembles rules for the build workflow.")]
    public async Task AssembleContextAsync_IncludesBuildRules()
    {
        var projectId = await SeedProjectAsync();
        var memoryService = ServiceProvider.GetRequiredService<IProjectMemoryService>();
        var ticketService = ServiceProvider.GetRequiredService<ITicketService>();

        var ticketId = await ticketService.SaveTicketAsync(new ProjectTicket
        {
            ProjectId = projectId,
            Title = "Fix Build",
            Summary = "Build is broken",
            Status = "Draft",
            Priority = "High",
            TicketType = "Bug",
            SessionId = Guid.NewGuid()
        });

        await memoryService.SaveProjectRuleAsync(new ProjectRule
        {
            ProjectId = projectId,
            Name = "SQL Truth",
            Type = "ArchitectureDecision",
            Description = "Use SQL Server.",
            EnforcementLevel = "Required",
            AppliesTo = "Both"
        });

        await memoryService.SaveProjectRuleAsync(new ProjectRule
        {
            ProjectId = projectId,
            Name = "Build Specific",
            Type = "CodeStandard",
            Description = "No unused usings.",
            EnforcementLevel = "Blocking",
            AppliesTo = "Build"
        });

        await memoryService.SaveProjectRuleAsync(new ProjectRule
        {
            ProjectId = projectId,
            Name = "Ticket Only",
            Type = "WorkflowRule",
            Description = "Labels required.",
            EnforcementLevel = "Advisory",
            AppliesTo = "Ticket"
        });

        var contextService = ServiceProvider.GetRequiredService<IBuilderContextService>();
        var ctx = await contextService.AssembleContextAsync(projectId, ticketId);

        Assert.AreEqual(2, ctx.Standards.Count);
        Assert.IsTrue(ctx.Standards.Any(r => r.Name == "SQL Truth"));
        Assert.IsTrue(ctx.Standards.Any(r => r.Name == "Build Specific"));
        Assert.IsFalse(ctx.Standards.Any(r => r.Name == "Ticket Only"), "Ticket-only rule should not be in build context.");
        
        // Sorting: Blocking > Required
        Assert.AreEqual("Build Specific", ctx.Standards[0].Name);
        Assert.AreEqual("SQL Truth", ctx.Standards[1].Name);
    }
}
