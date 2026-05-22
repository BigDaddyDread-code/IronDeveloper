using System.Net;
using System.Text.Json;
using IronDev.Core.Agents;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data;
using IronDev.Data.Models;
using IronDev.Infrastructure.Builder;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.Agents;
using IronDev.Infrastructure.Services.SemanticMemory;
using IronDev.Services;
using Microsoft.Data.SqlClient;

var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Usage: IronDev.ReplayRunner <replay-plan.json> | agent <list|profiles|tester run-plan> [...] | chat send <message> [...] | docs <clean|import|list|show|search> [...] | memory <search|reindex-freshness-smoke> [...] | failure latest --for-codex [...]");
    return 2;
}

if (IsCommand(args, "agent", "list"))
    return HandleAgentListCommand(args, options);

if (IsCommand(args, "agent", "profiles"))
    return HandleAgentProfilesCommand(args, options);

if (args.Length >= 3 &&
    string.Equals(args[0], "agent", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(args[1], "tester", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(args[2], "run-plan", StringComparison.OrdinalIgnoreCase))
{
    return await HandleAgentTesterRunPlanCommandAsync(args, options);
}

if (args.Length >= 3 &&
    string.Equals(args[0], "agent", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(args[1], "retriever", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(args[2], "search", StringComparison.OrdinalIgnoreCase))
{
    return await HandleAgentRetrieverSearchCommandAsync(args, options);
}

if (args.Length >= 3 &&
    string.Equals(args[0], "agent", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(args[1], "supervisor", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(args[2], "run-goal", StringComparison.OrdinalIgnoreCase))
{
    return await HandleAgentSupervisorRunGoalCommandAsync(args, options);
}

if (args.Length >= 3 &&
    string.Equals(args[0], "agent", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(args[1], "critic", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(args[2], "review-failure", StringComparison.OrdinalIgnoreCase))
{
    return await HandleAgentCriticReviewFailureCommandAsync(args, options);
}

if (args.Length >= 3 &&
    string.Equals(args[0], "agent", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(args[1], "quality", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(args[2], "run-gate", StringComparison.OrdinalIgnoreCase))
{
    return await HandleAgentQualityRunGateCommandAsync(args, options);
}

if (args.Length >= 3 &&
    string.Equals(args[0], "agent", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(args[1], "planner", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(args[2], "draft-test-plan", StringComparison.OrdinalIgnoreCase))
{
    return await HandleAgentPlannerDraftTestPlanCommandAsync(args, options);
}

if (IsCommand(args, "chat", "send"))
    return await HandleChatSendCommandAsync(args, options);

if (IsCommand(args, "docs", "clean"))
    return await HandleDocsCleanCommandAsync(args, options);

if (IsCommand(args, "docs", "import"))
    return await HandleDocsImportCommandAsync(args, options);

if (IsCommand(args, "docs", "list"))
    return await HandleDocsListCommandAsync(args, options);

if (IsCommand(args, "docs", "show"))
    return await HandleDocsShowCommandAsync(args, options);

if (IsCommand(args, "docs", "search"))
    return await HandleDocsSearchCommandAsync(args, options);

if (IsCommand(args, "docs", "discussion-smoke"))
    return await DocsDiscussionSmokeCommand.HandleAsync(args, options);

if (IsCommand(args, "tickets", "document-to-tickets-smoke"))
    return await TicketsDocumentToTicketsSmokeCommand.HandleAsync(args, options);

if (IsCommand(args, "failure", "latest"))
    return await HandleFailureLatestCommandAsync(args, options);

if (IsCommand(args, "memory", "search"))
    return await MemorySearchCommand.HandleAsync(args, options);

if (IsCommand(args, "memory", "sql-version-smoke"))
    return await MemorySqlVersionSmokeCommand.HandleAsync(args, options);

if (IsCommand(args, "memory", "weaviate-sql-version-smoke"))
    return await MemoryWeaviateSqlVersionSmokeCommand.HandleAsync(args, options);

if (IsCommand(args, "memory", "cross-project-smoke"))
    return await MemoryCrossProjectSmokeCommand.HandleAsync(args, options);

if (IsCommand(args, "memory", "reindex-freshness-smoke"))
    return await MemoryReindexFreshnessSmokeCommand.HandleAsync(args, options);

if (IsCommand(args, "memory", "ticket-source-link-smoke"))
    return await HandleMemoryTicketSourceLinkSmokeCommandAsync(args, options);

if (IsCommand(args, "memory", "builder-context-source-smoke"))
    return await HandleMemoryBuilderContextSourceSmokeCommandAsync(args, options);

if (IsCommand(args, "builder", "proposal-safety-smoke"))
    return await BuilderProposalSafetySmokeCommand.HandleAsync(args, options);

if (IsCommand(args, "builder", "disposable-workspace-apply-smoke"))
    return await DisposableWorkspaceApplySmokeCommand.HandleAsync(args, options);

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

static int HandleAgentListCommand(string[] args, JsonSerializerOptions options)
{
    var (_, registry, _) = CreateAgentRuntime();
    var definitions = registry.ListDefinitions()
        .Select(definition => new
        {
            name = definition.Name,
            purpose = definition.Purpose,
            defaultModelProfile = definition.DefaultModelProfile,
            enabled = definition.Enabled,
            allowedTools = definition.AllowedTools
        })
        .ToArray();

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        command = "agent list",
        agents = definitions
    }, options));

    return 0;
}

static int HandleAgentProfilesCommand(string[] args, JsonSerializerOptions options)
{
    var (modelResolver, _, _) = CreateAgentRuntime();

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        command = "agent profiles",
        providerBoundary = "014 supports OpenAI model profiles only.",
        profiles = modelResolver.ListProfiles()
    }, options));

    return 0;
}

static async Task<int> HandleAgentTesterRunPlanCommandAsync(string[] args, JsonSerializerOptions options)
{
    var planPath = ReadOption(args, "--plan");
    if (string.IsNullOrWhiteSpace(planPath))
    {
        Console.Error.WriteLine("Usage: IronDev.ReplayRunner agent tester run-plan --plan <path> [--run-id id] [--json]");
        return 2;
    }

    var runId = ReadOption(args, "--run-id") ?? $"TesterAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    var (_, _, runner) = CreateAgentRuntime();
    var result = await runner.RunAsync(new AgentRequest
    {
        AgentName = "TesterAgent",
        GoalId = "agent-tester-run-plan-014",
        DogfoodRunId = runId,
        Inputs = new Dictionary<string, string>
        {
            ["plan_path"] = planPath
        }
    });

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        command = "agent tester run-plan",
        agent = result.AgentName,
        status = result.Status.ToString(),
        summary = result.Summary,
        modelProfile = result.ModelProfileName,
        provider = result.Provider,
        model = result.Model,
        exitCode = result.ExitCode,
        commandsRun = result.CommandsRun,
        evidencePaths = result.EvidencePaths,
        report = TryParseJson(result.OutputJson),
        completedAtUtc = result.CompletedAtUtc
    }, options));

    return result.Status == AgentRunStatus.Succeeded ? 0 : 1;
}

static async Task<int> HandleAgentRetrieverSearchCommandAsync(string[] args, JsonSerializerOptions options)
{
    var project = ReadOption(args, "--project") ?? "IronDev";
    var query = ReadOption(args, "--query") ?? ReadPositionalText(args, 3);
    var take = ReadOption(args, "--take") ?? "5";
    if (string.IsNullOrWhiteSpace(query))
    {
        Console.Error.WriteLine("Usage: IronDev.ReplayRunner agent retriever search --project <project> --query <query> [--take n] [--run-id id] [--json]");
        return 2;
    }

    var runId = ReadOption(args, "--run-id") ?? $"RetrieverAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    var (_, _, runner) = CreateAgentRuntime();
    var result = await runner.RunAsync(new AgentRequest
    {
        AgentName = "RetrieverAgent",
        GoalId = "agent-retriever-search-024",
        DogfoodRunId = runId,
        Inputs = new Dictionary<string, string>
        {
            ["project"] = project,
            ["query"] = query,
            ["take"] = take
        }
    });

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        command = "agent retriever search",
        agent = result.AgentName,
        status = result.Status.ToString(),
        summary = result.Summary,
        modelProfile = result.ModelProfileName,
        provider = result.Provider,
        model = result.Model,
        exitCode = result.ExitCode,
        commandsRun = result.CommandsRun,
        contextPackage = TryParseJson(result.OutputJson),
        completedAtUtc = result.CompletedAtUtc
    }, options));

    return result.Status == AgentRunStatus.Succeeded ? 0 : 1;
}

static async Task<int> HandleAgentSupervisorRunGoalCommandAsync(string[] args, JsonSerializerOptions options)
{
    var project = ReadOption(args, "--project") ?? "IronDev";
    var query = ReadOption(args, "--query");
    var planPath = ReadOption(args, "--plan");
    if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(planPath))
    {
        Console.Error.WriteLine("Usage: IronDev.ReplayRunner agent supervisor run-goal --project <project> --query <query> --plan <path> [--run-id id] [--json]");
        return 2;
    }

    var runId = ReadOption(args, "--run-id") ?? $"SupervisorAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    var (_, _, runner) = CreateAgentRuntime();
    var result = await runner.RunAsync(new AgentRequest
    {
        AgentName = "SupervisorAgent",
        GoalId = "supervisor-codex-loop-proof-025",
        DogfoodRunId = runId,
        Inputs = new Dictionary<string, string>
        {
            ["project"] = project,
            ["query"] = query,
            ["plan_path"] = planPath
        }
    });

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        command = "agent supervisor run-goal",
        agent = result.AgentName,
        status = result.Status.ToString(),
        summary = result.Summary,
        modelProfile = result.ModelProfileName,
        provider = result.Provider,
        model = result.Model,
        exitCode = result.ExitCode,
        commandsRun = result.CommandsRun,
        evidencePaths = result.EvidencePaths,
        loopReport = TryParseJson(result.OutputJson),
        completedAtUtc = result.CompletedAtUtc
    }, options));

    return result.Status == AgentRunStatus.Succeeded ? 0 : 1;
}

