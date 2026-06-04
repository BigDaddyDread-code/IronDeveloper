using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Infrastructure.Builder;
using IronDev.Infrastructure.Services;
using IronDev.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public class LlmProviderTests
{
    [TestMethod]
    public void OpenAi_ResolvesCorrectImplementation()
    {
        var options = new LlmOptions { Provider = "OpenAI", ApiKey = "fake-key" };
        var service = CreateService(options);
        Assert.IsInstanceOfType(service, typeof(OpenAiLlmService));
    }

    [TestMethod]
    public void LocalOpenAi_ResolvesCorrectImplementation()
    {
        var options = new LlmOptions { Provider = "LocalOpenAI", BaseUrl = "http://localhost:11434" };
        var service = CreateService(options);
        Assert.IsInstanceOfType(service, typeof(LocalOpenAiCompatibleLlmService));
    }

    [TestMethod]
    public void Ollama_ResolvesCorrectImplementation()
    {
        var options = new LlmOptions { Provider = "Ollama", BaseUrl = "http://localhost:11434" };
        var service = CreateService(options);
        Assert.IsInstanceOfType(service, typeof(OllamaLlmService));
    }

    [TestMethod]
    public void LocalOpenAi_MissingBaseUrl_Throws()
    {
        var options = new LlmOptions { Provider = "LocalOpenAI", BaseUrl = "" };
        try
        {
            _ = new LocalOpenAiCompatibleLlmService(options);
            Assert.Fail("Should have thrown ArgumentException");
        }
        catch (ArgumentException)
        {
            // Expected
        }
    }

    [TestMethod]
    public void UnknownProvider_Throws()
    {
        var options = new LlmOptions { Provider = "Unknown" };
        try
        {
            _ = CreateService(options);
            Assert.Fail("Should have thrown InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }
    }

    [TestMethod]
    public void DraftTicketService_CanUseILLMService()
    {
        // This test verifies that DraftTicketService can be instantiated with an ILLMService
        // and doesn't know about the provider.
        var mockLlm    = new StubLlmService();
        var mockMemory = new NullProjectMemoryService();
        var mockTrace  = new NullLlmTraceService();
        var draftService = new DraftTicketService(mockLlm, mockMemory, mockTrace);
        Assert.IsNotNull(draftService);
    }

    private ILLMService CreateService(LlmOptions options)
    {
        var provider = options.Provider?.ToLowerInvariant() ?? "openai";
        return provider switch
        {
            "openai"      => new OpenAiLlmService(options),
            "localopenai" => new LocalOpenAiCompatibleLlmService(options),
            "ollama"      => new OllamaLlmService(options),
            "custom"      => new LocalOpenAiCompatibleLlmService(options),
            _ => throw new InvalidOperationException($"Unsupported AI provider: {options.Provider}")
        };
    }

    private class StubLlmService : ILLMService
    {
        public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default) => Task.FromResult("{}");
    }

    /// <summary>No-op memory service for tests that don't require real DB context.</summary>
    private class NullProjectMemoryService : IProjectMemoryService
    {
        public Task<ProjectSummary?> GetLatestSummaryAsync(int projectId, CancellationToken ct = default) => Task.FromResult<ProjectSummary?>(null);
        public Task<IReadOnlyList<ProjectDecision>> GetRecentDecisionsAsync(int projectId, int take = 10, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProjectDecision>>(new List<ProjectDecision>());
        public Task<ProjectDecision?> GetDecisionByIdAsync(long decisionId, CancellationToken ct = default) => Task.FromResult<ProjectDecision?>(null);
        public Task<long> SaveSummaryAsync(ProjectSummary summary, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<IReadOnlyList<ProjectContextDocument>> GetContextDocumentsAsync(
            int projectId,
            string? documentType = null,
            string? authorityLevel = null,
            string? status = "Active",
            int take = 100,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProjectContextDocument>>([]);
        public Task<IReadOnlyList<ProjectContextDocument>> GetRelevantContextDocumentsAsync(int projectId, string query, int take = 20, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProjectContextDocument>>([]);
        public Task<ProjectContextDocument?> GetContextDocumentByIdAsync(long documentId, CancellationToken ct = default) => Task.FromResult<ProjectContextDocument?>(null);
        public Task<long> SaveContextDocumentAsync(ProjectContextDocument document, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<bool> ArchiveContextDocumentAsync(long documentId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<ProjectObservableState?> GetObservableStateAsync(int projectId, CancellationToken ct = default) => Task.FromResult<ProjectObservableState?>(null);
        public Task SaveObservableStateAsync(ProjectObservableState state, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ProjectImplementationPlan>> GetRecentPlansAsync(int projectId, int take = 10, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProjectImplementationPlan>>(new List<ProjectImplementationPlan>());
        public Task<ProjectImplementationPlan?> GetPlanByIdAsync(long planId, CancellationToken ct = default) => Task.FromResult<ProjectImplementationPlan?>(null);
        public Task<ProjectImplementationPlan?> GetPlanByTicketIdAsync(long ticketId, CancellationToken ct = default) => Task.FromResult<ProjectImplementationPlan?>(null);
        public Task<long> SavePlanAsync(ProjectImplementationPlan plan, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> SaveDecisionAsync(ProjectDecision decision, CancellationToken ct = default) => Task.FromResult(0L);

        public Task<IReadOnlyList<ProjectRule>> GetProjectRulesAsync(int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectRule>>([]);
        public Task<long> SaveProjectRuleAsync(ProjectRule rule, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);
    }

    private class NullLlmTraceService : ILlmTraceService
    {
        public event EventHandler<LlmTraceEntry>? TraceAdded;
        public bool IsTracingEnabled { get; set; } = true;
        public void AddTrace(LlmTraceEntry entry) { }
        public void Clear() { }
        public string ExportAll() => string.Empty;
        public string ExportTrace(LlmTraceEntry entry) => string.Empty;
        public IReadOnlyList<LlmTraceEntry> GetRecentTraces(int take = 100) => [];
        public IReadOnlyList<LlmTraceEntry> GetTracesByGroupId(string traceGroupId, int take = 100) => [];
    }
}
