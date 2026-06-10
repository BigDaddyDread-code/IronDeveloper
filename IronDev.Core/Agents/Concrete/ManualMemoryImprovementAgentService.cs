using Audit = IronDev.Core.Agents.Audit;
using AuditThoughtLedgerEntry = IronDev.Core.Agents.Audit.ThoughtLedgerEntry;

namespace IronDev.Core.Agents.Concrete;

public sealed record ManualMemoryImprovementInputRef
{
    public required string InputRefId { get; init; }
    public required string RefType { get; init; }
    public required string RefId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool IsAuthoritativeForAction { get; init; }
}

public sealed record ManualMemoryImprovementPatternDraft
{
    public required MemoryImprovementPatternType PatternType { get; init; }
    public required string Summary { get; init; }
    public required decimal Confidence { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> RelatedMemoryIds { get; init; } = [];
    public IReadOnlyList<string> RelatedProposalIds { get; init; } = [];
    public bool IsDuplicateCandidate { get; init; }
    public bool RequiresHumanReview { get; init; } = true;
}

public sealed record ManualMemoryImprovementProposalDraftInput
{
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Rationale { get; init; }
    public required int SourcePatternIndex { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsProposalOnly { get; init; } = true;
    public bool CreatesCollectiveMemory { get; init; }
    public bool PromotesMemory { get; init; }
    public bool RequiresHumanReview { get; init; } = true;
}

public sealed record ManualMemoryImprovementDetectionRequest
{
    public required string DetectionRequestId { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string CampaignId { get; init; }
    public required string RunId { get; init; }
    public required string RequestedByUserId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string RequestSummary { get; init; } = string.Empty;
    public IReadOnlyList<ManualMemoryImprovementInputRef> Inputs { get; init; } = [];
    public IReadOnlyList<ManualMemoryImprovementPatternDraft> PatternDrafts { get; init; } = [];
    public IReadOnlyList<ManualMemoryImprovementProposalDraftInput> ProposalDrafts { get; init; } = [];
    public MemoryImprovementNoProposalReason? NoProposalReason { get; init; }
}

public sealed record ManualMemoryImprovementIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string? Field { get; init; }
}

public sealed record ManualMemoryImprovementDetectionResult
{
    public required string ManualMemoryImprovementRunId { get; init; }
    public required string DetectionRequestId { get; init; }
    public required bool Succeeded { get; init; }
    public MemoryImprovementDetectionResult? DetectionResult { get; init; }
    public Audit.AgentRunAuditEnvelope? AuditEnvelope { get; init; }
    public IReadOnlyList<ManualMemoryImprovementIssue> Issues { get; init; } = [];
}

public interface IManualMemoryImprovementAgentService
{
    ManualMemoryImprovementDetectionResult Detect(
        ManualMemoryImprovementDetectionRequest request,
        DateTimeOffset detectedAtUtc);
}

public sealed class ManualMemoryImprovementDetectionValidator
{
    public const string DetectionRequestIdRequired = "MANUAL_MEMORY_IMPROVEMENT_DETECTION_REQUEST_ID_REQUIRED";
    public const string ScopeRequired = "MANUAL_MEMORY_IMPROVEMENT_SCOPE_REQUIRED";
    public const string RequestedByUserIdRequired = "MANUAL_MEMORY_IMPROVEMENT_REQUESTED_BY_USER_ID_REQUIRED";
    public const string InputRequired = "MANUAL_MEMORY_IMPROVEMENT_INPUT_REQUIRED";
    public const string InputRequiredField = "MANUAL_MEMORY_IMPROVEMENT_INPUT_REQUIRED_FIELD";
    public const string PatternRequired = "MANUAL_MEMORY_IMPROVEMENT_PATTERN_REQUIRED";
    public const string NoProposalReasonInvalid = "MANUAL_MEMORY_IMPROVEMENT_NO_PROPOSAL_REASON_INVALID";
    public const string PatternTypeInvalid = "MANUAL_MEMORY_IMPROVEMENT_PATTERN_TYPE_INVALID";
    public const string PatternSummaryRequired = "MANUAL_MEMORY_IMPROVEMENT_PATTERN_SUMMARY_REQUIRED";
    public const string PatternConfidenceInvalid = "MANUAL_MEMORY_IMPROVEMENT_PATTERN_CONFIDENCE_INVALID";
    public const string PatternHumanReviewRequired = "MANUAL_MEMORY_IMPROVEMENT_PATTERN_HUMAN_REVIEW_REQUIRED";
    public const string BlankEvidenceRef = "MANUAL_MEMORY_IMPROVEMENT_BLANK_EVIDENCE_REF";
    public const string ProposalSourcePatternInvalid = "MANUAL_MEMORY_IMPROVEMENT_PROPOSAL_SOURCE_PATTERN_INVALID";
    public const string ProposalTitleRequired = "MANUAL_MEMORY_IMPROVEMENT_PROPOSAL_TITLE_REQUIRED";
    public const string ProposalSummaryRequired = "MANUAL_MEMORY_IMPROVEMENT_PROPOSAL_SUMMARY_REQUIRED";
    public const string ProposalRationaleRequired = "MANUAL_MEMORY_IMPROVEMENT_PROPOSAL_RATIONALE_REQUIRED";
    public const string ProposalEvidenceRequired = "MANUAL_MEMORY_IMPROVEMENT_PROPOSAL_EVIDENCE_REQUIRED";
    public const string ProposalOnlyRequired = "MANUAL_MEMORY_IMPROVEMENT_PROPOSAL_ONLY_REQUIRED";
    public const string CreatesCollectiveMemoryBlocked = "MANUAL_MEMORY_IMPROVEMENT_CREATES_COLLECTIVE_MEMORY_BLOCKED";
    public const string PromotesMemoryBlocked = "MANUAL_MEMORY_IMPROVEMENT_PROMOTES_MEMORY_BLOCKED";
    public const string ProposalHumanReviewRequired = "MANUAL_MEMORY_IMPROVEMENT_PROPOSAL_HUMAN_REVIEW_REQUIRED";
    public const string RawPrivateReasoningBlocked = "MANUAL_MEMORY_IMPROVEMENT_RAW_PRIVATE_REASONING_BLOCKED";
    public const string AuthorityClaimBlocked = "MANUAL_MEMORY_IMPROVEMENT_AUTHORITY_CLAIM_BLOCKED";
    public const string ApprovalClaimBlocked = "MANUAL_MEMORY_IMPROVEMENT_APPROVAL_CLAIM_BLOCKED";
    public const string MemoryPromotionClaimBlocked = "MANUAL_MEMORY_IMPROVEMENT_MEMORY_PROMOTION_CLAIM_BLOCKED";
    public const string InputAuthorityBlocked = "MANUAL_MEMORY_IMPROVEMENT_INPUT_AUTHORITY_BLOCKED";

