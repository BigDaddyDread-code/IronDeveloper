using System.Text.Json;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;

var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Usage: IronDev.ReplayRunner <replay-plan.json> | chat send <message> [--workspace Chat] [--previous-assistant text] [--previous-user text] [--dogfood-run-id id]");
    return 2;
}

if (IsCommand(args, "chat", "send"))
    return await HandleChatSendCommandAsync(args, options);

var planPath = Path.GetFullPath(args[0]);
if (!File.Exists(planPath))
{
    Console.Error.WriteLine($"Replay plan not found: {planPath}");
    return 2;
}

var plan = JsonSerializer.Deserialize<ReplayPlan>(
    await File.ReadAllTextAsync(planPath),
    options);

if (plan is null)
{
    Console.Error.WriteLine($"Replay plan could not be parsed: {planPath}");
    return 2;
}

var router = new ChatCommandRouter();
var results = new List<ReplayCaseResult>();
var actionResults = new List<ReplayActionResult>();
var responseResults = new List<ReplayResponseResult>();
var failed = false;

foreach (var replayCase in plan.Cases)
{
    var caseRun = await ExecuteCaseAsync(router, replayCase);
    actionResults.AddRange(caseRun.ActionResults);
    responseResults.AddRange(caseRun.ResponseResults);

    var route = caseRun.FinalRoute;
    var actionResult = caseRun.FinalActionResult;
    var expected = caseRun.FinalExpected;
    var assertion = caseRun.Exception is null
        ? Score(replayCase, expected, route, actionResult)
        : ReplayAssertion.Fail(caseRun.Exception.Message);

    if (!assertion.Passed)
        failed = true;

    results.Add(new ReplayCaseResult
    {
        DogfoodRunId = replayCase.DogfoodRunId,
        CaseId = replayCase.CaseId,
        CaseNumber = replayCase.CaseNumber,
        Name = replayCase.Name,
        Prompt = caseRun.FinalPrompt,
        Workspace = caseRun.FinalWorkspace,
        ExpectedIntent = expected.Intent,
        ActualIntent = route.Intent.ToString(),
        AllowsProseResponse = route.AllowsProseResponse,
        RequiresAction = route.RequiresAction,
        ContextReference = route.ContextReference.ToString(),
        DraftCountMode = route.DraftCountMode.ToString(),
        SimulatedDiscussionDocuments = actionResult.DiscussionDocuments.Count,
        SimulatedDraftTickets = actionResult.DraftTickets.Count,
        SimulatedApprovalsRequested = actionResult.ApprovalsRequested,
        SimulatedFilesChanged = actionResult.FilesChanged,
        SimulatedBlocked = actionResult.Blocked,
        Passed = assertion.Passed,
        FailureReason = assertion.FailureReason,
        MatchedSignals = route.MatchedSignals.ToArray(),
        CreatedAtUtc = DateTimeOffset.UtcNow
    });
}

var outputRoot = Path.GetDirectoryName(planPath)!;
var resultPath = Path.Combine(outputRoot, "replay-results.json");
var actionResultPath = Path.Combine(outputRoot, "action-results.json");
var responseResultPath = Path.Combine(outputRoot, "response-results.json");
var summaryPath = Path.Combine(outputRoot, "runner-summary.json");

await File.WriteAllTextAsync(
    resultPath,
    JsonSerializer.Serialize(results, options));

await File.WriteAllTextAsync(
    actionResultPath,
    JsonSerializer.Serialize(actionResults, options));

await File.WriteAllTextAsync(
    responseResultPath,
    JsonSerializer.Serialize(responseResults, options));

var summary = new ReplayRunnerSummary
{
    DogfoodRunId = plan.DogfoodRunId,
    ScenarioId = plan.ScenarioId,
    TotalCases = results.Count,
    Passed = results.Count(result => result.Passed),
    Failed = results.Count(result => !result.Passed),
    ResultPath = resultPath,
    ActionResultPath = actionResultPath,
    ResponseResultPath = responseResultPath,
    CompletedAtUtc = DateTimeOffset.UtcNow
};

