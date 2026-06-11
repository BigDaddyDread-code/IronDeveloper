using IronDev.Core.Agents.Audit;
using AuditAgentRunStatus = IronDev.Core.Agents.Audit.AgentRunStatus;
using AuditThoughtLedgerEntry = IronDev.Core.Agents.Audit.ThoughtLedgerEntry;

namespace IronDev.Core.Agents.Concrete;

public enum ManualTestFailureRepairProposalLoopStatus
{
    Succeeded = 1,
    NeedsHumanReview = 2,
    Blocked = 3,
    InvalidRequest = 4,
    CriticRejected = 5,
    ProposalRejected = 6,
    Failed = 7
}

public sealed record ManualTestFailureRepairProposalLoopRequest
{
    public required string LoopRunId { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public string? CampaignId { get; init; }
    public required string RequestedByUserId { get; init; }
    public required ManualTestFailureInput Failure { get; init; }
    public ManualTestFailureEvidenceBundle EvidenceBundle { get; init; } = new();
    public bool UseModelBackedCritic { get; init; }
    public bool UseModelBackedProposal { get; init; }
    public bool PersistToolExecutionAudit { get; init; }
    public DateTimeOffset RequestedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ManualTestFailureInput
{
    public required string FailureRef { get; init; }
    public required string TestRunRef { get; init; }
    public required string TestName { get; init; }
    public required string FailureSummary { get; init; }
    public string FailureMessage { get; init; } = string.Empty;
    public string StackTraceSummary { get; init; } = string.Empty;
    public IReadOnlyList<string> FailedAssertions { get; init; } = [];
    public IReadOnlyList<string> RelatedFiles { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsSecret { get; init; }
    public bool IsAuthoritativeForAction { get; init; }
    public bool ClaimsRepairPermission { get; init; }
    public bool ClaimsTestRerunPermission { get; init; }
    public bool ClaimsSourceMutationPermission { get; init; }
    public bool ClaimsMemoryPromotion { get; init; }
}

public sealed record ManualTestFailureEvidenceBundle
{
    public IReadOnlyList<ManualTestFailureEvidenceItem> Items { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsSecret { get; init; }
    public bool IsAuthoritativeForAction { get; init; }
}

public sealed record ManualTestFailureEvidenceItem
{
    public required string EvidenceId { get; init; }
    public required string RefType { get; init; }
    public required string RefId { get; init; }
    public string Source { get; init; } = string.Empty;
    public required string Summary { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool SupportsFailureReview { get; init; } = true;
    public bool SupportsRepairProposal { get; init; } = true;
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsSecret { get; init; }
    public bool IsAuthoritativeForAction { get; init; }
}

public sealed record ManualTestFailureCriticStage
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
    public bool PromotesMemory { get; init; }
}

public sealed record ManualTestFailureRepairProposalStage
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
    public bool RequiresValidation { get; init; } = true;
    public bool RequiresSeparateTestRerun { get; init; } = true;
    public bool MutatesSource { get; init; }
    public bool AppliesPatch { get; init; }
    public bool RunsTests { get; init; }
    public bool CreatesPullRequest { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesAuthority { get; init; }
    public bool CreatesRuntimeAction { get; init; }
    public ToolExecutionAuditAppendStatus? ToolExecutionAuditStatus { get; init; }
    public string? ToolExecutionAuditId { get; init; }
}

public sealed record ManualTestFailureLoopSummary
{
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> SuspectedCauses { get; init; } = [];
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
    public bool RunsTests { get; init; }
    public bool CreatesPullRequest { get; init; }
    public bool PromotesMemory { get; init; }
}

public sealed record ManualTestFailureAuditStage
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
    public bool PromotesMemory { get; init; }
}

public sealed record ManualTestFailureRepairProposalLoopIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public sealed record ManualTestFailureRepairProposalLoopResult
{
    public required bool Succeeded { get; init; }
    public required ManualTestFailureRepairProposalLoopStatus Status { get; init; }
    public required string LoopRunId { get; init; }
    public ManualTestFailureCriticStage? CriticStage { get; init; }
    public ManualTestFailureRepairProposalStage? ProposalStage { get; init; }
    public ManualTestFailureLoopSummary? Summary { get; init; }
    public ManualTestFailureAuditStage? AuditStage { get; init; }
    public AgentRunAuditEnvelope? AuditEnvelope { get; init; }
    public IReadOnlyList<ManualTestFailureRepairProposalLoopIssue> Issues { get; init; } = [];
}

public interface IManualTestFailureRepairProposalLoopService
{
    Task<ManualTestFailureRepairProposalLoopResult> RunAsync(
        ManualTestFailureRepairProposalLoopRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ManualTestFailureRepairProposalLoopValidator
{
    public const string TestFailureLoopRequestRequired = "TEST_FAILURE_LOOP_REQUEST_REQUIRED";
    public const string TestFailureLoopFailureRequired = "TEST_FAILURE_LOOP_FAILURE_REQUIRED";
    public const string TestFailureLoopEvidenceRequired = "TEST_FAILURE_LOOP_EVIDENCE_REQUIRED";
    public const string TestFailureLoopUnsafeInput = "TEST_FAILURE_LOOP_UNSAFE_INPUT";
    public const string TestFailureLoopCriticRejected = "TEST_FAILURE_LOOP_CRITIC_REJECTED";
    public const string TestFailureLoopGateBlocked = "TEST_FAILURE_LOOP_GATE_BLOCKED";
    public const string TestFailureLoopProposalRejected = "TEST_FAILURE_LOOP_PROPOSAL_REJECTED";
    public const string TestFailureLoopAuditInvalid = "TEST_FAILURE_LOOP_AUDIT_INVALID";
    public const string TestFailureLoopToolAuditRejected = "TEST_FAILURE_LOOP_TOOL_AUDIT_REJECTED";
    public const string TestFailureLoopThoughtLedgerInvalid = "TEST_FAILURE_LOOP_THOUGHT_LEDGER_INVALID";
    public const string TestFailureLoopRuntimeWiringForbidden = "TEST_FAILURE_LOOP_RUNTIME_WIRING_FORBIDDEN";

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
        "repair permission",
        "rerun tests",
        "tests rerun",
        "test rerun permission",
        "source mutated",
        "github review submitted",
        "promoted memory",
        "accepted memory",
        "collective memory created"
    ];

    public IReadOnlyList<ManualTestFailureRepairProposalLoopIssue> Validate(
        ManualTestFailureRepairProposalLoopRequest? request,
        bool toolAuditStoreAvailable)
    {
        var issues = new List<ManualTestFailureRepairProposalLoopIssue>();
        if (request is null)
        {
            AddError(issues, TestFailureLoopRequestRequired, "Manual test failure repair loop request is required.");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(request.LoopRunId))
            AddError(issues, TestFailureLoopRequestRequired, "LoopRunId is required.", nameof(request.LoopRunId));
        if (string.IsNullOrWhiteSpace(request.TenantId))
            AddError(issues, TestFailureLoopRequestRequired, "TenantId is required.", nameof(request.TenantId));
        if (string.IsNullOrWhiteSpace(request.ProjectId))
            AddError(issues, TestFailureLoopRequestRequired, "ProjectId is required.", nameof(request.ProjectId));
        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
            AddError(issues, TestFailureLoopRequestRequired, "RequestedByUserId is required.", nameof(request.RequestedByUserId));
        if (request.UseModelBackedCritic)
            AddError(issues, TestFailureLoopUnsafeInput, "Model-backed critic execution is not wired into this manual loop slice.", nameof(request.UseModelBackedCritic));
        if (request.UseModelBackedProposal)
            AddError(issues, TestFailureLoopUnsafeInput, "Model-backed proposal execution is not allowed in this manual loop slice.", nameof(request.UseModelBackedProposal));
        if (request.PersistToolExecutionAudit && !toolAuditStoreAvailable)
            AddError(issues, TestFailureLoopUnsafeInput, "Tool execution audit persistence was requested without an explicit audit store.", nameof(request.PersistToolExecutionAudit));

        ValidateFailure(request.Failure, issues);
        ValidateEvidenceBundle(request.EvidenceBundle, issues);
        return issues;
    }

    private static void ValidateFailure(ManualTestFailureInput? failure, List<ManualTestFailureRepairProposalLoopIssue> issues)
    {
        if (failure is null)
        {
            AddError(issues, TestFailureLoopFailureRequired, "Test failure input is required.", "TestFailure");
            return;
        }

        if (string.IsNullOrWhiteSpace(failure.FailureRef))
            AddError(issues, TestFailureLoopFailureRequired, "FailureRef is required.", "Failure.FailureRef");
        if (string.IsNullOrWhiteSpace(failure.TestRunRef))
            AddError(issues, TestFailureLoopFailureRequired, "TestRunRef is required.", "Failure.TestRunRef");
        if (string.IsNullOrWhiteSpace(failure.TestName))
            AddError(issues, TestFailureLoopFailureRequired, "TestName is required.", "Failure.TestName");
        if (string.IsNullOrWhiteSpace(failure.FailureSummary))
            AddError(issues, TestFailureLoopFailureRequired, "FailureSummary is required.", "Failure.FailureSummary");
        if (failure.ContainsRawPrivateReasoning ||
            failure.ContainsSecret ||
            failure.IsAuthoritativeForAction ||
            failure.ClaimsRepairPermission ||
            failure.ClaimsTestRerunPermission ||
            failure.ClaimsSourceMutationPermission ||
            failure.ClaimsMemoryPromotion)
        {
            AddError(issues, TestFailureLoopUnsafeInput, "Failure input contains unsafe authority, permission, secret, private-reasoning, source-mutation, test-rerun, or memory flags.", "Failure");
        }

        ValidateText(failure.FailureRef, "Failure.FailureRef", issues);
        ValidateText(failure.TestRunRef, "Failure.TestRunRef", issues);
        ValidateText(failure.TestName, "Failure.TestName", issues);
        ValidateText(failure.FailureSummary, "Failure.FailureSummary", issues);
        ValidateText(failure.FailureMessage, "Failure.FailureMessage", issues);
        ValidateText(failure.StackTraceSummary, "Failure.StackTraceSummary", issues);
        foreach (var assertion in failure.FailedAssertions)
            ValidateText(assertion, "Failure.FailedAssertions", issues);
        foreach (var relatedFile in failure.RelatedFiles)
            ValidateText(relatedFile, "Failure.RelatedFiles", issues);
        foreach (var evidenceRef in failure.EvidenceRefs)
            ValidateText(evidenceRef, "Failure.EvidenceRefs", issues);
    }

    private static void ValidateEvidenceBundle(ManualTestFailureEvidenceBundle? bundle, List<ManualTestFailureRepairProposalLoopIssue> issues)
    {
        if (bundle is null || bundle.Items.Count == 0)
        {
            AddError(issues, TestFailureLoopEvidenceRequired, "At least one evidence item is required.", "EvidenceBundle.Items");
            return;
        }

        if (bundle.ContainsRawPrivateReasoning || bundle.ContainsSecret || bundle.IsAuthoritativeForAction)
            AddError(issues, TestFailureLoopUnsafeInput, "Evidence bundle contains unsafe authority, secret, or private-reasoning flags.", "EvidenceBundle");

        foreach (var item in bundle.Items)
        {
            if (string.IsNullOrWhiteSpace(item.EvidenceId) ||
                string.IsNullOrWhiteSpace(item.RefType) ||
                string.IsNullOrWhiteSpace(item.RefId) ||
                string.IsNullOrWhiteSpace(item.Summary))
            {
                AddError(issues, TestFailureLoopEvidenceRequired, "Evidence item requires EvidenceId, RefType, RefId, and Summary.", "EvidenceBundle.Items");
            }

            if (item.ContainsRawPrivateReasoning || item.ContainsSecret || item.IsAuthoritativeForAction)
                AddError(issues, TestFailureLoopUnsafeInput, $"Evidence item '{item.EvidenceId}' contains unsafe authority, secret, or private-reasoning flags.", "EvidenceBundle.Items");

            ValidateText(item.EvidenceId, "EvidenceItem.EvidenceId", issues);
            ValidateText(item.RefType, "EvidenceItem.RefType", issues);
            ValidateText(item.RefId, "EvidenceItem.RefId", issues);
            ValidateText(item.Source, "EvidenceItem.Source", issues);
            ValidateText(item.Summary, "EvidenceItem.Summary", issues);
            foreach (var evidenceRef in item.EvidenceRefs)
                ValidateText(evidenceRef, "EvidenceItem.EvidenceRefs", issues);
        }
    }

    private static void ValidateText(string? value, string field, List<ManualTestFailureRepairProposalLoopIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var marker in RawPrivateReasoningMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                AddError(issues, TestFailureLoopUnsafeInput, $"{field} contains raw/private reasoning marker '{marker}'.", field);
        }

        foreach (var marker in AuthorityMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                AddError(issues, TestFailureLoopUnsafeInput, $"{field} contains authority or execution marker '{marker}'.", field);
        }
    }

    public static ManualTestFailureRepairProposalLoopIssue Issue(string code, string severity, string message, string field = "") =>
        new() { Code = code, Severity = severity, Message = message, Field = field };

    private static void AddError(List<ManualTestFailureRepairProposalLoopIssue> issues, string code, string message, string field = "") =>
        issues.Add(Issue(code, AgentDefinitionValidator.SeverityError, message, field));
}

public sealed class ManualTestFailureRepairProposalLoopService : IManualTestFailureRepairProposalLoopService
{
    private readonly IManualIndependentCriticAgentService _criticService;
    private readonly IManualImplementationAgentPatchProposalService _proposalService;
    private readonly IAgentToolExecutionGate _gate;
    private readonly IToolExecutionAuditStore? _toolExecutionAuditStore;
    private readonly ManualTestFailureRepairProposalLoopValidator _validator;
    private readonly CriticReviewResultValidator _criticValidator;
    private readonly AgentRunAuditEnvelopeValidator _auditValidator;
    private readonly ThoughtLedgerSafetyValidator _thoughtLedgerValidator;
    private readonly ToolExecutionAuditValidator _toolExecutionAuditValidator;

    public ManualTestFailureRepairProposalLoopService()
        : this(
            new ManualIndependentCriticAgentService(),
            new ManualImplementationAgentPatchProposalService(),
            new AgentToolExecutionGate(),
            toolExecutionAuditStore: null)
    {
    }

    public ManualTestFailureRepairProposalLoopService(
        IManualIndependentCriticAgentService criticService,
        IManualImplementationAgentPatchProposalService proposalService,
        IAgentToolExecutionGate gate,
        IToolExecutionAuditStore? toolExecutionAuditStore = null,
        ManualTestFailureRepairProposalLoopValidator? validator = null,
        CriticReviewResultValidator? criticValidator = null,
        AgentRunAuditEnvelopeValidator? auditValidator = null,
        ThoughtLedgerSafetyValidator? thoughtLedgerValidator = null,
        ToolExecutionAuditValidator? toolExecutionAuditValidator = null)
    {
        _criticService = criticService;
        _proposalService = proposalService;
        _gate = gate;
        _toolExecutionAuditStore = toolExecutionAuditStore;
        _validator = validator ?? new ManualTestFailureRepairProposalLoopValidator();
        _criticValidator = criticValidator ?? new CriticReviewResultValidator();
        _auditValidator = auditValidator ?? new AgentRunAuditEnvelopeValidator();
        _thoughtLedgerValidator = thoughtLedgerValidator ?? new ThoughtLedgerSafetyValidator();
        _toolExecutionAuditValidator = toolExecutionAuditValidator ?? new ToolExecutionAuditValidator();
    }

    public async Task<ManualTestFailureRepairProposalLoopResult> RunAsync(
        ManualTestFailureRepairProposalLoopRequest request,
        CancellationToken cancellationToken = default)
    {
        var issues = _validator.Validate(request, _toolExecutionAuditStore is not null);
        if (issues.Count > 0)
            return Rejected(request?.LoopRunId, ManualTestFailureRepairProposalLoopStatus.InvalidRequest, issues);

        var evidenceRefs = CollectRequestEvidenceRefs(request);
        var criticResult = _criticService.Review(BuildCriticRequest(request, evidenceRefs), request.RequestedAtUtc);
        var criticIssues = ValidateCriticResult(criticResult);
        if (criticIssues.Count > 0)
        {
            return Rejected(
                request.LoopRunId,
                ManualTestFailureRepairProposalLoopStatus.CriticRejected,
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
                PolicyRefs = ["policy:manual-test-failure-repair-proposal-loop"]
            },
            EvaluatedAtUtc = request.RequestedAtUtc
        });

        var gateIssue = ValidateGate(gateResult);
        if (gateIssue is not null)
        {
            return Rejected(
                request.LoopRunId,
                gateResult.Decision?.Decision == AgentToolExecutionGateDecisionType.RequiresApproval
                    ? ManualTestFailureRepairProposalLoopStatus.NeedsHumanReview
                    : ManualTestFailureRepairProposalLoopStatus.Blocked,
                [gateIssue],
                criticStage: criticStage);
        }

        var proposalResult = _proposalService.Propose(BuildProposalRequest(request, toolRequest, gateResult.Decision!, criticResult.CriticReviewResult!, evidenceRefs));
        var proposalIssues = ValidateProposalResult(proposalResult);
        if (proposalIssues.Count > 0)
        {
            return Rejected(
                request.LoopRunId,
                ManualTestFailureRepairProposalLoopStatus.ProposalRejected,
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
                : [ManualTestFailureRepairProposalLoopValidator.Issue(ManualTestFailureRepairProposalLoopValidator.TestFailureLoopToolAuditRejected, AgentDefinitionValidator.SeverityError, "Tool execution audit append was rejected.")];

            return Rejected(
                request.LoopRunId,
                ManualTestFailureRepairProposalLoopStatus.NeedsHumanReview,
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
                ManualTestFailureRepairProposalLoopStatus.Failed,
                auditIssues,
                criticStage,
                proposalStage);
        }

        return new ManualTestFailureRepairProposalLoopResult
        {
            Succeeded = true,
            Status = ManualTestFailureRepairProposalLoopStatus.Succeeded,
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
        ManualTestFailureRepairProposalLoopRequest request,
        IReadOnlyList<string> evidenceRefs)
    {
        var inputs = new List<ManualCriticReviewInputRef>
        {
            new()
            {
                InputRefId = $"test-failure-input-{request.LoopRunId}",
                RefType = "TestFailure",
                RefId = request.Failure.FailureRef,
                Source = "manual-test-failure-repair-loop",
                Summary = $"Test failure {request.Failure.FailureRef}: {request.Failure.TestName}",
                EvidenceRefs = request.Failure.EvidenceRefs,
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

        var requiredFix = request.Failure.FailedAssertions.Count > 0
            ? $"Draft a proposal-only implementation plan that addresses: {request.Failure.FailedAssertions[0]}"
            : "Draft a proposal-only implementation package for human review. Do not apply patches.";

        return new ManualCriticReviewRequest
        {
            ReviewRequestId = $"test-failure-review-{request.LoopRunId}",
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            CampaignId = CampaignId(request),
            RunId = request.LoopRunId,
            SubjectType = CriticReviewSubjectType.TestReport,
            SubjectId = request.Failure.FailureRef,
            RequestedByUserId = request.RequestedByUserId,
            CorrelationId = request.LoopRunId,
            RequestSummary = $"Review test failure {request.Failure.FailureRef} and identify the repair proposal boundary.",
            Inputs = inputs,
            FindingDrafts =
            [
                new ManualCriticFindingDraft
                {
                    Severity = CriticSeverity.Medium,
                    Title = $"Test failure needs proposal-only repair: {request.Failure.TestName}",
                    Problem = request.Failure.FailureSummary,
                    WhyItMatters = "The test failure should be reviewed before any implementation proposal is treated as actionable evidence.",
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
        ManualTestFailureRepairProposalLoopRequest request,
        CriticReviewResult criticReview,
        IReadOnlyList<string> evidenceRefs)
    {
        var implementation = AgentDefinitionCatalog.ImplementationAgent;

        return new AgentToolRequest
        {
            ToolRequestId = $"test-failure-loop-patch-proposal-{request.LoopRunId}",
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
            Purpose = $"Create a proposal-only repair proposal package for test failure {request.Failure.FailureRef} using critic review {criticReview.ReviewResultId}.",
            Inputs =
            [
                new AgentToolRequestInput
                {
                    InputId = $"test-failure-{request.Failure.FailureRef}",
                    RefType = "TestFailure",
                    RefId = request.Failure.FailureRef,
                    Source = "manual-test-failure-repair-loop",
                    Summary = request.Failure.TestName,
                    EvidenceRefs = request.Failure.EvidenceRefs,
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
                PolicyRefs = ["policy:manual-test-failure-repair-proposal-loop"]
            },
            RequestedAtUtc = request.RequestedAtUtc
        };
    }

    private static ManualImplementationPatchProposalRequest BuildProposalRequest(
        ManualTestFailureRepairProposalLoopRequest request,
        AgentToolRequest toolRequest,
        AgentToolExecutionGateDecision gateDecision,
        CriticReviewResult criticReview,
        IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            ManualProposalId = $"test-failure-loop-proposal-{request.LoopRunId}",
            ToolRequest = toolRequest,
            GateDecision = gateDecision,
            RequestedByUserId = request.RequestedByUserId,
            ProposalGoal = $"Produce a proposal-only repair proposal package for test failure {request.Failure.FailureRef}; do not apply changes.",
            Inputs =
            [
                new PatchProposalInputRef
                {
                    InputRefId = $"proposal-test-failure-{request.Failure.FailureRef}",
                    RefType = "TestFailure",
                    RefId = request.Failure.FailureRef,
                    Source = "manual-test-failure-repair-loop",
                    Summary = request.Failure.TestName,
                    EvidenceRefs = request.Failure.EvidenceRefs,
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
                ["failureRef"] = request.Failure.FailureRef,
                ["criticReviewResultId"] = criticReview.ReviewResultId,
                ["loopRunId"] = request.LoopRunId
            },
            RequestedAtUtc = request.RequestedAtUtc
        };

    private IReadOnlyList<ManualTestFailureRepairProposalLoopIssue> ValidateCriticResult(ManualCriticReviewResult result)
    {
        var issues = new List<ManualTestFailureRepairProposalLoopIssue>();
        if (!result.Succeeded || result.CriticReviewResult is null || result.AuditEnvelope is null)
        {
            issues.AddRange(result.Issues.Select(issue => ManualTestFailureRepairProposalLoopValidator.Issue(
                ManualTestFailureRepairProposalLoopValidator.TestFailureLoopCriticRejected,
                issue.Severity,
                issue.Message,
                issue.Field ?? string.Empty)));

            if (issues.Count == 0)
            {
                issues.Add(ManualTestFailureRepairProposalLoopValidator.Issue(
                    ManualTestFailureRepairProposalLoopValidator.TestFailureLoopCriticRejected,
                    AgentDefinitionValidator.SeverityError,
                    "Manual critic did not produce a review result and audit envelope."));
            }

            return issues;
        }

        issues.AddRange(_criticValidator.Validate(result.CriticReviewResult).Select(issue => ToLoopIssue(issue, ManualTestFailureRepairProposalLoopValidator.TestFailureLoopCriticRejected)));
        issues.AddRange(ValidateAudit(result.AuditEnvelope, ManualTestFailureRepairProposalLoopValidator.TestFailureLoopCriticRejected));

        if (result.CriticReviewResult.Verdict == CriticReviewVerdict.NoObjection)
        {
            issues.Add(ManualTestFailureRepairProposalLoopValidator.Issue(
                ManualTestFailureRepairProposalLoopValidator.TestFailureLoopCriticRejected,
                AgentDefinitionValidator.SeverityError,
                "Manual test failure repair loop requires an actionable critic finding before a repair proposal can be requested."));
        }

        if (result.AuditEnvelope.BoundaryDecisions.Any(decision => decision.GrantsAuthority || decision.GrantsHumanApproval || decision.GrantsPolicyApproval || decision.GrantsMemoryPromotion))
        {
            issues.Add(ManualTestFailureRepairProposalLoopValidator.Issue(
                ManualTestFailureRepairProposalLoopValidator.TestFailureLoopCriticRejected,
                AgentDefinitionValidator.SeverityError,
                "Critic audit envelope attempted to grant authority, approval, policy approval, or memory promotion."));
        }

        return issues;
    }

    private static ManualTestFailureRepairProposalLoopIssue? ValidateGate(AgentToolExecutionGateResult gateResult)
    {
        if (!gateResult.Succeeded || gateResult.Decision is null)
        {
            return ManualTestFailureRepairProposalLoopValidator.Issue(
                ManualTestFailureRepairProposalLoopValidator.TestFailureLoopGateBlocked,
                AgentDefinitionValidator.SeverityError,
                "Patch proposal tool request gate did not produce a decision.");
        }

        var decision = gateResult.Decision;
        if (decision.Decision != AgentToolExecutionGateDecisionType.Allowed || !decision.GrantsExecution)
        {
            return ManualTestFailureRepairProposalLoopValidator.Issue(
                ManualTestFailureRepairProposalLoopValidator.TestFailureLoopGateBlocked,
                decision.Decision == AgentToolExecutionGateDecisionType.RequiresApproval ? AgentDefinitionValidator.SeverityWarning : AgentDefinitionValidator.SeverityError,
                $"Patch proposal gate returned {decision.Decision}.");
        }

        if (decision.ExecutesTool || decision.MutatesSource || decision.CallsExternalSystem || decision.SubmitsGitHubReview || decision.PromotesMemory || decision.CreatesCollectiveMemory || decision.WritesWeaviate)
        {
            return ManualTestFailureRepairProposalLoopValidator.Issue(
                ManualTestFailureRepairProposalLoopValidator.TestFailureLoopGateBlocked,
                AgentDefinitionValidator.SeverityError,
                "Patch proposal gate decision carried unsafe execution, source, external, or memory authority flags.");
        }

        return null;
    }

    private IReadOnlyList<ManualTestFailureRepairProposalLoopIssue> ValidateProposalResult(ManualImplementationPatchProposalResult result)
    {
        var issues = new List<ManualTestFailureRepairProposalLoopIssue>();
        if (!result.Succeeded || result.Output is null || result.AuditEnvelope is null)
        {
            issues.AddRange(result.Issues.Select(issue => ManualTestFailureRepairProposalLoopValidator.Issue(
                ManualTestFailureRepairProposalLoopValidator.TestFailureLoopProposalRejected,
                issue.Severity,
                issue.Message,
                issue.Field ?? string.Empty)));

            if (issues.Count == 0)
            {
                issues.Add(ManualTestFailureRepairProposalLoopValidator.Issue(
                    ManualTestFailureRepairProposalLoopValidator.TestFailureLoopProposalRejected,
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
            issues.Add(ManualTestFailureRepairProposalLoopValidator.Issue(
                ManualTestFailureRepairProposalLoopValidator.TestFailureLoopProposalRejected,
                AgentDefinitionValidator.SeverityError,
                "Implementation proposal output must remain proposal-only, human-reviewed, validation-required, and non-mutating."));
        }

        foreach (var change in output.Proposal.FileChanges)
        {
            if (!change.IsProposalOnly || change.WritesFile || change.DeletesFile || change.AppliesPatch)
            {
                issues.Add(ManualTestFailureRepairProposalLoopValidator.Issue(
                    ManualTestFailureRepairProposalLoopValidator.TestFailureLoopProposalRejected,
                    AgentDefinitionValidator.SeverityError,
                    $"Proposed file change '{change.FileChangeId}' claimed file mutation or application authority."));
            }

            foreach (var hunk in change.Hunks)
            {
                if (hunk.ContainsRawPrivateReasoning || hunk.ContainsSecret || hunk.ClaimsApplied)
                {
                    issues.Add(ManualTestFailureRepairProposalLoopValidator.Issue(
                        ManualTestFailureRepairProposalLoopValidator.TestFailureLoopProposalRejected,
                        AgentDefinitionValidator.SeverityError,
                        $"Proposed hunk '{hunk.HunkId}' contains unsafe flags or claims it was applied."));
                }
            }
        }

        issues.AddRange(ValidateAudit(result.AuditEnvelope, ManualTestFailureRepairProposalLoopValidator.TestFailureLoopProposalRejected));
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
                        Code = ManualTestFailureRepairProposalLoopValidator.TestFailureLoopToolAuditRejected,
                        Severity = AgentDefinitionValidator.SeverityError,
                        Message = ex.Message
                    }
                ]
            };
        }
    }

    private IReadOnlyList<ManualTestFailureRepairProposalLoopIssue> ValidateAudit(
        AgentRunAuditEnvelope auditEnvelope,
        string code) =>
        _auditValidator.Validate(auditEnvelope)
            .Select(issue => ToLoopIssue(issue, code))
            .Concat(_thoughtLedgerValidator.Validate(auditEnvelope.ThoughtLedger)
                .Select(issue => ToLoopIssue(issue, ManualTestFailureRepairProposalLoopValidator.TestFailureLoopThoughtLedgerInvalid)))
            .ToArray();

    private IReadOnlyList<ManualTestFailureRepairProposalLoopIssue> ValidateAuditEnvelope(AgentRunAuditEnvelope auditEnvelope) =>
        ValidateAudit(auditEnvelope, ManualTestFailureRepairProposalLoopValidator.TestFailureLoopAuditInvalid);

    private static ManualTestFailureCriticStage BuildCriticStage(
        ManualCriticReviewResult result,
        IReadOnlyList<string> evidenceRefs,
        bool succeeded)
    {
        var review = result.CriticReviewResult;
        return new ManualTestFailureCriticStage
        {
            Succeeded = succeeded,
            CriticRunId = result.ManualCriticRunId,
            CriticProfileId = AgentSpecialisationCatalog.TestFailureCritic.SpecialisationId,
            CriticReviewResultId = review?.ReviewResultId ?? string.Empty,
            Verdict = review?.Verdict ?? CriticReviewVerdict.CommentOnly,
            Findings = review?.Findings ?? [],
            EvidenceRefs = evidenceRefs,
            IsReviewOnly = true,
            BlocksExecution = false,
            GrantsApproval = false,
            GrantsPolicyApproval = false,
            MutatesSource = false,
            ExecutesTool = false,
            PromotesMemory = false
        };
    }

    private static ManualTestFailureRepairProposalStage BuildProposalStage(
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
            RequiresValidation = true,
            RequiresSeparateTestRerun = true,
            MutatesSource = false,
            AppliesPatch = false,
            RunsTests = false,
            CreatesPullRequest = false,
            PromotesMemory = false,
            CreatesAuthority = false,
            CreatesRuntimeAction = false,
            ToolExecutionAuditStatus = auditAppendResult?.Status,
            ToolExecutionAuditId = auditAppendResult?.ToolExecutionAuditId
        };

    private static ManualTestFailureLoopSummary BuildSummary(
        ManualTestFailureRepairProposalLoopRequest request,
        ManualTestFailureCriticStage criticStage,
        ManualTestFailureRepairProposalStage proposalStage)
    {
        var evidenceRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var evidenceRef in criticStage.EvidenceRefs.Concat(proposalStage.EvidenceRefs))
            evidenceRefs.Add(evidenceRef);

        return new ManualTestFailureLoopSummary
        {
            Title = "Manual test failure repair proposal ready for human review",
            Summary = $"Test failure {request.Failure.FailureRef} from run {request.Failure.TestRunRef} was reviewed by IndependentCriticAgent and converted into a proposal-only ImplementationAgent repair proposal package. Human review, separate validation, and a separate test rerun remain required.",
            SuspectedCauses = request.Failure.FailedAssertions.Count > 0
                ? request.Failure.FailedAssertions
                : [request.Failure.FailureSummary],
            RecommendedNextActions =
            [
                "Human reviewer should inspect the critic findings and proposal package.",
                "Run validation separately before any source-changing apply path.",
                "Rerun tests separately after any future governed repair is applied.",
                "Do not treat this loop result as approval, execution permission, or applied source change."
            ],
            RequiredHumanDecisions =
            [
                "Decide whether the proposal should enter a governed apply path.",
                "Decide whether additional test failure clarification is required."
            ],
            RequiredValidation =
            [
                "Validate any future patch in a disposable workspace before apply.",
                "Rerun the failed test separately after any future governed apply path.",
                "Capture promotion approval evidence separately if the proposal advances."
            ],
            EvidenceRefs = evidenceRefs.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            IsAdvisoryOnly = true,
            GrantsApproval = false,
            GrantsPolicyApproval = false,
            GrantsExecutionPermission = false,
            MutatesSource = false,
            AppliesPatch = false,
            RunsTests = false,
            CreatesPullRequest = false,
            PromotesMemory = false
        };
    }

    private static AgentRunAuditEnvelope BuildLoopAuditEnvelope(
        ManualTestFailureRepairProposalLoopRequest request,
        ManualCriticReviewResult criticResult,
        ManualImplementationPatchProposalResult proposalResult,
        ManualTestFailureLoopSummary summary,
        IReadOnlyList<string> evidenceRefs)
    {
        var agent = AgentDefinitionCatalog.ReportingAgent;
        var agentRunId = $"manual-test-failure-repair-loop-{request.LoopRunId}";
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
                RequestSummary = $"Manual test failure repair proposal loop for {request.Failure.FailureRef}.",
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
                    InputRefId = $"loop-test-failure-{request.Failure.FailureRef}",
                    AgentRunId = agentRunId,
                    RefType = "TestFailure",
                    RefId = request.Failure.FailureRef,
                    Source = "manual-user-request",
                    Summary = request.Failure.TestName,
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
                    RefType = "ManualTestFailureRepairProposalLoopSummary",
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
                Step(agentRunId, 1, AgentRunStepType.Created, now, "Manual test failure repair proposal loop started.", evidenceRefs),
                Step(agentRunId, 2, AgentRunStepType.InputBound, now, "TestFailure, critic review, and proposal evidence were bound as non-authoritative inputs.", evidenceRefs),
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
                ,
                Capability(agentRunId, AgentCapability.BlockExecution, AgentCapabilityUseOutcome.Blocked, false, false, "Loop does not block execution as authority.")
            ],
            BoundaryDecisions =
            [
                Boundary(agentRunId, AgentBoundaryDecisionType.Evidence, "allowed", "Test failure input was validated as evidence only.", evidenceRefs),
                Boundary(agentRunId, AgentBoundaryDecisionType.Safety, "allowed", "Critic output is review-only and does not grant repair authority.", evidenceRefs),
                Boundary(agentRunId, AgentBoundaryDecisionType.GovernanceDecision, "allowed", "PatchProposal gate allowed a future proposal generator only.", evidenceRefs),
                Boundary(agentRunId, AgentBoundaryDecisionType.Policy, "warn", "Loop output is advisory and does not grant approval or execution permission.", evidenceRefs),
                Boundary(agentRunId, AgentBoundaryDecisionType.Safety, "blocked", "Test rerun, source mutation, patch application, file writes, git commands, GitHub submission, pull request creation, external calls, and memory authority changes are outside this loop.", evidenceRefs),
                Boundary(agentRunId, AgentBoundaryDecisionType.Output, "allowed", "Only review/proposal summary output was recorded.", outputEvidence)
            ],
            ThoughtLedger =
            [
                Thought(agentRunId, "loop-thought-evidence", ThoughtLedgerEntryType.EvidenceUsed, "Manual test failure repair loop request was validated as evidence only.", evidenceRefs, now),
                Thought(agentRunId, "loop-thought-critic", ThoughtLedgerEntryType.OutputRationale, "IndependentCriticAgent produced review-only test failure findings.", evidenceRefs, now),
                Thought(agentRunId, "loop-thought-proposal", ThoughtLedgerEntryType.OutputRationale, "ImplementationAgent produced a proposal-only repair package.", outputEvidence, now),
                Thought(agentRunId, "loop-thought-boundary", ThoughtLedgerEntryType.BoundaryDecision, "No patch was applied, no source was changed, no tests were run, no GitHub review or PR was submitted, and memory authority was unchanged.", outputEvidence, now, risks: ["Human review, separate validation, and a separate test rerun remain required."])
            ]
        };
    }

    private static ManualTestFailureAuditStage BuildAuditStage(
        ManualTestFailureRepairProposalLoopRequest request,
        ManualCriticReviewResult criticResult,
        ManualImplementationPatchProposalResult proposalResult,
        ToolExecutionAuditAppendResult? auditAppendResult)
    {
        var toolAuditRefs = auditAppendResult is { Status: ToolExecutionAuditAppendStatus.Appended or ToolExecutionAuditAppendStatus.AlreadyExists } &&
                            !string.IsNullOrWhiteSpace(auditAppendResult.ToolExecutionAuditId)
            ? new[] { $"tool-execution-audit:{auditAppendResult.ToolExecutionAuditId}" }
            : [];

        return new ManualTestFailureAuditStage
        {
            AgentRunIds =
            [
                criticResult.AuditEnvelope?.Run.AgentRunId ?? criticResult.ManualCriticRunId,
                proposalResult.AuditEnvelope?.Run.AgentRunId ?? proposalResult.ManualProposalId,
                $"manual-test-failure-repair-loop-{request.LoopRunId}"
            ],
            AuditEnvelopeRefs =
            [
                $"agent-run-audit:{criticResult.AuditEnvelope?.Run.AgentRunId ?? criticResult.ManualCriticRunId}",
                $"agent-run-audit:{proposalResult.AuditEnvelope?.Run.AgentRunId ?? proposalResult.ManualProposalId}",
                $"agent-run-audit:manual-test-failure-repair-loop-{request.LoopRunId}"
            ],
            ToolExecutionAuditRefs = toolAuditRefs,
            PersistedToolExecutionAudit = toolAuditRefs.Length > 0,
            ContainsRawPrivateReasoning = false,
            GrantsAuthority = false,
            GrantsApproval = false,
            MutatesSource = false,
            ExecutesTool = false,
            PromotesMemory = false
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

    private static IReadOnlyList<string> CollectRequestEvidenceRefs(ManualTestFailureRepairProposalLoopRequest request)
    {
        var evidenceRefs = new HashSet<string>(StringComparer.Ordinal)
        {
            $"test failure:{request.Failure.FailureRef}"
        };

        foreach (var evidenceRef in request.Failure.EvidenceRefs)
            evidenceRefs.Add(evidenceRef);
        foreach (var item in request.EvidenceBundle.Items)
        {
            evidenceRefs.Add(item.EvidenceId);
            foreach (var evidenceRef in item.EvidenceRefs)
                evidenceRefs.Add(evidenceRef);
        }

        return evidenceRefs.Where(value => !string.IsNullOrWhiteSpace(value)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static string CampaignId(ManualTestFailureRepairProposalLoopRequest request) =>
        string.IsNullOrWhiteSpace(request.CampaignId) ? "campaign-unspecified" : request.CampaignId;

    private static string StableToken(string value) =>
        new string(value.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray()).Trim('-').ToLowerInvariant();

    private static ManualTestFailureRepairProposalLoopIssue ToLoopIssue(AgentDefinitionValidationIssue issue, string code) =>
        ManualTestFailureRepairProposalLoopValidator.Issue(code, issue.Severity, issue.Message);

    private static ManualTestFailureRepairProposalLoopIssue ToLoopIssue(ToolExecutionAuditIssue issue) =>
        ManualTestFailureRepairProposalLoopValidator.Issue(
            ManualTestFailureRepairProposalLoopValidator.TestFailureLoopToolAuditRejected,
            issue.Severity,
            issue.Message,
            issue.Field ?? string.Empty);

    private static ManualTestFailureRepairProposalLoopResult Rejected(
        string? loopRunId,
        ManualTestFailureRepairProposalLoopStatus status,
        IReadOnlyList<ManualTestFailureRepairProposalLoopIssue> issues,
        ManualTestFailureCriticStage? criticStage = null,
        ManualTestFailureRepairProposalStage? proposalStage = null) =>
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
