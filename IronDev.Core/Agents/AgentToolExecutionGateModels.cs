namespace IronDev.Core.Agents;

public enum AgentToolExecutionGateDecisionType
{
    Blocked = 1,
    RequiresApproval = 2,
    Allowed = 3
}

public sealed record AgentToolExecutionGatePolicyContext
{
    public bool PolicyKnown { get; init; }
    public bool AllowsToolRequest { get; init; }
    public bool AllowsToolExecution { get; init; }
    public bool AllowsSourceMutation { get; init; }
    public bool AllowsExternalEffects { get; init; }
    public bool AllowsGitHubSubmission { get; init; }
    public bool AllowsBuildExecution { get; init; }
    public bool AllowsTestExecution { get; init; }
    public bool AllowsPatchProposal { get; init; }
    public IReadOnlyList<string> PolicyRefs { get; init; } = [];
}

public sealed record AgentToolExecutionGateApprovalContext
{
    public bool HasHumanApproval { get; init; }
    public string? HumanApprovalId { get; init; }
    public bool HasPolicyApproval { get; init; }
    public string? PolicyApprovalId { get; init; }
    public bool HasGovernanceGateApproval { get; init; }
    public string? GovernanceGateDecisionId { get; init; }
    public bool HasDryRunEvidence { get; init; }
    public string? DryRunEvidenceId { get; init; }
    public IReadOnlyList<string> ApprovalRefs { get; init; } = [];
}

public sealed record AgentToolExecutionGateMemoryContext
{
    public bool RequestReferencesMemory { get; init; }
    public bool HasMemoryGovernanceDecision { get; init; }
    public string? MemoryGovernanceDecisionId { get; init; }
    public bool MemoryGovernanceAllowsUse { get; init; }
    public bool MemoryGovernanceWarnsOnly { get; init; }
    public bool MemoryGovernanceBlocksUse { get; init; }
    public IReadOnlyList<string> MemoryRefs { get; init; } = [];
}

public sealed record AgentToolExecutionGateRequest
{
    public required AgentToolRequest ToolRequest { get; init; }
    public AgentToolExecutionGatePolicyContext PolicyContext { get; init; } = new();
    public AgentToolExecutionGateApprovalContext ApprovalContext { get; init; } = new();
    public AgentToolExecutionGateMemoryContext MemoryContext { get; init; } = new();
    public DateTimeOffset EvaluatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AgentToolExecutionGateReason
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
}

