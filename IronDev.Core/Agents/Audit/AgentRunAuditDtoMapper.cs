using IronDev.Core.Agents;

namespace IronDev.Core.Agents.Audit;

public static class AgentRunAuditDtoMapper
{
    private const string RedactedUnsafeAuditText = "[redacted unsafe audit text]";

    private static readonly string[] RawPrivateReasoningMarkers =
    [
        "raw" + "prompt",
        "raw" + "completion",
        "chain" + "of" + "thought",
        "scratch" + "pad",
        "private" + "reasoning",
        "hidden" + "reasoning",
        "hidden" + "deliberation",
        "system" + "prompt",
        "developer" + "prompt"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "authoritative for action",
        "grant authority",
        "creates authority",
        "policy cleared",
        "bypass governance"
    ];

    private static readonly string[] ApprovalMarkers =
    [
        "approval granted",
        "human approved",
        "approved for execution",
        "i approve",
        "i authorize"
    ];

    private static readonly string[] MemoryPromotionMarkers =
    [
        "accepted memory",
        "promoted memory",
        "promote memory",
        "collective memory promotion"
    ];

    public static AgentRunListItemDto ToListItem(AgentRunAuditEnvelope envelope)
    {
        var summary = ToSafetySummary(envelope);
        var blockedCapabilityCount = envelope.CapabilityUses.Count(capability => capability.Outcome == AgentCapabilityUseOutcome.Blocked);

        return new AgentRunListItemDto
        {
            AgentRunId = envelope.Run.AgentRunId,
            AgentId = envelope.Run.AgentId,
            AgentName = envelope.Run.AgentName,
            AgentKind = envelope.AgentDefinitionSnapshot.Kind,
            ExecutionMode = envelope.AgentDefinitionSnapshot.ExecutionMode,
            Status = envelope.Run.Status,
            TriggerType = envelope.Run.TriggerType,
            CreatedAtUtc = envelope.Run.CreatedAtUtc,
            CompletedAtUtc = envelope.Run.CompletedAtUtc,
            RequestedByUserId = envelope.Run.RequestedByUserId,
            CorrelationId = envelope.Run.RunId,
            InputCount = envelope.Inputs.Count,
            OutputCount = envelope.Outputs.Count,
            ThoughtLedgerCount = envelope.ThoughtLedger.Count,
            CapabilityUseCount = envelope.CapabilityUses.Count,
            BoundaryDecisionCount = envelope.BoundaryDecisions.Count,
            BlockedCapabilityCount = blockedCapabilityCount,
            HasBoundaryBlocks = summary.HasBoundaryBlock,
            HasUnsafeAttempt = summary.ContainsRawPrivateReasoning ||
                summary.HasAuthorityClaim ||
                summary.HasApprovalClaim ||
                summary.HasMemoryPromotionClaim ||
                summary.HasRuntimeActionOutput ||
                summary.HasAuthorityCreatingOutput ||
                summary.HasBlockedCapabilityAttempt ||
                summary.HasBoundaryBlock,
            HasRawPrivateReasoning = summary.ContainsRawPrivateReasoning,
            HasAuthorityClaim = summary.HasAuthorityClaim,
            HasApprovalClaim = summary.HasApprovalClaim,
            HasMemoryPromotionClaim = summary.HasMemoryPromotionClaim
        };
    }

    public static AgentRunDetailDto ToDetail(AgentRunAuditEnvelope envelope) =>
        new()
        {
            Run = ToRun(envelope.Run),
            AgentDefinition = ToAgentDefinition(envelope.AgentDefinitionSnapshot),
            Inputs = envelope.Inputs.Select(ToInput).ToArray(),
            Outputs = envelope.Outputs.Select(ToOutput).ToArray(),
            CapabilityUses = envelope.CapabilityUses.Select(ToCapabilityUse).ToArray(),
            BoundaryDecisions = envelope.BoundaryDecisions.Select(ToBoundaryDecision).ToArray(),
            ThoughtLedger = envelope.ThoughtLedger.Select(ToThoughtLedger).ToArray(),
            Steps = envelope.Steps.Select(ToStep).ToArray(),
            SafetySummary = ToSafetySummary(envelope)
        };

