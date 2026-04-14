using System;

namespace IronDev.Agent.Models;

public sealed record StatusMessage(string Status);

/// <summary>Sent when a new ticket is generated and ready for preview.</summary>
public sealed record TicketPreviewMessage(TicketItem Ticket);

/// <summary>Sent after a ticket is saved to refresh the left panel.</summary>
public sealed record TicketSavedMessage(long TicketId);

/// <summary>Sent when a ticket is clicked in the left panel to display its structured content.</summary>
public sealed record TicketSelectedMessage(TicketItem Ticket);

/// <summary>Sent after a decision is saved to refresh the left panel.</summary>
public sealed record DecisionSavedMessage();

/// <summary>Sent after the auto-summarizer updates the project summary.</summary>
public sealed record SummaryUpdatedMessage();

/// <summary>Sent to trigger the Code Workbench window opening for a specific ticket.</summary>
public sealed record OpenWorkbenchMessage(long TicketId);
