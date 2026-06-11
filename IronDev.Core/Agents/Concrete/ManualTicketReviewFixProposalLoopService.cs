using IronDev.Core.Agents.Audit;
using AuditAgentRunStatus = IronDev.Core.Agents.Audit.AgentRunStatus;
using AuditThoughtLedgerEntry = IronDev.Core.Agents.Audit.ThoughtLedgerEntry;

namespace IronDev.Core.Agents.Concrete;

public enum ManualTicketReviewFixProposalLoopStatus
{
    Succeeded = 1,
    InvalidRequest = 2,
    CriticRejected = 3,
    ProposalRejected = 4,
    NeedsHumanReview = 5,
    Blocked = 6,
    Failed = 7
}

public sealed record ManualTicketReviewFixProposalLoopRequest
{
    public required string LoopRunId { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public string? CampaignId { get; init; }
    public required string RequestedByUserId { get; init; }
    public required ManualTicketReviewTicketInput Ticket { get; init; }
    public ManualTicketReviewEvidenceBundle EvidenceBundle { get; init; } = new();
    public bool UseModelBackedCritic { get; init; }
    public bool UseModelBackedProposal { get; init; }
    public bool PersistToolExecutionAudit { get; init; }
    public DateTimeOffset RequestedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ManualTicketReviewTicketInput
{
    public required string TicketRef { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsSecret { get; init; }
    public bool IsAuthoritativeForAction { get; init; }
}

public sealed record ManualTicketReviewEvidenceBundle
{
    public IReadOnlyList<ManualTicketReviewEvidenceItem> Items { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsSecret { get; init; }
    public bool IsAuthoritativeForAction { get; init; }
}

public sealed record ManualTicketReviewEvidenceItem
{
    public required string EvidenceId { get; init; }
    public required string RefType { get; init; }
    public required string RefId { get; init; }
    public string Source { get; init; } = string.Empty;
    public required string Summary { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool SupportsReview { get; init; } = true;
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsSecret { get; init; }
    public bool IsAuthoritativeForAction { get; init; }
}

public sealed record ManualTicketReviewCriticStage
{
    public required bool Succeeded { get; init; }
    public required string CriticRunId { get; init; }
    public required string CriticProfileId { get; init; }
    public required string CriticReviewResultId { get; init; }
    public required CriticReviewVerdict Verdict { get; init; }
    public IReadOnlyList<CriticFinding> Findings { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsReviewOnly { get; init; } = true;
    public bool BlocksExecution { get; init; }
    public bool GrantsApproval { get; init; }
    public bool GrantsPolicyApproval { get; init; }
    public bool MutatesSource { get; init; }
    public bool ExecutesTool { get; init; }
}

public sealed record ManualTicketReviewProposalStage
{
    public required bool Succeeded { get; init; }
    public required string ManualProposalId { get; init; }
    public required string ToolRequestId { get; init; }
    public required string GateDecisionId { get; init; }
    public required AgentToolExecutionGateDecisionType GateDecision { get; init; }
    public ManualImplementationPatchProposalOutput? Output { get; init; }
    public PatchProposalPackage? Proposal { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsProposalOnly { get; init; } = true;
    public bool RequiresHumanReview { get; init; } = true;
    public bool MutatesSource { get; init; }
    public bool AppliesPatch { get; init; }
    public bool WritesFiles { get; init; }
    public bool DeletesFiles { get; init; }
    public bool RunsGit { get; init; }
    public bool CallsExternalSystem { get; init; }
    public bool SubmitsGitHubReview { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesAuthority { get; init; }
    public bool CreatesRuntimeAction { get; init; }
    public ToolExecutionAuditAppendStatus? ToolExecutionAuditStatus { get; init; }
    public string? ToolExecutionAuditId { get; init; }
}

public sealed record ManualTicketReviewLoopSummary
{
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> RecommendedNextActions { get; init; } = [];
    public IReadOnlyList<string> RequiredHumanDecisions { get; init; } = [];
    public IReadOnlyList<string> RequiredValidation { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsAdvisoryOnly { get; init; } = true;
    public bool GrantsApproval { get; init; }
    public bool GrantsPolicyApproval { get; init; }
    public bool GrantsExecutionPermission { get; init; }
    public bool MutatesSource { get; init; }
    public bool AppliesPatch { get; init; }
    public bool CreatesTicket { get; init; }
    public bool SubmitsGitHubReview { get; init; }
    public bool PromotesMemory { get; init; }
}

public sealed record ManualTicketReviewAuditStage
{
    public IReadOnlyList<string> AgentRunIds { get; init; } = [];
    public IReadOnlyList<string> AuditEnvelopeRefs { get; init; } = [];
    public IReadOnlyList<string> ToolExecutionAuditRefs { get; init; } = [];
    public bool PersistedToolExecutionAudit { get; init; }
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool GrantsAuthority { get; init; }
    public bool GrantsApproval { get; init; }
    public bool MutatesSource { get; init; }
    public bool ExecutesTool { get; init; }
}

public sealed record ManualTicketReviewFixProposalLoopIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public sealed record ManualTicketReviewFixProposalLoopResult
{
    public required bool Succeeded { get; init; }
    public required ManualTicketReviewFixProposalLoopStatus Status { get; init; }
    public required string LoopRunId { get; init; }
    public ManualTicketReviewCriticStage? CriticStage { get; init; }
    public ManualTicketReviewProposalStage? ProposalStage { get; init; }
    public ManualTicketReviewLoopSummary? Summary { get; init; }
    public ManualTicketReviewAuditStage? AuditStage { get; init; }
    public AgentRunAuditEnvelope? AuditEnvelope { get; init; }
    public IReadOnlyList<ManualTicketReviewFixProposalLoopIssue> Issues { get; init; } = [];
}

public interface IManualTicketReviewFixProposalLoopService
{
    Task<ManualTicketReviewFixProposalLoopResult> RunAsync(
        ManualTicketReviewFixProposalLoopRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ManualTicketReviewFixProposalLoopValidator
{
    public const string TicketLoopRequestRequired = "TICKET_LOOP_REQUEST_REQUIRED";
    public const string TicketLoopTicketRequired = "TICKET_LOOP_TICKET_REQUIRED";
    public const string TicketLoopEvidenceRequired = "TICKET_LOOP_EVIDENCE_REQUIRED";
    public const string TicketLoopUnsafeInput = "TICKET_LOOP_UNSAFE_INPUT";
    public const string TicketLoopCriticRejected = "TICKET_LOOP_CRITIC_REJECTED";
    public const string TicketLoopGateBlocked = "TICKET_LOOP_GATE_BLOCKED";
    public const string TicketLoopProposalRejected = "TICKET_LOOP_PROPOSAL_REJECTED";
    public const string TicketLoopAuditInvalid = "TICKET_LOOP_AUDIT_INVALID";
    public const string TicketLoopToolAuditRejected = "TICKET_LOOP_TOOL_AUDIT_REJECTED";

    private static readonly IReadOnlyList<string> RawPrivateReasoningMarkers =
    [
        "RawPrompt",
        "RawCompletion",
        "ChainOfThought",
        "Scratchpad",
        "PrivateReasoning",
        "hidden reasoning"
    ];

    private static readonly IReadOnlyList<string> AuthorityMarkers =
    [
        "approval granted",
        "policy cleared",
        "authoritative for action",
        "execute without gate",
        "applied patch",
        "source mutated",
        "github review submitted",
        "promoted memory",
        "accepted memory",
        "collective memory created"
    ];

    public IReadOnlyList<ManualTicketReviewFixProposalLoopIssue> Validate(
        ManualTicketReviewFixProposalLoopRequest? request,
        bool toolAuditStoreAvailable)
    {
        var issues = new List<ManualTicketReviewFixProposalLoopIssue>();
        if (request is null)
        {
            AddError(issues, TicketLoopRequestRequired, "Manual ticket review loop request is required.");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(request.LoopRunId))
            AddError(issues, TicketLoopRequestRequired, "LoopRunId is required.", nameof(request.LoopRunId));
        if (string.IsNullOrWhiteSpace(request.TenantId))
            AddError(issues, TicketLoopRequestRequired, "TenantId is required.", nameof(request.TenantId));
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            AddError(issues, TicketLoopRequestRequired, "ProjectId is required.", nameof(request.ProjectId));
        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
            AddError(issues, TicketLoopRequestRequired, "RequestedByUserId is required.", nameof(request.RequestedByUserId));
        if (request.UseModelBackedCritic)
            AddError(issues, TicketLoopUnsafeInput, "Model-backed critic execution is not wired into this manual loop slice.", nameof(request.UseModelBackedCritic));
        if (request.UseModelBackedProposal)
            AddError(issues, TicketLoopUnsafeInput, "Model-backed proposal execution is not allowed in this manual loop slice.", nameof(request.UseModelBackedProposal));
        if (request.PersistToolExecutionAudit && !toolAuditStoreAvailable)
            AddError(issues, TicketLoopUnsafeInput, "Tool execution audit persistence was requested without an explicit audit store.", nameof(request.PersistToolExecutionAudit));

        ValidateTicket(request.Ticket, issues);
        ValidateEvidenceBundle(request.EvidenceBundle, issues);
        return issues;
    }

    private static void ValidateTicket(ManualTicketReviewTicketInput? ticket, List<ManualTicketReviewFixProposalLoopIssue> issues)
    {
        if (ticket is null)
        {
            AddError(issues, TicketLoopTicketRequired, "Ticket input is required.", "Ticket");
            return;
        }

        if (string.IsNullOrWhiteSpace(ticket.TicketRef))
            AddError(issues, TicketLoopTicketRequired, "TicketRef is required.", "Ticket.TicketRef");
        if (string.IsNullOrWhiteSpace(ticket.Title))
            AddError(issues, TicketLoopTicketRequired, "Ticket title is required.", "Ticket.Title");
        if (string.IsNullOrWhiteSpace(ticket.Description))
            AddError(issues, TicketLoopTicketRequired, "Ticket description is required.", "Ticket.Description");
        if (ticket.ContainsRawPrivateReasoning || ticket.ContainsSecret || ticket.IsAuthoritativeForAction)
            AddError(issues, TicketLoopUnsafeInput, "Ticket input contains unsafe authority, secret, or private-reasoning flags.", "Ticket");

        ValidateText(ticket.Title, "Ticket.Title", issues);
        ValidateText(ticket.Description, "Ticket.Description", issues);
        foreach (var criterion in ticket.AcceptanceCriteria)
            ValidateText(criterion, "Ticket.AcceptanceCriteria", issues);
        foreach (var evidenceRef in ticket.EvidenceRefs)
            ValidateText(evidenceRef, "Ticket.EvidenceRefs", issues);
    }

    private static void ValidateEvidenceBundle(ManualTicketReviewEvidenceBundle? bundle, List<ManualTicketReviewFixProposalLoopIssue> issues)
    {
        if (bundle is null || bundle.Items.Count == 0)
        {
            AddError(issues, TicketLoopEvidenceRequired, "At least one evidence item is required.", "EvidenceBundle.Items");
            return;
        }

        if (bundle.ContainsRawPrivateReasoning || bundle.ContainsSecret || bundle.IsAuthoritativeForAction)
            AddError(issues, TicketLoopUnsafeInput, "Evidence bundle contains unsafe authority, secret, or private-reasoning flags.", "EvidenceBundle");

        foreach (var item in bundle.Items)
        {
            if (string.IsNullOrWhiteSpace(item.EvidenceId) ||
                string.IsNullOrWhiteSpace(item.RefType) ||
                string.IsNullOrWhiteSpace(item.RefId) ||
                string.IsNullOrWhiteSpace(item.Summary))
            {
                AddError(issues, TicketLoopEvidenceRequired, "Evidence item requires EvidenceId, RefType, RefId, and Summary.", "EvidenceBundle.Items");
            }

            if (item.ContainsRawPrivateReasoning || item.ContainsSecret || item.IsAuthoritativeForAction)
                AddError(issues, TicketLoopUnsafeInput, $"Evidence item '{item.EvidenceId}' contains unsafe authority, secret, or private-reasoning flags.", "EvidenceBundle.Items");

            ValidateText(item.EvidenceId, "EvidenceItem.EvidenceId", issues);
            ValidateText(item.RefType, "EvidenceItem.RefType", issues);
            ValidateText(item.RefId, "EvidenceItem.RefId", issues);
            ValidateText(item.Source, "EvidenceItem.Source", issues);
            ValidateText(item.Summary, "EvidenceItem.Summary", issues);
            foreach (var evidenceRef in item.EvidenceRefs)
                ValidateText(evidenceRef, "EvidenceItem.EvidenceRefs", issues);
        }
    }

    private static void ValidateText(string? value, string field, List<ManualTicketReviewFixProposalLoopIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var marker in RawPrivateReasoningMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                AddError(issues, TicketLoopUnsafeInput, $"{field} contains raw/private reasoning marker '{marker}'.", field);
        }

        foreach (var marker in AuthorityMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                AddError(issues, TicketLoopUnsafeInput, $"{field} contains authority or execution marker '{marker}'.", field);
        }
    }

    public static ManualTicketReviewFixProposalLoopIssue Issue(string code, string severity, string message, string field = "") =>
        new() { Code = code, Severity = severity, Message = message, Field = field };

    private static void AddError(List<ManualTicketReviewFixProposalLoopIssue> issues, string code, string message, string field = "") =>
        issues.Add(Issue(code, AgentDefinitionValidator.SeverityError, message, field));
}

public sealed class ManualTicketReviewFixProposalLoopService : IManualTicketReviewFixProposalLoopService
{
    private readonly IManualIndependentCriticAgentService _criticService;
    private readonly IManualImplementationAgentPatchProposalService _proposalService;
    private readonly IAgentToolExecutionGate _gate;
    private readonly IToolExecutionAuditStore? _toolExecutionAuditStore;
    private readonly ManualTicketReviewFixProposalLoopValidator _validator;
    private readonly CriticReviewResultValidator _criticValidator;
    private readonly AgentRunAuditEnvelopeValidator _auditValidator;
    private readonly ThoughtLedgerSafetyValidator _thoughtLedgerValidator;
    private readonly ToolExecutionAuditValidator _toolExecutionAuditValidator;

    public ManualTicketReviewFixProposalLoopService()
        : this(
            new ManualIndependentCriticAgentService(),
            new ManualImplementationAgentPatchProposalService(),
            new AgentToolExecutionGate(),
            toolExecutionAuditStore: null)
    {
    }

    public ManualTicketReviewFixProposalLoopService(
        IManualIndependentCriticAgentService criticService,
        IManualImplementationAgentPatchProposalService proposalService,
        IAgentToolExecutionGate gate,
        IToolExecutionAuditStore? toolExecutionAuditStore = null,
        ManualTicketReviewFixProposalLoopValidator? validator = null,
        CriticReviewResultValidator? criticValidator = null,
        AgentRunAuditEnvelopeValidator? auditValidator = null,
        ThoughtLedgerSafetyValidator? thoughtLedgerValidator = null,
        ToolExecutionAuditValidator? toolExecutionAuditValidator = null)
    {
        _criticService = criticService;
        _proposalService = proposalService;
        _gate = gate;
        _toolExecutionAuditStore = toolExecutionAuditStore;
        _validator = validator ?? new ManualTicketReviewFixProposalLoopValidator();
        _criticValidator = criticValidator ?? new CriticReviewResultValidator();
        _auditValidator = auditValidator ?? new AgentRunAuditEnvelopeValidator();
        _thoughtLedgerValidator = thoughtLedgerValidator ?? new ThoughtLedgerSafetyValidator();
        _toolExecutionAuditValidator = toolExecutionAuditValidator ?? new ToolExecutionAuditValidator();
    }

    public async Task<ManualTicketReviewFixProposalLoopResult> RunAsync(
        ManualTicketReviewFixProposalLoopRequest request,
        CancellationToken cancellationToken = default)
    {
        var issues = _validator.Validate(request, _toolExecutionAuditStore is not null);
        if (issues.Count > 0)
            return Rejected(request?.LoopRunId, ManualTicketReviewFixProposalLoopStatus.InvalidRequest, issues);

        var evidenceRefs = CollectRequestEvidenceRefs(request);
        var criticResult = _criticService.Review(BuildCriticRequest(request, evidenceRefs), request.RequestedAtUtc);
        var criticIssues = ValidateCriticResult(criticResult);
        if (criticIssues.Count > 0)
        {
            return Rejected(
                request.LoopRunId,
                ManualTicketReviewFixProposalLoopStatus.CriticRejected,
                criticIssues,
                criticStage: BuildCriticStage(criticResult, evidenceRefs, succeeded: false));
        }

        var criticStage = BuildCriticStage(criticResult, evidenceRefs, succeeded: true);
        var toolRequest = BuildPatchProposalToolRequest(request, criticResult.CriticReviewResult!, evidenceRefs);
        var gateResult = _gate.Evaluate(new AgentToolExecutionGateRequest
        {
            ToolRequest = toolRequest,
            PolicyContext = new AgentToolExecutionGatePolicyContext
            {
                PolicyKnown = true,
                AllowsToolRequest = true,
                AllowsPatchProposal = true,
                PolicyRefs = ["policy:manual-ticket-review-fix-proposal-loop"]
            },
            EvaluatedAtUtc = request.RequestedAtUtc
        });

        var gateIssue = ValidateGate(gateResult);
        if (gateIssue is not null)
        {
            return Rejected(
                request.LoopRunId,
                gateResult.Decision?.Decision == AgentToolExecutionGateDecisionType.RequiresApproval
                    ? ManualTicketReviewFixProposalLoopStatus.NeedsHumanReview
                    : ManualTicketReviewFixProposalLoopStatus.Blocked,
                [gateIssue],
                criticStage: criticStage);
        }

        var proposalResult = _proposalService.Propose(BuildProposalRequest(request, toolRequest, gateResult.Decision!, criticResult.CriticReviewResult!, evidenceRefs));
        var proposalIssues = ValidateProposalResult(proposalResult);
        if (proposalIssues.Count > 0)
        {
            return Rejected(
                request.LoopRunId,
                ManualTicketReviewFixProposalLoopStatus.ProposalRejected,
                proposalIssues,
                criticStage,
                BuildProposalStage(proposalResult, gateResult.Decision!, null));
        }

        var auditAppendResult = request.PersistToolExecutionAudit
            ? await AppendToolAuditAsync(proposalResult, request.RequestedAtUtc, cancellationToken).ConfigureAwait(false)
            : null;

        if (auditAppendResult is { Status: ToolExecutionAuditAppendStatus.Conflict or ToolExecutionAuditAppendStatus.Rejected })
        {
            var appendIssues = auditAppendResult.Issues.Count > 0
                ? auditAppendResult.Issues.Select(ToLoopIssue).ToArray()
                : [ManualTicketReviewFixProposalLoopValidator.Issue(ManualTicketReviewFixProposalLoopValidator.TicketLoopToolAuditRejected, AgentDefinitionValidator.SeverityError, "Tool execution audit append was rejected.")];

            return Rejected(
                request.LoopRunId,
                ManualTicketReviewFixProposalLoopStatus.NeedsHumanReview,
                appendIssues,
                criticStage,
                BuildProposalStage(proposalResult, gateResult.Decision!, auditAppendResult));
        }

        var proposalStage = BuildProposalStage(proposalResult, gateResult.Decision!, auditAppendResult);
        var summary = BuildSummary(request, criticStage, proposalStage);
        var auditEnvelope = BuildLoopAuditEnvelope(request, criticResult, proposalResult, summary, evidenceRefs);
        var auditIssues = ValidateAuditEnvelope(auditEnvelope);
        if (auditIssues.Count > 0)
        {
            return Rejected(
                request.LoopRunId,
                ManualTicketReviewFixProposalLoopStatus.Failed,
                auditIssues,
                criticStage,
                proposalStage);
        }

        return new ManualTicketReviewFixProposalLoopResult
        {
            Succeeded = true,
            Status = ManualTicketReviewFixProposalLoopStatus.Succeeded,
            LoopRunId = request.LoopRunId,
            CriticStage = criticStage,
            ProposalStage = proposalStage,
            Summary = summary,
            AuditStage = BuildAuditStage(request, criticResult, proposalResult, auditAppendResult),
            AuditEnvelope = auditEnvelope,
            Issues = []
        };
    }

    private static ManualCriticReviewRequest BuildCriticRequest(
        ManualTicketReviewFixProposalLoopRequest request,
        IReadOnlyList<string> evidenceRefs)
    {
        var inputs = new List<ManualCriticReviewInputRef>
        {
            new()
            {
                InputRefId = $"ticket-input-{request.LoopRunId}",
                RefType = "Ticket",
                RefId = request.Ticket.TicketRef,
                Source = "manual-ticket-review-loop",
                Summary = $"Ticket {request.Ticket.TicketRef}: {request.Ticket.Title}",
                EvidenceRefs = request.Ticket.EvidenceRefs,
                ContainsRawPrivateReasoning = false,
                IsAuthoritativeForAction = false
            }
        };

        inputs.AddRange(request.EvidenceBundle.Items.Select(item => new ManualCriticReviewInputRef
        {
            InputRefId = $"evidence-input-{item.EvidenceId}",
            RefType = item.RefType,
            RefId = item.RefId,
            Source = item.Source,
            Summary = item.Summary,
            EvidenceRefs = item.EvidenceRefs.Count > 0 ? item.EvidenceRefs : [item.EvidenceId],
            ContainsRawPrivateReasoning = false,
            IsAuthoritativeForAction = false
        }));

        var requiredFix = request.Ticket.AcceptanceCriteria.Count > 0
            ? $"Draft a proposal-only implementation plan that addresses: {request.Ticket.AcceptanceCriteria[0]}"
            : "Draft a proposal-only implementation package for human review. Do not apply patches.";

        return new ManualCriticReviewRequest
        {
            ReviewRequestId = $"ticket-review-{request.LoopRunId}",
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            CampaignId = CampaignId(request),
            RunId = request.LoopRunId,
            SubjectType = CriticReviewSubjectType.Ticket,
            SubjectId = request.Ticket.TicketRef,
            RequestedByUserId = request.RequestedByUserId,
            CorrelationId = request.LoopRunId,
            RequestSummary = $"Review ticket {request.Ticket.TicketRef} and identify the fix proposal boundary.",
            Inputs = inputs,
            FindingDrafts =
            [
                new ManualCriticFindingDraft
                {
                    Severity = CriticSeverity.Medium,
                    Title = $"Ticket needs proposal-only fix: {request.Ticket.Title}",
                    Problem = request.Ticket.Description,
                    WhyItMatters = "The ticket should be reviewed before any implementation proposal is treated as actionable evidence.",
                    RequiredFix = requiredFix,
                    EvidenceRefs = evidenceRefs,
                    BlocksMerge = false,
                    RequiresHumanReview = true
                }
            ],
            RequestedVerdict = CriticReviewVerdict.RequestChanges
        };
    }

    private static AgentToolRequest BuildPatchProposalToolRequest(
        ManualTicketReviewFixProposalLoopRequest request,
        CriticReviewResult criticReview,
        IReadOnlyList<string> evidenceRefs)
    {
        var implementation = AgentDefinitionCatalog.ImplementationAgent;

        return new AgentToolRequest
        {
            ToolRequestId = $"ticket-loop-patch-proposal-{request.LoopRunId}",
            Status = AgentToolRequestStatus.PendingGate,
            RequestType = AgentToolRequestType.PatchProposalRequest,
            ToolKind = AgentToolKind.PatchProposal,
            RiskLevel = AgentToolRiskLevel.Medium,
            Scope = new AgentToolRequestScope
            {
                TenantId = request.TenantId,
                ProjectId = request.ProjectId,
                CampaignId = request.CampaignId,
                RunId = request.LoopRunId,
                AgentRunId = $"implementation-proposal-{request.LoopRunId}",
                CorrelationId = request.LoopRunId
            },
            Actor = new AgentToolRequestActor
            {
                AgentId = implementation.AgentId,
                AgentName = implementation.Name,
                AgentKind = implementation.Kind,
                ExecutionMode = implementation.ExecutionMode,
                DeclaredCapabilities = implementation.Capabilities?.ToArray() ?? [],
                ForbiddenCapabilities = implementation.ForbiddenCapabilities?.ToArray() ?? []
            },
            Purpose = $"Create a proposal-only fix package for ticket {request.Ticket.TicketRef} using critic review {criticReview.ReviewResultId}.",
            Inputs =
            [
                new AgentToolRequestInput
                {
                    InputId = $"ticket-{request.Ticket.TicketRef}",
                    RefType = "Ticket",
                    RefId = request.Ticket.TicketRef,
                    Source = "manual-ticket-review-loop",
                    Summary = request.Ticket.Title,
                    EvidenceRefs = request.Ticket.EvidenceRefs,
                    IsSanitised = true
                },
                new AgentToolRequestInput
                {
                    InputId = criticReview.ReviewResultId,
                    RefType = "CriticReviewResult",
                    RefId = criticReview.ReviewResultId,
                    Source = "IndependentCriticAgent",
                    Summary = $"Critic verdict: {criticReview.Verdict}.",
                    EvidenceRefs = evidenceRefs,
                    IsSanitised = true
                }
            ],
            Evidence = evidenceRefs.Select(evidenceRef => new AgentToolRequestEvidence
            {
                EvidenceId = $"evidence-{StableToken(evidenceRef)}",
                RefType = "EvidenceRef",
                RefId = evidenceRef,
                Summary = evidenceRef,
                SupportsNeedForTool = true
            }).ToArray(),
            ApprovalRequirement = new AgentToolRequestApprovalRequirement
            {
                Reason = "Patch proposal requests are proposal-only and still require the deterministic tool gate."
            },
            PolicySnapshot = new AgentToolRequestPolicySnapshot
            {
                PolicyKnown = true,
                AllowsToolRequest = true,
                PolicyRefs = ["policy:manual-ticket-review-fix-proposal-loop"]
            },
            RequestedAtUtc = request.RequestedAtUtc
        };
    }

    private static ManualImplementationPatchProposalRequest BuildProposalRequest(
        ManualTicketReviewFixProposalLoopRequest request,
        AgentToolRequest toolRequest,
        AgentToolExecutionGateDecision gateDecision,
        CriticReviewResult criticReview,
        IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            ManualProposalId = $"ticket-loop-proposal-{request.LoopRunId}",
            ToolRequest = toolRequest,
            GateDecision = gateDecision,
            RequestedByUserId = request.RequestedByUserId,
            ProposalGoal = $"Produce a proposal-only fix package for ticket {request.Ticket.TicketRef}; do not apply changes.",
            Inputs =
            [
                new PatchProposalInputRef
                {
                    InputRefId = $"proposal-ticket-{request.Ticket.TicketRef}",
                    RefType = "Ticket",
                    RefId = request.Ticket.TicketRef,
                    Source = "manual-ticket-review-loop",
                    Summary = request.Ticket.Title,
                    EvidenceRefs = request.Ticket.EvidenceRefs,
                    IsSanitised = true
                },
                new PatchProposalInputRef
                {
                    InputRefId = $"proposal-critic-{criticReview.ReviewResultId}",
                    RefType = "CriticReviewResult",
                    RefId = criticReview.ReviewResultId,
                    Source = "IndependentCriticAgent",
                    Summary = $"Critic verdict: {criticReview.Verdict}.",
                    EvidenceRefs = evidenceRefs,
                    IsSanitised = true
                }
            ],
            Parameters = new Dictionary<string, string>
            {
                ["ticketRef"] = request.Ticket.TicketRef,
                ["criticReviewResultId"] = criticReview.ReviewResultId,
                ["loopRunId"] = request.LoopRunId
            },
            RequestedAtUtc = request.RequestedAtUtc
        };

    private IReadOnlyList<ManualTicketReviewFixProposalLoopIssue> ValidateCriticResult(ManualCriticReviewResult result)
    {
        var issues = new List<ManualTicketReviewFixProposalLoopIssue>();
        if (!result.Succeeded || result.CriticReviewResult is null || result.AuditEnvelope is null)
        {
            issues.AddRange(result.Issues.Select(issue => ManualTicketReviewFixProposalLoopValidator.Issue(
                ManualTicketReviewFixProposalLoopValidator.TicketLoopCriticRejected,
                issue.Severity,
                issue.Message,
                issue.Field ?? string.Empty)));

            if (issues.Count == 0)
            {
                issues.Add(ManualTicketReviewFixProposalLoopValidator.Issue(
                    ManualTicketReviewFixProposalLoopValidator.TicketLoopCriticRejected,
                    AgentDefinitionValidator.SeverityError,
                    "Manual critic did not produce a review result and audit envelope."));
            }

            return issues;
        }

        issues.AddRange(_criticValidator.Validate(result.CriticReviewResult).Select(issue => ToLoopIssue(issue, ManualTicketReviewFixProposalLoopValidator.TicketLoopCriticRejected)));
        issues.AddRange(ValidateAudit(result.AuditEnvelope, ManualTicketReviewFixProposalLoopValidator.TicketLoopCriticRejected));

        if (result.CriticReviewResult.Verdict == CriticReviewVerdict.NoObjection)
        {
            issues.Add(ManualTicketReviewFixProposalLoopValidator.Issue(
                ManualTicketReviewFixProposalLoopValidator.TicketLoopCriticRejected,
                AgentDefinitionValidator.SeverityError,
                "Manual ticket review loop requires an actionable critic finding before a fix proposal can be requested."));
        }

        if (result.AuditEnvelope.BoundaryDecisions.Any(decision => decision.GrantsAuthority || decision.GrantsHumanApproval || decision.GrantsPolicyApproval || decision.GrantsMemoryPromotion))
        {
            issues.Add(ManualTicketReviewFixProposalLoopValidator.Issue(
                ManualTicketReviewFixProposalLoopValidator.TicketLoopCriticRejected,
                AgentDefinitionValidator.SeverityError,
                "Critic audit envelope attempted to grant authority, approval, policy approval, or memory promotion."));
        }

        return issues;
    }

    private static ManualTicketReviewFixProposalLoopIssue? ValidateGate(AgentToolExecutionGateResult gateResult)
    {
        if (!gateResult.Succeeded || gateResult.Decision is null)
        {
            return ManualTicketReviewFixProposalLoopValidator.Issue(
                ManualTicketReviewFixProposalLoopValidator.TicketLoopGateBlocked,
                AgentDefinitionValidator.SeverityError,
                "Patch proposal tool request gate did not produce a decision.");
        }

        var decision = gateResult.Decision;
        if (decision.Decision != AgentToolExecutionGateDecisionType.Allowed || !decision.GrantsExecution)
        {
            return ManualTicketReviewFixProposalLoopValidator.Issue(
                ManualTicketReviewFixProposalLoopValidator.TicketLoopGateBlocked,
                decision.Decision == AgentToolExecutionGateDecisionType.RequiresApproval ? AgentDefinitionValidator.SeverityWarning : AgentDefinitionValidator.SeverityError,
                $"Patch proposal gate returned {decision.Decision}.");
        }

        if (decision.ExecutesTool || decision.MutatesSource || decision.CallsExternalSystem || decision.SubmitsGitHubReview || decision.PromotesMemory || decision.CreatesCollectiveMemory || decision.WritesWeaviate)
        {
            return ManualTicketReviewFixProposalLoopValidator.Issue(
                ManualTicketReviewFixProposalLoopValidator.TicketLoopGateBlocked,
                AgentDefinitionValidator.SeverityError,
                "Patch proposal gate decision carried unsafe execution, source, external, or memory authority flags.");
        }

        return null;
    }

    private IReadOnlyList<ManualTicketReviewFixProposalLoopIssue> ValidateProposalResult(ManualImplementationPatchProposalResult result)
    {
        var issues = new List<ManualTicketReviewFixProposalLoopIssue>();
        if (!result.Succeeded || result.Output is null || result.AuditEnvelope is null)
        {
            issues.AddRange(result.Issues.Select(issue => ManualTicketReviewFixProposalLoopValidator.Issue(
                ManualTicketReviewFixProposalLoopValidator.TicketLoopProposalRejected,
                issue.Severity,
                issue.Message,
                issue.Field ?? string.Empty)));

            if (issues.Count == 0)
            {
                issues.Add(ManualTicketReviewFixProposalLoopValidator.Issue(
                    ManualTicketReviewFixProposalLoopValidator.TicketLoopProposalRejected,
                    AgentDefinitionValidator.SeverityError,
                    "Manual implementation proposal did not produce proposal output and audit envelope."));
            }

            return issues;
        }

        var output = result.Output;
        if (!output.Proposal.IsProposalOnly ||
            !output.Proposal.RequiresHumanReview ||
            !output.Proposal.RequiresValidation ||
            output.ContainsRawPrivateReasoning ||
            output.MutatesSource ||
            output.AppliesPatch ||
            output.WritesFiles ||
            output.DeletesFiles ||
            output.RunsGit ||
            output.CallsExternalSystem ||
            output.SubmitsGitHubReview ||
            output.CreatesPullRequest ||
            output.PromotesMemory ||
            output.CreatesCollectiveMemory ||
            output.WritesWeaviate ||
            output.Proposal.CreatesAuthority ||
            output.Proposal.CreatesRuntimeAction ||
            output.Proposal.MutatesSource ||
            output.Proposal.AppliesPatch)
        {
            issues.Add(ManualTicketReviewFixProposalLoopValidator.Issue(
                ManualTicketReviewFixProposalLoopValidator.TicketLoopProposalRejected,
                AgentDefinitionValidator.SeverityError,
                "Implementation proposal output must remain proposal-only, human-reviewed, validation-required, and non-mutating."));
        }

        foreach (var change in output.Proposal.FileChanges)
        {
            if (!change.IsProposalOnly || change.WritesFile || change.DeletesFile || change.AppliesPatch)
            {
                issues.Add(ManualTicketReviewFixProposalLoopValidator.Issue(
                    ManualTicketReviewFixProposalLoopValidator.TicketLoopProposalRejected,
                    AgentDefinitionValidator.SeverityError,
                    $"Proposed file change '{change.FileChangeId}' claimed file mutation or application authority."));
            }

            foreach (var hunk in change.Hunks)
            {
                if (hunk.ContainsRawPrivateReasoning || hunk.ContainsSecret || hunk.ClaimsApplied)
                {
                    issues.Add(ManualTicketReviewFixProposalLoopValidator.Issue(
                        ManualTicketReviewFixProposalLoopValidator.TicketLoopProposalRejected,
                        AgentDefinitionValidator.SeverityError,
                        $"Proposed hunk '{hunk.HunkId}' contains unsafe flags or claims it was applied."));
                }
            }
        }

        issues.AddRange(ValidateAudit(result.AuditEnvelope, ManualTicketReviewFixProposalLoopValidator.TicketLoopProposalRejected));
        return issues;
    }

    private async Task<ToolExecutionAuditAppendResult> AppendToolAuditAsync(
        ManualImplementationPatchProposalResult proposalResult,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var record = ToolExecutionAuditRecordFactory.FromManualImplementationPatchProposalResult(proposalResult, createdAtUtc);
            var validationIssues = _toolExecutionAuditValidator.Validate(record);
            if (validationIssues.Count > 0)
            {
                return new ToolExecutionAuditAppendResult
                {
                    Status = ToolExecutionAuditAppendStatus.Rejected,
                    ToolExecutionAuditId = record.ToolExecutionAuditId,
                    PayloadSha256 = record.PayloadSha256,
                    AuditEnvelopeSha256 = record.AuditEnvelopeSha256,
                    Issues = validationIssues
                };
            }

            return await _toolExecutionAuditStore!.AppendAsync(new ToolExecutionAuditAppendRequest { Record = record }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ToolExecutionAuditAppendResult
            {
                Status = ToolExecutionAuditAppendStatus.Rejected,
                Issues =
                [
                    new ToolExecutionAuditIssue
                    {
                        Code = ManualTicketReviewFixProposalLoopValidator.TicketLoopToolAuditRejected,
                        Severity = AgentDefinitionValidator.SeverityError,
                        Message = ex.Message
                    }
                ]
            };
        }
    }

    private IReadOnlyList<ManualTicketReviewFixProposalLoopIssue> ValidateAudit(
        AgentRunAuditEnvelope auditEnvelope,
        string code) =>
        _auditValidator.Validate(auditEnvelope)
            .Concat(_thoughtLedgerValidator.Validate(auditEnvelope.ThoughtLedger))
            .Select(issue => ToLoopIssue(issue, code))
            .ToArray();

    private IReadOnlyList<ManualTicketReviewFixProposalLoopIssue> ValidateAuditEnvelope(AgentRunAuditEnvelope auditEnvelope) =>
        ValidateAudit(auditEnvelope, ManualTicketReviewFixProposalLoopValidator.TicketLoopAuditInvalid);

    private static ManualTicketReviewCriticStage BuildCriticStage(
        ManualCriticReviewResult result,
        IReadOnlyList<string> evidenceRefs,
        bool succeeded)
    {
        var review = result.CriticReviewResult;
        return new ManualTicketReviewCriticStage
        {
            Succeeded = succeeded,
            CriticRunId = result.ManualCriticRunId,
            CriticProfileId = AgentSpecialisationCatalog.CodeReviewCritic.SpecialisationId,
            CriticReviewResultId = review?.ReviewResultId ?? string.Empty,
            Verdict = review?.Verdict ?? CriticReviewVerdict.CommentOnly,
            Findings = review?.Findings ?? [],
            EvidenceRefs = evidenceRefs,
            IsReviewOnly = true,
            BlocksExecution = false,
            GrantsApproval = false,
            GrantsPolicyApproval = false,
            MutatesSource = false,
            ExecutesTool = false
        };
    }

    private static ManualTicketReviewProposalStage BuildProposalStage(
        ManualImplementationPatchProposalResult result,
        AgentToolExecutionGateDecision gateDecision,
        ToolExecutionAuditAppendResult? auditAppendResult) =>
        new()
        {
            Succeeded = result.Succeeded,
            ManualProposalId = result.ManualProposalId,
            ToolRequestId = result.ToolRequestId ?? gateDecision.ToolRequestId,
            GateDecisionId = result.GateDecisionId ?? gateDecision.GateDecisionId,
            GateDecision = gateDecision.Decision,
            Output = result.Output,
            Proposal = result.Output?.Proposal,
            EvidenceRefs = result.Output?.EvidenceRefs ?? [],
            IsProposalOnly = true,
            RequiresHumanReview = true,
            MutatesSource = false,
            AppliesPatch = false,
            WritesFiles = false,
            DeletesFiles = false,
            RunsGit = false,
            CallsExternalSystem = false,
            SubmitsGitHubReview = false,
            PromotesMemory = false,
            CreatesAuthority = false,
            CreatesRuntimeAction = false,
            ToolExecutionAuditStatus = auditAppendResult?.Status,
            ToolExecutionAuditId = auditAppendResult?.ToolExecutionAuditId
        };

    private static ManualTicketReviewLoopSummary BuildSummary(
        ManualTicketReviewFixProposalLoopRequest request,
        ManualTicketReviewCriticStage criticStage,
        ManualTicketReviewProposalStage proposalStage)
    {
        var evidenceRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var evidenceRef in criticStage.EvidenceRefs.Concat(proposalStage.EvidenceRefs))
            evidenceRefs.Add(evidenceRef);

        return new ManualTicketReviewLoopSummary
        {
            Title = "Manual ticket review fix proposal ready for human review",
            Summary = $"Ticket {request.Ticket.TicketRef} was reviewed by IndependentCriticAgent and converted into a proposal-only ImplementationAgent fix package.",
            RecommendedNextActions =
            [
                "Human reviewer should inspect the critic findings and proposal package.",
                "Run validation separately before any source-changing apply path.",
                "Do not treat this loop result as approval, execution permission, or applied source change."
            ],
            RequiredHumanDecisions =
            [
                "Decide whether the proposal should enter a governed apply path.",
                "Decide whether additional ticket clarification is required."
            ],
            RequiredValidation =
            [
                "Validate any future patch in a disposable workspace before apply.",
                "Capture promotion approval evidence separately if the proposal advances."
            ],
            EvidenceRefs = evidenceRefs.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            IsAdvisoryOnly = true,
            GrantsApproval = false,
            GrantsPolicyApproval = false,
            GrantsExecutionPermission = false,
            MutatesSource = false,
            AppliesPatch = false,
            CreatesTicket = false,
            SubmitsGitHubReview = false,
            PromotesMemory = false
        };
    }

    private static AgentRunAuditEnvelope BuildLoopAuditEnvelope(
        ManualTicketReviewFixProposalLoopRequest request,
        ManualCriticReviewResult criticResult,
        ManualImplementationPatchProposalResult proposalResult,
        ManualTicketReviewLoopSummary summary,
        IReadOnlyList<string> evidenceRefs)
    {
        var agent = AgentDefinitionCatalog.ReportingAgent;
        var agentRunId = $"manual-ticket-review-loop-{request.LoopRunId}";
        var now = request.RequestedAtUtc;
        var outputEvidence = summary.EvidenceRefs.Count > 0 ? summary.EvidenceRefs : evidenceRefs;

        return new AgentRunAuditEnvelope
        {
            Run = new AgentRunRecord
            {
                AgentRunId = agentRunId,
                TenantId = request.TenantId,
                ProjectId = request.ProjectId,
                CampaignId = CampaignId(request),
                RunId = request.LoopRunId,
                AgentId = agent.AgentId,
                AgentName = agent.Name,
                RequestedByUserId = request.RequestedByUserId,
                TriggerType = AgentRunTriggerType.ManualGovernedRequest,
                Status = AuditAgentRunStatus.Completed,
                RequestSummary = $"Manual ticket review fix proposal loop for {request.Ticket.TicketRef}.",
                Purpose = "Coordinate review-only critic output and proposal-only implementation output without applying changes.",
                CreatedAtUtc = now,
                StartedAtUtc = now,
                CompletedAtUtc = now
            },
            AgentDefinitionSnapshot = agent,
            Inputs =
            [
                new AgentRunInputRef
                {
                    InputRefId = $"loop-ticket-{request.Ticket.TicketRef}",
                    AgentRunId = agentRunId,
                    RefType = "Ticket",
                    RefId = request.Ticket.TicketRef,
                    Source = "manual-user-request",
                    Summary = request.Ticket.Title,
                    IsAuthoritativeForAction = false,
                    ContainsRawPrivateReasoning = false
                },
                new AgentRunInputRef
                {
                    InputRefId = $"loop-critic-{criticResult.ManualCriticRunId}",
                    AgentRunId = agentRunId,
                    RefType = "CriticReviewResult",
                    RefId = criticResult.CriticReviewResult?.ReviewResultId ?? criticResult.ManualCriticRunId,
                    Source = "IndependentCriticAgent",
                    Summary = "Review-only critic findings used as evidence for a proposal-only implementation package.",
                    IsAuthoritativeForAction = false,
                    ContainsRawPrivateReasoning = false
                },
                new AgentRunInputRef
                {
                    InputRefId = $"loop-proposal-{proposalResult.ManualProposalId}",
                    AgentRunId = agentRunId,
                    RefType = "PatchProposalPackage",
                    RefId = proposalResult.Output?.Proposal.PatchProposalId ?? proposalResult.ManualProposalId,
                    Source = "ImplementationAgent",
                    Summary = "Proposal-only implementation output; not applied.",
                    IsAuthoritativeForAction = false,
                    ContainsRawPrivateReasoning = false
                }
            ],
            Outputs =
            [
                new AgentRunOutputRef
                {
                    OutputRefId = $"loop-summary-{request.LoopRunId}",
                    AgentRunId = agentRunId,
                    RefType = "ManualTicketReviewFixProposalLoopSummary",
                    RefId = request.LoopRunId,
                    Summary = summary.Summary,
                    IsReviewOnly = true,
                    IsProposalOnly = true,
                    CreatesAuthority = false,
                    CreatesRuntimeAction = false,
                    ContainsRawPrivateReasoning = false,
                    EvidenceRefs = outputEvidence
                }
            ],
            Steps =
            [
                Step(agentRunId, 1, AgentRunStepType.Created, now, "Manual ticket review fix proposal loop started.", evidenceRefs),
                Step(agentRunId, 2, AgentRunStepType.InputBound, now, "Ticket, critic review, and proposal evidence were bound as non-authoritative inputs.", evidenceRefs),
                Step(agentRunId, 3, AgentRunStepType.CapabilityEvaluated, now, "Loop confirmed it can report evidence but cannot execute tools or mutate source.", evidenceRefs),
                Step(agentRunId, 4, AgentRunStepType.OutputRecorded, now, "Proposal-only loop summary recorded for human review.", outputEvidence),
                Step(agentRunId, 5, AgentRunStepType.Completed, now, "Manual loop completed without applying source changes.", outputEvidence)
            ],
            CapabilityUses =
            [
                Capability(agentRunId, AgentCapability.CreateReport, AgentCapabilityUseOutcome.Allowed, true, false, "Created a loop summary report."),
                Capability(agentRunId, AgentCapability.RunTool, AgentCapabilityUseOutcome.Blocked, false, false, "Loop does not run tools."),
                Capability(agentRunId, AgentCapability.MutateSource, AgentCapabilityUseOutcome.Blocked, false, false, "Loop does not mutate source."),
                Capability(agentRunId, AgentCapability.CallExternalSystem, AgentCapabilityUseOutcome.Blocked, false, false, "Loop does not call external systems."),
                Capability(agentRunId, AgentCapability.PromoteCollectiveMemory, AgentCapabilityUseOutcome.Blocked, false, false, "Loop does not change memory authority."),
                Capability(agentRunId, AgentCapability.RepresentHumanApproval, AgentCapabilityUseOutcome.Blocked, false, false, "Loop does not represent human approval.")
            ],
            BoundaryDecisions =
            [
                Boundary(agentRunId, AgentBoundaryDecisionType.Policy, "warn", "Loop output is advisory and does not grant approval or execution permission.", evidenceRefs),
                Boundary(agentRunId, AgentBoundaryDecisionType.Safety, "blocked", "Source mutation, patch application, GitHub submission, and external calls are outside this loop.", evidenceRefs),
                Boundary(agentRunId, AgentBoundaryDecisionType.Output, "allowed", "Only review/proposal summary output was recorded.", outputEvidence)
            ],
            ThoughtLedger =
            [
                Thought(agentRunId, "loop-thought-evidence", ThoughtLedgerEntryType.EvidenceUsed, "Critic review and implementation proposal evidence were used to create a human-review package.", evidenceRefs, now),
                Thought(agentRunId, "loop-thought-boundary", ThoughtLedgerEntryType.BoundaryDecision, "The loop cannot approve, apply, mutate source, submit reviews, or change memory authority.", outputEvidence, now, risks: ["Human review and separate validation remain required."])
            ]
        };
    }

    private static ManualTicketReviewAuditStage BuildAuditStage(
        ManualTicketReviewFixProposalLoopRequest request,
        ManualCriticReviewResult criticResult,
        ManualImplementationPatchProposalResult proposalResult,
        ToolExecutionAuditAppendResult? auditAppendResult)
    {
        var toolAuditRefs = auditAppendResult is { Status: ToolExecutionAuditAppendStatus.Appended or ToolExecutionAuditAppendStatus.AlreadyExists } &&
                            !string.IsNullOrWhiteSpace(auditAppendResult.ToolExecutionAuditId)
            ? new[] { $"tool-execution-audit:{auditAppendResult.ToolExecutionAuditId}" }
            : [];

        return new ManualTicketReviewAuditStage
        {
            AgentRunIds =
            [
                criticResult.AuditEnvelope?.Run.AgentRunId ?? criticResult.ManualCriticRunId,
                proposalResult.AuditEnvelope?.Run.AgentRunId ?? proposalResult.ManualProposalId,
                $"manual-ticket-review-loop-{request.LoopRunId}"
            ],
            AuditEnvelopeRefs =
            [
                $"agent-run-audit:{criticResult.AuditEnvelope?.Run.AgentRunId ?? criticResult.ManualCriticRunId}",
                $"agent-run-audit:{proposalResult.AuditEnvelope?.Run.AgentRunId ?? proposalResult.ManualProposalId}",
                $"agent-run-audit:manual-ticket-review-loop-{request.LoopRunId}"
            ],
            ToolExecutionAuditRefs = toolAuditRefs,
            PersistedToolExecutionAudit = toolAuditRefs.Length > 0,
            ContainsRawPrivateReasoning = false,
            GrantsAuthority = false,
            GrantsApproval = false,
            MutatesSource = false,
            ExecutesTool = false
        };
    }

    private static AgentRunStep Step(string agentRunId, int sequence, AgentRunStepType stepType, DateTimeOffset occurredAtUtc, string summary, IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            StepId = $"{agentRunId}-step-{sequence}",
            AgentRunId = agentRunId,
            Sequence = sequence,
            StepType = stepType,
            OccurredAtUtc = occurredAtUtc,
            Summary = summary,
            ContainsRawPrivateReasoning = false,
            EvidenceRefs = evidenceRefs.Count > 0 ? evidenceRefs : [$"loop:{agentRunId}"]
        };

    private static AgentCapabilityUseRecord Capability(string agentRunId, AgentCapability capability, AgentCapabilityUseOutcome outcome, bool wasDeclared, bool wasForbidden, string summary) =>
        new()
        {
            CapabilityUseId = $"{agentRunId}-capability-{capability}",
            AgentRunId = agentRunId,
            Capability = capability,
            Outcome = outcome,
            Summary = summary,
            WasDeclaredOnAgent = wasDeclared,
            WasForbiddenOnAgent = wasForbidden
        };

    private static AgentBoundaryDecision Boundary(string agentRunId, AgentBoundaryDecisionType type, string decision, string reason, IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            BoundaryDecisionId = $"{agentRunId}-boundary-{type}-{StableToken(reason)}",
            AgentRunId = agentRunId,
            BoundaryType = type,
            Decision = decision,
            Reason = reason,
            GrantsAuthority = false,
            GrantsHumanApproval = false,
            GrantsPolicyApproval = false,
            GrantsMemoryPromotion = false,
            EvidenceRefs = evidenceRefs.Count > 0 ? evidenceRefs : [$"loop:{agentRunId}"]
        };

    private static AuditThoughtLedgerEntry Thought(
        string agentRunId,
        string suffix,
        ThoughtLedgerEntryType entryType,
        string summary,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset recordedAtUtc,
        IReadOnlyList<string>? risks = null) =>
        new()
        {
            ThoughtLedgerEntryId = $"{agentRunId}-{suffix}",
            AgentRunId = agentRunId,
            EntryType = entryType,
            Summary = summary,
            EvidenceRefs = evidenceRefs.Count > 0 ? evidenceRefs : [$"loop:{agentRunId}"],
            Risks = risks ?? [],
            RequiredFollowUps = ["Human review is required before any source mutation path."],
            ContainsRawPrivateReasoning = false,
            GrantsAuthority = false,
            GrantsApproval = false,
            GrantsMemoryPromotion = false,
            RecordedAtUtc = recordedAtUtc
        };

    private static IReadOnlyList<string> CollectRequestEvidenceRefs(ManualTicketReviewFixProposalLoopRequest request)
    {
        var evidenceRefs = new HashSet<string>(StringComparer.Ordinal)
        {
            $"ticket:{request.Ticket.TicketRef}"
        };

        foreach (var evidenceRef in request.Ticket.EvidenceRefs)
            evidenceRefs.Add(evidenceRef);
        foreach (var item in request.EvidenceBundle.Items)
        {
            evidenceRefs.Add(item.EvidenceId);
            foreach (var evidenceRef in item.EvidenceRefs)
                evidenceRefs.Add(evidenceRef);
        }

        return evidenceRefs.Where(value => !string.IsNullOrWhiteSpace(value)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static string CampaignId(ManualTicketReviewFixProposalLoopRequest request) =>
        string.IsNullOrWhiteSpace(request.CampaignId) ? "campaign-unspecified" : request.CampaignId;

    private static string StableToken(string value) =>
        new string(value.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray()).Trim('-').ToLowerInvariant();

    private static ManualTicketReviewFixProposalLoopIssue ToLoopIssue(AgentDefinitionValidationIssue issue, string code) =>
        ManualTicketReviewFixProposalLoopValidator.Issue(code, issue.Severity, issue.Message);

    private static ManualTicketReviewFixProposalLoopIssue ToLoopIssue(ToolExecutionAuditIssue issue) =>
        ManualTicketReviewFixProposalLoopValidator.Issue(
            ManualTicketReviewFixProposalLoopValidator.TicketLoopToolAuditRejected,
            issue.Severity,
            issue.Message,
            issue.Field ?? string.Empty);

    private static ManualTicketReviewFixProposalLoopResult Rejected(
        string? loopRunId,
        ManualTicketReviewFixProposalLoopStatus status,
        IReadOnlyList<ManualTicketReviewFixProposalLoopIssue> issues,
        ManualTicketReviewCriticStage? criticStage = null,
        ManualTicketReviewProposalStage? proposalStage = null) =>
        new()
        {
            Succeeded = false,
            Status = status,
            LoopRunId = string.IsNullOrWhiteSpace(loopRunId) ? string.Empty : loopRunId,
            CriticStage = criticStage,
            ProposalStage = proposalStage,
            Issues = issues
        };
}



