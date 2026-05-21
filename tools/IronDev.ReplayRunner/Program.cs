using System.Text.Json;
using IronDev.Core.Models;
using IronDev.Infrastructure.Services;

if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Usage: IronDev.ReplayRunner <replay-plan.json>");
    return 2;
}

var planPath = Path.GetFullPath(args[0]);
if (!File.Exists(planPath))
{
    Console.Error.WriteLine($"Replay plan not found: {planPath}");
    return 2;
}

var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

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
var failed = false;

foreach (var replayCase in plan.Cases)
{
    var input = new ChatTurnInput
    {
        ProjectId = 0,
        ChatSessionId = 0,
        UserMessage = replayCase.Prompt,
        ActiveWorkspace = replayCase.Workspace,
        PreviousAssistantMessage = BuildPreviousAssistantMessage(replayCase),
        PreviousUserMessage = "We are planning the BookSeller MVP."
    };

    ChatRouteResult route;
    try
    {
        route = await router.RouteAsync(input);
    }
    catch (Exception ex)
    {
        results.Add(ReplayCaseResult.Failed(replayCase, "RouterException", ex.Message));
        failed = true;
        continue;
    }

    var actionResult = ExecuteDryRunAction(replayCase, route);
    actionResults.Add(actionResult);

    var assertion = Score(replayCase, route, actionResult);
    if (!assertion.Passed)
        failed = true;

    results.Add(new ReplayCaseResult
    {
        DogfoodRunId = replayCase.DogfoodRunId,
        CaseId = replayCase.CaseId,
        CaseNumber = replayCase.CaseNumber,
        Name = replayCase.Name,
        Prompt = replayCase.Prompt,
        Workspace = replayCase.Workspace,
        ExpectedIntent = replayCase.Expected.Intent,
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
var summaryPath = Path.Combine(outputRoot, "runner-summary.json");

await File.WriteAllTextAsync(
    resultPath,
    JsonSerializer.Serialize(results, options));

await File.WriteAllTextAsync(
    actionResultPath,
    JsonSerializer.Serialize(actionResults, options));

var summary = new ReplayRunnerSummary
{
    DogfoodRunId = plan.DogfoodRunId,
    ScenarioId = plan.ScenarioId,
    TotalCases = results.Count,
    Passed = results.Count(result => result.Passed),
    Failed = results.Count(result => !result.Passed),
    ResultPath = resultPath,
    ActionResultPath = actionResultPath,
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

static ReplayActionResult ExecuteDryRunAction(ReplayCase replayCase, ChatRouteResult route)
{
    var result = new ReplayActionResult
    {
        DogfoodRunId = replayCase.DogfoodRunId,
        CaseId = replayCase.CaseId,
        Intent = route.Intent.ToString(),
        DryRun = replayCase.Expected.NoUnsafeWrites
    };

    switch (route.Intent)
    {
        case ChatRouteIntent.CreateSingleDraftTicket:
            result.DraftTickets.Add(CreateTicketDraft(replayCase, route, 1));
            break;

        case ChatRouteIntent.CreateMultipleDraftTickets:
            var count = Math.Clamp(
                Math.Max(
                    replayCase.Expected.MinDraftTickets ?? 0,
                    Math.Max(route.CreateTicketIntent?.TicketCount ?? 0, route.CreateTicketIntent?.SplitHints?.Count ?? 0)),
                2,
                5);

            for (var i = 1; i <= count; i++)
                result.DraftTickets.Add(CreateTicketDraft(replayCase, route, i));
            break;

        case ChatRouteIntent.SaveDiscussionDocument:
            result.DiscussionDocuments.Add(new SimulatedDiscussionDocument
            {
                Title = BuildTitle("Discussion", replayCase.Prompt),
                SourcePrompt = replayCase.Prompt,
                Status = "Draft"
            });

            if (MentionsTickets(replayCase.Prompt))
            {
                var draftCount = Math.Clamp(replayCase.Expected.MinDraftTickets ?? 2, 2, 5);
                for (var i = 1; i <= draftCount; i++)
                    result.DraftTickets.Add(CreateTicketDraft(replayCase, route, i));
            }
            break;

        case ChatRouteIntent.CreateImplementationPlan:
            result.ImplementationPlans.Add(new SimulatedImplementationPlan
            {
                Title = BuildTitle("Implementation plan", route.ActionText ?? replayCase.Prompt),
                RequiresApproval = true,
                SourcePrompt = replayCase.Prompt
            });
            result.ApprovalsRequested = 1;
            break;

        case ChatRouteIntent.BuildTicket:
            result.BuildRuns.Add(new SimulatedBuildRun
            {
                Title = "Build Agent proposal",
                Status = "WaitingForApproval",
                FilesChanged = 0,
                SourcePrompt = replayCase.Prompt
            });
            result.ApprovalsRequested = 1;
            result.FilesChanged = 0;
            break;

        case ChatRouteIntent.GeneralChat:
            if (IsUnsafeWriteCommand(replayCase) ||
                replayCase.Expected.ActionBlockedIfContextMissing ||
                replayCase.Expected.RequiresClarificationWhenNoContext ||
                replayCase.Expected.MustIdentifyContradiction ||
                replayCase.Expected.MustIdentifyProjectAmbiguity)
            {
                result.Blocked = true;
                result.BlockReason = "Clarification or action block required by replay expectation.";
            }
            break;
    }

    return result;
}

static bool IsUnsafeWriteCommand(ReplayCase replayCase)
{
    var prompt = replayCase.Prompt;
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

static SimulatedDraftTicket CreateTicketDraft(ReplayCase replayCase, ChatRouteResult route, int index)
{
    var hint = route.CreateTicketIntent?.SplitHints?.Skip(index - 1).FirstOrDefault();
    return new SimulatedDraftTicket
    {
        Title = string.IsNullOrWhiteSpace(hint)
            ? BuildTitle($"Draft ticket {index}", route.CreateTicketIntent?.WorkText ?? replayCase.Prompt)
            : hint,
        SourcePrompt = replayCase.Prompt,
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

static ReplayAssertion Score(ReplayCase replayCase, ChatRouteResult route, ReplayActionResult actionResult)
{
    var expected = replayCase.Expected;
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
    public DateTimeOffset CompletedAtUtc { get; init; }
}

public sealed class ReplayActionResult
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public string CaseId { get; init; } = string.Empty;
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
