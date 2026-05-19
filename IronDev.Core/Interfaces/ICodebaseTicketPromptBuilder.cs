using IronDev.Core.Builder;
using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

/// <summary>
/// Assembles the structured LLM prompt for codebase ticket generation
/// from a project snapshot and project memory inputs.
/// </summary>
public interface ICodebaseTicketPromptBuilder
{
    /// <summary>
    /// Builds the full prompt string to send to the LLM.
    /// </summary>
    string Build(CodebaseTicketPromptInputs inputs);
}

/// <summary>
/// Inputs required by <see cref="ICodebaseTicketPromptBuilder"/>.
/// </summary>
public sealed class CodebaseTicketPromptInputs
{
    public required CodexProjectSnapshot  Snapshot        { get; init; }
    public string?                        ProjectSummary  { get; init; }
    public IReadOnlyList<string>          RecentDecisions { get; init; } = [];
    public IReadOnlyList<string>          ProjectRules    { get; init; } = [];
}
