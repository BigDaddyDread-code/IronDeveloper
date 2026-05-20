using IronDev.Core.Interfaces;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Core.Workflow;
using IronDev.Infrastructure.Services.CodeIntelligence;
using IronDev.Infrastructure.Services.SemanticMemory;
using IronDev.Infrastructure.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace IronDev.Infrastructure.DependencyInjection;

/// <summary>
/// Registers all code-intelligence services — semantic indexers, snapshot builder,
/// quality scorer, grounding validator, and the new prompt/parser helpers.
/// 
/// Call <c>services.AddCodeIntelligenceServices()</c> from the composition root
/// instead of duplicating these registrations inline.
/// </summary>
public static class CodeIntelligenceServiceCollectionExtensions
{
    public static IServiceCollection AddCodeIntelligenceServices(this IServiceCollection services)
    {
        // ── Language indexers (multiple implementations, resolved as IEnumerable<>) ──
        services.AddTransient<ILanguageSemanticIndexer, CSharpStructuralSemanticIndexer>();
        services.AddTransient<ILanguageSemanticIndexer, XamlStructuralSemanticIndexer>();
        services.AddTransient<ILanguageSemanticIndexer, ConfigStructuralSemanticIndexer>();

        // ── Semantic index pipeline ───────────────────────────────────────────
        services.AddTransient<IProjectSemanticIndexService, RoslynProjectSemanticIndexService>();

        // ── Snapshot & quality ────────────────────────────────────────────────
        services.AddSingleton<ICodexContextQualityScorer, CodexContextQualityScorer>();
        services.AddTransient<ICodexSnapshotBuilder, CodexSnapshotBuilder>();

        // ── Grounding & prompt/parse ──────────────────────────────────────────
        services.AddSingleton<ICodexTicketGroundingValidator, CodexTicketGroundingValidator>();
        services.AddSingleton<ICodebaseTicketPromptBuilder, CodebaseTicketPromptBuilder>();
        services.AddSingleton<ICodebaseTicketResponseParser, CodebaseTicketResponseParser>();

        // ── Semantic Memory ──────────────────────────────────────────────────
        services.AddSingleton<IEmbeddingContentExtractor, ContextDocumentEmbeddingContentExtractor>();
        services.AddSingleton<ISemanticChunker, MarkdownAwareSemanticChunker>();
        services.AddSingleton<ISemanticRankingService, SemanticRankingService>();
        services.AddScoped<ISemanticArtefactRepository, SemanticArtefactRepository>();
        services.AddScoped<ISemanticChunkRepository, SemanticChunkRepository>();
        services.AddScoped<IEmbeddingJobRepository, EmbeddingJobRepository>();
        services.AddScoped<ISemanticSearchTraceRepository, SemanticSearchTraceRepository>();

        // Register options
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var options = config.GetSection("Embedding").Get<EmbeddingOptions>() ?? new EmbeddingOptions();
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                options.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
            }
            return options;
        });

        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var options = config.GetSection("Weaviate").Get<WeaviateOptions>() ?? new WeaviateOptions();
            return options;
        });

        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            return config.GetSection("SemanticRanking").Get<SemanticRankingOptions>() ?? new SemanticRankingOptions();
        });

        // Register providers
        services.AddTransient<FakeEmbeddingProvider>();
        services.AddTransient<OpenAiEmbeddingProvider>();
        services.AddTransient<IEmbeddingProvider>(sp =>
        {
            var options = sp.GetRequiredService<EmbeddingOptions>();
            var provider = options.Provider?.ToLowerInvariant() ?? "fake";
            return provider switch
            {
                "openai" => sp.GetRequiredService<OpenAiEmbeddingProvider>(),
                _ => sp.GetRequiredService<FakeEmbeddingProvider>()
            };
        });

        // Register memory services
        services.AddTransient<WeaviateSemanticMemoryService>();
        services.AddTransient<InMemorySemanticMemoryService>();
        services.AddTransient<ISemanticMemoryService>(sp =>
        {
            var options = sp.GetRequiredService<WeaviateOptions>();
            return options.Enabled
                ? sp.GetRequiredService<WeaviateSemanticMemoryService>()
                : sp.GetRequiredService<InMemorySemanticMemoryService>();
        });
        services.AddTransient<ISemanticWorkflowMemoryNode, SemanticWorkflowMemoryNode>();

        // ── LangGraph-style ticket workflow first slice ──────────────────────
        services.AddTransient<IWorkflowNode<TicketBuildWorkflowState>, LoadTicketNode>();
        services.AddTransient<IWorkflowNode<TicketBuildWorkflowState>, CompileKnowledgeContextNode>();
        services.AddTransient<IWorkflowNode<TicketBuildWorkflowState>, CreateImplementationPlanNode>();
        services.AddTransient<IWorkflowNode<TicketBuildWorkflowState>, RequestPlanApprovalNode>();
        services.AddTransient<IWorkflowNode<TicketBuildWorkflowState>, ProposeCodeChangesNode>();
        services.AddTransient<IWorkflowNode<TicketBuildWorkflowState>, RequestCodeApprovalNode>();
        services.AddTransient<ITicketBuildWorkflowOrchestrator, TicketBuildWorkflowOrchestrator>();

        return services;
    }
}
