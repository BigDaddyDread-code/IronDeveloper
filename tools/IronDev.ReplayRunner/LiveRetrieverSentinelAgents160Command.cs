using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Infrastructure.Services.Agents;

public static class LiveRetrieverSentinelAgents160Command
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var runId = ReadOption(args, "--run-id") ?? ReadOption(args, "--dogfood-run-id") ?? $"LiveRetrieverSentinel160-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var repoRoot = FindRepositoryRoot();
        var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", runId);
        Directory.CreateDirectory(runRoot);

        var profiles = LoadProfiles(repoRoot);
        var resolver = new AgentModelResolver(profiles);
        var definitions = AgentModelDefaults.CreateDefaultDefinitions();

        var retrieverFallback = await RunRetrieverAsync(definitions, resolver, null, repoRoot, "cheap-runner", false, runId, "fallback");
        var retrieverLive = await RunRetrieverAsync(definitions, resolver, new AgentLlmClient(), repoRoot, "local-cheap-runner", true, runId, "live-local");
        var sentinelFallback = await RunSentinelAsync(definitions, resolver, null, "cheap-runner", false, runId, "fallback");
        var sentinelLive = await RunSentinelAsync(definitions, resolver, new AgentLlmClient(), "local-cheap-runner", true, runId, "live-local");

        var retrieverFallbackJson = JsonSerializer.Deserialize<JsonElement>(retrieverFallback.OutputJson);
        var retrieverLiveJson = JsonSerializer.Deserialize<JsonElement>(retrieverLive.OutputJson);
        var sentinelFallbackJson = JsonSerializer.Deserialize<JsonElement>(sentinelFallback.OutputJson);
        var sentinelLiveJson = JsonSerializer.Deserialize<JsonElement>(sentinelLive.OutputJson);
        var retrieverLiveLlm = retrieverLiveJson.GetProperty("LlmIntelligence");
        var sentinelLiveLlm = sentinelLiveJson.GetProperty("llmIntelligence");

        var passed =
            retrieverFallback.Status == AgentRunStatus.Succeeded &&
            retrieverLive.Status == AgentRunStatus.Succeeded &&
            sentinelFallback.Status == AgentRunStatus.Succeeded &&
            sentinelLive.Status == AgentRunStatus.Succeeded &&
            ReadBool(retrieverLiveLlm, "wasAttempted") &&
            ReadBool(sentinelLiveLlm, "wasAttempted") &&
            retrieverFallbackJson.TryGetProperty("WeightedContextBundle", out var bundle) &&
            bundle.TryGetProperty("includedSources", out var includedSources) &&
            includedSources.ValueKind == JsonValueKind.Array &&
            includedSources.GetArrayLength() > 0 &&
            StringEquals(sentinelFallbackJson, "insightType", "RoutingWeakness") &&
            StringEquals(sentinelFallbackJson, "observedProject", "BookSeller") &&
            StringEquals(sentinelFallbackJson, "affectedProject", "IronDev");

        var report = new
        {
            command = "campaign live-retriever-sentinel-160",
            status = passed ? "Succeeded" : "Failed",
            runId,
            traceId = Guid.NewGuid().ToString("N"),
            project = "IronDev",
            summary = passed
                ? "Live RetrieverAgent and SentinelAgent campaign smoke passed."
                : "Live RetrieverAgent and SentinelAgent campaign smoke found missing evidence.",
            governedAgents = new[] { "RetrieverAgent", "SentinelAgent" },
            retrieverFallbackContext = retrieverFallbackJson,
            retrieverLiveContext = retrieverLiveJson,
            sentinelFallbackInsight = sentinelFallbackJson,
            sentinelLiveInsight = sentinelLiveJson,
            liveProviderHandling = new
            {
                profile = "local-cheap-runner",
                retrieverAttempted = ReadBool(retrieverLiveLlm, "wasAttempted"),
                retrieverSuccessful = ReadBool(retrieverLiveLlm, "wasSuccessful"),
                retrieverInvocationMode = ReadString(retrieverLiveLlm, "invocationMode", string.Empty),
                sentinelAttempted = ReadBool(sentinelLiveLlm, "wasAttempted"),
                sentinelSuccessful = ReadBool(sentinelLiveLlm, "wasSuccessful"),
                sentinelInvocationMode = ReadString(sentinelLiveLlm, "invocationMode", string.Empty),
                boundary = "Live model execution is opt-in and failure falls back to deterministic context packaging/insight classification without granting mutation authority."
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
                rankingOverrideBlocked = true
            },
            evidence = new[]
            {
                new { type = "RetrieverContext", path = Path.Combine(runRoot, "retriever-fallback-context.json"), summary = "Deterministic RetrieverAgent weighted context bundle." },
                new { type = "RetrieverContext", path = Path.Combine(runRoot, "retriever-live-local-context.json"), summary = "Opt-in local live model RetrieverAgent attempt with fallback handling." },
                new { type = "SentinelInsight", path = Path.Combine(runRoot, "sentinel-fallback-insight.json"), summary = "Deterministic SentinelAgent insight." },
                new { type = "SentinelInsight", path = Path.Combine(runRoot, "sentinel-live-local-insight.json"), summary = "Opt-in local live model SentinelAgent attempt with fallback handling." },
                new { type = "CampaignReport", path = Path.Combine(runRoot, "report.json"), summary = "Live Retriever/Sentinel campaign report." }
            },
            boundary = "160 proves opt-in live RetrieverAgent and SentinelAgent evidence paths. It does not grant writes, memory mutation, ticket creation, patch apply, ranking override, self-approval, or ungated autonomy.",
            reproCommand = $"dotnet run --project .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -- campaign live-retriever-sentinel-160 --run-id {runId} --json"
        };

        await File.WriteAllTextAsync(Path.Combine(runRoot, "retriever-fallback-context.json"), retrieverFallback.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "retriever-live-local-context.json"), retrieverLive.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "sentinel-fallback-insight.json"), sentinelFallback.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "sentinel-live-local-insight.json"), sentinelLive.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.json"), JsonSerializer.Serialize(report, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.md"), BuildMarkdownReport(report.summary, retrieverLiveLlm, sentinelLiveLlm));

        Console.WriteLine(JsonSerializer.Serialize(report, options));
        return passed ? 0 : 1;
    }

    private static async Task<AgentResult> RunRetrieverAsync(
        IReadOnlyList<AgentDefinition> definitions,
        AgentModelResolver resolver,
        IAgentLlmClient? llmClient,
        string repoRoot,
        string modelProfile,
        bool liveLlm,
        string runId,
        string caseId)
    {
        var definition = OverrideProfile(definitions, "RetrieverAgent", modelProfile);
        var agent = new RetrieverAgent(definition, resolver, repoRoot, llmClient);
        return await agent.RunAsync(new AgentRequest
        {
            AgentName = "RetrieverAgent",
            GoalId = "live-retriever-sentinel-agents-160",
            DogfoodRunId = $"{runId}-retriever-{caseId}",
            Inputs = new Dictionary<string, string>
            {
                ["project"] = "IronDev",
                ["query"] = "LIVE_CRITIC_PLANNER_AGENTS_159",
                ["take"] = "5",
                ["live_llm"] = liveLlm.ToString()
            }
        });
    }

    private static async Task<AgentResult> RunSentinelAsync(
        IReadOnlyList<AgentDefinition> definitions,
        AgentModelResolver resolver,
        IAgentLlmClient? llmClient,
        string modelProfile,
        bool liveLlm,
        string runId,
        string caseId)
    {
        var definition = OverrideProfile(definitions, "SentinelAgent", modelProfile);
        var agent = new SentinelAgent(definition, resolver, llmClient);
        return await agent.RunAsync(new AgentRequest
        {
            AgentName = "SentinelAgent",
            GoalId = "live-retriever-sentinel-agents-160",
            DogfoodRunId = $"{runId}-sentinel-{caseId}",
            Inputs = new Dictionary<string, string>
            {
                ["observed_project"] = "BookSeller",
                ["affected_project"] = "IronDev",
                ["finding_type"] = "RoutingWeakness",
                ["evidence"] = "Expected SaveDiscussionDocument, Actual GeneralChat during BookSeller project knowledge campaign.",
                ["live_llm"] = liveLlm.ToString()
            }
        });
    }

    private static AgentDefinition OverrideProfile(IReadOnlyList<AgentDefinition> definitions, string agentName, string modelProfile)
    {
        var source = definitions.Single(definition => string.Equals(definition.Name, agentName, StringComparison.OrdinalIgnoreCase));
        return new AgentDefinition
        {
            Name = source.Name,
            Purpose = source.Purpose,
            DefaultModelProfile = modelProfile,
            Enabled = source.Enabled,
            AllowedTools = source.AllowedTools
        };
    }

    private static string BuildMarkdownReport(string summary, JsonElement retrieverLiveLlm, JsonElement sentinelLiveLlm) =>
        $"""
        # Live Retriever And Sentinel Agents 160

        {summary}

        Retriever live profile: local-cheap-runner
        Retriever invocation mode: {ReadString(retrieverLiveLlm, "invocationMode", string.Empty)}
        Retriever attempted: {ReadBool(retrieverLiveLlm, "wasAttempted")}
        Retriever successful: {ReadBool(retrieverLiveLlm, "wasSuccessful")}

        Sentinel live profile: local-cheap-runner
        Sentinel invocation mode: {ReadString(sentinelLiveLlm, "invocationMode", string.Empty)}
        Sentinel attempted: {ReadBool(sentinelLiveLlm, "wasAttempted")}
        Sentinel successful: {ReadBool(sentinelLiveLlm, "wasSuccessful")}

        Boundary: opt-in live model execution only. No writes, memory mutation, ticket creation, patch apply, ranking override, self-approval, or ungated autonomy.
        """;

    private static bool StringEquals(JsonElement element, string propertyName, string expected) =>
        element.TryGetProperty(propertyName, out var property) &&
        string.Equals(property.GetString(), expected, StringComparison.OrdinalIgnoreCase);

    private static bool ReadBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<ModelProfile> LoadProfiles(string repoRoot)
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
                continue;

            var profiles = profilesElement.EnumerateObject()
                .Select(profileProperty =>
                {
                    var profile = profileProperty.Value;
                    return new ModelProfile
                    {
                        Name = profileProperty.Name,
                        Provider = ReadString(profile, "Provider", "OpenAI"),
                        Model = ReadString(profile, "Model", string.Empty),
                        BaseUrl = ReadNullableString(profile, "BaseUrl"),
                        ApiKeyEnvironmentVariable = ReadNullableString(profile, "ApiKeyEnvironmentVariable"),
                        Temperature = ReadDouble(profile, "Temperature", 0.2),
                        MaxOutputTokens = ReadInt(profile, "MaxOutputTokens", 2000),
                        TimeoutSeconds = ReadInt(profile, "TimeoutSeconds", 60)
                    };
                })
                .ToArray();

            if (profiles.Length > 0)
                return profiles;
        }

        return AgentModelDefaults.CreateDefaultProfiles();
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

    private static string ReadString(JsonElement element, string propertyName, string fallback) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;

    private static string? ReadNullableString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static double ReadDouble(JsonElement element, string propertyName, double fallback) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetDouble(out var value)
            ? value
            : fallback;

    private static int ReadInt(JsonElement element, string propertyName, int fallback) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value)
            ? value
            : fallback;
}
