using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class SentinelAgent : StaticIronDevAgent
{
    private readonly IAgentModelResolver _modelResolver;

    public SentinelAgent(AgentDefinition definition, IAgentModelResolver modelResolver)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
    }

    public override Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
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
            boundary = "SentinelAgent Lite is observational only. It creates insight artefacts; it does not patch code, create tickets, approve writes, or mutate memory."
        };

        return Task.FromResult(new AgentResult
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
        });
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