public sealed record AgentToolExecutionGateIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public sealed record AgentToolExecutionGateDecision
{
    public required string GateDecisionId { get; init; }
    public required string ToolRequestId { get; init; }
    public required AgentToolExecutionGateDecisionType Decision { get; init; }
    public required AgentToolKind ToolKind { get; init; }
    public required AgentToolRequestType RequestType { get; init; }
    public required AgentToolRiskLevel RiskLevel { get; init; }
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public IReadOnlyList<AgentToolExecutionGateReason> Reasons { get; init; } = [];
    public IReadOnlyList<AgentToolExecutionGateIssue> Issues { get; init; } = [];
    public bool GrantsExecution { get; init; }
    public bool ExecutesTool { get; init; }
    public bool MutatesSource { get; init; }
    public bool CallsExternalSystem { get; init; }
    public bool SubmitsGitHubReview { get; init; }
    public bool PersistsResult { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesCollectiveMemory { get; init; }
    public bool WritesWeaviate { get; init; }
    public bool RequiresExecutor { get; init; }
}

public sealed record AgentToolExecutionGateResult
{
    public required bool Succeeded { get; init; }
    public AgentToolExecutionGateDecision? Decision { get; init; }
    public IReadOnlyList<AgentToolExecutionGateIssue> Issues { get; init; } = [];
}

public interface IAgentToolExecutionGate
{
    AgentToolExecutionGateResult Evaluate(AgentToolExecutionGateRequest request);
}

public sealed class AgentToolExecutionGate : IAgentToolExecutionGate
{
    public const string ToolGateRequestInvalid = "TOOL_GATE_REQUEST_INVALID";
    public const string ToolGatePolicyUnknown = "TOOL_GATE_POLICY_UNKNOWN";
    public const string ToolGateToolRequestBlockedByPolicy = "TOOL_GATE_TOOL_REQUEST_BLOCKED_BY_POLICY";
    public const string ToolGateToolExecutionBlockedByPolicy = "TOOL_GATE_TOOL_EXECUTION_BLOCKED_BY_POLICY";
    public const string ToolGateSourceMutationBlockedByPolicy = "TOOL_GATE_SOURCE_MUTATION_BLOCKED_BY_POLICY";
    public const string ToolGateExternalEffectBlockedByPolicy = "TOOL_GATE_EXTERNAL_EFFECT_BLOCKED_BY_POLICY";
    public const string ToolGateGitHubSubmissionBlockedByPolicy = "TOOL_GATE_GITHUB_SUBMISSION_BLOCKED_BY_POLICY";
    public const string ToolGateBuildBlockedByPolicy = "TOOL_GATE_BUILD_BLOCKED_BY_POLICY";
    public const string ToolGateTestBlockedByPolicy = "TOOL_GATE_TEST_BLOCKED_BY_POLICY";
    public const string ToolGatePatchProposalBlockedByPolicy = "TOOL_GATE_PATCH_PROPOSAL_BLOCKED_BY_POLICY";
    public const string ToolGateHumanApprovalRequired = "TOOL_GATE_HUMAN_APPROVAL_REQUIRED";
    public const string ToolGatePolicyApprovalRequired = "TOOL_GATE_POLICY_APPROVAL_REQUIRED";
    public const string ToolGateGovernanceApprovalRequired = "TOOL_GATE_GOVERNANCE_APPROVAL_REQUIRED";
    public const string ToolGateDryRunRequired = "TOOL_GATE_DRY_RUN_REQUIRED";
    public const string ToolGateMemoryGovernanceRequired = "TOOL_GATE_MEMORY_GOVERNANCE_REQUIRED";
    public const string ToolGateMemoryGovernanceBlocked = "TOOL_GATE_MEMORY_GOVERNANCE_BLOCKED";
    public const string ToolGateMemoryWarningNotAuthority = "TOOL_GATE_MEMORY_WARNING_NOT_AUTHORITY";
    public const string ToolGateAllowedForFutureExecutor = "TOOL_GATE_ALLOWED_FOR_FUTURE_EXECUTOR";
    public const string ToolGateBlockedNonExecutableRequest = "TOOL_GATE_BLOCKED_NON_EXECUTABLE_REQUEST";

    private readonly AgentToolRequestValidator _requestValidator;

    public AgentToolExecutionGate(AgentToolRequestValidator? requestValidator = null)
    {
        _requestValidator = requestValidator ?? new AgentToolRequestValidator();
    }

    public AgentToolExecutionGateResult Evaluate(AgentToolExecutionGateRequest request)
    {
        if (request?.ToolRequest is null)
        {
            var issue = new AgentToolExecutionGateIssue
            {
                Code = ToolGateRequestInvalid,
                Severity = "error",
                Message = "ToolRequest is required.",
                Field = nameof(AgentToolExecutionGateRequest.ToolRequest)
            };

            return new AgentToolExecutionGateResult
            {
                Succeeded = false,
                Issues = [issue]
            };
        }

        var toolRequest = request.ToolRequest;
        var validation = _requestValidator.Validate(toolRequest);
        if (!validation.IsValid)
            return BuildInvalidRequestResult(toolRequest, request.EvaluatedAtUtc, validation.Issues);

        var issues = new List<AgentToolExecutionGateIssue>();
        var reasons = new List<AgentToolExecutionGateReason>();
        var blocked = false;
        var requiresApproval = false;

        void AddBlocked(string code, string message, string field = "")
        {
            blocked = true;
            issues.Add(new AgentToolExecutionGateIssue
            {
                Code = code,
                Severity = "error",
                Message = message,
                Field = field
            });
            reasons.Add(new AgentToolExecutionGateReason
            {
                Code = code,
                Severity = "error",
                Message = message
            });
        }

        void AddRequiresApproval(string code, string message, string field = "")
        {
            requiresApproval = true;
            issues.Add(new AgentToolExecutionGateIssue
            {
                Code = code,
                Severity = "warning",
                Message = message,
                Field = field
            });
            reasons.Add(new AgentToolExecutionGateReason
            {
                Code = code,
                Severity = "warning",
                Message = message
            });
        }

        void AddWarning(string code, string message, string field = "")
        {
            issues.Add(new AgentToolExecutionGateIssue
            {
                Code = code,
                Severity = "warning",
                Message = message,
                Field = field
            });
            reasons.Add(new AgentToolExecutionGateReason
            {
                Code = code,
                Severity = "warning",
                Message = message
            });
        }

        EvaluatePolicy(toolRequest, request.PolicyContext, AddBlocked, AddRequiresApproval);
        EvaluateApproval(toolRequest, request.ApprovalContext, AddRequiresApproval);
        EvaluateMemory(toolRequest, request.MemoryContext, AddBlocked, AddRequiresApproval, AddWarning);

        var decisionType = blocked
            ? AgentToolExecutionGateDecisionType.Blocked
            : requiresApproval
                ? AgentToolExecutionGateDecisionType.RequiresApproval
                : AgentToolExecutionGateDecisionType.Allowed;

        if (decisionType == AgentToolExecutionGateDecisionType.Allowed)
        {
            reasons.Add(new AgentToolExecutionGateReason
            {
                Code = ToolGateAllowedForFutureExecutor,
                Severity = "info",
                Message = "Tool request is eligible for a future executor; the gate does not execute the tool."
            });
        }

        var decision = BuildDecision(
            toolRequest,
            request.EvaluatedAtUtc,
            decisionType,
            reasons,
            issues);

        return new AgentToolExecutionGateResult
        {
            Succeeded = true,
            Decision = decision,
            Issues = issues
        };
    }

