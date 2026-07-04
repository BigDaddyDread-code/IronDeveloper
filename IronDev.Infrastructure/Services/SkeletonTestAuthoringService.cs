using System.Text.Json;
using IronDev.Core;
using IronDev.Core.Agents;
using IronDev.Core.Builder;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// LLM-backed test authoring for skeleton runs. The prompt is built exclusively from
/// the request's requirement surface — the contract has no field for the builder's
/// diff, and this implementation adds none. Failures degrade explicitly: the caller
/// reports authoring as skipped rather than silently shipping a run with no tests.
///
/// AG-2: the Tester runs on whatever model its profile configures, and its profile's
/// personality/skill frame the prompt — but the code-owned body below (the blind-by-
/// contract requirement surface and the strict JSON contract) always comes last and
/// always wins. A skill.md can change the tester's phrasing; it cannot hand the tester
/// the builder's code or loosen the output contract.
/// </summary>
public sealed class SkeletonTestAuthoringService : ISkeletonTestAuthoringService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IAgentLlmResolver _llmResolver;
    private readonly ISkeletonAgentProfileService _profiles;

    public SkeletonTestAuthoringService(IAgentLlmResolver llmResolver, ISkeletonAgentProfileService profiles)
    {
        _llmResolver = llmResolver;
        _profiles = profiles;
    }

    public async Task<SkeletonTestAuthoringResult> AuthorTestsAsync(SkeletonTestAuthoringRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.AcceptanceCriteria))
        {
            return new SkeletonTestAuthoringResult
            {
                Succeeded = false,
                FailureReason = "The ticket has no acceptance criteria to author tests from."
            };
        }

        var agent = await _llmResolver.ResolveAsync(SkeletonAgentRole.Tester, cancellationToken).ConfigureAwait(false);
        var profile = await _profiles.GetAsync(SkeletonAgentRole.Tester, cancellationToken).ConfigureAwait(false);
        var prompt = SkeletonAgentPromptComposer.Compose(profile, BuildPrompt(request));

        string response;
        try
        {
            response = await agent.Llm.GetResponseAsync(prompt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SkeletonTestAuthoringResult
            {
                Succeeded = false,
                FailureReason = $"Test authoring model call failed: {exception.Message}",
                ModelProvider = agent.Provider,
                ModelName = agent.Model
            };
        }

        return TryParse(response) with { ModelProvider = agent.Provider, ModelName = agent.Model };
    }

    private static string BuildPrompt(SkeletonTestAuthoringRequest request) =>
        $$"""
        You are a software tester. Write automated test files that verify the acceptance
        criteria below. You are deliberately NOT shown the implementation: derive every
        assertion from the requirements, not from any code.

        Ticket: {{request.TicketTitle}}
        Problem: {{request.Problem}}
        Acceptance criteria:
        {{request.AcceptanceCriteria}}

        Rules:
        - MSTest (C#), one test class per criterion where practical.
        - Every test file must map to exactly one criterion.
        - coversCriterion must QUOTE the criterion line exactly as given above —
          it is matched against the criteria to build the coverage record.
        - Relative paths only, under a "tests/" folder.
        - Respond with ONLY a JSON array, no prose, no code fences:
          [{"relativePath":"tests/...Tests.cs","content":"...","coversCriterion":"..."}]
        """;

    /// <summary>Parses the model response. Tolerates code fences; anything else fails explicitly.</summary>
    public static SkeletonTestAuthoringResult TryParse(string response)
    {
        var text = response.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                text = text[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            var tests = JsonSerializer.Deserialize<List<SkeletonAuthoredTest>>(text, JsonOptions) ?? [];
            var valid = tests
                .Where(test => !string.IsNullOrWhiteSpace(test.RelativePath) &&
                               !string.IsNullOrWhiteSpace(test.Content) &&
                               !Path.IsPathRooted(test.RelativePath))
                .ToList();

            if (valid.Count == 0)
            {
                return new SkeletonTestAuthoringResult
                {
                    Succeeded = false,
                    FailureReason = "The model response contained no usable test files."
                };
            }

            return new SkeletonTestAuthoringResult { Succeeded = true, Tests = valid };
        }
        catch (JsonException exception)
        {
            return new SkeletonTestAuthoringResult
            {
                Succeeded = false,
                FailureReason = $"Test authoring response was not valid JSON: {exception.Message}"
            };
        }
    }
}
