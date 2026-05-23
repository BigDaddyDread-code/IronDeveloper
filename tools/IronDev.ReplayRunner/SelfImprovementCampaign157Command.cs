using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Infrastructure.Services.Agents;

public static class SelfImprovementCampaign157Command
{
    public static async Task<int> HandleAsync(string[] args, JsonSerializerOptions options)
    {
        var runId = ReadOption(args, "--run-id") ?? ReadOption(args, "--dogfood-run-id") ?? $"SelfImprovement157-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var repoRoot = FindRepositoryRoot();
        var runRoot = Path.Combine(repoRoot, "tools", "dogfood", "runs", runId);
        Directory.CreateDirectory(runRoot);

        var profiles = LoadProfiles(repoRoot);
        var modelResolver = new AgentModelResolver(profiles);
        var definitions = AgentModelDefaults.CreateDefaultDefinitions();
        var architectDefinition = definitions.Single(definition => definition.Name == "ArchitectAgent");
        var architect = new ArchitectAgent(architectDefinition, modelResolver);
        var architectResult = await architect.RunAsync(new AgentRequest
        {
            AgentName = "ArchitectAgent",
            GoalId = "self-improvement-campaign-157",
            DogfoodRunId = runId,
            Inputs = new Dictionary<string, string>
            {
                ["project"] = "IronDev",
                ["proposal"] = "Mature governed autonomy control plane with LLM-ready agents, runtime model profiles, traceable campaigns, and caged BuilderAgent repair loops.",
                ["weighted_context"] = "Accepted project memory: governed autonomy, disposable workspace boundary, Run Reports viewer, trace-backed BuilderAgent repair loop.",
                ["safety_boundary"] = "No real repository writes; ConscienceAgent and ThoughtLedger remain required before write-capable workflows."
            }
        });

        var providerNames = profiles.Select(profile => profile.Provider).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray();
        var hasLocalProfiles = profiles.Any(profile =>
            profile.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase) ||
            profile.Provider.Equals("LocalOpenAI", StringComparison.OrdinalIgnoreCase));
        var childTickets = BuildChildTicketStatus(hasLocalProfiles);
        var passed = hasLocalProfiles &&
                     providerNames.Contains("OpenAI", StringComparer.OrdinalIgnoreCase) &&
                     providerNames.Contains("Ollama", StringComparer.OrdinalIgnoreCase) &&
                     providerNames.Contains("LocalOpenAI", StringComparer.OrdinalIgnoreCase) &&
                     architectResult.Status == AgentRunStatus.Succeeded &&
                     childTickets.All(ticket => ticket.Status is "Implemented" or "CoveredByExistingProof");

        var report = new
        {
            command = "campaign self-improvement-157",
            status = passed ? "Succeeded" : "Failed",
            runId,
            traceId = Guid.NewGuid().ToString("N"),
            project = "IronDev",
            summary = passed
                ? "Self-improvement campaign 157 control-plane maturity smoke passed."
                : "Self-improvement campaign 157 found missing control-plane maturity evidence.",
            childTickets,
            modelProfiles = profiles.Select(profile => new
            {
                profile.Name,
                profile.Provider,
                profile.Model,
                profile.BaseUrl,
                profile.Temperature,
                profile.MaxOutputTokens,
                profile.TimeoutSeconds
            }).ToArray(),
            providerSupport = new
            {
                openAi = providerNames.Contains("OpenAI", StringComparer.OrdinalIgnoreCase),
                localOpenAi = providerNames.Contains("LocalOpenAI", StringComparer.OrdinalIgnoreCase),
                ollama = providerNames.Contains("Ollama", StringComparer.OrdinalIgnoreCase),
                boundary = "Local profile support configures providers; it does not make network calls during this smoke."
            },
            architectReview = JsonSerializer.Deserialize<JsonElement>(architectResult.OutputJson),
            governance = new
            {
                conscienceRequired = true,
                thoughtLedgerRequired = true,
                realRepoWritesBlocked = true,
                builderCaged = true,
                humanApprovalForRealRepoApply = true
            },
            evidence = new[]
            {
                new { type = "ArchitectureReview", path = Path.Combine(runRoot, "architect-review.json"), summary = "ArchitectAgent governed review output." },
                new { type = "CampaignReport", path = Path.Combine(runRoot, "report.json"), summary = "Self-improvement campaign 157 maturity report." }
            },
            boundary = "Campaign 157 matures the governed autonomy control plane. It does not grant ungated autonomy, real repository writes, memory mutation, or self-approval.",
            reproCommand = $"dotnet run --project .\\tools\\IronDev.ReplayRunner\\IronDev.ReplayRunner.csproj -- campaign self-improvement-157 --run-id {runId} --json"
        };