    private static readonly IReadOnlySet<string> AuthoritativeInputTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "HumanApprovalEvidence",
        "GovernanceDecision",
        "PolicyDecision"
    };

    private static readonly IReadOnlyList<string> RawPrivateReasoningMarkers =
    [
        "raw" + "prompt",
        "raw" + "completion",
        "chain" + "of" + "thought",
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
        "policy cleared",
        "authoritative for action",
        "bypass governance",
        "override policy",
        "grant authority"
    ];

    public IReadOnlyList<ManualMemoryImprovementIssue> Validate(ManualMemoryImprovementDetectionRequest request)
    {
        var issues = new List<ManualMemoryImprovementIssue>();

        if (string.IsNullOrWhiteSpace(request.DetectionRequestId))
            AddError(issues, DetectionRequestIdRequired, "DetectionRequestId is required.", nameof(request.DetectionRequestId));

        if (string.IsNullOrWhiteSpace(request.TenantId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.CampaignId) ||
            string.IsNullOrWhiteSpace(request.RunId))
        {
            AddError(issues, ScopeRequired, "TenantId, ProjectId, CampaignId, and RunId are required.", "Scope");
        }

        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
            AddError(issues, RequestedByUserIdRequired, "RequestedByUserId is required for manual memory improvement detection.", nameof(request.RequestedByUserId));

        if (request.Inputs.Count == 0)
            AddError(issues, InputRequired, "At least one input is required.", nameof(request.Inputs));

        if (request.NoProposalReason is not null && !Enum.IsDefined(request.NoProposalReason.Value))
            AddError(issues, NoProposalReasonInvalid, "NoProposalReason is invalid.", nameof(request.NoProposalReason));

        if (request.NoProposalReason is null && request.PatternDrafts.Count == 0)
            AddError(issues, PatternRequired, "At least one pattern draft is required unless NoProposalReason is set.", nameof(request.PatternDrafts));

        ValidateTextValues(issues, "Request", [request.DetectionRequestId, request.RequestedByUserId, request.CorrelationId, request.RequestSummary]);

        foreach (var input in request.Inputs)
            ValidateInput(input, issues);

        for (var i = 0; i < request.PatternDrafts.Count; i++)
            ValidatePattern(request.PatternDrafts[i], i, issues);

        for (var i = 0; i < request.ProposalDrafts.Count; i++)
            ValidateProposal(request.ProposalDrafts[i], i, request.PatternDrafts.Count, issues);

        return issues;
    }

