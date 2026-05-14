using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public class ContextAgentConflictGatingTests
{
    private class StubCodeIndexService : IronDev.Services.ICodeIndexService
    {
        public Task<CodeIndexResult> IndexDirectoryAsync(int projectId, string directoryPath, CancellationToken cancellationToken = default) => Task.FromResult(new CodeIndexResult());
        public Task<IReadOnlyList<ProjectFile>> SearchFilesAsync(int projectId, string query, int take = 5, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectFile>>(new List<ProjectFile>());
        public Task<ProjectFile?> GetByPathAsync(int projectId, string filePath, CancellationToken cancellationToken = default) => Task.FromResult<ProjectFile?>(null);
        public Task<IReadOnlyList<ProjectFile>> GetRecentFilesAsync(int projectId, int take = 20, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectFile>>(new List<ProjectFile>());
        public Task<IReadOnlyList<CodeIndexEntry>> GetSymbolsAsync(long fileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CodeIndexEntry>>(new List<CodeIndexEntry>());
        public Task<int> GetIndexedFileCountAsync(int projectId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<IReadOnlyList<CodeIndexEntry>> GetRelevantSnippetsAsync(int projectId, string query, int take = 10, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CodeIndexEntry>>(new List<CodeIndexEntry>());
    }

    private class StubLlmServiceForAgent : IronDev.Core.ILLMService
    {
        public string SufficiencyJson { get; set; } = "{ \"isSufficient\": true, \"confidence\": 5, \"reason\": \"All good\", \"requestedContext\": { \"codeSearchQueries\": [] } }";

        public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default)
        {
            if (prompt.Contains("You are a context quality evaluator"))
            {
                return Task.FromResult(SufficiencyJson);
            }
            return Task.FromResult("FINAL ANSWER");
        }
    }

    [TestMethod]
    [Description("Test A & E: Soft archive inspection does not trigger conflict blocking.")]
    public async Task Inspection_SoftArchive_DoesNotTriggerConflict()
    {
        var existingTicket = new ProjectTicket
        {
            Id = 22,
            Title = "Implement Soft Delete for ProjectTickets",
            Status = "Done",
            Summary = "Adds IsDeleted and updates TicketService.ArchiveTicketAsync"
        };

        var traceService = new LlmTraceService();
        var agent = new ContextAgentService(
            new StubPromptContextBuilder(),
            new StubCodeIndexService(),
            new StubLlmServiceForAgent(),
            traceService,
            new ContextConflictService());

        var request = new ContextAgentRequest 
        { 
            ProjectId = 1, 
            UserRequest = "Check whether ticket soft archive is implemented correctly. Look for ArchiveTicketAsync, ProjectTicket IsDeleted, and GetRecentTicketsAsync filtering.",
            RecentTickets = [existingTicket]
        };

        var result = await agent.RunAsync(request);

        // A. No clarification required
        Assert.IsFalse(result.IsClarificationRequired, "Inspection should not trigger conflict clarification.");
        Assert.IsTrue(result.WasSuccessful);
        Assert.IsNull(result.ConflictAssessment);

        // F. Trace clarity
        var allTraces = traceService.GetRecentTraces();
        var skipTrace = allTraces.FirstOrDefault(t => t.FeatureName == ContextAgentStage.ConflictAssessment);
        Assert.IsNotNull(skipTrace, "Should emit skipped conflict assessment trace");
        Assert.IsTrue(skipTrace.ParsedResponseSummary.Contains("skipped"));
    }

    [TestMethod]
    [Description("Test B: REST auth inspection does not conflict with Controls Isolation.")]
    public async Task Inspection_RestAuth_DoesNotConflictWithControlsIsolation()
    {
        var existingDecision = new ProjectDecision
        {
            Id = 1,
            Title = "Controls Isolation",
            Detail = "Keep all IronDeveloperControls completely separate from core models."
        };

        var traceService = new LlmTraceService();
        var agent = new ContextAgentService(
            new StubPromptContextBuilder(),
            new StubCodeIndexService(),
            new StubLlmServiceForAgent(),
            traceService,
            new ContextConflictService());

        var request = new ContextAgentRequest 
        { 
            ProjectId = 1, 
            UserRequest = "Inspect the current REST authentication flow. What does AuthController actually do?",
            RecentDecisions = [existingDecision]
        };

        var result = await agent.RunAsync(request);

        Assert.IsFalse(result.IsClarificationRequired, "Inspection should not trigger conflict clarification.");
        Assert.IsNull(result.ConflictAssessment);
        
        var allTraces = traceService.GetRecentTraces();
        var skipTrace = allTraces.FirstOrDefault(t => t.FeatureName == ContextAgentStage.ConflictAssessment);
        Assert.IsTrue(skipTrace.ParsedResponseSummary.Contains("skipped"));
    }

    [TestMethod]
    [Description("Test C: Create ticket still triggers conflict.")]
    public async Task Change_CreateTicket_TriggersConflict()
    {
        var existingTicket = new ProjectTicket
        {
            Id = 500,
            Title = "Implement OAuth in REST layer",
            Status = "Draft",
            Summary = "OAuth authentication."
        };

        var traceService = new LlmTraceService();
        var agent = new ContextAgentService(
            new StubPromptContextBuilder(),
            new StubCodeIndexService(),
            new StubLlmServiceForAgent(),
            traceService,
            new ContextConflictService());

        var request = new ContextAgentRequest 
        { 
            ProjectId = 1, 
            UserRequest = "Create a ticket to add API key authentication to the REST layer",
            CreateTicketIntent = new CreateTicketIntent { Intent = "CreateTicket" }, // Explicit intent from ChatIntentParser
            RecentTickets = [existingTicket]
        };

        var result = await agent.RunAsync(request);

        Assert.IsTrue(result.IsClarificationRequired, "Ticket creation should trigger conflict clarification.");
        Assert.IsNotNull(result.ConflictAssessment);
        Assert.IsTrue(result.ConflictAssessment.BlocksTicketCreation);
    }

    [TestMethod]
    [Description("Test D: Implement/change still triggers conflict without explicit ticket intent.")]
    public async Task Change_ReplaceApproach_TriggersConflict()
    {
        var existingTicket = new ProjectTicket
        {
            Id = 500,
            Title = "Implement OAuth in REST layer",
            Status = "Draft",
            Summary = "OAuth authentication."
        };

        var traceService = new LlmTraceService();
        var agent = new ContextAgentService(
            new StubPromptContextBuilder(),
            new StubCodeIndexService(),
            new StubLlmServiceForAgent(),
            traceService,
            new ContextConflictService());

        var request = new ContextAgentRequest 
        { 
            ProjectId = 1, 
            UserRequest = "Replace OAuth with API key authentication.",
            // Intentionally omit CreateTicketIntent
            RecentTickets = [existingTicket]
        };

        var result = await agent.RunAsync(request);

        Assert.IsTrue(result.IsClarificationRequired, "Change command should trigger conflict clarification.");
        Assert.IsNotNull(result.ConflictAssessment);
    }
}
