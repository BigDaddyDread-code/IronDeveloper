using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class MemoryImprovementAgent : StaticIronDevAgent
{
    private const int DefaultMaxContextTokens = 2400;
    private const int DefaultMaxProposals = 3;
    private const string ProposalBoundary = "MemoryImprovementAgent proposes staged memory improvements only. It does not mutate accepted memory, create tickets, patch files, or approve itself.";
    private readonly IAgentModelResolver _modelResolver;
    private readonly IAgentLlmClient? _llmClient;

    public MemoryImprovementAgent(AgentDefinition definition, IAgentModelResolver modelResolver, IAgentLlmClient? llmClient = null)
        : base(definition, modelResolver)
    {
        _modelResolver = modelResolver;
        _llmClient = llmClient;
    }

    public override async Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default)
    {
        var profile = _modelResolver.ResolveForAgent(Definition);
        var observedProject = ReadInput(request, "observed_project", "IronDev");
        var affectedProject = ReadInput(request, "affected_project", observedProject);
        var targetLanguage = ReadInput(request, "target_language", "C#");
        var targetStack = ReadInput(request, "target_stack", ".NET");
        var runTracePath = ReadInput(request, "run_trace_path", string.Empty);
        var doubtFindings = ReadInput(request, "doubt_findings", string.Empty);
        var killjoyReview = ReadInput(request, "killjoy_review", string.Empty);
        var promotionOutcome = ReadInput(request, "promotion_outcome", "NeedsHumanReview");
        var evidenceRefs = SplitInput(ReadInput(request, "evidence_refs", string.Empty));
        var maxContextTokens = ReadIntInput(request, "max_context_tokens", DefaultMaxContextTokens);
        var maxProposals = Math.Clamp(ReadIntInput(request, "max_proposals", DefaultMaxProposals), 1, DefaultMaxProposals);
        var context = BuildContext(runTracePath, doubtFindings, killjoyReview, promotionOutcome, evidenceRefs, maxContextTokens);
        var prompt = BuildPrompt(observedProject, affectedProject, targetLanguage, targetStack, context, maxProposals);
        var liveLlmRequested = ReadBoolInput(request, "live_llm");
        var llmResult = await ResolveLlmResultAsync(profile, prompt, liveLlmRequested, request, ct);
        var proposal = BuildProposal(
            observedProject,
            affectedProject,
            targetLanguage,
            targetStack,
            context,
            evidenceRefs,
            maxContextTokens,
            maxProposals);

        var envelope = new
        {
            proposal,
            proposal.Proposals,
            proposal.AuthorityKeyReadiness,
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
            boundary = ProposalBoundary
        };

        return new AgentResult
        {
            AgentName = AgentName,
            Status = AgentRunStatus.Succeeded,
            Summary = $"MemoryImprovementAgent staged {proposal.Proposals.Count} proposal(s) for {affectedProject}.",
            ModelProfileName = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            ExitCode = 0,
            OutputJson = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true }),
            CommandsRun = [$"memory-improvement propose --affected-project {affectedProject}"],
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
                ResponseText = "No live model response supplied; deterministic memory-improvement proposal was used for this governed smoke."
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

    private static MemoryImprovementProposal BuildProposal(
        string observedProject,
        string affectedProject,
        string targetLanguage,
        string targetStack,
        MemoryImprovementContext context,
        IReadOnlyList<string> evidenceRefs,
        int maxContextTokens,
        int maxProposals)
    {
        var proposals = new List<MemoryImprovementAction>
        {
            new()
            {
                ProposalId = $"memory-proposal-{Guid.NewGuid():N}"[..32],
                ActionType = "CreateObservation",
                TargetDocumentId = "ADVERSARIAL_REVIEW_OBSERVATION_183",
                Title = "Capture adversarial review lessons without changing accepted memory",
                Summary = "Stage an observation that high/critical DoubtAgent findings require explicit Killjoy rebuttal before promotion.",
                RecommendedDisposition = "Stage",
                MemoryAuthorityImpact = "None",
                TargetLanguage = targetLanguage,
                TargetStack = targetStack,
                EvidenceRefs = evidenceRefs.Take(3).ToArray(),
                RequiredReviews = ["Killjoy", "Conscience", "HumanOrCodex"]
            },
            new()
            {
                ProposalId = $"memory-proposal-{Guid.NewGuid():N}"[..32],
                ActionType = "UpdateDraftMemory",
                TargetDocumentId = "AGENTS",
                Title = "Clarify MemoryImprovementAgent remains proposal-only",
                Summary = "Update draft agent documentation to say MemoryImprovementAgent cannot receive accepted-memory authority during Alpha.",
                RecommendedDisposition = "NeedsHumanReview",
                MemoryAuthorityImpact = "DraftOnly",
                TargetLanguage = targetLanguage,
                TargetStack = targetStack,
                EvidenceRefs = evidenceRefs.Take(3).ToArray(),
                RequiredReviews = ["Killjoy", "Conscience", "HumanOwner"]
            },
            new()
            {
                ProposalId = $"memory-proposal-{Guid.NewGuid():N}"[..32],
                ActionType = "CreateDecisionCandidate",
                TargetDocumentId = "MEMORY_AUTHORITY_KEY_POLICY",
                Title = "Define when a memory agent may receive stronger authority",
                Summary = "Create a future decision candidate for key-grant criteria: repeated clean proposals, no noisy context bloat, human approval, and reversible staging.",
                RecommendedDisposition = "ObservationOnly",
                MemoryAuthorityImpact = "None",
                TargetLanguage = targetLanguage,
                TargetStack = targetStack,
                EvidenceRefs = evidenceRefs.Take(3).ToArray(),
                RequiredReviews = ["Conscience", "HumanOwner"]
            }
        };

        return new MemoryImprovementProposal
        {
            ProposalBatchId = $"memory-improvement-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..50],
            ObservedProject = observedProject,
            AffectedProject = affectedProject,
            MaxContextTokens = maxContextTokens,
            ContextTokensEstimated = context.EstimatedTokens,
            MaxProposalsPerRun = maxProposals,
            ContextRefsUsed = context.ContextRefs,
            Proposals = proposals.Take(maxProposals).ToArray(),
            RejectedNoisyInputs = context.RejectedNoisyInputs,
            MemoryHealthScore = context.EstimatedTokens <= maxContextTokens ? "Cautious" : "ContextTooLarge",
            LessonsLearnedSummary = "Doubt/Killjoy evidence can improve memory, but only as staged proposals with small context and explicit evidence refs.",
            AuthorityKeyReadiness = new MemoryAuthorityKeyReadiness
            {
                ReadyForAcceptedMemoryKey = false,
                CurrentAuthorityLevel = "ProposalOnly",
                RequiredBeforeKey = "Repeated low-noise staged proposals, explicit human approval, reversible versioning, and Conscience/Killjoy review.",
                MissingEvidence = [
                    "No long-run precision/recall evidence for memory proposals yet.",
                    "No accepted-memory rollback proof for autonomous memory updates.",
                    "No human-approved key-grant policy exists."
                ]
            },
            Boundary = ProposalBoundary
        };
    }

    private static MemoryImprovementContext BuildContext(
        string runTracePath,
        string doubtFindings,
        string killjoyReview,
        string promotionOutcome,
        IReadOnlyList<string> evidenceRefs,
        int maxContextTokens)
    {
        var contextParts = new List<string>();
        var contextRefs = new List<string>();
        var rejected = new List<string>();

        if (!string.IsNullOrWhiteSpace(runTracePath))
        {
            contextRefs.Add(runTracePath);
            if (File.Exists(runTracePath))
                contextParts.Add(Truncate(File.ReadAllText(runTracePath), 4000));
            else
                rejected.Add($"Trace path not found: {runTracePath}");
        }

        AddContext("doubt_findings", doubtFindings, contextParts, contextRefs);
        AddContext("killjoy_review", killjoyReview, contextParts, contextRefs);
        AddContext("promotion_outcome", promotionOutcome, contextParts, contextRefs);
        foreach (var evidenceRef in evidenceRefs.Take(6))
            contextRefs.Add(evidenceRef);

        var contextText = string.Join(Environment.NewLine, contextParts);
        var estimatedTokens = Math.Max(1, contextText.Length / 4);
        if (estimatedTokens > maxContextTokens)
            rejected.Add("Context was over budget; only clipped trace/review summaries were used.");

        return new MemoryImprovementContext(
            estimatedTokens,
            contextRefs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            rejected);
    }

    private static void AddContext(string name, string value, List<string> contextParts, List<string> contextRefs)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        contextRefs.Add(name);
        contextParts.Add(Truncate(value, 2500));
    }

    private static string BuildPrompt(
        string observedProject,
        string affectedProject,
        string targetLanguage,
        string targetStack,
        MemoryImprovementContext context,
        int maxProposals) =>
        $"""
        You are MemoryImprovementAgent for IronDev/IDA.
        Propose at most {maxProposals} staged memory improvements from completed-run evidence.
        Observed project: {observedProject}
        Affected project: {affectedProject}
        Target language: {targetLanguage}
        Target stack: {targetStack}
        Context refs: {string.Join("; ", context.ContextRefs)}
        Estimated context tokens: {context.EstimatedTokens}
        Never mutate accepted memory. Never create tickets. Never patch files. Return proposal-only JSON.
        """;

    private static string ReadInput(AgentRequest request, string key, string defaultValue) =>
        request.Inputs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;

    private static IReadOnlyList<string> SplitInput(string value) =>
        value.Split(['|', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static int ReadIntInput(AgentRequest request, string key, int defaultValue) =>
        request.Inputs.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
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
            ? "Live model call did not return usable content; deterministic memory proposal remained in force."
            : "No live model response supplied; deterministic memory proposal was used for this governed smoke.";
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private sealed record MemoryImprovementContext(
        int EstimatedTokens,
        IReadOnlyList<string> ContextRefs,
        IReadOnlyList<string> RejectedNoisyInputs);
}
