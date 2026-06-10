using IronDev.Core.Agents.Audit;
using AuditThoughtLedgerEntry = IronDev.Core.Agents.Audit.ThoughtLedgerEntry;

namespace IronDev.Core.Agents.Concrete;

public sealed record ManualCriticReviewInputRef
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

public sealed record ManualCriticFindingDraft
{
    public required CriticSeverity Severity { get; init; }
    public required string Title { get; init; }
    public required string Problem { get; init; }
    public required string WhyItMatters { get; init; }
    public required string RequiredFix { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool BlocksMerge { get; init; }
    public bool RequiresHumanReview { get; init; }
}

public sealed record ManualCriticReviewRequest
{
    public required string ReviewRequestId { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string CampaignId { get; init; }
    public required string RunId { get; init; }
    public required CriticReviewSubjectType SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string RequestedByUserId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string RequestSummary { get; init; } = string.Empty;
    public IReadOnlyList<ManualCriticReviewInputRef> Inputs { get; init; } = [];
    public IReadOnlyList<ManualCriticFindingDraft> FindingDrafts { get; init; } = [];
    public CriticReviewVerdict RequestedVerdict { get; init; }
}

public sealed record ManualCriticReviewIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string? Field { get; init; }
}

public sealed record ManualCriticReviewResult
{
    public required string ManualCriticRunId { get; init; }
    public required string ReviewRequestId { get; init; }
    public required bool Succeeded { get; init; }
    public CriticReviewResult? CriticReviewResult { get; init; }
    public AgentRunAuditEnvelope? AuditEnvelope { get; init; }
    public IReadOnlyList<ManualCriticReviewIssue> Issues { get; init; } = [];
}

public interface IManualIndependentCriticAgentService
{
    ManualCriticReviewResult Review(ManualCriticReviewRequest request, DateTimeOffset reviewedAtUtc);
}

public sealed class ManualCriticReviewValidator
{
    public const string ReviewRequestIdRequired = "MANUAL_CRITIC_REVIEW_REQUEST_ID_REQUIRED";
    public const string ScopeRequired = "MANUAL_CRITIC_SCOPE_REQUIRED";
    public const string SubjectIdRequired = "MANUAL_CRITIC_SUBJECT_ID_REQUIRED";
    public const string SubjectTypeInvalid = "MANUAL_CRITIC_SUBJECT_TYPE_INVALID";
    public const string RequestedByUserIdRequired = "MANUAL_CRITIC_REQUESTED_BY_USER_ID_REQUIRED";
    public const string InputRequired = "MANUAL_CRITIC_INPUT_REQUIRED";
    public const string InputRequiredField = "MANUAL_CRITIC_INPUT_REQUIRED_FIELD";
    public const string FindingRequired = "MANUAL_CRITIC_FINDING_REQUIRED";
    public const string FindingSeverityInvalid = "MANUAL_CRITIC_FINDING_SEVERITY_INVALID";
    public const string FindingTitleRequired = "MANUAL_CRITIC_FINDING_TITLE_REQUIRED";
    public const string FindingProblemRequired = "MANUAL_CRITIC_FINDING_PROBLEM_REQUIRED";
    public const string FindingWhyItMattersRequired = "MANUAL_CRITIC_FINDING_WHY_IT_MATTERS_REQUIRED";
    public const string FindingRequiredFixRequired = "MANUAL_CRITIC_FINDING_REQUIRED_FIX_REQUIRED";
    public const string NoObjectionCannotBlock = "MANUAL_CRITIC_NO_OBJECTION_CANNOT_BLOCK";
    public const string RecommendBlockRequiresBlockingFinding = "MANUAL_CRITIC_RECOMMEND_BLOCK_REQUIRES_BLOCKING_FINDING";
    public const string RawPrivateReasoningBlocked = "MANUAL_CRITIC_RAW_PRIVATE_REASONING_BLOCKED";
    public const string AuthorityClaimBlocked = "MANUAL_CRITIC_AUTHORITY_CLAIM_BLOCKED";
    public const string ApprovalClaimBlocked = "MANUAL_CRITIC_APPROVAL_CLAIM_BLOCKED";
    public const string MemoryPromotionClaimBlocked = "MANUAL_CRITIC_MEMORY_PROMOTION_CLAIM_BLOCKED";
    public const string InputAuthorityBlocked = "MANUAL_CRITIC_INPUT_AUTHORITY_BLOCKED";
    public const string BlankEvidenceRef = "MANUAL_CRITIC_BLANK_EVIDENCE_REF";

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