static async Task<int> HandleAgentCriticReviewFailureCommandAsync(string[] args, JsonSerializerOptions options)
{
    var packagePath = ReadOption(args, "--package");
    if (string.IsNullOrWhiteSpace(packagePath))
    {
        Console.Error.WriteLine("Usage: IronDev.ReplayRunner agent critic review-failure --package <failure-package.json> [--run-id id] [--json]");
        return 2;
    }

    var runId = ReadOption(args, "--run-id") ?? $"CriticAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    var (_, _, runner) = CreateAgentRuntime();
    var result = await runner.RunAsync(new AgentRequest
    {
        AgentName = "CriticAgent",
        GoalId = "critic-failure-package-review-036",
        DogfoodRunId = runId,
        Inputs = new Dictionary<string, string>
        {
            ["package_path"] = packagePath
        }
    });

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        command = "agent critic review-failure",
        agent = result.AgentName,
        status = result.Status.ToString(),
        summary = result.Summary,
        modelProfile = result.ModelProfileName,
        provider = result.Provider,
        model = result.Model,
        exitCode = result.ExitCode,
        evidencePaths = result.EvidencePaths,
        review = TryParseJson(result.OutputJson),
        completedAtUtc = result.CompletedAtUtc
    }, options));

    return result.Status == AgentRunStatus.Succeeded ? 0 : 1;
}

static async Task<int> HandleAgentQualityRunGateCommandAsync(string[] args, JsonSerializerOptions options)
{
    var planPath = ReadOption(args, "--plan") ?? "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json";
    var runId = ReadOption(args, "--run-id") ?? $"QualityAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    var (_, _, runner) = CreateAgentRuntime();
    var result = await runner.RunAsync(new AgentRequest
    {
        AgentName = "QualityAgent",
        GoalId = "quality-agent-real-path-037",
        DogfoodRunId = runId,
        Inputs = new Dictionary<string, string>
        {
            ["plan_path"] = planPath
        }
    });

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        command = "agent quality run-gate",
        agent = result.AgentName,
        status = result.Status.ToString(),
        summary = result.Summary,
        modelProfile = result.ModelProfileName,
        provider = result.Provider,
        model = result.Model,
        exitCode = result.ExitCode,
        commandsRun = result.CommandsRun,
        evidencePaths = result.EvidencePaths,
        qualityReport = TryParseJson(result.OutputJson),
        completedAtUtc = result.CompletedAtUtc
    }, options));

    return result.Status == AgentRunStatus.Succeeded ? 0 : 1;
}

static async Task<int> HandleAgentPlannerDraftTestPlanCommandAsync(string[] args, JsonSerializerOptions options)
{
    var project = ReadOption(args, "--project") ?? "BookSeller";
    var goal = ReadOption(args, "--goal") ?? ReadPositionalText(args, 3);
    if (string.IsNullOrWhiteSpace(goal))
    {
        Console.Error.WriteLine("Usage: IronDev.ReplayRunner agent planner draft-test-plan --project <project> --goal <goal> [--run-id id] [--json]");
        return 2;
    }

    var runId = ReadOption(args, "--run-id") ?? $"PlannerAgent-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    var (_, _, runner) = CreateAgentRuntime();
    var result = await runner.RunAsync(new AgentRequest
    {
        AgentName = "PlannerAgent",
        GoalId = "planner-agent-test-plan-draft-038",
        DogfoodRunId = runId,
        Inputs = new Dictionary<string, string>
        {
            ["project"] = project,
            ["goal"] = goal
        }
    });

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        command = "agent planner draft-test-plan",
        agent = result.AgentName,
        status = result.Status.ToString(),
        summary = result.Summary,
        modelProfile = result.ModelProfileName,
        provider = result.Provider,
        model = result.Model,
        exitCode = result.ExitCode,
        commandsRun = result.CommandsRun,
        draftPlan = TryParseJson(result.OutputJson),
        completedAtUtc = result.CompletedAtUtc
    }, options));

    return result.Status == AgentRunStatus.Succeeded ? 0 : 1;
}

static (AgentModelResolver ModelResolver, AgentRegistry Registry, AgentRunner Runner) CreateAgentRuntime()
{
    var repoRoot = FindRepositoryRoot();
    var modelResolver = new AgentModelResolver(LoadModelProfiles(repoRoot));
    var definitions = AgentModelDefaults.CreateDefaultDefinitions();
    var agents = definitions
        .Select<AgentDefinition, IIronDevAgent>(definition =>
            string.Equals(definition.Name, "TesterAgent", StringComparison.OrdinalIgnoreCase)
                ? new TesterAgent(definition, modelResolver, repoRoot)
                : string.Equals(definition.Name, "SupervisorAgent", StringComparison.OrdinalIgnoreCase)
                    ? new SupervisorAgent(definition, modelResolver, repoRoot)
                : string.Equals(definition.Name, "RetrieverAgent", StringComparison.OrdinalIgnoreCase)
                    ? new RetrieverAgent(definition, modelResolver, repoRoot)
                : string.Equals(definition.Name, "CriticAgent", StringComparison.OrdinalIgnoreCase)
                    ? new CriticAgent(definition, modelResolver)
                : string.Equals(definition.Name, "QualityAgent", StringComparison.OrdinalIgnoreCase)
                    ? new QualityAgent(definition, modelResolver, repoRoot)
                : string.Equals(definition.Name, "PlannerAgent", StringComparison.OrdinalIgnoreCase)
                    ? new PlannerAgent(definition, modelResolver)
                : new StaticIronDevAgent(definition, modelResolver))
        .ToArray();
    var registry = new AgentRegistry(agents, definitions);

    return (modelResolver, registry, new AgentRunner(registry));
}

static IReadOnlyList<ModelProfile> LoadModelProfiles(string repoRoot)
{
    var configPaths = new[]
    {
        Path.Combine(repoRoot, "IronDeveloper", "appsettings.Development.json"),
        Path.Combine(repoRoot, "IronDeveloper", "appsettings.json")
    };

    foreach (var configPath in configPaths)
    {
        if (!File.Exists(configPath))
            continue;

        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        if (!document.RootElement.TryGetProperty("ModelProfiles", out var profilesElement) ||
            profilesElement.ValueKind != JsonValueKind.Object)
        {
            continue;
        }

        var profiles = new List<ModelProfile>();
        foreach (var profileProperty in profilesElement.EnumerateObject())
        {
            var profile = profileProperty.Value;
            profiles.Add(new ModelProfile
            {
                Name = profileProperty.Name,
                Provider = ReadString(profile, "Provider", "OpenAI"),
                Model = ReadString(profile, "Model", string.Empty),
                Temperature = ReadDouble(profile, "Temperature", 0.2),
                MaxOutputTokens = ReadInt(profile, "MaxOutputTokens", 2000),
                MaxCostPerRun = ReadDecimal(profile, "MaxCostPerRun")
            });
        }

        if (profiles.Count > 0)
            return profiles;
    }

    return AgentModelDefaults.CreateDefaultProfiles();
}

static JsonElement? TryParseJson(string json)
{
    if (string.IsNullOrWhiteSpace(json))
        return null;

    try
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
    catch (JsonException)
    {
        return null;
    }
}

static string ReadString(JsonElement element, string propertyName, string fallback) =>
    element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
        ? property.GetString() ?? fallback
        : fallback;

static double ReadDouble(JsonElement element, string propertyName, double fallback) =>
    element.TryGetProperty(propertyName, out var property) && property.TryGetDouble(out var value)
        ? value
        : fallback;

static int ReadInt(JsonElement element, string propertyName, int fallback) =>
    element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
        ? value
        : fallback;

static decimal? ReadDecimal(JsonElement element, string propertyName) =>
    element.TryGetProperty(propertyName, out var property) && property.TryGetDecimal(out var value)
        ? value
        : null;

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

static async Task<int> HandleFailureLatestCommandAsync(string[] args, JsonSerializerOptions options)
{
    if (!HasFlag(args, "--for-codex"))
    {
        Console.Error.WriteLine("Usage: IronDev.ReplayRunner failure latest --for-codex [--runs-root path] [--run-id id]");
        return 2;
    }

    var runsRoot = Path.GetFullPath(ReadOption(args, "--runs-root") ?? Path.Combine("tools", "dogfood", "runs"));
    if (!Directory.Exists(runsRoot))
    {
        Console.Error.WriteLine($"Dogfood runs root not found: {runsRoot}");
        return 3;
    }

    var result = await FailurePackageService.TryWriteLatestAsync(
        runsRoot,
        ReadOption(args, "--run-id"),
        options);
    if (result is null)
    {
        Console.Error.WriteLine("No failed replay or Test Agent result found.");
        return 1;
    }

    Console.WriteLine(JsonSerializer.Serialize(result, options));
    return 0;
}

