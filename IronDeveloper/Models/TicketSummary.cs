using System;

namespace IronDev.Agent.Models;

/// <summary>
/// Lightweight ticket summary used in list/tile views.
/// </summary>
public sealed class TicketSummary
{
    public long   Id       { get; init; }
    public string Title    { get; init; } = string.Empty;
    public string Type     { get; init; } = "Task";
    public string Priority { get; init; } = "Medium";
    public string Status   { get; init; } = "Draft";
    public DateTime CreatedDate { get; init; } = DateTime.UtcNow;
}
