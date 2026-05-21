using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services;

public sealed class ChatCommandRouter : IChatCommandRouter
{
    private readonly ChatRoutePolicy _policy = new();

    public Task<ChatRouteResult> RouteAsync(ChatTurnInput input, CancellationToken cancellationToken = default)
    {
        var ticketIntent = ChatIntentParser.ParseCreateTicket(
            input.UserMessage,
            input.PreviousAssistantMessage ?? input.PreviousUserMessage);

        var route = ticketIntent != null
            ? BuildTicketRoute(ticketIntent, input)
            : BuildActionRoute(input);

        _policy.Validate(route);
        return Task.FromResult(route);
    }

    private static ChatRouteResult BuildTicketRoute(CreateTicketIntent ticketIntent, ChatTurnInput input)
    {
        var isMultiple = ticketIntent.Intent == "CreateTickets" || ticketIntent.TicketCount > 1;
        var contextReference = ResolveContextReference(ticketIntent, input);
        var matchedSignals = new List<string>
        {
            "deterministic-ticket-command",
            ticketIntent.CommandText
        };

        if (contextReference != ContextReferenceKind.None)
            matchedSignals.Add($"context:{contextReference}");

        return new ChatRouteResult
        {
            Intent = isMultiple ? ChatRouteIntent.CreateMultipleDraftTickets : ChatRouteIntent.CreateSingleDraftTicket,
            Confidence = ticketIntent.Confidence,
            IsAction = true,
            RequiresAction = true,
            AllowsProseResponse = false,
            ContextReference = contextReference,
            DraftCountMode = isMultiple ? DraftCountMode.Multiple : DraftCountMode.Single,
            CreateTicketIntent = ticketIntent,
            MatchedSignals = matchedSignals
        };
    }

    private static ChatRouteResult BuildActionRoute(ChatTurnInput input)
    {
        var lower = Normalize(input.UserMessage);
        if (IsNonActionQuestion(lower))
            return ChatRouteResult.GeneralChat();

        var actionText = ResolveActionText(input);
        var contextReference = ResolveContextReference(input, actionText);

        if (IsSaveDecisionCommand(lower))
            return BuildActionRoute(ChatRouteIntent.SaveDecision, 0.92, "deterministic-save-decision-command", input, actionText, contextReference);

        if (IsCreatePlanCommand(lower))
            return BuildActionRoute(ChatRouteIntent.CreateImplementationPlan, 0.90, "deterministic-create-plan-command", input, actionText, contextReference);

        if (IsCreateDocumentCommand(lower))
            return BuildActionRoute(ChatRouteIntent.SaveDiscussionDocument, 0.88, "deterministic-create-document-command", input, actionText, contextReference);

        if (IsBuildTicketCommand(lower))
            return BuildActionRoute(ChatRouteIntent.BuildTicket, 0.86, "deterministic-build-ticket-command", input, actionText, contextReference);

        if (IsMultiStepWorkflowCommand(lower))
            return BuildActionRoute(ChatRouteIntent.CreateImplementationPlan, 0.78, "deterministic-multi-step-workflow-command", input, actionText, contextReference);

        if (IsVagueContextActionCommand(lower))
            return BuildActionRoute(ChatRouteIntent.CreateImplementationPlan, 0.72, "deterministic-vague-context-action-command", input, actionText, contextReference);

        return ChatRouteResult.GeneralChat();
    }

    private static ChatRouteResult BuildActionRoute(
        ChatRouteIntent intent,
        double confidence,
        string signal,
        ChatTurnInput input,
        string? actionText,
        ContextReferenceKind contextReference)
    {
        var signals = new List<string> { signal };
        if (contextReference != ContextReferenceKind.None)
            signals.Add($"context:{contextReference}");

        return new ChatRouteResult
        {
            Intent = intent,
            Confidence = confidence,
            IsAction = true,
            RequiresAction = true,
            AllowsProseResponse = false,
            ContextReference = contextReference,
            DraftCountMode = DraftCountMode.None,
            ActionText = actionText,
            ActionTitle = BuildActionTitle(input.UserMessage, actionText, intent),
            MatchedSignals = signals
        };
    }

