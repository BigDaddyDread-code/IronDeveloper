using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    Console.Error.WriteLine("Usage: IronDev.ReplayRunner <replay-plan.json> | agent <list|profiles|tester run-plan> [...] | chat send <message> [...] | docs <clean|import|list|show|search> [...] | memory search <query> [...] | failure latest --for-codex [...]");
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

if (IsCommand(args, "failure", "latest"))
    return await HandleFailureLatestCommandAsync(args, options);

if (IsCommand(args, "memory", "search"))
    return await HandleMemorySearchCommandAsync(args, options);

if (IsCommand(args, "memory", "sql-version-smoke"))
    return await HandleMemorySqlVersionSmokeCommandAsync(args, options);

if (IsCommand(args, "memory", "weaviate-sql-version-smoke"))
    return await HandleMemoryWeaviateSqlVersionSmokeCommandAsync(args, options);

if (IsCommand(args, "memory", "cross-project-smoke"))
    return await HandleMemoryCrossProjectSmokeCommandAsync(args, options);

if (IsCommand(args, "memory", "ticket-source-link-smoke"))
    return await HandleMemoryTicketSourceLinkSmokeCommandAsync(args, options);

if (IsCommand(args, "memory", "builder-context-source-smoke"))
    return await HandleMemoryBuilderContextSourceSmokeCommandAsync(args, options);

if (IsCommand(args, "builder", "proposal-safety-smoke"))
    return await HandleBuilderProposalSafetySmokeCommandAsync(args, options);

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

