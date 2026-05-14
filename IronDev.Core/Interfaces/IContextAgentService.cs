using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

/// <summary>
/// Agentic RAG pipeline: builds initial context, calls the LLM for a sufficiency
/// check, optionally expands context via code-index tool calls, then returns a
/// final enriched prompt.
///
/// Design constraints:
/// - Maximum 1 expansion round (v1).
/// - Maximum 2 tool calls per round.
/// - Never calls the real "answer" LLM directly — returns the assembled prompt.
/// - All stages are traced via ILlmTraceService.
/// - UseContextAgent feature flag must be true to use this path.
/// </summary>
public interface IContextAgentService
{
    /// <summary>
    /// Runs the full Context Agent pipeline for a single user request.
    /// Returns a <see cref="ContextAgentResult"/> containing either:
    /// - A final prompt ready for the answer LLM, or
    /// - A clarification request when the LLM needs user input.
    /// </summary>
    Task<ContextAgentResult> RunAsync(ContextAgentRequest request, CancellationToken ct = default);
}
