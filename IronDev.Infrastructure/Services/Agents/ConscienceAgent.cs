using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class ConscienceAgent : StaticIronDevAgent
{
    private const string ReviewBoundary = "ConscienceAgent reviews only. It does not patch, create tickets, mutate memory, or approve itself.";
    private readonly IAgentModelResolver _modelResolver;

    public ConscienceAgent(AgentDefinition definition, IAgentModelResolver modelResolver)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
    }

    public override Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        var actionType = ReadInput(request, "action_type", string.Empty);
        var observedProject = ReadInput(request, "observed_project", string.Empty);
        var affectedProject = ReadInput(request, "affected_project", string.Empty);
        var evidence = SplitInput(ReadInput(request, "evidence", string.Empty));
        var requestedTools = SplitInput(ReadInput(request, "requested_tools", string.Empty));
        var memoryAuthorityRefs = SplitInput(ReadInput(request, "memory_authority_refs", string.Empty));
        var safetyBoundaryRefs = SplitInput(ReadInput(request, "safety_boundary_refs", string.Empty));

        var blockingFactors = new List<string>();
        var missingEvidence = new List<string>();
        var allowingFactors = new List<string>();
        var requiredNextSteps = new List<string>();
        var violatedBoundaries = new List<string>();

        if (string.IsNullOrWhiteSpace(actionType))
            missingEvidence.Add("actionType is required.");

        if (string.IsNullOrWhiteSpace(observedProject) || string.IsNullOrWhiteSpace(affectedProject))
            missingEvidence.Add("observedProject and affectedProject must be explicit.");

        if (evidence.Count == 0)
            missingEvidence.Add("At least one evidence item is required.");

        var actionText = $"{actionType} {string.Join(' ', requestedTools)}".ToLowerInvariant();
        var boundaryText = string.Join(' ', safetyBoundaryRefs).ToLowerInvariant();

        if (ContainsAny(actionText, "real repo", "real repository", "production", "developer working tree"))
        {
            blockingFactors.Add("Requested action targets a real repository or developer working tree.");
            violatedBoundaries.Add("NoRealRepositoryWrites");
        }

        if (ContainsAny(actionText, "tester") && ContainsAny(actionText, "repair", "fix", "patch", "write"))
        {
            blockingFactors.Add("TesterAgent was asked to repair or mutate instead of execute/report.");
            violatedBoundaries.Add("TesterAgentExecutesOnly");
        }

        if (ContainsAny(actionText, "sentinel") && ContainsAny(actionText, "create ticket", "patch", "mutate", "write"))
        {
            blockingFactors.Add("SentinelAgent was asked to mutate state instead of observe.");
            violatedBoundaries.Add("SentinelAgentObservesOnly");
        }

        if (ContainsAny(actionText, "research") && ContainsAny(actionText, "override", "authority", "replace accepted", "change architecture"))
        {
            blockingFactors.Add("ResearchAgent evidence was asked to override accepted project memory.");
            violatedBoundaries.Add("ProjectMemoryRemainsAuthority");
        }

        if (ContainsAny(actionText, "self-approve", "self approve", "approve itself", "auto-merge", "automerge"))
        {
            blockingFactors.Add("Action text implies self-approval or auto-merge, which is categorically blocked.");
            violatedBoundaries.Add("NoAgentSelfApproval");
        }

        if (ContainsAny(actionText, "bypass", "skip conscience", "skip thoughtledger", "override governance"))
        {
            blockingFactors.Add("Action text implies bypassing a governance gate.");
            violatedBoundaries.Add("GovernanceGatesCannotBeBypassed");
        }

        if (ContainsAny(actionText, "workspace", "apply", "patch") &&
            ContainsAny(actionText, "disposable") &&
            !ContainsAny(boundaryText, "disposable workspace", "outside real repo", "before hash", "after hash"))
        {
            missingEvidence.Add("Disposable workspace action requires explicit workspace boundary evidence.");
        }

        if (blockingFactors.Count > 0)
            requiredNextSteps.Add("Stop this action and produce a failure/safety package for Codex or human review.");

        if (missingEvidence.Count > 0)
            requiredNextSteps.Add("Collect the missing evidence before reviewing the action again.");

        if (blockingFactors.Count == 0 && missingEvidence.Count == 0)
        {
            allowingFactors.Add("Action is evidence-backed.");
            allowingFactors.Add("Project identity is explicit.");
            allowingFactors.Add("No blocked mutation boundary was requested.");
        }

        if (ContainsAny(actionText, "disposable") && ContainsAny(boundaryText, "disposable workspace"))
            allowingFactors.Add("Disposable workspace boundary is explicit.");

        var decision = blockingFactors.Count > 0
            ? "Block"
            : missingEvidence.Count > 0
                ? "NeedsMoreEvidence"
                : "Allow";

        var result = new
        {
            decision,
            confidence = decision == "Allow" ? 0.82m
                : decision == "Block" ? Math.Min(0.95m, 0.88m + (blockingFactors.Count * 0.02m))
                : 0.67m,
            reasons = blockingFactors.Count > 0 ? blockingFactors : missingEvidence.Count > 0 ? missingEvidence : allowingFactors,
            allowingFactors,
            blockingFactors,
            missingEvidence,
            violatedBoundaries,
            requiredNextSteps,
            observedProject,
            affectedProject,
            authoritySources = memoryAuthorityRefs,
            requestedTools,
            boundary = ReviewBoundary
        };

        return Task.FromResult(new AgentResult
        {
            AgentName = AgentName,
            Status = AgentRunStatus.Succeeded,
            Summary = $"ConscienceAgent decision={decision} for {actionType}.",
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = 0,
            OutputJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            CommandsRun = [$"conscience review --action-type {QuoteIfNeeded(actionType)}"],
            EvidencePaths = [],
            CompletedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static string ReadInput(AgentRequest request, string key, string defaultValue) =>
        request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;

    private static IReadOnlyList<string> SplitInput(string value) =>
        value.Split(['|', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
