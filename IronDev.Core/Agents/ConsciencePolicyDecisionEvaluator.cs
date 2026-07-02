namespace IronDev.Core.Agents;

public static class ConsciencePolicyDecisionEvaluator
{
    public const string ReviewBoundary = "ConscienceAgent reviews only. It does not patch, create tickets, mutate memory, or approve itself.";

    public static ConsciencePolicyDecision Evaluate(ConsciencePolicyDecisionRequest request)
    {
        var actionType = request.ActionType ?? string.Empty;
        var observedProject = request.ObservedProject ?? string.Empty;
        var affectedProject = request.AffectedProject ?? string.Empty;
        var evidence = request.Evidence ?? [];
        var requestedTools = request.RequestedTools ?? [];
        var memoryAuthorityRefs = request.MemoryAuthorityRefs ?? [];
        var safetyBoundaryRefs = request.SafetyBoundaryRefs ?? [];

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

        return new ConsciencePolicyDecision
        {
            Decision = decision,
            Confidence = decision == "Allow"
                ? 0.82m
                : decision == "Block"
                    ? Math.Min(0.95m, 0.88m + (blockingFactors.Count * 0.02m))
                    : 0.67m,
            Reasons = blockingFactors.Count > 0 ? blockingFactors : missingEvidence.Count > 0 ? missingEvidence : allowingFactors,
            AllowingFactors = allowingFactors,
            BlockingFactors = blockingFactors,
            MissingEvidence = missingEvidence,
            ViolatedBoundaries = violatedBoundaries,
            RequiredNextSteps = requiredNextSteps,
            ObservedProject = observedProject,
            AffectedProject = affectedProject,
            AuthoritySources = memoryAuthorityRefs,
            RequestedTools = requestedTools,
            Boundary = ReviewBoundary
        };
    }

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
}
