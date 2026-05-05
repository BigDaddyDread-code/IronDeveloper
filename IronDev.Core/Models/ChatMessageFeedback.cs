using System;

namespace IronDev.Data.Models;

/// <summary>
/// Stores per-message feedback for an AI chat response.
/// Persisted in dbo.ChatMessageFeedback.
/// </summary>
public sealed class ChatMessageFeedback
{
    public long    Id            { get; set; }
    public int     TenantId      { get; set; }
    public int     ProjectId     { get; set; }
    public long?   ChatSessionId { get; set; }
    public long    ChatMessageId { get; set; }

    /// <summary>"Useful" or "Weak"</summary>
    public string  Rating        { get; set; } = string.Empty;

    /// <summary>Reason selected from the predefined list, or null.</summary>
    public string? Reason        { get; set; }

    /// <summary>Optional free-text comment (not exposed in MVP UI).</summary>
    public string? Comment       { get; set; }

    public DateTime CreatedDate  { get; set; }
}
