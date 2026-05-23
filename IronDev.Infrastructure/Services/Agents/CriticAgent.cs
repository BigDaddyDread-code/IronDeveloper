using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class CriticAgent : StaticIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;
    private readonly IAgentLlmClient? _llmClient;

    public CriticAgent(AgentDefinition definition, IAgentModelResolver modelResolver, IAgentLlmClient? llmClient = null)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
        _llmClient = llmClient;
    }

    public override async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        var packagePath = RequireInput(request, "package_path");

        if (!File.Exists(packagePath))
            throw new FileNotFoundException("CriticAgent failure package not found.", packagePath);

        await using var stream = File.OpenRead(packagePath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var package = document.RootElement.Clone();

        var failureReason = ReadString(package, "FailureReason");
        var expectedJson = ReadString(package, "ExpectedJson");
        var actualJson = ReadString(package, "ActualJson");
        var reproCommand = ReadString(package, "ReproCommand");
        var validationCommand = ReadString(package, "ValidationCommand");
        var evidencePaths = ReadStringArray(package, "EvidencePaths");
        var likelyAreas = ReadStringArray(package, "LikelyAreas");
        var safetyRules = ReadStringArray(package, "SafetyRules");
        var prompt = BuildPrompt(failureReason, expectedJson, actualJson, reproCommand, validationCommand, likelyAreas, safetyRules);
        var liveLlmRequested = ReadBoolInput(request, "live_llm");
        var llmResult = await ResolveLlmResultAsync(profile, prompt, liveLlmRequested, request, ct);
        var evidenceSufficient =
            !string.IsNullOrWhiteSpace(failureReason) &&
            !string.IsNullOrWhiteSpace(expectedJson) &&
            !string.IsNullOrWhiteSpace(actualJson) &&
            evidencePaths.Count > 0 &&
            !string.IsNullOrWhiteSpace(reproCommand) &&
            !string.IsNullOrWhiteSpace(validationCommand);
        var actionable = evidenceSufficient && likelyAreas.Count > 0 && safetyRules.Count > 0;
        var recommendation = actionable
            ? "fix_with_smallest_evidence_backed_patch"
            : evidenceSufficient
                ? "ask_for_likely_area"
                : "request_more_evidence";

        var review = new
        {
            packagePath,
            dogfoodRunId = ReadString(package, "DogfoodRunId"),
            scenarioId = ReadString(package, "ScenarioId"),
            goalId = ReadString(package, "GoalId"),
            failureReason,
            expectedJsonPresent = !string.IsNullOrWhiteSpace(expectedJson),
            actualJsonPresent = !string.IsNullOrWhiteSpace(actualJson),
            evidenceSufficient,
            actionable,
            recommendation,
            likelyAreas,
            evidencePaths,
            safetyRules,
            llmIntelligence = new
            {
                modelProfile = profile.Name,
                profileProvider = profile.Provider,
                profileModel = profile.Model,
                prompt,
                invocationMode = llmResult.InvocationMode,
                liveLlmRequested,
                wasAttempted = llmResult.WasAttempted,
                wasSuccessful = llmResult.WasSuccessful,
                durationMs = llmResult.DurationMs,
                modelSummary = BuildModelSummary(llmResult),
                error = llmResult.WasSuccessful ? string.Empty : llmResult.ErrorMessage
            },
            risks = BuildRisks(package, evidenceSufficient, actionable),
            boundary = "CriticAgent reviews failure-package evidence only. Live LLM output is advisory evidence and does not patch code, run tests, create tickets, mutate memory, or approve writes."
        };

        return new AgentResult
        {
            AgentName = AgentName,
            Status = AgentRunStatus.Succeeded,
            Summary = actionable
                ? $"CriticAgent found actionable failure package for {review.goalId}."
                : $"CriticAgent needs more evidence for {review.goalId}.",
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = 0,
            OutputJson = JsonSerializer.Serialize(review, new JsonSerializerOptions { WriteIndented = true }),
            CommandsRun = [$"critic review-failure --package {QuoteIfNeeded(packagePath)}"],
            EvidencePaths = evidencePaths,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task<AgentLlmCallResult> ResolveLlmResultAsync(
        ModelProfile profile,
        string prompt,
        bool liveLlmRequested,
        AgentRequest request,
        CancellationToken ct)
    {
        if (request.Inputs.TryGetValue("llm_response", out var providedResponse) &&
            !string.IsNullOrWhiteSpace(providedResponse))
        {
            return new AgentLlmCallResult
            {
                WasAttempted = false,
                WasSuccessful = true,
                InvocationMode = "provided_llm_response",
                ResponseText = providedResponse
            };
        }

        if (!liveLlmRequested)
        {
            return new AgentLlmCallResult
            {
                WasAttempted = false,
                WasSuccessful = true,
                InvocationMode = "llm_ready_deterministic_fallback",
                ResponseText = "No live model response supplied; deterministic failure-package review was used for this governed smoke."
            };
        }

        if (_llmClient is null)
        {
            return new AgentLlmCallResult
            {
                WasAttempted = false,
                WasSuccessful = false,
                InvocationMode = "live_model_requested_without_client_fallback",
                ErrorMessage = "No governed agent LLM client was configured."
            };
        }

        return await _llmClient.CompleteAsync(profile, prompt, ct);
    }

    private static string BuildPrompt(
        string failureReason,
        string expectedJson,
        string actualJson,
        string reproCommand,
        string validationCommand,
        IReadOnlyList<string> likelyAreas,
        IReadOnlyList<string> safetyRules) =>
        $"""
        You are CriticAgent for IronDev/IDA.
        Review this failure package and return concise JSON with evidence gaps, likely risk areas, and next safe investigation steps.
        Failure reason: {failureReason}
        Expected JSON: {expectedJson}
        Actual JSON: {actualJson}
        Repro command: {reproCommand}
        Validation command: {validationCommand}
        Likely areas: {string.Join("; ", likelyAreas)}
        Safety rules: {string.Join("; ", safetyRules)}
        Do not suggest weakening assertions. Do not patch code, create tickets, mutate memory, or approve writes.
        """;

    private static bool ReadBoolInput(AgentRequest request, string key) =>
        request.Inputs.TryGetValue(key, out var value) &&
        bool.TryParse(value, out var parsed) &&
        parsed;

    private static string BuildModelSummary(AgentLlmCallResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ResponseText))
            return result.ResponseText;

        return result.WasAttempted
            ? "Live model call did not return usable content; deterministic failure-package review remained in force."
            : "No live model response supplied; deterministic failure-package review was used for this governed smoke.";
    }

    private static string RequireInput(AgentRequest request, string key)
    {
        if (request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return Path.GetFullPath(value);

        throw new InvalidOperationException($"CriticAgent requires input '{key}'.");
    }

    private static IReadOnlyList<string> BuildRisks(JsonElement package, bool evidenceSufficient, bool actionable)
    {
        var risks = new List<string>();
        if (!evidenceSufficient)
            risks.Add("Failure package is missing expected/actual/evidence/repro data.");
        if (!actionable)
            risks.Add("Codex should not patch until likely area and safety rules are present.");
        if (ReadString(package, "Prompt").Contains("memory search", StringComparison.OrdinalIgnoreCase))
            risks.Add("Memory-search failures may reflect retrieval/ranking/test assertion issues; inspect trace before patching.");

        return risks.Count == 0 ? ["No immediate evidence gaps detected."] : risks;
    }

    private static string ReadString(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