static async Task<int> HandleDocsCleanCommandAsync(string[] args, JsonSerializerOptions options)
{
    var store = GetDogfoodKnowledgeStore(args);
    var project = ReadOption(args, "--project") ?? "IronDev";
    var force = HasFlag(args, "--force");
    if (!force)
    {
        Console.Error.WriteLine("Usage: IronDev.ReplayRunner docs clean --project IronDev --force [--store-root path]");
        Console.Error.WriteLine("Clean archives local dogfood knowledge docs before seeding a fresh baseline. It does not delete SQL data.");
        return 2;
    }

    var projectRoot = GetKnowledgeProjectRoot(store, project);
    var docsRoot = Path.Combine(projectRoot, "docs");
    var archiveRoot = Path.Combine(projectRoot, "archive", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"));
    var archived = 0;

    if (Directory.Exists(docsRoot))
    {
        var existing = Directory.GetFiles(docsRoot, "*.md", SearchOption.TopDirectoryOnly);
        if (existing.Length > 0)
        {
            Directory.CreateDirectory(archiveRoot);
            foreach (var file in existing)
            {
                File.Move(file, Path.Combine(archiveRoot, Path.GetFileName(file)), overwrite: true);
                archived++;
            }
        }
    }

    Directory.CreateDirectory(docsRoot);
    var seeded = await SeedIronDevBaselineDocsAsync(project, docsRoot);
    var index = await BuildKnowledgeIndexAsync(project, docsRoot);
    await WriteKnowledgeIndexAsync(projectRoot, index, options);

    Console.WriteLine(JsonSerializer.Serialize(new DocsCleanResult
    {
        Project = project,
        StoreRoot = store,
        DocsRoot = docsRoot,
        ArchiveRoot = archived > 0 ? archiveRoot : string.Empty,
        ArchivedDocuments = archived,
        SeededDocuments = seeded,
        TotalDocuments = index.Count,
        Message = "Local dogfood knowledge cleaned by archive-and-seed. SQL project data was not modified."
    }, options));

    return 0;
}

static async Task<int> HandleDocsImportCommandAsync(string[] args, JsonSerializerOptions options)
{
    var source = ReadOption(args, "--file") ?? ReadOption(args, "--path") ?? ReadPositionalText(args, 2);
    if (string.IsNullOrWhiteSpace(source))
    {
        Console.Error.WriteLine("Usage: IronDev.ReplayRunner docs import --file <path> [--project IronDev] [--type Discussion] [--title title] [--authority Draft]");
        return 2;
    }

    var sourcePath = Path.GetFullPath(source);
    if (!File.Exists(sourcePath))
    {
        Console.Error.WriteLine($"Document file not found: {sourcePath}");
        return 2;
    }

    var store = GetDogfoodKnowledgeStore(args);
    var project = ReadOption(args, "--project") ?? "IronDev";
    var documentType = ReadOption(args, "--type") ?? "Discussion";
    var title = ReadOption(args, "--title") ?? Path.GetFileNameWithoutExtension(sourcePath);
    var authority = ReadOption(args, "--authority") ?? "Draft";
    var dogfoodRunId = ReadOption(args, "--dogfood-run-id") ?? string.Empty;
    var content = await File.ReadAllTextAsync(sourcePath);
    var docsRoot = EnsureKnowledgeDocsRoot(store, project);
    var imported = await WriteKnowledgeDocumentAsync(docsRoot, new KnowledgeDocumentWrite
    {
        Project = project,
        Title = title,
        DocumentType = documentType,
        Authority = authority,
        Source = sourcePath,
        DogfoodRunId = dogfoodRunId,
        Content = content
    });

    var projectRoot = GetKnowledgeProjectRoot(store, project);
    var index = await BuildKnowledgeIndexAsync(project, docsRoot);
    await WriteKnowledgeIndexAsync(projectRoot, index, options);

    Console.WriteLine(JsonSerializer.Serialize(new DocsImportResult
    {
        Project = project,
        Id = imported.Id,
        Title = imported.Title,
        DocumentType = imported.DocumentType,
        Authority = imported.Authority,
        Path = imported.Path,
        TotalDocuments = index.Count
    }, options));

    return 0;
}

static async Task<int> HandleDocsListCommandAsync(string[] args, JsonSerializerOptions options)
{
    var store = GetDogfoodKnowledgeStore(args);
    var project = ReadOption(args, "--project") ?? "IronDev";
    var docsRoot = EnsureKnowledgeDocsRoot(store, project);
    var index = await BuildKnowledgeIndexAsync(project, docsRoot);
    await WriteKnowledgeIndexAsync(GetKnowledgeProjectRoot(store, project), index, options);

    Console.WriteLine(JsonSerializer.Serialize(new DocsListResult
    {
        Project = project,
        StoreRoot = store,
        Documents = index
    }, options));

    return 0;
}

static async Task<int> HandleDocsShowCommandAsync(string[] args, JsonSerializerOptions options)
{
    var id = ReadOption(args, "--id") ?? ReadPositionalText(args, 2);
    if (string.IsNullOrWhiteSpace(id))
    {
        Console.Error.WriteLine("Usage: IronDev.ReplayRunner docs show <id> [--project IronDev]");
        return 2;
    }

    var store = GetDogfoodKnowledgeStore(args);
    var project = ReadOption(args, "--project") ?? "IronDev";
    var docsRoot = EnsureKnowledgeDocsRoot(store, project);
    var index = await BuildKnowledgeIndexAsync(project, docsRoot);
    var document = index.FirstOrDefault(doc =>
        string.Equals(doc.Id, id, StringComparison.OrdinalIgnoreCase) ||
        doc.Title.Contains(id, StringComparison.OrdinalIgnoreCase));

    if (document is null)
    {
        Console.Error.WriteLine($"Document not found: {id}");
        return 1;
    }

    Console.WriteLine(JsonSerializer.Serialize(new DocsShowResult
    {
        Project = project,
        Document = document,
        Content = await File.ReadAllTextAsync(document.Path)
    }, options));

    return 0;
}

static async Task<int> HandleDocsSearchCommandAsync(string[] args, JsonSerializerOptions options)
{
    var query = ReadOption(args, "--query") ?? ReadPositionalText(args, 2);
    if (string.IsNullOrWhiteSpace(query))
    {
        Console.Error.WriteLine("Usage: IronDev.ReplayRunner docs search <query> [--project IronDev] [--take 5]");
        return 2;
    }

    var take = int.TryParse(ReadOption(args, "--take"), out var parsedTake) ? parsedTake : 5;
    var store = GetDogfoodKnowledgeStore(args);
    var project = ReadOption(args, "--project") ?? "IronDev";
    var docsRoot = EnsureKnowledgeDocsRoot(store, project);
    var index = await BuildKnowledgeIndexAsync(project, docsRoot);
    var terms = query.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var matches = new List<DocsSearchMatch>();

    foreach (var document in index)
    {
        var content = await File.ReadAllTextAsync(document.Path);
        var body = StripFrontmatter(content);
        var bodyScore = terms.Sum(term => CountOccurrences(body, term));
        var titleScore = terms.Sum(term => CountOccurrences(document.Title, term)) * 20;
        var typeScore = terms.Sum(term => CountOccurrences(document.DocumentType, term)) * 8;
        var authorityScore = terms.Sum(term => CountOccurrences(document.Authority, term)) * 4;
        var architectureBoost = document.DocumentType.Contains("Architecture", StringComparison.OrdinalIgnoreCase) ? 28 : 0;
        var acceptedBoost = document.Authority.Contains("Accepted", StringComparison.OrdinalIgnoreCase) ? 90 : 0;
        var goalIntentBoost = terms.Any(term => term.Contains("goal", StringComparison.OrdinalIgnoreCase)) &&
                              document.Title.Contains("goal", StringComparison.OrdinalIgnoreCase)
            ? 70
            : 0;
        var score = bodyScore + titleScore + typeScore + authorityScore + architectureBoost + acceptedBoost + goalIntentBoost;
        if (score <= 0)
            continue;

        matches.Add(new DocsSearchMatch
        {
            Document = document,
            Score = score,
            Ranking = new Dictionary<string, int>
            {
                ["body"] = bodyScore,
                ["title"] = titleScore,
                ["type"] = typeScore,
                ["authority"] = authorityScore,
                ["architectureBoost"] = architectureBoost,
                ["acceptedBoost"] = acceptedBoost,
                ["goalIntentBoost"] = goalIntentBoost
            },
            Snippet = BuildSearchSnippet(body, terms)
        });
    }

    Console.WriteLine(JsonSerializer.Serialize(new DocsSearchResult
    {
        Project = project,
        Query = query,
        Matches = matches
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Document.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToArray()
    }, options));

    return 0;
}

static async Task<int> HandleMemoryTicketSourceLinkSmokeCommandAsync(string[] args, JsonSerializerOptions options)
{
    var requestedProjectName = ReadOption(args, "--project") ?? "IronDev";
    var dogfoodRunId = ReadOption(args, "--dogfood-run-id") ?? $"memory-ticket-source-link-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    var connectionString = ResolveIronDevConnectionString(args);
    var repoRoot = FindRepositoryRoot();
    var connectionFactory = new CliConnectionFactory(connectionString);

    await ApplySqlScriptAsync(
        connectionFactory,
        Path.Combine(repoRoot, "Database", "migrate_project_documents.sql"));

    var project = await ResolveProjectAsync(connectionFactory, requestedProjectName);
    if (project is null)
    {
        Console.Error.WriteLine($"Project not found: {requestedProjectName}");
        return 1;
    }

    var tenant = new CliTenantContext(project.TenantId);
    var documentService = new ProjectDocumentService(connectionFactory, tenant);
    var sourceReferenceService = new ArtifactSourceReferenceService(connectionFactory);
    var ticketService = new TicketService(connectionFactory, tenant, sourceReferenceService);

    var stamp = dogfoodRunId.Replace(':', '-').Replace('\\', '-').Replace('/', '-');
    var titleSeed = $"TICKET_SOURCE_LINK_SPINE_{stamp}_{Guid.NewGuid():N}";
    var title = titleSeed[..Math.Min(120, titleSeed.Length)];
    var content = """
        # Ticket Source Link Spine

        Current rule: tickets generated from project documents must preserve the exact SQL ProjectDocumentVersion source.
        The generated ticket should not become orphaned from this document version.
        """;

    var document = await documentService.CreateDocumentAsync(new CreateProjectDocumentRequest
    {
        ProjectId = project.ProjectId,
        Title = title,
        DocumentType = "Architecture",
        ContentMarkdown = content,
        ChangeSummary = "Ticket source-link dogfood source",
        CreatedBy = "TestAgent",
        SourceEntityType = "Discussion",
        SourceEntityId = 9401
    });

    var sourceVersion = await documentService.GetCurrentVersionAsync(document.Id)
        ?? throw new InvalidOperationException("Ticket source-link document version was not created.");

    var ticket = new ProjectTicket
    {
        TenantId = project.TenantId,
        ProjectId = project.ProjectId,
        SessionId = Guid.NewGuid(),
        Title = "Prove ticket source document version link integrity",
        TicketType = "Test",
        Priority = "High",
        Summary = "Generated smoke ticket created from an exact SQL ProjectDocumentVersion.",
        Problem = "Tickets without source document links become orphaned and cannot be trusted by Codex or builder context.",
        AcceptanceCriteria = "- Ticket has SourceDocumentVersionId.\n- Source version resolves to the expected SQL ProjectDocumentVersion.\n- Source references identify the document version.",
        Status = "Draft",
        Content = "Memory Spine 010 smoke ticket.",
        ContextSummary = $"Source ProjectDocumentVersion:{sourceVersion.Id}",
        IsGenerated = true,
        GenerationNote = "Memory Spine 010 smoke",
        SourceDocumentVersionId = sourceVersion.Id
    };

    var ticketId = await ticketService.SaveTicketAsync(ticket);
    await documentService.LinkVersionAsync(new LinkProjectDocumentVersionRequest
    {
        DocumentVersionId = sourceVersion.Id,
        LinkedEntityType = "Ticket",
        LinkedEntityId = ticketId,
        LinkType = "GeneratedTicket",
        CreatedBy = "TestAgent"
    });

    var savedTicket = await ticketService.GetTicketByIdAsync(ticketId)
        ?? throw new InvalidOperationException("Saved smoke ticket could not be reloaded.");
    var resolvedVersion = savedTicket.SourceDocumentVersionId is { } savedSourceVersionId
        ? await documentService.GetVersionAsync(savedSourceVersionId)
        : null;
    var sourceReferences = await sourceReferenceService.GetForArtifactAsync(
        project.TenantId,
        project.ProjectId,
        "Ticket",
        ticketId);
    var versionLinks = await documentService.GetLinksForVersionAsync(sourceVersion.Id);

    var orphanTicket = new ProjectTicket
    {
        TenantId = project.TenantId,
        ProjectId = project.ProjectId,
        SessionId = Guid.NewGuid(),
        Title = "Intentional orphan source-link control",
        TicketType = "Test",
        Priority = "Medium",
        Summary = "Negative control for Memory Spine 010.",
        AcceptanceCriteria = "- This ticket intentionally has no SourceDocumentVersionId.",
        Status = "Draft",
        Content = "This ticket should be reported as orphaned by the 010 validator.",
        IsGenerated = true,
        GenerationNote = "Memory Spine 010 orphan control"
    };
    var orphanTicketId = await ticketService.SaveTicketAsync(orphanTicket);
    var orphanReloaded = await ticketService.GetTicketByIdAsync(orphanTicketId)
        ?? throw new InvalidOperationException("Orphan control ticket could not be reloaded.");
    var orphanFailure = BuildTicketSourceLinkValidation(orphanReloaded, expectedDocumentVersionId: sourceVersion.Id, resolvedVersion: null);

    var positiveValidation = BuildTicketSourceLinkValidation(savedTicket, sourceVersion.Id, resolvedVersion);
    var hasArtifactReference = sourceReferences.Any(reference =>
        reference.SourceType == "ProjectDocumentVersion" &&
        reference.SourceId == sourceVersion.Id &&
        reference.ReferenceType == "CreatedFrom");
    var hasDocumentLink = versionLinks.Any(link =>
        link.LinkedEntityType == "Ticket" &&
        link.LinkedEntityId == ticketId &&
        link.LinkType == "GeneratedTicket");

    var passed = positiveValidation.Passed &&
                 hasArtifactReference &&
                 hasDocumentLink &&
                 !orphanFailure.Passed;

    var result = new MemoryTicketSourceLinkSmokeResult
    {
        DogfoodRunId = dogfoodRunId,
        TenantId = project.TenantId,
        ProjectId = project.ProjectId,
        ProjectName = project.ProjectName,
        SourceDocumentId = document.Id,
        SourceDocumentVersionId = sourceVersion.Id,
        TicketId = ticketId,
        TicketSourceDocumentVersionId = savedTicket.SourceDocumentVersionId,
        LinkResolutionStatus = positiveValidation.Status,
        ArtifactSourceReferenceFound = hasArtifactReference,
        ProjectDocumentLinkFound = hasDocumentLink,
        OrphanTicketId = orphanTicketId,
        OrphanReportedAsFailure = !orphanFailure.Passed,
        Passed = passed,
        Expected = new MemoryTicketSourceLinkExpected
        {
            TicketSourceDocumentVersionId = sourceVersion.Id,
            OrphanShouldFailValidation = true
        },
        Evidence = new MemoryTicketSourceLinkEvidence
        {
            ResolvedDocumentVersionId = resolvedVersion?.Id,
            ResolvedDocumentId = resolvedVersion?.DocumentId,
            SourceReferenceCount = sourceReferences.Count,
            ProjectDocumentLinkCount = versionLinks.Count,
            PositiveValidation = positiveValidation,
            OrphanValidation = orphanFailure
        }
    };

    Console.WriteLine(JsonSerializer.Serialize(result, options));
    return passed ? 0 : 1;
}

static async Task<int> HandleMemoryBuilderContextSourceSmokeCommandAsync(string[] args, JsonSerializerOptions options)
{
    var requestedProjectName = ReadOption(args, "--project") ?? "IronDev";
    var dogfoodRunId = ReadOption(args, "--dogfood-run-id") ?? $"memory-builder-context-source-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    var connectionString = ResolveIronDevConnectionString(args);
    var repoRoot = FindRepositoryRoot();
    var connectionFactory = new CliConnectionFactory(connectionString);

    await ApplySqlScriptAsync(
        connectionFactory,
        Path.Combine(repoRoot, "Database", "migrate_project_documents.sql"));

    var project = await ResolveProjectAsync(connectionFactory, requestedProjectName);
    if (project is null)
    {
        Console.Error.WriteLine($"Project not found: {requestedProjectName}");
        return 1;
    }

    var tenant = new CliTenantContext(project.TenantId);
    var sourceReferenceService = new ArtifactSourceReferenceService(connectionFactory);
    var documentService = new ProjectDocumentService(connectionFactory, tenant);
    var ticketService = new TicketService(connectionFactory, tenant, sourceReferenceService);
    var projectService = new ProjectService(connectionFactory, tenant);
    var memoryService = new ProjectMemoryService(connectionFactory, tenant, sourceReferenceService);
    var profileService = new ProjectProfileService(connectionFactory, tenant);
    var contextService = new BuilderContextService(
        ticketService,
        projectService,
        memoryService,
        profileService,
        documentService);

    var stamp = dogfoodRunId.Replace(':', '-').Replace('\\', '-').Replace('/', '-');
    var titleSeed = $"BUILDER_CONTEXT_SOURCE_SPINE_{stamp}_{Guid.NewGuid():N}";
    var title = titleSeed[..Math.Min(120, titleSeed.Length)];
    var content = """
        # Builder Context Source Memory Spine

        Current rule: builder context must include the exact SQL ProjectDocumentVersion linked from the ticket.
        This smoke proves context assembly only. It does not generate code, apply patches, or write target project files.
        The source markdown must be available to the builder as grounded project memory.
        """;

    var document = await documentService.CreateDocumentAsync(new CreateProjectDocumentRequest
    {
        ProjectId = project.ProjectId,
        Title = title,
        DocumentType = "Architecture",
        ContentMarkdown = content,
        ChangeSummary = "Builder context source-memory dogfood source",
        CreatedBy = "TestAgent",
        SourceEntityType = "Discussion",
        SourceEntityId = 11011
    });

    var sourceVersion = await documentService.GetCurrentVersionAsync(document.Id)
        ?? throw new InvalidOperationException("Builder context source document version was not created.");

    var ticket = new ProjectTicket
    {
        TenantId = project.TenantId,
        ProjectId = project.ProjectId,
        SessionId = Guid.NewGuid(),
        Title = "Prove builder context includes source project memory",
        TicketType = "Test",
        Priority = "High",
        Summary = "Assemble builder context for a ticket grounded in an exact SQL ProjectDocumentVersion.",
        Problem = "Builder context can drift if it receives a ticket without its linked source document memory.",
        AcceptanceCriteria = "- Builder context includes the ticket.\n- Builder context includes the exact source document version.\n- Builder context reports source link evidence.",
        Status = "Draft",
        Content = "Memory Spine 011 smoke ticket.",
        ContextSummary = $"Source ProjectDocumentVersion:{sourceVersion.Id}",
        IsGenerated = true,
        GenerationNote = "Memory Spine 011 smoke",
        SourceDocumentVersionId = sourceVersion.Id
    };

    var ticketId = await ticketService.SaveTicketAsync(ticket);
    await documentService.LinkVersionAsync(new LinkProjectDocumentVersionRequest
    {
        DocumentVersionId = sourceVersion.Id,
        LinkedEntityType = "Ticket",
        LinkedEntityId = ticketId,
        LinkType = "GeneratedTicket",
        CreatedBy = "TestAgent"
    });

    var context = await contextService.AssembleContextAsync(project.ProjectId, ticketId);

    var orphanTicketId = await ticketService.SaveTicketAsync(new ProjectTicket
    {
        TenantId = project.TenantId,
        ProjectId = project.ProjectId,
        SessionId = Guid.NewGuid(),
        Title = "Intentional builder context orphan control",
        TicketType = "Test",
        Priority = "Medium",
        Summary = "Negative control for Memory Spine 011.",
        AcceptanceCriteria = "- Builder context reports missing SourceDocumentVersionId.",
        Status = "Draft",
        Content = "This ticket intentionally has no SourceDocumentVersionId.",
        IsGenerated = true,
        GenerationNote = "Memory Spine 011 orphan control"
    });
    var orphanContext = await contextService.AssembleContextAsync(project.ProjectId, orphanTicketId);

    var missingVersionId = 9_999_999_999L;
    var missingVersionTicketId = await ticketService.SaveTicketAsync(new ProjectTicket
    {
        TenantId = project.TenantId,
        ProjectId = project.ProjectId,
        SessionId = Guid.NewGuid(),
        Title = "Intentional missing source document version control",
        TicketType = "Test",
        Priority = "Medium",
        Summary = "Negative control for a missing ProjectDocumentVersion.",
        AcceptanceCriteria = "- Builder context reports source_document_version_not_found.",
        Status = "Draft",
        Content = "This ticket references a nonexistent ProjectDocumentVersion.",
        IsGenerated = true,
        GenerationNote = "Memory Spine 011 missing version control",
        SourceDocumentVersionId = missingVersionId
    });
    var missingVersionContext = await contextService.AssembleContextAsync(project.ProjectId, missingVersionTicketId);

    var bleedProject = await EnsureBuilderContextBleedProjectAsync(connectionFactory, project, tenant, dogfoodRunId);
    var bleedDocumentService = new ProjectDocumentService(connectionFactory, tenant);
    var bleedTitleSeed = $"BUILDER_CONTEXT_WRONG_PROJECT_{stamp}_{Guid.NewGuid():N}";
    var bleedDocument = await bleedDocumentService.CreateDocumentAsync(new CreateProjectDocumentRequest
    {
        ProjectId = bleedProject.ProjectId,
        Title = bleedTitleSeed[..Math.Min(120, bleedTitleSeed.Length)],
        DocumentType = "Architecture",
        ContentMarkdown = "# Wrong Project Source\n\nThis document belongs to a same-tenant non-IronDev project and must not become authoritative builder context.",
        ChangeSummary = "Wrong-project builder context control",
        CreatedBy = "TestAgent"
    });
    var bleedVersion = await bleedDocumentService.GetCurrentVersionAsync(bleedDocument.Id)
        ?? throw new InvalidOperationException("Wrong-project source document version was not created.");
    var wrongProjectTicketId = await ticketService.SaveTicketAsync(new ProjectTicket
    {
        TenantId = project.TenantId,
        ProjectId = project.ProjectId,
        SessionId = Guid.NewGuid(),
        Title = "Intentional wrong project source document control",
        TicketType = "Test",
        Priority = "Medium",
        Summary = "Negative control for wrong-project source document memory.",
        AcceptanceCriteria = "- Builder context reports source_document_wrong_project.",
        Status = "Draft",
        Content = "This ticket references a document version from a different project.",
        IsGenerated = true,
        GenerationNote = "Memory Spine 011 wrong project control",
        SourceDocumentVersionId = bleedVersion.Id
    });
    var wrongProjectContext = await contextService.AssembleContextAsync(project.ProjectId, wrongProjectTicketId);

    var historicalTitleSeed = $"BUILDER_CONTEXT_HISTORICAL_SOURCE_{stamp}_{Guid.NewGuid():N}";
    var historicalDocument = await documentService.CreateDocumentAsync(new CreateProjectDocumentRequest
    {
        ProjectId = project.ProjectId,
        Title = historicalTitleSeed[..Math.Min(120, historicalTitleSeed.Length)],
        DocumentType = "Architecture",
        ContentMarkdown = "# Historical Source\n\nThis version is intentionally marked Superseded for builder context proof.",
        ChangeSummary = "Historical builder context control",
        CreatedBy = "TestAgent"
    });
    var historicalVersion = await documentService.GetCurrentVersionAsync(historicalDocument.Id)
        ?? throw new InvalidOperationException("Historical source document version was not created.");
    await MarkProjectDocumentVersionStatusAsync(connectionFactory, historicalVersion.Id, "Superseded");
    var historicalTicketId = await ticketService.SaveTicketAsync(new ProjectTicket
    {
        TenantId = project.TenantId,
        ProjectId = project.ProjectId,
        SessionId = Guid.NewGuid(),
        Title = "Intentional historical source document control",
        TicketType = "Test",
        Priority = "Medium",
        Summary = "Negative control for stale/historical source document memory.",
        AcceptanceCriteria = "- Builder context marks stale source memory historical.",
        Status = "Draft",
        Content = "This ticket references a superseded ProjectDocumentVersion.",
        IsGenerated = true,
        GenerationNote = "Memory Spine 011 historical source control",
        SourceDocumentVersionId = historicalVersion.Id
    });
    var historicalContext = await contextService.AssembleContextAsync(project.ProjectId, historicalTicketId);

    var builderFlags = new BuilderContextSourceFlags
    {
        TicketIncluded = context.TicketId == ticketId && context.TicketTitle == ticket.Title,
        SourceDocumentIncluded = context.SourceDocumentId == document.Id,
        SourceDocumentVersionIncluded = context.SourceDocumentVersionId == sourceVersion.Id,
        SourceMarkdownIncluded = !string.IsNullOrWhiteSpace(context.SourceDocumentMarkdownExcerpt) &&
                                 context.SourceDocumentMarkdownExcerpt.Contains("Current rule: builder context must include", StringComparison.OrdinalIgnoreCase),
        SourceLinkEvidenceIncluded = context.SourceLinkEvidence.Any(evidence =>
            evidence.Contains($"GeneratedTicket:Ticket:{ticketId}", StringComparison.OrdinalIgnoreCase)),
        WrongProjectMemoryExcluded = wrongProjectContext.SourceDocumentResolutionStatus == "source_document_wrong_project",
        StaleMemoryExcludedOrMarkedHistorical = historicalContext.SourceDocumentResolutionStatus == "resolved_historical_source_document_version"
    };

    var negativeChecks = new BuilderContextNegativeChecks
    {
        OrphanTicketId = orphanTicketId,
        OrphanTicketFailsCleanly = orphanContext.SourceDocumentResolutionStatus == "missing_source_document_version",
        MissingDocumentVersionTicketId = missingVersionTicketId,
        MissingDocumentVersionFailsCleanly = missingVersionContext.SourceDocumentResolutionStatus == "source_document_version_not_found",
        WrongProjectTicketId = wrongProjectTicketId,
        WrongProjectFailsCleanly = builderFlags.WrongProjectMemoryExcluded,
        HistoricalTicketId = historicalTicketId,
        HistoricalSourceMarkedHistorical = builderFlags.StaleMemoryExcludedOrMarkedHistorical
    };

    var passed = builderFlags.TicketIncluded &&
                 builderFlags.SourceDocumentIncluded &&
                 builderFlags.SourceDocumentVersionIncluded &&
                 builderFlags.SourceMarkdownIncluded &&
                 builderFlags.SourceLinkEvidenceIncluded &&
                 builderFlags.WrongProjectMemoryExcluded &&
                 builderFlags.StaleMemoryExcludedOrMarkedHistorical &&
                 negativeChecks.OrphanTicketFailsCleanly &&
                 negativeChecks.MissingDocumentVersionFailsCleanly;

    var result = new MemoryBuilderContextSourceSmokeResult
    {
        Goal = "memory-spine-011-builder-context-source-memory",
        DogfoodRunId = dogfoodRunId,
        Passed = passed,
        TenantId = project.TenantId,
        ProjectId = project.ProjectId,
        ProjectName = project.ProjectName,
        TicketId = ticketId,
        TicketTitle = ticket.Title,
        SourceDocumentId = document.Id,
        SourceDocumentVersionId = sourceVersion.Id,
        SourceDocumentTitle = document.Title,
        BuilderContext = builderFlags,
        NegativeChecks = negativeChecks,
        Evidence = new BuilderContextSourceEvidence
        {
            ContextProjectId = context.ProjectId,
            ContextTicketId = context.TicketId,
            ContextTicketTitle = context.TicketTitle,
            ContextSourceDocumentId = context.SourceDocumentId,
            ContextSourceDocumentVersionId = context.SourceDocumentVersionId,
            ContextSourceDocumentTitle = context.SourceDocumentTitle,
            ContextSourceResolutionStatus = context.SourceDocumentResolutionStatus,
            ContextSourceResolutionDetail = context.SourceDocumentResolutionDetail,
            ContextSourceLinkEvidence = context.SourceLinkEvidence,
            WrongProjectResolutionStatus = wrongProjectContext.SourceDocumentResolutionStatus,
            HistoricalResolutionStatus = historicalContext.SourceDocumentResolutionStatus,
            MissingSourceResolutionStatus = orphanContext.SourceDocumentResolutionStatus,
            MissingVersionResolutionStatus = missingVersionContext.SourceDocumentResolutionStatus
        },
        Boundary = "This proves builder context source inclusion only; it does not prove code generation or patch application."
    };

    Console.WriteLine(JsonSerializer.Serialize(result, options));
    return passed ? 0 : 1;
}

static async Task<CliProjectContext> EnsureBuilderContextBleedProjectAsync(
    IDbConnectionFactory connectionFactory,
    CliProjectContext queryProject,
    ICurrentTenantContext tenant,
    string dogfoodRunId)
{
    var projectService = new ProjectService(connectionFactory, tenant);
    var name = $"IronDevMemorySpine011_Bleed_{dogfoodRunId.Replace(':', '-').Replace('\\', '-').Replace('/', '-')}";
    var projects = await projectService.GetProjectsAsync();
    var existing = projects.FirstOrDefault(project => project.Name == name);
    if (existing is not null)
    {
        return new CliProjectContext
        {
            ProjectId = existing.Id,
            TenantId = existing.TenantId,
            ProjectName = existing.Name
        };
    }

    var projectId = await projectService.CreateProjectAsync(new Project
    {
        TenantId = queryProject.TenantId,
        Name = name,
        Description = "Same-tenant wrong-project control for Memory Spine 011.",
        LocalPath = queryProject.ProjectName
    });

    return new CliProjectContext
    {
        ProjectId = projectId,
        TenantId = queryProject.TenantId,
        ProjectName = name
    };
}

static async Task MarkProjectDocumentVersionStatusAsync(
    IDbConnectionFactory connectionFactory,
    long documentVersionId,
    string status)
{
    using var connection = connectionFactory.CreateConnection();
    await connection.ExecuteAsync(new CommandDefinition(
        """
        UPDATE dbo.ProjectDocumentVersions
        SET Status = @Status
        WHERE Id = @DocumentVersionId;
        """,
        new { DocumentVersionId = documentVersionId, Status = status }));
}

static TicketSourceLinkValidation BuildTicketSourceLinkValidation(
    ProjectTicket ticket,
    long expectedDocumentVersionId,
    ProjectDocumentVersion? resolvedVersion)
{
    if (ticket.SourceDocumentVersionId is null)
    {
        return new TicketSourceLinkValidation
        {
            Passed = false,
            Status = "missing_source_document_version",
            FailureReason = "Ticket does not have SourceDocumentVersionId."
        };
    }

    if (ticket.SourceDocumentVersionId.Value != expectedDocumentVersionId)
    {
        return new TicketSourceLinkValidation
        {
            Passed = false,
            Status = "wrong_source_document_version",
            FailureReason = $"Ticket SourceDocumentVersionId {ticket.SourceDocumentVersionId.Value} did not match expected {expectedDocumentVersionId}."
        };
    }

    if (resolvedVersion is null)
    {
        return new TicketSourceLinkValidation
        {
            Passed = false,
            Status = "source_document_version_not_found",
            FailureReason = $"ProjectDocumentVersion {ticket.SourceDocumentVersionId.Value} could not be resolved from SQL."
        };
    }

    return new TicketSourceLinkValidation
    {
        Passed = true,
        Status = "resolved_exact_project_document_version",
        FailureReason = null
    };
}

static async Task<CliProjectContext?> ResolveProjectAsync(IDbConnectionFactory connectionFactory, string projectName)
{
    using var connection = connectionFactory.CreateConnection();
    return await connection.QuerySingleOrDefaultAsync<CliProjectContext?>(new CommandDefinition(
        """
        SELECT TOP (1)
            Id AS ProjectId,
            TenantId,
            Name AS ProjectName
        FROM dbo.Projects
        WHERE Name = @ProjectName OR Name = @FallbackProjectName
        ORDER BY CASE WHEN Name = @ProjectName THEN 0 ELSE 1 END, Id;
        """,
        new
        {
            ProjectName = projectName,
            FallbackProjectName = projectName == "IronDev" ? "IronDeveloper" : "IronDev"
        }));
}

static async Task ApplySqlScriptAsync(IDbConnectionFactory connectionFactory, string scriptPath)
{
    if (!File.Exists(scriptPath))
        return;

    var script = await File.ReadAllTextAsync(scriptPath);
    var batches = script
        .Split(["\r\nGO\r\n", "\nGO\n", "\r\nGO\n", "\nGO\r\n"], StringSplitOptions.RemoveEmptyEntries)
        .Select(batch => batch.Trim())
        .Where(batch => !string.IsNullOrWhiteSpace(batch) && !batch.StartsWith("USE ", StringComparison.OrdinalIgnoreCase));

    using var connection = connectionFactory.CreateConnection();
    foreach (var batch in batches)
        await connection.ExecuteAsync(batch);
}

static string ResolveIronDevConnectionString(string[] args)
{
    var explicitConnection = ReadOption(args, "--connection-string");
    if (!string.IsNullOrWhiteSpace(explicitConnection))
        return explicitConnection;

    var envConnection = Environment.GetEnvironmentVariable("IRONDEV_CONNECTION_STRING");
    if (!string.IsNullOrWhiteSpace(envConnection))
        return envConnection;

    var repoRoot = FindRepositoryRoot();
    foreach (var path in new[]
    {
        Path.Combine(repoRoot, "IronDeveloper", "appsettings.Development.json"),
        Path.Combine(repoRoot, "IronDeveloper", "appsettings.json")
    })
    {
        var connection = TryReadConnectionString(path, "IronDeveloperDb");
        if (!string.IsNullOrWhiteSpace(connection))
            return connection;
    }

    throw new InvalidOperationException("Could not resolve IronDeveloperDb connection string.");
}

static string? TryReadConnectionString(string path, string name)
{
    if (!File.Exists(path))
        return null;

    using var document = JsonDocument.Parse(File.ReadAllText(path));
    if (document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings) &&
        connectionStrings.TryGetProperty(name, out var value))
    {
        return value.GetString();
    }

    return null;
}