    public IReadOnlyList<ManualCriticReviewIssue> Validate(ManualCriticReviewRequest request)
    {
        var issues = new List<ManualCriticReviewIssue>();

        if (string.IsNullOrWhiteSpace(request.ReviewRequestId))
            AddError(issues, ReviewRequestIdRequired, "ReviewRequestId is required.", nameof(request.ReviewRequestId));

        if (string.IsNullOrWhiteSpace(request.TenantId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.CampaignId) ||
            string.IsNullOrWhiteSpace(request.RunId))
        {
            AddError(issues, ScopeRequired, "TenantId, ProjectId, CampaignId, and RunId are required.", "Scope");
        }

        if (string.IsNullOrWhiteSpace(request.SubjectId))
            AddError(issues, SubjectIdRequired, "SubjectId is required.", nameof(request.SubjectId));

        if (!Enum.IsDefined(request.SubjectType))
            AddError(issues, SubjectTypeInvalid, "SubjectType is invalid.", nameof(request.SubjectType));

        if (!Enum.IsDefined(request.RequestedVerdict))
            AddError(issues, SubjectTypeInvalid, "RequestedVerdict is invalid.", nameof(request.RequestedVerdict));

        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
            AddError(issues, RequestedByUserIdRequired, "RequestedByUserId is required for manual critic review.", nameof(request.RequestedByUserId));

        if (request.Inputs.Count == 0)
            AddError(issues, InputRequired, "At least one review input is required.", nameof(request.Inputs));

        if (request.RequestedVerdict != CriticReviewVerdict.NoObjection && request.FindingDrafts.Count == 0)
            AddError(issues, FindingRequired, "At least one finding is required unless RequestedVerdict is NoObjection.", nameof(request.FindingDrafts));

        if (request.RequestedVerdict == CriticReviewVerdict.NoObjection &&
            request.FindingDrafts.Any(finding => finding.BlocksMerge))
        {
            AddError(issues, NoObjectionCannotBlock, "NoObjection cannot include a blocking finding.", nameof(request.FindingDrafts));
        }

        if (request.RequestedVerdict == CriticReviewVerdict.RecommendBlock &&
            !request.FindingDrafts.Any(finding => finding.BlocksMerge))
        {
            AddError(issues, RecommendBlockRequiresBlockingFinding, "RecommendBlock requires at least one blocking finding.", nameof(request.FindingDrafts));
        }

        ValidateTextValues(issues, "Request", [request.ReviewRequestId, request.SubjectId, request.RequestedByUserId, request.CorrelationId, request.RequestSummary]);

        foreach (var input in request.Inputs)
            ValidateInput(input, issues);

        for (var i = 0; i < request.FindingDrafts.Count; i++)
            ValidateFinding(request.FindingDrafts[i], i, issues);

        return issues;
    }

    private static void ValidateInput(ManualCriticReviewInputRef input, List<ManualCriticReviewIssue> issues)
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
            AddError(issues, InputAuthorityBlocked, "Manual critic inputs cannot be authoritative for action unless they are explicit human/governance evidence.", nameof(input.IsAuthoritativeForAction));

        if (input.EvidenceRefs.Any(string.IsNullOrWhiteSpace))
            AddError(issues, BlankEvidenceRef, "Evidence references cannot be blank.", nameof(input.EvidenceRefs));

        ValidateTextValues(issues, "Input", [input.InputRefId, input.RefType, input.RefId, input.Source, input.Summary, .. input.EvidenceRefs]);
    }

