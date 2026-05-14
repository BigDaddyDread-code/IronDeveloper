using System;
using System.Collections.Generic;
using System.Linq;
using IronDev.Core.Models;
using IronDev.Data.Models;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// Parses chat intents, specifically for detecting ticket creation
/// without polluting domain/conflict assessment with generic commands.
/// </summary>
public static class ChatIntentParser
{
    private static readonly string[] CreateTicketCommands =
    [
        "create a ticket to ",
        "create a ticket for ",
        "create a ticket ",
        "make a ticket to ",
        "make a ticket for ",
        "make a ticket ",
        "raise a ticket to ",
        "raise a ticket for ",
        "raise a ticket ",
        "turn this into a ticket",
        "create task to ",
        "create task for ",
        "create task ",
        "add this as a ticket",
        "ticket this"
    ];

    public static CreateTicketIntent? ParseCreateTicket(string request, string? previousMessage = null)
    {
        if (string.IsNullOrWhiteSpace(request)) return null;

        var lower = request.ToLowerInvariant().Trim();

        foreach (var cmd in CreateTicketCommands)
        {
            if (lower.StartsWith(cmd) || lower == cmd.TrimEnd())
            {
                var workText = string.Empty;
                if (lower.Length > cmd.Length)
                {
                    workText = request.Substring(cmd.Length).Trim();
                }
                
                // If it's just "ticket this" or "turn this into a ticket", use previous message
                if (string.IsNullOrWhiteSpace(workText) && !string.IsNullOrWhiteSpace(previousMessage))
                {
                    workText = previousMessage;
                    return new CreateTicketIntent
                    {
                        Intent = "CreateTicket",
                        Confidence = 1.0,
                        CommandText = request, // The whole request is the command
                        WorkText = workText,
                        RequiresClarification = false
                    };
                }

                // Normal extraction
                if (!string.IsNullOrWhiteSpace(workText))
                {
                    // Find original case command text
                    var originalCmd = request.Substring(0, cmd.Length);
                    return new CreateTicketIntent
                    {
                        Intent = "CreateTicket",
                        Confidence = 1.0,
                        CommandText = originalCmd.Trim(),
                        WorkText = workText,
                        RequiresClarification = false
                    };
                }
            }
        }

        return null;
    }
}