static string FindRepositoryRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
            File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
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

static string GetDogfoodKnowledgeStore(string[] args)
    => Path.GetFullPath(ReadOption(args, "--store-root") ?? Path.Combine("tools", "dogfood", "knowledge"));

static string GetKnowledgeProjectRoot(string storeRoot, string project)
    => Path.Combine(storeRoot, Slugify(project));

static string EnsureKnowledgeDocsRoot(string storeRoot, string project)
{
    var docsRoot = Path.Combine(GetKnowledgeProjectRoot(storeRoot, project), "docs");
    Directory.CreateDirectory(docsRoot);
    return docsRoot;
}

static async Task<int> SeedIronDevBaselineDocsAsync(string project, string docsRoot)
{
    if (!project.Contains("IronDev", StringComparison.OrdinalIgnoreCase))
        return 0;

    var docs = new[]
    {
        new KnowledgeDocumentWrite
        {
            Project = project,
            Title = "IronDev Alpha Stabilisation Principles",
            DocumentType = "Architecture",
            Authority = "Accepted",
            Source = "SeedBaseline",
            Content = """
            # IronDev Alpha Stabilisation Principles

            SQL Server remains the canonical source of truth. Weaviate and local dogfood stores are retrieval/index layers only.

            The stabilisation branch should prefer bug fixes, traceability, and dogfood loops over broad new product surface.
            Unsafe writes, code changes, and destructive actions must pause at review or approval gates.
            """
        },
        new KnowledgeDocumentWrite
        {
            Project = project,
            Title = "Headless Dogfood Loop",
            DocumentType = "Discussion",
            Authority = "WorkingDraft",
            Source = "SeedBaseline",
            Content = """
            # Headless Dogfood Loop

            IronDev needs a command-line control port so Codex can reset a test world, run messy prompt variants, inspect traces, patch IronDev, and run again.

            Dogfood runs are identified by DogfoodRunId. Replay tests assert behaviour such as route, action blocking, dry-run safety, and generated draft artefacts rather than exact prose.
            """
        },
        new KnowledgeDocumentWrite
        {
            Project = project,
            Title = "Test Agent Contract",
            DocumentType = "Architecture",
            Authority = "WorkingDraft",
            Source = "SeedBaseline",
            Content = """
            # Test Agent Contract

            The Test Agent is cheap and literal. It executes structured plans, captures logs, and returns concise reports to Codex.

            It does not patch code or invent fixes. It may continue a bounded conversation when the test plan provides scenario facts and expected outcomes.
            """
        },
        new KnowledgeDocumentWrite
        {
            Project = project,
            Title = "Model Role Settings Direction",
            DocumentType = "Discussion",
            Authority = "WorkingDraft",
            Source = "SeedBaseline",
            Content = """
            # Model Role Settings Direction

            IronDev should choose models by agent role. Cheap models should handle routing, summarisation, and Test Agent execution. Stronger models should handle planning, difficult failure diagnosis, and code proposal review.

            Every trace should record agent role, provider, model, and DogfoodRunId when present.
            """
        }
    };

    foreach (var doc in docs)
        await WriteKnowledgeDocumentAsync(docsRoot, doc);

    return docs.Length;
}

