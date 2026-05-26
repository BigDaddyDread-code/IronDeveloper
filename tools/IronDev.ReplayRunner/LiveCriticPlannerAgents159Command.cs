using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Infrastructure.Services.Agents;

public static class LiveCriticPlannerAgents159Command
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var runId = ReadOption(args, "--run-id") ?? ReadOption(args, "--dogfood-run-id") ?? $"LiveCriticPlanner159-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var repoRoot = FindRepositoryRoot();
        var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", runId);
        Directory.CreateDirectory(runRoot);

        var profiles = LoadProfiles(repoRoot);
        var resolver = new AgentModelResolver(profiles);
        var definitions = AgentModelDefaults.CreateDefaultDefinitions();
        var failurePackagePath = Path.Combine(runRoot, "synthetic-failure-package.json");
        await File.WriteAllTextAsync(failurePackagePath, JsonSerializer.Serialize(BuildSyntheticFailurePackage(runId), options));

        var criticFallback = await RunCriticAsync(definitions, resolver, null, "strong-reviewer", false, failurePackagePath, runId, "fallback");
        var criticLive = await RunCriticAsync(definitions, resolver, new AgentLlmClient(), "local-cheap-runner", true, failurePackagePath, runId, "live-local");
        var plannerFallback = await RunPlannerIntakeAsync(definitions, resolver, null, "standard-reasoner", false, runId, "fallback");
        var plannerLive = await RunPlannerIntakeAsync(definitions, resolver, new AgentLlmClient(), "local-cheap-runner", true, runId, "live-local");

        var criticFallbackJson = JsonSerializer.Deserialize<JsonElement>(criticFallback.OutputJson);
        var criticLiveJson = JsonSerializer.Deserialize<JsonElement>(criticLive.OutputJson);
        var plannerFallbackJson = JsonSerializer.Deserialize<JsonElement>(plannerFallback.OutputJson);
        var plannerLiveJson = JsonSerializer.Deserialize<JsonElement>(plannerLive.OutputJson);
        var criticLiveLlm = criticLiveJson.GetProperty("llmIntelligence");
        var plannerLiveLlm = plannerLiveJson.GetProperty("planner").GetProperty("llmIntelligence");

        var passed =
            criticFallback.Status == AgentRunStatus.Succeeded &&
            criticLive.Status == AgentRunStatus.Succeeded &&
            plannerFallback.Status == AgentRunStatus.Succeeded &&
            plannerLive.Status == AgentRunStatus.Succeeded &&
            ReadBool(criticLiveLlm, "wasAttempted") &&
            ReadBool(plannerLiveLlm, "wasAttempted") &&
            StringEquals(criticFallbackJson, "recommendation", "fix_with_smallest_evidence_backed_patch") &&
            StringEquals(plannerFallbackJson, "intakeKind", "ProductSpikeCandidate") &&
            StringEquals(plannerFallbackJson, "detectedProject", "Solitaire");

        var report = new
        {
            command = "campaign live-critic-planner-159",
            status = passed ? "Succeeded" : "Failed",
            runId,
            traceId = Guid.NewGuid().ToString("N"),
            project = "IronDev",
            summary = passed
                ? "Live CriticAgent and PlannerAgent campaign smoke passed."
                : "Live CriticAgent and PlannerAgent campaign smoke found missing evidence.",
            governedAgents = new[] { "CriticAgent", "PlannerAgent" },
            criticFallbackReview = criticFallbackJson,
            criticLiveReview = criticLiveJson,
            plannerFallbackIntake = plannerFallbackJson,
            plannerLiveIntake = plannerLiveJson,
            liveProviderHandling = new
            {
                profile = "local-cheap-runner",
                criticAttempted = ReadBool(criticLiveLlm, "wasAttempted"),
                criticSuccessful = ReadBool(criticLiveLlm, "wasSuccessful"),
                criticInvocationMode = ReadString(criticLiveLlm, "invocationMode", string.Empty),
                plannerAttempted = ReadBool(plannerLiveLlm, "wasAttempted"),
                plannerSuccessful = ReadBool(plannerLiveLlm, "wasSuccessful"),
                plannerInvocationMode = ReadString(plannerLiveLlm, "invocationMode", string.Empty),
                boundary = "Live model execution is opt-in and failure falls back to deterministic review/planning without granting mutation authority."
            },
            governance = new
            {
                conscienceRequired = true,
                thoughtLedgerRequired = true,
                realRepoWritesBlocked = true,
                memoryMutationBlocked = true,
                ticketCreationBlocked = true,
                patchApplyBlocked = true,
                selfApprovalBlocked = true
            },
            evidence = new[]
            {
                new { type = "FailurePackage", path = failurePackagePath, summary = "Synthetic failure package for CriticAgent review." },
                new { type = "CriticReview", path = Path.Combine(runRoot, "critic-fallback-review.json"), summary = "Deterministic CriticAgent fallback review." },
                new { type = "CriticReview", path = Path.Combine(runRoot, "critic-live-local-review.json"), summary = "Opt-in local live model CriticAgent attempt with fallback handling." },
                new { type = "PlannerIntake", path = Path.Combine(runRoot, "planner-fallback-intake.json"), summary = "Deterministic PlannerAgent product-spike intake." },
                new { type = "PlannerIntake", path = Path.Combine(runRoot, "planner-live-local-intake.json"), summary = "Opt-in local live model PlannerAgent attempt with fallback handling." },
                new { type = "CampaignReport", path = Path.Combine(runRoot, "report.json"), summary = "Live Critic/Planner campaign report." }
            },
            boundary = "159 proves opt-in live CriticAgent and PlannerAgent evidence paths. It does not grant writes, memory mutation, ticket creation, patch apply, self-approval, or ungated autonomy.",
            reproCommand = $"dotnet run --project .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -- campaign live-critic-planner-159 --run-id {runId} --json"
        };

        await File.WriteAllTextAsync(Path.Combine(runRoot, "critic-fallback-review.json"), criticFallback.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "critic-live-local-review.json"), criticLive.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "planner-fallback-intake.json"), plannerFallback.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "planner-live-local-intake.json"), plannerLive.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.json"), JsonSerializer.Serialize(report, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.md"), BuildMarkdownReport(report.summary, criticLiveLlm, plannerLiveLlm));

        Console.WriteLine(JsonSerializer.Serialize(report, options));
        return passed ? 0 : 1;
    }

    private static object BuildSyntheticFailurePackage(string runId) => new
    {
        DogfoodRunId = runId,
        ScenarioId = "live-critic-planner-159",
        GoalId = "live-critic-planner-agents-159",
        FailureReason = "Builder repair loop expected a test failure classification but received Unknown.",
        ExpectedJson = "{\"failureClassification\":\"RuleBug\"}",
        ActualJson = "{\"failureClassification\":\"Unknown\"}",
        ReproCommand = "test run-plan --plan tools/dogfood/test-agent-plans/irondev-builder-repair-loop-141.json --run-id repro --json",
        ValidationCommand = "test run-plan --plan tools/dogfood/test-agent-plans/irondev-builder-repair-loop-141.json --run-id validation --json",
        EvidencePaths = new[] { "tools/dogfood/runs/repro/report.json", "tools/dogfood/runs/repro/logs/step-001.log" },
        LikelyAreas = new[] { "BuilderRepairLoopCommand failure classification", "TestPlanRunnerCommand parsed output checks" },
        SafetyRules = new[] { "Do not weaken assertions.", "Do not patch outside disposable workspace.", "Do not mutate memory or create tickets." },
        Prompt = "Review builder repair loop failure package."
    };

    private static async Task<AgentResult> RunCriticAsync(
        IReadOnlyList<AgentDefinition> definitions,
        AgentModelResolver resolver,
        IAgentLlmClient? llmClient,
        string modelProfile,
        bool liveLlm,
        string failurePackagePath,
        string runId,
        string caseId)
    {
        var definition = OverrideProfile(definitions, "CriticAgent", modelProfile);
        var agent = new CriticAgent(definition, resolver, llmClient);
        return await agent.RunAsync(new AgentRequest
        {
            AgentName = "CriticAgent",
            GoalId = "live-critic-planner-agents-159",
            DogfoodRunId = $"{runId}-critic-{caseId}",
            Inputs = new Dictionary<string, string>
            {
                ["package_path"] = failurePackagePath,
                ["live_llm"] = liveLlm.ToString()
            }
        });
    }

    private static async Task<AgentResult> RunPlannerIntakeAsync(
        IReadOnlyList<AgentDefinition> definitions,
        AgentModelResolver resolver,
        IAgentLlmClient? llmClient,
        string modelProfile,
        bool liveLlm,
        string runId,
        string caseId)
    {
        var definition = OverrideProfile(definitions, "PlannerAgent", modelProfile);
        var agent = new PlannerAgent(definition, resolver, llmClient);
        return await agent.RunAsync(new AgentRequest
        {
            AgentName = "PlannerAgent",
            GoalId = "live-critic-planner-agents-159",
            DogfoodRunId = $"{runId}-planner-{caseId}",
            Inputs = new Dictionary<string, string>
            {
                ["mode"] = "product_spike_intake",
                ["prompt"] = "i want build solitare",
                ["project"] = string.Empty,
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

    private static string BuildMarkdownReport(string summary, JsonElement criticLiveLlm, JsonElement plannerLiveLlm) =>
        $"""
        # Live Critic And Planner Agents 159

        {summary}

        Critic live profile: local-cheap-runner
        Critic invocation mode: {ReadString(criticLiveLlm, "invocationMode", string.Empty)}
        Critic attempted: {ReadBool(criticLiveLlm, "wasAttempted")}
        Critic successful: {ReadBool(criticLiveLlm, "wasSuccessful")}

        Planner live profile: local-cheap-runner
        Planner invocation mode: {ReadString(plannerLiveLlm, "invocationMode", string.Empty)}
        Planner attempted: {ReadBool(plannerLiveLlm, "wasAttempted")}
        Planner successful: {ReadBool(plannerLiveLlm, "wasSuccessful")}

        Boundary: opt-in live model execution only. No writes, memory mutation, ticket creation, patch apply, self-approval, or ungated autonomy.
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
            Path.Combine(repoRoot, "IronDev.Api", "appsettings.Development.json"),
            Path.Combine(repoRoot, "IronDev.Api", "appsettings.json")
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
