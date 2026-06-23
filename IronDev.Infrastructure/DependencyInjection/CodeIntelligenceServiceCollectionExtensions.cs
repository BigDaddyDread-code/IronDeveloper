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
using System.Reflection;

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
            return WeaviateAuthConfigValidator.ResolveOptionsOrThrow(config);
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
        services.AddTransient<ISemanticMemoryEvidenceProvider, SemanticMemoryEvidenceProvider>();
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

public sealed class WeaviateAuthConfigResolution
{
    public WeaviateAuthConfigResolution(
        WeaviateOptions options,
        WeaviateAuthConfigValidationResult validation)
    {
        Options = options;
        Validation = validation;
    }

    public WeaviateOptions Options { get; }
    public WeaviateAuthConfigValidationResult Validation { get; }

    public override string ToString() =>
        $"WeaviateAuthConfigResolution {{ Validation = {Validation} }}";
}

public static class WeaviateAuthConfigValidator
{
    public const string IronDevWeaviateApiKeyEnvironmentVariableName = "IRONDEV_WEAVIATE_API_KEY";
    public const string StartupValidationFailedMessage =
        "Weaviate auth configuration is invalid. Production-like enabled Weaviate requires HTTPS, a non-local endpoint, and an API key from safe configuration.";
    public const int MinimumApiKeyLength = 16;

    public static WeaviateOptions ResolveOptionsOrThrow(IConfiguration configuration) =>
        ResolveOptionsOrThrow(configuration, environmentName: null, Environment.GetEnvironmentVariable);

    public static WeaviateOptions ResolveOptionsOrThrow(
        IConfiguration configuration,
        string? environmentName,
        Func<string, string?> environmentVariableReader)
    {
        var resolution = Resolve(configuration, environmentName, environmentVariableReader);
        if (resolution.Validation.Valid)
            return resolution.Options;

        throw new InvalidOperationException(StartupValidationFailedMessage);
    }

