using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class ResearchAgent : StaticIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;
    private readonly IAgentLlmClient? _llmClient;

    public ResearchAgent(AgentDefinition definition, IAgentModelResolver modelResolver, IAgentLlmClient? llmClient = null)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
        _llmClient = llmClient;
    }

    public override async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        var project = ReadInput(request, "project", "IronDev");
        var topic = ReadInput(request, "topic", string.Empty);
        if (string.IsNullOrWhiteSpace(topic))
            throw new InvalidOperationException("ResearchAgent requires a topic.");

        var sourceUrl = ReadInput(request, "source_url", "external-evidence://explicit-source-required");
        var sourceTitle = ReadInput(request, "source_title", "Explicit external evidence source");
        var sourceType = ReadInput(request, "source_type", "ExternalEvidence");
        var snippet = ReadInput(request, "snippet", "No snippet supplied. ResearchAgent Lite packages explicit evidence only in this slice.");
        var publishedDate = ReadInput(request, "published_date", string.Empty);
        var liveLlmRequested = ReadBoolInput(request, "live_llm");
        var prompt = BuildPrompt(project, topic, sourceUrl, sourceTitle, sourceType, snippet);
        var llmResult = await ResolveLlmResultAsync(profile, prompt, liveLlmRequested, request, ct);

        var findings = BuildFindings(topic, snippet);
        var package = new
        {
            type = "ResearchPackage",
            topic,
            project,
            sources = new[]
            {
                new
                {
                    url = sourceUrl,
                    title = sourceTitle,
                    publishedDate = string.IsNullOrWhiteSpace(publishedDate) ? null : publishedDate,
                    sourceType,
                    credibilityNote = BuildCredibilityNote(sourceType),
                    snippet
                }
            },
            keyFindings = findings,
            conflicts = Array.Empty<string>(),
            confidenceScore = sourceUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? 0.74m : 0.55m,
            authorityWarning = $"External research is evidence only. Accepted {project} memory remains authoritative unless explicitly changed by a project decision.",
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
                error = llmResult.WasSuccessful ? string.Empty : llmResult.ErrorMessage,
                boundary = "Live ResearchAgent output is external evidence only. It cannot override accepted project memory or create work."
            },
            boundary = "ResearchAgent Lite is read-only. It packages explicit external evidence; it does not decide architecture, create tickets, update memory, patch code, or override project memory.",
            evidenceMode = "explicit_sources_only"
        };

        return new AgentResult
        {
            AgentName = AgentName,
            Status = AgentRunStatus.Succeeded,
            Summary = $"ResearchAgent packaged external evidence for {project}: {topic}.",
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = 0,
            OutputJson = JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true }),
            CommandsRun = [$"research package --project {QuoteIfNeeded(project)} --topic {QuoteIfNeeded(topic)}"],
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
                ResponseText = "No live model response supplied; deterministic explicit-source research packaging was used."
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

    private static IReadOnlyList<string> BuildFindings(string topic, string snippet)
    {
        var findings = new List<string>();
        if (snippet.Contains("catalogue", StringComparison.OrdinalIgnoreCase) ||
            topic.Contains("inventory", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add("Separate catalogue identity from stock quantity when inventory behaviour matters.");
        }

        if (snippet.Contains("negative", StringComparison.OrdinalIgnoreCase) ||
            topic.Contains("stock", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add("Prevent negative stock in domain/service logic, not only in UI validation.");
        }

        if (snippet.Contains("movement", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("audit", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add("Use stock movement records when audit/history matters.");
        }

        if (findings.Count == 0)
            findings.Add("External evidence was packaged for review; no domain-specific finding was inferred by the lite classifier.");

        return findings;
    }

    private static string BuildCredibilityNote(string sourceType) =>
        sourceType.Contains("Official", StringComparison.OrdinalIgnoreCase)
            ? "Official documentation or vendor source."
            : "External evidence supplied explicitly for review; verify before changing accepted project memory.";

    private static string BuildPrompt(string project, string topic, string sourceUrl, string sourceTitle, string sourceType, string snippet) =>
        $"""
        You are ResearchAgent for IronDev/IDA.
        Review this explicitly supplied external evidence for project '{project}' and topic '{topic}'.
        Return concise JSON with key findings, conflicts, source credibility notes, and questions for Codex/human review.
        Do not decide architecture, create tickets, update memory, patch files, override accepted project memory, or approve writes.
        Source URL: {sourceUrl}
        Source title: {sourceTitle}
        Source type: {sourceType}
        Snippet: {snippet}
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
            ? "Live model call did not return usable content; deterministic explicit-source research packaging remained in force."
            : "No live model response supplied; deterministic explicit-source research packaging was used.";
    }

    private static string ReadInput(AgentRequest request, string key, string defaultValue) =>
        request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
