using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.AI;
using IronDev.Core;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public class ContextAgentEvidenceTests
{
    [TestMethod]
    public async Task SoftArchive_ProvesImplementation_OnlyWhenAllElementsPresent()
    {
        // 1. Arrange with partial evidence (missing IsDeleted)
        var snippets = new List<CodeIndexEntry>
        {
            new() { FilePath = "TicketService.cs", SymbolName = "ArchiveTicketAsync", ChunkText = "public async Task ArchiveTicketAsync(int id) { var ticket = await _db.Tickets.FindAsync(id); if (ticket != null) { /* mark as archived somehow */ await _db.SaveChangesAsync(); } }" },
            new() { FilePath = "TicketService.cs", SymbolName = "GetRecentTicketsAsync", ChunkText = "public List<ProjectTicket> GetRecentTicketsAsync() { return _db.Tickets.OrderByDescending(t => t.CreatedAt).ToList(); }" }
        };

        const string insufficientJson = """{"isSufficient":false,"confidence":3,"reason":"Need code.","requestedContext":{"codeSearchQueries":["soft archive"],"clarificationQuestions":[]}}""";

        var (agent, llm, index, traces) = ContextAgentFactory.Build(snippets: snippets, llmResponses: new[] { insufficientJson });
        
        // 2. Act
        var result = await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId = 1,
            UserRequest = "Verify soft archive implementation"
        });

        // 3. Assert Proof Gate
        var proofTrace = traces.GetRecentTraces().FirstOrDefault(t => t.FeatureName == ContextAgentStage.EvidenceProofGate);
        Assert.IsNotNull(proofTrace);
        Assert.IsFalse(proofTrace.WasSuccessful, "Proof should fail because IsDeleted is missing.");
        Assert.IsTrue(proofTrace.RawResponseText.Contains("ProjectTicket.IsDeleted property"));

        // 4. Arrange with FULL evidence
        snippets.Add(new CodeIndexEntry { FilePath = "DataModels.cs", SymbolName = "IsDeleted", ChunkText = "public bool IsDeleted { get; set; }" });
        snippets[1].ChunkText = "public List<ProjectTicket> GetRecentTicketsAsync() { return _db.Tickets.Where(t => !t.IsDeleted).ToList(); }";

        (agent, llm, index, traces) = ContextAgentFactory.Build(snippets: snippets, llmResponses: new[] { insufficientJson });
        
        result = await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId = 1,
            UserRequest = "Verify soft archive implementation"
        });

        proofTrace = traces.GetRecentTraces().FirstOrDefault(t => t.FeatureName == ContextAgentStage.EvidenceProofGate);
        Assert.IsTrue(proofTrace.WasSuccessful, "Proof should pass with all elements.");
    }

    [TestMethod]
    public async Task AuthInspection_RetrievesAuthController_BeforeConflictService()
    {
        // 1. Arrange: index contains both AuthController and ContextConflictService
        var snippets = new List<CodeIndexEntry>
        {
            new() { FilePath = "ContextConflictService.cs", ChunkText = "public class ContextConflictService { /* implementation of conflict checking logic for tickets and decisions */ }" },
            new() { FilePath = "AuthController.cs", ChunkText = "public class AuthController : ControllerBase { [HttpPost(\"login\")] public async Task<IActionResult> Login([FromBody] LoginRequest req) { return Ok(new { Token = \"stub\" }); } }" }
        };

        const string insufficientJson = """{"isSufficient":false,"confidence":3,"reason":"Need auth code.","requestedContext":{"codeSearchQueries":["auth"],"clarificationQuestions":[]}}""";

        var (agent, llm, index, traces) = ContextAgentFactory.Build(snippets: snippets, llmResponses: new[] { insufficientJson });
        
        // 2. Act
        await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId = 1,
            UserRequest = "Inspect the current REST authentication flow"
        });

        // 3. Assert: AuthController is boosted, ConflictService is excluded
        var allTraces = traces.GetRecentTraces();
        var searchTraces = allTraces.Where(t => t.FeatureName == ContextAgentStage.ToolResultSearch).ToList();
        Assert.IsTrue(searchTraces.Count > 0, "Should have at least one search trace.");
        
        // Verify exclusion logic in Traces
        Assert.IsTrue(searchTraces.Any(t => t.RawResponseText.Contains("AuthController.cs")), "AuthController should be present in at least one retrieval trace.");
        Assert.IsFalse(searchTraces.Any(t => t.RawResponseText.Contains("ContextConflictService.cs")), "ContextConflictService should be excluded from all auth retrieval traces.");
    }

    [TestMethod]
    public async Task DeepLookup_SkipsNonSemanticSymbols()
    {
        // 1. Arrange: index returns a shallow snippet for "Ok" or "readonly"
        var snippets = new List<CodeIndexEntry>
        {
            new() { FilePath = "AuthController.cs", SymbolName = "Ok", ChunkText = "return Ok();" }, // shallow
            new() { FilePath = "AuthController.cs", SymbolName = "Login", ChunkText = "public Task Login() { /* implementation */ }" } // shallow but semantic
        };

        const string insufficientJson = """{"isSufficient":false,"confidence":3,"reason":"Need code.","requestedContext":{"codeSearchQueries":["login"],"clarificationQuestions":[]}}""";

        var (agent, llm, index, traces) = ContextAgentFactory.Build(snippets: snippets, llmResponses: new[] { insufficientJson });
        
        // 2. Act
        await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId = 1,
            UserRequest = "Inspect login"
        });

        // 3. Assert: Deep lookup did NOT run for "Ok" but DID run for "Login"
        var deepTraces = traces.GetRecentTraces().Where(t => t.FeatureName == ContextAgentStage.DeepCodeEvidence).ToList();
        Assert.IsFalse(deepTraces.Any(t => t.RequestText.Contains("SelectedSymbol: Ok")), "Deep lookup should skip non-semantic symbol 'Ok'.");
        Assert.IsTrue(deepTraces.Any(t => t.RequestText.Contains("SelectedSymbol: Login")), "Deep lookup should run for semantic symbol 'Login'.");
    }

    [TestMethod]
    public async Task InspectionRoutes_DoNotEmitDecisionTags()
    {
        // 1. Arrange
        var (agent, llm, index, traces) = ContextAgentFactory.Build();
        
        // 2. Act
        var result = await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId = 1,
            UserRequest = "Inspect soft archive"
        });

        // 3. Assert Final Prompt
        var finalTrace = traces.GetRecentTraces().FirstOrDefault(t => t.FeatureName == ContextAgentStage.FinalAnswer);
        Assert.IsNotNull(finalTrace);
        Assert.IsTrue(finalTrace.RequestText.Contains("DO NOT emit <decision> tags"), "Final prompt should forbid decision tags for inspection.");
    }

    [TestMethod]
    public async Task OAuthExistenceCheck_Negative_ReportsProvenAbsent()
    {
        // 1. Arrange: only JWT evidence exists
        var snippets = new List<CodeIndexEntry>
        {
            new() { FilePath = "AuthController.cs", ChunkText = "public class AuthController { [HttpPost(\"login\")] public IActionResult Login() { return Ok(new { token = \"jwt\" }); } }" }
        };

        const string insufficientJson = """{"isSufficient":false,"confidence":3,"reason":"Need code.","requestedContext":{"codeSearchQueries":["oauth"],"clarificationQuestions":[]}}""";
        var (agent, _, _, traces) = ContextAgentFactory.Build(snippets: snippets, llmResponses: new[] { insufficientJson });

        // 2. Act
        await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId = 1,
            UserRequest = "Does IronDev already support OAuth in the REST API?"
        });

        // 3. Assert Proof Gate: Status=ProvenAbsent
        var proofTrace = traces.GetRecentTraces().FirstOrDefault(t => t.FeatureName == ContextAgentStage.EvidenceProofGate);
        Assert.IsNotNull(proofTrace);
        Assert.IsTrue(proofTrace.RawResponseText.Contains("Status: ProvenAbsent"));
        Assert.IsTrue(proofTrace.RawResponseText.Contains("I found authentication logic (JWT), but no mentions of OAuth"));
    }

    [TestMethod]
    public async Task OAuthExistenceCheck_Partial_ReportsNotProven()
    {
        // 1. Arrange: mentions of OAuth but no implementation
        var snippets = new List<CodeIndexEntry>
        {
            new() { FilePath = "Config.cs", ChunkText = "public class Config { // TODO: implement oauth support }" }
        };

        const string insufficientJson = """{"isSufficient":false,"confidence":3,"reason":"Need code.","requestedContext":{"codeSearchQueries":["oauth"],"clarificationQuestions":[]}}""";
        var (agent, _, _, traces) = ContextAgentFactory.Build(snippets: snippets, llmResponses: new[] { insufficientJson });

        // 2. Act
        await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId = 1,
            UserRequest = "Does IronDev already support OAuth in the REST API?"
        });

        // 3. Assert Proof Gate: Status=NotProven
        var proofTrace = traces.GetRecentTraces().FirstOrDefault(t => t.FeatureName == ContextAgentStage.EvidenceProofGate);
        Assert.IsNotNull(proofTrace);
        Assert.IsTrue(proofTrace.RawResponseText.Contains("Status: NotProven"));
        Assert.IsTrue(proofTrace.RawResponseText.Contains("/action"), $"Missing element not found in trace. Got:\n{proofTrace.RawResponseText}");
        Assert.IsTrue(proofTrace.RawResponseText.Contains("/config"), "Missing element not found in trace.");
        Assert.IsTrue(proofTrace.RawResponseText.Contains("flow logic"), "Missing element not found in trace.");
    }

    [TestMethod]
    public async Task SoftArchiveVerification_ExcludesInternalConflictService()
    {
        // 1. Arrange: index contains both product and internal files
        var snippets = new List<CodeIndexEntry>
        {
            new() { FilePath = "TicketService.cs", ChunkText = "public void Archive(int id) { /* ... */ }" },
            new() { FilePath = "ContextConflictService.cs", ChunkText = "private static readonly string[] SoftArchiveTriggers = { \"soft archive\" };" }
        };

        const string insufficientJson = """{"isSufficient":false,"confidence":3,"reason":"Need code.","requestedContext":{"codeSearchQueries":["soft archive"],"clarificationQuestions":[]}}""";
        var (agent, _, _, traces) = ContextAgentFactory.Build(snippets: snippets, llmResponses: new[] { insufficientJson });

        // 2. Act
        await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId = 1,
            UserRequest = "Verify soft archive implementation"
        });

        // 3. Assert: ContextConflictService is excluded
        var allTraces = traces.GetRecentTraces();
        var searchTraces = allTraces.Where(t => t.FeatureName == ContextAgentStage.ToolResultSearch).ToList();
        Assert.IsTrue(searchTraces.Count > 0, "Should have at least one search trace.");
        
        Assert.IsFalse(searchTraces.Any(t => t.RawResponseText.Contains("ContextConflictService.cs")), "Internal conflict service should be excluded from all product verification traces.");
        Assert.IsTrue(searchTraces.Any(t => t.RawResponseText.Contains("TicketService.cs")), "TicketService.cs should be present in at least one retrieval trace.");
    }

    [TestMethod]
    public async Task SelfQuery_AllowsInternalFiles()
    {
        // 1. Arrange: user asks about the agent itself
        var snippets = new List<CodeIndexEntry>
        {
            new() { FilePath = "ContextAgentService.cs", ChunkText = "public class ContextAgentService { /* ... */ }" }
        };

        const string insufficientJson = """{"isSufficient":false,"confidence":3,"reason":"Need code.","requestedContext":{"codeSearchQueries":["ContextAgentService"],"clarificationQuestions":[]}}""";
        var (agent, _, _, traces) = ContextAgentFactory.Build(snippets: snippets, llmResponses: new[] { insufficientJson });

        // 2. Act
        await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId = 1,
            UserRequest = "How does the Context Agent routing work?"
        });

        // 3. Assert: ContextAgentService is allowed because it's a self-query
        var resultTrace = traces.GetRecentTraces().FirstOrDefault(t => t.FeatureName == ContextAgentStage.ToolResultSearch);
        Assert.IsNotNull(resultTrace);
        Assert.IsTrue(resultTrace.RawResponseText.Contains("ContextAgentService.cs"), "Internal file should be allowed for self-queries.");
    }

    [TestMethod]
    public async Task VerifyImplementation_PerformsDirectDeepLookup_ForKnownTargets()
    {
        // 1. Arrange: empty index snippets, but RouteJudge (stubbed) will identify targets
        var snippets = new List<CodeIndexEntry>(); 
        const string insufficientJson = """{"isSufficient":false,"confidence":3,"reason":"Need code.","requestedContext":{"codeSearchQueries":["soft archive"],"clarificationQuestions":[]}}""";
        
        var (agent, _, index, traces) = ContextAgentFactory.Build(snippets: snippets, llmResponses: new[] { insufficientJson });
        
        // Populate index with the full files so DeepLookupService can find them
        index.Files["TicketService.cs"] = "public class TicketService { public async Task ArchiveTicketAsync(int id) { var t = await _db.Tickets.FindAsync(id); t.IsDeleted = true; await _db.SaveChangesAsync(); } public List<ProjectTicket> GetRecentTicketsAsync() { return _db.Tickets.Where(t => !t.IsDeleted).ToList(); } }";
        index.Files["DataModels.cs"] = "public class ProjectTicket { public int Id { get; set; } public bool IsDeleted { get; set; } }";

        // 2. Act
        var result = await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId = 1,
            UserRequest = "Check whether ticket soft archive is implemented correctly"
        });

        // 3. Assert
        var allTraces = traces.GetRecentTraces();
        var targetTrace = allTraces.FirstOrDefault(t => t.FeatureName == ContextAgentStage.DirectDeepLookupTargets);
        Assert.IsNotNull(targetTrace, "DirectDeepLookupTargets trace should exist.");
        
        // Should have 3 direct deep lookups (ArchiveTicketAsync, GetRecentTicketsAsync, ProjectTicket)
        // Note: The trace raw response text contains the target details summary.
        Assert.IsTrue(targetTrace.RawResponseText.Contains("ArchiveTicketAsync"), "Should have direct lookup for ArchiveTicketAsync");
        Assert.IsTrue(targetTrace.RawResponseText.Contains("GetRecentTicketsAsync"), "Should have direct lookup for GetRecentTicketsAsync");
        Assert.IsTrue(targetTrace.RawResponseText.Contains("ProjectTicket"), "Should have direct lookup for ProjectTicket");

        var proofTrace = allTraces.FirstOrDefault(t => t.FeatureName == ContextAgentStage.EvidenceProofGate);
        Assert.IsNotNull(proofTrace);
        Assert.IsTrue(proofTrace.WasSuccessful, "Proof should pass because direct lookup found all implementation details.");
        Assert.IsTrue(proofTrace.RawResponseText.Contains("Status: ProvenPresent"));
    }
}
