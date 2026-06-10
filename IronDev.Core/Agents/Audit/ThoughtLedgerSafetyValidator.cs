using IronDev.Core.Agents;

namespace IronDev.Core.Agents.Audit;

public sealed class ThoughtLedgerSafetyValidator
{
    public const string EntryIdRequired = "THOUGHT_LEDGER_ENTRY_ID_REQUIRED";
    public const string AgentRunIdRequired = "THOUGHT_LEDGER_AGENT_RUN_ID_REQUIRED";
    public const string EntryTypeInvalid = "THOUGHT_LEDGER_ENTRY_TYPE_INVALID";
    public const string SummaryRequired = "THOUGHT_LEDGER_SUMMARY_REQUIRED";
    public const string EvidenceRequired = "THOUGHT_LEDGER_EVIDENCE_REQUIRED";
    public const string RawPrivateReasoningBlocked = "THOUGHT_LEDGER_RAW_PRIVATE_REASONING_BLOCKED";
    public const string AuthorityClaimBlocked = "THOUGHT_LEDGER_AUTHORITY_CLAIM_BLOCKED";
    public const string ApprovalClaimBlocked = "THOUGHT_LEDGER_APPROVAL_CLAIM_BLOCKED";
    public const string MemoryPromotionClaimBlocked = "THOUGHT_LEDGER_MEMORY_PROMOTION_CLAIM_BLOCKED";

    private static readonly IReadOnlySet<ThoughtLedgerEntryType> EvidenceRequiredTypes = new HashSet<ThoughtLedgerEntryType>
    {
        ThoughtLedgerEntryType.DecisionRationale,
        ThoughtLedgerEntryType.EvidenceUsed,
        ThoughtLedgerEntryType.BoundaryDecision,
        ThoughtLedgerEntryType.OutputRationale
    };

    public IReadOnlyList<AgentDefinitionValidationIssue> Validate(IReadOnlyList<ThoughtLedgerEntry> entries)
    {
        var issues = new List<AgentDefinitionValidationIssue>();

        foreach (var entry in entries)
            Validate(entry, issues);

        return issues;
    }

    public IReadOnlyList<AgentDefinitionValidationIssue> Validate(ThoughtLedgerEntry entry)
    {
        var issues = new List<AgentDefinitionValidationIssue>();
        Validate(entry, issues);
        return issues;
    }

    private static void Validate(ThoughtLedgerEntry entry, List<AgentDefinitionValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(entry.ThoughtLedgerEntryId))
            AddError(issues, EntryIdRequired, "ThoughtLedgerEntryId is required.");

        if (string.IsNullOrWhiteSpace(entry.AgentRunId))
            AddError(issues, AgentRunIdRequired, "AgentRunId is required.");

        if (!Enum.IsDefined(entry.EntryType))
            AddError(issues, EntryTypeInvalid, "ThoughtLedgerEntryType is invalid.");

        if (string.IsNullOrWhiteSpace(entry.Summary))
            AddError(issues, SummaryRequired, "Summary is required.");

        if (EvidenceRequiredTypes.Contains(entry.EntryType) && entry.EvidenceRefs.Count == 0)
            AddError(issues, EvidenceRequired, $"Thought ledger entry type '{entry.EntryType}' requires evidence references.");

        if (entry.ContainsRawPrivateReasoning)
            AddError(issues, RawPrivateReasoningBlocked, "ThoughtLedger entries cannot contain raw private reasoning.");

        if (entry.GrantsAuthority)
            AddError(issues, AuthorityClaimBlocked, "ThoughtLedger entries cannot grant authority.");

        if (entry.GrantsApproval)
            AddError(issues, ApprovalClaimBlocked, "ThoughtLedger entries cannot grant approval.");

        if (entry.GrantsMemoryPromotion)
            AddError(issues, MemoryPromotionClaimBlocked, "ThoughtLedger entries cannot promote memory.");

        var textValues = new List<string?> { entry.Summary };
        textValues.AddRange(entry.EvidenceRefs);
        textValues.AddRange(entry.Assumptions);
        textValues.AddRange(entry.RejectedAlternatives);
        textValues.AddRange(entry.Risks);
        textValues.AddRange(entry.RequiredFollowUps);

        if (AgentAuditTextSafety.ContainsRawPrivateReasoning(textValues))
            AddError(issues, RawPrivateReasoningBlocked, "ThoughtLedger entries cannot store raw prompts, completions, scratchpads, hidden deliberation, or private reasoning.");

        if (AgentAuditTextSafety.ContainsApprovalClaim(textValues))
            AddError(issues, ApprovalClaimBlocked, "ThoughtLedger entries cannot claim approval.");

        if (AgentAuditTextSafety.ContainsMemoryPromotionClaim(textValues))
            AddError(issues, MemoryPromotionClaimBlocked, "ThoughtLedger entries cannot claim memory promotion.");

        if (AgentAuditTextSafety.ContainsAuthorityClaim(textValues))
            AddError(issues, AuthorityClaimBlocked, "ThoughtLedger entries cannot create authority.");
    }

    private static void AddError(List<AgentDefinitionValidationIssue> issues, string code, string message) =>
        issues.Add(new AgentDefinitionValidationIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message
        });
}

internal static class AgentAuditTextSafety
{
    private static readonly IReadOnlyList<string> RawPrivateReasoningMarkers =
    [
        "chain" + "of" + "thought",
        "raw" + "prompt",
        "raw" + "completion",
        "scratch" + "pad",
        "private" + "reasoning",
        "hidden" + "deliberation",
        "system" + "prompt",
        "developer" + "prompt"
    ];

    private static readonly IReadOnlyList<string> ApprovalClaimMarkers =
    [
        "i approve",
        "i authorize",
        "approval granted",
        "approved for execution",
        "human approved",
        "grant approval"
    ];

    private static readonly IReadOnlyList<string> MemoryPromotionClaimMarkers =
    [
        "promoted memory",
        "promote memory",
        "accepted memory",
        "system rule"
    ];

    private static readonly IReadOnlyList<string> AuthorityClaimMarkers =
    [
        "grant authority",
        "authoritative for action",
        "policy cleared",
        "bypass governance",
        "override policy"
    ];

    public static bool ContainsRawPrivateReasoning(IEnumerable<string?> values) => ContainsAny(values, RawPrivateReasoningMarkers);

    public static bool ContainsApprovalClaim(IEnumerable<string?> values) => ContainsAny(values, ApprovalClaimMarkers);

    public static bool ContainsMemoryPromotionClaim(IEnumerable<string?> values) => ContainsAny(values, MemoryPromotionClaimMarkers);

    public static bool ContainsAuthorityClaim(IEnumerable<string?> values) => ContainsAny(values, AuthorityClaimMarkers);

    private static bool ContainsAny(IEnumerable<string?> values, IReadOnlyList<string> markers) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value) &&
                            markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));
}