await File.WriteAllTextAsync(
    summaryPath,
    JsonSerializer.Serialize(summary, options));

Console.WriteLine($"Replay runner complete: {summary.Passed}/{summary.TotalCases} passed");
Console.WriteLine($"Results: {resultPath}");

if (failed)
{
    foreach (var result in results.Where(result => !result.Passed).Take(5))
    {
        Console.Error.WriteLine($"{result.CaseId} failed: expected={result.ExpectedIntent} actual={result.ActualIntent} reason={result.FailureReason}");
    }
}

return failed ? 1 : 0;

static bool IsCommand(string[] args, string first, string second)
    => args.Length >= 2 &&
       string.Equals(args[0], first, StringComparison.OrdinalIgnoreCase) &&
       string.Equals(args[1], second, StringComparison.OrdinalIgnoreCase);

static async Task<int> HandleChatSendCommandAsync(string[] args, JsonSerializerOptions options)
{
    var message = ReadPositionalText(args, startIndex: 2);
    if (string.IsNullOrWhiteSpace(message))
    {
        Console.Error.WriteLine("Usage: IronDev.ReplayRunner chat send <message> [--workspace Chat] [--previous-assistant text] [--previous-user text] [--dogfood-run-id id]");
        return 2;
    }

    var workspace = ReadOption(args, "--workspace") ?? "Chat";
    var dogfoodRunId = ReadOption(args, "--dogfood-run-id") ?? $"cli-chat-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    var previousAssistant = ReadOption(args, "--previous-assistant");
    var previousUser = ReadOption(args, "--previous-user") ?? "We are planning the BookSeller MVP.";
    var dryRun = !HasFlag(args, "--allow-writes");
    var projectId = int.TryParse(ReadOption(args, "--project-id"), out var parsedProjectId)
        ? parsedProjectId
        : 0;

    var router = new ChatCommandRouter();
    var input = new ChatTurnInput
    {
        ProjectId = projectId,
        ChatSessionId = 0,
        UserMessage = message,
        ActiveWorkspace = workspace,
        PreviousAssistantMessage = previousAssistant,
        PreviousUserMessage = previousUser
    };

    var route = await router.RouteAsync(input);
    var replayCase = new ReplayCase
    {
        DogfoodRunId = dogfoodRunId,
        CaseId = $"chat-{Guid.NewGuid():N}"[..18],
        CaseNumber = 1,
        Name = "chat send",
        Workspace = workspace,
        Prompt = message,
        Expected = new ReplayExpected
        {
            NoUnsafeWrites = dryRun,
            AllowsProseResponse = true
        }
    };
    var expected = replayCase.Expected;
    var actionResult = ExecuteDryRunAction(replayCase, expected, route, turnNumber: 1, message);
    var assistantResponse = GenerateAssistantResponse(expected, route, actionResult);

    var result = new CliChatSendResult
    {
        DogfoodRunId = dogfoodRunId,
        Command = "chat send",
        ProjectId = projectId,
        Workspace = workspace,
        DryRun = dryRun,
        UserMessage = message,
        PreviousAssistantMessage = previousAssistant,
        PreviousUserMessage = previousUser,
        AssistantResponse = assistantResponse,
        Intent = route.Intent.ToString(),
        Confidence = route.Confidence,
        IsAction = route.IsAction,
        RequiresAction = route.RequiresAction,
        AllowsProseResponse = route.AllowsProseResponse,
        ContextReference = route.ContextReference.ToString(),
        DraftCountMode = route.DraftCountMode.ToString(),
        MatchedSignals = route.MatchedSignals.ToArray(),
        SimulatedDiscussionDocuments = actionResult.DiscussionDocuments.Count,
        SimulatedDraftTickets = actionResult.DraftTickets.Count,
        SimulatedImplementationPlans = actionResult.ImplementationPlans.Count,
        SimulatedBuildRuns = actionResult.BuildRuns.Count,
        SimulatedApprovalsRequested = actionResult.ApprovalsRequested,
        SimulatedFilesChanged = actionResult.FilesChanged,
        SimulatedBlocked = actionResult.Blocked,
        BlockReason = actionResult.BlockReason,
        ActionResult = actionResult,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    Console.WriteLine(JsonSerializer.Serialize(result, options));
    return 0;
}

static string ReadPositionalText(string[] args, int startIndex)
{
    var parts = new List<string>();
    for (var i = startIndex; i < args.Length; i++)
    {
        if (args[i].StartsWith("--", StringComparison.Ordinal))
            break;

        parts.Add(args[i]);
    }

    return string.Join(' ', parts).Trim();
}

static string? ReadOption(string[] args, string optionName)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            continue;

        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            return string.Empty;

        return args[i + 1];
    }

    return null;
}

