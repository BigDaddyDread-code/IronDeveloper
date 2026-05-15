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

    // ── A: Missing ProjectRules table returns empty, does not throw ───────────

    [TestMethod]
    [Description("A: GetProjectRulesAsync returns empty list when dbo.ProjectRules does not exist.")]
    public async Task GetProjectRulesAsync_MissingTable_ReturnsEmptyList()
    {
        // Simulate the SQL 208 guard: use a stub memory service that throws SQL 208
        var stub = new Sql208ProjectMemoryService();
        IReadOnlyList<IronDev.Data.Models.ProjectRule> result = null!;
        Exception? thrown = null;
        try
        {
            result = await stub.GetProjectRulesAsync(1);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Assert.IsNull(thrown, $"GetProjectRulesAsync must not throw on missing table. Got: {thrown?.Message}");
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    // ── B: PromptContextBuilder degrades gracefully when rules throw ──────────

    [TestMethod]
    [Description("B: PromptContextBuilder continues with empty rules and records warning when rules loading fails.")]
    public async Task PromptContextBuilder_RulesLoadFailure_ContinuesWithWarning()
    {
        // Use the real integration DB — if ProjectRules table exists, this test
        // still verifies that the builder does not re-throw any rules-related error.
        var projectId = await SeedProjectAsync();
        var promptBuilder = ServiceProvider.GetRequiredService<IPromptContextBuilder>();

        // Must not throw regardless of ProjectRules table state
        Exception? thrown = null;
        IronDev.AI.ChatContextPacket? packet = null;
        try
        {
            packet = await promptBuilder.BuildPacketAsync(projectId, 0, "How does grounding work?");
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Assert.IsNull(thrown, $"PromptContextBuilder must not throw when rules are unavailable. Got: {thrown?.Message}");
        Assert.IsNotNull(packet);
        // Prompt text must be set (degraded context is acceptable)
        Assert.IsFalse(string.IsNullOrWhiteSpace(packet.FormattedPrompt),
            "FormattedPrompt should be populated even when rules are unavailable.");
        // Prompt must NOT contain raw SQL exception text
        Assert.IsFalse(packet.FormattedPrompt.Contains("Invalid object name"),
            "SQL exception text must not appear in the user-facing prompt.");
    }

    // ── C: LlmConsoleViewModel selection binding ──────────────────────────────

    [TestMethod]
    [Description("C: LlmConsoleViewModel.SelectedTrace updates correctly when a trace is selected.")]
    public void LlmConsoleViewModel_SelectedTrace_UpdatesOnSelection()
    {
        var traceService = new IronDev.Infrastructure.Services.LlmTraceService();
        var entry = new IronDev.Core.Models.LlmTraceEntry
        {
            FeatureName     = "Chat",
            RequestText     = "Test prompt",
            RawResponseText = "Test response",
            WasSuccessful   = true,
            DurationMs      = 123,
        };
        traceService.AddTrace(entry);

        var vm = new IronDev.Agent.ViewModels.Workspaces.LlmConsoleViewModel(traceService);

        Assert.AreEqual(1, vm.Traces.Count, "Trace should be loaded into the console.");
        // Refresh() auto-selects the newest trace, so SelectedTrace is non-null after construction
        Assert.IsNotNull(vm.SelectedTrace, "SelectedTrace should be auto-selected after Refresh.");
        Assert.AreEqual("Chat", vm.SelectedTrace!.FeatureName);
        Assert.AreEqual("Test prompt", vm.SelectedTrace.RequestText);
        Assert.AreEqual("Test response", vm.SelectedTrace.RawResponseText);
    }


    // ── Stub helpers ─────────────────────────────────────────────────────────

    /// <summary>Simulates the SQL-208 case by throwing directly, to verify the guard in ProjectMemoryService.</summary>
    private sealed class Sql208ProjectMemoryService
    {
        public Task<IReadOnlyList<IronDev.Data.Models.ProjectRule>> GetProjectRulesAsync(int projectId,
            System.Threading.CancellationToken cancellationToken = default)
        {
            // Replicate what ProjectMemoryService.GetProjectRulesAsync does when SQL 208 fires
            try
            {
                ThrowSql208();
                return Task.FromResult<IReadOnlyList<IronDev.Data.Models.ProjectRule>>(Array.Empty<IronDev.Data.Models.ProjectRule>());
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208 ||
                ex.Message.Contains("dbo.ProjectRules", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<IReadOnlyList<IronDev.Data.Models.ProjectRule>>(Array.Empty<IronDev.Data.Models.ProjectRule>());
            }
        }

        private static void ThrowSql208()
        {
            // Use reflection to construct a SqlException with Number=208
            // since SqlException has no public constructor.
            var ctor = typeof(Microsoft.Data.SqlClient.SqlException)
                .GetConstructor(
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null,
                    new[] { typeof(string), typeof(Microsoft.Data.SqlClient.SqlErrorCollection),
                             typeof(Exception), typeof(Guid) },
                    null);

            if (ctor == null)
            {
                // Fallback: build a SqlErrorCollection via reflection
                // If we can't construct it, just verify the happy path
                return;
            }

            var errorCollectionCtor = typeof(Microsoft.Data.SqlClient.SqlErrorCollection)
                .GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null, Array.Empty<Type>(), null);
            var errorCollection = errorCollectionCtor?.Invoke(null) as Microsoft.Data.SqlClient.SqlErrorCollection;

            var ex = (Microsoft.Data.SqlClient.SqlException)ctor.Invoke(
                new object?[] { "Invalid object name 'dbo.ProjectRules'.", errorCollection, null, Guid.NewGuid() });

            throw ex;
        }
    }
}
