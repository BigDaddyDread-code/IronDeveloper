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
public class ContextAgentDeepEvidenceTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────

    private class StubCodeIndexService : IronDev.Services.ICodeIndexService
    {
        public List<CodeIndexEntry> Entries { get; set; } = new();
        public List<ProjectFile> Files { get; set; } = new();

        public Task<CodeIndexResult> IndexDirectoryAsync(int projectId, string directoryPath, CancellationToken cancellationToken = default) => Task.FromResult(new CodeIndexResult());
        public Task<IReadOnlyList<ProjectFile>> SearchFilesAsync(int projectId, string query, int take = 5, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectFile>>(new List<ProjectFile>());
        
        public Task<ProjectFile?> GetByPathAsync(int projectId, string filePath, CancellationToken cancellationToken = default)
        {
            var file = Files.FirstOrDefault(f => f.FilePath == filePath);
            return Task.FromResult(file);
        }

        public Task<IReadOnlyList<ProjectFile>> GetRecentFilesAsync(int projectId, int take = 20, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectFile>>(new List<ProjectFile>());
        public Task<IReadOnlyList<CodeIndexEntry>> GetSymbolsAsync(long fileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CodeIndexEntry>>(new List<CodeIndexEntry>());
        public Task<int> GetIndexedFileCountAsync(int projectId, CancellationToken cancellationToken = default) => Task.FromResult(Files.Count);
        
        public Task<IReadOnlyList<CodeIndexEntry>> GetRelevantSnippetsAsync(int projectId, string query, int take = 10, CancellationToken cancellationToken = default)
        {
            // Simple mock: return entries that match the query
            var result = Entries.Where(e => e.SymbolName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true || 
                                            e.ChunkText?.Contains(query, StringComparison.OrdinalIgnoreCase) == true).Take(take).ToList();
            return Task.FromResult<IReadOnlyList<CodeIndexEntry>>(result);
        }
    }

    private class StubLlmServiceForAgent : IronDev.Core.ILLMService
    {
        public string SufficiencyJson { get; set; } = "{ \"isSufficient\": false, \"confidence\": 5, \"reason\": \"Need more code.\", \"requestedContext\": { \"codeSearchQueries\": [\"ArchiveTicketAsync\"] } }";

        public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default)
        {
            if (prompt.Contains("You are a context quality evaluator"))
            {
                return Task.FromResult(SufficiencyJson);
            }
            return Task.FromResult("FINAL ANSWER");
        }
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("Test A & B: Shallow ArchiveTicketAsync snippet triggers deep lookup and returns method body.")]
    public async Task DeepLookup_ArchiveTicketAsync_ReturnsMethodBody()
    {
        var codeIndex = new StubCodeIndexService();
        codeIndex.Entries.Add(new CodeIndexEntry
        {
            FilePath = "TicketService.cs",
            SymbolName = "ArchiveTicketAsync",
            // Shallow snippet: just the method signature without body
            ChunkText = "public Task<bool> ArchiveTicketAsync(long id, CancellationToken ct = default);" 
        });
        codeIndex.Files.Add(new ProjectFile
        {
            FilePath = "TicketService.cs",
            Content = @"
using System;

public class TicketService {
    public Task<bool> ArchiveTicketAsync(long id, CancellationToken ct = default)
    {
        // DO NOT HARD DELETE
        var ticket = GetTicket(id);
        ticket.IsArchived = true;
        Save(ticket);
        return Task.FromResult(true);
    }
}
" + new string('/', 4000)
        });

        var traceService = new LlmTraceService();
        var agent = new ContextAgentService(
            new StubPromptContextBuilder(),
            codeIndex,
            new StubLlmServiceForAgent(),
            traceService,
            null);

        var request = new ContextAgentRequest { ProjectId = 1, UserRequest = "How does archive work?" };
        await agent.RunAsync(request);

        var deepTraces = traceService.GetRecentTraces().Where(t => t.FeatureName == ContextAgentStage.DeepCodeEvidence).ToList();
        
        // Assert G: Trace emitted
        Assert.AreEqual(1, deepTraces.Count, "Deep code trace should be emitted.");
        var deepTrace = deepTraces[0];
        
        // Assert A: Shallow snippet triggers deep lookup
        Assert.IsTrue(deepTrace.RequestText.Contains("Reason: Shallow snippet detected."));
        Assert.IsTrue(deepTrace.WasSuccessful);

        // Assert B: Deep lookup returns ArchiveTicketAsync method body
        Assert.IsTrue(deepTrace.RequestText.Contains("EvidenceType: MethodBody"));
        Assert.IsTrue(deepTrace.ParsedResponseSummary.Contains("MethodBody"));
        
        var finalTrace = traceService.GetRecentTraces().FirstOrDefault(t => t.FeatureName == ContextAgentStage.FinalAnswer);
        Assert.IsNotNull(finalTrace);
        Assert.IsTrue(finalTrace.RequestText.Contains("ticket.IsArchived = true;"), "Final prompt should contain the deep method body.");
    }

    [TestMethod]
    [Description("Test C: ProjectTicket shallow snippet triggers lookup and returns IsDeleted property when present.")]
    public async Task DeepLookup_ProjectTicket_ReturnsIsDeletedProperty()
    {
        var codeIndex = new StubCodeIndexService();
        codeIndex.Entries.Add(new CodeIndexEntry
        {
            FilePath = "ProjectTicket.cs",
            SymbolName = "ProjectTicket",
            // Shallow snippet: class header
            ChunkText = "public class ProjectTicket { public int Id { get; set; } }" 
        });
        codeIndex.Files.Add(new ProjectFile
        {
            FilePath = "ProjectTicket.cs",
            Content = @"
public class ProjectTicket {
    public int Id { get; set; }
    public string Title { get; set; }
    public bool IsDeleted { get; set; }
}
" + new string('/', 4000)
        });

        var traceService = new LlmTraceService();
        var agent = new ContextAgentService(new StubPromptContextBuilder(), codeIndex, new StubLlmServiceForAgent { SufficiencyJson = "{ \"isSufficient\": false, \"confidence\": 5, \"reason\": \"Need more code.\", \"requestedContext\": { \"codeSearchQueries\": [\"ProjectTicket\"] } }" }, traceService, null);

        await agent.RunAsync(new ContextAgentRequest { ProjectId = 1, UserRequest = "Does project ticket have isdeleted?" });

        var deepTrace = traceService.GetRecentTraces().FirstOrDefault(t => t.FeatureName == ContextAgentStage.DeepCodeEvidence);
        Assert.IsNotNull(deepTrace);
        Assert.IsTrue(deepTrace.WasSuccessful);
        Assert.IsTrue(deepTrace.RequestText.Contains("EvidenceType: PropertyDefinition"));

        var finalTrace = traceService.GetRecentTraces().FirstOrDefault(t => t.FeatureName == ContextAgentStage.FinalAnswer);
        Assert.IsNotNull(finalTrace);
        Assert.IsTrue(finalTrace.RequestText.Contains("public bool IsDeleted { get; set; }"));
    }

    [TestMethod]
    [Description("Test D: GetRecentTicketsAsync deep lookup includes IsDeleted filtering when present.")]
    public async Task DeepLookup_GetRecentTicketsAsync_IncludesFiltering()
    {
        var codeIndex = new StubCodeIndexService();
        codeIndex.Entries.Add(new CodeIndexEntry
        {
            FilePath = "TicketService.cs",
            SymbolName = "GetRecentTicketsAsync",
            ChunkText = "public Task<List<Ticket>> GetRecentTicketsAsync() => throw new Exception();"
        });
        codeIndex.Files.Add(new ProjectFile
        {
            FilePath = "TicketService.cs",
            Content = @"
public class TicketService {
    public Task<List<Ticket>> GetRecentTicketsAsync()
    {
        return db.Tickets.Where(t => !t.IsDeleted).ToList();
    }
}
"
        });

        var traceService = new LlmTraceService();
        var agent = new ContextAgentService(new StubPromptContextBuilder(), codeIndex, new StubLlmServiceForAgent { SufficiencyJson = "{ \"isSufficient\": false, \"confidence\": 5, \"reason\": \"Need more code.\", \"requestedContext\": { \"codeSearchQueries\": [\"GetRecentTicketsAsync\"] } }" }, traceService, null);

        await agent.RunAsync(new ContextAgentRequest { ProjectId = 1, UserRequest = "How are recent tickets fetched?" });

        var finalTrace = traceService.GetRecentTraces().FirstOrDefault(t => t.FeatureName == ContextAgentStage.FinalAnswer);
        Assert.IsNotNull(finalTrace);
        Assert.IsTrue(finalTrace.RequestText.Contains("!t.IsDeleted"));
    }

    [TestMethod]
    [Description("Test E: AuthController shallow constructor triggers lookup for relevant auth methods.")]
    public async Task DeepLookup_AuthController_ReturnsAuthMethods()
    {
        var codeIndex = new StubCodeIndexService();
        codeIndex.Entries.Add(new CodeIndexEntry
        {
            FilePath = "AuthController.cs",
            SymbolName = "AuthController",
            ChunkText = "public class AuthController { public AuthController() { } }"
        });
        codeIndex.Files.Add(new ProjectFile
        {
            FilePath = "AuthController.cs",
            Content = @"
public class AuthController {
    public AuthController() { }
    
    public IActionResult Login(string username)
    {
        return Ok(new { token = ""abc"" });
    }
}
" + new string('/', 4000)
        });

        var traceService = new LlmTraceService();
        var agent = new ContextAgentService(new StubPromptContextBuilder(), codeIndex, new StubLlmServiceForAgent { SufficiencyJson = "{ \"isSufficient\": false, \"confidence\": 5, \"reason\": \"Need more code.\", \"requestedContext\": { \"codeSearchQueries\": [\"AuthController\"] } }" }, traceService, null);

        await agent.RunAsync(new ContextAgentRequest { ProjectId = 1, UserRequest = "inspect auth" });

        var deepTrace = traceService.GetRecentTraces().FirstOrDefault(t => t.FeatureName == ContextAgentStage.DeepCodeEvidence);
        Assert.IsNotNull(deepTrace);
        Assert.IsTrue(deepTrace.RequestText.Contains("EvidenceType: SymbolBody"));

        var finalTrace = traceService.GetRecentTraces().FirstOrDefault(t => t.FeatureName == ContextAgentStage.FinalAnswer);
        Assert.IsNotNull(finalTrace);
        Assert.IsTrue(finalTrace.RequestText.Contains("Login"));
    }

    [TestMethod]
    [Description("Test F: Deep lookup respects max lookup count and max chars.")]
    public async Task DeepLookup_RespectsLimits()
    {
        var codeIndex = new StubCodeIndexService();
        for (int i = 0; i < 5; i++)
        {
            codeIndex.Entries.Add(new CodeIndexEntry
            {
                FilePath = $"File{i}.cs",
                SymbolName = $"Method{i}",
                ChunkText = $"void Method{i}();"
            });
            codeIndex.Files.Add(new ProjectFile
            {
                FilePath = $"File{i}.cs",
                Content = $"void Method{i}() {{ /* body */ }}"
            });
        }

        var traceService = new LlmTraceService();
        var agent = new ContextAgentService(new StubPromptContextBuilder(), codeIndex, new StubLlmServiceForAgent { SufficiencyJson = "{ \"isSufficient\": false, \"confidence\": 5, \"reason\": \"Need more code.\", \"requestedContext\": { \"codeSearchQueries\": [\"Method\"] } }" }, traceService, null);

        var limits = new ContextAgentLimits { MaxCodeSearchQueries = 1, MaxSnippets = 5, MaxToolCallsPerRound = 1 };
        await agent.RunAsync(new ContextAgentRequest { ProjectId = 1, UserRequest = "inspect Method", Limits = limits });

        var deepTraces = traceService.GetRecentTraces().Where(t => t.FeatureName == ContextAgentStage.DeepCodeEvidence).ToList();
        
        // Assert F: Max 3 deep lookups despite 5 shallow snippets
        Assert.AreEqual(3, deepTraces.Count);
    }

    [TestMethod]
    [Description("Test H: If deep lookup fails, final answer remains honest.")]
    public async Task DeepLookup_Fails_AgentRemainsHonest()
    {
        var codeIndex = new StubCodeIndexService();
        codeIndex.Entries.Add(new CodeIndexEntry
        {
            FilePath = "MissingFile.cs",
            SymbolName = "MissingMethod",
            ChunkText = "void MissingMethod();"
        });
        // File not added to Files list => GetByPathAsync returns null => Deep lookup fails

        var traceService = new LlmTraceService();
        var agent = new ContextAgentService(new StubPromptContextBuilder(), codeIndex, new StubLlmServiceForAgent { SufficiencyJson = "{ \"isSufficient\": false, \"confidence\": 5, \"reason\": \"Need more code.\", \"requestedContext\": { \"codeSearchQueries\": [\"MissingMethod\"] } }" }, traceService, null);

        await agent.RunAsync(new ContextAgentRequest { ProjectId = 1, UserRequest = "inspect MissingMethod" });

        var finalTrace = traceService.GetRecentTraces().FirstOrDefault(t => t.FeatureName == ContextAgentStage.FinalAnswer);
        Assert.IsNotNull(finalTrace);
        
        // Assert H: Grounding rule injected
        Assert.IsTrue(finalTrace.RequestText.Contains("GROUNDING RULE: Deep lookup failed"));
        Assert.IsTrue(finalTrace.RequestText.Contains("I found the likely file/symbol, but the indexed/deep evidence still does not prove the implementation details"));
    }
}