    private static void ValidateInput(ManualMemoryImprovementInputRef input, List<ManualMemoryImprovementIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(input.InputRefId) ||
            string.IsNullOrWhiteSpace(input.RefType) ||
            string.IsNullOrWhiteSpace(input.RefId))
        {
            AddError(issues, InputRequiredField, "InputRefId, RefType, and RefId are required.", nameof(input.InputRefId));
        }

        if (input.ContainsRawPrivateReasoning)
            AddError(issues, RawPrivateReasoningBlocked, "Inputs cannot contain raw private reasoning.", nameof(input.ContainsRawPrivateReasoning));

        if (input.IsAuthoritativeForAction && !AuthoritativeInputTypes.Contains(input.RefType))
            AddError(issues, InputAuthorityBlocked, "Manual memory-improvement inputs cannot be authoritative for action unless they are explicit human/governance evidence.", nameof(input.IsAuthoritativeForAction));

        if (input.EvidenceRefs.Any(string.IsNullOrWhiteSpace))
            AddError(issues, BlankEvidenceRef, "Evidence references cannot be blank.", nameof(input.EvidenceRefs));

        ValidateTextValues(issues, "Input", [input.InputRefId, input.RefType, input.RefId, input.Source, input.Summary, .. input.EvidenceRefs]);
    }

    private static void ValidatePattern(
        ManualMemoryImprovementPatternDraft pattern,
        int index,
        List<ManualMemoryImprovementIssue> issues)
    {
        var fieldPrefix = $"{nameof(ManualMemoryImprovementDetectionRequest.PatternDrafts)}[{index}]";

        if (!Enum.IsDefined(pattern.PatternType))
            AddError(issues, PatternTypeInvalid, "PatternType is invalid.", $"{fieldPrefix}.{nameof(pattern.PatternType)}");

        if (string.IsNullOrWhiteSpace(pattern.Summary))
            AddError(issues, PatternSummaryRequired, "Pattern Summary is required.", $"{fieldPrefix}.{nameof(pattern.Summary)}");

        if (pattern.Confidence < 0m || pattern.Confidence > 1m)
            AddError(issues, PatternConfidenceInvalid, "Pattern confidence must be between 0 and 1.", $"{fieldPrefix}.{nameof(pattern.Confidence)}");

        if (!pattern.RequiresHumanReview)
            AddError(issues, PatternHumanReviewRequired, "Pattern drafts require human review.", $"{fieldPrefix}.{nameof(pattern.RequiresHumanReview)}");

        if (pattern.EvidenceRefs.Any(string.IsNullOrWhiteSpace))
            AddError(issues, BlankEvidenceRef, "Pattern evidence references cannot be blank.", $"{fieldPrefix}.{nameof(pattern.EvidenceRefs)}");

        ValidateTextValues(
            issues,
            "Pattern",
            [pattern.Summary, .. pattern.EvidenceRefs, .. pattern.RelatedMemoryIds, .. pattern.RelatedProposalIds]);
    }

    private static void ValidateProposal(
        ManualMemoryImprovementProposalDraftInput proposal,
        int index,
        int patternCount,
        List<ManualMemoryImprovementIssue> issues)
    {
        var fieldPrefix = $"{nameof(ManualMemoryImprovementDetectionRequest.ProposalDrafts)}[{index}]";

        if (proposal.SourcePatternIndex < 0 || proposal.SourcePatternIndex >= patternCount)
            AddError(issues, ProposalSourcePatternInvalid, "Proposal SourcePatternIndex must reference a supplied pattern draft.", $"{fieldPrefix}.{nameof(proposal.SourcePatternIndex)}");

        if (string.IsNullOrWhiteSpace(proposal.Title))
            AddError(issues, ProposalTitleRequired, "Proposal Title is required.", $"{fieldPrefix}.{nameof(proposal.Title)}");

        if (string.IsNullOrWhiteSpace(proposal.Summary))
            AddError(issues, ProposalSummaryRequired, "Proposal Summary is required.", $"{fieldPrefix}.{nameof(proposal.Summary)}");

        if (string.IsNullOrWhiteSpace(proposal.Rationale))
            AddError(issues, ProposalRationaleRequired, "Proposal Rationale is required.", $"{fieldPrefix}.{nameof(proposal.Rationale)}");

        if (proposal.EvidenceRefs.Count == 0)
            AddError(issues, ProposalEvidenceRequired, "Proposal draft inputs require evidence references.", $"{fieldPrefix}.{nameof(proposal.EvidenceRefs)}");

        if (proposal.EvidenceRefs.Any(string.IsNullOrWhiteSpace))
            AddError(issues, BlankEvidenceRef, "Proposal evidence references cannot be blank.", $"{fieldPrefix}.{nameof(proposal.EvidenceRefs)}");

        if (!proposal.IsProposalOnly)
            AddError(issues, ProposalOnlyRequired, "Proposal draft inputs must be proposal-only.", $"{fieldPrefix}.{nameof(proposal.IsProposalOnly)}");

        if (proposal.CreatesCollectiveMemory)
            AddError(issues, CreatesCollectiveMemoryBlocked, "Proposal draft inputs must not create CollectiveMemory.", $"{fieldPrefix}.{nameof(proposal.CreatesCollectiveMemory)}");

        if (proposal.PromotesMemory)
            AddError(issues, PromotesMemoryBlocked, "Proposal draft inputs must not promote memory.", $"{fieldPrefix}.{nameof(proposal.PromotesMemory)}");

        if (!proposal.RequiresHumanReview)
            AddError(issues, ProposalHumanReviewRequired, "Proposal draft inputs require human review.", $"{fieldPrefix}.{nameof(proposal.RequiresHumanReview)}");

        ValidateTextValues(issues, "Proposal", [proposal.Title, proposal.Summary, proposal.Rationale, .. proposal.EvidenceRefs]);
    }

