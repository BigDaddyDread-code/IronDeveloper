using System;

namespace IronDev.Agent.Models;

/// <summary>
/// Lightweight implementation plan record used in the Plans workspace.
/// </summary>
public sealed class ImplementationPlanSummary
{
    public long   Id        { get; init; }
    public string Title     { get; init; } = string.Empty;
    public string Goal      { get; init; } = string.Empty;
    public DateTime CreatedDate { get; init; } = DateTime.UtcNow;
}
