using System;
using System.Collections.Generic;

namespace IronDev.Core.Models;

public enum ChatRouteIntent
{
    GeneralChat = 0,

    ArchitectureDecisionExploration = 100,
    ProjectPlanningDiscussion = 110,
    CodeExplanation = 120,

    CreateSingleDraftTicket = 1000,
    CreateMultipleDraftTickets = 1010,
    OpenDraftTicketReview = 1020,

    CreateImplementationPlan = 1100,
    SaveDecision = 1200,
    SaveDiscussionDocument = 1300,

    BuildTicket = 2000,
    ProposeCodeChanges = 2010,
    RunTests = 2020
}

public enum ContextReferenceKind
{
    None,
    PreviousAssistantMessage,
    PreviousUserMessage,
    CurrentMessage,
    SelectedDocument,
    SelectedTicket,
    LatestCandidateWorkSet
}

public enum DraftCountMode
{
    None,
    Single,
    Multiple
}

public sealed class ChatTurnInput
{
    public int ProjectId { get; init; }
    public long ChatSessionId { get; init; }
    public string UserMessage { get; init; } = string.Empty;
    public string? PreviousAssistantMessage { get; init; }
    public string? PreviousUserMessage { get; init; }
    public string ActiveWorkspace { get; init; } = "Chat";
}

public sealed class ChatRouteResult
{
    public ChatRouteIntent Intent { get; init; }
    public double Confidence { get; init; }

    public bool IsAction { get; init; }
    public bool RequiresAction { get; init; }
    public bool AllowsProseResponse { get; init; }

    public ContextReferenceKind ContextReference { get; init; }
    public DraftCountMode DraftCountMode { get; init; }

    public CreateTicketIntent? CreateTicketIntent { get; init; }
    public IReadOnlyList<string> MatchedSignals { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public static ChatRouteResult GeneralChat() => new()
    {
        Intent = ChatRouteIntent.GeneralChat,
        Confidence = 0.5,
        IsAction = false,
        RequiresAction = false,
        AllowsProseResponse = true,
        ContextReference = ContextReferenceKind.None,
        DraftCountMode = DraftCountMode.None
    };
}

public sealed class ChatRoutePolicy
{
    public void Validate(ChatRouteResult route)
    {
        if (route.RequiresAction && route.AllowsProseResponse)
        {
            throw new InvalidOperationException("Action routes must not allow prose fallback.");
        }
    }
}