    private static AgentToolExecutionGateResult BuildInvalidRequestResult(
        AgentToolRequest toolRequest,
        DateTimeOffset evaluatedAtUtc,
        IReadOnlyList<AgentToolRequestValidationIssue> validationIssues)
    {
        var issues = validationIssues
            .Select(issue => new AgentToolExecutionGateIssue
            {
                Code = IsNonExecutableBoundaryIssue(issue.Code) ? ToolGateBlockedNonExecutableRequest : ToolGateRequestInvalid,
                Severity = "error",
                Message = $"{issue.Code}: {issue.Message}",
                Field = issue.Field
            })
            .ToArray();

        var reasons = issues
            .Select(issue => new AgentToolExecutionGateReason
            {
                Code = issue.Code,
                Severity = issue.Severity,
                Message = issue.Message
            })
            .ToArray();

        return new AgentToolExecutionGateResult
        {
            Succeeded = true,
            Decision = BuildDecision(
                toolRequest,
                evaluatedAtUtc,
                AgentToolExecutionGateDecisionType.Blocked,
                reasons,
                issues),
            Issues = issues
        };
    }

    private static void EvaluatePolicy(
        AgentToolRequest toolRequest,
        AgentToolExecutionGatePolicyContext policy,
        Action<string, string, string> addBlocked,
        Action<string, string, string> addRequiresApproval)
    {
        if (!policy.PolicyKnown)
        {
            if (toolRequest.RiskLevel is AgentToolRiskLevel.Low or AgentToolRiskLevel.Medium)
            {
                addRequiresApproval(
                    ToolGatePolicyUnknown,
                    "Policy context is unknown; low and medium risk requests require approval before any future executor.",
                    nameof(AgentToolExecutionGatePolicyContext.PolicyKnown));
                return;
            }

            addBlocked(
                ToolGatePolicyUnknown,
                "Policy context is unknown; high and critical risk requests are blocked.",
                nameof(AgentToolExecutionGatePolicyContext.PolicyKnown));
            return;
        }

        if (!policy.AllowsToolRequest)
            addBlocked(ToolGateToolRequestBlockedByPolicy, "Policy blocks this tool request.", nameof(AgentToolExecutionGatePolicyContext.AllowsToolRequest));

        if (RequiresToolExecutionPolicy(toolRequest) && !policy.AllowsToolExecution)
            addBlocked(ToolGateToolExecutionBlockedByPolicy, "Policy blocks tool execution for this request.", nameof(AgentToolExecutionGatePolicyContext.AllowsToolExecution));

        if (toolRequest.RequestType == AgentToolRequestType.BuildExecutionRequest && !policy.AllowsBuildExecution)
            addBlocked(ToolGateBuildBlockedByPolicy, "Policy blocks build execution.", nameof(AgentToolExecutionGatePolicyContext.AllowsBuildExecution));

        if (toolRequest.RequestType == AgentToolRequestType.TestExecutionRequest && !policy.AllowsTestExecution)
            addBlocked(ToolGateTestBlockedByPolicy, "Policy blocks test execution.", nameof(AgentToolExecutionGatePolicyContext.AllowsTestExecution));

        if (toolRequest.RequestType == AgentToolRequestType.PatchProposalRequest && !policy.AllowsPatchProposal)
            addBlocked(ToolGatePatchProposalBlockedByPolicy, "Policy blocks patch proposal requests.", nameof(AgentToolExecutionGatePolicyContext.AllowsPatchProposal));

        if (toolRequest.RequestType == AgentToolRequestType.SourceMutationRequest && !policy.AllowsSourceMutation)
            addBlocked(ToolGateSourceMutationBlockedByPolicy, "Policy blocks source mutation.", nameof(AgentToolExecutionGatePolicyContext.AllowsSourceMutation));

        if (toolRequest.RequestType == AgentToolRequestType.ExternalEffectRequest && !policy.AllowsExternalEffects)
            addBlocked(ToolGateExternalEffectBlockedByPolicy, "Policy blocks external effects.", nameof(AgentToolExecutionGatePolicyContext.AllowsExternalEffects));

        if (toolRequest.ToolKind == AgentToolKind.GitHubReviewSubmission && !policy.AllowsGitHubSubmission)
            addBlocked(ToolGateGitHubSubmissionBlockedByPolicy, "Policy blocks GitHub review submission.", nameof(AgentToolExecutionGatePolicyContext.AllowsGitHubSubmission));
    }