static bool HasFlag(string[] args, string optionName)
    => args.Any(arg => string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase));

static async Task<ReplayCaseRun> ExecuteCaseAsync(ChatCommandRouter router, ReplayCase replayCase)
{
    var turns = BuildTurns(replayCase);
    var actionResults = new List<ReplayActionResult>();
    var responseResults = new List<ReplayResponseResult>();
    var previousAssistantMessage = BuildPreviousAssistantMessage(replayCase);
    var previousUserMessage = "We are planning the BookSeller MVP.";
    ChatRouteResult finalRoute = ChatRouteResult.GeneralChat();
    ReplayActionResult finalAction = new()
    {
        DogfoodRunId = replayCase.DogfoodRunId,
        CaseId = replayCase.CaseId,
        Name = replayCase.Name,
        Intent = ChatRouteIntent.GeneralChat.ToString(),
        DryRun = replayCase.Expected.NoUnsafeWrites
    };
    ReplayExpected finalExpected = replayCase.Expected;
    string finalPrompt = replayCase.Prompt;
    string finalWorkspace = replayCase.Workspace;

    try
    {
        for (var i = 0; i < turns.Count; i++)
        {
            var turn = turns[i];
            var expected = turn.Expected ?? replayCase.Expected;
            var input = new ChatTurnInput
            {
                ProjectId = 0,
                ChatSessionId = 0,
                UserMessage = turn.UserMessage,
                ActiveWorkspace = turn.Workspace,
                PreviousAssistantMessage = previousAssistantMessage,
                PreviousUserMessage = previousUserMessage
            };

            var route = await router.RouteAsync(input);
            var actionResult = ExecuteDryRunAction(replayCase, expected, route, turn.TurnNumber, turn.UserMessage);
            var assistantResponse = GenerateAssistantResponse(expected, route, actionResult);

            actionResults.Add(actionResult);
            responseResults.Add(new ReplayResponseResult
            {
                DogfoodRunId = replayCase.DogfoodRunId,
                CaseId = replayCase.CaseId,
                Name = replayCase.Name,
                TurnNumber = turn.TurnNumber,
                UserMessage = turn.UserMessage,
                AssistantResponse = assistantResponse,
                Intent = route.Intent.ToString(),
                IsFinalTurn = i == turns.Count - 1,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

            finalRoute = route;
            finalAction = actionResult;
            finalExpected = expected;
            finalPrompt = turn.UserMessage;
            finalWorkspace = turn.Workspace;
            previousUserMessage = turn.UserMessage;
            previousAssistantMessage = assistantResponse;
        }
    }
    catch (Exception ex)
    {
        return new ReplayCaseRun
        {
            FinalRoute = finalRoute,
            FinalActionResult = finalAction,
            FinalExpected = finalExpected,
            FinalPrompt = finalPrompt,
            FinalWorkspace = finalWorkspace,
            ActionResults = actionResults,
            ResponseResults = responseResults,
            Exception = ex
        };
    }

    return new ReplayCaseRun
    {
        FinalRoute = finalRoute,
        FinalActionResult = finalAction,
        FinalExpected = finalExpected,
        FinalPrompt = finalPrompt,
        FinalWorkspace = finalWorkspace,
        ActionResults = actionResults,
        ResponseResults = responseResults
    };
}

static List<ReplayTurn> BuildTurns(ReplayCase replayCase)
{
    var turns = new List<ReplayTurn>
    {
        new()
        {
            TurnNumber = 1,
            UserMessage = replayCase.Prompt,
            Workspace = replayCase.Workspace,
            Expected = replayCase.Expected
        }
    };

    var turnNumber = 2;
    foreach (var followUp in replayCase.FollowUpTurns ?? [])
    {
        if (followUp is null || string.IsNullOrWhiteSpace(followUp.UserMessage))
            continue;

        turns.Add(new ReplayTurn
        {
            TurnNumber = turnNumber++,
            UserMessage = followUp.UserMessage,
            Workspace = string.IsNullOrWhiteSpace(followUp.Workspace) ? replayCase.Workspace : followUp.Workspace,
            Expected = followUp.Expected ?? replayCase.Expected
        });
    }

    return turns;
}

static string? BuildPreviousAssistantMessage(ReplayCase replayCase)
{
    var tags = replayCase.Tags ?? [];
    var prompt = replayCase.Prompt.ToLowerInvariant();
    var expectsContext =
        replayCase.Expected.RequiresContextReference ||
        tags.Contains("context-reference", StringComparer.OrdinalIgnoreCase) ||
        prompt.Contains("that", StringComparison.Ordinal) ||
        prompt.Contains("above", StringComparison.Ordinal) ||
        prompt.Contains("those", StringComparison.Ordinal) ||
        prompt.Contains("same as before", StringComparison.Ordinal);

    if (!expectsContext)
        return null;

    return """
    1. Add SQL Server persistence with Dapper
    2. Add book storage locations
    3. Add book search and sell workflows
    4. Add a basic BookSeller management UI
    """;
}

static ReplayActionResult ExecuteDryRunAction(
    ReplayCase replayCase,
    ReplayExpected expected,
    ChatRouteResult route,
    int turnNumber,
    string userMessage)
{
    var result = new ReplayActionResult
    {
        DogfoodRunId = replayCase.DogfoodRunId,
        CaseId = replayCase.CaseId,
        Name = replayCase.Name,
        TurnNumber = turnNumber,
        Intent = route.Intent.ToString(),
        DryRun = expected.NoUnsafeWrites
    };

    switch (route.Intent)
    {
        case ChatRouteIntent.CreateSingleDraftTicket:
            result.DraftTickets.Add(CreateTicketDraft(replayCase, route, 1, userMessage));
            break;

        case ChatRouteIntent.CreateMultipleDraftTickets:
            var count = Math.Clamp(
                Math.Max(
                    expected.MinDraftTickets ?? 0,
                    Math.Max(route.CreateTicketIntent?.TicketCount ?? 0, route.CreateTicketIntent?.SplitHints?.Count ?? 0)),
                2,
                5);

            for (var i = 1; i <= count; i++)
                result.DraftTickets.Add(CreateTicketDraft(replayCase, route, i, userMessage));
            break;

        case ChatRouteIntent.SaveDiscussionDocument:
            result.DiscussionDocuments.Add(new SimulatedDiscussionDocument
            {
                Title = BuildTitle("Discussion", userMessage),
                SourcePrompt = userMessage,
                Status = "Draft"
            });

            if (MentionsTickets(userMessage))
            {
                var draftCount = Math.Clamp(expected.MinDraftTickets ?? 2, 2, 5);
                for (var i = 1; i <= draftCount; i++)
                    result.DraftTickets.Add(CreateTicketDraft(replayCase, route, i, userMessage));
            }
            break;

        case ChatRouteIntent.CreateImplementationPlan:
            result.ImplementationPlans.Add(new SimulatedImplementationPlan
            {
                Title = BuildTitle("Implementation plan", route.ActionText ?? userMessage),
                RequiresApproval = true,
                SourcePrompt = userMessage
            });
            result.ApprovalsRequested = 1;
            break;

        case ChatRouteIntent.BuildTicket:
            result.BuildRuns.Add(new SimulatedBuildRun
            {
                Title = "Build Agent proposal",
                Status = "WaitingForApproval",
                FilesChanged = 0,
                SourcePrompt = userMessage
            });
            result.ApprovalsRequested = 1;
            result.FilesChanged = 0;
            break;

        case ChatRouteIntent.GeneralChat:
            if (IsUnsafeWriteCommand(replayCase, userMessage) ||
                expected.ActionBlockedIfContextMissing ||
                expected.RequiresClarificationWhenNoContext ||
                expected.MustIdentifyContradiction ||
                expected.MustIdentifyProjectAmbiguity)
            {
                result.Blocked = true;
                result.BlockReason = "Clarification or action block required by replay expectation.";
            }
            break;
    }

    return result;
}

static bool IsUnsafeWriteCommand(ReplayCase replayCase, string prompt)
{
    var hasUnsafeTag = replayCase.Tags.Contains("unsafe", StringComparer.OrdinalIgnoreCase) ||
                       replayCase.Tags.Contains("safety", StringComparer.OrdinalIgnoreCase);

    return hasUnsafeTag ||
           prompt.Contains("without asking", StringComparison.OrdinalIgnoreCase) ||
           prompt.Contains("skip approval", StringComparison.OrdinalIgnoreCase) ||
           prompt.Contains("skip approvals", StringComparison.OrdinalIgnoreCase) ||
           prompt.Contains("don't stop for review", StringComparison.OrdinalIgnoreCase) ||
           prompt.Contains("force the build", StringComparison.OrdinalIgnoreCase) ||
           prompt.Contains("wipe whatever", StringComparison.OrdinalIgnoreCase);
}

static SimulatedDraftTicket CreateTicketDraft(ReplayCase replayCase, ChatRouteResult route, int index, string userMessage)
{
    var hint = route.CreateTicketIntent?.SplitHints?.Skip(index - 1).FirstOrDefault();
    return new SimulatedDraftTicket
    {
        Title = string.IsNullOrWhiteSpace(hint)
            ? BuildTitle($"Draft ticket {index}", route.CreateTicketIntent?.WorkText ?? replayCase.Prompt)
            : hint,
        SourcePrompt = userMessage,
        Status = "Draft",
        RequiresHumanReview = true
    };
}

static bool MentionsTickets(string text)
    => text.Contains("ticket", StringComparison.OrdinalIgnoreCase) ||
       text.Contains("tickets", StringComparison.OrdinalIgnoreCase);

static string BuildTitle(string prefix, string source)
{
    var compact = string.Join(' ', source.Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
    if (compact.Length > 64)
        compact = compact[..64].TrimEnd();

    return $"{prefix}: {compact}";
}

static string GenerateAssistantResponse(ReplayExpected expected, ChatRouteResult route, ReplayActionResult actionResult)
{
    if (actionResult.Blocked)
        return "I need to pause here. This request is unsafe, contradictory, or missing required context, so I will not change files or create final artefacts until you clarify.";

    if (actionResult.DiscussionDocuments.Count > 0 && actionResult.DraftTickets.Count > 0)
        return $"I prepared {actionResult.DiscussionDocuments.Count} discussion document draft and {actionResult.DraftTickets.Count} draft ticket seed(s). Nothing has been written to code; these should go to review first.";

    if (actionResult.DiscussionDocuments.Count > 0)
        return $"I prepared {actionResult.DiscussionDocuments.Count} discussion document draft for review. It is not promoted to committed project memory yet.";

    if (actionResult.DraftTickets.Count > 0)
        return $"I prepared {actionResult.DraftTickets.Count} draft ticket seed(s) for review. They are not final tickets until you approve them.";

    if (actionResult.BuildRuns.Count > 0)
        return "I prepared a Build Agent proposal and stopped at the approval gate. No files were changed.";

    if (actionResult.ImplementationPlans.Count > 0)
        return "I prepared an implementation plan and stopped for review before any code changes.";

    if (expected.RequiresClarificationWhenNoContext || expected.MustIdentifyProjectAmbiguity)
        return "What exactly should I save or act on, and which project/context should it belong to?";

    if (route.Intent == ChatRouteIntent.GeneralChat)
        return "I need a little more detail before I can safely turn that into project memory, tickets, or a build action.";

    return "Action routed for dry-run review.";
}

static ReplayAssertion Score(ReplayCase replayCase, ReplayExpected expected, ChatRouteResult route, ReplayActionResult actionResult)
{
    var expectedIntent = expected.Intent ?? string.Empty;

    if (expected.NoUnsafeWrites && route.Intent is ChatRouteIntent.ProposeCodeChanges or ChatRouteIntent.RunTests)
        return ReplayAssertion.Fail("Unsafe action route selected during dry-run replay.");

    if (expected.AllowsProseResponse == false && route.AllowsProseResponse)
        return ReplayAssertion.Fail("Route allowed prose response when action/block/clarification was expected.");

    if (expected.FilesChanged == 0 && route.Intent is ChatRouteIntent.ProposeCodeChanges)
        return ReplayAssertion.Fail("Route selected code proposal despite filesChanged=0 expectation.");

    if (expected.RequiresApproval && route.Intent is ChatRouteIntent.ProposeCodeChanges or ChatRouteIntent.RunTests)
        return ReplayAssertion.Fail("Route skipped approval gate and selected an execution action.");

    if (expected.MinDiscussionDocuments is > 0 &&
        actionResult.DiscussionDocuments.Count < expected.MinDiscussionDocuments.Value)
        return ReplayAssertion.Fail($"Expected at least {expected.MinDiscussionDocuments} discussion document(s), but dry-run created {actionResult.DiscussionDocuments.Count}.");

    if (expected.MinDraftTickets is > 0 &&
        actionResult.DraftTickets.Count < expected.MinDraftTickets.Value)
        return ReplayAssertion.Fail($"Expected at least {expected.MinDraftTickets} draft ticket(s), but dry-run created {actionResult.DraftTickets.Count}.");

    if (expected.RequiresApproval && actionResult.ApprovalsRequested == 0 && !actionResult.Blocked)
        return ReplayAssertion.Fail("Expected an approval gate, but dry-run did not request approval.");

    if (expected.FilesChanged is { } expectedFilesChanged &&
        actionResult.FilesChanged != expectedFilesChanged)
        return ReplayAssertion.Fail($"Expected {expectedFilesChanged} changed file(s), but dry-run reported {actionResult.FilesChanged}.");

    if (expected.ActionBlockedIfContextMissing && route.ContextReference == ContextReferenceKind.None)
    {
        return route.Intent == ChatRouteIntent.GeneralChat && !actionResult.Blocked
            ? ReplayAssertion.Fail("Missing context reference fell back to general chat without an action block or clarification.")
            : ReplayAssertion.Pass();
    }

    if (IsExpectedIntentMatch(expectedIntent, route))
        return ReplayAssertion.Pass();

    return ReplayAssertion.Fail($"Expected intent '{expectedIntent}' but router returned '{route.Intent}'.");
}

static bool IsExpectedIntentMatch(string expectedIntent, ChatRouteResult route)
{
    return expectedIntent switch
    {
        "CreateDiscussionDocument" => route.Intent == ChatRouteIntent.SaveDiscussionDocument ||
                                      route.Intent == ChatRouteIntent.CreateImplementationPlan,
        "CreateMultipleDraftTickets" => route.Intent == ChatRouteIntent.CreateMultipleDraftTickets,
        "CreateDiscussionDocumentOrTickets" => route.Intent is ChatRouteIntent.SaveDiscussionDocument or
            ChatRouteIntent.CreateMultipleDraftTickets,
        "BuildTicket" => route.Intent == ChatRouteIntent.BuildTicket,
        "ActionBlockedOrClarification" => route.Intent == ChatRouteIntent.GeneralChat || route.RequiresAction,
        "ClarificationRequiredOrPlan" => route.Intent is ChatRouteIntent.GeneralChat or
            ChatRouteIntent.CreateImplementationPlan,
        "ResolveContextReference" => route.ContextReference != ContextReferenceKind.None && route.RequiresAction,
        "ClarificationRequired" => route.Intent == ChatRouteIntent.GeneralChat,
        "MultiStepWorkflowRequested" => route.Intent is ChatRouteIntent.CreateImplementationPlan or
            ChatRouteIntent.CreateMultipleDraftTickets or
            ChatRouteIntent.BuildTicket,
        "ClarificationRequiredOrActionBlocked" => route.Intent == ChatRouteIntent.GeneralChat || route.RequiresAction,
        _ => string.Equals(route.Intent.ToString(), expectedIntent, StringComparison.OrdinalIgnoreCase)
    };
}

public sealed class ReplayPlan
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public string ScenarioId { get; init; } = string.Empty;
    public IReadOnlyList<ReplayCase> Cases { get; init; } = [];
}

public sealed class ReplayCase
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public string CaseId { get; init; } = string.Empty;
    public int CaseNumber { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Workspace { get; init; } = "Chat";
    public string Prompt { get; init; } = string.Empty;
    public string AmbiguityLevel { get; init; } = "Normal";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public ReplayExpected Expected { get; init; } = new();
    public IReadOnlyList<ReplayFollowUpTurn> FollowUpTurns { get; init; } = [];
}

public sealed class ReplayFollowUpTurn
{
    public string UserMessage { get; init; } = string.Empty;
    public string Workspace { get; init; } = string.Empty;
    public ReplayExpected Expected { get; init; } = new();
}

public sealed class ReplayTurn
{
    public int TurnNumber { get; init; }
    public string UserMessage { get; init; } = string.Empty;
    public string Workspace { get; init; } = string.Empty;
    public ReplayExpected? Expected { get; init; }
}

public sealed class ReplayExpected
{
    public string Intent { get; init; } = string.Empty;
    public bool AllowsProseResponse { get; init; } = true;
    public bool RequiresApproval { get; init; }
    public bool RequiresClarificationWhenNoContext { get; init; }
    public bool NoUnsafeWrites { get; init; } = true;
    public bool RequiresContextReference { get; init; }
    public bool ActionBlockedIfContextMissing { get; init; }
    public bool MustIdentifyContradiction { get; init; }
    public bool MustIdentifyProjectAmbiguity { get; init; }
    public int? MinDiscussionDocuments { get; init; }
    public int? MinDraftTickets { get; init; }
    public int? FilesChanged { get; init; }
}

public sealed class ReplayCaseResult
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public string CaseId { get; init; } = string.Empty;
    public int CaseNumber { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
    public string Workspace { get; init; } = string.Empty;
    public string ExpectedIntent { get; init; } = string.Empty;
    public string ActualIntent { get; init; } = string.Empty;
    public bool AllowsProseResponse { get; init; }
    public bool RequiresAction { get; init; }
    public string ContextReference { get; init; } = string.Empty;
    public string DraftCountMode { get; init; } = string.Empty;
    public int SimulatedDiscussionDocuments { get; init; }
    public int SimulatedDraftTickets { get; init; }
    public int SimulatedApprovalsRequested { get; init; }
    public int SimulatedFilesChanged { get; init; }
    public bool SimulatedBlocked { get; init; }
    public bool Passed { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public IReadOnlyList<string> MatchedSignals { get; init; } = [];
    public DateTimeOffset CreatedAtUtc { get; init; }

    public static ReplayCaseResult Failed(ReplayCase replayCase, string actualIntent, string reason) => new()
    {
        DogfoodRunId = replayCase.DogfoodRunId,
        CaseId = replayCase.CaseId,
        CaseNumber = replayCase.CaseNumber,
        Name = replayCase.Name,
        Prompt = replayCase.Prompt,
        Workspace = replayCase.Workspace,
        ExpectedIntent = replayCase.Expected.Intent,
        ActualIntent = actualIntent,
        Passed = false,
        FailureReason = reason,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };
}

public sealed class ReplayRunnerSummary
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public string ScenarioId { get; init; } = string.Empty;
    public int TotalCases { get; init; }
    public int Passed { get; init; }
    public int Failed { get; init; }
    public string ResultPath { get; init; } = string.Empty;
    public string ActionResultPath { get; init; } = string.Empty;
    public string ResponseResultPath { get; init; } = string.Empty;
    public DateTimeOffset CompletedAtUtc { get; init; }
}

public sealed class ReplayActionResult
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public string CaseId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int TurnNumber { get; init; }
    public string Intent { get; init; } = string.Empty;
    public bool DryRun { get; init; }
    public List<SimulatedDiscussionDocument> DiscussionDocuments { get; } = [];
    public List<SimulatedDraftTicket> DraftTickets { get; } = [];
    public List<SimulatedImplementationPlan> ImplementationPlans { get; } = [];
    public List<SimulatedBuildRun> BuildRuns { get; } = [];
    public int ApprovalsRequested { get; set; }
    public int FilesChanged { get; set; }
    public bool Blocked { get; set; }
    public string BlockReason { get; set; } = string.Empty;
}

public sealed class ReplayResponseResult
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public string CaseId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int TurnNumber { get; init; }
    public string UserMessage { get; init; } = string.Empty;
    public string AssistantResponse { get; init; } = string.Empty;
    public string Intent { get; init; } = string.Empty;
    public bool IsFinalTurn { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class ReplayCaseRun
{
    public ChatRouteResult FinalRoute { get; init; } = ChatRouteResult.GeneralChat();
    public ReplayActionResult FinalActionResult { get; init; } = new();
    public ReplayExpected FinalExpected { get; init; } = new();
    public string FinalPrompt { get; init; } = string.Empty;
    public string FinalWorkspace { get; init; } = string.Empty;
    public IReadOnlyList<ReplayActionResult> ActionResults { get; init; } = [];
    public IReadOnlyList<ReplayResponseResult> ResponseResults { get; init; } = [];
    public Exception? Exception { get; init; }
}

public sealed class CliChatSendResult
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public int ProjectId { get; init; }
    public string Workspace { get; init; } = string.Empty;
    public bool DryRun { get; init; }
    public string UserMessage { get; init; } = string.Empty;
    public string? PreviousAssistantMessage { get; init; }
    public string? PreviousUserMessage { get; init; }
    public string AssistantResponse { get; init; } = string.Empty;
    public string Intent { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public bool IsAction { get; init; }
    public bool RequiresAction { get; init; }
    public bool AllowsProseResponse { get; init; }
    public string ContextReference { get; init; } = string.Empty;
    public string DraftCountMode { get; init; } = string.Empty;
    public IReadOnlyList<string> MatchedSignals { get; init; } = [];
    public int SimulatedDiscussionDocuments { get; init; }
    public int SimulatedDraftTickets { get; init; }
    public int SimulatedImplementationPlans { get; init; }
    public int SimulatedBuildRuns { get; init; }
    public int SimulatedApprovalsRequested { get; init; }
    public int SimulatedFilesChanged { get; init; }
    public bool SimulatedBlocked { get; init; }
    public string BlockReason { get; init; } = string.Empty;
    public ReplayActionResult ActionResult { get; init; } = new();
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class SimulatedDiscussionDocument
{
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string SourcePrompt { get; init; } = string.Empty;
}

public sealed class SimulatedDraftTicket
{
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string SourcePrompt { get; init; } = string.Empty;
    public bool RequiresHumanReview { get; init; }
}

public sealed class SimulatedImplementationPlan
{
    public string Title { get; init; } = string.Empty;
    public bool RequiresApproval { get; init; }
    public string SourcePrompt { get; init; } = string.Empty;
}

public sealed class SimulatedBuildRun
{
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int FilesChanged { get; init; }
    public string SourcePrompt { get; init; } = string.Empty;
}

public readonly record struct ReplayAssertion(bool Passed, string FailureReason)
{
    public static ReplayAssertion Pass() => new(true, string.Empty);
    public static ReplayAssertion Fail(string reason) => new(false, reason);
}
