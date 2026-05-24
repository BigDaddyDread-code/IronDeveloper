using System;
using CommunityToolkit.Mvvm.ComponentModel;
using IronDev.Client.Prompting;

namespace IronDev.Agent.Models;

/// <summary>
/// Represents a single chat message entry for display in the Chat workspace.
/// </summary>
public sealed partial class ChatSummary : ObservableObject
{
    [ObservableProperty] private string _role = "assistant"; // "user" | "assistant"
    [ObservableProperty] private string _messageText = string.Empty;
    [ObservableProperty] private string _formattedPrompt = string.Empty;
    [ObservableProperty] private DateTime _timestamp = DateTime.UtcNow;

    /// <summary>Persisted ChatMessage.Id, set after the message is saved to the database.</summary>
    [ObservableProperty] private long _persistedMessageId;

    public bool HasPersistedMessage => PersistedMessageId > 0;

    public ChatContextPacket? ContextPacket { get; init; }

    public bool HasContext => ContextPacket != null
                              && (ContextPacket.Snippets.Count > 0
                                  || ContextPacket.Tickets.Count > 0
                                  || ContextPacket.Decisions.Count > 0);

    public string ContextHeader => ContextPacket == null
        ? string.Empty
        : $"Context Used: {ContextPacket.Snippets.Count} snippets, {ContextPacket.Tickets.Count} tickets, {ContextPacket.Decisions.Count} decisions";

    // Feedback state for assistant messages.
    /// <summary>"Useful", "Weak", or null when not yet rated.</summary>
    [ObservableProperty] private string? _feedbackRating;

    /// <summary>Reason selected after clicking Useful or Weak.</summary>
    [ObservableProperty] private string? _feedbackReason;

    /// <summary>True once feedback has been persisted. Collapses the rating buttons.</summary>
    [ObservableProperty] private bool _feedbackSaved;

    /// <summary>True when the reason picker is open while waiting for a rating reason.</summary>
    [ObservableProperty] private bool _awaitingReason;

    partial void OnPersistedMessageIdChanged(long value)
    {
        OnPropertyChanged(nameof(HasPersistedMessage));
    }
}