    private static void ValidateFinding(ManualCriticFindingDraft finding, int index, List<ManualCriticReviewIssue> issues)
    {
        var fieldPrefix = $"{nameof(ManualCriticReviewRequest.FindingDrafts)}[{index}]";

        if (!Enum.IsDefined(finding.Severity))
            AddError(issues, FindingSeverityInvalid, "Finding severity is invalid.", $"{fieldPrefix}.{nameof(finding.Severity)}");

        if (string.IsNullOrWhiteSpace(finding.Title))
            AddError(issues, FindingTitleRequired, "Finding Title is required.", $"{fieldPrefix}.{nameof(finding.Title)}");

        if (string.IsNullOrWhiteSpace(finding.Problem))
            AddError(issues, FindingProblemRequired, "Finding Problem is required.", $"{fieldPrefix}.{nameof(finding.Problem)}");

        if (string.IsNullOrWhiteSpace(finding.WhyItMatters))
            AddError(issues, FindingWhyItMattersRequired, "Finding WhyItMatters is required.", $"{fieldPrefix}.{nameof(finding.WhyItMatters)}");

        if (string.IsNullOrWhiteSpace(finding.RequiredFix))
            AddError(issues, FindingRequiredFixRequired, "Finding RequiredFix is required.", $"{fieldPrefix}.{nameof(finding.RequiredFix)}");

        if (finding.EvidenceRefs.Any(string.IsNullOrWhiteSpace))
            AddError(issues, BlankEvidenceRef, "Evidence references cannot be blank.", $"{fieldPrefix}.{nameof(finding.EvidenceRefs)}");

        ValidateTextValues(
            issues,
            "Finding",
            [finding.Title, finding.Problem, finding.WhyItMatters, finding.RequiredFix, .. finding.EvidenceRefs]);
    }

    private static void ValidateTextValues(List<ManualCriticReviewIssue> issues, string field, IEnumerable<string?> values)
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

    private static void AddError(List<ManualCriticReviewIssue> issues, string code, string message, string? field = null) =>
        issues.Add(new ManualCriticReviewIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        });
}

public sealed class ManualIndependentCriticAgentService : IManualIndependentCriticAgentService
{
    private static readonly IReadOnlyList<string> BoundaryWarnings =
    [
        "Critic findings are recommendations only.",
        "Critic review does not grant or deny approval.",
        "Governance and human approval remain separate.",
        "Critic review does not enforce blocks.",
        "Critic review does not mutate source."
    ];

    private readonly ManualCriticReviewValidator _requestValidator;
    private readonly AgentDefinitionValidator _agentDefinitionValidator;
    private readonly CriticReviewResultValidator _criticReviewResultValidator;
    private readonly AgentRunAuditEnvelopeValidator _auditEnvelopeValidator;

    public ManualIndependentCriticAgentService()
        : this(
            new ManualCriticReviewValidator(),
            new AgentDefinitionValidator(),
            new CriticReviewResultValidator(),
            new AgentRunAuditEnvelopeValidator())
    {
    }

    public ManualIndependentCriticAgentService(
        ManualCriticReviewValidator requestValidator,
        AgentDefinitionValidator agentDefinitionValidator,
        CriticReviewResultValidator criticReviewResultValidator,
        AgentRunAuditEnvelopeValidator auditEnvelopeValidator)
    {
        _requestValidator = requestValidator;
        _agentDefinitionValidator = agentDefinitionValidator;
        _criticReviewResultValidator = criticReviewResultValidator;
        _auditEnvelopeValidator = auditEnvelopeValidator;
    }