    private static void ValidateTextValues(List<ManualMemoryImprovementIssue> issues, string field, IEnumerable<string?> values)
    {
        if (ContainsAny(values, RawPrivateReasoningMarkers))
            AddError(issues, RawPrivateReasoningBlocked, $"{field} contains raw/private reasoning markers.", field);

        if (ContainsAny(values, ApprovalClaimMarkers))
            AddError(issues, ApprovalClaimBlocked, $"{field} contains an approval claim.", field);

        if (ContainsAny(values, MemoryPromotionClaimMarkers))
            AddError(issues, MemoryPromotionClaimBlocked, $"{field} contains a memory-promotion claim.", field);

        if (ContainsAny(values, AuthorityClaimMarkers))
            AddError(issues, AuthorityClaimBlocked, $"{field} contains an authority claim.", field);
    }

    private static bool ContainsAny(IEnumerable<string?> values, IReadOnlyList<string> markers) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value) &&
                            markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));

    private static void AddError(
        List<ManualMemoryImprovementIssue> issues,
        string code,
        string message,
        string? field = null) =>
        issues.Add(new ManualMemoryImprovementIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        });
}

public sealed class ManualMemoryImprovementAgentService : IManualMemoryImprovementAgentService
{
    private static readonly IReadOnlyList<string> BoundaryWarnings =
    [
        "MemoryImprovementAgent output is proposal-only.",
        "Proposal drafts do not create accepted memory.",
        "Proposal drafts do not promote memory.",
        "Proposal drafts require governed review before persistence or promotion.",
        "Detection result does not create CollectiveMemory.",
        "Detection result does not write vector index data.",
        "Detection result does not approve runtime action."
    ];

    private readonly ManualMemoryImprovementDetectionValidator _requestValidator;
    private readonly AgentDefinitionValidator _agentDefinitionValidator;
    private readonly MemoryImprovementDetectionResultValidator _detectionResultValidator;
    private readonly Audit.AgentRunAuditEnvelopeValidator _auditEnvelopeValidator;

    public ManualMemoryImprovementAgentService()
        : this(
            new ManualMemoryImprovementDetectionValidator(),
            new AgentDefinitionValidator(),
            new MemoryImprovementDetectionResultValidator(),
            new Audit.AgentRunAuditEnvelopeValidator())
    {
    }

    public ManualMemoryImprovementAgentService(
        ManualMemoryImprovementDetectionValidator requestValidator,
        AgentDefinitionValidator agentDefinitionValidator,
        MemoryImprovementDetectionResultValidator detectionResultValidator,
        Audit.AgentRunAuditEnvelopeValidator auditEnvelopeValidator)
    {
        _requestValidator = requestValidator;
        _agentDefinitionValidator = agentDefinitionValidator;
        _detectionResultValidator = detectionResultValidator;
        _auditEnvelopeValidator = auditEnvelopeValidator;
    }

