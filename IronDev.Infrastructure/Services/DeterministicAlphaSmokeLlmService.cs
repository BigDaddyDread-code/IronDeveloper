using IronDev.Core;
using IronDev.Core.Agents;
using Microsoft.Extensions.Configuration;
using IronDev.Core.RunReadiness;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// D-2a deterministic, model-word-only stand-in for a real LLM.
///
/// Boundary: this provider may fake model words. It must not fake the governed
/// loop, approval, policy satisfaction, receipts, evidence hashes, run state, or
/// human dispositions. Its output is configured fixture text, not authority.
/// </summary>
public sealed class DeterministicAlphaSmokeLlmService : ILLMService
{
    public const string ProviderName = ProjectRunProviders.LocalTestDeterministic;

    private readonly string _response;

    public DeterministicAlphaSmokeLlmService(SkeletonAgentRole role, IConfiguration configuration)
    {
        _response = ResolveResponse(role, configuration);
    }

    public DeterministicAlphaSmokeLlmService(
        SkeletonAgentRole role,
        IReadOnlyDictionary<SkeletonAgentRole, string> responses)
    {
        _response = responses.TryGetValue(role, out var response) ? response : "{}";
    }

    public Task<string> GetResponseAsync(string prompt, CancellationToken ct = default) =>
        Task.FromResult(string.IsNullOrWhiteSpace(_response) ? "{}" : _response);

    private static string ResolveResponse(SkeletonAgentRole role, IConfiguration configuration)
    {
        var roleKey = role.ToString();
        var configuredText = FirstNonBlank(
            configuration[$"AlphaSmoke:Responses:{roleKey}"],
            configuration[$"AlphaSmoke:Responses:{roleKey}:Text"]);
        if (!string.IsNullOrWhiteSpace(configuredText))
            return configuredText;

        var configuredFile = configuration[$"AlphaSmoke:Responses:{roleKey}:File"];
        if (!string.IsNullOrWhiteSpace(configuredFile))
            return ReadConfiguredResponseFile(role, configuredFile);

        var responseRoot = configuration["AlphaSmoke:ResponseSetRoot"];
        if (!string.IsNullOrWhiteSpace(responseRoot))
        {
            var responsePath = Path.Combine(responseRoot, $"{role.ToString().ToLowerInvariant()}.json");
            return File.Exists(responsePath) ? File.ReadAllText(responsePath) : "{}";
        }

        // If no fixture is configured, answer with an empty object rather than
        // embedding a demo-specific response in runtime infrastructure.
        return "{}";
    }

    private static string ReadConfiguredResponseFile(SkeletonAgentRole role, string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Configured deterministic alpha-smoke response file for role '{role}' was not found.");

        return File.ReadAllText(path);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
