using System;

namespace IronDev.Agent.Models;

/// <summary>
/// Carries the chat context needed to generate a DraftTicket.
/// Populated by ChatWorkspaceViewModel and passed to the shell bridge.
/// Not persisted.
/// </summary>
public sealed class ChatTicketContext
{
    public long   SessionId       { get; set; }
    public long   MessageId       { get; set; }
    public string MessageText     { get; set; } = string.Empty;
    public string ProposedTitle   { get; set; } = string.Empty;
    public string? LinkedFilePaths { get; set; }
    public string? LinkedSymbols   { get; set; }
}

public sealed class ChatMessageItem
{
    public string Role { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
}

public sealed class DecisionItem
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? Category { get; set; }
    public string Status { get; set; } = "Accepted";
    public string? LinkedFilePaths { get; set; }
    public string? LinkedCodeIndexEntryIds { get; set; }
    public string? LinkedSymbols { get; set; }
    public DateTime CreatedDate { get; set; }
}

public sealed class PlanItem
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string? Scope { get; set; }
    public string? ProposedSteps { get; set; }
    public string? AffectedContext { get; set; }
    public string? RisksNotes { get; set; }
    public string Status { get; set; } = "Draft";
    public string? LinkedFilePaths { get; set; }
    public string? LinkedCodeIndexEntryIds { get; set; }
    public string? LinkedSymbols { get; set; }
    public DateTime CreatedDate { get; set; }
}

public sealed class TicketItem
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TicketType { get; set; } = "Task";
    public string Priority { get; set; } = "Medium";
    public string? Summary { get; set; }
    public string? Background { get; set; }
    public string? Problem { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public string? TechnicalNotes { get; set; }
    public string Status { get; set; } = "Draft";
    public string Content { get; set; } = string.Empty;
    public string? LinkedFilePaths { get; set; }
    public string? LinkedCodeIndexEntryIds { get; set; }
    public string? LinkedSymbols { get; set; }
    public string? UnitTests { get; set; }
    public string? IntegrationTests { get; set; }
    public string? ManualTests { get; set; }
    public string? RegressionTests { get; set; }
    public string? BuildValidation { get; set; }
    public string? ContextSummary { get; set; }
    public bool IsGenerated { get; set; }
    public string? GenerationNote { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedDate { get; set; }
    public bool IsDraft { get; set; }
}
