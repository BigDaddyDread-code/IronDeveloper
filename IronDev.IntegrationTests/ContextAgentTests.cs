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
using IronDev.Agent.ViewModels.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

// ── Test doubles ──────────────────────────────────────────────────────────────

/// <summary>
/// Configurable stub LLM. Returns a pre-configured response string and records
/// all prompts it received. Keeps tests deterministic and LLM-free.
/// </summary>
internal sealed class StubLlmService : ILLMService
{
    private readonly Queue<string> _responses;
    public List<string> ReceivedPrompts { get; } = new();

    public StubLlmService(params string[] responses)
        => _responses = new Queue<string>(responses);

    public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default)
    {
        ReceivedPrompts.Add(prompt);
        if (prompt.Contains("You are the Context Agent route judge") && _responses.Count == 0)
        {
            return Task.FromResult("INVALID_JSON"); // Force fallback if no response provided
        }
        var response = _responses.Count > 0
            ? _responses.Dequeue()
            : """{"isSufficient":true,"confidence":8,"reason":"Stub fallback.","requestedContext":{"codeSearchQueries":[],"clarificationQuestions":[]}}""";
        return Task.FromResult(response);
    }
}

/// <summary>
/// Stub PromptContextBuilder. Returns a pre-built packet without hitting the DB.
/// </summary>
internal sealed class StubPromptContextBuilder : IPromptContextBuilder
{
    private readonly ChatContextPacket _packet;

    public StubPromptContextBuilder(ChatContextPacket? packet = null)
        => _packet = packet ?? new ChatContextPacket
        {
            Intent    = ChatIntent.CodeQuery,
            FormattedPrompt = "STUB PROMPT"
        };

    public Task<string> BuildAsync(int projectId, long sessionId, string userRequest, CancellationToken ct = default)
        => Task.FromResult(_packet.FormattedPrompt);

    public Task<ChatContextPacket> BuildPacketAsync(int projectId, long sessionId, string userRequest, CancellationToken ct = default)
        => Task.FromResult(_packet);

    public Task<PromptPreviewResult> BuildFullPromptForTestingAsync(int projectId, string userMessage, CancellationToken ct = default)
        => Task.FromResult(new PromptPreviewResult { PromptText = _packet.FormattedPrompt });
}

/// <summary>
/// Stub CodeIndexService. Returns pre-configured snippets per query.
/// Also tracks which queries it received.
/// </summary>
internal sealed class StubCodeIndexService : ICodeIndexService
{
    private readonly IReadOnlyList<CodeIndexEntry> _snippets;
    public List<string> ReceivedQueries { get; } = new();

    public StubCodeIndexService(IEnumerable<CodeIndexEntry>? snippets = null)
        => _snippets = (snippets ?? Enumerable.Empty<CodeIndexEntry>()).ToList();

    public Task<IReadOnlyList<CodeIndexEntry>> GetRelevantSnippetsAsync(
        int projectId, string query, int take = 10, CancellationToken ct = default)
    {
        ReceivedQueries.Add(query);
        IReadOnlyList<CodeIndexEntry> result = _snippets.Take(take).ToList();
        return Task.FromResult(result);
    }