static (AgentModelResolver ModelResolver, AgentRegistry Registry, AgentRunner Runner) CreateAgentRuntime()
{
    var repoRoot = FindRepositoryRoot();
    var modelResolver = new AgentModelResolver(LoadModelProfiles(repoRoot));
    var definitions = AgentModelDefaults.CreateDefaultDefinitions();
    var agents = definitions
        .Select<AgentDefinition, IIronDevAgent>(definition =>
            string.Equals(definition.Name, "TesterAgent", StringComparison.OrdinalIgnoreCase)
                ? new TesterAgent(definition, modelResolver, repoRoot)
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

    var selectedRunId = ReadOption(args, "--run-id");
    var replayFailure = FindLatestReplayFailure(runsRoot, selectedRunId);
    var testAgentFailure = FindLatestTestAgentFailure(runsRoot, selectedRunId);

    if (replayFailure is null && testAgentFailure is null)
    {
        Console.Error.WriteLine("No failed replay or Test Agent result found.");
        return 1;
    }

    var useTestAgent = testAgentFailure is not null &&
                       (replayFailure is null ||
                        testAgentFailure.ReportFile.LastWriteTimeUtc >= replayFailure.ResultFile.LastWriteTimeUtc);

    var runRoot = useTestAgent
        ? testAgentFailure!.ReportFile.DirectoryName!
        : Directory.GetParent(replayFailure!.ResultFile.DirectoryName!)!.FullName;
    var package = useTestAgent
        ? BuildTestAgentFailurePackage(testAgentFailure!, runRoot)
        : BuildFailurePackage(replayFailure!, runRoot);
    var jsonPath = Path.Combine(runRoot, "failure-package.json");
    var markdownPath = Path.Combine(runRoot, "failure-package.md");
    await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(package, options));
    await File.WriteAllTextAsync(markdownPath, BuildFailurePackageMarkdown(package));

    var result = new FailurePackageCommandResult
    {
        DogfoodRunId = package.DogfoodRunId,
        ScenarioId = package.ScenarioId,
        CaseId = package.CaseId,
        Prompt = package.Prompt,
        ExpectedIntent = package.ExpectedIntent,
        ActualIntent = package.ActualIntent,
        FailureReason = package.FailureReason,
        JsonPath = jsonPath,
        MarkdownPath = markdownPath,
        ReportPath = package.ReportPath,
        ReproCommand = package.ReproCommand,
        ValidationCommand = package.ValidationCommand
    };

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

static async Task<int> HandleMemorySearchCommandAsync(string[] args, JsonSerializerOptions options)
{
    var query = ReadOption(args, "--query") ?? ReadPositionalText(args, 2);
    if (string.IsNullOrWhiteSpace(query))
    {
        Console.Error.WriteLine("Usage: IronDev.ReplayRunner memory search <query> [--project IronDev] [--take 5] [--json]");
        return 2;
    }

    var projectName = ReadOption(args, "--project") ?? "IronDev";
    var take = int.TryParse(ReadOption(args, "--take"), out var parsedTake) ? parsedTake : 5;
    var dogfoodRunId = ReadOption(args, "--dogfood-run-id") ?? $"memory-search-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
    var store = GetDogfoodKnowledgeStore(args);
    var docsRoot = EnsureKnowledgeDocsRoot(store, projectName);
    var index = await BuildKnowledgeIndexAsync(projectName, docsRoot);
    var documents = new Dictionary<string, (KnowledgeDocument Document, string Body)>(StringComparer.OrdinalIgnoreCase);

    foreach (var document in index.Where(document => string.Equals(document.Project, projectName, StringComparison.OrdinalIgnoreCase)))
    {
        var text = await File.ReadAllTextAsync(document.Path);
        documents[document.Id] = (document, StripFrontmatter(text));
    }

    var endpoint = ReadOption(args, "--weaviate-endpoint") ?? ResolveWeaviateEndpoint();
    using var httpClient = new HttpClient { BaseAddress = new Uri(endpoint.TrimEnd('/') + "/") };
    var collectionName = BuildWeaviateDogfoodCollectionName($"codex-memory-{Slugify(projectName)}-{Slugify(query)}-{dogfoodRunId}");
    await EnsureWeaviateDogfoodCollectionAsync(httpClient, collectionName);

    foreach (var item in documents.Values)
    {
        var doc = item.Document;
        var body = item.Body;
        var artefact = BuildKnowledgeArtefact(projectName, doc, body);
        var chunk = BuildKnowledgeChunk(artefact, body);
        await UpsertWeaviateChunkAsync(
            httpClient,
            collectionName,
            DeterministicGuid($"memory-search:{doc.Id}"),
            artefact,
            chunk,
            tenantId: 0,
            isStale: IsKnowledgeDocumentStale(doc),
            vector: BuildLexicalVector($"{doc.Title} {doc.DocumentType} {doc.Authority} {body}"));
    }

    var rawMatches = await QueryWeaviateDogfoodCollectionAsync(
        httpClient,
        collectionName,
        BuildLexicalVector(query),
        limit: Math.Max(take * 3, take));

    var relevantRawMatches = rawMatches
        .Where(match => match.ProjectId == StableProjectId(projectName))
        .Where(match => documents.ContainsKey(match.SourceEntityId))
        .ToList();

    var candidates = relevantRawMatches.Select(match =>
    {
        var (document, body) = documents[match.SourceEntityId];
        var artefact = BuildKnowledgeArtefact(projectName, document, body);
        var chunk = BuildKnowledgeChunk(artefact, body, match.ChunkId);

        return new SemanticSearchCandidate
        {
            Document = new ProjectContextDocument
            {
                Id = StableLongId(document.Id),
                TenantId = 0,
                ProjectId = StableProjectId(projectName),
                DocumentType = document.DocumentType,
                AuthorityLevel = document.Authority,
                Title = document.Title,
                Content = body,
                Summary = $"Dogfood knowledge imported from {document.Source}",
                Source = document.Source,
                CreatedDate = document.CreatedUtc.UtcDateTime,
                UpdatedDate = document.CreatedUtc.UtcDateTime
            },
            Artefact = artefact,
            Chunk = chunk,
            VectorSimilarity = Math.Min(1.0, match.VectorSimilarity + GetTitleOverlapBoost(document.Title, query)),
            ContentHashMismatch = false
        };
    }).ToList();

    var traceId = Guid.NewGuid();
    var boostedArtefactIds = documents.Values
        .Where(item => HasTitleTermOverlap(item.Document.Title, query))
        .Select(item => BuildKnowledgeArtefact(projectName, item.Document, item.Body).Id)
        .ToArray();
    var ranked = new SemanticRankingService().Rank(new SemanticSearchQuery
    {
        ProjectId = StableProjectId(projectName),
        QueryText = query,
        Limit = take,
        IncludeStale = false,
        Consumer = "CodexMemorySearch",
        BoostedArtefactIds = boostedArtefactIds
    }, candidates);

    var result = new CodexMemorySearchResult
    {
        Query = query,
        Project = new CodexMemorySearchProject
        {
            Id = StableProjectId(projectName),
            Name = projectName
        },
        WeaviateEndpoint = endpoint,
        WeaviateCollection = collectionName,
        SemanticTraceId = traceId.ToString(),
        DogfoodRunId = dogfoodRunId,
        Matches = ranked.Select((match, index) =>
        {
            var raw = relevantRawMatches.FirstOrDefault(raw => raw.SourceEntityId == match.SourceEntityId);
            var document = documents[match.SourceEntityId].Document;
            return new CodexMemorySearchMatch
            {
                DocumentTitle = match.Title,
                DocumentId = document.Id,
                DocumentVersionId = document.Id,
                SourceEntityType = "DogfoodKnowledgeDocument",
                SourceEntityId = document.Id,
                RawWeaviateRank = raw?.RawWeaviateRank,
                RawVectorScore = raw?.VectorSimilarity ?? match.VectorSimilarity,
                FinalIronDevRank = index + 1,
                FinalAuthorityScore = match.FinalScore,
                AuthorityLevel = document.Authority,
                CurrentStatus = match.IsStale ? "Stale" : "Current",
                SourceLinks = [document.Source, document.Path],
                Excerpt = match.Snippet,
                SemanticTraceId = traceId.ToString(),
                MatchReason = match.MatchReason
            };
        }).ToArray()
    };

    Console.WriteLine(JsonSerializer.Serialize(result, options));
    return 0;
}

static async Task<int> HandleMemorySqlVersionSmokeCommandAsync(string[] args, JsonSerializerOptions options)
{
    var requestedProjectName = ReadOption(args, "--project") ?? "IronDev";
    var dogfoodRunId = ReadOption(args, "--dogfood-run-id") ?? $"memory-sql-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    var queryText = ReadOption(args, "--query") ?? "current first goal";
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
    var artefactRepository = new SemanticArtefactRepository(connectionFactory);
    var chunkRepository = new SemanticChunkRepository(connectionFactory);
    var traceRepository = new SemanticSearchTraceRepository(connectionFactory);
    var ranker = new SemanticRankingService();

    var stamp = dogfoodRunId.Replace(':', '-').Replace('\\', '-').Replace('/', '-');
    var titleSeed = $"CODEX_GOALS_SQL_SPINE_{stamp}_{Guid.NewGuid():N}";
    var title = titleSeed[..Math.Min(120, titleSeed.Length)];
    var oldContent = """
        # Codex Goals SQL Spine

        Old first goal = test builder output.
        This historical version is intentionally stale and must not outrank the current version.
        """;
    var currentContent = """
        # Codex Goals SQL Spine

        Current first goal = prove memory spine retrieval.
        IronDev must retrieve the current SQL ProjectDocumentVersion as authoritative memory.
        """;

    var document = await documentService.CreateDocumentAsync(new CreateProjectDocumentRequest
    {
        ProjectId = project.ProjectId,
        Title = title,
        DocumentType = "Architecture",
        ContentMarkdown = oldContent,
        ChangeSummary = "Historical dogfood version",
        CreatedBy = "TestAgent",
        SourceEntityType = "Discussion",
        SourceEntityId = 9001
    });

    var oldVersion = await documentService.GetCurrentVersionAsync(document.Id)
        ?? throw new InvalidOperationException("Initial document version was not created.");

    var currentVersion = await documentService.AddVersionAsync(new AddProjectDocumentVersionRequest
    {
        DocumentId = document.Id,
        ContentMarkdown = currentContent,
        ChangeSummary = "Current dogfood version",
        CreatedBy = "TestAgent",
        IncrementMajorVersion = true,
        Status = "Approved"
    });

    await documentService.LinkVersionAsync(new LinkProjectDocumentVersionRequest
    {
        DocumentVersionId = currentVersion.Id,
        LinkedEntityType = "Discussion",
        LinkedEntityId = 9002,
        LinkType = "CurrentGoalSource",
        CreatedBy = "TestAgent"
    });

    var oldArtefactId = Guid.NewGuid();
    var currentArtefactId = Guid.NewGuid();
    await IndexDocumentVersionAsync(
        artefactRepository,
        chunkRepository,
        project.TenantId,
        project.ProjectId,
        oldArtefactId,
        document,
        oldVersion,
        authorityLevel: "LowAuthorityNote",
        content: oldContent);
    await IndexDocumentVersionAsync(
        artefactRepository,
        chunkRepository,
        project.TenantId,
        project.ProjectId,
        currentArtefactId,
        document,
        currentVersion,
        authorityLevel: "AcceptedArchitecture",
        content: currentContent);

    await artefactRepository.MarkStaleAsync(new SemanticStaleRequest
    {
        ProjectId = project.ProjectId,
        SourceEntityType = "ProjectDocument",
        SourceEntityId = document.Id.ToString(),
        SourceVersionId = oldVersion.Id.ToString()
    });

    var oldArtefact = await artefactRepository.GetArtefactAsync(oldArtefactId)
        ?? throw new InvalidOperationException("Old semantic artefact was not persisted.");
    var currentArtefact = await artefactRepository.GetArtefactAsync(currentArtefactId)
        ?? throw new InvalidOperationException("Current semantic artefact was not persisted.");
    var oldChunk = (await chunkRepository.GetChunksAsync(oldArtefactId, includeStale: true)).Single();
    var currentChunk = (await chunkRepository.GetChunksAsync(currentArtefactId, includeStale: true)).Single();

    var query = new SemanticSearchQuery
    {
        ProjectId = project.ProjectId,
        QueryText = queryText,
        Consumer = "MemorySpineSmoke",
        Limit = 5,
        IncludeStale = true
    };

    var candidates = new[]
    {
        BuildCandidate(project.TenantId, project.ProjectId, document, oldVersion, oldArtefact, oldChunk, oldContent, vectorSimilarity: 0.88, contentHashMismatch: false),
        BuildCandidate(project.TenantId, project.ProjectId, document, currentVersion, currentArtefact, currentChunk, currentContent, vectorSimilarity: 0.82, contentHashMismatch: false)
    };

    var results = ranker.Rank(query, candidates);
    var traceId = await traceRepository.CreateTraceAsync(query);
    await traceRepository.AddResultsAsync(traceId, results);
    var links = await documentService.GetLinksForVersionAsync(currentVersion.Id);

    var top = results.FirstOrDefault();
    var passed = top is not null &&
                 top.SourceVersionId == currentVersion.Id.ToString() &&
                 !top.IsStale &&
                 links.Any(link => link.LinkType == "CurrentGoalSource");

    var result = new MemorySqlVersionSmokeResult
    {
        DogfoodRunId = dogfoodRunId,
        ProjectId = project.ProjectId,
        TenantId = project.TenantId,
        ProjectName = project.ProjectName,
        DocumentId = document.Id,
        CurrentVersionId = currentVersion.Id,
        OldVersionId = oldVersion.Id,
        Query = queryText,
        SemanticTraceId = traceId,
        SourceLinkCount = links.Count,
        Passed = passed,
        Expected = new MemorySqlVersionExpected
        {
            TopSourceVersionId = currentVersion.Id.ToString(),
            OldVersionShouldBeStale = true,
            SourceLinkRequired = true
        },
        Results = results.Select(result => new MemorySqlVersionSearchResult
        {
            Title = result.Title,
            SourceEntityType = result.SourceEntityType,
            SourceEntityId = result.SourceEntityId,
            SourceVersionId = result.SourceVersionId,
            FinalScore = result.FinalScore,
            VectorSimilarity = result.VectorSimilarity,
            AuthorityBoost = result.AuthorityBoost,
            SourceTypeBoost = result.SourceTypeBoost,
            RecencyBoost = result.RecencyBoost,
            StalePenalty = result.StalePenalty,
            IsStale = result.IsStale,
            MatchReason = result.MatchReason
        }).ToArray()
    };

    Console.WriteLine(JsonSerializer.Serialize(result, options));
    return passed ? 0 : 1;
}

static async Task<int> HandleMemoryWeaviateSqlVersionSmokeCommandAsync(string[] args, JsonSerializerOptions options)
{
    var requestedProjectName = ReadOption(args, "--project") ?? "IronDev";
    var dogfoodRunId = ReadOption(args, "--dogfood-run-id") ?? $"memory-weaviate-sql-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    var queryText = ReadOption(args, "--query") ?? "first Codex goal builder output";
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
    var artefactRepository = new SemanticArtefactRepository(connectionFactory);
    var chunkRepository = new SemanticChunkRepository(connectionFactory);
    var traceRepository = new SemanticSearchTraceRepository(connectionFactory);
    var ranker = new SemanticRankingService();

    var stamp = dogfoodRunId.Replace(':', '-').Replace('\\', '-').Replace('/', '-');
    var titleSeed = $"CODEX_GOALS_WEAVIATE_SQL_SPINE_{stamp}_{Guid.NewGuid():N}";
    var title = titleSeed[..Math.Min(120, titleSeed.Length)];
    var oldContent = """
        # Codex Goals Weaviate Spine

        The first Codex goal is to test builder output and patch generation.
        This historical version is intentionally stale but semantically tempting for builder-output queries.
        """;
    var currentContent = """
        # Codex Goals Weaviate Spine

        The first Codex goal is to prove SQL-backed memory retrieval and authority ranking.
        Current authoritative memory must beat stale vector matches before builder context is trusted.
        """;

    var document = await documentService.CreateDocumentAsync(new CreateProjectDocumentRequest
    {
        ProjectId = project.ProjectId,
        Title = title,
        DocumentType = "Architecture",
        ContentMarkdown = oldContent,
        ChangeSummary = "Historical Weaviate dogfood version",
        CreatedBy = "TestAgent",
        SourceEntityType = "Discussion",
        SourceEntityId = 9101
    });

    var oldVersion = await documentService.GetCurrentVersionAsync(document.Id)
        ?? throw new InvalidOperationException("Initial document version was not created.");

    var currentVersion = await documentService.AddVersionAsync(new AddProjectDocumentVersionRequest
    {
        DocumentId = document.Id,
        ContentMarkdown = currentContent,
        ChangeSummary = "Current Weaviate dogfood version",
        CreatedBy = "TestAgent",
        IncrementMajorVersion = true,
        Status = "Approved"
    });

    await documentService.LinkVersionAsync(new LinkProjectDocumentVersionRequest
    {
        DocumentVersionId = currentVersion.Id,
        LinkedEntityType = "Discussion",
        LinkedEntityId = 9102,
        LinkType = "CurrentGoalSource",
        CreatedBy = "TestAgent"
    });

    var oldArtefactId = Guid.NewGuid();
    var currentArtefactId = Guid.NewGuid();
    await IndexDocumentVersionAsync(
        artefactRepository,
        chunkRepository,
        project.TenantId,
        project.ProjectId,
        oldArtefactId,
        document,
        oldVersion,
        authorityLevel: "LowAuthorityNote",
        content: oldContent);
    await IndexDocumentVersionAsync(
        artefactRepository,
        chunkRepository,
        project.TenantId,
        project.ProjectId,
        currentArtefactId,
        document,
        currentVersion,
        authorityLevel: "AcceptedArchitecture",
        content: currentContent);

    await artefactRepository.MarkStaleAsync(new SemanticStaleRequest
    {
        ProjectId = project.ProjectId,
        SourceEntityType = "ProjectDocument",
        SourceEntityId = document.Id.ToString(),
        SourceVersionId = oldVersion.Id.ToString()
    });

    var oldArtefact = await artefactRepository.GetArtefactAsync(oldArtefactId)
        ?? throw new InvalidOperationException("Old semantic artefact was not persisted.");
    var currentArtefact = await artefactRepository.GetArtefactAsync(currentArtefactId)
        ?? throw new InvalidOperationException("Current semantic artefact was not persisted.");
    var oldChunk = (await chunkRepository.GetChunksAsync(oldArtefactId, includeStale: true)).Single();
    var currentChunk = (await chunkRepository.GetChunksAsync(currentArtefactId, includeStale: true)).Single();

    var weaviateEndpoint = ReadOption(args, "--weaviate-endpoint") ?? ResolveWeaviateEndpoint();
    var collectionName = BuildWeaviateDogfoodCollectionName(dogfoodRunId);
    var queryVector = new[] { 1.0, 0.0, 0.0, 0.0 };
    var staleVector = new[] { 1.0, 0.0, 0.0, 0.0 };
    var currentVector = new[] { 0.8, 0.6, 0.0, 0.0 };

    using var httpClient = new HttpClient { BaseAddress = new Uri(weaviateEndpoint.TrimEnd('/') + "/") };
    await EnsureWeaviateDogfoodCollectionAsync(httpClient, collectionName);
    await UpsertWeaviateChunkAsync(
        httpClient,
        collectionName,
        oldChunk.Id,
        oldArtefact,
        oldChunk,
        project.TenantId,
        isStale: true,
        vector: staleVector);
    await UpsertWeaviateChunkAsync(
        httpClient,
        collectionName,
        currentChunk.Id,
        currentArtefact,
        currentChunk,
        project.TenantId,
        isStale: false,
        vector: currentVector);

    var rawMatches = await QueryWeaviateDogfoodCollectionAsync(httpClient, collectionName, queryVector, limit: 5);
    var rawRelevantMatches = rawMatches
        .Where(match => match.ProjectId == project.ProjectId &&
                        (string.Equals(match.SourceVersionId, oldVersion.Id.ToString(), StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(match.SourceVersionId, currentVersion.Id.ToString(), StringComparison.OrdinalIgnoreCase)))
        .OrderBy(match => match.RawWeaviateRank)
        .ToArray();

    var artefactsById = new Dictionary<Guid, SemanticArtefact>
    {
        [oldArtefact.Id] = oldArtefact,
        [currentArtefact.Id] = currentArtefact
    };
    var chunksById = new Dictionary<Guid, SemanticChunk>
    {
        [oldChunk.Id] = oldChunk,
        [currentChunk.Id] = currentChunk
    };
    var versionsById = new Dictionary<string, ProjectDocumentVersion>(StringComparer.OrdinalIgnoreCase)
    {
        [oldVersion.Id.ToString()] = oldVersion,
        [currentVersion.Id.ToString()] = currentVersion
    };

    var candidates = rawRelevantMatches
        .Where(match => artefactsById.ContainsKey(match.ArtefactId) &&
                        chunksById.ContainsKey(match.ChunkId) &&
                        versionsById.ContainsKey(match.SourceVersionId ?? string.Empty))
        .Select(match =>
        {
            var version = versionsById[match.SourceVersionId!];
            return BuildCandidate(
                project.TenantId,
                project.ProjectId,
                document,
                version,
                artefactsById[match.ArtefactId],
                chunksById[match.ChunkId],
                version.Id == currentVersion.Id ? currentContent : oldContent,
                vectorSimilarity: match.VectorSimilarity,
                contentHashMismatch: false);
        })
        .ToArray();

    var query = new SemanticSearchQuery
    {
        ProjectId = project.ProjectId,
        QueryText = queryText,
        Consumer = "MemorySpineWeaviateSmoke",
        Limit = 5,
        IncludeStale = true
    };

    var results = ranker.Rank(query, candidates);
    var traceId = await traceRepository.CreateTraceAsync(query);
    await traceRepository.AddResultsAsync(traceId, results);
    var links = await documentService.GetLinksForVersionAsync(currentVersion.Id);

    var top = results.FirstOrDefault();
    var staleRawRank = rawRelevantMatches.FirstOrDefault(match => match.SourceVersionId == oldVersion.Id.ToString())?.RawWeaviateRank;
    var currentRawRank = rawRelevantMatches.FirstOrDefault(match => match.SourceVersionId == currentVersion.Id.ToString())?.RawWeaviateRank;
    var passed = top is not null &&
                 top.SourceVersionId == currentVersion.Id.ToString() &&
                 !top.IsStale &&
                 staleRawRank.HasValue &&
                 currentRawRank.HasValue &&
                 staleRawRank.Value < currentRawRank.Value &&
                 links.Any(link => link.LinkType == "CurrentGoalSource");

    var result = new MemoryWeaviateSqlVersionSmokeResult
    {
        DogfoodRunId = dogfoodRunId,
        TenantId = project.TenantId,
        ProjectId = project.ProjectId,
        ProjectName = project.ProjectName,
        DocumentId = document.Id,
        CurrentVersionId = currentVersion.Id,
        OldVersionId = oldVersion.Id,
        Query = queryText,
        WeaviateEndpoint = weaviateEndpoint,
        WeaviateCollection = collectionName,
        SemanticTraceId = traceId,
        SourceLinkCount = links.Count,
        Passed = passed,
        RawMatches = rawRelevantMatches,
        Results = results.Select((ranked, index) => new MemoryWeaviateSqlVersionSearchResult
        {
            FinalAuthorityRank = index + 1,
            RawWeaviateRank = rawRelevantMatches.FirstOrDefault(match => match.SourceVersionId == ranked.SourceVersionId)?.RawWeaviateRank,
            Title = ranked.Title,
            SourceEntityType = ranked.SourceEntityType,
            SourceEntityId = ranked.SourceEntityId,
            SourceVersionId = ranked.SourceVersionId,
            FinalScore = ranked.FinalScore,
            VectorSimilarity = ranked.VectorSimilarity,
            AuthorityBoost = ranked.AuthorityBoost,
            SourceTypeBoost = ranked.SourceTypeBoost,
            RecencyBoost = ranked.RecencyBoost,
            StalePenalty = ranked.StalePenalty,
            IsStale = ranked.IsStale,
            MatchReason = ranked.MatchReason
        }).ToArray()
    };

    Console.WriteLine(JsonSerializer.Serialize(result, options));
    return passed ? 0 : 1;
}

static async Task<int> HandleMemoryCrossProjectSmokeCommandAsync(string[] args, JsonSerializerOptions options)
{
    var queryProjectName = ReadOption(args, "--project") ?? "IronDev";
    var bleedProjectName = ReadOption(args, "--bleed-project") ?? "BookSeller";
    var dogfoodRunId = ReadOption(args, "--dogfood-run-id") ?? $"memory-cross-project-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    var queryText = ReadOption(args, "--query") ?? "first Codex goal checkout flow";
    var connectionString = ResolveIronDevConnectionString(args);
    var repoRoot = FindRepositoryRoot();
    var connectionFactory = new CliConnectionFactory(connectionString);

    await ApplySqlScriptAsync(
        connectionFactory,
        Path.Combine(repoRoot, "Database", "migrate_project_documents.sql"));

    var queryProject = await ResolveProjectAsync(connectionFactory, queryProjectName);
    var bleedProject = await ResolveProjectAsync(connectionFactory, bleedProjectName);
    if (queryProject is null)
    {
        Console.Error.WriteLine($"Project not found: {queryProjectName}");
        return 1;
    }

    if (bleedProject is null)
    {
        Console.Error.WriteLine($"Bleed-test project not found: {bleedProjectName}");
        return 1;
    }

    var queryTenant = new CliTenantContext(queryProject.TenantId);
    var bleedTenant = new CliTenantContext(bleedProject.TenantId);
    var queryDocumentService = new ProjectDocumentService(connectionFactory, queryTenant);
    var bleedDocumentService = new ProjectDocumentService(connectionFactory, bleedTenant);
    var artefactRepository = new SemanticArtefactRepository(connectionFactory);
    var chunkRepository = new SemanticChunkRepository(connectionFactory);
    var traceRepository = new SemanticSearchTraceRepository(connectionFactory);
    var ranker = new SemanticRankingService();

    var stamp = dogfoodRunId.Replace(':', '-').Replace('\\', '-').Replace('/', '-');
    var ironDevTitleSeed = $"CODEX_GOALS_CROSS_PROJECT_IRONDEV_{stamp}_{Guid.NewGuid():N}";
    var bookSellerTitleSeed = $"CODEX_GOALS_CROSS_PROJECT_BOOKSELLER_{stamp}_{Guid.NewGuid():N}";
    var ironDevTitle = ironDevTitleSeed[..Math.Min(120, ironDevTitleSeed.Length)];
    var bookSellerTitle = bookSellerTitleSeed[..Math.Min(120, bookSellerTitleSeed.Length)];
    var ironDevContent = """
        # IronDev Cross Project Memory Goal

        The first Codex goal is to prove IronDev memory spine retrieval and prevent project bleed.
        IronDev memory is authoritative only inside the IronDev project context.
        """;
    var bookSellerContent = """
        # BookSeller Cross Project Memory Goal

        The first Codex goal is to test BookSeller checkout, cart, payment, and order flow.
        This document is intentionally semantically tempting for checkout-flow queries.
        """;

    var ironDevDocument = await queryDocumentService.CreateDocumentAsync(new CreateProjectDocumentRequest
    {
        ProjectId = queryProject.ProjectId,
        Title = ironDevTitle,
        DocumentType = "Architecture",
        ContentMarkdown = ironDevContent,
        ChangeSummary = "IronDev cross-project authority source",
        CreatedBy = "TestAgent",
        SourceEntityType = "Discussion",
        SourceEntityId = 9201
    });
    var ironDevVersion = await queryDocumentService.GetCurrentVersionAsync(ironDevDocument.Id)
        ?? throw new InvalidOperationException("IronDev cross-project document version was not created.");
    await queryDocumentService.LinkVersionAsync(new LinkProjectDocumentVersionRequest
    {
        DocumentVersionId = ironDevVersion.Id,
        LinkedEntityType = "Discussion",
        LinkedEntityId = 9202,
        LinkType = "CurrentGoalSource",
        CreatedBy = "TestAgent"
    });

    var bookSellerDocument = await bleedDocumentService.CreateDocumentAsync(new CreateProjectDocumentRequest
    {
        ProjectId = bleedProject.ProjectId,
        Title = bookSellerTitle,
        DocumentType = "Architecture",
        ContentMarkdown = bookSellerContent,
        ChangeSummary = "BookSeller cross-project temptation source",
        CreatedBy = "TestAgent",
        SourceEntityType = "Discussion",
        SourceEntityId = 9301
    });
    var bookSellerVersion = await bleedDocumentService.GetCurrentVersionAsync(bookSellerDocument.Id)
        ?? throw new InvalidOperationException("BookSeller cross-project document version was not created.");

    var ironDevArtefactId = Guid.NewGuid();
    var bookSellerArtefactId = Guid.NewGuid();
    await IndexDocumentVersionAsync(
        artefactRepository,
        chunkRepository,
        queryProject.TenantId,
        queryProject.ProjectId,
        ironDevArtefactId,
        ironDevDocument,
        ironDevVersion,
        authorityLevel: "AcceptedArchitecture",
        content: ironDevContent);
    await IndexDocumentVersionAsync(
        artefactRepository,
        chunkRepository,
        bleedProject.TenantId,
        bleedProject.ProjectId,
        bookSellerArtefactId,
        bookSellerDocument,
        bookSellerVersion,
        authorityLevel: "AcceptedArchitecture",
        content: bookSellerContent);

    var ironDevArtefact = await artefactRepository.GetArtefactAsync(ironDevArtefactId)
        ?? throw new InvalidOperationException("IronDev semantic artefact was not persisted.");
    var bookSellerArtefact = await artefactRepository.GetArtefactAsync(bookSellerArtefactId)
        ?? throw new InvalidOperationException("BookSeller semantic artefact was not persisted.");
    var ironDevChunk = (await chunkRepository.GetChunksAsync(ironDevArtefactId, includeStale: true)).Single();
    var bookSellerChunk = (await chunkRepository.GetChunksAsync(bookSellerArtefactId, includeStale: true)).Single();

    var weaviateEndpoint = ReadOption(args, "--weaviate-endpoint") ?? ResolveWeaviateEndpoint();
    var collectionName = BuildWeaviateDogfoodCollectionName(dogfoodRunId);
    var queryVector = new[] { 1.0, 0.0, 0.0, 0.0 };
    var bookSellerVector = new[] { 1.0, 0.0, 0.0, 0.0 };
    var ironDevVector = new[] { 0.82, 0.5723635, 0.0, 0.0 };

    using var httpClient = new HttpClient { BaseAddress = new Uri(weaviateEndpoint.TrimEnd('/') + "/") };
    await EnsureWeaviateDogfoodCollectionAsync(httpClient, collectionName);
    await UpsertWeaviateChunkAsync(
        httpClient,
        collectionName,
        bookSellerChunk.Id,
        bookSellerArtefact,
        bookSellerChunk,
        bleedProject.TenantId,
        isStale: false,
        vector: bookSellerVector);
    await UpsertWeaviateChunkAsync(
        httpClient,
        collectionName,
        ironDevChunk.Id,
        ironDevArtefact,
        ironDevChunk,
        queryProject.TenantId,
        isStale: false,
        vector: ironDevVector);

    var rawMatches = await QueryWeaviateDogfoodCollectionAsync(httpClient, collectionName, queryVector, limit: 5);
    var rawRelevantMatches = rawMatches
        .Where(match =>
            string.Equals(match.SourceVersionId, ironDevVersion.Id.ToString(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(match.SourceVersionId, bookSellerVersion.Id.ToString(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(match => match.RawWeaviateRank)
        .ToArray();
    var acceptedMatches = rawRelevantMatches
        .Where(match => match.ProjectId == queryProject.ProjectId)
        .ToArray();

    var candidates = acceptedMatches
        .Where(match => match.ArtefactId == ironDevArtefact.Id && match.ChunkId == ironDevChunk.Id)
        .Select(match => BuildCandidate(
            queryProject.TenantId,
            queryProject.ProjectId,
            ironDevDocument,
            ironDevVersion,
            ironDevArtefact,
            ironDevChunk,
            ironDevContent,
            vectorSimilarity: match.VectorSimilarity,
            contentHashMismatch: false))
        .ToArray();

    var query = new SemanticSearchQuery
    {
        ProjectId = queryProject.ProjectId,
        QueryText = queryText,
        Consumer = "MemorySpineCrossProjectSmoke",
        Limit = 5,
        IncludeStale = true
    };

    var results = ranker.Rank(query, candidates);
    var traceId = await traceRepository.CreateTraceAsync(query);
    await traceRepository.AddResultsAsync(traceId, results);
    var links = await queryDocumentService.GetLinksForVersionAsync(ironDevVersion.Id);

    var rawTop = rawRelevantMatches.FirstOrDefault();
    var finalTop = results.FirstOrDefault();
    var passed = rawTop is not null &&
                 rawTop.ProjectId == bleedProject.ProjectId &&
                 finalTop is not null &&
                 finalTop.SourceVersionId == ironDevVersion.Id.ToString() &&
                 finalTop.Document.ProjectId == queryProject.ProjectId &&
                 links.Any(link => link.LinkType == "CurrentGoalSource");

    var decisions = rawRelevantMatches.Select(match =>
    {
        var isQueryProject = match.ProjectId == queryProject.ProjectId;
        var finalRank = results
            .Select((result, index) => new { result.SourceVersionId, Rank = index + 1 })
            .FirstOrDefault(result => string.Equals(result.SourceVersionId, match.SourceVersionId, StringComparison.OrdinalIgnoreCase))
            ?.Rank;

        return new CrossProjectMemoryDecision
        {
            RawWeaviateRank = match.RawWeaviateRank,
            FinalAuthorityRank = finalRank,
            ProjectId = match.ProjectId,
            ProjectName = match.ProjectId == queryProject.ProjectId ? queryProject.ProjectName : bleedProject.ProjectName,
            SourceVersionId = match.SourceVersionId,
            VectorSimilarity = match.VectorSimilarity,
            Decision = isQueryProject ? "accepted_project_authority" : "rejected_cross_project"
        };
    }).ToArray();

    var result = new MemoryCrossProjectSmokeResult
    {
        DogfoodRunId = dogfoodRunId,
        TenantId = queryProject.TenantId,
        QueryProjectId = queryProject.ProjectId,
        QueryProjectName = queryProject.ProjectName,
        BleedProjectId = bleedProject.ProjectId,
        BleedProjectName = bleedProject.ProjectName,
        Query = queryText,
        WeaviateEndpoint = weaviateEndpoint,
        WeaviateCollection = collectionName,
        SemanticTraceId = traceId,
        SourceLinkCount = links.Count,
        Passed = passed,
        RawMatches = rawRelevantMatches,
        Decisions = decisions,
        Results = results.Select((ranked, index) => new MemoryWeaviateSqlVersionSearchResult
        {
            FinalAuthorityRank = index + 1,
            RawWeaviateRank = rawRelevantMatches.FirstOrDefault(match => match.SourceVersionId == ranked.SourceVersionId)?.RawWeaviateRank,
            Title = ranked.Title,
            SourceEntityType = ranked.SourceEntityType,
            SourceEntityId = ranked.SourceEntityId,
            SourceVersionId = ranked.SourceVersionId,
            FinalScore = ranked.FinalScore,
            VectorSimilarity = ranked.VectorSimilarity,
            AuthorityBoost = ranked.AuthorityBoost,
            SourceTypeBoost = ranked.SourceTypeBoost,
            RecencyBoost = ranked.RecencyBoost,
            StalePenalty = ranked.StalePenalty,
            IsStale = ranked.IsStale,
            MatchReason = ranked.MatchReason
        }).ToArray()
    };

    Console.WriteLine(JsonSerializer.Serialize(result, options));
    return passed ? 0 : 1;
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

static async Task<int> HandleBuilderProposalSafetySmokeCommandAsync(string[] args, JsonSerializerOptions options)
{
    var requestedProjectName = ReadOption(args, "--project") ?? "IronDev";
    var dogfoodRunId = ReadOption(args, "--dogfood-run-id") ?? $"builder-proposal-safety-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    var connectionString = ResolveIronDevConnectionString(args);
    var repoRoot = FindRepositoryRoot();
    var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", dogfoodRunId, "builder-safety-target");
    Directory.CreateDirectory(runRoot);

    var targetFileName = "BuilderSafetyTarget.txt";
    var targetFilePath = Path.Combine(runRoot, targetFileName);
    const string beforeContent = "original builder safety fixture";
    const string afterContent = "changed builder safety fixture";
    await File.WriteAllTextAsync(targetFilePath, beforeContent);
    var beforeHash = ComputeFileSha256(targetFilePath);

    var connectionFactory = new CliConnectionFactory(connectionString);
    await ApplySqlScriptAsync(
        connectionFactory,
        Path.Combine(repoRoot, "Database", "migrate_project_documents.sql"));

    var baseProject = await ResolveProjectAsync(connectionFactory, requestedProjectName);
    if (baseProject is null)
    {
        Console.Error.WriteLine($"Project not found: {requestedProjectName}");
        return 1;
    }

    var tenant = new CliTenantContext(baseProject.TenantId);
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
    var disposableProjectName = $"IronDevBuilderProposalSafety016_{stamp}";
    var projectId = await projectService.CreateProjectAsync(new Project
    {
        TenantId = baseProject.TenantId,
        Name = disposableProjectName[..Math.Min(120, disposableProjectName.Length)],
        Description = "Disposable project for Memory Spine 016 builder proposal safety smoke.",
        LocalPath = runRoot
    });

    var documentTitleSeed = $"BUILDER_PROPOSAL_SAFETY_016_{Guid.NewGuid():N}";
    var document = await documentService.CreateDocumentAsync(new CreateProjectDocumentRequest
    {
        ProjectId = projectId,
        Title = documentTitleSeed[..Math.Min(120, documentTitleSeed.Length)],
        DocumentType = "Architecture",
        ContentMarkdown = """
            # Builder Proposal Safety

            Builder must generate reviewable proposals and stop before writes.
            Dry-run validation may inspect target files, but proposal generation must not modify them.
            Applying patches requires explicit approval and is outside this smoke.
            """,
        ChangeSummary = "Builder proposal safety source document",
        CreatedBy = "TestAgent",
        SourceEntityType = "Discussion",
        SourceEntityId = 16016
    });

    var sourceVersion = await documentService.GetCurrentVersionAsync(document.Id)
        ?? throw new InvalidOperationException("Builder proposal safety source document version was not created.");

    var ticket = new ProjectTicket
    {
        TenantId = baseProject.TenantId,
        ProjectId = projectId,
        SessionId = Guid.NewGuid(),
        Title = "Prove builder proposal remains approval-first",
        TicketType = "Test",
        Priority = "High",
        Summary = "Generate a deterministic builder proposal and prove no target files are changed before approval.",
        Problem = "Builder workflows are dangerous if proposal generation writes files or bypasses approval.",
        AcceptanceCriteria = "- Proposal is generated.\n- Dry-run validation runs.\n- Target file hash is unchanged.\n- Apply without implemented approval path changes no files.",
        Status = "Draft",
        Content = "Memory Spine 016 smoke ticket.",
        ContextSummary = $"Source ProjectDocumentVersion:{sourceVersion.Id}",
        LinkedFilePaths = targetFileName,
        IsGenerated = true,
        GenerationNote = "Memory Spine 016 builder proposal safety smoke",
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

    var proposalService = new DeterministicCodeChangeProposalService(
        targetFileName,
        beforeContent,
        afterContent);
    var patchService = new CodePatchService();
    var orchestrator = new TicketBuildOrchestrator(
        contextService,
        proposalService,
        patchService);

    var preview = await orchestrator.CreateBuildPreviewAsync(projectId, ticketId);
    var afterPreviewHash = ComputeFileSha256(targetFilePath);

    var approvalResult = await orchestrator.ApplyAndBuildAsync(new IronDev.Core.Builder.TicketBuildApproval
    {
        ProjectId = projectId,
        TicketId = ticketId,
        ProjectPath = runRoot,
        ApprovedProposal = preview.Proposal
    });
    var afterApplyAttemptHash = ComputeFileSha256(targetFilePath);

    var directPatchResult = await patchService.ApplyPatchesAsync(
        runRoot,
        preview.Proposal.FileChanges);
    var afterDirectApplyAttemptHash = ComputeFileSha256(targetFilePath);

    var fileUnchangedAfterPreview = beforeHash == afterPreviewHash;
    var fileUnchangedAfterApplyAttempt = beforeHash == afterApplyAttemptHash;
    var fileUnchangedAfterDirectPatchAttempt = beforeHash == afterDirectApplyAttemptHash;
    var proposalGenerated = preview.Proposal.FileChanges.Count > 0;
    var dryRunValidated = preview.ValidationResult.FileResults.Count > 0;
    var approvalGateBlockedApply = !approvalResult.PatchSucceeded &&
                                   approvalResult.FilesChanged.Count == 0 &&
                                   approvalResult.ErrorMessage.Contains("not implemented", StringComparison.OrdinalIgnoreCase);
    var directPatchBlocked = !directPatchResult.Succeeded &&
                             directPatchResult.FilesWritten.Count == 0;
    var sourceContextIncluded = preview.ContextSummary.Contains(ticket.Title, StringComparison.OrdinalIgnoreCase) ||
                                preview.TicketTitle == ticket.Title;

    var passed = proposalGenerated &&
                 dryRunValidated &&
                 preview.ValidationResult.AllValid &&
                 fileUnchangedAfterPreview &&
                 fileUnchangedAfterApplyAttempt &&
                 fileUnchangedAfterDirectPatchAttempt &&
                 approvalGateBlockedApply &&
                 directPatchBlocked &&
                 sourceContextIncluded;

    var result = new BuilderProposalSafetySmokeResult
    {
        Goal = "builder-proposal-safety-016",
        DogfoodRunId = dogfoodRunId,
        Passed = passed,
        TenantId = baseProject.TenantId,
        ProjectId = projectId,
        ProjectName = disposableProjectName,
        TicketId = ticketId,
        TicketTitle = ticket.Title,
        SourceDocumentId = document.Id,
        SourceDocumentVersionId = sourceVersion.Id,
        TargetFile = targetFilePath,
        Proposal = new BuilderProposalSafetyProposalEvidence
        {
            ProposalGenerated = proposalGenerated,
            ProposedFileCount = preview.Proposal.FileChanges.Count,
            ProposedFiles = preview.Proposal.FileChanges.Select(change => change.FilePath).ToArray(),
            Summary = preview.Proposal.Summary,
            Rationale = preview.Proposal.Rationale,
            RiskNotes = preview.Proposal.RiskNotes,
            TestPlan = preview.Proposal.TestPlan
        },
        Safety = new BuilderProposalSafetyFlags
        {
            DryRunValidationRan = dryRunValidated,
            DryRunValidationPassed = preview.ValidationResult.AllValid,
            FileUnchangedAfterPreview = fileUnchangedAfterPreview,
            ApprovalGateBlockedApply = approvalGateBlockedApply,
            FileUnchangedAfterApplyAttempt = fileUnchangedAfterApplyAttempt,
            DirectPatchApplyBlocked = directPatchBlocked,
            FileUnchangedAfterDirectPatchAttempt = fileUnchangedAfterDirectPatchAttempt,
            SourceContextIncluded = sourceContextIncluded
        },
        Evidence = new BuilderProposalSafetyEvidence
        {
            BeforeHash = beforeHash,
            AfterPreviewHash = afterPreviewHash,
            AfterApplyAttemptHash = afterApplyAttemptHash,
            AfterDirectPatchAttemptHash = afterDirectApplyAttemptHash,
            ValidationSummary = preview.ValidationResult.Summary,
            ValidationMessages = preview.ValidationResult.FileResults.Select(result => result.Message).ToArray(),
            ApplyErrorMessage = approvalResult.ErrorMessage,
            DirectPatchErrorMessage = directPatchResult.ErrorMessage,
            ContextSummary = preview.ContextSummary
        },
        Boundary = "This proves builder proposal safety only: context plus deterministic proposal plus dry-run validation. It does not apply patches, run builds, or prove LLM proposal quality."
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

static string ComputeFileSha256(string path)
{
    using var stream = File.OpenRead(path);
    var hash = System.Security.Cryptography.SHA256.HashData(stream);
    return Convert.ToHexString(hash);
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

static ReplayFailure? FindLatestReplayFailure(string runsRoot, string? selectedRunId)
{
    var resultFiles = Directory
        .EnumerateFiles(runsRoot, "replay-results.json", SearchOption.AllDirectories)
        .Select(path => new FileInfo(path))
        .Where(file => string.IsNullOrWhiteSpace(selectedRunId) ||
                       file.FullName.Contains($"{Path.DirectorySeparatorChar}{selectedRunId}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                       file.FullName.Contains($"{Path.AltDirectorySeparatorChar}{selectedRunId}{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(file => file.LastWriteTimeUtc);

    foreach (var file in resultFiles)
    {
        var text = File.ReadAllText(file.FullName);
        var results = JsonSerializer.Deserialize<List<ReplayCaseResult>>(text, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
        var failed = results.FirstOrDefault(result => !result.Passed);
        if (failed is null)
            continue;

        var replayDirectory = file.DirectoryName!;
        var planPath = Path.Combine(replayDirectory, "replay-plan.json");
        ReplayCase? replayCase = null;
        ReplayPlan? plan = null;
        if (File.Exists(planPath))
        {
            plan = JsonSerializer.Deserialize<ReplayPlan>(
                File.ReadAllText(planPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            replayCase = plan?.Cases.FirstOrDefault(item => string.Equals(item.CaseId, failed.CaseId, StringComparison.OrdinalIgnoreCase));
        }

        return new ReplayFailure(file, failed, replayCase, plan);
    }

    return null;
}

static TestAgentFailure? FindLatestTestAgentFailure(string runsRoot, string? selectedRunId)
{
    var reportFiles = Directory
        .EnumerateFiles(runsRoot, "test-agent-report.json", SearchOption.AllDirectories)
        .Select(path => new FileInfo(path))
        .Where(file => string.IsNullOrWhiteSpace(selectedRunId) ||
                       file.FullName.Contains($"{Path.DirectorySeparatorChar}{selectedRunId}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                       file.FullName.Contains($"{Path.AltDirectorySeparatorChar}{selectedRunId}{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(file => file.LastWriteTimeUtc);

    foreach (var file in reportFiles)
    {
        var text = File.ReadAllText(file.FullName);
        using var document = JsonDocument.Parse(text);
        var report = document.RootElement.Clone();
        var status = ReadJsonString(report, "status");
        var overall = ReadJsonString(report, "overall_result");
        var failures = ReadJsonInt(GetJsonPropertyOrDefault(report, "actual"), "steps_failed");
        var isFailure = failures > 0 ||
                        status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
                        overall.Equals("FAILED", StringComparison.OrdinalIgnoreCase) ||
                        overall.Equals("PARTIAL_SUCCESS", StringComparison.OrdinalIgnoreCase);

        if (isFailure)
            return new TestAgentFailure(file, report);
    }

    return null;
}

static FailurePackage BuildFailurePackage(ReplayFailure failure, string runRoot)
{
    var result = failure.Result;
    var replayCase = failure.ReplayCase;
    var plan = failure.Plan;
    var replayPlanPath = Path.Combine(failure.ResultFile.DirectoryName!, "replay-plan.json");
    var seed = replayCase?.Seed;
    var reps = plan?.Cases.Count > 0 ? plan.Cases.Count : 1;
    var reproCommand = seed is null
        ? $"dotnet run --project .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -- \"{replayPlanPath}\""
        : $"powershell -NoProfile -ExecutionPolicy Bypass -File .\\tools\\dogfood\\Start-BookSellerReplay.ps1 -RunId {result.DogfoodRunId}-repro -Scenario .\\tools\\dogfood\\dogfood-scenarios\\BookSellerMvp.json -Reps {reps} -DryRun -StopOnFailure -Seed {seed} -RunnerCommand \"dotnet run --project .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj --\"";

    return new FailurePackage
    {
        DogfoodRunId = result.DogfoodRunId,
        ScenarioId = replayCase?.ScenarioId ?? plan?.ScenarioId ?? "Unknown",
        GoalId = replayCase?.ScenarioId ?? plan?.ScenarioId ?? "Unknown",
        Step = replayCase?.Step,
        PromptVariantId = result.CaseId,
        CaseId = result.CaseId,
        CaseName = result.Name,
        Workspace = result.Workspace,
        Prompt = result.Prompt,
        ExpectedIntent = result.ExpectedIntent,
        ActualIntent = result.ActualIntent,
        AllowsProseResponse = result.AllowsProseResponse,
        RequiresAction = result.RequiresAction,
        ContextReference = result.ContextReference,
        DraftCountMode = result.DraftCountMode,
        FailureReason = result.FailureReason,
        MatchedSignals = result.MatchedSignals,
        ResultPath = failure.ResultFile.FullName,
        ReportPath = string.Empty,
        FailedStepLogPath = string.Empty,
        ReplayPlanPath = replayPlanPath,
        RunRoot = runRoot,
        ExpectedJson = "{}",
        ActualJson = "{}",
        EvidencePaths = [failure.ResultFile.FullName],
        LikelyAreas = InferLikelyAreas(result).ToArray(),
        ReproCommand = reproCommand,
        ValidationCommand = "dotnet build .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -p:UseSharedCompilation=false -nr:false; dotnet test .\\IronDev.IntegrationTests\\IronDev.IntegrationTests.csproj --no-restore --filter \"ChatCommandRouter_CreateTickets_IsActionFirst|ChatCommandRouter_SaveDecision_IsActionFirst|ChatCommandRouter_CreatePlan_IsActionFirst|ChatCommandRouter_BuildTicketQuestion_AllowsProse\" -p:UseSharedCompilation=false -nr:false",
        SafetyRules = [
            "Replay defaults to dry-run.",
            "Do not modify target project files while repairing routing failures.",
            "Patch deterministic routing/context logic before weakening assertions.",
            "Rerun the same seed before running chaos batches."
        ],
        CreatedAtUtc = DateTimeOffset.UtcNow
    };
}

static FailurePackage BuildTestAgentFailurePackage(TestAgentFailure failure, string runRoot)
{
    var report = failure.Report;
    var failedStep = FirstFailedStep(report);
    var evidencePaths = ReadEvidencePaths(report);
    var command = ReadJsonString(failedStep, "command");
    var failureReason = ReadJsonString(failedStep, "summary");
    if (string.IsNullOrWhiteSpace(failureReason))
        failureReason = string.Join("; ", ReadStringArray(report, "critical_issues"));

    var runId = ReadJsonString(report, "test_run_id");
    var goalId = ReadJsonString(report, "goal_id");
    var trace = GetJsonPropertyOrDefault(report, "trace");
    var traceGroupId = ReadJsonString(trace, "trace_group_id");
    var stepNumber = ReadJsonInt(failedStep, "step");
    var action = ReadJsonString(failedStep, "action");
    var logPath = ReadJsonString(failedStep, "log_path");
    var planPath = ReadJsonString(report, "plan_path");
    var reproPlanPath = string.IsNullOrWhiteSpace(planPath)
        ? $"<PLAN_PATH_FOR_{goalId}>"
        : planPath;

    return new FailurePackage
    {
        DogfoodRunId = runId,
        ScenarioId = goalId,
        GoalId = goalId,
        Step = stepNumber == 0 ? null : stepNumber,
        PromptVariantId = string.IsNullOrWhiteSpace(action) ? "test-agent-step" : action,
        CaseId = string.IsNullOrWhiteSpace(action) ? "test-agent-step" : action,
        CaseName = action,
        Workspace = "TestAgent",
        Prompt = command,
        ExpectedIntent = "TestAgentStepSuccess",
        ActualIntent = ReadJsonString(failedStep, "status"),
        AllowsProseResponse = false,
        RequiresAction = true,
        ContextReference = traceGroupId,
        DraftCountMode = "N/A",
        FailureReason = failureReason,
        MatchedSignals = ReadStringArray(report, "critical_issues"),
        ResultPath = failure.ReportFile.FullName,
        ReportPath = failure.ReportFile.FullName,
        FailedStepLogPath = logPath,
        ReplayPlanPath = string.Empty,
        RunRoot = runRoot,
        ExpectedJson = ToCompactJson(GetJsonPropertyOrDefault(report, "expected")),
        ActualJson = ToCompactJson(GetJsonPropertyOrDefault(report, "actual")),
        EvidencePaths = evidencePaths,
        LikelyAreas = InferLikelyAreasFromTestAgentFailure(goalId, action, failureReason).ToArray(),
        ReproCommand = $"powershell -NoProfile -ExecutionPolicy Bypass -File .\\tools\\dogfood\\Invoke-TestAgentPlan.ps1 -PlanPath \"{reproPlanPath}\" -RunId {runId}-repro -Json",
        ValidationCommand = $"dotnet build .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -p:UseSharedCompilation=false -nr:false; powershell -NoProfile -ExecutionPolicy Bypass -File .\\tools\\dogfood\\Invoke-TestAgentPlan.ps1 -PlanPath \"{reproPlanPath}\" -RunId validation-after-fix -Json",
        SafetyRules = [
            "Do not fix without preserving the failing report and log paths as evidence.",
            "Do not weaken the Test Agent assertion just to make the package pass.",
            "Patch the smallest command, router, memory, or harness behaviour that explains the observed failure.",
            "Rerun the failing plan before broad regression plans.",
            "Replay and Test Agent commands default to dry-run unless a plan explicitly allows writes."
        ],
        CreatedAtUtc = DateTimeOffset.UtcNow
    };
}

static IEnumerable<string> InferLikelyAreasFromTestAgentFailure(string goalId, string action, string reason)
{
    if (action.Contains("memory", StringComparison.OrdinalIgnoreCase) ||
        goalId.Contains("memory", StringComparison.OrdinalIgnoreCase))
    {
        yield return "tools/IronDev.ReplayRunner/Program.cs";
        yield return "IronDev.Infrastructure/Services/SemanticMemory";
        yield return "tools/dogfood/Invoke-TestAgentPlan.ps1";
        yield break;
    }

    if (action.Contains("builder", StringComparison.OrdinalIgnoreCase) ||
        goalId.Contains("builder", StringComparison.OrdinalIgnoreCase))
    {
        yield return "IronDev.Infrastructure/Builder";
        yield return "tools/IronDev.ReplayRunner/Program.cs";
        yield return "tools/dogfood/Invoke-TestAgentPlan.ps1";
        yield break;
    }

    if (reason.Contains("schema", StringComparison.OrdinalIgnoreCase))
    {
        yield return "tools/dogfood/TestAgentReport.schema.json";
        yield return "tools/dogfood/Invoke-TestAgentPlan.ps1";
        yield break;
    }

    yield return "tools/dogfood/Invoke-TestAgentPlan.ps1";
    yield return "tools/IronDev.ReplayRunner/Program.cs";
}

static IEnumerable<string> InferLikelyAreas(ReplayCaseResult result)
{
    if (result.AllowsProseResponse && result.ExpectedIntent.Contains("Ticket", StringComparison.OrdinalIgnoreCase))
    {
        yield return "IronDev.Infrastructure/Services/ChatIntentParser.cs";
        yield return "IronDev.Infrastructure/Services/ChatCommandRouter.cs";
        yield break;
    }

    if (result.ExpectedIntent.Contains("Discussion", StringComparison.OrdinalIgnoreCase) ||
        result.ExpectedIntent.Contains("Document", StringComparison.OrdinalIgnoreCase))
    {
        yield return "IronDev.Infrastructure/Services/ChatCommandRouter.cs";
        yield break;
    }

    yield return "tools/IronDev.ReplayRunner/Program.cs";
}

static string BuildFailurePackageMarkdown(FailurePackage package)
{
    return $"""
    # Codex Failure Package

    DogfoodRunId: {package.DogfoodRunId}
    ScenarioId: {package.ScenarioId}
    GoalId: {package.GoalId}
    CaseId: {package.CaseId}
    Step: {package.Step?.ToString() ?? "Unknown"}
    Workspace: {package.Workspace}

    ## Prompt

    ```text
    {package.Prompt}
    ```

    ## Expected

    ```text
    Intent: {package.ExpectedIntent}
    ExpectedJson: {package.ExpectedJson}
    ```

    ## Actual

    ```text
    Intent: {package.ActualIntent}
    AllowsProseResponse: {package.AllowsProseResponse}
    RequiresAction: {package.RequiresAction}
    ContextReference: {package.ContextReference}
    DraftCountMode: {package.DraftCountMode}
    Failure: {package.FailureReason}
    ActualJson: {package.ActualJson}
    ```

    ## Evidence

    ResultPath: {package.ResultPath}
    ReportPath: {package.ReportPath}
    FailedStepLogPath: {package.FailedStepLogPath}

    {string.Join(Environment.NewLine, package.EvidencePaths.Select(path => $"- {path}"))}

    ## Likely Areas

    {string.Join(Environment.NewLine, package.LikelyAreas.Select(area => $"- {area}"))}

    ## Repro

    ```powershell
    {package.ReproCommand}
    ```

    ## Validation

    ```powershell
    {package.ValidationCommand}
    ```

    ## Safety Rules

    {string.Join(Environment.NewLine, package.SafetyRules.Select(rule => $"- {rule}"))}
    """;
}

static JsonElement FirstFailedStep(JsonElement report)
{
    if (!report.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
        return default;

    foreach (var step in steps.EnumerateArray())
    {
        if (ReadJsonString(step, "status").Equals("FAILED", StringComparison.OrdinalIgnoreCase))
            return step.Clone();
    }

    return default;
}

static IReadOnlyList<string> ReadEvidencePaths(JsonElement report)
{
    if (!report.TryGetProperty("evidence", out var evidence) || evidence.ValueKind != JsonValueKind.Array)
        return [];

    return evidence.EnumerateArray()
        .Select(item => ReadJsonString(item, "path"))
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .ToArray();
}

static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        return [];

    return value.EnumerateArray()
        .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString())
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToArray();
}

static string ReadJsonString(JsonElement element, string propertyName)
{
    if (element.ValueKind == JsonValueKind.Undefined ||
        !element.TryGetProperty(propertyName, out var value))
    {
        return string.Empty;
    }

    return value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : value.ToString();
}

static int ReadJsonInt(JsonElement element, string propertyName)
{
    if (element.ValueKind == JsonValueKind.Undefined ||
        !element.TryGetProperty(propertyName, out var value))
    {
        return 0;
    }

    if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed))
        return parsed;

    return int.TryParse(value.ToString(), out parsed) ? parsed : 0;
}

static string ToCompactJson(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Undefined)
        return "{}";

    return JsonSerializer.Serialize(JsonNode.Parse(element.GetRawText()), new JsonSerializerOptions { WriteIndented = false });
}

static JsonElement GetJsonPropertyOrDefault(JsonElement element, string propertyName)
{
    return element.ValueKind != JsonValueKind.Undefined &&
           element.TryGetProperty(propertyName, out var value)
        ? value
        : default;
}

static async Task IndexDocumentVersionAsync(
    SemanticArtefactRepository artefactRepository,
    SemanticChunkRepository chunkRepository,
    int tenantId,
    int projectId,
    Guid artefactId,
    ProjectDocument document,
    ProjectDocumentVersion version,
    string authorityLevel,
    string content)
{
    var hash = ComputeSha256(content);
    var artefact = new SemanticArtefactDraft
    {
        Id = artefactId,
        TenantId = tenantId,
        ProjectId = projectId,
        SourceEntityType = "ProjectDocument",
        SourceEntityId = document.Id.ToString(),
        SourceVersionId = version.Id.ToString(),
        ArtefactType = document.DocumentType,
        AuthorityLevel = authorityLevel,
        Title = document.Title,
        Summary = version.ChangeSummary,
        SearchableText = content,
        ContentHash = hash
    };

    await artefactRepository.UpsertArtefactAsync(artefact);
    await chunkRepository.ReplaceChunksAsync(artefactId, [
        new SemanticChunkDraft
        {
            Id = Guid.NewGuid(),
            ArtefactId = artefactId,
            ProjectId = projectId,
            ChunkIndex = 0,
            ChunkText = content,
            TokenEstimate = Math.Max(1, content.Length / 4),
            ContentHash = hash
        }
    ]);
}

static SemanticSearchCandidate BuildCandidate(
    int tenantId,
    int projectId,
    ProjectDocument document,
    ProjectDocumentVersion version,
    SemanticArtefact artefact,
    SemanticChunk chunk,
    string content,
    double vectorSimilarity,
    bool contentHashMismatch)
{
    return new SemanticSearchCandidate
    {
        Document = new ProjectContextDocument
        {
            TenantId = tenantId,
            ProjectId = projectId,
            DocumentType = document.DocumentType,
            AuthorityLevel = artefact.AuthorityLevel,
            Status = version.Id == document.CurrentVersionId ? "Active" : "Superseded",
            Title = document.Title,
            Content = content,
            Summary = version.ChangeSummary,
            Source = $"ProjectDocumentVersion:{version.Id}",
            CreatedDate = version.CreatedAtUtc,
            UpdatedDate = version.CreatedAtUtc
        },
        Artefact = artefact,
        Chunk = chunk,
        VectorSimilarity = vectorSimilarity,
        ContentHashMismatch = contentHashMismatch
    };
}

static async Task EnsureWeaviateDogfoodCollectionAsync(HttpClient httpClient, string collectionName)
{
    var existsResponse = await httpClient.GetAsync($"v1/schema/{collectionName}");
    if (existsResponse.IsSuccessStatusCode)
        return;

    var schema = new
    {
        @class = collectionName,
        vectorizer = "none",
        properties = new object[]
        {
            new { name = "chunkId", dataType = new[] { "text" } },
            new { name = "artefactId", dataType = new[] { "text" } },
            new { name = "tenantId", dataType = new[] { "int" } },
            new { name = "projectId", dataType = new[] { "int" } },
            new { name = "sourceEntityType", dataType = new[] { "text" } },
            new { name = "sourceEntityId", dataType = new[] { "text" } },
            new { name = "sourceVersionId", dataType = new[] { "text" } },
            new { name = "artefactType", dataType = new[] { "text" } },
            new { name = "authorityLevel", dataType = new[] { "text" } },
            new { name = "title", dataType = new[] { "text" } },
            new { name = "chunkText", dataType = new[] { "text" } },
            new { name = "chunkIndex", dataType = new[] { "int" } },
            new { name = "contentHash", dataType = new[] { "text" } },
            new { name = "isStale", dataType = new[] { "boolean" } }
        }
    };

    var response = await PostJsonAsync(httpClient, "v1/schema", schema);
    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity &&
            body.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException($"Weaviate schema create failed for {collectionName}: {(int)response.StatusCode} {body}");
    }
}

static async Task UpsertWeaviateChunkAsync(
    HttpClient httpClient,
    string collectionName,
    Guid objectId,
    SemanticArtefact artefact,
    SemanticChunk chunk,
    int tenantId,
    bool isStale,
    IReadOnlyList<double> vector)
{
    var payload = new
    {
        @class = collectionName,
        id = objectId.ToString(),
        properties = new
        {
            chunkId = chunk.Id.ToString(),
            artefactId = artefact.Id.ToString(),
            tenantId,
            projectId = artefact.ProjectId,
            sourceEntityType = artefact.SourceEntityType,
            sourceEntityId = artefact.SourceEntityId,
            sourceVersionId = artefact.SourceVersionId ?? string.Empty,
            artefactType = artefact.ArtefactType,
            authorityLevel = artefact.AuthorityLevel,
            title = artefact.Title,
            chunkText = chunk.ChunkText,
            chunkIndex = chunk.ChunkIndex,
            contentHash = chunk.ContentHash,
            isStale
        },
        vector
    };

    var response = await PostJsonAsync(httpClient, "v1/objects", payload);
    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.UnprocessableEntity &&
            body.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            response = await PutJsonAsync(httpClient, $"v1/objects/{collectionName}/{objectId}", payload);
            if (response.IsSuccessStatusCode)
                return;

            body = await response.Content.ReadAsStringAsync();
        }

        throw new InvalidOperationException($"Weaviate object upsert failed for {objectId}: {(int)response.StatusCode} {body}");
    }
}

static async Task<IReadOnlyList<WeaviateRawMatch>> QueryWeaviateDogfoodCollectionAsync(
    HttpClient httpClient,
    string collectionName,
    IReadOnlyList<double> queryVector,
    int limit)
{
    var vectorText = string.Join(",", queryVector.Select(value => value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)));
    var graphQl = new
    {
        query = $$"""
        {
          Get {
            {{collectionName}}(
              nearVector: { vector: [{{vectorText}}] }
              limit: {{limit}}
            ) {
              chunkId
              artefactId
              tenantId
              projectId
              sourceEntityType
              sourceEntityId
              sourceVersionId
              title
              isStale
              _additional {
                id
                distance
              }
            }
          }
        }
        """
    };

    var response = await PostJsonAsync(httpClient, "v1/graphql", graphQl);
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException($"Weaviate GraphQL query failed: {(int)response.StatusCode} {body}");

    using var document = JsonDocument.Parse(body);
    if (!document.RootElement.TryGetProperty("data", out var data) ||
        !data.TryGetProperty("Get", out var get) ||
        !get.TryGetProperty(collectionName, out var objects) ||
        objects.ValueKind != JsonValueKind.Array)
    {
        return [];
    }

    var matches = new List<WeaviateRawMatch>();
    var rank = 1;
    foreach (var item in objects.EnumerateArray())
    {
        var distance = item.TryGetProperty("_additional", out var additional) &&
                       additional.TryGetProperty("distance", out var distanceElement) &&
                       distanceElement.TryGetDouble(out var parsedDistance)
            ? parsedDistance
            : 1d;

        matches.Add(new WeaviateRawMatch
        {
            RawWeaviateRank = rank++,
            ChunkId = ParseGuidProperty(item, "chunkId"),
            ArtefactId = ParseGuidProperty(item, "artefactId"),
            TenantId = ReadIntProperty(item, "tenantId"),
            ProjectId = ReadIntProperty(item, "projectId"),
            SourceEntityType = ReadStringProperty(item, "sourceEntityType"),
            SourceEntityId = ReadStringProperty(item, "sourceEntityId"),
            SourceVersionId = ReadStringProperty(item, "sourceVersionId"),
            Title = ReadStringProperty(item, "title"),
            IsStale = item.TryGetProperty("isStale", out var staleElement) && staleElement.ValueKind == JsonValueKind.True,
            Distance = distance,
            VectorSimilarity = Math.Clamp(1d - distance, 0d, 1d)
        });
    }

    return matches;
}

static async Task<HttpResponseMessage> PostJsonAsync(HttpClient httpClient, string requestUri, object payload)
{
    var json = JsonSerializer.Serialize(payload);
    using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    return await httpClient.PostAsync(requestUri, content);
}

static async Task<HttpResponseMessage> PutJsonAsync(HttpClient httpClient, string requestUri, object payload)
{
    var json = JsonSerializer.Serialize(payload);
    using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    return await httpClient.PutAsync(requestUri, content);
}

static Guid ParseGuidProperty(JsonElement item, string propertyName)
    => Guid.TryParse(ReadStringProperty(item, propertyName), out var value) ? value : Guid.Empty;

static int ReadIntProperty(JsonElement item, string propertyName)
{
    if (!item.TryGetProperty(propertyName, out var value))
        return 0;

    if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed))
        return parsed;

    return int.TryParse(value.ToString(), out parsed) ? parsed : 0;
}

static string ReadStringProperty(JsonElement item, string propertyName)
    => item.TryGetProperty(propertyName, out var value) ? value.ToString() : string.Empty;

static string BuildWeaviateDogfoodCollectionName(string dogfoodRunId)
{
    var hash = ComputeSha256(dogfoodRunId)[..12];
    return $"IronDevDogfoodMemoryChunks{hash}";
}

static string ResolveWeaviateEndpoint()
{
    var envEndpoint = Environment.GetEnvironmentVariable("IRONDEV_WEAVIATE_ENDPOINT");
    if (!string.IsNullOrWhiteSpace(envEndpoint))
        return envEndpoint;

    var repoRoot = FindRepositoryRoot();
    foreach (var path in new[]
    {
        Path.Combine(repoRoot, "IronDeveloper", "appsettings.Development.json"),
        Path.Combine(repoRoot, "IronDeveloper", "appsettings.json")
    })
    {
        var endpoint = TryReadNestedString(path, "Weaviate", "Endpoint");
        if (!string.IsNullOrWhiteSpace(endpoint))
            return endpoint;
    }

    return "http://localhost:8080";
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

static string? TryReadNestedString(string path, string sectionName, string propertyName)
{
    if (!File.Exists(path))
        return null;

    using var document = JsonDocument.Parse(File.ReadAllText(path));
    if (document.RootElement.TryGetProperty(sectionName, out var section) &&
        section.TryGetProperty(propertyName, out var value))
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

static string ComputeSha256(string text)
{
    var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
    return Convert.ToHexString(bytes);
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

static SemanticArtefact BuildKnowledgeArtefact(string projectName, KnowledgeDocument document, string body)
{
    var id = DeterministicGuid($"artefact:{document.Id}");
    return new SemanticArtefact
    {
        Id = id,
        TenantId = 0,
        ProjectId = StableProjectId(projectName),
        SourceEntityType = "DogfoodKnowledgeDocument",
        SourceEntityId = document.Id,
        SourceVersionId = document.Id,
        ArtefactType = document.DocumentType,
        AuthorityLevel = MapKnowledgeAuthorityLevel(document),
        Title = document.Title,
        Summary = $"Dogfood knowledge imported from {document.Source}",
        ContentHash = ComputeSha256(body),
        IsStale = IsKnowledgeDocumentStale(document),
        CreatedUtc = document.CreatedUtc.UtcDateTime,
        UpdatedUtc = document.CreatedUtc.UtcDateTime
    };
}

static SemanticChunk BuildKnowledgeChunk(SemanticArtefact artefact, string body, Guid? chunkId = null)
    => new()
    {
        Id = chunkId ?? DeterministicGuid($"chunk:{artefact.SourceEntityId}"),
        ArtefactId = artefact.Id,
        ProjectId = artefact.ProjectId,
        ChunkIndex = 0,
        ChunkText = body,
        TokenEstimate = Math.Max(1, body.Length / 4),
        ContentHash = artefact.ContentHash,
        IsStale = artefact.IsStale
    };

static bool IsKnowledgeDocumentStale(KnowledgeDocument document)
    => document.Authority.Contains("Superseded", StringComparison.OrdinalIgnoreCase) ||
       document.Authority.Contains("Stale", StringComparison.OrdinalIgnoreCase) ||
       document.Title.Contains("superseded", StringComparison.OrdinalIgnoreCase);

static string MapKnowledgeAuthorityLevel(KnowledgeDocument document)
{
    if (document.Authority.Contains("Accepted", StringComparison.OrdinalIgnoreCase) &&
        document.DocumentType.Contains("Architecture", StringComparison.OrdinalIgnoreCase))
        return "AcceptedArchitecture";

    if (document.Authority.Contains("Accepted", StringComparison.OrdinalIgnoreCase))
        return "AcceptedRequirement";

    if (document.Authority.Contains("Resolved", StringComparison.OrdinalIgnoreCase))
        return "ResolvedKnowledge";

    if (document.Authority.Contains("Draft", StringComparison.OrdinalIgnoreCase) ||
        document.Authority.Contains("Working", StringComparison.OrdinalIgnoreCase))
        return "ChatSummary";

    return document.Authority;
}

static bool HasTitleTermOverlap(string title, string query)
{
    var terms = query
        .Split([' ', '\t', '\r', '\n', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(term => term.Length >= 4)
        .ToArray();

    return terms.Any(term => title.Contains(term, StringComparison.OrdinalIgnoreCase));
}

static double GetTitleOverlapBoost(string title, string query)
{
    var terms = query
        .Split([' ', '\t', '\r', '\n', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(term => term.Length >= 4)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var hits = terms.Count(term => title.Contains(term, StringComparison.OrdinalIgnoreCase));
    return Math.Min(0.60, hits * 0.25);
}

static IReadOnlyList<double> BuildLexicalVector(string text, int dimensions = 32)
{
    var vector = new double[dimensions];
    foreach (var rawTerm in text.Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '/', '\\', '|', '-', '_', '`', '"', '\'', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var term = rawTerm.ToLowerInvariant();
        var hash = StablePositiveHash(term);
        vector[hash % dimensions] += 1.0;
    }

    var magnitude = Math.Sqrt(vector.Sum(value => value * value));
    if (magnitude <= 0)
        return vector;

    for (var i = 0; i < vector.Length; i++)
        vector[i] /= magnitude;

    return vector;
}

static Guid DeterministicGuid(string value)
{
    var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value));
    return new Guid(bytes);
}

static int StableProjectId(string projectName)
    => StablePositiveHash(projectName) % 100_000 + 1;

static long StableLongId(string value)
{
    var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
    return Math.Abs(BitConverter.ToInt64(bytes, 0));
}

static int StablePositiveHash(string value)
{
    var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
    return BitConverter.ToInt32(bytes, 0) & int.MaxValue;
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

public sealed record ReplayFailure(
    FileInfo ResultFile,
    ReplayCaseResult Result,
    ReplayCase? ReplayCase,
    ReplayPlan? Plan);

public sealed record TestAgentFailure(
    FileInfo ReportFile,
    JsonElement Report);

public sealed class FailurePackageCommandResult
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public string ScenarioId { get; init; } = string.Empty;
    public string CaseId { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
    public string ExpectedIntent { get; init; } = string.Empty;
    public string ActualIntent { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public string JsonPath { get; init; } = string.Empty;
    public string MarkdownPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string ReproCommand { get; init; } = string.Empty;
    public string ValidationCommand { get; init; } = string.Empty;
}

public sealed class FailurePackage
{
    public string DogfoodRunId { get; init; } = string.Empty;
    public string ScenarioId { get; init; } = string.Empty;
    public string GoalId { get; init; } = string.Empty;
    public int? Step { get; init; }
    public string PromptVariantId { get; init; } = string.Empty;
    public string CaseId { get; init; } = string.Empty;
    public string CaseName { get; init; } = string.Empty;
    public string Workspace { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
    public string ExpectedIntent { get; init; } = string.Empty;
    public string ActualIntent { get; init; } = string.Empty;
    public bool AllowsProseResponse { get; init; }
    public bool RequiresAction { get; init; }
    public string ContextReference { get; init; } = string.Empty;
    public string DraftCountMode { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public IReadOnlyList<string> MatchedSignals { get; init; } = [];
    public string ResultPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string FailedStepLogPath { get; init; } = string.Empty;
    public string ReplayPlanPath { get; init; } = string.Empty;
    public string RunRoot { get; init; } = string.Empty;
    public string ExpectedJson { get; init; } = string.Empty;
    public string ActualJson { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidencePaths { get; init; } = [];
    public IReadOnlyList<string> LikelyAreas { get; init; } = [];
    public string ReproCommand { get; init; } = string.Empty;
    public string ValidationCommand { get; init; } = string.Empty;
    public IReadOnlyList<string> SafetyRules { get; init; } = [];
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
