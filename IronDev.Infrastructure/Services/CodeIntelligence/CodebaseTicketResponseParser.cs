using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.CodeIntelligence;

/// <inheritdoc cref="ICodebaseTicketResponseParser"/>
public sealed class CodebaseTicketResponseParser : ICodebaseTicketResponseParser
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<CodebaseTicketDraft> Parse(string rawResponse)
    {
        var cleaned = StripMarkdownFences(rawResponse);

        var wrapper = JsonSerializer.Deserialize<GenerationWrapper>(cleaned, _jsonOptions);

        return (wrapper?.Drafts ?? [])
            .OrderBy(d => d.SuggestedBuildOrder <= 0 ? int.MaxValue : d.SuggestedBuildOrder)
            .ToList();
    }

    /// <summary>
    /// Removes ```json / ``` fences that LLMs commonly wrap JSON responses in.
    /// </summary>
    internal static string StripMarkdownFences(string input)
    {
        var s = input.Trim();

        if (s.StartsWith("```json", System.StringComparison.OrdinalIgnoreCase))
            s = s[7..];
        else if (s.StartsWith("```"))
            s = s[3..];

        if (s.EndsWith("```"))
            s = s[..^3].Trim();

        return s.Trim();
    }

    private sealed class GenerationWrapper
    {
        public List<CodebaseTicketDraft> Drafts { get; set; } = [];
    }
}