    public static WeaviateAuthConfigResolution Resolve(
        IConfiguration configuration,
        string? environmentName = null,
        Func<string, string?>? environmentVariableReader = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        environmentVariableReader ??= Environment.GetEnvironmentVariable;

        var resolvedEnvironmentName = ResolveEnvironmentName(configuration, environmentName, environmentVariableReader);
        var productionLike = IsProductionLikeEnvironment(resolvedEnvironmentName);
        var options = configuration.GetSection("Weaviate").Get<WeaviateOptions>() ?? new WeaviateOptions();
        var issues = new List<string>();
        var apiKeySource = ResolveApiKey(configuration, options, environmentVariableReader, issues);
        var endpointClassification = ClassifyEndpoint(options.Endpoint, out var endpointUri);

        if (apiKeySource != WeaviateAuthSource.Missing)
            ValidateApiKey(options.ApiKey, issues);

        if (!options.Enabled)
        {
            return new WeaviateAuthConfigResolution(
                options,
                BuildValidation(options, resolvedEnvironmentName, productionLike, apiKeySource, endpointClassification, issues));
        }

        if (endpointClassification is WeaviateEndpointClassification.Missing or WeaviateEndpointClassification.Invalid || endpointUri is null)
        {
            issues.Add("WeaviateEndpointInvalid");
        }
        else if (productionLike)
        {
            if (endpointClassification == WeaviateEndpointClassification.Localhost)
                issues.Add("WeaviateProductionEndpointCannotBeLocalhost");

            if (!string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                issues.Add("WeaviateProductionEndpointMustUseHttps");

            if (apiKeySource == WeaviateAuthSource.Missing)
                issues.Add("WeaviateProductionApiKeyRequired");
        }
        else
        {
            var localAnonymousAllowed =
                endpointClassification == WeaviateEndpointClassification.Localhost &&
                string.Equals(endpointUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                apiKeySource == WeaviateAuthSource.Missing;

            if (!localAnonymousAllowed)
            {
                if (!string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                    endpointClassification != WeaviateEndpointClassification.Localhost)
                {
                    issues.Add("WeaviateNonLocalEndpointMustUseHttps");
                }

                if (endpointClassification != WeaviateEndpointClassification.Localhost &&
                    apiKeySource == WeaviateAuthSource.Missing)
                {
                    issues.Add("WeaviateNonLocalEndpointRequiresApiKey");
                }
            }
        }

        return new WeaviateAuthConfigResolution(
            options,
            BuildValidation(options, resolvedEnvironmentName, productionLike, apiKeySource, endpointClassification, issues));
    }

    private static WeaviateAuthConfigValidationResult BuildValidation(
        WeaviateOptions options,
        string environmentName,
        bool productionLike,
        WeaviateAuthSource apiKeySource,
        WeaviateEndpointClassification endpointClassification,
        IReadOnlyList<string> issues) =>
        new(
            options.Enabled,
            environmentName,
            productionLike,
            apiKeySource,
            endpointClassification,
            issues.Count == 0,
            issues.ToArray());

    private static string ResolveEnvironmentName(
        IConfiguration configuration,
        string? environmentName,
        Func<string, string?> environmentVariableReader)
    {
        if (!string.IsNullOrWhiteSpace(environmentName))
            return environmentName.Trim();

        return configuration["ASPNETCORE_ENVIRONMENT"] ??
            configuration["DOTNET_ENVIRONMENT"] ??
            environmentVariableReader("ASPNETCORE_ENVIRONMENT") ??
            environmentVariableReader("DOTNET_ENVIRONMENT") ??
            "Production";
    }

    private static bool IsProductionLikeEnvironment(string environmentName) =>
        !string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(environmentName, "LocalTest", StringComparison.OrdinalIgnoreCase);

    private static WeaviateAuthSource ResolveApiKey(
        IConfiguration configuration,
        WeaviateOptions options,
        Func<string, string?> environmentVariableReader,
        ICollection<string> issues)
    {
        var configurationCandidate = TryGetConfigurationCandidate(configuration);
        if (configurationCandidate is not null)
        {
            options.ApiKey = configurationCandidate.Value.Key;
            ValidateConfigurationSource(configurationCandidate.Value.Provider, issues);
            return WeaviateAuthSource.Configuration;
        }

        var environmentApiKey = environmentVariableReader(IronDevWeaviateApiKeyEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentApiKey))
        {
            options.ApiKey = environmentApiKey;
            return WeaviateAuthSource.IronDevWeaviateApiKeyEnvironment;
        }

        options.ApiKey = string.Empty;
        return WeaviateAuthSource.Missing;
    }

    private static WeaviateConfigurationCandidate? TryGetConfigurationCandidate(IConfiguration configuration)
    {
        if (configuration is not IConfigurationRoot root)
            return null;

        foreach (var provider in root.Providers.Reverse())
        {
            if (provider.TryGet("Weaviate:ApiKey", out var key) && !string.IsNullOrWhiteSpace(key))
                return new WeaviateConfigurationCandidate(key, provider);
        }

        return null;
    }

    private static void ValidateConfigurationSource(
        IConfigurationProvider provider,
        ICollection<string> issues)
    {
        var providerName = provider.GetType().Name;
        if (providerName.Contains("Memory", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("EnvironmentVariables", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (providerName.Contains("Json", StringComparison.OrdinalIgnoreCase) && IsCommittedAppsettingsProvider(provider))
        {
            issues.Add("WeaviateApiKeyCommittedAppsettingsForbidden");
            return;
        }

        issues.Add("WeaviateApiKeySourceUnknown");
    }

    private static bool IsCommittedAppsettingsProvider(IConfigurationProvider provider)
    {
        var source = provider.GetType()
            .GetProperty("Source", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(provider);
        var path = source?.GetType()
            .GetProperty("Path", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(source) as string;

        if (string.IsNullOrWhiteSpace(path))
            return true;

        var fileName = Path.GetFileName(path);
        return string.Equals(fileName, "appsettings.json", StringComparison.OrdinalIgnoreCase) ||
            (fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase) &&
             fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateApiKey(string apiKey, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            issues.Add("WeaviateApiKeyMissing");
            return;
        }

        if (apiKey.Trim().Length < MinimumApiKeyLength)
            issues.Add("WeaviateApiKeyTooShort");

        if (IsPlaceholderApiKey(apiKey))
            issues.Add("WeaviateApiKeyPlaceholder");
    }

    private static bool IsPlaceholderApiKey(string apiKey)
    {
        var normalized = apiKey.Trim().ToLowerInvariant();
        return normalized is
            "your-api-key-here" or
            "local-dev-key" or
            "changeme" or
            "change-me" or
            "test" or
            "api-key" or
            "weaviate-api-key";
    }

    private static WeaviateEndpointClassification ClassifyEndpoint(
        string? endpoint,
        out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(endpoint))
            return WeaviateEndpointClassification.Missing;

        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out uri))
            return WeaviateEndpointClassification.Invalid;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return WeaviateEndpointClassification.Invalid;
        }

        return IsLocalhost(uri)
            ? WeaviateEndpointClassification.Localhost
            : WeaviateEndpointClassification.Remote;
    }

    private static bool IsLocalhost(Uri uri) =>
        string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uri.Host, "[::1]", StringComparison.OrdinalIgnoreCase);

    private readonly record struct WeaviateConfigurationCandidate(
        string Key,
        IConfigurationProvider Provider);
}
