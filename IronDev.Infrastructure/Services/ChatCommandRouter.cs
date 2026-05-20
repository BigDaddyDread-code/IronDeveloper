using System.Collections.Generic;
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

        var route = ticketIntent == null
            ? ChatRouteResult.GeneralChat()
            : BuildTicketRoute(ticketIntent, input);

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
}
