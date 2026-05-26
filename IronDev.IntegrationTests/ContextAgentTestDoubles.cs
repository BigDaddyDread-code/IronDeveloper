using IronDev.AI;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Services;
using IronDev.Services;

namespace IronDev.IntegrationTests;

internal sealed class StubPromptContextBuilder : IPromptContextBuilder
{
    private readonly ChatContextPacket _packet;

    public StubPromptContextBuilder(ChatContextPacket? packet = null)
    {
        _packet = packet ?? new ChatContextPacket
        {
            Intent = ChatIntent.CodeQuery,
            FormattedPrompt = "STUB PROMPT"
        };
    }

    public Task<string> BuildAsync(int projectId, long sessionId, string userRequest, CancellationToken ct = default) =>
        Task.FromResult(_packet.FormattedPrompt);

    public Task<ChatContextPacket> BuildPacketAsync(int projectId, long sessionId, string userRequest, CancellationToken ct = default) =>
        Task.FromResult(_packet);

    public Task<PromptPreviewResult> BuildFullPromptForTestingAsync(int projectId, string userMessage, CancellationToken ct = default) =>
        Task.FromResult(new PromptPreviewResult { PromptText = _packet.FormattedPrompt });
}

internal sealed class StubCodeIndexService : ICodeIndexService
{
    private readonly IReadOnlyList<CodeIndexEntry> _snippets;

    public StubCodeIndexService(IEnumerable<CodeIndexEntry>? snippets = null)
    {
        _snippets = (snippets ?? []).ToList();
    }

    public List<string> ReceivedQueries { get; } = [];

    public Dictionary<string, string> Files { get; } = [];

    public Task<IReadOnlyList<CodeIndexEntry>> GetRelevantSnippetsAsync(
        int projectId,
        string query,
        int take = 10,
        CancellationToken ct = default)
    {
        ReceivedQueries.Add(query);
        return Task.FromResult<IReadOnlyList<CodeIndexEntry>>(_snippets.Take(take).ToList());
    }

    public Task<CodeIndexResult> IndexDirectoryAsync(int projectId, string directoryPath, CancellationToken ct = default) =>
        Task.FromResult(new CodeIndexResult());

    public Task<IReadOnlyList<ProjectFile>> SearchFilesAsync(int projectId, string query, int take = 5, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProjectFile>>([]);

    public Task<ProjectFile?> GetByPathAsync(int projectId, string filePath, CancellationToken ct = default)
    {
        if (Files.TryGetValue(filePath, out var content))
            return Task.FromResult<ProjectFile?>(new ProjectFile { FilePath = filePath, Content = content });

        return Task.FromResult<ProjectFile?>(null);
    }

    public Task<IReadOnlyList<ProjectFile>> GetRecentFilesAsync(int projectId, int take = 20, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProjectFile>>([]);

    public Task<IReadOnlyList<CodeIndexEntry>> GetSymbolsAsync(long fileId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CodeIndexEntry>>([]);

    public Task<int> GetIndexedFileCountAsync(int projectId, CancellationToken ct = default) =>
        Task.FromResult(0);
}

internal sealed class StubRouteJudge : IContextAgentRouteJudge
{
    public Task<ContextAgentRouteDecision> DecideRouteAsync(
        ContextAgentRouteRequest request,
        CancellationToken ct = default)
    {
        var lower = request.UserRequest.ToLowerInvariant();
        var decision = new ContextAgentRouteDecision
        {
            RequestKind = ContextRequestKind.InspectCode,
            Confidence = 1.0,
            AllowCodeSearch = true,
            AllowDeepLookup = true,
            AllowConflictAssessment = false,
            AllowConflictBlocking = false,
            AllowTicketCreation = false,
            RelatedTicketsAreContextOnly = true,
            OriginalUserRequest = request.UserRequest,
            EffectiveWorkText = request.UserRequest
        };

        if (lower.Contains("verify", StringComparison.Ordinal) || lower.Contains("check whether", StringComparison.Ordinal))
            decision.RequestKind = ContextRequestKind.VerifyImplementation;
        else if (lower.Contains("create", StringComparison.Ordinal) ||
                 lower.Contains("ticket", StringComparison.Ordinal) ||
                 lower.Contains("implementation", StringComparison.Ordinal) ||
                 lower.Contains("replace", StringComparison.Ordinal) ||
                 lower.Contains("add", StringComparison.Ordinal) ||
                 lower.Contains("fix", StringComparison.Ordinal))
        {
            decision.RequestKind = ContextRequestKind.CreateTicket;
            decision.AllowConflictBlocking = true;
            decision.AllowConflictAssessment = true;
        }
        else if (lower.Contains("inspect", StringComparison.Ordinal) || lower.Contains("check", StringComparison.Ordinal))
        {
            decision.RequestKind = ContextRequestKind.InspectCode;
        }

        decision.DeepLookupTargets = IdentifyTargets(lower);
        return Task.FromResult(decision);
    }

    private static IReadOnlyList<DeepLookupTarget> IdentifyTargets(string lower)
    {
        if (!lower.Contains("soft archive", StringComparison.Ordinal) &&
            !lower.Contains("archive ticket", StringComparison.Ordinal))
            return [];

        return
        [
            new DeepLookupTarget { FilePath = "TicketService.cs", SymbolName = "ArchiveTicketAsync", ProofPattern = "Body" },
            new DeepLookupTarget { FilePath = "TicketService.cs", SymbolName = "GetRecentTicketsAsync", ProofPattern = "IsDeleted filter" },
            new DeepLookupTarget { FilePath = "DataModels.cs", SymbolName = "ProjectTicket", ProofPattern = "IsDeleted property" }
        ];
    }
}

internal static class ContextAgentFactory
{
    public static (ContextAgentService agent, StubLlmService llm, StubCodeIndexService index, LlmTraceService traces) Build(
        ChatContextPacket? packet = null,
        IEnumerable<CodeIndexEntry>? snippets = null,
        params string[] llmResponses)
    {
        var llm = new StubLlmService(llmResponses);
        var index = new StubCodeIndexService(snippets);
        var traces = new LlmTraceService();

        var agent = new ContextAgentService(
            new StubPromptContextBuilder(packet),
            index,
            llm,
            traces,
            routeJudge: new StubRouteJudge());

        return (agent, llm, index, traces);
    }

    public static (ContextAgentService agent, StubLlmService llm, StubCodeIndexService index, LlmTraceService traces) BuildWithConflict(
        ChatContextPacket? packet = null,
        IEnumerable<CodeIndexEntry>? snippets = null,
        string? llmResponse = null)
    {
        var llm = new StubLlmService(llmResponse
            ?? """{"isSufficient":true,"confidence":9,"reason":"Enough context.","requestedContext":{"codeSearchQueries":[],"clarificationQuestions":[]}}""");
        var index = new StubCodeIndexService(snippets);
        var traces = new LlmTraceService();

        var agent = new ContextAgentService(
            new StubPromptContextBuilder(packet),
            index,
            llm,
            traces,
            new ContextConflictService(),
            routeJudge: new StubRouteJudge());

        return (agent, llm, index, traces);
    }
}
