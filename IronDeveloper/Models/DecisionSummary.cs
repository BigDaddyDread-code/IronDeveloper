using System;

namespace IronDev.Agent.Models;

/// <summary>
/// Lightweight decision record used in the Decisions workspace.
/// </summary>
public sealed class DecisionSummary
{
    public long   Id        { get; init; }
    public string Title     { get; init; } = string.Empty;
    public string Detail    { get; init; } = string.Empty;
    public string Rationale { get; init; } = string.Empty;
    public DateTime CapturedDate { get; init; } = DateTime.UtcNow;
}