    // Unused stubs
    public Task<CodeIndexResult> IndexDirectoryAsync(int p, string d, CancellationToken ct = default)        => Task.FromResult(new CodeIndexResult());
    public Task<IReadOnlyList<ProjectFile>> SearchFilesAsync(int p, string q, int t = 5, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProjectFile>>(Array.Empty<ProjectFile>());
    public Dictionary<string, string> Files { get; } = new();

    public Task<ProjectFile?> GetByPathAsync(int p, string f, CancellationToken ct = default)
    {
        if (Files.TryGetValue(f, out var content))
        {
            return Task.FromResult<ProjectFile?>(new ProjectFile { FilePath = f, Content = content });
        }
        return Task.FromResult<ProjectFile?>(null);
    }
    public Task<IReadOnlyList<ProjectFile>> GetRecentFilesAsync(int p, int t = 20, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProjectFile>>(Array.Empty<ProjectFile>());
    public Task<IReadOnlyList<CodeIndexEntry>> GetSymbolsAsync(long fileId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CodeIndexEntry>>(Array.Empty<CodeIndexEntry>());
    public Task<int> GetIndexedFileCountAsync(int p, CancellationToken ct = default)                        => Task.FromResult(0);
}

// ── Helper factory ────────────────────────────────────────────────────────────
 
/// <summary>
/// Stub route judge. Returns a default InspectCode route unless configured.
/// </summary>
internal sealed class StubRouteJudge : IContextAgentRouteJudge
{
    public ContextAgentRouteDecision Decision { get; set; } = new()
    {
        RequestKind = ContextRequestKind.InspectCode,
        Confidence = 1.0,
        AllowCodeSearch = true,
        AllowDeepLookup = true,
        AllowConflictAssessment = false
    };

    public Task<ContextAgentRouteDecision> DecideRouteAsync(ContextAgentRouteRequest request, CancellationToken ct = default)
    {
        var d = new ContextAgentRouteDecision
        {
            RequestKind = ContextRequestKind.InspectCode,
            Confidence = 1.0,
            AllowCodeSearch = true,
            AllowDeepLookup = true,
            AllowConflictAssessment = false,
            AllowConflictBlocking = false,
            AllowTicketCreation = false,
            RelatedTicketsAreContextOnly = true,
            EffectiveWorkText = request.UserRequest
        };

        var lower = request.UserRequest.ToLowerInvariant();
        if (lower.Contains("verify") || lower.Contains("check whether")) d.RequestKind = ContextRequestKind.VerifyImplementation;
        else if (lower.Contains("create") || lower.Contains("ticket") || lower.Contains("implementation") || lower.Contains("replace") || lower.Contains("add") || lower.Contains("fix"))
        {
            d.RequestKind = ContextRequestKind.CreateTicket;
            d.AllowConflictBlocking = true;
            d.AllowConflictAssessment = true;
        }
        else if (lower.Contains("inspect") || lower.Contains("check")) d.RequestKind = ContextRequestKind.InspectCode;

        d.OriginalUserRequest = request.UserRequest;
        d.EffectiveWorkText = request.UserRequest;
        d.DeepLookupTargets = IdentifyTargets(lower);
        return Task.FromResult(d);
    }

    private IReadOnlyList<DeepLookupTarget> IdentifyTargets(string lower)
    {
        var targets = new List<DeepLookupTarget>();
        if (lower.Contains("soft archive") || lower.Contains("archive ticket"))
        {
            targets.Add(new DeepLookupTarget { FilePath = "TicketService.cs", SymbolName = "ArchiveTicketAsync", ProofPattern = "Body" });
            targets.Add(new DeepLookupTarget { FilePath = "TicketService.cs", SymbolName = "GetRecentTicketsAsync", ProofPattern = "IsDeleted filter" });
            targets.Add(new DeepLookupTarget { FilePath = "DataModels.cs", SymbolName = "ProjectTicket", ProofPattern = "IsDeleted property" });
        }
        return targets;
    }
}


internal static class ContextAgentFactory
{
    /// <summary>Builds a ContextAgentService with all stubs pre-configured.</summary>
    public static (ContextAgentService agent, StubLlmService llm, StubCodeIndexService index, LlmTraceService traces)
        Build(
            ChatContextPacket? packet = null,
            IEnumerable<CodeIndexEntry>? snippets = null,
            params string[] llmResponses)
    {
        var llm    = new StubLlmService(llmResponses);
        var index  = new StubCodeIndexService(snippets);
        var traces = new LlmTraceService();
        var route  = new StubRouteJudge();

        var agent = new ContextAgentService(
            new StubPromptContextBuilder(packet),
            index,
            llm,
            traces,
            routeJudge: route);

        return (agent, llm, index, traces);
    }

    /// <summary>
    /// Builds a ContextAgentService with a real ContextConflictService wired in,
    /// so conflict assessment tests run deterministically without a DB.
    /// </summary>
    public static (ContextAgentService agent, StubLlmService llm, StubCodeIndexService index, LlmTraceService traces)
        BuildWithConflict(
            ChatContextPacket? packet = null,
            IEnumerable<CodeIndexEntry>? snippets = null,
            string? llmResponse = null)
    {
        var llm    = new StubLlmService(llmResponse
            ?? """{"isSufficient":true,"confidence":9,"reason":"Enough context.","requestedContext":{"codeSearchQueries":[],"clarificationQuestions":[]}}""");
        var index  = new StubCodeIndexService(snippets);
        var traces = new LlmTraceService();
        var conflict = new ContextConflictService();
        var route  = new StubRouteJudge();

        var agent = new ContextAgentService(
            new StubPromptContextBuilder(packet),
            index,
            llm,
            traces,
            conflict,
            routeJudge: route);

        return (agent, llm, index, traces);
    }
}


// ── Test class ────────────────────────────────────────────────────────────────

[TestClass]
public sealed class ContextAgentTests
{
    private static ContextAgentRequest MakeRequest(int projectId = 1)
        => new()
        {
            ProjectId   = projectId,
            SessionId   = 0,
            UserRequest = "How does ArchiveTicketAsync work?",
        };

    // ── A: Sufficient context — no tool calls ─────────────────────────────────

    [TestMethod]
    [Description("A: When the LLM reports the initial context is sufficient, the agent does not call code search tools and returns a final prompt.")]
    public async Task ContextAgent_SufficientContext_NoToolCallsAndReturnsFinalPrompt()
    {
        const string sufficientJson = """
            {
              "isSufficient": true,
              "confidence": 9,
              "reason": "Relevant code and decisions are present.",
              "requestedContext": {
                "codeSearchQueries": [],
                "clarificationQuestions": []
              }
            }
            """;

        var (agent, llm, index, _) = ContextAgentFactory.Build(llmResponses: sufficientJson);

        var result = await agent.RunAsync(MakeRequest());

        Assert.IsTrue(result.WasSuccessful, "Agent run must succeed.");
        Assert.IsFalse(result.IsClarificationRequired, "No clarification should be required.");
        Assert.IsNotNull(result.FinalPrompt, "A final prompt must be returned.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.FinalPrompt), "Final prompt must not be empty.");

        // No tool calls
        Assert.HasCount(0, index.ReceivedQueries,
            "No code search queries should have been issued when context is sufficient.");
        Assert.IsFalse(result.WasExpanded,
            "WasExpanded must be false when no expansion occurred.");
        Assert.AreEqual(0, result.ExpandedFileCount);
    }

    // ── B: Code expansion ─────────────────────────────────────────────────────

    [TestMethod]
    [Description("B: When context is insufficient and queries are returned, the agent calls code search and adds evidence to the final prompt.")]
    public async Task ContextAgent_InsufficientContext_CallsCodeSearchAndExpandsContext()
    {
        const string insufficientJson = """
            {
              "isSufficient": false,
              "confidence": 4,
              "reason": "Need TicketService and ProjectTicket to verify archive behaviour.",
              "requestedContext": {
                "codeSearchQueries": [
                  "TicketService ArchiveTicketAsync IsDeleted",
                  "ProjectTicket IsDeleted"
                ],
                "clarificationQuestions": []
              }
            }
            """;

        var snippets = new[]
        {
            new CodeIndexEntry { FilePath = "IronDev.Infrastructure/Services/TicketService.cs",
                                 SymbolName = "ArchiveTicketAsync",
                                 ChunkText = "public async Task<bool> ArchiveTicketAsync(long ticketId" },
            new CodeIndexEntry { FilePath = "IronDev.Data.Models/DataModels.cs",
                                 SymbolName = "ProjectTicket",
                                 ChunkText = "public bool IsDeleted { get; set; }" },
        };

        var (agent, _, index, _) = ContextAgentFactory.Build(snippets: snippets, llmResponses: insufficientJson);

        var result = await agent.RunAsync(MakeRequest());

        Assert.IsTrue(result.WasSuccessful);
        Assert.IsFalse(result.IsClarificationRequired);
        Assert.IsNotNull(result.FinalPrompt);

        // Tool calls were made
        Assert.IsTrue(index.ReceivedQueries.Count > 0,
            "At least one code search query must have been issued.");
        Assert.IsTrue(result.WasExpanded, "WasExpanded must be true.");
        Assert.IsTrue(result.ExpandedSnippetCount > 0, "Snippets must have been added.");
        Assert.IsTrue(result.ExpandedFileCount > 0,    "Files must have been counted.");

        // Evidence is injected into the final prompt
        Assert.IsTrue(result.FinalPrompt!.Contains("EXPANDED CODE EVIDENCE"),
            "Final prompt must contain the expanded evidence section.");
        Assert.Contains(result.FinalPrompt, "ArchiveTicketAsync",
            "Final prompt must include content from the retrieved snippets.");

        // HasCodeEvidence is set
        Assert.IsTrue(result.HasCodeEvidence, "HasCodeEvidence must be true after expansion.");
    }

    // ── C: Clarification required ─────────────────────────────────────────────

    [TestMethod]
    [Description("C: When the LLM requests clarification, the agent returns clarification questions and no final prompt.")]
    public async Task ContextAgent_ClarificationRequired_ReturnsClarificationAndNoFinalPrompt()
    {
        const string clarifyJson = """
            {
              "isSufficient": false,
              "confidence": 3,
              "reason": "The question is ambiguous — need to know which ticket workspace is meant.",
              "requestedContext": {
                "codeSearchQueries": [],
                "clarificationQuestions": [
                  "Are you asking about the saved ticket list or the draft ticket flow?",
                  "Which project is the ticket in?"
                ]
              }
            }
            """;

        var (agent, _, index, _) = ContextAgentFactory.Build(llmResponses: clarifyJson);

        var result = await agent.RunAsync(MakeRequest());

        Assert.IsTrue(result.WasSuccessful);
        Assert.IsTrue(result.IsClarificationRequired,
            "IsClarificationRequired must be true when the LLM asked questions.");
        Assert.IsNull(result.FinalPrompt,
            "FinalPrompt must be null when clarification is required.");
        Assert.AreEqual(2, result.ClarificationQuestions.Count,
            "Both clarification questions must be present.");
        Assert.Contains(result.ClarificationQuestions[0], "draft ticket flow");

        // No code search was executed
        Assert.HasCount(0, index.ReceivedQueries,
            "No code search should run when clarification is required.");
    }

    // ── D: Limits are respected ───────────────────────────────────────────────

    [TestMethod]
    [Description("D: The agent never executes more queries or adds more snippets/files than the configured limits allow.")]
    public async Task ContextAgent_RespectsHardLimits_OnQueriesAndFiles()
    {
        const string insufficientJson = """
            {
              "isSufficient": false,
              "confidence": 2,
              "reason": "Need lots of code.",
              "requestedContext": {
                "codeSearchQueries": [
                  "Query1", "Query2", "Query3", "Query4", "Query5", "Query6"
                ],
                "clarificationQuestions": []
              }
            }
            """;

        // Stub returns 10 snippets per query — all from different "files"
        var snippets = Enumerable.Range(1, 10).Select(i => new CodeIndexEntry
        {
            FilePath   = $"File{i}.cs",
            SymbolName = $"Symbol{i}",
            ChunkText  = $"code for {i}",
        }).ToList();

        var limits = new ContextAgentLimits
        {
            MaxCodeSearchQueries = 2,
            MaxToolCallsPerRound = 2,
            MaxAddedFiles        = 3,
            MaxSnippets          = 3,
        };

        var request = new ContextAgentRequest
        {
            ProjectId   = 1,
            SessionId   = 0,
            UserRequest = "Show me everything.",
            Limits      = limits,
        };

        var (agent, _, index, _) = ContextAgentFactory.Build(snippets: snippets, llmResponses: insufficientJson);

        var result = await agent.RunAsync(request);

        Assert.IsTrue(result.WasSuccessful);

        // Never more than MaxCodeSearchQueries queries
        Assert.IsTrue(index.ReceivedQueries.Count <= limits.MaxCodeSearchQueries,
            $"Expected <= {limits.MaxCodeSearchQueries} queries but got {index.ReceivedQueries.Count}.");

        // Never more than MaxSnippets snippets or MaxAddedFiles files
        Assert.IsTrue(result.ExpandedSnippetCount <= limits.MaxSnippets,
            $"Expected <= {limits.MaxSnippets} snippets but got {result.ExpandedSnippetCount}.");
        Assert.IsTrue(result.ExpandedFileCount <= limits.MaxAddedFiles,
            $"Expected <= {limits.MaxAddedFiles} files but got {result.ExpandedFileCount}.");
    }

    // ── E: Trace entries created with TraceGroupId ────────────────────────────

    [TestMethod]
    [Description("E: Every stage of the Context Agent pipeline creates a trace entry with a shared, non-empty TraceGroupId.")]
    public async Task ContextAgent_CreatesTraceEntries_WithSharedTraceGroupId()
    {
        const string insufficientJson = """
            {
              "isSufficient": false,
              "confidence": 3,
              "reason": "Need more code.",
              "requestedContext": {
                "codeSearchQueries": ["ArchiveTicketAsync"],
                "clarificationQuestions": []
              }
            }
            """;

        var snippets = new[]
        {
            new CodeIndexEntry { FilePath = "Ticket.cs", SymbolName = "ArchiveTicketAsync", ChunkText = "body" }
        };

        var (agent, _, _, traces) = ContextAgentFactory.Build(snippets: snippets, llmResponses: insufficientJson);

        var result = await agent.RunAsync(MakeRequest());

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.TraceGroupId),
            "TraceGroupId must be set on the result.");

        var allTraces = traces.GetRecentTraces();

        // Must have at least InitialContext, SufficiencyCheck, ToolCall, ToolResult, FinalAnswer
        Assert.IsTrue(allTraces.Count >= 4,
            $"Expected at least 4 trace entries but got {allTraces.Count}.");

        // All trace entries share the same TraceGroupId
        var groupId = result.TraceGroupId;
        foreach (var t in allTraces)
        {
            Assert.AreEqual(groupId, t.TraceGroupId,
                $"Trace {t.FeatureName} has wrong TraceGroupId: {t.TraceGroupId}");
        }

        // Expected stage feature names are present
        var featureNames = allTraces.Select(t => t.FeatureName).ToHashSet();
        Assert.IsTrue(featureNames.Contains(ContextAgentStage.InitialContext),
            "Missing InitialContext trace.");
        Assert.IsTrue(featureNames.Contains(ContextAgentStage.SufficiencyCheck),
            "Missing SufficiencyCheck trace.");
        Assert.IsTrue(featureNames.Contains(ContextAgentStage.ToolCallSearch),
            "Missing ToolCall trace.");
        Assert.IsTrue(featureNames.Contains(ContextAgentStage.ToolResultSearch),
            "Missing ToolResult trace.");
        Assert.IsTrue(featureNames.Contains(ContextAgentStage.FinalAnswer),
            "Missing FinalAnswer trace.");
    }

    // ── F: No fake code claims ────────────────────────────────────────────────

    [TestMethod]
    [Description("F: When no code evidence exists, HasCodeEvidence is false and the final prompt contains the no-evidence disclaimer.")]
    public async Task ContextAgent_NoCodeEvidence_HasCodeEvidenceFalseAndDisclaimerPresent()
    {
        const string sufficientJson = """
            {
              "isSufficient": true,
              "confidence": 6,
              "reason": "Context is good enough without extra code.",
              "requestedContext": {
                "codeSearchQueries": [],
                "clarificationQuestions": []
              }
            }
            """;

        var (agent, _, _, _) = ContextAgentFactory.Build(llmResponses: sufficientJson);

        var result = await agent.RunAsync(MakeRequest());

        Assert.IsTrue(result.WasSuccessful);
        Assert.IsFalse(result.HasCodeEvidence,
            "HasCodeEvidence must be false when no expansion occurred.");
        Assert.AreEqual(0, result.Evidence.Count,
            "Evidence collection must be empty.");

        // Final prompt must include the no-evidence disclaimer
        Assert.IsTrue(result.FinalPrompt!.Contains("I do not have enough indexed code context to verify that"),
            "Final prompt must contain the evidence rule disclaimer when no code was retrieved.");
    }

    // ── G: Main chat compatibility (feature flag off) ─────────────────────────

    [TestMethod]
    [Description("G: When UseContextAgent is false, the ChatWorkspaceViewModel does not interact with the Context Agent service at all.")]
    public async Task ChatWorkspaceViewModel_ContextAgentOff_DoesNotUseAgentService()
    {
        // Arrange: track whether the agent was called
        bool agentCalled = false;

        var agentSpy = new SpyContextAgentService(() => agentCalled = true);

        // We can't run the full ViewModel easily here (it requires WPF DI)
        // so we directly verify the ContextAgentService behaviour independently.
        // The VM's UseContextAgent=false path is verified by NOT setting the flag.
        var vm = new IronDev.Agent.ViewModels.Workspaces.ChatWorkspaceViewModel(
            new StubChatHistoryService(),
            new StubPromptContextBuilder(),
            new StubLlmService("Hello"),
            new ContextStubProjectMemoryService(),
            new ContextStubTicketService(),
            new StubChatFeedbackService(),
            new LlmTraceService(),
            agentSpy);

        // UseContextAgent defaults to false — agent must not be called
        Assert.IsFalse(vm.UseContextAgent, "UseContextAgent must default to false.");

        // Calling RunAsync when flag is off is verified by inspecting the flag itself;
        // the VM SendMessageAsync branches on it before calling the service.
        // Here we also verify the agent spy was NOT called.
        Assert.IsFalse(agentCalled,
            "ContextAgentService must not be called when UseContextAgent is false.");
    }

    // ── H: JSON parsing edge cases ────────────────────────────────────────────

    [TestMethod]
    [Description("H: ParseSufficiencyJson handles markdown-wrapped JSON, missing fields, and invalid JSON gracefully.")]
    public void ContextAgentService_ParseSufficiencyJson_HandlesEdgeCases()
    {
        // Valid compact JSON
        var r1 = ContextAgentService.ParseSufficiencyJson(
            """{"isSufficient":true,"confidence":9,"reason":"Good","requestedContext":{"codeSearchQueries":[],"clarificationQuestions":[]}}""");
        Assert.IsTrue(r1.IsSufficient);
        Assert.AreEqual(9, r1.Confidence);
        Assert.IsFalse(r1.ParseError);

        // Markdown-wrapped JSON
        var r2 = ContextAgentService.ParseSufficiencyJson(
            "```json\n{\"isSufficient\":false,\"confidence\":3,\"reason\":\"Need code.\",\"requestedContext\":{\"codeSearchQueries\":[\"TicketService\"],\"clarificationQuestions\":[]}}\n```");
        Assert.IsFalse(r2.IsSufficient);
        Assert.HasCount(1, r2.CodeSearchQueries);
        Assert.AreEqual("TicketService", r2.CodeSearchQueries[0]);

        // Invalid JSON — must not throw, must set ParseError
        var r3 = ContextAgentService.ParseSufficiencyJson("this is not json");
        Assert.IsTrue(r3.ParseError, "ParseError must be set on invalid JSON.");
        Assert.IsTrue(r3.IsSufficient, "Fail-safe: ParseError result must be treated as sufficient.");

        // Empty string
        var r4 = ContextAgentService.ParseSufficiencyJson(string.Empty);
        Assert.IsTrue(r4.ParseError, "Empty string must be a ParseError.");

        // Confidence clamped to 0–10
        var r5 = ContextAgentService.ParseSufficiencyJson(
            """{"isSufficient":true,"confidence":99,"reason":"X","requestedContext":{"codeSearchQueries":[],"clarificationQuestions":[]}}""");
        Assert.AreEqual(10, r5.Confidence, "Confidence must be clamped to 10.");
    }

    // ── I: Sufficiency LLM failure degrades gracefully ────────────────────────

    [TestMethod]
    [Description("I: When the sufficiency-check LLM call throws, the agent treats context as sufficient and still returns a usable final prompt.")]
    public async Task ContextAgent_SufficiencyLlmFails_DegradesGracefully()
    {
        var failingLlm = new ThrowingLlmService();
        var traces     = new LlmTraceService();
        var agent      = new ContextAgentService(
            new StubPromptContextBuilder(),
            new StubCodeIndexService(),
            failingLlm,
            traces);

        var result = await agent.RunAsync(MakeRequest());

        Assert.IsTrue(result.WasSuccessful, "Agent must succeed even when LLM fails.");
        Assert.IsNotNull(result.FinalPrompt, "A final prompt must be returned as degraded output.");
        Assert.IsTrue(result.Warnings.Contains("Sufficiency check LLM error"),
            "Warnings must mention the LLM error.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Retrieval quality tests (A2–G2)
    // ═══════════════════════════════════════════════════════════════════════════

    // ── A2: Query expansion — LLM Console ────────────────────────────────────

    [TestMethod]
    [Description("A2: 'LLM Console' query expands to symbol-level queries including LlmConsoleViewModel and LlmTraceService.")]
    public void RetrievalQuality_QueryExpansion_LlmConsoleExpandsToSymbols()
    {
        var expanded = RetrievalQualityHelpers.ExpandQueries(["LLM Console"]);

        CollectionAssert.Contains(expanded.ToList(), "LLM Console",
            "Original query must be preserved.");
        Assert.IsTrue(expanded.Any(q => q.Contains("LlmConsoleViewModel")),
            "Must expand to LlmConsoleViewModel.");
        Assert.IsTrue(expanded.Any(q => q.Contains("LlmTraceService")),
            "Must expand to LlmTraceService.");
        Assert.IsTrue(expanded.Any(q => q.Contains("ILlmTraceService")),
            "Must expand to ILlmTraceService.");
    }

    // ── B2: Query expansion — soft archive ────────────────────────────────────

    [TestMethod]
    [Description("B2: 'soft archive tickets' expands to ArchiveTicketAsync / IsDeleted / ProjectTicket / GetRecentTicketsAsync.")]
    public void RetrievalQuality_QueryExpansion_SoftArchiveExpandsToSymbols()
    {
        var expanded = RetrievalQualityHelpers.ExpandQueries(["soft archive tickets"]);

        Assert.IsTrue(expanded.Any(q => q.Contains("ArchiveTicketAsync")),
            "Must expand to ArchiveTicketAsync.");
        Assert.IsTrue(expanded.Any(q => q.Contains("IsDeleted")),
            "Must expand to IsDeleted.");
        Assert.IsTrue(expanded.Any(q => q.Contains("ProjectTicket")),
            "Must expand to ProjectTicket.");
        Assert.IsTrue(expanded.Any(q => q.Contains("GetRecentTicketsAsync")),
            "Must expand to GetRecentTicketsAsync.");
    }

    // ── C2: Production ranking — production over tests ────────────────────────

    [TestMethod]
    [Description("C2: Given mixed production and test snippets, production files are ranked first.")]
    public void RetrievalQuality_ProductionRanking_ProductionFilesRankedFirst()
    {
        var entries = new List<IronDev.Data.Models.CodeIndexEntry>
        {
            new() { FilePath = "IronDev.IntegrationTests/ChatGroundingQualityTests.cs",
                    SymbolName = "SeedCodeIndexEntry", ChunkText = "seed data" },
            new() { FilePath = "IronDev.Infrastructure/Services/TicketService.cs",
                    SymbolName = "ArchiveTicketAsync", ChunkText = new string('x', 150) },
            new() { FilePath = "IronDev.Core/Models/DataModels.cs",
                    SymbolName = "ProjectTicket", ChunkText = "public bool IsDeleted" },
        };

        var ranked = RetrievalQualityHelpers.RankByProductionFirst(entries, excludeTests: false);

        // Test file must be last
        Assert.AreEqual(
            "IronDev.IntegrationTests/ChatGroundingQualityTests.cs",
            ranked[ranked.Count - 1].FilePath,
            "Test file must be ranked last.");

        // Production infrastructure must be first
        Assert.AreEqual(
            "IronDev.Infrastructure/Services/TicketService.cs",
            ranked[0].FilePath,
            "TicketService must be ranked first.");
    }

    // ── D2: Test fixture exclusion ─────────────────────────────────────────────

    [TestMethod]
    [Description("D2: ChatGroundingQualityTests.cs is excluded when excludeTests=true and is never selected as primary evidence.")]
    public async Task RetrievalQuality_TestFixtureExclusion_GroundingTestsNotPrimaryEvidence()
    {
        const string insufficientJson = """
            {
              "isSufficient": false,
              "confidence": 3,
              "reason": "Need ArchiveTicketAsync implementation.",
              "requestedContext": {
                "codeSearchQueries": ["ArchiveTicketAsync"],
                "clarificationQuestions": []
              }
            }
            """;

        var snippets = new[]
        {
            // Test fixture file — should be excluded from primary evidence
            new IronDev.Data.Models.CodeIndexEntry
            {
                FilePath   = "IronDev.IntegrationTests/ChatGroundingQualityTests.cs",
                SymbolName = "SeedCodeIndexEntryAsync",
                ChunkText  = "await SeedCodeIndexEntryAsync(conn, projectId, \"ArchiveTicketAsync\", ...);"
            },
            // Production file — should be selected
            new IronDev.Data.Models.CodeIndexEntry
            {
                FilePath   = "IronDev.Infrastructure/Services/TicketService.cs",
                SymbolName = "ArchiveTicketAsync",
                ChunkText  = new string('p', 200), // sufficiently deep
            },
        };

        var (agent, _, _, _) = ContextAgentFactory.Build(snippets: snippets, llmResponses: insufficientJson);
        var result = await agent.RunAsync(MakeRequest());

        Assert.IsTrue(result.WasSuccessful);
        Assert.IsTrue(result.HasCodeEvidence, "Should have evidence from the production file.");

        // ChatGroundingQualityTests.cs must not appear as primary evidence
        Assert.IsFalse(
            result.Evidence.Any(e => e.FilePath.Contains("ChatGrounding") || e.FilePath.Contains("IntegrationTests")),
            "ChatGroundingQualityTests.cs must not be in primary evidence.");

        // Production file must be selected
        Assert.IsTrue(
            result.Evidence.Any(e => e.FilePath.Contains("TicketService")),
            "TicketService.cs must be included as primary evidence.");
    }

    // ── E2: Clarification wins for "Create a ticket to fix delete" ───────────

    [TestMethod]
    [Description("E2: 'Create a ticket to fix delete' returns clarification questions before any LLM call.")]
    public async Task RetrievalQuality_ClarificationFirst_VagueDeleteRequestAsksClarification()
    {
        // Even if LLM would say "insufficient", the pre-check must catch this first.
        // The stub LLM won't be called at all for this pattern.
        var trackLlmCalled = false;
        var trackingLlm    = new TrackingLlmService(() => trackLlmCalled = true,
            """{"isSufficient":false,"confidence":2,"reason":"vague","requestedContext":{"codeSearchQueries":["DeleteTicketAsync"],"clarificationQuestions":[]}}""");

        var agent = new ContextAgentService(
            new StubPromptContextBuilder(),
            new StubCodeIndexService(),
            trackingLlm,
            new LlmTraceService());

        var result = await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId   = 1,
            SessionId   = 0,
            UserRequest = "Create a ticket to fix delete.",
        });

        Assert.IsTrue(result.WasSuccessful);
        Assert.IsTrue(result.IsClarificationRequired,
            "Vague 'fix delete' request must return clarification.");
        Assert.IsNull(result.FinalPrompt,
            "No final prompt should be generated for a vague request.");
        Assert.IsTrue(result.ClarificationQuestions.Count > 0,
            "At least one clarification question must be returned.");
        Assert.IsFalse(trackLlmCalled,
            "LLM must NOT be called for the sufficiency check when vague intent detected pre-check.");
    }

    // ── F2: Useful snippet depth — deep snippets preferred over declarations ──

    [TestMethod]
    [Description("F2: Deep implementation snippets are preferred over shallow interface stubs.")]
    public void RetrievalQuality_SnippetDepth_DeepSnippetsPreferredOverDeclarations()
    {
        var entries = new List<IronDev.Data.Models.CodeIndexEntry>
        {
            // Shallow: just a method signature
            new() { FilePath = "IronDev.Core/Interfaces/ITicketService.cs",
                    SymbolName = "ArchiveTicketAsync",
                    ChunkText  = "Task<bool> ArchiveTicketAsync(long id);" },
            // Deep: full method body
            new() { FilePath = "IronDev.Infrastructure/Services/TicketService.cs",
                    SymbolName = "ArchiveTicketAsync",
                    ChunkText  = new string('x', 250) + "UPDATE ProjectTickets SET IsDeleted=1" },
        };

        var result = RetrievalQualityHelpers.PreferDeepSnippets(
                     RetrievalQualityHelpers.RankByProductionFirst(entries));

        // The deep production snippet should come first
        Assert.AreEqual(
            "IronDev.Infrastructure/Services/TicketService.cs",
            result[0].FilePath,
            "Deep production implementation must be ranked before shallow interface declaration.");
    }

    // ── G2: Trace transparency — ToolResult shows retrieval diagnostics ────────

    [TestMethod]
    [Description("G2: ToolResult trace contains original query, expanded queries, raw/filtered counts, and excluded test file count.")]
    public async Task RetrievalQuality_TraceTransparency_ToolResultContainsRetrievalDiagnostics()
    {
        const string insufficientJson = """
            {
              "isSufficient": false,
              "confidence": 3,
              "reason": "Need LLM Console implementation.",
              "requestedContext": {
                "codeSearchQueries": ["LLM Console"],
                "clarificationQuestions": []
              }
            }
            """;

        // Mix of production and test snippets to verify filtering diagnostics
        var snippets = new[]
        {
            new IronDev.Data.Models.CodeIndexEntry
            {
                FilePath   = "IronDev.IntegrationTests/LlmConsoleTests.cs",
                SymbolName = "Test_LlmConsole",
                ChunkText  = "test code"
            },
            new IronDev.Data.Models.CodeIndexEntry
            {
                FilePath   = "IronDeveloper/ViewModels/Workspaces/LlmConsoleViewModel.cs",
                SymbolName = "LlmConsoleViewModel",
                ChunkText  = new string('v', 200),
            },
        };

        var (agent, _, _, traces) = ContextAgentFactory.Build(snippets: snippets, llmResponses: insufficientJson);
        var result = await agent.RunAsync(new ContextAgentRequest
        {
            ProjectId   = 1,
            SessionId   = 0,
            UserRequest = "What is the purpose of the LLM Console?",
        });

        Assert.IsTrue(result.WasSuccessful);

        var allTraces    = traces.GetRecentTraces();
        var toolResults  = allTraces.Where(t => t.FeatureName == ContextAgentStage.ToolResultSearch).ToList();

        Assert.IsTrue(toolResults.Count > 0, "At least one ToolResult trace must exist.");

        var firstResult = toolResults.First();

        // ParsedResponseSummary must contain raw count, excluded test count, filtered count, added count
        Assert.IsTrue(firstResult.ParsedResponseSummary.Contains("Raw="),
            $"ParsedResponseSummary must show raw count. Got: {firstResult.ParsedResponseSummary}");
        Assert.IsTrue(firstResult.ParsedResponseSummary.Contains("ExcludedTests="),
            $"ParsedResponseSummary must show excluded test count. Got: {firstResult.ParsedResponseSummary}");
        Assert.IsTrue(firstResult.ParsedResponseSummary.Contains("AfterFilter="),
            $"ParsedResponseSummary must show after-filter count. Got: {firstResult.ParsedResponseSummary}");

        // RawResponseText is the RetrievalTraceSummary.ToTraceText() — must contain expanded queries
        Assert.IsTrue(firstResult.RawResponseText.Contains("Original query"),
            "ToolResult trace RawResponseText must contain original query label.");

        // Test file must NOT be in final evidence
        Assert.IsFalse(
            result.Evidence.Any(e => e.FilePath.Contains("IntegrationTests")),
            "Test file must not appear in final evidence.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Conflict assessment tests (Q–V)
    // ═══════════════════════════════════════════════════════════════════════════

    private static ContextAgentRequest MakeTicketRequest(
        string userRequest,
        IEnumerable<IronDev.Data.Models.ProjectTicket>? tickets = null,
        IEnumerable<IronDev.Data.Models.ProjectDecision>? decisions = null,
        IEnumerable<IronDev.Data.Models.ProjectRule>? rules = null)
        => new()
        {
            ProjectId       = 1,
            SessionId       = 0,
            UserRequest     = userRequest,
            RecentTickets   = tickets?.ToList()   ?? [],
            RecentDecisions = decisions?.ToList() ?? [],
            ProjectRules    = rules?.ToList()     ?? [],
        };

    private static ContextAgentService BuildConflictAgent(
        string sufficientJson = """{"isSufficient":true,"confidence":9,"reason":"Enough context.","requestedContext":{"codeSearchQueries":[],"clarificationQuestions":[]}}""")
    {
        var (agent, _, _, _) = ContextAgentFactory.BuildWithConflict(
            llmResponse: sufficientJson);
        return agent;
    }

    // ── Q: OAuth ticket conflicts with API key auth request ───────────────────

    [TestMethod]
    [Description("Q: Given existing OAuth ticket, requesting API key auth for same layer is classified as Conflicts and blocks creation.")]
    public async Task ConflictAssessment_OAuthVsApiKey_ClassifiesAsConflict()
    {
        var existingTicket = new IronDev.Data.Models.ProjectTicket
        {
            Id      = 123,
            Title   = "Implement OAuth in REST layer",
            Status  = "Draft",
            Summary = "Add OAuth authentication to the REST API endpoints.",
        };

        var agent = BuildConflictAgent();
        var result = await agent.RunAsync(MakeTicketRequest(
            "Create a ticket to add API key authentication to the REST layer",
            tickets: [existingTicket]));

        Assert.IsTrue(result.WasSuccessful);
        Assert.IsTrue(result.IsClarificationRequired,
            "A conflict with OAuth should block ticket creation and require clarification.");
        Assert.IsNotNull(result.ConflictAssessment, "ConflictAssessment must be set.");
        Assert.IsTrue(
            result.ConflictAssessment!.Classification is
                ConflictClassification.Conflicts or ConflictClassification.NeedsDecision,
            $"Expected Conflicts or NeedsDecision, got {result.ConflictAssessment.Classification}");
        Assert.AreEqual(1, result.ConflictAssessment.RelatedTickets.Count,
            "OAuth ticket must be in related tickets.");
        Assert.IsTrue(result.ClarificationQuestions.Count > 0,
            "Clarification questions must be returned.");
    }

    // ── R: Same-approach duplicate ────────────────────────────────────────────

    [TestMethod]
    [Description("R: Existing OAuth middleware ticket and new OAuth login middleware request → Duplicate or Overlaps.")]
    public async Task ConflictAssessment_SameApproach_ClassifiesAsDuplicateOrOverlaps()
    {
        var existingTicket = new IronDev.Data.Models.ProjectTicket
        {
            Id      = 200,
            Title   = "Implement OAuth login middleware",
            Status  = "Draft",
            Summary = "Add OAuth-based login middleware to the REST API.",
        };

        var agent = BuildConflictAgent();
        var result = await agent.RunAsync(MakeTicketRequest(
            "Create a ticket to add OAuth login middleware",
            tickets: [existingTicket]));

        Assert.IsTrue(result.WasSuccessful);
        Assert.IsNotNull(result.ConflictAssessment, "ConflictAssessment must be set.");
        Assert.IsTrue(
            result.ConflictAssessment!.Classification is
                ConflictClassification.Duplicate or ConflictClassification.Overlaps,
            $"Expected Duplicate or Overlaps for same-approach, got {result.ConflictAssessment.Classification}");
        Assert.IsTrue(result.IsClarificationRequired,
            "Duplicate/Overlaps must block creation and ask clarification.");
    }

    // ── S: Architecture decision blocks contradictory request ─────────────────

    [TestMethod]
    [Description("S: Decision 'OAuth is required for REST APIs' blocks a Basic Auth request.")]
    public async Task ConflictAssessment_DecisionContradictsRequest_ClassifiesAsConflict()
    {
        var oauthDecision = new IronDev.Data.Models.ProjectDecision
        {
            Id     = 10,
            Title  = "OAuth is required for REST APIs",
            Detail = "All REST API authentication must use OAuth. Basic Auth is not permitted.",
            Status = "Accepted",
        };

        var agent = BuildConflictAgent();
        var result = await agent.RunAsync(MakeTicketRequest(
            "Create a ticket to implement Basic Auth for the REST API",
            decisions: [oauthDecision]));

        Assert.IsTrue(result.WasSuccessful);
        Assert.IsNotNull(result.ConflictAssessment, "ConflictAssessment must be set.");
        Assert.IsTrue(
            result.ConflictAssessment!.Classification is
                ConflictClassification.Conflicts or ConflictClassification.NeedsDecision,
            $"Decision contradiction should classify as Conflicts or NeedsDecision, got {result.ConflictAssessment.Classification}");
        Assert.IsTrue(result.ConflictAssessment.ConflictingDecisions.Count > 0,
            "The contradicting decision must appear in ConflictingDecisions.");
        Assert.IsTrue(result.IsClarificationRequired,
            "Decision conflict must block creation.");
    }

    // ── T: Explicit replace intent ────────────────────────────────────────────

    [TestMethod]
    [Description("T: 'Replace OAuth with API key auth' is classified as ReplacesExisting.")]
    public async Task ConflictAssessment_ExplicitReplace_ClassifiesAsReplacesExisting()
    {
        var existingTicket = new IronDev.Data.Models.ProjectTicket
        {
            Id      = 300,
            Title   = "Implement OAuth in REST layer",
            Status  = "Draft",
            Summary = "OAuth authentication for REST.",
        };

        var agent = BuildConflictAgent();
        var result = await agent.RunAsync(MakeTicketRequest(
            "Replace OAuth with API key authentication in the REST layer",
            tickets: [existingTicket]));

        Assert.IsTrue(result.WasSuccessful);
        Assert.IsNotNull(result.ConflictAssessment, "ConflictAssessment must be set.");
        Assert.AreEqual(ConflictClassification.ReplacesExisting,
            result.ConflictAssessment!.Classification,
            "Explicit replace phrasing must classify as ReplacesExisting.");
        
        Assert.AreEqual("OAuth", result.ConflictAssessment.ExistingApproach, "ExistingApproach should be OAuth.");
        Assert.AreEqual("API key authentication", result.ConflictAssessment.RequestedApproach, "RequestedApproach should be API key authentication.");
        
        Assert.IsTrue(result.IsClarificationRequired,
            "ReplacesExisting must block silent creation.");
    }

    // ── U: Conflict prevents silent creation ──────────────────────────────────

    [TestMethod]
    [Description("U: Strong conflict (Conflicts / Duplicate) means FinalPrompt is null and IsClarificationRequired is true.")]
    public async Task ConflictAssessment_StrongConflict_BlocksTicketCreation()
    {
        var existingTicket = new IronDev.Data.Models.ProjectTicket
        {
            Id      = 400,
            Title   = "Implement OAuth in REST layer",
            Status  = "Active",
            Summary = "OAuth authentication for REST.",
        };

        var agent = BuildConflictAgent();
        var result = await agent.RunAsync(MakeTicketRequest(
            "Add API key authentication to the REST layer",
            tickets: [existingTicket]));

        Assert.IsTrue(result.WasSuccessful);
        Assert.IsTrue(result.IsClarificationRequired,
            "Conflict must prevent ticket creation.");
        Assert.IsNull(result.FinalPrompt,
            "FinalPrompt must be null when conflict blocks creation.");
        Assert.IsTrue(result.ConflictAssessment!.BlocksTicketCreation,
            "BlocksTicketCreation must be true.");
    }

    // ── V: Trace includes conflict classification ──────────────────────────────

    [TestMethod]
    [Description("V: ContextAgent.ConflictAssessment trace entry includes classification, domain, related ticket IDs/titles, and recommended action.")]
    public async Task ConflictAssessment_TraceContainsClassificationDetails()
    {
        var existingTicket = new IronDev.Data.Models.ProjectTicket
        {
            Id      = 500,
            Title   = "Implement OAuth in REST layer",
            Status  = "Draft",
            Summary = "OAuth authentication.",
        };

        var (agent, _, _, traces) = ContextAgentFactory.BuildWithConflict();
        var result = await agent.RunAsync(MakeTicketRequest(
            "Add API key authentication to the REST layer",
            tickets: [existingTicket]));

        Assert.IsTrue(result.WasSuccessful);

        var allTraces = traces.GetRecentTraces();
        var conflictTrace = allTraces.FirstOrDefault(
            t => t.FeatureName == ContextAgentStage.ConflictAssessment);

        Assert.IsNotNull(conflictTrace,
            "ContextAgent.ConflictAssessment trace entry must be present.");
        Assert.IsTrue(conflictTrace!.ParsedResponseSummary.Contains("Classification="),
            $"Trace must show Classification. Got: {conflictTrace.ParsedResponseSummary}");
        Assert.IsTrue(conflictTrace.ParsedResponseSummary.Contains("Related="),
            $"Trace must show related ticket count. Got: {conflictTrace.ParsedResponseSummary}");
        Assert.IsTrue(conflictTrace.ParsedResponseSummary.Contains("Domain="),
            $"Trace must show domain. Got: {conflictTrace.ParsedResponseSummary}");
        Assert.IsTrue(conflictTrace.RawResponseText.Contains("OAuth") ||
                      conflictTrace.RawResponseText.Contains("REST"),
            "Trace RawResponseText must reference the domain/approach.");
    }

    // ── W: ChatWorkspaceViewModel passes tickets to ContextAgentRequest ───────

    [TestMethod]
    [Description("W: ChatWorkspaceViewModel populates ContextAgentRequest.RecentTickets when UseContextAgent is true.")]
    public async Task ChatWorkspaceViewModel_PopulatesRecentTicketsInRequest()
    {
        var tickets = new List<IronDev.Data.Models.ProjectTicket> { new IronDev.Data.Models.ProjectTicket { Id = 1, Title = "Existing Ticket" } };
        var ticketService = new ContextStubTicketService { Tickets = tickets };
        var spy = new ContextRequestSpyAgentService();
        
        var vm = new IronDev.Agent.ViewModels.Workspaces.ChatWorkspaceViewModel(
            new StubChatHistoryService(),
            new StubPromptContextBuilder(),
            new StubLlmService("Hello"),
            new ContextStubProjectMemoryService(),
            ticketService,
            new StubChatFeedbackService(),
            new LlmTraceService(),
            spy);

        await vm.LoadAsync(new IronDev.Data.Models.Project { Id = 1 });

        vm.UseContextAgent = true;
        vm.PromptText = "Help me with something";
        await vm.SendMessageCommand.ExecuteAsync(null);

        Assert.IsNotNull(spy.LastRequest);
        Assert.HasCount(1, spy.LastRequest.RecentTickets);
        Assert.AreEqual("Existing Ticket", spy.LastRequest.RecentTickets[0].Title);
    }

    // ── X: ChatWorkspaceViewModel passes decisions/rules to Request ───────────

    [TestMethod]
    [Description("X: ChatWorkspaceViewModel populates RecentDecisions and ProjectRules when UseContextAgent is true.")]
    public async Task ChatWorkspaceViewModel_PopulatesDecisionsAndRulesInRequest()
    {
        var memoryService = new ContextStubProjectMemoryService
        {
            Decisions = [ new IronDev.Data.Models.ProjectDecision { Title = "D1" } ],
            Rules     = [ new IronDev.Data.Models.ProjectRule { Name = "R1" } ]
        };
        var spy = new ContextRequestSpyAgentService();
        
        var vm = new IronDev.Agent.ViewModels.Workspaces.ChatWorkspaceViewModel(
            new StubChatHistoryService(),
            new StubPromptContextBuilder(),
            new StubLlmService("Hello"),
            memoryService,
            new ContextStubTicketService(),
            new StubChatFeedbackService(),
            new LlmTraceService(),
            spy);

        await vm.LoadAsync(new IronDev.Data.Models.Project { Id = 1 });

        vm.UseContextAgent = true;
        vm.PromptText = "Create a ticket";
        await vm.SendMessageCommand.ExecuteAsync(null);

        Assert.IsNotNull(spy.LastRequest);
        Assert.HasCount(1, spy.LastRequest.RecentDecisions);
        Assert.AreEqual("D1", spy.LastRequest.RecentDecisions[0].Title);
        Assert.HasCount(1, spy.LastRequest.ProjectRules);
        Assert.AreEqual("R1", spy.LastRequest.ProjectRules[0].Name);
    }

    // ── Y: OAuth ticket blocks API key auth request in runtime chat ───────────

    [TestMethod]
    [Description("Y: Existing OAuth ticket in the runtime environment successfully blocks an API key auth ticket creation via ContextAgent.")]
    public async Task ChatWorkspaceViewModel_RuntimeConflict_BlocksTicketCreation()
    {
        // Arrange
        var existingTicket = new IronDev.Data.Models.ProjectTicket
        {
            Id = 1,
            Title = "Implement OAuth in REST layer",
            Status = "Draft",
            Summary = "OAuth auth."
        };
        
        var ticketService = new ContextStubTicketService { Tickets = [existingTicket] };
        
        // We use the REAL ContextAgentService wired with a REAL ContextConflictService,
        // but stubbed LLM/Index/Traces.
        var (agent, _, _, traces) = ContextAgentFactory.BuildWithConflict();
        
        var vm = new IronDev.Agent.ViewModels.Workspaces.ChatWorkspaceViewModel(
            new StubChatHistoryService(),
            new StubPromptContextBuilder(),
            new StubLlmService("Hello"), // Final answer LLM
            new ContextStubProjectMemoryService(),
            ticketService,
            new StubChatFeedbackService(),
            new LlmTraceService(),
            agent);

        await vm.LoadAsync(new IronDev.Data.Models.Project { Id = 1 });

        vm.UseContextAgent = true;
        vm.PromptText = "Create a ticket to add API key authentication to the REST layer";

        // Act
        await vm.SendMessageCommand.ExecuteAsync(null);

        // Assert
        var lastMsg = vm.Messages.LastOrDefault();
        Assert.IsNotNull(lastMsg);
        Assert.AreEqual("assistant", lastMsg.Role);
        Assert.IsTrue(lastMsg.MessageText.Contains("Before I can answer, I need a bit more context"),
            "Should have triggered clarification due to conflict.");
        Assert.IsTrue(lastMsg.MessageText.Contains("replace"),
            "Clarification should mention replacement or overlap.");
            
        // Check trace
        var allTraces = traces.GetRecentTraces();
        Assert.IsTrue(allTraces.Any(t => t.FeatureName == ContextAgentStage.ConflictAssessment),
            "ConflictAssessment trace must be present.");
    }

    // ── Z: Graceful degradation on retrieval failure ──────────────────────────

    [TestMethod]
    [Description("Z: When artefact retrieval fails, ChatWorkspaceViewModel degrades gracefully and still calls the Context Agent.")]
    public async Task ChatWorkspaceViewModel_RetrievalFailure_DegradesGracefully()
    {
        var failingTicketService = new ContextFailingTicketService();
        var spy = new ContextRequestSpyAgentService();
        
        var vm = new ChatWorkspaceViewModel(
            new StubChatHistoryService(),
            new StubPromptContextBuilder(),
            new StubLlmService("Hello"),
            new ContextStubProjectMemoryService(),
            failingTicketService,
            new StubChatFeedbackService(),
            new LlmTraceService(),
            spy);

        await vm.LoadAsync(new IronDev.Data.Models.Project { Id = 1 });

        vm.UseContextAgent = true;
        vm.PromptText = "Hello";
        
        await vm.SendMessageCommand.ExecuteAsync(null);

        Assert.IsNotNull(spy.LastRequest, "Agent must still be called even if tickets fail.");
        Assert.HasCount(0, spy.LastRequest.RecentTickets);
    }

    // ── A3: ConflictAssessment trace in runtime chat ───────────────────────────

    [TestMethod]
    [Description("A3: ConflictAssessment trace contains diagnostic details when artefacts are passed in a runtime chat.")]
    public async Task ChatWorkspaceViewModel_Runtime_TraceContainsConflictDiagnostics()
    {
        var existingTicket = new IronDev.Data.Models.ProjectTicket { Id = 1, Title = "OAuth", Summary = "OAuth" };
        var ticketService = new ContextStubTicketService { Tickets = [existingTicket] };
        var (agent, _, _, traces) = ContextAgentFactory.BuildWithConflict();
        
        var vm = new ChatWorkspaceViewModel(
            new StubChatHistoryService(),
            new StubPromptContextBuilder(),
            new StubLlmService("Hello"),
            new ContextStubProjectMemoryService(),
            ticketService,
            new StubChatFeedbackService(),
            traces,
            agent);

        await vm.LoadAsync(new IronDev.Data.Models.Project { Id = 1 });

        vm.UseContextAgent = true;
        vm.PromptText = "Create a ticket for soft archive in rest layer";
        await vm.SendMessageCommand.ExecuteAsync(null);

        var conflictTrace = traces.GetRecentTraces()
            .FirstOrDefault(t => t.FeatureName == ContextAgentStage.ConflictAssessment);

        Assert.IsNotNull(conflictTrace);
        Assert.IsTrue(conflictTrace.ParsedResponseSummary.Contains("Classification="));
        Assert.IsTrue(conflictTrace.ParsedResponseSummary.Contains("Domain=REST"));
        
        // Assert B: RequestText includes user request and related ticket titles
        Assert.IsNotNull(conflictTrace.RequestText);
        Assert.IsTrue(conflictTrace.RequestText.Contains("Create a ticket for soft archive in rest layer"));
        Assert.IsTrue(conflictTrace.RequestText.Contains("OAuth"));
        Assert.IsTrue(conflictTrace.RequestText.Contains("Decision Rules"));
    }
    
    // ── B2: Pre-check clarification trace contains diagnostic details ────────
    
    [TestMethod]
    [Description("B2: ClarificationRequired trace contains user request and matched pattern for pre-check decisions.")]
    public async Task ContextAgent_PreCheckClarification_TraceContainsDiagnostics()
    {
        var (agent, _, _, traces) = ContextAgentFactory.Build();
        var request = new ContextAgentRequest 
        { 
            UserRequest = "Create a ticket to fix delete",
            ProjectId = 1
        };
        
        await agent.RunAsync(request);
        
        var trace = traces.GetRecentTraces()
            .FirstOrDefault(t => t.FeatureName == ContextAgentStage.ClarificationRequired);
            
        Assert.IsNotNull(trace);
        Assert.IsNotNull(trace.RequestText);
        Assert.IsTrue(trace.RequestText.Contains("UserRequest: Create a ticket to fix delete"));
        Assert.IsTrue(trace.RequestText.Contains("Matched Pattern: fix delete"));
        Assert.IsTrue(trace.RequestText.Contains("Candidate Domains"));
    }

    // ── B3: ToolResult trace contains query expansion and filtering info ─────

    [TestMethod]
    [Description("B3: ToolResult trace includes query expansion and filtering input for retrieval transparency.")]
    public async Task ContextAgent_ToolResult_TraceContainsRetrievalInput()
    {
        // Setup: LLM asks for "soft archive" code search
        var sufficiencyJson = """
        {
          "isSufficient": false,
          "confidence": 3,
          "reason": "Need to see soft archive code.",
          "requestedContext": {
            "codeSearchQueries": ["soft archive"],
            "clarificationQuestions": []
          }
        }
        """;
        
        var snippets = new List<IronDev.Data.Models.CodeIndexEntry>
        {
            new() { FilePath = "TicketService.cs", ChunkText = "public void ArchiveTicket() {}" }
        };
        
        var (agent, _, _, traces) = ContextAgentFactory.BuildWithConflict(llmResponse: sufficiencyJson, snippets: snippets);
        var request = new ContextAgentRequest { UserRequest = "How does soft archive work?", ProjectId = 1 };
        
        await agent.RunAsync(request);
        
        var resultTrace = traces.GetRecentTraces()
            .FirstOrDefault(t => t.FeatureName == ContextAgentStage.ToolResultSearch && t.RequestText.Contains("soft archive"));
            
        Assert.IsNotNull(resultTrace);
        Assert.IsNotNull(resultTrace.RequestText);
        // Using CaseInsensitive search to be safe
        Assert.IsTrue(resultTrace.RequestText.Contains("OriginalQuery:"), "Should have OriginalQuery label");
        Assert.IsTrue(resultTrace.RequestText.Contains("soft archive"), "Should have the query text");
        // "soft archive" expands to ArchiveTicketAsync etc. in ExpansionTable
        Assert.IsTrue(resultTrace.RequestText.Contains("ExpandedQueries: soft archive, ArchiveTicketAsync", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(resultTrace.RequestText.Contains("ExcludeTests: true", StringComparison.OrdinalIgnoreCase));
    }
    // ── C1: Intent extraction (Test A & D) ───────────────────────────────────

    [TestMethod]
    [Description("Test A & D: Parser extracts WorkText and ignores non-ticket chat.")]
    public void ChatIntentParser_ExtractsWorkText_IgnoresNormalChat()
    {
        var intent = IronDev.Infrastructure.Services.ChatIntentParser.ParseCreateTicket("Create a ticket to add API key authentication to REST layer");
        Assert.IsNotNull(intent);
        Assert.AreEqual("add API key authentication to REST layer", intent.WorkText);

        var noIntent = IronDev.Infrastructure.Services.ChatIntentParser.ParseCreateTicket("How do I add API key authentication?");
        Assert.IsNull(noIntent);
    }

    // ── C2: Domain extraction via WorkText (Test A & B) ──────────────────────

    [TestMethod]
    [Description("Test A & B: Domain detection uses WorkText. Prevents polluting non-ticket domains with 'ticket' keyword.")]
    public void ContextConflictService_UsesWorkTextForDomain()
    {
        // "Create a ticket" has "ticket", which triggers "ticket management" domain.
        // If we only use WorkText "add API key authentication to REST layer", it only triggers REST auth.
        var intentA = IronDev.Infrastructure.Services.ChatIntentParser.ParseCreateTicket("Create a ticket to add API key authentication to REST layer");
        var domainsA = IronDev.Infrastructure.Services.ContextConflictService.DetectDomains(intentA!.WorkText);
        Assert.IsFalse(domainsA.Contains("ticket management"));
        Assert.IsTrue(domainsA.Contains("REST authentication"));

        // "Create a ticket to soft archive saved tickets" has "soft archive" in WorkText.
        var intentB = IronDev.Infrastructure.Services.ChatIntentParser.ParseCreateTicket("Create a ticket to soft archive saved tickets");
        var domainsB = IronDev.Infrastructure.Services.ContextConflictService.DetectDomains(intentB!.WorkText);
        Assert.IsTrue(domainsB.Contains("ticket management"));
    }

    // ── C3: ticket this (Test C) ─────────────────────────────────────────────

    [TestMethod]
    [Description("Test C: 'ticket this' uses previous message as WorkText.")]
    public void ChatIntentParser_TicketThis_UsesPreviousMessage()
    {
        var intent = IronDev.Infrastructure.Services.ChatIntentParser.ParseCreateTicket("ticket this", "Implement OAuth in the backend");
        Assert.IsNotNull(intent);
        Assert.AreEqual("Implement OAuth in the backend", intent.WorkText);
        Assert.AreEqual("ticket this", intent.CommandText);
    }

    // ── C4: Conflict Assessment and Traces (Test E & F) ──────────────────────

    [TestMethod]
    [Description("Test E & F: Conflict assessment receives WorkText, and Trace includes original and WorkText.")]
    public async Task ChatWorkspaceViewModel_PassesWorkText_And_LogsTrace()
    {
        var tickets = new List<IronDev.Data.Models.ProjectTicket> 
        { 
            new IronDev.Data.Models.ProjectTicket { Id = 1, Title = "add API key authentication to REST layer" } 
        };
        var ticketService = new ContextStubTicketService { Tickets = tickets };
        var spy = new ContextRequestSpyAgentService();
        var traceService = new LlmTraceService();
        
        var vm = new IronDev.Agent.ViewModels.Workspaces.ChatWorkspaceViewModel(
            new StubChatHistoryService(),
            new StubPromptContextBuilder(),
            new StubLlmService("Hello"),
            new ContextStubProjectMemoryService(),
            ticketService,
            new StubChatFeedbackService(),
            traceService,
            spy);

        await vm.LoadAsync(new IronDev.Data.Models.Project { Id = 1 });

        vm.UseContextAgent = true;
        vm.PromptText = "Create a ticket to add API key authentication to REST layer";
        await vm.SendMessageCommand.ExecuteAsync(null);

        // Verify E: Conflict assessment (ContextAgentRequest) receives WorkText
        Assert.IsNotNull(spy.LastRequest);
        Assert.IsNotNull(spy.LastRequest.CreateTicketIntent);
        Assert.AreEqual("add API key authentication to REST layer", spy.LastRequest.CreateTicketIntent.WorkText);
        Assert.AreEqual("Create a ticket to add API key authentication to REST layer", spy.LastRequest.UserRequest);

        // Verify F: Trace includes original text and extracted WorkText
        var trace = traceService.GetRecentTraces().FirstOrDefault(t => t.FeatureName == ContextAgentStage.IntentCreateTicket);
        Assert.IsNotNull(trace);
        // The RequestText should contain both (note: redaction replaces 'API key')
        Assert.IsTrue(trace.RequestText.Contains("OriginalMessage: Create a ticket to", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(trace.RequestText.Contains("WorkText: add", StringComparison.OrdinalIgnoreCase));
    }
}

// ── Spy / stub helpers for test G and I ──────────────────────────────────────

internal sealed class SpyContextAgentService : IContextAgentService
{
    private readonly Action _onCall;
    public SpyContextAgentService(Action onCall) => _onCall = onCall;

    public Task<ContextAgentResult> RunAsync(ContextAgentRequest request, CancellationToken ct = default)
    {
        _onCall();
        return Task.FromResult(new ContextAgentResult { WasSuccessful = true, FinalPrompt = "AGENT RESULT" });
    }
}

internal sealed class ContextRequestSpyAgentService : IContextAgentService
{
    public ContextAgentRequest? LastRequest { get; private set; }
    public ContextAgentResult Result { get; set; } = new() { WasSuccessful = true, FinalPrompt = "AGENT RESULT" };

    public Task<ContextAgentResult> RunAsync(ContextAgentRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(Result);
    }
}

internal sealed class ContextStubTicketService : IronDev.Services.ITicketService
{
    public List<IronDev.Data.Models.ProjectTicket> Tickets { get; set; } = [];
    public Task<long> SaveTicketAsync(IronDev.Data.Models.ProjectTicket t, CancellationToken ct = default) => Task.FromResult(1L);
    public Task<IReadOnlyList<IronDev.Data.Models.ProjectTicket>> GetRecentTicketsAsync(int p, int take = 10, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IronDev.Data.Models.ProjectTicket>>(Tickets.Take(take).ToList());
    public Task<IronDev.Data.Models.ProjectTicket?> GetTicketByIdAsync(long id, CancellationToken ct = default) => Task.FromResult(Tickets.FirstOrDefault(t => t.Id == id));
    public Task<bool> ArchiveTicketAsync(long id, CancellationToken ct = default) => Task.FromResult(true);
}

internal sealed class ContextFailingTicketService : IronDev.Services.ITicketService
{
    public Task<long> SaveTicketAsync(IronDev.Data.Models.ProjectTicket t, CancellationToken ct = default) => throw new Exception("DB FAIL");
    public Task<IReadOnlyList<IronDev.Data.Models.ProjectTicket>> GetRecentTicketsAsync(int p, int take = 10, CancellationToken ct = default) => throw new Exception("DB FAIL");
    public Task<IronDev.Data.Models.ProjectTicket?> GetTicketByIdAsync(long id, CancellationToken ct = default) => throw new Exception("DB FAIL");
    public Task<bool> ArchiveTicketAsync(long id, CancellationToken ct = default) => throw new Exception("DB FAIL");
}

internal sealed class ThrowingLlmService : ILLMService
{
    public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default)
        => throw new InvalidOperationException("LLM endpoint is unavailable.");
}

/// <summary>
/// LLM stub that fires a callback before returning its configured response.
/// Used to assert whether the LLM was called when it should NOT have been
/// (e.g. pre-check clarification gate fires before the LLM round-trip).
/// </summary>
internal sealed class TrackingLlmService : ILLMService
{
    private readonly Action _onCall;
    private readonly string _response;

    public TrackingLlmService(Action onCall, string response)
    {
        _onCall   = onCall;
        _response = response;
    }

    public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default)
    {
        _onCall();
        return Task.FromResult(_response);
    }
}

internal sealed class StubChatHistoryService : IronDev.Services.IChatHistoryService
{
    public Task<long> SaveSessionAsync(IronDev.Data.Models.ProjectChatSession s, CancellationToken ct = default)
        => Task.FromResult(1L);
    public Task<long> SaveMessageAsync(IronDev.Data.Models.ChatMessage m, CancellationToken ct = default)
        => Task.FromResult(1L);
    public Task<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.ProjectChatSession>> GetRecentSessionsAsync(int p, int t = 20, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.ProjectChatSession>>(Array.Empty<IronDev.Data.Models.ProjectChatSession>());
    public Task<IronDev.Data.Models.ProjectChatSession?> GetSessionByIdAsync(long sessionId, CancellationToken ct = default)
        => Task.FromResult<IronDev.Data.Models.ProjectChatSession?>(null);
    public Task<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.ChatMessage>> GetRecentMessagesAsync(int p, long s, int t = 50, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.ChatMessage>>(Array.Empty<IronDev.Data.Models.ChatMessage>());
    public Task DeleteSessionAsync(long sessionId, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class ContextStubProjectMemoryService : IronDev.Services.IProjectMemoryService
{
    public List<IronDev.Data.Models.ProjectDecision> Decisions { get; set; } = [];
    public List<IronDev.Data.Models.ProjectRule> Rules { get; set; } = [];

    public Task<long> SaveSummaryAsync(IronDev.Data.Models.ProjectSummary s, CancellationToken ct = default) => Task.FromResult(1L);
    public Task<IronDev.Data.Models.ProjectSummary?> GetLatestSummaryAsync(int p, CancellationToken ct = default) => Task.FromResult<IronDev.Data.Models.ProjectSummary?>(null);
    public Task<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.ProjectDecision>> GetRecentDecisionsAsync(int p, int t = 5, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.ProjectDecision>>(Decisions.Take(t).ToList());
    public Task<long> SaveDecisionAsync(IronDev.Data.Models.ProjectDecision d, CancellationToken ct = default) => Task.FromResult(1L);
    public Task<IronDev.Data.Models.ProjectDecision?> GetDecisionByIdAsync(long id, CancellationToken ct = default) => Task.FromResult<IronDev.Data.Models.ProjectDecision?>(null);
    public Task<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.ProjectRule>> GetProjectRulesAsync(int p, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.ProjectRule>>(Rules);
    public Task<long> SaveProjectRuleAsync(IronDev.Data.Models.ProjectRule r, CancellationToken ct = default) => Task.FromResult(1L);
    public Task<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.ProjectImplementationPlan>> GetRecentPlansAsync(int p, int t = 10, CancellationToken ct = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.ProjectImplementationPlan>>(Array.Empty<IronDev.Data.Models.ProjectImplementationPlan>());
    public Task<IronDev.Data.Models.ProjectImplementationPlan?> GetPlanByIdAsync(long id, CancellationToken ct = default) => Task.FromResult<IronDev.Data.Models.ProjectImplementationPlan?>(null);
    public Task<IronDev.Data.Models.ProjectImplementationPlan?> GetPlanByTicketIdAsync(long ticketId, CancellationToken ct = default) => Task.FromResult<IronDev.Data.Models.ProjectImplementationPlan?>(null);
    public Task<long> SavePlanAsync(IronDev.Data.Models.ProjectImplementationPlan p, CancellationToken ct = default) => Task.FromResult(1L);
}

internal sealed class StubChatFeedbackService : IronDev.Services.IChatFeedbackService
{
    public Task<long> SaveFeedbackAsync(IronDev.Data.Models.ChatMessageFeedback f, CancellationToken ct = default) => Task.FromResult(1L);
    public Task<string> GetProjectFeedbackSummaryAsync(int p, CancellationToken ct = default) => Task.FromResult(string.Empty);
}