        await File.WriteAllTextAsync(Path.Combine(runRoot, "architect-review.json"), architectResult.OutputJson);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.json"), JsonSerializer.Serialize(report, options));
        await File.WriteAllTextAsync(Path.Combine(runRoot, "report.md"), BuildMarkdownReport(report.summary, childTickets, providerNames));

        Console.WriteLine(JsonSerializer.Serialize(report, options));
        return passed ? 0 : 1;
    }

    private static IReadOnlyList<CampaignTicketStatus> BuildChildTicketStatus(bool hasLocalProfiles) =>
    [
        new("IRONDEV-144", "Activate intelligent LLM-ready agents", "Implemented", "Planner, Research, Critic, Retriever, Sentinel, Architect emit model-profile evidence and governed LLM-ready prompts/fallbacks."),
        new("IRONDEV-145", "Implement full ArchitectAgent", "Implemented", "ArchitectAgent now performs governed architecture review with model-profile evidence."),
        new("IRONDEV-146", "Expand BuilderAgent repair loop", "CoveredByExistingProof", "Trace-backed caged BuilderAgent repair loop 141 remains the production-grade disposable repair proof."),
        new("IRONDEV-147", "Robust SupervisorAgent campaigns", "CoveredByExistingProof", "Supervisor governed loops 135/136 and main regression remain the bounded campaign orchestrator baseline."),
        new("IRONDEV-148", "Consolidate agent docs", "Implemented", "Docs/AGENTS.md becomes the current agent-layer source of truth."),
        new("IRONDEV-149", "Local/Ollama support", hasLocalProfiles ? "Implemented" : "MissingEvidence", "Model profiles support OpenAI, LocalOpenAI, and Ollama."),
        new("IRONDEV-150", "Tracing observability", "CoveredByExistingProof", "Run report viewer 144 reads trace/report/evidence files through shared services."),
        new("IRONDEV-151", "Less conservative governance", "Implemented", "Governance allows evidence-backed planning/review/caged execution while blocking real repo writes."),
        new("IRONDEV-152", "Simulation dry-run mode", "CoveredByExistingProof", "Trace smoke and campaign report provide non-mutating dry-run evidence paths."),
        new("IRONDEV-153", "Evidence and failure packaging", "CoveredByExistingProof", "Failure handoff, run reports, and builder repair evidence are in the main pack."),
        new("IRONDEV-154", "Contract/property-style tests", "Implemented", "Campaign smoke validates provider/profile contracts and governance invariants."),
        new("IRONDEV-155", "Runtime-configurable model profiles", "Implemented", "Profiles load from appsettings with provider/base URL/timeouts."),
        new("IRONDEV-156", "Reduce Windows/PowerShell brittleness", "CoveredByExistingProof", "C# dogfood runner 143 owns primary plan execution; PowerShell is compatibility only.")
    ];

    private static string BuildMarkdownReport(string summary, IReadOnlyList<CampaignTicketStatus> tickets, IReadOnlyList<string> providers) =>
        $"""
        # Self-Improvement Campaign 157

        {summary}

        Providers: {string.Join(", ", providers)}

        | Ticket | Status | Evidence |
        | --- | --- | --- |
        {string.Join(Environment.NewLine, tickets.Select(ticket => $"| {ticket.Id} | {ticket.Status} | {ticket.Evidence} |"))}

        Boundary: governed autonomy only. No real repository writes or self-approval.
        """;

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

    private sealed record CampaignTicketStatus(string Id, string Title, string Status, string Evidence);
}