    public ManualCriticReviewResult Review(ManualCriticReviewRequest request, DateTimeOffset reviewedAtUtc)
    {
        var manualCriticRunId = BuildManualCriticRunId(request.ReviewRequestId);
        var issues = new List<ManualCriticReviewIssue>();

        issues.AddRange(_requestValidator.Validate(request));
        issues.AddRange(ValidateIndependentCriticAgentDefinition(AgentDefinitionCatalog.IndependentCriticAgent));

        if (issues.Count > 0)
            return Failed(manualCriticRunId, request.ReviewRequestId, issues);

        var criticReviewResult = BuildCriticReviewResult(request, reviewedAtUtc);
        issues.AddRange(ToManualIssues(_criticReviewResultValidator.Validate(criticReviewResult), "CriticReviewResult"));

        if (issues.Count > 0)
            return Failed(manualCriticRunId, request.ReviewRequestId, issues);

        var auditEnvelope = BuildAuditEnvelope(request, criticReviewResult, manualCriticRunId, reviewedAtUtc);
        issues.AddRange(ToManualIssues(_auditEnvelopeValidator.Validate(auditEnvelope), "AgentRunAuditEnvelope"));

        if (issues.Count > 0)
            return Failed(manualCriticRunId, request.ReviewRequestId, issues);

        return new ManualCriticReviewResult
        {
            ManualCriticRunId = manualCriticRunId,
            ReviewRequestId = request.ReviewRequestId,
            Succeeded = true,
            CriticReviewResult = criticReviewResult,
            AuditEnvelope = auditEnvelope,
            Issues = []
        };
    }

    private static IReadOnlyList<ManualCriticReviewIssue> ValidateIndependentCriticAgentDefinition(AgentDefinition definition)
    {
        var issues = new List<ManualCriticReviewIssue>();
        var definitionIssues = new AgentDefinitionValidator().Validate(definition);
        issues.AddRange(ToManualIssues(definitionIssues, "AgentDefinition"));

        if (definition.Kind != AgentKind.ReviewAgent)
            AddError(issues, "MANUAL_CRITIC_AGENT_KIND_INVALID", "IndependentCriticAgent must be a ReviewAgent.", "AgentDefinition.Kind");

        if (definition.ExecutionMode != AgentExecutionMode.OutOfBandReviewOnly)
            AddError(issues, "MANUAL_CRITIC_AGENT_MODE_INVALID", "IndependentCriticAgent must be OutOfBandReviewOnly.", "AgentDefinition.ExecutionMode");

        if (definition.Capabilities is null || !definition.Capabilities.Contains(AgentCapability.CreateCriticFinding))
            AddError(issues, "MANUAL_CRITIC_CREATE_FINDING_REQUIRED", "IndependentCriticAgent must allow CreateCriticFinding.", "AgentDefinition.Capabilities");

        if (definition.Capabilities is not null &&
            definition.Capabilities.Any(capability => capability is AgentCapability.BlockExecution or AgentCapability.RunTool or AgentCapability.MutateSource))
        {
            AddError(issues, "MANUAL_CRITIC_DANGEROUS_CAPABILITY_ALLOWED", "IndependentCriticAgent cannot allow BlockExecution, RunTool, or MutateSource.", "AgentDefinition.Capabilities");
        }

        return issues;
    }

    private static CriticReviewResult BuildCriticReviewResult(ManualCriticReviewRequest request, DateTimeOffset reviewedAtUtc) =>
        new()
        {
            ReviewResultId = $"critic-review-{request.ReviewRequestId}",
            ReviewRequestId = request.ReviewRequestId,
            Verdict = request.RequestedVerdict,
            Findings = request.FindingDrafts
                .Select((finding, index) => new CriticFinding
                {
                    FindingId = $"critic-finding-{request.ReviewRequestId}-{index + 1:000}",
                    Severity = finding.Severity,
                    Title = finding.Title,
                    Problem = finding.Problem,
                    WhyItMatters = finding.WhyItMatters,
                    RequiredFix = finding.RequiredFix,
                    EvidenceRefs = finding.EvidenceRefs,
                    BlocksMerge = finding.BlocksMerge,
                    RequiresHumanReview = finding.RequiresHumanReview
                })
                .ToArray(),
            ReviewedAt = reviewedAtUtc,
            ReviewedByAgentId = AgentDefinitionCatalog.IndependentCriticAgent.AgentId,
            CorrelationId = request.CorrelationId,
            Warnings = BoundaryWarnings
        };

