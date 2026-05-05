using System.Collections.Generic;

namespace IronDev.Core.Builder;

/// <summary>
/// A ticket draft generated from the project's own codebase context.
/// Matches the structure expected by the existing draft review/import flow.
/// </summary>
public sealed class CodebaseTicketDraft
{
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string AcceptanceCriteria { get; set; } = string.Empty;
    public string Priority { get; set; } = "Medium";
    public string TicketType { get; set; } = "Task";
    
    // Sub-fields for tests convention
    public string UnitTests { get; set; } = string.Empty;
    public string IntegrationTests { get; set; } = string.Empty;
    public string ManualTests { get; set; } = string.Empty;
    public string RegressionTests { get; set; } = string.Empty;
    public string BuildValidation { get; set; } = "dotnet build";
}

/// <summary>
/// Encapsulates a request to generate codebase-wide improvement tickets.
/// </summary>
public sealed class CodebaseTicketGenerationRequest
{
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string? ProjectContext { get; set; }
}

/// <summary>
/// The result of a codebase ticket generation run.
/// </summary>
public sealed class CodebaseTicketGenerationResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public List<CodebaseTicketDraft> Drafts { get; set; } = [];
}
