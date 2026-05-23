using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class SentinelAgent : StaticIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;
    private readonly IAgentLlmClient? _llmClient;

    public SentinelAgent(AgentDefinition definition, IAgentModelResolver modelResolver, IAgentLlmClient? llmClient = null)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
        _llmClient = llmClient;
    }

    public override async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        var observedProject = ReadInput(request, "observed_project", "Unknown");
        var affectedProject = ReadInput(request, "affected_project", observedProject);
        var findingType = ReadInput(request, "finding_type", "Observation");
        var evidence = ReadInput(request, "evidence", string.Empty);
        if (string.IsNullOrWhiteSpace(evidence))
            throw new InvalidOperationException("SentinelAgent requires evidence input.");

        var insightType = ClassifyInsightType(findingType, evidence);
        var severity = ClassifySeverity(insightType, evidence);
        var recommendedDispositions = BuildRecommendedDispositions(insightType);
        var prompt = BuildPrompt(observedProject, affectedProject, findingType, evidence);
        var liveLlmRequested = ReadBoolInput(request, "live_llm");
        var llmResult = await ResolveLlmResultAsync(profile, prompt, liveLlmRequested, request, ct);
        var insight = new
        {
            insightId = $"sentinel-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..42],
            dogfoodRunId = request.DogfoodRunId,
            observedProject,
            affectedProject,
            insightType,
            title = BuildTitle(insightType, evidence),
            description = BuildDescription(observedProject, affectedProject, insightType, evidence),
            severity,
            confidence = BuildConfidence(insightType, evidence),
            evidenceRefs = new[] { evidence },
            recommendedDispositions,
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
            boundary = "SentinelAgent Lite is observational only. It creates insight artefacts; it does not patch code, create tickets, approve writes, or mutate memory."
        };

        return new AgentResult
        {
            AgentName = AgentName,
            Status = AgentRunStatus.Succeeded,
            Summary = $"SentinelAgent observed {insightType} affecting {affectedProject}.",
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = 0,
            OutputJson = JsonSerializer.Serialize(insight, new JsonSerializerOptions { WriteIndented = true }),
            CommandsRun = [$"sentinel observe --observed-project {QuoteIfNeeded(observedProject)} --affected-project {QuoteIfNeeded(affectedProject)}"],
            EvidencePaths = [],
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
                ResponseText = "No live model response supplied; deterministic SentinelAgent classification was used for this governed smoke."
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

    private static string BuildPrompt(string observedProject, string affectedProject, string findingType, string evidence) =>
        $"""
        You are SentinelAgent for IronDev/IDA.
        Review this internal evidence and return concise JSON with insight risks, affected scope, recommended disposition, and follow-up questions.
        Observed project: {observedProject}
        Affected project: {affectedProject}
        Finding type: {findingType}
        Evidence: {evidence}
        Do not create tickets, mutate memory, patch files, block builds, approve writes, or take action.
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
            ? "Live model call did not return usable content; deterministic SentinelAgent classification remained in force."
            : "No live model response supplied; deterministic SentinelAgent classification was used for this governed smoke.";
    }

    private static string ReadInput(AgentRequest request, string key, string defaultValue) =>
        request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;

    private static string ClassifyInsightType(string findingType, string evidence)
    {
        if (!string.IsNullOrWhiteSpace(findingType) &&
            !string.Equals(findingType, "Observation", StringComparison.OrdinalIgnoreCase))
            return findingType;

        if (evidence.Contains("GeneralChat", StringComparison.OrdinalIgnoreCase) ||
            evidence.Contains("wrong route", StringComparison.OrdinalIgnoreCase))
            return "RoutingWeakness";

        if (evidence.Contains("real repo", StringComparison.OrdinalIgnoreCase) ||
            evidence.Contains("unsafe", StringComparison.OrdinalIgnoreCase))
            return "SafetyBoundarySignal";

        if (evidence.Contains("negative stock", StringComparison.OrdinalIgnoreCase) ||
            evidence.Contains("inventory", StringComparison.OrdinalIgnoreCase))
            return "DomainRuleSignal";

        return "Observation";
    }

    private static string ClassifySeverity(string insightType, string evidence)
    {
        if (evidence.Contains("unsafe", StringComparison.OrdinalIgnoreCase) ||
            evidence.Contains("real repo", StringComparison.OrdinalIgnoreCase))
            return "High";

        if (string.Equals(insightType, "RoutingWeakness", StringComparison.OrdinalIgnoreCase))
            return "Concern";

        return "Info";
    }

    private static decimal BuildConfidence(string insightType, string evidence)
    {
        if (string.Equals(insightType, "RoutingWeakness", StringComparison.OrdinalIgnoreCase) &&
            evidence.Contains("Expected", StringComparison.OrdinalIgnoreCase) &&
            evidence.Contains("Actual", StringComparison.OrdinalIgnoreCase))
            return 0.9m;

        return 0.72m;
    }

    private static IReadOnlyList<string> BuildRecommendedDispositions(string insightType)
    {
        if (string.Equals(insightType, "RoutingWeakness", StringComparison.OrdinalIgnoreCase))
            return ["CreateTicket", "CreateDiscussion", "CreateCampaignFinding"];

        if (string.Equals(insightType, "SafetyBoundarySignal", StringComparison.OrdinalIgnoreCase))
            return ["CreateDecisionCandidate", "CreateObservation"];

        if (string.Equals(insightType, "DomainRuleSignal", StringComparison.OrdinalIgnoreCase))
            return ["CreateObservation", "ReviewProjectTicket"];

        return ["CreateObservation"];
    }

    private static string BuildTitle(string insightType, string evidence)
    {
        if (string.Equals(insightType, "RoutingWeakness", StringComparison.OrdinalIgnoreCase))
            return "Project knowledge save phrase may route to chat";

        if (string.Equals(insightType, "SafetyBoundarySignal", StringComparison.OrdinalIgnoreCase))
            return "Safety boundary signal observed";

        return evidence.Length <= 80 ? evidence : evidence[..80];
    }

    private static string BuildDescription(string observedProject, string affectedProject, string insightType, string evidence) =>
        observedProject.Equals(affectedProject, StringComparison.OrdinalIgnoreCase)
            ? $"{insightType} observed in {observedProject}: {evidence}"
            : $"{insightType} observed during {observedProject} work but affects {affectedProject}: {evidence}";

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