    private static void EvaluateApproval(
        AgentToolRequest toolRequest,
        AgentToolExecutionGateApprovalContext approval,
        Action<string, string, string> addRequiresApproval)
    {
        if (toolRequest.RequestType is AgentToolRequestType.BuildExecutionRequest or AgentToolRequestType.TestExecutionRequest &&
            !approval.HasGovernanceGateApproval)
        {
            addRequiresApproval(
                ToolGateGovernanceApprovalRequired,
                "Build and test requests require governance gate approval before any future executor.",
                nameof(AgentToolExecutionGateApprovalContext.HasGovernanceGateApproval));
        }

        if (toolRequest.RequestType == AgentToolRequestType.SourceMutationRequest)
        {
            RequireHumanApproval(approval, addRequiresApproval);
            RequirePolicyApproval(approval, addRequiresApproval);
            RequireGovernanceApproval(approval, addRequiresApproval);

            if (!approval.HasDryRunEvidence)
            {
                addRequiresApproval(
                    ToolGateDryRunRequired,
                    "Source mutation requests require dry-run evidence before any future executor.",
                    nameof(AgentToolExecutionGateApprovalContext.HasDryRunEvidence));
            }
        }

        if (toolRequest.RequestType == AgentToolRequestType.ExternalEffectRequest)
        {
            RequireHumanApproval(approval, addRequiresApproval);
            RequirePolicyApproval(approval, addRequiresApproval);
            RequireGovernanceApproval(approval, addRequiresApproval);
        }
    }

    private static void EvaluateMemory(
        AgentToolRequest toolRequest,
        AgentToolExecutionGateMemoryContext memory,
        Action<string, string, string> addBlocked,
        Action<string, string, string> addRequiresApproval,
        Action<string, string, string> addWarning)
    {
        if (!ReferencesMemory(toolRequest) && !memory.RequestReferencesMemory)
            return;

        if (memory.MemoryGovernanceBlocksUse)
        {
            addBlocked(
                ToolGateMemoryGovernanceBlocked,
                "Memory governance blocks this memory-backed tool request.",
                nameof(AgentToolExecutionGateMemoryContext.MemoryGovernanceBlocksUse));
            return;
        }

        if (!memory.HasMemoryGovernanceDecision)
        {
            addRequiresApproval(
                ToolGateMemoryGovernanceRequired,
                "Memory-backed tool requests require a memory governance decision.",
                nameof(AgentToolExecutionGateMemoryContext.HasMemoryGovernanceDecision));
            return;
        }

        if (!memory.MemoryGovernanceAllowsUse)
        {
            var code = memory.MemoryGovernanceWarnsOnly
                ? ToolGateMemoryWarningNotAuthority
                : ToolGateMemoryGovernanceRequired;
            addRequiresApproval(
                code,
                "Memory governance warning alone is not authority to use memory-backed evidence.",
                nameof(AgentToolExecutionGateMemoryContext.MemoryGovernanceAllowsUse));
            return;
        }

        if (memory.MemoryGovernanceWarnsOnly)
        {
            addWarning(
                ToolGateMemoryWarningNotAuthority,
                "Memory governance warning is advisory only and does not replace approval requirements.",
                nameof(AgentToolExecutionGateMemoryContext.MemoryGovernanceWarnsOnly));
        }
    }

