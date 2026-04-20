using System;

namespace IronDev.Agent.Models;

/// <summary>
/// Represents a single chat message entry for display in the Chat workspace.
/// </summary>
public sealed class ChatSummary
{
    public string Role      { get; init; } = "assistant"; // "user" | "assistant"
    public string MessageText   { get; init; } = string.Empty;
    public string FormattedPrompt { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public global::IronDev.AI.ChatContextPacket? ContextPacket { get; init; }

    public bool HasContext => ContextPacket != null && (ContextPacket.Snippets.Count > 0 || ContextPacket.Tickets.Count > 0 || ContextPacket.Decisions.Count > 0);
    public string ContextHeader => ContextPacket == null ? string.Empty : $"Context Used: {ContextPacket.Snippets.Count} snippets, {ContextPacket.Tickets.Count} tickets, {ContextPacket.Decisions.Count} decisions";
}