    private static AgentRunAuditEnvelope BuildAuditEnvelope(
        ManualCriticReviewRequest request,
        CriticReviewResult criticReviewResult,
        string manualCriticRunId,
        DateTimeOffset reviewedAtUtc)
    {
        var evidenceRefs = BuildEvidenceRefs(request, criticReviewResult);
        var status = request.RequestedVerdict == CriticReviewVerdict.NoObjection
            ? Audit.AgentRunStatus.Completed
            : Audit.AgentRunStatus.CompletedWithWarnings;

        return new AgentRunAuditEnvelope
        {
            Run = new AgentRunRecord
            {
                AgentRunId = manualCriticRunId,
                TenantId = request.TenantId,
                ProjectId = request.ProjectId,
                CampaignId = request.CampaignId,
                RunId = request.RunId,
                AgentId = AgentDefinitionCatalog.IndependentCriticAgent.AgentId,
                AgentName = AgentDefinitionCatalog.IndependentCriticAgent.Name,
                RequestedByUserId = request.RequestedByUserId,
                TriggerType = Audit.AgentRunTriggerType.ManualUserRequest,
                Status = status,
                RequestSummary = request.RequestSummary,
                Purpose = "Manual IndependentCriticAgent review over supplied evidence.",
                CreatedAtUtc = reviewedAtUtc,
                StartedAtUtc = reviewedAtUtc,
                CompletedAtUtc = reviewedAtUtc
            },
            AgentDefinitionSnapshot = AgentDefinitionCatalog.IndependentCriticAgent,
            Inputs = request.Inputs.Select(input => new AgentRunInputRef
            {
                InputRefId = input.InputRefId,
                AgentRunId = manualCriticRunId,
                RefType = input.RefType,
                RefId = input.RefId,
                Source = input.Source,
                Summary = input.Summary,
                IsAuthoritativeForAction = input.IsAuthoritativeForAction,
                ContainsRawPrivateReasoning = false
            }).ToArray(),
            Outputs =
            [
                new AgentRunOutputRef
                {
                    OutputRefId = $"output-{criticReviewResult.ReviewResultId}",
                    AgentRunId = manualCriticRunId,
                    RefType = "CriticReviewResult",
                    RefId = criticReviewResult.ReviewResultId,
                    Summary = "Manual IndependentCriticAgent produced a review-only CriticReviewResult.",
                    IsReviewOnly = true,
                    IsProposalOnly = false,
                    CreatesAuthority = false,
                    CreatesRuntimeAction = false,
                    ContainsRawPrivateReasoning = false,
                    EvidenceRefs = evidenceRefs
                }
            ],
            Steps = BuildSteps(manualCriticRunId, evidenceRefs, reviewedAtUtc),
            CapabilityUses = BuildCapabilityUses(manualCriticRunId),
            BoundaryDecisions = BuildBoundaryDecisions(manualCriticRunId, evidenceRefs),
            ThoughtLedger = BuildThoughtLedger(manualCriticRunId, evidenceRefs, reviewedAtUtc)
        };
    }

    private static IReadOnlyList<AgentRunStep> BuildSteps(
        string manualCriticRunId,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset reviewedAtUtc) =>
        [
            BuildStep(manualCriticRunId, 1, AgentRunStepType.Created, "Manual IndependentCriticAgent run created.", evidenceRefs, reviewedAtUtc),
            BuildStep(manualCriticRunId, 2, AgentRunStepType.InputBound, "Supplied review inputs were bound without repository reads.", evidenceRefs, reviewedAtUtc),
            BuildStep(manualCriticRunId, 3, AgentRunStepType.CapabilityEvaluated, "Review capabilities were checked and dangerous capabilities stayed blocked.", evidenceRefs, reviewedAtUtc),
            BuildStep(manualCriticRunId, 4, AgentRunStepType.OutputRecorded, "Review-only CriticReviewResult was recorded.", evidenceRefs, reviewedAtUtc),
            BuildStep(manualCriticRunId, 5, AgentRunStepType.Completed, "Manual critic review completed without execution authority.", evidenceRefs, reviewedAtUtc)
        ];

