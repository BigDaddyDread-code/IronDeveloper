using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class ThoughtLedgerService : IThoughtLedgerService
{
    public ThoughtLedgerResult Explain(
        string subject,
        string observedProject,
        string affectedProject,
        string? conscienceDecision,
        IReadOnlyList<string> evidence,
        IReadOnlyList<string> knownBoundaries,
        IReadOnlyList<string> uncertainties,
        IReadOnlyList<string> candidateActions)
    {
        var decision = string.IsNullOrWhiteSpace(conscienceDecision) ? "Unknown" : conscienceDecision;
        var blockedActions = candidateActions
            .Where(action => IsBlocked(action, knownBoundaries, decision))
            .Select(action => Entry("BlockedAction", action, affectedProject))
            .ToArray();
        var temptingActions = candidateActions
            .Where(action => !IsBlocked(action, knownBoundaries, decision))
            .Select(action => Entry("CandidateAction", action, affectedProject))
            .ToArray();
        var saferAlternatives = BuildSaferAlternatives(blockedActions, knownBoundaries, affectedProject);
        var uncertaintyEntries = uncertainties.Count > 0
            ? uncertainties.Select(value => Entry("Uncertainty", value, affectedProject)).ToArray()
            : evidence.Count == 0
                ? [Entry("Uncertainty", "Evidence is missing or too thin to justify action.", affectedProject)]
                : [];

        var currentBelief = BuildCurrentBelief(subject, observedProject, affectedProject, decision, evidence.Count);

        return new ThoughtLedgerResult
        {
            Subject = subject,
            CurrentBelief = currentBelief,
            Evidence = evidence.Select(value => Entry("Evidence", value, observedProject)).ToArray(),
            Uncertainties = uncertaintyEntries,
            Assumptions = BuildAssumptions(knownBoundaries, affectedProject),
            TemptingActions = temptingActions,
            BlockedActions = blockedActions,
            SaferAlternatives = saferAlternatives,
            RecommendedNextMove = BuildRecommendedNextMove(decision, blockedActions.Length, uncertaintyEntries.Length),
            ObservedProject = observedProject,
            AffectedProject = affectedProject
        };
    }

    private static string BuildCurrentBelief(
        string subject,
        string observedProject,
        string affectedProject,
        string decision,
        int evidenceCount)
    {
        var projectText = observedProject.Equals(affectedProject, StringComparison.OrdinalIgnoreCase)
            ? $"in {observedProject}"
            : $"observed in {observedProject} and affecting {affectedProject}";

        return evidenceCount == 0
            ? $"{subject}: not enough evidence yet {projectText}; visible summary only."
            : $"{subject}: Conscience decision is {decision} {projectText}; visible summary only.";
    }

    private static IReadOnlyList<ThoughtLedgerEntry> BuildAssumptions(
        IReadOnlyList<string> knownBoundaries,
        string affectedProject)
    {
        if (knownBoundaries.Count == 0)
            return [Entry("Assumption", "No explicit boundary was supplied, so the safe default is no mutation.", affectedProject)];

        return knownBoundaries.Select(value => Entry("KnownBoundary", value, affectedProject)).ToArray();
    }

    private static IReadOnlyList<ThoughtLedgerEntry> BuildSaferAlternatives(
        IReadOnlyList<ThoughtLedgerEntry> blockedActions,
        IReadOnlyList<string> knownBoundaries,
        string affectedProject)
    {
        var alternatives = new List<ThoughtLedgerEntry>();
        if (blockedActions.Count > 0)
        {
            alternatives.Add(Entry("SaferAlternative", "Use disposable workspace proof or preview-only review instead of real repo mutation.", affectedProject));
            alternatives.Add(Entry("SaferAlternative", "Package evidence for Codex/human review before any write path.", affectedProject));
        }

        if (knownBoundaries.Any(boundary => boundary.Contains("UI", StringComparison.OrdinalIgnoreCase)))
            alternatives.Add(Entry("SaferAlternative", "Keep UI work blocked until foundation gates pass.", affectedProject));

        return alternatives;
    }

    private static string BuildRecommendedNextMove(string decision, int blockedCount, int uncertaintyCount)
    {
        if (blockedCount > 0 || string.Equals(decision, "Block", StringComparison.OrdinalIgnoreCase))
            return "Do not execute the blocked action; use the safer alternative and keep evidence.";

        if (uncertaintyCount > 0 || string.Equals(decision, "NeedsMoreEvidence", StringComparison.OrdinalIgnoreCase))
            return "Gather missing evidence, then ask ConscienceAgent to review again.";

        return "Proceed only within the stated boundary and keep the evidence package.";
    }

    private static bool IsBlocked(string action, IReadOnlyList<string> knownBoundaries, string decision)
    {
        if (string.Equals(decision, "Block", StringComparison.OrdinalIgnoreCase))
            return true;

        if (ContainsAny(action, "real repo", "real repository", "developer working tree", "production write"))
            return true;

        if (ContainsAny(action, "ui implementation", "build ui", "start ui") &&
            knownBoundaries.Any(boundary => boundary.Contains("UI", StringComparison.OrdinalIgnoreCase)))
            return true;

        return ContainsAny(action, "tester fix", "sentinel create ticket", "research override", "mutate memory");
    }

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static ThoughtLedgerEntry Entry(string category, string text, string? project = null) =>
        new()
        {
            Category = category,
            Text = text,
            Project = string.IsNullOrWhiteSpace(project) ? null : project
        };
}
