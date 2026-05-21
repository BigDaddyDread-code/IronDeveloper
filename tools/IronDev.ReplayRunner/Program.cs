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

    var assertion = Score(replayCase, route);
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
        Passed = assertion.Passed,
        FailureReason = assertion.FailureReason,
        MatchedSignals = route.MatchedSignals.ToArray(),
        CreatedAtUtc = DateTimeOffset.UtcNow
    });
}

var outputRoot = Path.GetDirectoryName(planPath)!;
var resultPath = Path.Combine(outputRoot, "replay-results.json");
var summaryPath = Path.Combine(outputRoot, "runner-summary.json");

await File.WriteAllTextAsync(
    resultPath,
    JsonSerializer.Serialize(results, options));

var summary = new ReplayRunnerSummary
{
    DogfoodRunId = plan.DogfoodRunId,
    ScenarioId = plan.ScenarioId,
    TotalCases = results.Count,
    Passed = results.Count(result => result.Passed),
    Failed = results.Count(result => !result.Passed),
    ResultPath = resultPath,
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

static ReplayAssertion Score(ReplayCase replayCase, ChatRouteResult route)
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

    if (expected.ActionBlockedIfContextMissing && route.ContextReference == ContextReferenceKind.None)
    {
        return route.Intent == ChatRouteIntent.GeneralChat
            ? ReplayAssertion.Fail("Missing context reference fell back to general chat instead of blocked/clarifying action.")
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
    public bool NoUnsafeWrites { get; init; } = true;
    public bool RequiresContextReference { get; init; }
    public bool ActionBlockedIfContextMissing { get; init; }
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
    public DateTimeOffset CompletedAtUtc { get; init; }
}

public readonly record struct ReplayAssertion(bool Passed, string FailureReason)
{
    public static ReplayAssertion Pass() => new(true, string.Empty);
    public static ReplayAssertion Fail(string reason) => new(false, reason);
}