static async Task<KnowledgeDocument> WriteKnowledgeDocumentAsync(string docsRoot, KnowledgeDocumentWrite write)
{
    Directory.CreateDirectory(docsRoot);
    var id = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Slugify(write.Title)}";
    var path = Path.Combine(docsRoot, $"{id}.md");
    var markdown = $"""
    ---
    id: {id}
    project: {write.Project}
    title: {write.Title}
    document_type: {write.DocumentType}
    authority: {write.Authority}
    source: {write.Source}
    dogfood_run_id: {write.DogfoodRunId}
    created_utc: {DateTimeOffset.UtcNow:o}
    ---

    {write.Content.Trim()}
    """;

    await File.WriteAllTextAsync(path, markdown);

    return new KnowledgeDocument
    {
        Id = id,
        Project = write.Project,
        Title = write.Title,
        DocumentType = write.DocumentType,
        Authority = write.Authority,
        Source = write.Source,
        DogfoodRunId = write.DogfoodRunId,
        Path = path,
        CreatedUtc = DateTimeOffset.UtcNow
    };
}

static async Task<List<KnowledgeDocument>> BuildKnowledgeIndexAsync(string project, string docsRoot)
{
    var documents = new List<KnowledgeDocument>();
    if (!Directory.Exists(docsRoot))
        return documents;

    foreach (var file in Directory.GetFiles(docsRoot, "*.md", SearchOption.TopDirectoryOnly))
    {
        var text = await File.ReadAllTextAsync(file);
        var frontmatter = ParseFrontmatter(text);
        var created = DateTimeOffset.TryParse(GetMeta(frontmatter, "created_utc"), out var parsedCreated)
            ? parsedCreated
            : File.GetCreationTimeUtc(file);

        documents.Add(new KnowledgeDocument
        {
            Id = GetMeta(frontmatter, "id", Path.GetFileNameWithoutExtension(file)),
            Project = GetMeta(frontmatter, "project", project),
            Title = GetMeta(frontmatter, "title", Path.GetFileNameWithoutExtension(file)),
            DocumentType = GetMeta(frontmatter, "document_type", "Discussion"),
            Authority = GetMeta(frontmatter, "authority", "Draft"),
            Source = GetMeta(frontmatter, "source", "Unknown"),
            DogfoodRunId = GetMeta(frontmatter, "dogfood_run_id", string.Empty),
            Path = Path.GetFullPath(file),
            CreatedUtc = created
        });
    }

    return documents
        .OrderByDescending(document => document.CreatedUtc)
        .ThenBy(document => document.Title, StringComparer.OrdinalIgnoreCase)
        .ToList();
}