    private static void RequireHumanApproval(
        AgentToolExecutionGateApprovalContext approval,
        Action<string, string, string> addRequiresApproval)
    {
        if (!approval.HasHumanApproval)
        {
            addRequiresApproval(
                ToolGateHumanApprovalRequired,
                "Human approval is required before any future executor.",
                nameof(AgentToolExecutionGateApprovalContext.HasHumanApproval));
        }
    }

    private static void RequirePolicyApproval(
        AgentToolExecutionGateApprovalContext approval,
        Action<string, string, string> addRequiresApproval)
    {
        if (!approval.HasPolicyApproval)
        {
            addRequiresApproval(
                ToolGatePolicyApprovalRequired,
                "Policy approval is required before any future executor.",
                nameof(AgentToolExecutionGateApprovalContext.HasPolicyApproval));
        }
    }

    private static void RequireGovernanceApproval(
        AgentToolExecutionGateApprovalContext approval,
        Action<string, string, string> addRequiresApproval)
    {
        if (!approval.HasGovernanceGateApproval)
        {
            addRequiresApproval(
                ToolGateGovernanceApprovalRequired,
                "Governance gate approval is required before any future executor.",
                nameof(AgentToolExecutionGateApprovalContext.HasGovernanceGateApproval));
        }
    }

    private static AgentToolExecutionGateDecision BuildDecision(
        AgentToolRequest toolRequest,
        DateTimeOffset evaluatedAtUtc,
        AgentToolExecutionGateDecisionType decisionType,
        IReadOnlyList<AgentToolExecutionGateReason> reasons,
        IReadOnlyList<AgentToolExecutionGateIssue> issues)
    {
        var allowed = decisionType == AgentToolExecutionGateDecisionType.Allowed;
        return new AgentToolExecutionGateDecision
        {
            GateDecisionId = $"tool-gate-{toolRequest.ToolRequestId}",
            ToolRequestId = toolRequest.ToolRequestId,
            Decision = decisionType,
            ToolKind = toolRequest.ToolKind,
            RequestType = toolRequest.RequestType,
            RiskLevel = toolRequest.RiskLevel,
            EvaluatedAtUtc = evaluatedAtUtc,
            Reasons = reasons,
            Issues = issues,
            GrantsExecution = allowed,
            ExecutesTool = false,
            MutatesSource = false,
            CallsExternalSystem = false,
            SubmitsGitHubReview = false,
            PersistsResult = false,
            PromotesMemory = false,
            CreatesCollectiveMemory = false,
            WritesWeaviate = false,
            RequiresExecutor = allowed
        };
    }

    private static bool RequiresToolExecutionPolicy(AgentToolRequest request) =>
        request.RequestType is AgentToolRequestType.BuildExecutionRequest
            or AgentToolRequestType.TestExecutionRequest
            or AgentToolRequestType.SourceMutationRequest
            or AgentToolRequestType.ExternalEffectRequest;

    private static bool ReferencesMemory(AgentToolRequest request) =>
        request.Inputs.Any(input => IsMemoryRefType(input.RefType)) ||
        request.Evidence.Any(evidence => IsMemoryRefType(evidence.RefType));

    private static bool IsMemoryRefType(string refType) =>
        refType.Contains("Memory", StringComparison.OrdinalIgnoreCase) ||
        refType.Contains("MemoryInfluence", StringComparison.OrdinalIgnoreCase) ||
        refType.Contains("CollectiveMemory", StringComparison.OrdinalIgnoreCase);

    private static bool IsNonExecutableBoundaryIssue(string code) =>
        string.Equals(code, AgentToolRequestValidator.ToolRequestApprovalClaimBlocked, StringComparison.Ordinal) ||
        string.Equals(code, AgentToolRequestValidator.ToolRequestExecutionPermissionClaimBlocked, StringComparison.Ordinal) ||
        string.Equals(code, AgentToolRequestValidator.ToolRequestExecutionResultBlocked, StringComparison.Ordinal) ||
        string.Equals(code, AgentToolRequestValidator.ToolRequestNotExecutable, StringComparison.Ordinal);
}
