using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Infrastructure.Services.Agents;

public static class LiveGovernedAgentExecution158Command
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var runId = ReadOption(args, "--run-id") ?? ReadOption(args, "--dogfood-run-id") ?? $"LiveGovernedAgent158-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var repoRoot = FindRepositoryRoot();
        var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", runId);
        Directory.CreateDirectory(runRoot);

        var profiles = LoadProfiles(repoRoot);
        var resolver = new AgentModelResolver(profiles);
        var definitions = AgentModelDefaults.CreateDefaultDefinitions();

        var fallbackReview = await RunArchitectReviewAsync(
            definitions,
            resolver,
            null,
            "strong-reasoner",
            liveLlm: false,
            weightedContext: "Accepted project memory: governed autonomy, no real repo writes, disposable workspace boundary.",
            runId,
            "fallback");

        var liveReview = await RunArchitectReviewAsync(
            definitions,
            resolver,
            new AgentLlmClient(),
            "local-cheap-runner",
            liveLlm: true,
            weightedContext: "Accepted project memory: governed autonomy, no real repo writes, disposable workspace boundary.",
            runId,
            "live-local");

        var missingEvidenceReview = await RunArchitectReviewAsync(
            definitions,
            resolver,
            null,
            "strong-reasoner",
            liveLlm: false,
            weightedContext: string.Empty,
            runId,
            "missing-evidence");

        var fallbackJson = JsonSerializer.Deserialize<JsonElement>(fallbackReview.OutputJson);
        var liveJson = JsonSerializer.Deserialize<JsonElement>(liveReview.OutputJson);
        var missingEvidenceJson = JsonSerializer.Deserialize<JsonElement>(missingEvidenceReview.OutputJson);
        var liveLlm = liveJson.GetProperty("llmIntelligence");

        var passed =
            fallbackReview.Status == AgentRunStatus.Succeeded &&
            liveReview.Status == AgentRunStatus.Succeeded &&
            missingEvidenceReview.Status == AgentRunStatus.Succeeded &&
            StringEquals(fallbackJson, "decision", "AllowPlanningOnly") &&
            StringEquals(missingEvidenceJson, "decision", "NeedsMoreEvidence") &&
            liveLlm.TryGetProperty("wasAttempted", out var wasAttempted) &&
            wasAttempted.ValueKind == JsonValueKind.True;

        var report = new
        {
            command = "campaign live-governed-agent-158",
            status = passed ? "Succeeded" : "Failed",
            runId,
            traceId = Guid.NewGuid().ToString("N"),
            project = "IronDev",
            summary = passed
                ? "Live governed agent execution smoke passed."
                : "Live governed agent execution smoke found missing evidence.",
            governedAgent = "ArchitectAgent",
            fallbackReview = fallbackJson,
            liveReview = liveJson,
            missingEvidenceReview = missingEvidenceJson,
            liveProviderHandling = new
            {
                profile = "local-cheap-runner",
                attempted = liveLlm.GetProperty("wasAttempted").GetBoolean(),
                successful = liveLlm.GetProperty("wasSuccessful").GetBoolean(),
                invocationMode = liveLlm.GetProperty("invocationMode").GetString(),
                boundary = "Live model execution is opt-in and failure falls back to deterministic review without granting mutation authority."
            },
            governance = new
            {
                conscienceRequired = true,
                thoughtLedgerRequired = true,
                realRepoWritesBlocked = true,
                memoryMutationBlocked = true,
                ticketCreationBlocked = true
            },
            evidence = new[]
            {
                new { type = "ArchitectureReview", path = Path.Combine(runRoot, "architect-fallback-review.json"), summary = "Deterministic fallback review." },
                new { type = "ArchitectureReview", path = Path.Combine(runRoot, "architect-live-local-review.json"), summary = "Opt-in local live model review attempt with fallback handling." },
                new { type = "ArchitectureReview", path = Path.Combine(runRoot, "architect-missing-evidence-review.json"), summary = "Missing evidence review." },
                new { type = "CampaignReport", path = Path.Combine(runRoot, "report.json"), summary = "Live governed agent execution report." }
            },
            boundary = "158 proves opt-in live governed agent execution and fallback handling. It does not grant writes, memory mutation, ticket creation, self-approval, or ungated autonomy.",
            reproCommand = $"dotnet run --project .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -- campaign live-governed-agent-158 --run-id {runId} --json"
        };

        await File.WriteAllTextAsync(Path.Combine(runRoot, "architect-fallback-review.json"), fallbackReview.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "architect-live-local-review.json"), liveReview.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "architect-missing-evidence-review.json"), missingEvidenceReview.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.json"), JsonSerializer.Serialize(report, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.md"), BuildMarkdownReport(report.summary, liveLlm));

        Console.WriteLine(JsonSerializer.Serialize(report, options));
        return passed ? 0 : 1;
    }

    private static async Task<AgentResult> RunArchitectReviewAsync(
        IReadOnlyList<AgentDefinition> definitions,
        AgentModelResolver resolver,
        IAgentLlmClient? llmClient,
        string modelProfile,
        bool liveLlm,
        string weightedContext,
        string runId,
        string caseId)
    {
        var source = definitions.Single(definition => definition.Name == "ArchitectAgent");
        var definition = new AgentDefinition
        {
            Name = source.Name,
            Purpose = source.Purpose,
            DefaultModelProfile = modelProfile,
            Enabled = source.Enabled,
            AllowedTools = source.AllowedTools
        };
        var agent = new ArchitectAgent(definition, resolver, llmClient);
        return await agent.RunAsync(new AgentRequest
        {
            AgentName = "ArchitectAgent",
            GoalId = "live-governed-agent-execution-158",
            DogfoodRunId = $"{runId}-{caseId}",
            Inputs = new Dictionary<string, string>
            {
                ["project"] = "IronDev",
                ["proposal"] = "Let ArchitectAgent perform a governed architecture review using live model execution when explicitly enabled.",
                ["weighted_context"] = weightedContext,
                ["safety_boundary"] = "No real repository writes; no memory mutation; no ticket creation; ConscienceAgent and ThoughtLedger remain required.",
                ["live_llm"] = liveLlm.ToString()
            }
        });
    }

    private static string BuildMarkdownReport(string summary, JsonElement liveLlm) =>
        $"""
        # Live Governed Agent Execution 158

        {summary}

        Live profile: local-cheap-runner
        Invocation mode: {liveLlm.GetProperty("invocationMode").GetString()}
        Attempted: {liveLlm.GetProperty("wasAttempted").GetBoolean()}
        Successful: {liveLlm.GetProperty("wasSuccessful").GetBoolean()}

        Boundary: opt-in live model execution only. No writes, memory mutation, ticket creation, self-approval, or ungated autonomy.
        """;

    private static bool StringEquals(JsonElement element, string propertyName, string expected) =>
        element.TryGetProperty(propertyName, out var property) &&
        string.Equals(property.GetString(), expected, StringComparison.OrdinalIgnoreCase);

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

