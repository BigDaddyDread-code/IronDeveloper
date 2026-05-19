using IronDev.Core.Interfaces;
using IronDev.Infrastructure.Services.CodeIntelligence;
using Microsoft.Extensions.DependencyInjection;

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

        return services;
    }
}
