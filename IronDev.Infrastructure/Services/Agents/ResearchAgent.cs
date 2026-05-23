using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class ResearchAgent : StaticIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;

    public ResearchAgent(AgentDefinition definition, IAgentModelResolver modelResolver)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
    }

    public override Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
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
            boundary = "ResearchAgent Lite is read-only. It packages explicit external evidence; it does not decide architecture, create tickets, update memory, patch code, or override project memory.",
            evidenceMode = "explicit_sources_only"
        };

        return Task.FromResult(new AgentResult
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
        });
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

    private static string ReadInput(AgentRequest request, string key, string defaultValue) =>
        request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
