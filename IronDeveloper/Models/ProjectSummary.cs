using System;

namespace IronDev.Agent.Models;

/// <summary>
/// Lightweight project summary used in ProjectHub tiles and ProjectOverview.
/// </summary>
public sealed class ProjectSummary
{
    public string Name        { get; init; } = string.Empty;
    public string LocalPath   { get; init; } = string.Empty;
    public string Model       { get; init; } = "gpt-4o";
    public string Status      { get; init; } = "Ready";
    public DateTime LastOpened { get; init; } = DateTime.UtcNow;
    public string Description { get; init; } = string.Empty;
}
