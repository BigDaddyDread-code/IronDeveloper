using System.Text.Json;
using IronDev.Core;
using IronDev.Core.Builder;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// LLM-backed test authoring for skeleton runs. The prompt is built exclusively from
/// the request's requirement surface — the contract has no field for the builder's
/// diff, and this implementation adds none. Failures degrade explicitly: the caller
/// reports authoring as skipped rather than silently shipping a run with no tests.
/// </summary>
public sealed class SkeletonTestAuthoringService : ISkeletonTestAuthoringService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILLMService _llm;

    public SkeletonTestAuthoringService(ILLMService llm) => _llm = llm;

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

        string response;
        try
        {
            response = await _llm.GetResponseAsync(BuildPrompt(request), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SkeletonTestAuthoringResult
            {
                Succeeded = false,
                FailureReason = $"Test authoring model call failed: {exception.Message}"
            };
        }

        return TryParse(response);
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
