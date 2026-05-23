using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class ArchitectAgent : StaticIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;
    private readonly IAgentLlmClient? _llmClient;

    public ArchitectAgent(AgentDefinition definition, IAgentModelResolver modelResolver, IAgentLlmClient? llmClient = null)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
        _llmClient = llmClient;
    }

    public override async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        var project = ReadInput(request, "project", "IronDev");
        var proposal = RequireInput(request, "proposal");
        var weightedContext = ReadInput(request, "weighted_context", string.Empty);
        var safetyBoundary = ReadInput(request, "safety_boundary", "No real repository writes; disposable workspace only for apply.");
        var prompt = BuildPrompt(project, proposal, weightedContext, safetyBoundary);
        var liveLlmRequested = ReadBoolInput(request, "live_llm");
        var llmResult = await ResolveLlmResultAsync(profile, prompt, liveLlmRequested, request, ct);
        var architectureDecision = BuildDecision(proposal, weightedContext, safetyBoundary);
        var review = new
        {
            type = "ArchitectureReview",
            project,
            proposal,
            decision = architectureDecision.Decision,
            confidence = architectureDecision.Confidence,
            requiredEvidence = architectureDecision.RequiredEvidence,
            risks = architectureDecision.Risks,
            recommendations = architectureDecision.Recommendations,
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
            boundary = "ArchitectAgent reviews architecture and produces recommendations only. It does not patch, create accepted decisions, mutate memory, or approve real repo writes."
        };

        return new AgentResult
        {
            AgentName = AgentName,
            Status = AgentRunStatus.Succeeded,
            Summary = $"ArchitectAgent reviewed {project} proposal with decision {architectureDecision.Decision}.",
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = 0,
            OutputJson = JsonSerializer.Serialize(review, new JsonSerializerOptions { WriteIndented = true }),
            CommandsRun = [$"architect review --project {QuoteIfNeeded(project)}"],
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
                ResponseText = "No live model response supplied; deterministic architecture review was used for this governed smoke."
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

    private static ArchitectureDecision BuildDecision(string proposal, string weightedContext, string safetyBoundary)
    {
        var risks = new List<string>();
        var requiredEvidence = new List<string>();
        var recommendations = new List<string>();

        if (string.IsNullOrWhiteSpace(weightedContext))
            requiredEvidence.Add("WeightedContextBundle");
        if (!safetyBoundary.Contains("No real", StringComparison.OrdinalIgnoreCase))
            risks.Add("Safety boundary does not explicitly block real repository writes.");
        if (proposal.Contains("database", StringComparison.OrdinalIgnoreCase) &&
            !weightedContext.Contains("database", StringComparison.OrdinalIgnoreCase))
            risks.Add("Proposal introduces persistence without weighted architecture context.");
        if (proposal.Contains("UI", StringComparison.OrdinalIgnoreCase) &&
            safetyBoundary.Contains("UI implementation remains blocked", StringComparison.OrdinalIgnoreCase))
            risks.Add("Proposal touches UI while the current boundary blocks UI implementation.");

        recommendations.Add("Keep the next slice small and evidence-backed.");
        recommendations.Add("Require ConscienceAgent and ThoughtLedger before any write-capable workflow.");

        var decision = risks.Count == 0 && requiredEvidence.Count == 0
            ? "AllowPlanningOnly"
            : requiredEvidence.Count > 0
                ? "NeedsMoreEvidence"
                : "ReviseBeforeBuild";

        return new ArchitectureDecision(
            decision,
            requiredEvidence.Count == 0 ? 0.82m : 0.58m,
            requiredEvidence,
            risks.Count == 0 ? ["No architecture-blocking risks detected."] : risks,
            recommendations);
    }

    private static string BuildPrompt(string project, string proposal, string weightedContext, string safetyBoundary) =>
        $"""
        You are ArchitectAgent for IronDev/IDA.
        Review the proposal for project '{project}'.
        Proposal: {proposal}
        Weighted context: {weightedContext}
        Safety boundary: {safetyBoundary}
        Return JSON with decision, risks, required evidence, and recommendations.
        Do not approve real repository writes.
        """;

    private static string RequireInput(AgentRequest request, string key)
    {
        if (request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value;

        throw new InvalidOperationException($"ArchitectAgent requires input '{key}'.");
    }

    private static string ReadInput(AgentRequest request, string key, string defaultValue) =>
        request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;

    private static bool ReadBoolInput(AgentRequest request, string key) =>
        request.Inputs.TryGetValue(key, out var value) &&
        bool.TryParse(value, out var parsed) &&
        parsed;

    private static string BuildModelSummary(AgentLlmCallResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ResponseText))
            return result.ResponseText;

        return result.WasAttempted
            ? "Live model call did not return usable content; deterministic architecture review remained in force."
            : "No live model response supplied; deterministic architecture review was used for this governed smoke.";
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private sealed record ArchitectureDecision(
        string Decision,
        decimal Confidence,
        IReadOnlyList<string> RequiredEvidence,
        IReadOnlyList<string> Risks,
        IReadOnlyList<string> Recommendations);
}
