using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Infrastructure.Services.Agents;

public static class LiveRemainingGovernedAgents161Command
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var runId = ReadOption(args, "--run-id") ?? ReadOption(args, "--dogfood-run-id") ?? $"LiveRemainingAgents161-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var repoRoot = FindRepositoryRoot();
        var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", runId);
        Directory.CreateDirectory(runRoot);

        var profiles = LoadProfiles(repoRoot);
        var resolver = new AgentModelResolver(profiles);
        var definitions = AgentModelDefaults.CreateDefaultDefinitions();
        var localClient = new AgentLlmClient();

        var researchFallback = await RunResearchAsync(definitions, resolver, null, "cheap-runner", false, runId, "fallback");
        var researchLive = await RunResearchAsync(definitions, resolver, localClient, "local-cheap-runner", true, runId, "live-local");
        var qualityFallback = await RunQualityAsync(definitions, resolver, repoRoot, null, "cheap-runner", false, runId, "fallback");
        var qualityLive = await RunQualityAsync(definitions, resolver, repoRoot, localClient, "local-cheap-runner", true, runId, "live-local");
        var supervisorFallback = await RunSupervisorAsync(definitions, resolver, repoRoot, null, "strong-reasoner", false, runId, "fallback");
        var supervisorLive = await RunSupervisorAsync(definitions, resolver, repoRoot, localClient, "local-cheap-runner", true, runId, "live-local");

        var report = BuildReport(
            runId,
            runRoot,
            researchFallback,
            researchLive,
            qualityFallback,
            qualityLive,
            supervisorFallback,
            supervisorLive);

        await WriteEvidenceAsync(runRoot, options, researchFallback, researchLive, qualityFallback, qualityLive, supervisorFallback, supervisorLive, report);
        Console.WriteLine(JsonSerializer.Serialize(report, options));
        return string.Equals(ReadString(JsonSerializer.Serialize(report), "status", "Failed"), "Succeeded", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private static object BuildReport(
        string runId,
        string runRoot,
        AgentResult researchFallback,
        AgentResult researchLive,
        AgentResult qualityFallback,
        AgentResult qualityLive,
        AgentResult supervisorFallback,
        AgentResult supervisorLive)
    {
        var researchFallbackJson = JsonSerializer.Deserialize<JsonElement>(researchFallback.OutputJson);
        var researchLiveJson = JsonSerializer.Deserialize<JsonElement>(researchLive.OutputJson);
        var qualityFallbackJson = JsonSerializer.Deserialize<JsonElement>(qualityFallback.OutputJson);
        var qualityLiveJson = JsonSerializer.Deserialize<JsonElement>(qualityLive.OutputJson);
        var supervisorFallbackJson = JsonSerializer.Deserialize<JsonElement>(supervisorFallback.OutputJson);
        var supervisorLiveJson = JsonSerializer.Deserialize<JsonElement>(supervisorLive.OutputJson);

        var researchLiveLlm = researchLiveJson.GetProperty("llmIntelligence");
        var qualityLiveLlm = qualityLiveJson.GetProperty("LlmIntelligence");
        var supervisorLiveLlm = supervisorLiveJson.GetProperty("supervisor").GetProperty("llmIntelligence");
        var passed = AllSucceeded(researchFallback, researchLive, qualityFallback, qualityLive, supervisorFallback, supervisorLive) &&
                     ReadBool(researchLiveLlm, "wasAttempted") &&
                     ReadBool(qualityLiveLlm, "wasAttempted") &&
                     ReadBool(supervisorLiveLlm, "wasAttempted") &&
                     StringEquals(researchFallbackJson, "type", "ResearchPackage") &&
                     StringEquals(qualityFallbackJson, "Status", "passed") &&
                     StringEquals(supervisorFallbackJson, "supervisor", "decision", "report_ready");

        return new
        {
            command = "campaign live-remaining-agents-161",
            status = passed ? "Succeeded" : "Failed",
            runId,
            traceId = Guid.NewGuid().ToString("N"),
            project = "IronDev",
            summary = passed
                ? "Live ResearchAgent, QualityAgent, and SupervisorAgent campaign smoke passed."
                : "Live remaining governed agents campaign smoke found missing evidence.",
            governedAgents = new[] { "ResearchAgent", "QualityAgent", "SupervisorAgent" },
            researchFallbackPackage = researchFallbackJson,
            researchLivePackage = researchLiveJson,
            qualityFallbackReport = qualityFallbackJson,
            qualityLiveReport = qualityLiveJson,
            supervisorFallbackLoop = supervisorFallbackJson,
            supervisorLiveLoop = supervisorLiveJson,
            liveProviderHandling = new
            {
                profile = "local-cheap-runner",
                researchAttempted = ReadBool(researchLiveLlm, "wasAttempted"),
                researchInvocationMode = ReadString(researchLiveLlm, "invocationMode", string.Empty),
                qualityAttempted = ReadBool(qualityLiveLlm, "wasAttempted"),
                qualityInvocationMode = ReadString(qualityLiveLlm, "invocationMode", string.Empty),
                supervisorAttempted = ReadBool(supervisorLiveLlm, "wasAttempted"),
                supervisorInvocationMode = ReadString(supervisorLiveLlm, "invocationMode", string.Empty),
                boundary = "Live model execution is opt-in and failure falls back to deterministic packaging, quality gates, and orchestration."
            },
            governance = new
            {
                conscienceRequired = true,
                thoughtLedgerRequired = true,
                realRepoWritesBlocked = true,
                memoryMutationBlocked = true,
                ticketCreationBlocked = true,
                patchApplyBlocked = true,
                selfApprovalBlocked = true,
                deterministicGatesRemainAuthoritative = true
            },
            evidence = Evidence(runRoot),
            boundary = "161 proves opt-in live ResearchAgent, QualityAgent, and SupervisorAgent evidence paths. It does not grant writes, memory mutation, ticket creation, patch apply, quality override, self-approval, or ungated autonomy.",
            reproCommand = $"dotnet run --project .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -- campaign live-remaining-agents-161 --run-id {runId} --json"
        };
    }

    private static async Task WriteEvidenceAsync(
        string runRoot,
        JsonSerializerOptions options,
        AgentResult researchFallback,
        AgentResult researchLive,
        AgentResult qualityFallback,
        AgentResult qualityLive,
        AgentResult supervisorFallback,
        AgentResult supervisorLive,
        object report)
    {
        await File.WriteAllTextAsync(Path.Combine(runRoot, "research-fallback-package.json"), researchFallback.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "research-live-local-package.json"), researchLive.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "quality-fallback-report.json"), qualityFallback.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "quality-live-local-report.json"), qualityLive.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "supervisor-fallback-loop.json"), supervisorFallback.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "supervisor-live-local-loop.json"), supervisorLive.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.json"), JsonSerializer.Serialize(report, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.md"), "# Live Remaining Governed Agents 161\n\nOpt-in live paths for ResearchAgent, QualityAgent, and SupervisorAgent with deterministic fallback and no new authority.\n");
    }

    private static async Task<AgentResult> RunResearchAsync(IReadOnlyList<AgentDefinition> definitions, AgentModelResolver resolver, IAgentLlmClient? client, string profile, bool live, string runId, string caseId)
    {
        var agent = new ResearchAgent(OverrideProfile(definitions, "ResearchAgent", profile), resolver, client);
        return await agent.RunAsync(new AgentRequest
        {
            AgentName = "ResearchAgent",
            GoalId = "live-remaining-governed-agents-161",
            DogfoodRunId = $"{runId}-research-{caseId}",
            Inputs = new Dictionary<string, string>
            {
                ["project"] = "BookSeller",
                ["topic"] = "BookSeller inventory evidence packaging",
                ["source_url"] = "https://learn.microsoft.com/dotnet/",
                ["source_title"] = ".NET documentation evidence placeholder",
                ["source_type"] = "OfficialDocumentation",
                ["snippet"] = "Separate catalogue identity from stock quantity and prevent negative stock in domain logic.",
                ["live_llm"] = live.ToString()
            }
        });
    }

    private static async Task<AgentResult> RunQualityAsync(IReadOnlyList<AgentDefinition> definitions, AgentModelResolver resolver, string repoRoot, IAgentLlmClient? client, string profile, bool live, string runId, string caseId)
    {
        var agent = new QualityAgent(OverrideProfile(definitions, "QualityAgent", profile), resolver, repoRoot, client);
        return await agent.RunAsync(new AgentRequest
        {
            AgentName = "QualityAgent",
            GoalId = "live-remaining-governed-agents-161",
            DogfoodRunId = $"{runId}-quality-{caseId}",
            Inputs = new Dictionary<string, string>
            {
                ["plan_path"] = "tools/dogfood/test-agent-plans/irondev-thought-ledger-132.json",
                ["live_llm"] = live.ToString()
            }
        });
    }

    private static async Task<AgentResult> RunSupervisorAsync(IReadOnlyList<AgentDefinition> definitions, AgentModelResolver resolver, string repoRoot, IAgentLlmClient? client, string profile, bool live, string runId, string caseId)
    {
        var agent = new SupervisorAgent(OverrideProfile(definitions, "SupervisorAgent", profile), resolver, repoRoot, client);
        return await agent.RunAsync(new AgentRequest
        {
            AgentName = "SupervisorAgent",
            GoalId = "live-remaining-governed-agents-161",
            DogfoodRunId = $"{runId}-supervisor-{caseId}",
            Inputs = new Dictionary<string, string>
            {
                ["project"] = "IronDev",
                ["query"] = "THOUGHT_LEDGER_132",
                ["plan_path"] = "tools/dogfood/test-agent-plans/irondev-thought-ledger-132.json",
                ["live_llm"] = live.ToString()
            }
        });
    }

    private static object[] Evidence(string runRoot) =>
    [
        new { type = "ResearchPackage", path = Path.Combine(runRoot, "research-fallback-package.json"), summary = "Deterministic ResearchAgent package." },
        new { type = "ResearchPackage", path = Path.Combine(runRoot, "research-live-local-package.json"), summary = "Opt-in local live model ResearchAgent attempt." },
        new { type = "QualityReport", path = Path.Combine(runRoot, "quality-fallback-report.json"), summary = "Deterministic QualityAgent report." },
        new { type = "QualityReport", path = Path.Combine(runRoot, "quality-live-local-report.json"), summary = "Opt-in local live model QualityAgent attempt." },
        new { type = "SupervisorLoop", path = Path.Combine(runRoot, "supervisor-fallback-loop.json"), summary = "Deterministic SupervisorAgent loop." },
        new { type = "SupervisorLoop", path = Path.Combine(runRoot, "supervisor-live-local-loop.json"), summary = "Opt-in local live model SupervisorAgent attempt." },
        new { type = "CampaignReport", path = Path.Combine(runRoot, "report.json"), summary = "Live remaining governed agents campaign report." }
    ];

    private static bool AllSucceeded(params AgentResult[] results) =>
        results.All(result => result.Status == AgentRunStatus.Succeeded);

    private static AgentDefinition OverrideProfile(IReadOnlyList<AgentDefinition> definitions, string agentName, string modelProfile)
    {
        var source = definitions.Single(definition => string.Equals(definition.Name, agentName, StringComparison.OrdinalIgnoreCase));
        return new AgentDefinition { Name = source.Name, Purpose = source.Purpose, DefaultModelProfile = modelProfile, Enabled = source.Enabled, AllowedTools = source.AllowedTools };
    }

    private static bool StringEquals(JsonElement element, string propertyName, string expected) =>
        element.TryGetProperty(propertyName, out var property) &&
        string.Equals(property.GetString(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool StringEquals(JsonElement element, string first, string second, string expected) =>
        element.TryGetProperty(first, out var firstElement) &&
        firstElement.TryGetProperty(second, out var secondElement) &&
        string.Equals(secondElement.GetString(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool ReadBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;

    private static string ReadString(string json, string propertyName, string fallback)
    {
        using var document = JsonDocument.Parse(json);
        return ReadString(document.RootElement, propertyName, fallback);
    }

    private static string ReadString(JsonElement element, string propertyName, string fallback) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;

    private static IReadOnlyList<ModelProfile> LoadProfiles(string repoRoot)
    {
        var configPath = Path.Combine(repoRoot, "IronDev.Api", "appsettings.Development.json");
        if (!File.Exists(configPath))
            return AgentModelDefaults.CreateDefaultProfiles();

        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        if (!document.RootElement.TryGetProperty("ModelProfiles", out var profilesElement))
            return AgentModelDefaults.CreateDefaultProfiles();

        return profilesElement.EnumerateObject()
            .Select(item =>
            {
                var profile = item.Value;
                return new ModelProfile
                {
                    Name = item.Name,
                    Provider = ReadString(profile, "Provider", "OpenAI"),
                    Model = ReadString(profile, "Model", string.Empty),
                    BaseUrl = profile.TryGetProperty("BaseUrl", out var baseUrl) ? baseUrl.GetString() : null,
                    ApiKeyEnvironmentVariable = profile.TryGetProperty("ApiKeyEnvironmentVariable", out var apiKey) ? apiKey.GetString() : null,
                    TimeoutSeconds = profile.TryGetProperty("TimeoutSeconds", out var timeout) && timeout.TryGetInt32(out var seconds) ? seconds : 60
                };
            })
            .ToArray();
    }

    private static string? ReadOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;
            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