    public ManualMemoryImprovementDetectionResult Detect(
        ManualMemoryImprovementDetectionRequest request,
        DateTimeOffset detectedAtUtc)
    {
        var manualRunId = BuildManualMemoryImprovementRunId(request.DetectionRequestId);
        var issues = new List<ManualMemoryImprovementIssue>();

        issues.AddRange(_requestValidator.Validate(request));
        issues.AddRange(ValidateMemoryImprovementAgentDefinition(AgentDefinitionCatalog.MemoryImprovementAgent));

        if (issues.Count > 0)
            return Failed(manualRunId, request.DetectionRequestId, issues);

        var detectionResult = BuildDetectionResult(request, detectedAtUtc);
        issues.AddRange(ToManualIssues(_detectionResultValidator.Validate(detectionResult), "MemoryImprovementDetectionResult"));

        if (issues.Count > 0)
            return Failed(manualRunId, request.DetectionRequestId, issues);

        var auditEnvelope = BuildAuditEnvelope(request, detectionResult, manualRunId, detectedAtUtc);
        issues.AddRange(ToManualIssues(_auditEnvelopeValidator.Validate(auditEnvelope), "AgentRunAuditEnvelope"));

        if (issues.Count > 0)
            return Failed(manualRunId, request.DetectionRequestId, issues);

        return new ManualMemoryImprovementDetectionResult
        {
            ManualMemoryImprovementRunId = manualRunId,
            DetectionRequestId = request.DetectionRequestId,
            Succeeded = true,
            DetectionResult = detectionResult,
            AuditEnvelope = auditEnvelope,
            Issues = []
        };
    }

    private IReadOnlyList<ManualMemoryImprovementIssue> ValidateMemoryImprovementAgentDefinition(AgentDefinition definition)
    {
        var issues = new List<ManualMemoryImprovementIssue>();
        issues.AddRange(ToManualIssues(_agentDefinitionValidator.Validate(definition), "AgentDefinition"));

        if (definition.Kind != AgentKind.ProposalAgent)
            AddError(issues, "MANUAL_MEMORY_IMPROVEMENT_AGENT_KIND_INVALID", "MemoryImprovementAgent must be a ProposalAgent.", "AgentDefinition.Kind");

        if (definition.ExecutionMode != AgentExecutionMode.ProposalOnly)
            AddError(issues, "MANUAL_MEMORY_IMPROVEMENT_AGENT_MODE_INVALID", "MemoryImprovementAgent must be ProposalOnly.", "AgentDefinition.ExecutionMode");

        if (definition.Capabilities is null || !definition.Capabilities.Contains(AgentCapability.CreateMemoryProposal))
            AddError(issues, "MANUAL_MEMORY_IMPROVEMENT_CREATE_PROPOSAL_REQUIRED", "MemoryImprovementAgent must allow CreateMemoryProposal.", "AgentDefinition.Capabilities");

        if (definition.Capabilities is not null &&
            definition.Capabilities.Any(capability => capability is
                AgentCapability.PromoteCollectiveMemory or
                AgentCapability.RunTool or
                AgentCapability.MutateSource or
                AgentCapability.CallExternalSystem or
                AgentCapability.BlockExecution))
        {
            AddError(issues, "MANUAL_MEMORY_IMPROVEMENT_DANGEROUS_CAPABILITY_ALLOWED", "MemoryImprovementAgent cannot allow dangerous capabilities.", "AgentDefinition.Capabilities");
        }

        return issues;
    }

    private static MemoryImprovementDetectionResult BuildDetectionResult(
        ManualMemoryImprovementDetectionRequest request,
        DateTimeOffset detectedAtUtc)
    {
        var findings = request.PatternDrafts
            .Select((pattern, index) => new MemoryImprovementPatternFinding
            {
                PatternFindingId = $"memory-pattern-{request.DetectionRequestId}-{index + 1:000}",
                PatternType = pattern.PatternType,
                Summary = pattern.Summary,
                Confidence = pattern.Confidence,
                EvidenceRefs = pattern.EvidenceRefs,
                RelatedMemoryIds = pattern.RelatedMemoryIds,
                RelatedProposalIds = pattern.RelatedProposalIds,
                IsDuplicateCandidate = pattern.IsDuplicateCandidate,
                RequiresHumanReview = pattern.RequiresHumanReview
            })
            .ToArray();

        var proposals = request.ProposalDrafts
            .Select((proposal, index) => new MemoryImprovementProposalDraft
            {
                ProposalDraftId = $"memory-proposal-draft-{request.DetectionRequestId}-{index + 1:000}",
                Title = proposal.Title,
                Summary = proposal.Summary,
                Rationale = proposal.Rationale,
                SourcePattern = findings[proposal.SourcePatternIndex],
                EvidenceRefs = proposal.EvidenceRefs,
                IsProposalOnly = true,
                CreatesCollectiveMemory = false,
                PromotesMemory = false,
                RequiresHumanReview = true
            })
            .ToArray();

        return new MemoryImprovementDetectionResult
        {
            DetectionResultId = $"memory-detection-{request.DetectionRequestId}",
            Findings = findings,
            ProposalDrafts = proposals,
            NoProposalReason = request.NoProposalReason,
            DetectedAt = detectedAtUtc,
            DetectedByAgentId = AgentDefinitionCatalog.MemoryImprovementAgent.AgentId,
            CorrelationId = request.CorrelationId,
            Warnings = BoundaryWarnings
        };
    }