    private static ContextReferenceKind ResolveContextReference(CreateTicketIntent ticketIntent, ChatTurnInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.PreviousAssistantMessage) &&
            string.Equals(ticketIntent.WorkText?.Trim(), input.PreviousAssistantMessage.Trim(), System.StringComparison.Ordinal))
            return ContextReferenceKind.PreviousAssistantMessage;

        if (!string.IsNullOrWhiteSpace(input.PreviousUserMessage) &&
            string.Equals(ticketIntent.WorkText?.Trim(), input.PreviousUserMessage.Trim(), System.StringComparison.Ordinal))
            return ContextReferenceKind.PreviousUserMessage;

        return string.IsNullOrWhiteSpace(ticketIntent.WorkText)
            ? ContextReferenceKind.None
            : ContextReferenceKind.CurrentMessage;
    }

    private static ContextReferenceKind ResolveContextReference(ChatTurnInput input, string? actionText)
    {
        if (!string.IsNullOrWhiteSpace(input.PreviousAssistantMessage) &&
            string.Equals(actionText?.Trim(), input.PreviousAssistantMessage.Trim(), StringComparison.Ordinal))
            return ContextReferenceKind.PreviousAssistantMessage;

        if (!string.IsNullOrWhiteSpace(input.PreviousUserMessage) &&
            string.Equals(actionText?.Trim(), input.PreviousUserMessage.Trim(), StringComparison.Ordinal))
            return ContextReferenceKind.PreviousUserMessage;

        return string.IsNullOrWhiteSpace(actionText)
            ? ContextReferenceKind.None
            : ContextReferenceKind.CurrentMessage;
    }

    private static string? ResolveActionText(ChatTurnInput input)
    {
        var lower = Normalize(input.UserMessage);
        var refersToPrevious =
            ContainsAny(lower, " this", " that", " above", " it") ||
            lower is "save this" or "save that" or "create plan" or "make plan";

        if (refersToPrevious && !string.IsNullOrWhiteSpace(input.PreviousAssistantMessage))
            return input.PreviousAssistantMessage.Trim();

        if (refersToPrevious && !string.IsNullOrWhiteSpace(input.PreviousUserMessage))
            return input.PreviousUserMessage.Trim();

        return StripActionCommand(input.UserMessage).Trim();
    }

    private static string BuildActionTitle(string userMessage, string? actionText, ChatRouteIntent intent)
    {
        var source = string.IsNullOrWhiteSpace(actionText) ? userMessage : actionText;
        var firstLine = source
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? userMessage;

        var prefix = intent switch
        {
            ChatRouteIntent.SaveDecision => "Decision",
            ChatRouteIntent.CreateImplementationPlan => "Implementation plan",
            ChatRouteIntent.SaveDiscussionDocument => "Discussion document",
            ChatRouteIntent.BuildTicket => "Build ticket",
            _ => "Action"
        };

        return firstLine.Length > 80
            ? $"{prefix}: {firstLine[..77]}..."
            : $"{prefix}: {firstLine}";
    }

    private static bool IsSaveDecisionCommand(string lower)
        => ContainsAny(lower,
            "save this as decision",
            "save that as decision",
            "save this as a decision",
            "save that as a decision",
            "make this a decision",
            "turn this into a decision",
            "create decision from this",
            "create a decision from this");

    private static bool IsCreatePlanCommand(string lower)
        => ContainsAny(lower,
            "turn this into a plan",
            "turn that into a plan",
            "create plan from this",
            "create a plan from this",
            "make this a plan",
            "make a plan from this",
            "turn this into an implementation plan",
            "create implementation plan");

    private static bool IsCreateDocumentCommand(string lower)
        => ContainsAny(lower,
            "create discussion doc",
            "create discussion document",
            "create document from this",
            "create a document from this",
            "save this as document",
            "save this as a document",
            "save this as architecture doc",
            "save this as architecture document",
            "create architecture doc",
            "create architecture document",
            "create the architecture notes",
            "architecture notes and break",
            "break the build work into tickets",
            "make the discussion doc",
            "make a discussion doc",
            "make discussion doc",
            "discussion doc and the ticket set",
            "discussion doc and ticket set",
            "turn that into discussion docs",
            "turn this into discussion docs",
            "discussion docs and tickets",
            "create the project docs",
            "set up the planning package",
            "turn that into docs",
            "turn this into docs",
            "save this as project knowledge",
            "save that as project knowledge",
            "project knowledge then make tickets");

    private static bool IsBuildTicketCommand(string lower)
        => ContainsAny(lower,
            "build this ticket",
            "build the ticket",
            "build ticket",
            "build selected ticket",
            "build first ticket",
            "build the first ticket",
            "start build agent",
            "run build agent",
            "start the build agent",
            "use the build agent",
            "make a plan and proposed patch",
            "proposed patch for ticket",
            "approval gate only");

    private static bool IsVagueContextActionCommand(string lower)
        => ContainsAny(lower,
            "turn that into the thing",
            "turn this into the thing",
            "do that now",
            "do this now",
            "make those real",
            "make that real",
            "make this real",
            "use the last bit",
            "from above",
            "make it happen",
            "that one",
            "same as before",
            "finish what we were doing",
            "make it official");

    private static bool IsMultiStepWorkflowCommand(string lower)
        => ContainsAny(lower,
            "set it all up then build",
            "make the docs tickets and start building",
            "do the whole flow",
            "create everything and run the first build",
            "plan it, ticket it, build it",
            "turn this into work and start");

    private static string StripActionCommand(string text)
    {
        var trimmed = text.Trim();
        var lower = trimmed.ToLowerInvariant();
        foreach (var prefix in new[]
        {
            "save this as a decision",
            "save this as decision",
            "save that as a decision",
            "save that as decision",
            "turn this into an implementation plan",
            "turn this into a plan",
            "create a plan from this",
            "create plan from this",
            "create discussion document",
            "create discussion doc",
            "create architecture document",
            "create architecture doc",
            "build this ticket",
            "build ticket"
        })
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
                return trimmed[prefix.Length..];
        }

        return trimmed;
    }

    private static bool IsNonActionQuestion(string lower)
        => lower.StartsWith(" what ", StringComparison.Ordinal) ||
           lower.StartsWith(" how ", StringComparison.Ordinal) ||
           lower.StartsWith(" why ", StringComparison.Ordinal) ||
           lower.StartsWith(" explain ", StringComparison.Ordinal) ||
           lower.StartsWith(" should we ", StringComparison.Ordinal) ||
           lower.StartsWith(" do we need ", StringComparison.Ordinal);

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.Ordinal));

    private static string Normalize(string value)
        => " " + value.Trim().ToLowerInvariant().Replace("  ", " ") + " ";
}