    public static AgentRunSafetySummaryDto ToSafetySummary(AgentRunAuditEnvelope envelope)
    {
        var containsRaw = envelope.Inputs.Any(input => input.ContainsRawPrivateReasoning || ContainsRawMarker(input.Summary) || ContainsRawMarker(input.Source)) ||
            envelope.Outputs.Any(output => output.ContainsRawPrivateReasoning || ContainsRawMarker(output.Summary)) ||
            envelope.Steps.Any(step => step.ContainsRawPrivateReasoning || ContainsRawMarker(step.Summary)) ||
            envelope.ThoughtLedger.Any(thought => thought.ContainsRawPrivateReasoning || ContainsRawMarker(thought.Summary)) ||
            envelope.BoundaryDecisions.Any(boundary => ContainsRawMarker(boundary.Reason));

        var hasAuthority = envelope.Outputs.Any(output => output.CreatesAuthority || ContainsAny(output.Summary, AuthorityMarkers)) ||
            envelope.BoundaryDecisions.Any(boundary => boundary.GrantsAuthority || ContainsAny(boundary.Reason, AuthorityMarkers)) ||
            envelope.ThoughtLedger.Any(thought => thought.GrantsAuthority || ContainsAny(thought.Summary, AuthorityMarkers));

        var hasApproval = envelope.BoundaryDecisions.Any(boundary => boundary.GrantsHumanApproval || boundary.GrantsPolicyApproval || ContainsAny(boundary.Reason, ApprovalMarkers)) ||
            envelope.ThoughtLedger.Any(thought => thought.GrantsApproval || ContainsAny(thought.Summary, ApprovalMarkers));

        var hasMemoryPromotion = envelope.BoundaryDecisions.Any(boundary => boundary.GrantsMemoryPromotion || ContainsAny(boundary.Reason, MemoryPromotionMarkers)) ||
            envelope.ThoughtLedger.Any(thought => thought.GrantsMemoryPromotion || ContainsAny(thought.Summary, MemoryPromotionMarkers));

        var hasRuntimeAction = envelope.Outputs.Any(output => output.CreatesRuntimeAction);
        var hasAuthorityOutput = envelope.Outputs.Any(output => output.CreatesAuthority);
        var hasBlockedCapability = envelope.CapabilityUses.Any(capability => capability.Outcome == AgentCapabilityUseOutcome.Blocked);
        var hasBoundaryBlock = envelope.BoundaryDecisions.Any(IsBoundaryBlock);
        var warnings = new List<string>();

        if (hasBlockedCapability)
            warnings.Add("Run contains blocked capability attempts.");
        if (hasBoundaryBlock)
            warnings.Add("Run contains boundary block decisions.");
        if (envelope.Outputs.Any(output => output.IsReviewOnly))
            warnings.Add("Run is review-only.");
        if (envelope.Outputs.Any(output => output.IsProposalOnly))
            warnings.Add("Run is proposal-only.");
        if (!hasAuthority && !hasAuthorityOutput)
            warnings.Add("Run does not create authority.");
        if (containsRaw)
            warnings.Add("Run contains redacted unsafe audit text.");

        return new AgentRunSafetySummaryDto
        {
            ContainsRawPrivateReasoning = containsRaw,
            HasAuthorityClaim = hasAuthority,
            HasApprovalClaim = hasApproval,
            HasMemoryPromotionClaim = hasMemoryPromotion,
            HasRuntimeActionOutput = hasRuntimeAction,
            HasAuthorityCreatingOutput = hasAuthorityOutput,
            HasBlockedCapabilityAttempt = hasBlockedCapability,
            HasBoundaryBlock = hasBoundaryBlock,
            Warnings = warnings
        };
    }

    private static AgentRunRecordDto ToRun(AgentRunRecord run) =>
        new()
        {
            AgentRunId = run.AgentRunId,
            TenantId = run.TenantId,
            ProjectId = run.ProjectId,
            CampaignId = run.CampaignId,
            RunId = run.RunId,
            AgentId = run.AgentId,
            AgentName = run.AgentName,
            Status = run.Status,
            TriggerType = run.TriggerType,
            CreatedAtUtc = run.CreatedAtUtc,
            StartedAtUtc = run.StartedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc,
            RequestedByUserId = run.RequestedByUserId,
            RequestedByAgentId = run.RequestedByAgentId,
            RequestSummary = Safe(run.RequestSummary),
            Purpose = Safe(run.Purpose)
        };

    private static AgentDefinitionSnapshotDto ToAgentDefinition(AgentDefinition definition) =>
        new()
        {
            AgentId = definition.AgentId,
            Name = definition.Name,
            Kind = definition.Kind,
            ExecutionMode = definition.ExecutionMode,
            Capabilities = definition.Capabilities?.OrderBy(capability => capability).ToArray() ?? [],
            ForbiddenCapabilities = definition.ForbiddenCapabilities?.OrderBy(capability => capability).ToArray() ?? [],
            PersonaDisplayName = Safe(definition.Persona?.DisplayName ?? string.Empty),
            Purpose = Safe(definition.Purpose)
        };

