using System;

namespace IronDev.Agent.Models;

/// <summary>
/// Represents a single chat message entry for display in the Chat workspace.
/// </summary>
public sealed class ChatSummary
{
    public string Role      { get; init; } = "assistant"; // "user" | "assistant"
    public string Content   { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