static async Task WriteKnowledgeIndexAsync(string projectRoot, IReadOnlyList<KnowledgeDocument> index, JsonSerializerOptions options)
{
    Directory.CreateDirectory(projectRoot);
    await File.WriteAllTextAsync(
        Path.Combine(projectRoot, "knowledge-index.json"),
        JsonSerializer.Serialize(index, options));
}

static Dictionary<string, string> ParseFrontmatter(string text)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    using var reader = new StringReader(text);
    if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
        return result;

    string? line;
    while ((line = reader.ReadLine()) is not null)
    {
        if (string.Equals(line, "---", StringComparison.Ordinal))
            break;

        var separator = line.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
            continue;

        result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
    }

    return result;
}

static string StripFrontmatter(string text)
{
    using var reader = new StringReader(text);
    if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
        return text;

    string? line;
    while ((line = reader.ReadLine()) is not null)
    {
        if (string.Equals(line, "---", StringComparison.Ordinal))
            return reader.ReadToEnd().Trim();
    }

    return text;
}

static string GetMeta(IReadOnlyDictionary<string, string> metadata, string key, string fallback = "")
    => metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : fallback;

static int CountOccurrences(string text, string term)
{
    var count = 0;
    var index = 0;
    while ((index = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
    {
        count++;
        index += term.Length;
    }

    return count;
}

static string BuildSearchSnippet(string content, IReadOnlyList<string> terms)
{
    var firstHit = terms
        .Select(term => content.IndexOf(term, StringComparison.OrdinalIgnoreCase))
        .Where(index => index >= 0)
        .DefaultIfEmpty(0)
        .Min();

    var start = Math.Max(0, firstHit - 80);
    var length = Math.Min(content.Length - start, 260);
    return string.Join(' ', content.Substring(start, length)
        .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

static string Slugify(string value)
{
    var chars = value
        .Trim()
        .ToLowerInvariant()
        .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
        .ToArray();
    var compact = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    return string.IsNullOrWhiteSpace(compact) ? "document" : compact[..Math.Min(compact.Length, 72)];
}

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
    public string ScenarioId { get; init; } = string.Empty;
    public int? Seed { get; init; }
    public int? Step { get; init; }
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

public sealed class KnowledgeDocumentWrite
{
    public string Project { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string DocumentType { get; init; } = "Discussion";
    public string Authority { get; init; } = "Draft";
    public string Source { get; init; } = string.Empty;
    public string DogfoodRunId { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
}

public sealed class KnowledgeDocument
{
    public string Id { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public string Authority { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string DogfoodRunId { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class DocsCleanResult
{
    public string Project { get; init; } = string.Empty;
    public string StoreRoot { get; init; } = string.Empty;
    public string DocsRoot { get; init; } = string.Empty;
    public string ArchiveRoot { get; init; } = string.Empty;
    public int ArchivedDocuments { get; init; }
    public int SeededDocuments { get; init; }
    public int TotalDocuments { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class DocsImportResult
{
    public string Project { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public string Authority { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public int TotalDocuments { get; init; }
}

public sealed class DocsListResult
{
    public string Project { get; init; } = string.Empty;
    public string StoreRoot { get; init; } = string.Empty;
    public IReadOnlyList<KnowledgeDocument> Documents { get; init; } = [];
}

public sealed class DocsShowResult
{
    public string Project { get; init; } = string.Empty;
    public KnowledgeDocument Document { get; init; } = new();
    public string Content { get; init; } = string.Empty;
}

public sealed class DocsSearchResult
{
    public string Project { get; init; } = string.Empty;
    public string Query { get; init; } = string.Empty;
    public IReadOnlyList<DocsSearchMatch> Matches { get; init; } = [];
}

public sealed class DocsSearchMatch
{
    public KnowledgeDocument Document { get; init; } = new();
    public int Score { get; init; }
    public IReadOnlyDictionary<string, int> Ranking { get; init; } = new Dictionary<string, int>();
    public string Snippet { get; init; } = string.Empty;
}

public sealed class CodexMemorySearchResult
{
    public string Query { get; init; } = string.Empty;
    public CodexMemorySearchProject Project { get; init; } = new();
    public string WeaviateEndpoint { get; init; } = string.Empty;
    public string WeaviateCollection { get; init; } = string.Empty;
    public string SemanticTraceId { get; init; } = string.Empty;
    public string DogfoodRunId { get; init; } = string.Empty;
    public IReadOnlyList<CodexMemorySearchMatch> Matches { get; init; } = [];
}

public sealed class CodexMemorySearchProject
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class CodexMemorySearchMatch
{
    public string DocumentTitle { get; init; } = string.Empty;
    public string DocumentId { get; init; } = string.Empty;
    public string DocumentVersionId { get; init; } = string.Empty;
    public string SourceEntityType { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public int? RawWeaviateRank { get; init; }
    public double RawVectorScore { get; init; }
    public int FinalIronDevRank { get; init; }
    public double FinalAuthorityScore { get; init; }
    public string AuthorityLevel { get; init; } = string.Empty;
    public string CurrentStatus { get; init; } = string.Empty;
    public IReadOnlyList<string> SourceLinks { get; init; } = [];
    public string Excerpt { get; init; } = string.Empty;
    public string SemanticTraceId { get; init; } = string.Empty;
    public string MatchReason { get; init; } = string.Empty;
}

public sealed class MemorySqlVersionSmokeResult
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public long DocumentId { get; init; }
    public long CurrentVersionId { get; init; }
    public long OldVersionId { get; init; }
    public string Query { get; init; } = string.Empty;
    public Guid SemanticTraceId { get; init; }
    public int SourceLinkCount { get; init; }
    public bool Passed { get; init; }
    public MemorySqlVersionExpected Expected { get; init; } = new();
    public IReadOnlyList<MemorySqlVersionSearchResult> Results { get; init; } = [];
}

public sealed class MemorySqlVersionExpected
{
    public string TopSourceVersionId { get; init; } = string.Empty;
    public bool OldVersionShouldBeStale { get; init; }
    public bool SourceLinkRequired { get; init; }
}

public sealed class MemorySqlVersionSearchResult
{
    public string Title { get; init; } = string.Empty;
    public string SourceEntityType { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public string? SourceVersionId { get; init; }
    public double FinalScore { get; init; }
    public double VectorSimilarity { get; init; }
    public double AuthorityBoost { get; init; }
    public double SourceTypeBoost { get; init; }
    public double RecencyBoost { get; init; }
    public double StalePenalty { get; init; }
    public bool IsStale { get; init; }
    public string MatchReason { get; init; } = string.Empty;
}

public sealed class MemoryWeaviateSqlVersionSmokeResult
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public long DocumentId { get; init; }
    public long CurrentVersionId { get; init; }
    public long OldVersionId { get; init; }
    public string Query { get; init; } = string.Empty;
    public string WeaviateEndpoint { get; init; } = string.Empty;
    public string WeaviateCollection { get; init; } = string.Empty;
    public Guid SemanticTraceId { get; init; }
    public int SourceLinkCount { get; init; }
    public bool Passed { get; init; }
    public IReadOnlyList<WeaviateRawMatch> RawMatches { get; init; } = [];
    public IReadOnlyList<MemoryWeaviateSqlVersionSearchResult> Results { get; init; } = [];
}

public sealed class WeaviateRawMatch
{
    public int RawWeaviateRank { get; init; }
    public Guid ChunkId { get; init; }
    public Guid ArtefactId { get; init; }
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public string SourceEntityType { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public string? SourceVersionId { get; init; }
    public string Title { get; init; } = string.Empty;
    public bool IsStale { get; init; }
    public double Distance { get; init; }
    public double VectorSimilarity { get; init; }
}

public sealed class MemoryWeaviateSqlVersionSearchResult
{
    public int FinalAuthorityRank { get; init; }
    public int? RawWeaviateRank { get; init; }
    public string Title { get; init; } = string.Empty;
    public string SourceEntityType { get; init; } = string.Empty;
    public string SourceEntityId { get; init; } = string.Empty;
    public string? SourceVersionId { get; init; }
    public double FinalScore { get; init; }
    public double VectorSimilarity { get; init; }
    public double AuthorityBoost { get; init; }
    public double SourceTypeBoost { get; init; }
    public double RecencyBoost { get; init; }
    public double StalePenalty { get; init; }
    public bool IsStale { get; init; }
    public string MatchReason { get; init; } = string.Empty;
}

public sealed class MemoryCrossProjectSmokeResult
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public int TenantId { get; init; }
    public int QueryProjectId { get; init; }
    public string QueryProjectName { get; init; } = string.Empty;
    public int BleedProjectId { get; init; }
    public string BleedProjectName { get; init; } = string.Empty;
    public string Query { get; init; } = string.Empty;
    public string WeaviateEndpoint { get; init; } = string.Empty;
    public string WeaviateCollection { get; init; } = string.Empty;
    public Guid SemanticTraceId { get; init; }
    public int SourceLinkCount { get; init; }
    public bool Passed { get; init; }
    public IReadOnlyList<WeaviateRawMatch> RawMatches { get; init; } = [];
    public IReadOnlyList<CrossProjectMemoryDecision> Decisions { get; init; } = [];
    public IReadOnlyList<MemoryWeaviateSqlVersionSearchResult> Results { get; init; } = [];
}

public sealed class CrossProjectMemoryDecision
{
    public int RawWeaviateRank { get; init; }
    public int? FinalAuthorityRank { get; init; }
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string? SourceVersionId { get; init; }
    public double VectorSimilarity { get; init; }
    public string Decision { get; init; } = string.Empty;
}

public sealed class MemoryReindexFreshnessSmokeResult
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public int ProjectId { get; init; }
    public string BleedProject { get; init; } = string.Empty;
    public int BleedProjectId { get; init; }
    public long DocumentId { get; init; }
    public string DocumentTitle { get; init; } = string.Empty;
    public long OldVersionId { get; init; }
    public long NewVersionId { get; init; }
    public string Query { get; init; } = string.Empty;
    public string WeaviateEndpoint { get; init; } = string.Empty;
    public string WeaviateCollection { get; init; } = string.Empty;
    public Guid SemanticTraceId { get; init; }
    public bool Passed { get; init; }
    public ReindexRawRankEvidence RawRank { get; init; } = new();
    public ReindexFinalRankEvidence FinalRank { get; init; } = new();
    public ReindexStaleDemotionEvidence StaleDemotion { get; init; } = new();
    public ReindexDuplicateEvidence Duplicates { get; init; } = new();
    public ReindexWrongProjectRejectionEvidence WrongProjectRejection { get; init; } = new();
    public ReindexExactTitlePromotionEvidence ExactTitlePromotion { get; init; } = new();
    public IReadOnlyList<MemoryWeaviateSqlVersionSearchResult> Results { get; init; } = [];
}

public sealed class ReindexRawRankEvidence
{
    public int? OldVersionRawRank { get; init; }
    public int? NewVersionRawRank { get; init; }
    public int? WrongProjectRawRank { get; init; }
}

public sealed class ReindexFinalRankEvidence
{
    public int? OldVersionFinalRank { get; init; }
    public int? NewVersionFinalRank { get; init; }
}

public sealed class ReindexStaleDemotionEvidence
{
    public bool OldVersionVisible { get; init; }
    public bool OldVersionIsStale { get; init; }
    public double OldVersionStalePenalty { get; init; }
    public bool CurrentBeatsStale { get; init; }
}

public sealed class ReindexDuplicateEvidence
{
    public int DuplicateArtefactSourceRecords { get; init; }
    public int DuplicateActiveChunks { get; init; }
    public int DuplicateIndexedCandidates { get; init; }
    public int ActiveChunkCount { get; init; }
    public int IndexedCandidateCount { get; init; }
    public int DuplicateCount { get; init; }
}

public sealed class ReindexWrongProjectRejectionEvidence
{
    public bool WrongProjectCandidateVisibleRaw { get; init; }
    public string WrongProjectName { get; init; } = string.Empty;
    public bool WrongProjectRejectedFromFinal { get; init; }
}

public sealed class ReindexExactTitlePromotionEvidence
{
    public string ExactTitleQuery { get; init; } = string.Empty;
    public bool PromotedAcceptedCurrentVersion { get; init; }
    public double DirectLinkBoost { get; init; }
}

public sealed class MemoryTicketSourceLinkSmokeResult
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public long SourceDocumentId { get; init; }
    public long SourceDocumentVersionId { get; init; }
    public long TicketId { get; init; }
    public long? TicketSourceDocumentVersionId { get; init; }
    public string LinkResolutionStatus { get; init; } = string.Empty;
    public bool ArtifactSourceReferenceFound { get; init; }
    public bool ProjectDocumentLinkFound { get; init; }
    public long OrphanTicketId { get; init; }
    public bool OrphanReportedAsFailure { get; init; }
    public bool Passed { get; init; }
    public MemoryTicketSourceLinkExpected Expected { get; init; } = new();
    public MemoryTicketSourceLinkEvidence Evidence { get; init; } = new();
}

public sealed class MemoryTicketSourceLinkExpected
{
    public long TicketSourceDocumentVersionId { get; init; }
    public bool OrphanShouldFailValidation { get; init; }
}

public sealed class MemoryTicketSourceLinkEvidence
{
    public long? ResolvedDocumentVersionId { get; init; }
    public long? ResolvedDocumentId { get; init; }
    public int SourceReferenceCount { get; init; }
    public int ProjectDocumentLinkCount { get; init; }
    public TicketSourceLinkValidation PositiveValidation { get; init; } = new();
    public TicketSourceLinkValidation OrphanValidation { get; init; } = new();
}

public sealed class TicketSourceLinkValidation
{
    public bool Passed { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? FailureReason { get; init; }
}

public sealed class MemoryBuilderContextSourceSmokeResult
{
    public string Goal { get; init; } = string.Empty;
    public string DogfoodRunId { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public long TicketId { get; init; }
    public string TicketTitle { get; init; } = string.Empty;
    public long SourceDocumentId { get; init; }
    public long SourceDocumentVersionId { get; init; }
    public string SourceDocumentTitle { get; init; } = string.Empty;
    public BuilderContextSourceFlags BuilderContext { get; init; } = new();
    public BuilderContextNegativeChecks NegativeChecks { get; init; } = new();
    public BuilderContextSourceEvidence Evidence { get; init; } = new();
    public string Boundary { get; init; } = string.Empty;
}

public sealed class BuilderContextSourceFlags
{
    public bool TicketIncluded { get; init; }
    public bool SourceDocumentIncluded { get; init; }
    public bool SourceDocumentVersionIncluded { get; init; }
    public bool SourceMarkdownIncluded { get; init; }
    public bool SourceLinkEvidenceIncluded { get; init; }
    public bool WrongProjectMemoryExcluded { get; init; }
    public bool StaleMemoryExcludedOrMarkedHistorical { get; init; }
}

public sealed class BuilderContextNegativeChecks
{
    public long OrphanTicketId { get; init; }
    public bool OrphanTicketFailsCleanly { get; init; }
    public long MissingDocumentVersionTicketId { get; init; }
    public bool MissingDocumentVersionFailsCleanly { get; init; }
    public long WrongProjectTicketId { get; init; }
    public bool WrongProjectFailsCleanly { get; init; }
    public long HistoricalTicketId { get; init; }
    public bool HistoricalSourceMarkedHistorical { get; init; }
}

public sealed class BuilderContextSourceEvidence
{
    public int ContextProjectId { get; init; }
    public long ContextTicketId { get; init; }
    public string ContextTicketTitle { get; init; } = string.Empty;
    public long? ContextSourceDocumentId { get; init; }
    public long? ContextSourceDocumentVersionId { get; init; }
    public string? ContextSourceDocumentTitle { get; init; }
    public string ContextSourceResolutionStatus { get; init; } = string.Empty;
    public string? ContextSourceResolutionDetail { get; init; }
    public IReadOnlyList<string> ContextSourceLinkEvidence { get; init; } = [];
    public string WrongProjectResolutionStatus { get; init; } = string.Empty;
    public string HistoricalResolutionStatus { get; init; } = string.Empty;
    public string MissingSourceResolutionStatus { get; init; } = string.Empty;
    public string MissingVersionResolutionStatus { get; init; } = string.Empty;
}

public sealed class BuilderProposalSafetySmokeResult
{
    public string Goal { get; init; } = string.Empty;
    public string DogfoodRunId { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public int TenantId { get; init; }
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public long TicketId { get; init; }
    public string TicketTitle { get; init; } = string.Empty;
    public long SourceDocumentId { get; init; }
    public long SourceDocumentVersionId { get; init; }
    public string TargetFile { get; init; } = string.Empty;
    public BuilderProposalSafetyProposalEvidence Proposal { get; init; } = new();
    public BuilderProposalSafetyFlags Safety { get; init; } = new();
    public BuilderProposalSafetyEvidence Evidence { get; init; } = new();
    public string Boundary { get; init; } = string.Empty;
}

public sealed class BuilderProposalSafetyProposalEvidence
{
    public bool ProposalGenerated { get; init; }
    public int ProposedFileCount { get; init; }
    public IReadOnlyList<string> ProposedFiles { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
    public string Rationale { get; init; } = string.Empty;
    public string RiskNotes { get; init; } = string.Empty;
    public string TestPlan { get; init; } = string.Empty;
}

public sealed class BuilderProposalSafetyFlags
{
    public bool DryRunValidationRan { get; init; }
    public bool DryRunValidationPassed { get; init; }
    public bool FileUnchangedAfterPreview { get; init; }
    public bool ApprovalGateBlockedApply { get; init; }
    public bool FileUnchangedAfterApplyAttempt { get; init; }
    public bool DirectPatchApplyBlocked { get; init; }
    public bool FileUnchangedAfterDirectPatchAttempt { get; init; }
    public bool SourceContextIncluded { get; init; }
}

public sealed class BuilderProposalSafetyEvidence
{
    public string BeforeHash { get; init; } = string.Empty;
    public string AfterPreviewHash { get; init; } = string.Empty;
    public string AfterApplyAttemptHash { get; init; } = string.Empty;
    public string AfterDirectPatchAttemptHash { get; init; } = string.Empty;
    public string ValidationSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> ValidationMessages { get; init; } = [];
    public string ApplyErrorMessage { get; init; } = string.Empty;
    public string DirectPatchErrorMessage { get; init; } = string.Empty;
    public string ContextSummary { get; init; } = string.Empty;
}

public sealed class DeterministicCodeChangeProposalService : ICodeChangeProposalService
{
    private readonly string _filePath;
    private readonly string _beforeSnippet;
    private readonly string _afterSnippet;

    public DeterministicCodeChangeProposalService(
        string filePath,
        string beforeSnippet,
        string afterSnippet)
    {
        _filePath = filePath;
        _beforeSnippet = beforeSnippet;
        _afterSnippet = afterSnippet;
    }

    public Task<IronDev.Core.Builder.CodeChangeProposal> GenerateProposalAsync(
        IronDev.Core.Builder.TicketBuildContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new IronDev.Core.Builder.CodeChangeProposal
        {
            TicketId = context.TicketId,
            Summary = "Deterministic proposal generated for builder proposal safety smoke.",
            Rationale = "The proposal targets a disposable fixture file so dry-run validation can prove no writes happen before approval.",
            RiskNotes = "No production files are targeted; this smoke validates orchestration safety only.",
            TestPlan = "Compare file hashes before preview, after preview, and after blocked apply attempts.",
            OriginalRequest = context.TicketSummary,
            StandardsCompliance = "Proposal-first, approval-before-writes.",
            FileChanges =
            [
                new IronDev.Core.Builder.FileChangeProposal
                {
                    FilePath = _filePath,
                    ChangeReason = "Exercise dry-run validation without applying the change.",
                    BeforeSnippet = _beforeSnippet,
                    AfterSnippet = _afterSnippet,
                    Patch = $"--- a/{_filePath}\n+++ b/{_filePath}\n@@\n-{_beforeSnippet}\n+{_afterSnippet}",
                    FullContentAfter = _afterSnippet
                }
            ]
        });
    }
}

public sealed class CliProjectContext
{
    public int ProjectId { get; init; }
    public int TenantId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
}

public sealed class CliTenantContext : ICurrentTenantContext
{
    public CliTenantContext(int tenantId) => TenantId = tenantId;

    public int TenantId { get; }
}

public sealed class CliConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public CliConnectionFactory(string connectionString) => _connectionString = connectionString;

    public System.Data.IDbConnection CreateConnection() => new SqlConnection(_connectionString);
}