    private static Audit.AgentRunAuditEnvelope BuildAuditEnvelope(
        ManualMemoryImprovementDetectionRequest request,
        MemoryImprovementDetectionResult detectionResult,
        string manualRunId,
        DateTimeOffset detectedAtUtc)
    {
        var evidenceRefs = BuildEvidenceRefs(request, detectionResult);
        var status = detectionResult.ProposalDrafts.Count > 0 || detectionResult.Findings.Count > 0
            ? Audit.AgentRunStatus.CompletedWithWarnings
            : Audit.AgentRunStatus.Completed;

        var outputs = new List<Audit.AgentRunOutputRef>
        {
            new()
            {
                OutputRefId = $"output-{detectionResult.DetectionResultId}",
                AgentRunId = manualRunId,
                RefType = "MemoryImprovementDetectionResult",
                RefId = detectionResult.DetectionResultId,
                Summary = "Manual MemoryImprovementAgent produced proposal-only detection output.",
                IsReviewOnly = false,
                IsProposalOnly = true,
                CreatesAuthority = false,
                CreatesRuntimeAction = false,
                ContainsRawPrivateReasoning = false,
                EvidenceRefs = evidenceRefs
            }
        };

        outputs.AddRange(detectionResult.ProposalDrafts.Select(draft => new Audit.AgentRunOutputRef
        {
            OutputRefId = $"output-{draft.ProposalDraftId}",
            AgentRunId = manualRunId,
            RefType = "MemoryImprovementProposalDraft",
            RefId = draft.ProposalDraftId,
            Summary = "Proposal-only memory improvement draft recorded without persistence.",
            IsReviewOnly = false,
            IsProposalOnly = true,
            CreatesAuthority = false,
            CreatesRuntimeAction = false,
            ContainsRawPrivateReasoning = false,
            EvidenceRefs = draft.EvidenceRefs
        }));

        return new Audit.AgentRunAuditEnvelope
        {
            Run = new Audit.AgentRunRecord
            {
                AgentRunId = manualRunId,
                TenantId = request.TenantId,
                ProjectId = request.ProjectId,
                CampaignId = request.CampaignId,
                RunId = request.RunId,
                AgentId = AgentDefinitionCatalog.MemoryImprovementAgent.AgentId,
                AgentName = AgentDefinitionCatalog.MemoryImprovementAgent.Name,
                RequestedByUserId = request.RequestedByUserId,
                TriggerType = Audit.AgentRunTriggerType.ManualUserRequest,
                Status = status,
                RequestSummary = request.RequestSummary,
                Purpose = "Manual MemoryImprovementAgent detection over supplied evidence.",
                CreatedAtUtc = detectedAtUtc,
                StartedAtUtc = detectedAtUtc,
                CompletedAtUtc = detectedAtUtc
            },
            AgentDefinitionSnapshot = AgentDefinitionCatalog.MemoryImprovementAgent,
            Inputs = request.Inputs.Select(input => new Audit.AgentRunInputRef
            {
                InputRefId = input.InputRefId,
                AgentRunId = manualRunId,
                RefType = input.RefType,
                RefId = input.RefId,
                Source = input.Source,
                Summary = input.Summary,
                IsAuthoritativeForAction = input.IsAuthoritativeForAction,
                ContainsRawPrivateReasoning = false
            }).ToArray(),
            Outputs = outputs,
            Steps = BuildSteps(manualRunId, evidenceRefs, detectedAtUtc),
            CapabilityUses = BuildCapabilityUses(manualRunId),
            BoundaryDecisions = BuildBoundaryDecisions(manualRunId, evidenceRefs),
            ThoughtLedger = BuildThoughtLedger(manualRunId, evidenceRefs, detectedAtUtc)
        };
    }

    private static IReadOnlyList<Audit.AgentRunStep> BuildSteps(
        string manualRunId,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset detectedAtUtc) =>
        [
            BuildStep(manualRunId, 1, Audit.AgentRunStepType.Created, "Manual MemoryImprovementAgent run created.", evidenceRefs, detectedAtUtc),
            BuildStep(manualRunId, 2, Audit.AgentRunStepType.InputBound, "Supplied evidence and pattern material were bound without scanning memory.", evidenceRefs, detectedAtUtc),
            BuildStep(manualRunId, 3, Audit.AgentRunStepType.CapabilityEvaluated, "Proposal-only capabilities were checked and dangerous capabilities stayed blocked.", evidenceRefs, detectedAtUtc),
            BuildStep(manualRunId, 4, Audit.AgentRunStepType.OutputRecorded, "Proposal-only MemoryImprovementDetectionResult was recorded.", evidenceRefs, detectedAtUtc),
            BuildStep(manualRunId, 5, Audit.AgentRunStepType.Completed, "Manual memory-improvement detection completed without authority.", evidenceRefs, detectedAtUtc)
        ];

