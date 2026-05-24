using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class DoubtAgent : StaticIronDevAgent
{
    private const string ReviewBoundary = "DoubtAgent reviews and challenges only. It does not patch, create tickets, mutate memory, approve writes, or override ConscienceAgent.";
    private readonly IAgentModelResolver _modelResolver;
    private readonly IAgentLlmClient? _llmClient;

    public DoubtAgent(AgentDefinition definition, IAgentModelResolver modelResolver, IAgentLlmClient? llmClient = null)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
        _llmClient = llmClient;
    }

    public override async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        var subject = ReadInput(request, "subject", "governed action");
        var observedProject = ReadInput(request, "observed_project", "IronDev");
        var affectedProject = ReadInput(request, "affected_project", observedProject);
        var targetLanguage = ReadInput(request, "target_language", "C#");
        var targetStack = ReadInput(request, "target_stack", ".NET");
        var planText = ReadInput(request, "plan", string.Empty);
        var evidenceRefs = SplitInput(ReadInput(request, "evidence_refs", string.Empty));
        var safetyRefs = SplitInput(ReadInput(request, "safety_refs", string.Empty));
        var prompt = BuildPrompt(subject, observedProject, affectedProject, targetLanguage, targetStack, planText, evidenceRefs, safetyRefs);
        var liveLlmRequested = ReadBoolInput(request, "live_llm");
        var llmResult = await ResolveLlmResultAsync(profile, prompt, liveLlmRequested, request, ct);
        var findings = BuildFindings(subject, targetLanguage, targetStack, planText, evidenceRefs, safetyRefs);

        var highOrCritical = findings.Any(finding =>
            string.Equals(finding.Severity, "High", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finding.Severity, "Critical", StringComparison.OrdinalIgnoreCase));
        var result = new DoubtReviewResult
        {
            ReviewId = $"doubt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..42],
            Subject = subject,
            ObservedProject = observedProject,
            AffectedProject = affectedProject,
            TargetLanguage = targetLanguage,
            TargetStack = targetStack,
            Criticisms = findings,
            RebuttalRequired = highOrCritical,
            KilljoyEscalation = highOrCritical,
            RevisionRequired = highOrCritical || findings.Count >= 3,
            Boundary = ReviewBoundary
        };

        var envelope = new
        {
            result,
            criticisms = result.Criticisms,
            result.RebuttalRequired,
            result.KilljoyEscalation,
            result.RevisionRequired,
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
            boundary = ReviewBoundary
        };

        return new AgentResult
        {
            AgentName = AgentName,
            Status = AgentRunStatus.Succeeded,
            Summary = $"DoubtAgent produced {findings.Count} adversarial finding(s) for {subject}.",
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = 0,
            OutputJson = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true }),
            CommandsRun = [$"doubt review --subject {QuoteIfNeeded(subject)}"],
            EvidencePaths = evidenceRefs,
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
                ResponseText = "No live model response supplied; deterministic adversarial review was used for this governed smoke."
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

    private static IReadOnlyList<DoubtFinding> BuildFindings(
        string subject,
        string targetLanguage,
        string targetStack,
        string planText,
        IReadOnlyList<string> evidenceRefs,
        IReadOnlyList<string> safetyRefs)
    {
        var findings = new List<DoubtFinding>();
        if (evidenceRefs.Count < 2)
        {
            findings.Add(Finding(
                "High",
                "Evidence",
                "Plan has weak evidence citation coverage",
                "A governed plan needs concrete trace, memory, or report evidence before it can be trusted.",
                evidenceRefs.FirstOrDefault() ?? "evidence:none",
                "Require at least two concrete evidence refs before promotion or memory proposal.",
                88,
                targetLanguage,
                targetStack));
        }

        if (!ContainsAny(planText, "conscience", "thoughtledger", "thought ledger"))
        {
            findings.Add(Finding(
                "High",
                "Governance",
                "Governance gates are not explicit in the plan text",
                "The plan does not visibly require ConscienceAgent and ThoughtLedger before write-capable work.",
                safetyRefs.FirstOrDefault() ?? "boundary:missing-governance",
                "Add explicit ConscienceAgent and ThoughtLedger gates before any write path.",
                91,
                targetLanguage,
                targetStack));
        }

        if (ContainsAny(planText, "accepted memory", "mutate memory", "update memory") &&
            !ContainsAny(planText, "staged", "proposal", "human approval"))
        {
            findings.Add(Finding(
                "Critical",
                "Memory",
                "Memory mutation is not staged",
                "The plan appears to touch accepted memory without saying proposals are staged and reviewed.",
                "memory:accepted-authority",
                "Make MemoryImprovementAgent proposal-only and require human approval for accepted memory changes.",
                94,
                targetLanguage,
                targetStack));
        }

        if (!ContainsAny(planText, "test", "quality", "killjoy"))
        {
            findings.Add(Finding(
                "Medium",
                "Maintainability",
                "No explicit test or Killjoy gate",
                "The plan does not clearly say how deterministic quality evidence will be produced.",
                "quality:missing",
                "Run QualityAgent/Killjoy and attach the report before promotion.",
                79,
                targetLanguage,
                targetStack));
        }

        if (string.IsNullOrWhiteSpace(targetLanguage) || string.IsNullOrWhiteSpace(targetStack))
        {
            findings.Add(Finding(
                "Medium",
                "Language",
                "Target language or stack is ambiguous",
                "Agent review cannot attack language-specific issues without target language and stack metadata.",
                "runtime:missing",
                "Carry targetLanguage and targetStack through the plan, trace, and proposal.",
                84,
                targetLanguage,
                targetStack));
        }

        if (findings.Count == 0)
        {
            findings.Add(Finding(
                "Low",
                "Other",
                "No high-risk adversarial blocker found",
                $"DoubtAgent did not find a blocker in '{subject}', but human review remains required.",
                evidenceRefs.FirstOrDefault() ?? "evidence:summary",
                "Proceed only through the existing governed review path.",
                70,
                targetLanguage,
                targetStack));
        }

        return findings.Take(5).ToArray();
    }

    private static DoubtFinding Finding(
        string severity,
        string category,
        string title,
        string description,
        string evidenceCitation,
        string suggestedFix,
        int confidence,
        string targetLanguage,
        string targetStack) =>
        new()
        {
            FindingId = $"doubt-finding-{Guid.NewGuid():N}"[..28],
            Severity = severity,
            Category = category,
            Title = title,
            Description = description,
            EvidenceCitation = evidenceCitation,
            SuggestedFix = suggestedFix,
            Confidence = confidence,
            TargetLanguage = targetLanguage,
            TargetStack = targetStack
        };

    private static string BuildPrompt(
        string subject,
        string observedProject,
        string affectedProject,
        string targetLanguage,
        string targetStack,
        string planText,
        IReadOnlyList<string> evidenceRefs,
        IReadOnlyList<string> safetyRefs) =>
        $"""
        You are DoubtAgent, the adversarial review agent for IronDev/IDA.
        Attack this plan for hidden assumptions, missing evidence, language-specific risks, overconfidence, safety bypasses, and maintainability problems.
        Subject: {subject}
        Observed project: {observedProject}
        Affected project: {affectedProject}
        Target language: {targetLanguage}
        Target stack: {targetStack}
        Evidence refs: {string.Join("; ", evidenceRefs)}
        Safety refs: {string.Join("; ", safetyRefs)}
        Plan: {planText}
        Return concise JSON criticisms only. Do not patch, create tickets, mutate memory, or approve writes.
        """;

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string ReadInput(AgentRequest request, string key, string defaultValue) =>
        request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;

    private static IReadOnlyList<string> SplitInput(string value) =>
        value.Split(['|', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static bool ReadBoolInput(AgentRequest request, string key) =>
        request.Inputs.TryGetValue(key, out var value) &&
        bool.TryParse(value, out var parsed) &&
        parsed;

    private static string BuildModelSummary(AgentLlmCallResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ResponseText))
            return result.ResponseText;

        return result.WasAttempted
            ? "Live model call did not return usable content; deterministic adversarial review remained in force."
            : "No live model response supplied; deterministic adversarial review was used for this governed smoke.";
    }

    private static string QuoteIfNeeded(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
