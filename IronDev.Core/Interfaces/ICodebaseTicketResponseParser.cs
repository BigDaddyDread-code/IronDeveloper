using System.Collections.Generic;
using IronDev.Core.Builder;

namespace IronDev.Core.Interfaces;

/// <summary>
/// Parses and cleans the raw LLM response for codebase ticket generation,
/// returning a list of typed draft objects ready for grounding validation.
/// </summary>
public interface ICodebaseTicketResponseParser
{
    /// <summary>
    /// Strips markdown fences from <paramref name="rawResponse"/>,
    /// deserialises the JSON, and returns the ordered draft list.
    /// </summary>
    /// <exception cref="System.Text.Json.JsonException">
    /// Thrown when the response cannot be parsed as valid ticket JSON.
    /// </exception>
    IReadOnlyList<CodebaseTicketDraft> Parse(string rawResponse);
}