    private static Audit.AgentRunStep BuildStep(
        string manualRunId,
        int sequence,
        Audit.AgentRunStepType stepType,
        string summary,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset detectedAtUtc) =>
        new()
        {
            StepId = $"step-{manualRunId}-{sequence:000}",
            AgentRunId = manualRunId,
            Sequence = sequence,
            StepType = stepType,
            OccurredAtUtc = detectedAtUtc,
            Summary = summary,
            EvidenceRefs = evidenceRefs
        };

    private static IReadOnlyList<Audit.AgentCapabilityUseRecord> BuildCapabilityUses(string manualRunId) =>
        [
            CapabilityUse(manualRunId, AgentCapability.CreateMemoryProposal, Audit.AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(manualRunId, AgentCapability.CreateReport, Audit.AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(manualRunId, AgentCapability.PromoteCollectiveMemory, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(manualRunId, AgentCapability.RunTool, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(manualRunId, AgentCapability.MutateSource, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(manualRunId, AgentCapability.CallExternalSystem, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(manualRunId, AgentCapability.BlockExecution, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(manualRunId, AgentCapability.RepresentHumanApproval, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(manualRunId, AgentCapability.RepresentHumanPromotionDecision, Audit.AgentCapabilityUseOutcome.Blocked)
        ];

    private static Audit.AgentCapabilityUseRecord CapabilityUse(
        string manualRunId,
        AgentCapability capability,
        Audit.AgentCapabilityUseOutcome outcome)
    {
        var definition = AgentDefinitionCatalog.MemoryImprovementAgent;
        var declared = definition.Capabilities?.Contains(capability) == true;
        var forbidden = definition.ForbiddenCapabilities?.Contains(capability) == true;

        return new Audit.AgentCapabilityUseRecord
        {
            CapabilityUseId = $"capability-{manualRunId}-{capability}",
            AgentRunId = manualRunId,
            Capability = capability,
            Outcome = outcome,
            Summary = $"{capability} was {outcome} for manual memory-improvement detection.",
            PolicyDecisionId = $"policy-{manualRunId}",
            BoundaryDecisionId = $"boundary-{manualRunId}-{capability}",
            EvidenceRef = $"evidence-{manualRunId}",
            WasDeclaredOnAgent = declared,
            WasForbiddenOnAgent = forbidden
        };
    }

    private static IReadOnlyList<Audit.AgentBoundaryDecision> BuildBoundaryDecisions(
        string manualRunId,
        IReadOnlyList<string> evidenceRefs) =>
        [
            BoundaryDecision(manualRunId, "agent-definition", Audit.AgentBoundaryDecisionType.AgentDefinition, "allow", "MemoryImprovementAgent definition is valid for manual proposal-only detection.", evidenceRefs),
            BoundaryDecision(manualRunId, "create-memory-proposal", Audit.AgentBoundaryDecisionType.Capability, "allow", "CreateMemoryProposal is allowed only for proposal-only output.", evidenceRefs),
            BoundaryDecision(manualRunId, "promote-memory", Audit.AgentBoundaryDecisionType.Capability, "block", "PromoteCollectiveMemory remains unavailable.", evidenceRefs),
            BoundaryDecision(manualRunId, "run-tool", Audit.AgentBoundaryDecisionType.Capability, "block", "RunTool remains unavailable.", evidenceRefs),
            BoundaryDecision(manualRunId, "mutate-source", Audit.AgentBoundaryDecisionType.Capability, "block", "MutateSource remains unavailable.", evidenceRefs),
            BoundaryDecision(manualRunId, "call-external-system", Audit.AgentBoundaryDecisionType.Capability, "block", "CallExternalSystem remains unavailable.", evidenceRefs),
            BoundaryDecision(manualRunId, "block-execution", Audit.AgentBoundaryDecisionType.Capability, "block", "BlockExecution remains unavailable.", evidenceRefs),
            BoundaryDecision(manualRunId, "output-validation", Audit.AgentBoundaryDecisionType.OutputValidation, "allow", "MemoryImprovementDetectionResult passed proposal-only validation.", evidenceRefs),
            BoundaryDecision(manualRunId, "thought-ledger-safety", Audit.AgentBoundaryDecisionType.ThoughtLedgerSafety, "allow", "ThoughtLedger entries contain safe rationale only.", evidenceRefs)
        ];

    private static Audit.AgentBoundaryDecision BoundaryDecision(
        string manualRunId,
        string suffix,
        Audit.AgentBoundaryDecisionType boundaryType,
        string decision,
        string reason,
        IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            BoundaryDecisionId = $"boundary-{manualRunId}-{suffix}",
            AgentRunId = manualRunId,
            BoundaryType = boundaryType,
            Decision = decision,
            Reason = reason,
            SourceRefId = $"manual-memory-improvement-{suffix}",
            GrantsAuthority = false,
            GrantsHumanApproval = false,
            GrantsPolicyApproval = false,
            GrantsMemoryPromotion = false,
            EvidenceRefs = evidenceRefs
        };

    private static IReadOnlyList<AuditThoughtLedgerEntry> BuildThoughtLedger(
        string manualRunId,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset detectedAtUtc) =>
        [
            Thought(manualRunId, "evidence-used", Audit.ThoughtLedgerEntryType.EvidenceUsed, "Reviewed supplied run reports, memory influence records, critic findings, and proposal evidence references.", evidenceRefs, detectedAtUtc),
            Thought(manualRunId, "assumption", Audit.ThoughtLedgerEntryType.Assumption, "Detection is limited to supplied evidence and does not scan project memory directly.", [], detectedAtUtc),
            Thought(manualRunId, "rejected-alternative", Audit.ThoughtLedgerEntryType.RejectedAlternative, "Did not persist proposal drafts because MemoryImprovementAgent is proposal-only.", [], detectedAtUtc),
            Thought(manualRunId, "boundary-decision", Audit.ThoughtLedgerEntryType.BoundaryDecision, "Collective-memory elevation, RunTool, MutateSource, CallExternalSystem, and BlockExecution remain blocked.", evidenceRefs, detectedAtUtc),
            Thought(manualRunId, "output-rationale", Audit.ThoughtLedgerEntryType.OutputRationale, "Produced MemoryImprovementDetectionResult as proposal-only output; no durable memory state changed.", evidenceRefs, detectedAtUtc)
        ];

    private static AuditThoughtLedgerEntry Thought(
        string manualRunId,
        string suffix,
        Audit.ThoughtLedgerEntryType entryType,
        string summary,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset detectedAtUtc) =>
        new()
        {
            ThoughtLedgerEntryId = $"thought-{manualRunId}-{suffix}",
            AgentRunId = manualRunId,
            EntryType = entryType,
            Summary = summary,
            EvidenceRefs = evidenceRefs,
            RecordedAtUtc = detectedAtUtc,
            ContainsRawPrivateReasoning = false,
            GrantsAuthority = false,
            GrantsApproval = false,
            GrantsMemoryPromotion = false
        };

    private static IReadOnlyList<string> BuildEvidenceRefs(
        ManualMemoryImprovementDetectionRequest request,
        MemoryImprovementDetectionResult result)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var input in request.Inputs)
        {
            refs.Add(input.InputRefId);
            refs.Add(input.RefId);

            foreach (var evidenceRef in input.EvidenceRefs)
                refs.Add(evidenceRef);
        }

        foreach (var finding in result.Findings)
        {
            foreach (var evidenceRef in finding.EvidenceRefs)
                refs.Add(evidenceRef);

            foreach (var memoryId in finding.RelatedMemoryIds)
                refs.Add(memoryId);

            foreach (var proposalId in finding.RelatedProposalIds)
                refs.Add(proposalId);
        }

        foreach (var draft in result.ProposalDrafts)
        {
            refs.Add(draft.ProposalDraftId);

            foreach (var evidenceRef in draft.EvidenceRefs)
                refs.Add(evidenceRef);
        }

        return refs.Where(value => !string.IsNullOrWhiteSpace(value)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static string BuildManualMemoryImprovementRunId(string detectionRequestId) =>
        $"manual-memory-improvement-{(string.IsNullOrWhiteSpace(detectionRequestId) ? "missing-request" : detectionRequestId)}";

    private static ManualMemoryImprovementDetectionResult Failed(
        string manualRunId,
        string detectionRequestId,
        IReadOnlyList<ManualMemoryImprovementIssue> issues) =>
        new()
        {
            ManualMemoryImprovementRunId = manualRunId,
            DetectionRequestId = detectionRequestId,
            Succeeded = false,
            Issues = issues
        };

    private static IReadOnlyList<ManualMemoryImprovementIssue> ToManualIssues(
        IReadOnlyList<AgentDefinitionValidationIssue> issues,
        string field) =>
        issues.Select(issue => new ManualMemoryImprovementIssue
        {
            Code = issue.Code,
            Severity = issue.Severity,
            Message = issue.Message,
            Field = field
        }).ToArray();

    private static void AddError(
        List<ManualMemoryImprovementIssue> issues,
        string code,
        string message,
        string field) =>
        issues.Add(new ManualMemoryImprovementIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        });
}
