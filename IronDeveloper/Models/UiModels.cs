using System;

namespace IronDev.Agent.Models;

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
    public DateTime CreatedDate { get; set; }
}