    private static AgentRunInputRefDto ToInput(AgentRunInputRef input) =>
        new()
        {
            InputRefId = input.InputRefId,
            RefType = input.RefType,
            RefId = input.RefId,
            Source = Safe(input.Source),
            Summary = Safe(input.Summary),
            EvidenceRefs = string.IsNullOrWhiteSpace(input.Sha256) ? [] : [input.Sha256],
            IsAuthoritativeForAction = input.IsAuthoritativeForAction,
            ContainsRawPrivateReasoning = input.ContainsRawPrivateReasoning || ContainsRawMarker(input.Summary) || ContainsRawMarker(input.Source)
        };

    private static AgentRunOutputRefDto ToOutput(AgentRunOutputRef output) =>
        new()
        {
            OutputRefId = output.OutputRefId,
            RefType = output.RefType,
            RefId = output.RefId,
            Summary = Safe(output.Summary),
            EvidenceRefs = output.EvidenceRefs,
            IsReviewOnly = output.IsReviewOnly,
            IsProposalOnly = output.IsProposalOnly,
            CreatesAuthority = output.CreatesAuthority,
            CreatesRuntimeAction = output.CreatesRuntimeAction,
            ContainsRawPrivateReasoning = output.ContainsRawPrivateReasoning || ContainsRawMarker(output.Summary)
        };

    private static AgentCapabilityUseDto ToCapabilityUse(AgentCapabilityUseRecord capability) =>
        new()
        {
            CapabilityUseId = capability.CapabilityUseId,
            Capability = capability.Capability,
            Outcome = capability.Outcome,
            Summary = Safe(capability.Summary),
            BoundaryDecisionId = capability.BoundaryDecisionId,
            EvidenceRef = capability.EvidenceRef,
            WasDeclaredOnAgent = capability.WasDeclaredOnAgent,
            WasForbiddenOnAgent = capability.WasForbiddenOnAgent
        };

    private static AgentBoundaryDecisionDto ToBoundaryDecision(AgentBoundaryDecision decision) =>
        new()
        {
            BoundaryDecisionId = decision.BoundaryDecisionId,
            BoundaryType = decision.BoundaryType,
            Decision = Safe(decision.Decision),
            Reason = Safe(decision.Reason),
            SourceRefId = decision.SourceRefId,
            EvidenceRefs = decision.EvidenceRefs,
            GrantsAuthority = decision.GrantsAuthority,
            GrantsHumanApproval = decision.GrantsHumanApproval,
            GrantsPolicyApproval = decision.GrantsPolicyApproval,
            GrantsMemoryPromotion = decision.GrantsMemoryPromotion
        };

    private static ThoughtLedgerEntryDto ToThoughtLedger(ThoughtLedgerEntry thought) =>
        new()
        {
            ThoughtLedgerEntryId = thought.ThoughtLedgerEntryId,
            EntryType = thought.EntryType,
            Summary = Safe(thought.Summary),
            EvidenceRefs = thought.EvidenceRefs,
            Assumptions = thought.Assumptions.Select(Safe).ToArray(),
            RejectedAlternatives = thought.RejectedAlternatives.Select(Safe).ToArray(),
            Risks = thought.Risks.Select(Safe).ToArray(),
            RequiredFollowUps = thought.RequiredFollowUps.Select(Safe).ToArray(),
            ContainsRawPrivateReasoning = thought.ContainsRawPrivateReasoning || ContainsRawMarker(thought.Summary),
            GrantsAuthority = thought.GrantsAuthority,
            GrantsApproval = thought.GrantsApproval,
            GrantsMemoryPromotion = thought.GrantsMemoryPromotion,
            RecordedAtUtc = thought.RecordedAtUtc
        };

    private static AgentRunStepDto ToStep(AgentRunStep step) =>
        new()
        {
            StepId = step.StepId,
            Sequence = step.Sequence,
            StepType = step.StepType,
            OccurredAtUtc = step.OccurredAtUtc,
            Summary = Safe(step.Summary),
            EvidenceRefs = step.EvidenceRefs,
            ContainsRawPrivateReasoning = step.ContainsRawPrivateReasoning || ContainsRawMarker(step.Summary)
        };

    private static bool IsBoundaryBlock(AgentBoundaryDecision decision) =>
        string.Equals(decision.Decision, "block", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(decision.Decision, "blocked", StringComparison.OrdinalIgnoreCase);

    private static string Safe(string value) =>
        ContainsRawMarker(value) ? RedactedUnsafeAuditText : value;

    private static bool ContainsRawMarker(string value) =>
        ContainsAny(value, RawPrivateReasoningMarkers);

    private static bool ContainsAny(string value, IReadOnlyList<string> markers) =>
        !string.IsNullOrWhiteSpace(value) &&
        markers.Any(marker => value.Replace("-", string.Empty, StringComparison.Ordinal).Contains(marker, StringComparison.OrdinalIgnoreCase));
}