    private static AgentRunStep BuildStep(
        string manualCriticRunId,
        int sequence,
        AgentRunStepType stepType,
        string summary,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset reviewedAtUtc) =>
        new()
        {
            StepId = $"step-{manualCriticRunId}-{sequence:000}",
            AgentRunId = manualCriticRunId,
            Sequence = sequence,
            StepType = stepType,
            OccurredAtUtc = reviewedAtUtc,
            Summary = summary,
            EvidenceRefs = evidenceRefs
        };

    private static IReadOnlyList<AgentCapabilityUseRecord> BuildCapabilityUses(string manualCriticRunId) =>
        [
            CapabilityUse(manualCriticRunId, AgentCapability.CreateCriticFinding, AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(manualCriticRunId, AgentCapability.CreateReport, AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(manualCriticRunId, AgentCapability.WarnExecution, AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(manualCriticRunId, AgentCapability.BlockExecution, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(manualCriticRunId, AgentCapability.RunTool, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(manualCriticRunId, AgentCapability.MutateSource, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(manualCriticRunId, AgentCapability.CallExternalSystem, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(manualCriticRunId, AgentCapability.PromoteCollectiveMemory, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(manualCriticRunId, AgentCapability.RepresentHumanApproval, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(manualCriticRunId, AgentCapability.RepresentHumanPromotionDecision, AgentCapabilityUseOutcome.Blocked)
        ];

    private static AgentCapabilityUseRecord CapabilityUse(
        string manualCriticRunId,
        AgentCapability capability,
        AgentCapabilityUseOutcome outcome)
    {
        var definition = AgentDefinitionCatalog.IndependentCriticAgent;
        var declared = definition.Capabilities?.Contains(capability) == true;
        var forbidden = definition.ForbiddenCapabilities?.Contains(capability) == true;

        return new AgentCapabilityUseRecord
        {
            CapabilityUseId = $"capability-{manualCriticRunId}-{capability}",
            AgentRunId = manualCriticRunId,
            Capability = capability,
            Outcome = outcome,
            Summary = $"{capability} was {outcome} for manual critic review.",
            PolicyDecisionId = $"policy-{manualCriticRunId}",
            BoundaryDecisionId = $"boundary-{manualCriticRunId}-{capability}",
            EvidenceRef = $"evidence-{manualCriticRunId}",
            WasDeclaredOnAgent = declared,
            WasForbiddenOnAgent = forbidden
        };
    }

    private static IReadOnlyList<AgentBoundaryDecision> BuildBoundaryDecisions(
        string manualCriticRunId,
        IReadOnlyList<string> evidenceRefs) =>
        [
            BoundaryDecision(manualCriticRunId, "agent-definition", AgentBoundaryDecisionType.AgentDefinition, "allow", "IndependentCriticAgent definition is valid for manual review.", evidenceRefs),
            BoundaryDecision(manualCriticRunId, "create-finding", AgentBoundaryDecisionType.Capability, "allow", "CreateCriticFinding is allowed for review output.", evidenceRefs),
            BoundaryDecision(manualCriticRunId, "block-execution", AgentBoundaryDecisionType.Capability, "block", "BlockExecution remains unavailable to the critic.", evidenceRefs),
            BoundaryDecision(manualCriticRunId, "run-tool", AgentBoundaryDecisionType.Capability, "block", "RunTool remains unavailable to the critic.", evidenceRefs),
            BoundaryDecision(manualCriticRunId, "mutate-source", AgentBoundaryDecisionType.Capability, "block", "MutateSource remains unavailable to the critic.", evidenceRefs),
            BoundaryDecision(manualCriticRunId, "output-validation", AgentBoundaryDecisionType.OutputValidation, "allow", "CriticReviewResult passed review-only validation.", evidenceRefs),
            BoundaryDecision(manualCriticRunId, "thought-ledger-safety", AgentBoundaryDecisionType.ThoughtLedgerSafety, "allow", "ThoughtLedger entries contain safe rationale only.", evidenceRefs)
        ];

    private static AgentBoundaryDecision BoundaryDecision(
        string manualCriticRunId,
        string suffix,
        AgentBoundaryDecisionType boundaryType,
        string decision,
        string reason,
        IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            BoundaryDecisionId = $"boundary-{manualCriticRunId}-{suffix}",
            AgentRunId = manualCriticRunId,
            BoundaryType = boundaryType,
            Decision = decision,
            Reason = reason,
            SourceRefId = $"manual-critic-{suffix}",
            GrantsAuthority = false,
            GrantsHumanApproval = false,
            GrantsPolicyApproval = false,
            GrantsMemoryPromotion = false,
            EvidenceRefs = evidenceRefs
        };

    private static IReadOnlyList<AuditThoughtLedgerEntry> BuildThoughtLedger(
        string manualCriticRunId,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset reviewedAtUtc) =>
        [
            Thought(manualCriticRunId, "evidence-used", ThoughtLedgerEntryType.EvidenceUsed, "Reviewed supplied evidence references for the manual critic request.", evidenceRefs, reviewedAtUtc),
            Thought(manualCriticRunId, "assumption", ThoughtLedgerEntryType.Assumption, "Review is limited to supplied evidence and does not inspect repository state directly.", [], reviewedAtUtc),
            Thought(manualCriticRunId, "rejected-alternative", ThoughtLedgerEntryType.RejectedAlternative, "Did not attempt source mutation or tool execution because the critic is review-only.", [], reviewedAtUtc),
            Thought(manualCriticRunId, "boundary-decision", ThoughtLedgerEntryType.BoundaryDecision, "BlockExecution, RunTool, MutateSource, and PromoteCollectiveMemory remain blocked.", evidenceRefs, reviewedAtUtc),
            Thought(manualCriticRunId, "output-rationale", ThoughtLedgerEntryType.OutputRationale, "Produced CriticReviewResult as review-only output with no governance enforcement.", evidenceRefs, reviewedAtUtc)
        ];

    private static AuditThoughtLedgerEntry Thought(
        string manualCriticRunId,
        string suffix,
        ThoughtLedgerEntryType entryType,
        string summary,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset reviewedAtUtc) =>
        new()
        {
            ThoughtLedgerEntryId = $"thought-{manualCriticRunId}-{suffix}",
            AgentRunId = manualCriticRunId,
            EntryType = entryType,
            Summary = summary,
            EvidenceRefs = evidenceRefs,
            RecordedAtUtc = reviewedAtUtc,
            ContainsRawPrivateReasoning = false,
            GrantsAuthority = false,
            GrantsApproval = false,
            GrantsMemoryPromotion = false
        };

    private static IReadOnlyList<string> BuildEvidenceRefs(
        ManualCriticReviewRequest request,
        CriticReviewResult result)
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
        }

        return refs.Where(value => !string.IsNullOrWhiteSpace(value)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static string BuildManualCriticRunId(string reviewRequestId) =>
        $"manual-independent-critic-{(string.IsNullOrWhiteSpace(reviewRequestId) ? "missing-request" : reviewRequestId)}";

    private static ManualCriticReviewResult Failed(
        string manualCriticRunId,
        string reviewRequestId,
        IReadOnlyList<ManualCriticReviewIssue> issues) =>
        new()
        {
            ManualCriticRunId = manualCriticRunId,
            ReviewRequestId = reviewRequestId,
            Succeeded = false,
            Issues = issues
        };

    private static IReadOnlyList<ManualCriticReviewIssue> ToManualIssues(
        IReadOnlyList<AgentDefinitionValidationIssue> issues,
        string field) =>
        issues.Select(issue => new ManualCriticReviewIssue
        {
            Code = issue.Code,
            Severity = issue.Severity,
            Message = issue.Message,
            Field = field
        }).ToArray();

    private static void AddError(List<ManualCriticReviewIssue> issues, string code, string message, string field) =>
        issues.Add(new ManualCriticReviewIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        });
}
