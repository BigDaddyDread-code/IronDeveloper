using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class MemoryImprovementAgent : StaticIronDevAgent
{
    private const int DefaultMaxContextTokens = 2400;
    private const int DefaultMaxProposals = 3;
    private const string ProposalBoundary = "MemoryImprovementAgent is Level1ProposalOnly. It may recommend staging, but it cannot write staging memory, mutate accepted memory, create tickets, patch files, or approve itself.";
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
        var sourceEvidence = BuildEvidenceRefs(evidenceRefs, context);
        var firstProposalId = $"memory-proposal-{Guid.NewGuid():N}"[..32];
        var secondProposalId = $"memory-proposal-{Guid.NewGuid():N}"[..32];
        var thirdProposalId = $"memory-proposal-{Guid.NewGuid():N}"[..32];
        var firstBundle = BuildEvidenceBundle(
            firstProposalId,
            "High/critical adversarial findings must receive explicit Killjoy rebuttal before promotion.",
            sourceEvidence,
            missingEvidence: []);
        var secondBundle = BuildEvidenceBundle(
            secondProposalId,
            "MemoryImprovementAgent must remain proposal-only until the memory key gate has enough reviewed outcomes.",
            sourceEvidence,
            missingEvidence: ["No human acceptance history for memory proposals exists yet."]);
        var thirdBundle = BuildEvidenceBundle(
            thirdProposalId,
            "Future memory authority changes need a measurable key gate instead of ad hoc trust.",
            sourceEvidence,
            missingEvidence: ["No long-run proposal precision score exists yet.", "No retrieval improvement proof exists yet."]);
        var proposals = new List<MemoryImprovementAction>
        {
            new()
            {
                ProposalId = firstProposalId,
                ActionType = "CreateObservation",
                TargetDocumentId = "ADVERSARIAL_REVIEW_OBSERVATION_183",
                Title = "Capture adversarial review lessons without changing accepted memory",
                Summary = "Stage an observation that high/critical DoubtAgent findings require explicit Killjoy rebuttal before promotion.",
                RecommendedDisposition = "Stage",
                MemoryAuthorityImpact = "None",
                TargetLanguage = targetLanguage,
                TargetStack = targetStack,
                EvidenceRefs = firstBundle.EvidenceRefs.Select(item => item.Source).ToArray(),
                EvidenceBundle = firstBundle,
                RequiredReviews = ["Killjoy", "Conscience", "HumanOrCodex"]
            },
            new()
            {
                ProposalId = secondProposalId,
                ActionType = "UpdateDraftMemory",
                TargetDocumentId = "AGENTS",
                Title = "Clarify MemoryImprovementAgent remains proposal-only",
                Summary = "Update draft agent documentation to say MemoryImprovementAgent starts at Level 1 ProposalOnly and cannot receive accepted-memory authority during Alpha.",
                RecommendedDisposition = "NeedsHumanReview",
                MemoryAuthorityImpact = "DraftOnly",
                TargetLanguage = targetLanguage,
                TargetStack = targetStack,
                EvidenceRefs = secondBundle.EvidenceRefs.Select(item => item.Source).ToArray(),
                EvidenceBundle = secondBundle,
                RequiredReviews = ["Killjoy", "Conscience", "HumanOwner"]
            },
            new()
            {
                ProposalId = thirdProposalId,
                ActionType = "CreateDecisionCandidate",
                TargetDocumentId = "MEMORY_AUTHORITY_KEY_POLICY",
                Title = "Define when a memory agent may receive stronger authority",
                Summary = "Create a future decision candidate for key-grant criteria: reviewed outcomes, evidence refs, no unsafe proposals, no duplicate spam, human approval, and retrieval improvement proof.",
                RecommendedDisposition = "ObservationOnly",
                MemoryAuthorityImpact = "None",
                TargetLanguage = targetLanguage,
                TargetStack = targetStack,
                EvidenceRefs = thirdBundle.EvidenceRefs.Select(item => item.Source).ToArray(),
                EvidenceBundle = thirdBundle,
                RequiredReviews = ["Conscience", "HumanOwner"]
            }
        };
        var selectedProposals = proposals.Take(maxProposals).ToArray();
        var selectedBundles = selectedProposals
            .Select(proposal => proposal.EvidenceBundle)
            .OfType<MemoryProposalEvidenceBundle>()
            .ToArray();
        var metrics = BuildAuditMetrics(selectedProposals, context, maxContextTokens);
        var keyGate = ReviewKeyRequest(metrics, sourceEvidence);

        return new MemoryImprovementProposal
        {
            ProposalBatchId = $"memory-improvement-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..50],
            ObservedProject = observedProject,
            AffectedProject = affectedProject,
            MaxContextTokens = maxContextTokens,
            ContextTokensEstimated = context.EstimatedTokens,
            MaxProposalsPerRun = maxProposals,
            ContextRefsUsed = context.ContextRefs,
            Proposals = selectedProposals,
            EvidenceBundles = selectedBundles,
            RejectedNoisyInputs = context.RejectedNoisyInputs,
            MemoryHealthScore = context.EstimatedTokens <= maxContextTokens ? "Cautious" : "ContextTooLarge",
            LessonsLearnedSummary = "Doubt/Killjoy evidence can improve memory, but MemoryImprovementAgent earns more keys only from reviewed outcomes, not self-assessment.",
            AuthorityKeyReadiness = new MemoryAuthorityKeyReadiness
            {
                ReadyForAcceptedMemoryKey = false,
                CurrentPermissionLevel = MemoryImprovementPermissionLevel.ProposalOnly,
                CurrentAuthorityLevel = "Level1ProposalOnly",
                RequiredBeforeKey = "Pass MemoryKeyGate with repeated reviewed proposals, explicit evidence refs, human acceptance history, low duplicate/noise rate, retrieval improvement proof, reversible staging, and Conscience/Killjoy review.",
                MissingEvidence = [
                    "No long-run precision/recall evidence for memory proposals yet.",
                    "No accepted-memory rollback proof for autonomous memory updates.",
                    "No human-approved key-grant policy exists.",
                    "No proposal acceptance/rejection audit history exists yet."
                ]
            },
            KeyGateReview = keyGate,
            Boundary = ProposalBoundary
        };
    }

    private static IReadOnlyList<MemoryImprovementEvidenceRef> BuildEvidenceRefs(
        IReadOnlyList<string> evidenceRefs,
        MemoryImprovementContext context)
    {
        var refs = evidenceRefs
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(6)
            .Select((source, index) => new MemoryImprovementEvidenceRef
            {
                EvidenceId = $"evidence-{index + 1}",
                EvidenceType = ClassifyEvidenceType(source),
                Source = source,
                Summary = $"External governed evidence reference: {source}",
                IsAuthoritativeEvidence = IsAuthoritativeEvidence(source)
            })
            .ToList();

        if (refs.Count == 0 && context.ContextRefs.Count > 0)
        {
            refs.AddRange(context.ContextRefs.Take(3).Select((source, index) => new MemoryImprovementEvidenceRef
            {
                EvidenceId = $"context-evidence-{index + 1}",
                EvidenceType = ClassifyEvidenceType(source),
                Source = source,
                Summary = $"Focused context reference: {source}",
                IsAuthoritativeEvidence = IsAuthoritativeEvidence(source)
            }));
        }

        return refs;
    }

    private static MemoryProposalEvidenceBundle BuildEvidenceBundle(
        string proposalId,
        string claim,
        IReadOnlyList<MemoryImprovementEvidenceRef> evidenceRefs,
        IReadOnlyList<string> missingEvidence) =>
        new()
        {
            ProposalId = proposalId,
            Claim = claim,
            EvidenceRefs = evidenceRefs.Take(4).ToArray(),
            MissingEvidence = missingEvidence,
            EvidenceBoundary = "Evidence must come from run traces, tests, reviews, human outcomes, retrieval traces, or code/index facts. MemoryImprovementAgent self-assessment does not count."
        };

    private static MemoryProposalAuditMetrics BuildAuditMetrics(
        IReadOnlyList<MemoryImprovementAction> proposals,
        MemoryImprovementContext context,
        int maxContextTokens)
    {
        var missingEvidenceCount = proposals.Count(proposal =>
            proposal.EvidenceBundle is null ||
            proposal.EvidenceBundle.EvidenceRefs.Count == 0 ||
            proposal.EvidenceBundle.MissingEvidence.Count > 0);

        return new MemoryProposalAuditMetrics
        {
            ProposalCount = proposals.Count,
            AcceptedByHumanCount = 0,
            RejectedByHumanCount = 0,
            EditedByHumanCount = 0,
            UnsafeProposalCount = proposals.Count(proposal => proposal.MemoryAuthorityImpact == "UpdatesAcceptedMemory"),
            DuplicateProposalCount = 0,
            MissingEvidenceCount = missingEvidenceCount,
            KilljoyApprovalRate = 0m,
            HumanAcceptanceRate = 0m,
            ContextBudgetHealthy = context.EstimatedTokens <= maxContextTokens,
            RetrievalImprovementProven = false
        };
    }

    private static MemoryKeyGateReview ReviewKeyRequest(
        MemoryProposalAuditMetrics metrics,
        IReadOnlyList<MemoryImprovementEvidenceRef> evidenceRefs)
    {
        var reasons = new List<string>
        {
            "MemoryImprovementAgent is currently Level1ProposalOnly.",
            "No human acceptance history has been recorded for memory proposals.",
            "No retrieval improvement proof has been recorded after accepted memory changes."
        };
        if (metrics.UnsafeProposalCount > 0)
            reasons.Add("Unsafe proposal count must be zero before any key increase.");
        if (!metrics.ContextBudgetHealthy)
            reasons.Add("Context budget must remain healthy before any key increase.");

        return new MemoryKeyGateReview
        {
            ReviewId = $"memory-key-gate-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..48],
            CurrentLevel = MemoryImprovementPermissionLevel.ProposalOnly,
            CurrentLevelName = "Level1ProposalOnly",
            RequestedLevel = MemoryImprovementPermissionLevel.StagingAreaWrite,
            RequestedLevelName = "Level2StagingAreaWrite",
            Decision = "NeedsMoreEvidence",
            PrecisionScore = 0m,
            Metrics = metrics,
            EvidenceSourcesReviewed = evidenceRefs.Select(item => item.Source).ToArray(),
            Reasons = reasons,
            RequiredNextEvidence = [
                "At least 10 proposal-only runs with explicit evidence refs.",
                "Zero unsafe proposals.",
                "Killjoy well-formed approval history.",
                "Human accepted/rejected/edited outcome history.",
                "Duplicate proposal count near zero.",
                "Proof that accepted reviewed memory improves future retrieval."
            ],
            Boundary = "MemoryKeyGate can recommend permission changes only. It does not grant accepted-memory write authority."
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

    private static string ClassifyEvidenceType(string source)
    {
        var value = source.ToLowerInvariant();
        if (value.Contains("trace"))
            return "RunTrace";
        if (value.Contains("test-agent") || value.Contains("test"))
            return "TestEvidence";
        if (value.Contains("code-standards") || value.Contains("killjoy") || value.Contains("quality"))
            return "KilljoyEvidence";
        if (value.Contains("memory") || value.Contains("docs/") || value.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return "MemoryEvidence";
        if (value.Contains("report"))
            return "RunReport";
        return "EvidenceRef";
    }

    private static bool IsAuthoritativeEvidence(string source)
    {
        var value = source.ToLowerInvariant();
        return value.Contains("runs/") ||
               value.Contains("trace") ||
               value.Contains("report") ||
               value.Contains("test-agent") ||
               value.Contains("code-standards") ||
               value.Contains("killjoy");
    }

    private sealed record MemoryImprovementContext(
        int EstimatedTokens,
        IReadOnlyList<string> ContextRefs,
        IReadOnlyList<string> RejectedNoisyInputs);
}
