using System;
using System.Linq;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// Determines the overarching goal of a context agent request to route it safely.
/// Replaces naive intent gating with a robust orchestration decision that controls
/// which stages (conflict assessment, blocking, deep lookup) are permitted.
/// </summary>
public static class ContextAgentRouter
{
    private static readonly string[] InspectionPrefixes =
    [
        "check", "inspect", "what", "look", "explain", "how", "where", "find",
        "show", "why", "does", "is", "are", "can", "review", "verify", "who"
    ];

    private static readonly string[] ChangePrefixes =
    [
        "implement", "replace", "change", "build", "generate", "add", "update",
        "fix", "refactor", "remove", "rewrite", "migrate", "create"
    ];

    public static ContextAgentRouteDecision DetermineRoute(string request, CreateTicketIntent? ticketIntent)
    {
        var lower = (request ?? string.Empty).ToLowerInvariant().Trim();
        var effectiveWorkText = request ?? string.Empty;

        // 1. Explicit Create Ticket Intent
        if (ticketIntent != null)
        {
            return new ContextAgentRouteDecision
            {
                OriginalUserRequest     = request ?? string.Empty,
                EffectiveWorkText       = string.IsNullOrWhiteSpace(ticketIntent.WorkText) ? (request ?? string.Empty) : ticketIntent.WorkText,
                RequestKind             = ContextRequestKind.CreateTicket,
                Confidence              = 1.0,
                Reason                  = "Explicitly requested ticket creation.",
                AllowCodeSearch         = true,
                AllowDeepLookup         = true,
                AllowConflictAssessment = true,
                AllowConflictBlocking   = true,
                AllowTicketCreation     = true,
                RequiresClarification   = ticketIntent.RequiresClarification
            };
        }

        // 2. Inspection / Verification
        if (StartsWithAny(lower, InspectionPrefixes))
        {
            return new ContextAgentRouteDecision
            {
                OriginalUserRequest     = request ?? string.Empty,
                EffectiveWorkText       = request ?? string.Empty,
                RequestKind             = DetermineInspectionKind(lower),
                Confidence              = 0.9,
                Reason                  = "Matched inspection/explanation/verification query.",
                AllowCodeSearch         = true,
                AllowDeepLookup         = true,
                AllowConflictAssessment = false,
                AllowConflictBlocking   = false,
                AllowTicketCreation     = false,
                RequiresClarification   = false
            };
        }

        // 3. Change / Replacement
        if (StartsWithAny(lower, ChangePrefixes))
        {
            return new ContextAgentRouteDecision
            {
                OriginalUserRequest     = request ?? string.Empty,
                EffectiveWorkText       = request ?? string.Empty,
                RequestKind             = DetermineChangeKind(lower),
                Confidence              = 0.8,
                Reason                  = "Matched implementation/change/replacement command.",
                AllowCodeSearch         = true,
                AllowDeepLookup         = true,
                AllowConflictAssessment = true,
                AllowConflictBlocking   = true,
                AllowTicketCreation     = false, // It's a change, not explicit ticket creation
                RequiresClarification   = false
            };
        }

        // 4. Fallback (General Chat)
        return new ContextAgentRouteDecision
        {
            OriginalUserRequest     = request ?? string.Empty,
            EffectiveWorkText       = request ?? string.Empty,
            RequestKind             = ContextRequestKind.GeneralChat,
            Confidence              = 0.5,
            Reason                  = "Default fallback route. No explicit command recognized.",
            AllowCodeSearch         = true,
            AllowDeepLookup         = false, // Usually just general chat doesn't need deep lookup unless necessary
            AllowConflictAssessment = false,
            AllowConflictBlocking   = false,
            AllowTicketCreation     = false,
            RequiresClarification   = false
        };
    }

    private static bool StartsWithAny(string text, string[] prefixes)
    {
        return prefixes.Any(p => text.StartsWith(p + " ") || text == p);
    }

    private static ContextRequestKind DetermineInspectionKind(string lower)
    {
        if (lower.StartsWith("explain") || lower.StartsWith("how"))
            return ContextRequestKind.ExplainCode;
        if (lower.StartsWith("verify") || lower.StartsWith("check"))
            return ContextRequestKind.VerifyImplementation;
        return ContextRequestKind.InspectCode;
    }

    private static ContextRequestKind DetermineChangeKind(string lower)
    {
        if (lower.StartsWith("replace") || lower.StartsWith("migrate") || lower.StartsWith("rewrite"))
            return ContextRequestKind.ReplaceArchitecture;
        if (lower.StartsWith("build"))
            return ContextRequestKind.BuildTicket;
        return ContextRequestKind.ChangeImplementation;
    }
}
