namespace IronDev.Core.Agents;

public enum AgentToolRequestStatus
{
    Draft = 1,
    PendingGate = 2,
    Rejected = 3,
    Cancelled = 4
}

public enum AgentToolRequestType
{
    AnalyseOnly = 1,
    ReadOnlyInspection = 2,
    TestExecutionRequest = 3,
    BuildExecutionRequest = 4,
    PatchProposalRequest = 5,
    SourceMutationRequest = 6,
    ExternalEffectRequest = 7
}

public enum AgentToolKind
{
    Unknown = 0,

    CodeStandardsAnalysePatch = 1,
    WorkspaceDiff = 2,
    BuildRun = 3,
    TestRun = 4,
    PatchProposal = 5,
    SourceApply = 6,
    GitStatus = 7,
    GitDiff = 8,
    ExternalHttpCall = 9,
    GitHubReviewSubmission = 10
}

public enum AgentToolRiskLevel
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public sealed record AgentToolRequest
{
    public required string ToolRequestId { get; init; }

    public required AgentToolRequestStatus Status { get; init; }

    public required AgentToolRequestType RequestType { get; init; }

    public required AgentToolKind ToolKind { get; init; }

    public required AgentToolRiskLevel RiskLevel { get; init; }

    public required AgentToolRequestScope Scope { get; init; }

    public required AgentToolRequestActor Actor { get; init; }

    public required string Purpose { get; init; }

    public IReadOnlyList<AgentToolRequestInput> Inputs { get; init; } = [];

    public IReadOnlyList<AgentToolRequestEvidence> Evidence { get; init; } = [];

    public AgentToolRequestApprovalRequirement ApprovalRequirement { get; init; } = new();

    public AgentToolRequestPolicySnapshot PolicySnapshot { get; init; } = new();

    public DateTimeOffset RequestedAtUtc { get; init; }

    public bool ContainsRawPrivateReasoning { get; init; }

    public bool ClaimsApproval { get; init; }

    public bool ClaimsExecutionPermission { get; init; }

    public bool ContainsExecutionResult { get; init; }

    public bool IsExecutableWithoutGate { get; init; }
}

public sealed record AgentToolRequestScope
{
    public required string TenantId { get; init; }

    public required string ProjectId { get; init; }

    public string? CampaignId { get; init; }

    public string? RunId { get; init; }

    public string? AgentRunId { get; init; }

    public required string CorrelationId { get; init; }
}

public sealed record AgentToolRequestActor
{
    public required string AgentId { get; init; }

    public required string AgentName { get; init; }

    public required AgentKind AgentKind { get; init; }

    public required AgentExecutionMode ExecutionMode { get; init; }

    public string? SpecialisationId { get; init; }

    public IReadOnlyList<AgentCapability> DeclaredCapabilities { get; init; } = [];

    public IReadOnlyList<AgentCapability> ForbiddenCapabilities { get; init; } = [];
}

public sealed record AgentToolRequestInput
{
    public required string InputId { get; init; }

    public required string RefType { get; init; }

    public required string RefId { get; init; }

    public string Source { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];

    public bool IsAuthoritativeForAction { get; init; }

    public bool ContainsRawPrivateReasoning { get; init; }

    public bool ContainsSecret { get; init; }

    public bool IsSanitised { get; init; }
}

public sealed record AgentToolRequestEvidence
{
    public required string EvidenceId { get; init; }

    public required string RefType { get; init; }

    public required string RefId { get; init; }

    public string Summary { get; init; } = string.Empty;

    public bool SupportsNeedForTool { get; init; }

    public bool IsAuthorityGrant { get; init; }

    public bool ContainsRawPrivateReasoning { get; init; }

    public bool ContainsSecret { get; init; }
}

public sealed record AgentToolRequestApprovalRequirement
{
    public bool RequiresHumanApproval { get; init; }

    public bool RequiresGovernanceGate { get; init; }

    public bool RequiresMemoryGovernance { get; init; }

    public bool RequiresPolicyApproval { get; init; }

    public bool RequiresDryRunFirst { get; init; }

    public string Reason { get; init; } = string.Empty;
}

public sealed record AgentToolRequestPolicySnapshot
{
    public bool PolicyKnown { get; init; }

    public bool AllowsToolRequest { get; init; }

    public bool AllowsToolExecution { get; init; }

    public bool AllowsSourceMutation { get; init; }

    public bool AllowsExternalEffects { get; init; }

    public bool AllowsGitHubSubmission { get; init; }

    public IReadOnlyList<string> PolicyRefs { get; init; } = [];
}

public sealed record AgentToolRequestValidationResult
{
    public required bool IsValid { get; init; }

    public IReadOnlyList<AgentToolRequestValidationIssue> Issues { get; init; } = [];
}

public sealed record AgentToolRequestValidationIssue
{
    public required string Code { get; init; }

    public required string Severity { get; init; }

    public required string Message { get; init; }

    public string Field { get; init; } = string.Empty;
}

public sealed class AgentToolRequestValidator
{
    public const string ToolRequestIdRequired = "TOOL_REQUEST_ID_REQUIRED";
    public const string ToolRequestStatusInvalid = "TOOL_REQUEST_STATUS_INVALID";
    public const string ToolRequestScopeRequired = "TOOL_REQUEST_SCOPE_REQUIRED";
    public const string ToolRequestAgentRequired = "TOOL_REQUEST_AGENT_REQUIRED";
    public const string ToolRequestAgentDefinitionInvalid = "TOOL_REQUEST_AGENT_DEFINITION_INVALID";
    public const string ToolRequestAgentCapabilityForbidden = "TOOL_REQUEST_AGENT_CAPABILITY_FORBIDDEN";
    public const string ToolRequestKindInvalid = "TOOL_REQUEST_KIND_INVALID";
    public const string ToolRequestTypeInvalid = "TOOL_REQUEST_TYPE_INVALID";
    public const string ToolRequestRiskInvalid = "TOOL_REQUEST_RISK_INVALID";
    public const string ToolRequestPurposeRequired = "TOOL_REQUEST_PURPOSE_REQUIRED";
    public const string ToolRequestInputRequired = "TOOL_REQUEST_INPUT_REQUIRED";
    public const string ToolRequestInputInvalid = "TOOL_REQUEST_INPUT_INVALID";
    public const string ToolRequestInputAuthorityBlocked = "TOOL_REQUEST_INPUT_AUTHORITY_BLOCKED";
    public const string ToolRequestInputRawReasoningBlocked = "TOOL_REQUEST_INPUT_RAW_REASONING_BLOCKED";
    public const string ToolRequestInputSecretBlocked = "TOOL_REQUEST_INPUT_SECRET_BLOCKED";
    public const string ToolRequestEvidenceRequired = "TOOL_REQUEST_EVIDENCE_REQUIRED";
    public const string ToolRequestEvidenceInvalid = "TOOL_REQUEST_EVIDENCE_INVALID";
    public const string ToolRequestEvidenceAuthorityBlocked = "TOOL_REQUEST_EVIDENCE_AUTHORITY_BLOCKED";
    public const string ToolRequestEvidenceRawReasoningBlocked = "TOOL_REQUEST_EVIDENCE_RAW_REASONING_BLOCKED";
    public const string ToolRequestEvidenceSecretBlocked = "TOOL_REQUEST_EVIDENCE_SECRET_BLOCKED";
    public const string ToolRequestApprovalClaimBlocked = "TOOL_REQUEST_APPROVAL_CLAIM_BLOCKED";
    public const string ToolRequestExecutionPermissionClaimBlocked = "TOOL_REQUEST_EXECUTION_PERMISSION_CLAIM_BLOCKED";
    public const string ToolRequestExecutionResultBlocked = "TOOL_REQUEST_EXECUTION_RESULT_BLOCKED";
    public const string ToolRequestGateRequired = "TOOL_REQUEST_GATE_REQUIRED";
    public const string ToolRequestHumanApprovalRequired = "TOOL_REQUEST_HUMAN_APPROVAL_REQUIRED";
    public const string ToolRequestPolicyApprovalRequired = "TOOL_REQUEST_POLICY_APPROVAL_REQUIRED";
    public const string ToolRequestDryRunRequired = "TOOL_REQUEST_DRY_RUN_REQUIRED";
    public const string ToolRequestMemoryGovernanceRequired = "TOOL_REQUEST_MEMORY_GOVERNANCE_REQUIRED";
    public const string ToolRequestNotExecutable = "TOOL_REQUEST_NOT_EXECUTABLE";
    public const string ToolRequestTypeToolMismatch = "TOOL_REQUEST_TYPE_TOOL_MISMATCH";

    private static readonly IReadOnlyDictionary<AgentToolKind, AgentCapability> RequiredCapabilities =
        new Dictionary<AgentToolKind, AgentCapability>
        {
            [AgentToolKind.CodeStandardsAnalysePatch] = AgentCapability.CreateReport,
            [AgentToolKind.WorkspaceDiff] = AgentCapability.CreateReport,
            [AgentToolKind.BuildRun] = AgentCapability.RunTool,
            [AgentToolKind.TestRun] = AgentCapability.RunTool,
            [AgentToolKind.PatchProposal] = AgentCapability.CreateReport,
            [AgentToolKind.SourceApply] = AgentCapability.MutateSource,
            [AgentToolKind.GitStatus] = AgentCapability.CreateReport,
            [AgentToolKind.GitDiff] = AgentCapability.CreateReport,
            [AgentToolKind.ExternalHttpCall] = AgentCapability.CallExternalSystem,
            [AgentToolKind.GitHubReviewSubmission] = AgentCapability.CallExternalSystem
        };

    private static readonly IReadOnlyDictionary<AgentToolRequestType, IReadOnlySet<AgentToolKind>> AllowedToolsByType =
        new Dictionary<AgentToolRequestType, IReadOnlySet<AgentToolKind>>
        {
            [AgentToolRequestType.AnalyseOnly] = Set(AgentToolKind.CodeStandardsAnalysePatch),
            [AgentToolRequestType.ReadOnlyInspection] = Set(AgentToolKind.WorkspaceDiff, AgentToolKind.GitStatus, AgentToolKind.GitDiff),
            [AgentToolRequestType.TestExecutionRequest] = Set(AgentToolKind.TestRun),
            [AgentToolRequestType.BuildExecutionRequest] = Set(AgentToolKind.BuildRun),
            [AgentToolRequestType.PatchProposalRequest] = Set(AgentToolKind.PatchProposal),
            [AgentToolRequestType.SourceMutationRequest] = Set(AgentToolKind.SourceApply),
            [AgentToolRequestType.ExternalEffectRequest] = Set(AgentToolKind.ExternalHttpCall, AgentToolKind.GitHubReviewSubmission)
        };

    private readonly IReadOnlyDictionary<string, AgentDefinition> _agentDefinitionsById;

    public AgentToolRequestValidator(IReadOnlyList<AgentDefinition>? agentDefinitions = null)
    {
        _agentDefinitionsById = (agentDefinitions ?? AgentDefinitionCatalog.All)
            .GroupBy(agent => agent.AgentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public AgentToolRequestValidationResult Validate(AgentToolRequest request)
    {
        var issues = new List<AgentToolRequestValidationIssue>();

        ValidateRequestIdentity(request, issues);
        ValidateScope(request, issues);
        ValidateActor(request, issues);
        ValidateToolShape(request, issues);
        ValidateInputs(request, issues);
        ValidateEvidence(request, issues);
        ValidateNonExecutableBoundary(request, issues);
        ValidateApprovalRequirements(request, issues);
        ValidateMemoryGovernance(request, issues);

        return new AgentToolRequestValidationResult
        {
            IsValid = issues.All(issue => !string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            Issues = issues
        };
    }

    private void ValidateRequestIdentity(AgentToolRequest request, List<AgentToolRequestValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(request.ToolRequestId))
            AddError(issues, ToolRequestIdRequired, "ToolRequestId is required.", nameof(AgentToolRequest.ToolRequestId));

        if (request.Status is not AgentToolRequestStatus.Draft and not AgentToolRequestStatus.PendingGate)
            AddError(issues, ToolRequestStatusInvalid, "Tool request status must be Draft or PendingGate.", nameof(AgentToolRequest.Status));

        if (string.IsNullOrWhiteSpace(request.Purpose))
            AddError(issues, ToolRequestPurposeRequired, "Purpose is required.", nameof(AgentToolRequest.Purpose));

        if (request.ContainsRawPrivateReasoning)
            AddError(issues, ToolRequestInputRawReasoningBlocked, "Tool request cannot contain raw private reasoning.", nameof(AgentToolRequest.ContainsRawPrivateReasoning));
    }

    private static void ValidateScope(AgentToolRequest request, List<AgentToolRequestValidationIssue> issues)
    {
        if (request.Scope is null)
        {
            AddError(issues, ToolRequestScopeRequired, "Scope is required.", nameof(AgentToolRequest.Scope));
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Scope.TenantId))
            AddError(issues, ToolRequestScopeRequired, "TenantId is required.", nameof(AgentToolRequestScope.TenantId));

        if (string.IsNullOrWhiteSpace(request.Scope.ProjectId))
            AddError(issues, ToolRequestScopeRequired, "ProjectId is required.", nameof(AgentToolRequestScope.ProjectId));

        if (string.IsNullOrWhiteSpace(request.Scope.CorrelationId))
            AddError(issues, ToolRequestScopeRequired, "CorrelationId is required.", nameof(AgentToolRequestScope.CorrelationId));

        if (request.Status == AgentToolRequestStatus.PendingGate)
        {
            if (string.IsNullOrWhiteSpace(request.Scope.RunId))
                AddError(issues, ToolRequestScopeRequired, "PendingGate requests require RunId.", nameof(AgentToolRequestScope.RunId));

            if (string.IsNullOrWhiteSpace(request.Scope.AgentRunId))
                AddError(issues, ToolRequestScopeRequired, "PendingGate requests require AgentRunId.", nameof(AgentToolRequestScope.AgentRunId));
        }
    }

    private void ValidateActor(AgentToolRequest request, List<AgentToolRequestValidationIssue> issues)
    {
        if (request.Actor is null)
        {
            AddError(issues, ToolRequestAgentRequired, "Actor is required.", nameof(AgentToolRequest.Actor));
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Actor.AgentId))
            AddError(issues, ToolRequestAgentRequired, "AgentId is required.", nameof(AgentToolRequestActor.AgentId));

        if (string.IsNullOrWhiteSpace(request.Actor.AgentName))
            AddError(issues, ToolRequestAgentRequired, "AgentName is required.", nameof(AgentToolRequestActor.AgentName));

        if (!Enum.IsDefined(request.Actor.AgentKind))
            AddError(issues, ToolRequestAgentDefinitionInvalid, "AgentKind is invalid.", nameof(AgentToolRequestActor.AgentKind));

        if (!Enum.IsDefined(request.Actor.ExecutionMode))
            AddError(issues, ToolRequestAgentDefinitionInvalid, "ExecutionMode is invalid.", nameof(AgentToolRequestActor.ExecutionMode));

        if (string.IsNullOrWhiteSpace(request.Actor.AgentId) ||
            !_agentDefinitionsById.TryGetValue(request.Actor.AgentId, out var definition))
        {
            AddError(issues, ToolRequestAgentDefinitionInvalid, "Actor must reference a known agent definition.", nameof(AgentToolRequestActor.AgentId));
            return;
        }

        if (!string.Equals(definition.Name, request.Actor.AgentName, StringComparison.Ordinal) ||
            definition.Kind != request.Actor.AgentKind ||
            definition.ExecutionMode != request.Actor.ExecutionMode)
        {
            AddError(issues, ToolRequestAgentDefinitionInvalid, "Actor identity must match the agent definition.", nameof(AgentToolRequest.Actor));
        }

        var definitionCapabilities = definition.Capabilities ?? new HashSet<AgentCapability>();
        var definitionForbidden = definition.ForbiddenCapabilities ?? new HashSet<AgentCapability>();

        foreach (var capability in request.Actor.DeclaredCapabilities)
        {
            if (!definitionCapabilities.Contains(capability))
                AddError(issues, ToolRequestAgentDefinitionInvalid, $"Actor declared capability not present on agent definition: {capability}.", nameof(AgentToolRequestActor.DeclaredCapabilities));
        }

        if (request.ToolKind != AgentToolKind.Unknown &&
            RequiredCapabilities.TryGetValue(request.ToolKind, out var requiredCapability))
        {
            if (!definitionCapabilities.Contains(requiredCapability) ||
                !request.Actor.DeclaredCapabilities.Contains(requiredCapability) ||
                definitionForbidden.Contains(requiredCapability) ||
                request.Actor.ForbiddenCapabilities.Contains(requiredCapability))
            {
                AddError(issues, ToolRequestAgentCapabilityForbidden, $"Actor cannot request tool kind {request.ToolKind} because capability {requiredCapability} is unavailable or forbidden.", nameof(AgentToolRequestActor.DeclaredCapabilities));
            }
        }
    }

    private static void ValidateToolShape(AgentToolRequest request, List<AgentToolRequestValidationIssue> issues)
    {
        if (!Enum.IsDefined(request.ToolKind) || request.ToolKind == AgentToolKind.Unknown)
            AddError(issues, ToolRequestKindInvalid, "ToolKind must be known.", nameof(AgentToolRequest.ToolKind));

        if (!Enum.IsDefined(request.RequestType))
            AddError(issues, ToolRequestTypeInvalid, "RequestType is invalid.", nameof(AgentToolRequest.RequestType));

        if (!Enum.IsDefined(request.RiskLevel))
            AddError(issues, ToolRequestRiskInvalid, "RiskLevel is invalid.", nameof(AgentToolRequest.RiskLevel));

        if (AllowedToolsByType.TryGetValue(request.RequestType, out var allowedTools) &&
            !allowedTools.Contains(request.ToolKind))
        {
            AddError(issues, ToolRequestTypeToolMismatch, "RequestType does not match ToolKind.", nameof(AgentToolRequest.ToolKind));
        }

        if ((request.RequestType == AgentToolRequestType.SourceMutationRequest ||
             request.RequestType == AgentToolRequestType.ExternalEffectRequest) &&
            request.RiskLevel != AgentToolRiskLevel.Critical)
        {
            AddError(issues, ToolRequestRiskInvalid, "Source mutation and external effect requests must be Critical risk.", nameof(AgentToolRequest.RiskLevel));
        }

        if ((request.RequestType == AgentToolRequestType.BuildExecutionRequest ||
             request.RequestType == AgentToolRequestType.TestExecutionRequest) &&
            request.RiskLevel == AgentToolRiskLevel.Low)
        {
            AddError(issues, ToolRequestRiskInvalid, "Build and test requests must be Medium or High risk.", nameof(AgentToolRequest.RiskLevel));
        }
    }

    private static void ValidateInputs(AgentToolRequest request, List<AgentToolRequestValidationIssue> issues)
    {
        if (request.Inputs.Count == 0)
        {
            AddError(issues, ToolRequestInputRequired, "At least one input is required.", nameof(AgentToolRequest.Inputs));
            return;
        }

        foreach (var input in request.Inputs)
        {
            if (string.IsNullOrWhiteSpace(input.InputId) ||
                string.IsNullOrWhiteSpace(input.RefType) ||
                string.IsNullOrWhiteSpace(input.RefId))
            {
                AddError(issues, ToolRequestInputInvalid, "InputId, RefType, and RefId are required.", nameof(AgentToolRequestInput.InputId));
            }

            if (input.IsAuthoritativeForAction)
                AddError(issues, ToolRequestInputAuthorityBlocked, "Inputs cannot be authoritative for action.", nameof(AgentToolRequestInput.IsAuthoritativeForAction));

            if (input.ContainsRawPrivateReasoning)
                AddError(issues, ToolRequestInputRawReasoningBlocked, "Inputs cannot contain raw private reasoning.", nameof(AgentToolRequestInput.ContainsRawPrivateReasoning));

            if (input.ContainsSecret && (!input.IsSanitised || input.EvidenceRefs.Count == 0))
                AddError(issues, ToolRequestInputSecretBlocked, "Secret-bearing inputs must be sanitised and backed by redaction evidence.", nameof(AgentToolRequestInput.ContainsSecret));
        }
    }

    private static void ValidateEvidence(AgentToolRequest request, List<AgentToolRequestValidationIssue> issues)
    {
        if (request.Evidence.Count == 0)
        {
            AddError(issues, ToolRequestEvidenceRequired, "At least one evidence item is required.", nameof(AgentToolRequest.Evidence));
            return;
        }

        if (!request.Evidence.Any(evidence => evidence.SupportsNeedForTool))
            AddError(issues, ToolRequestEvidenceInvalid, "At least one evidence item must support the need for the tool.", nameof(AgentToolRequestEvidence.SupportsNeedForTool));

        foreach (var evidence in request.Evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence.EvidenceId) ||
                string.IsNullOrWhiteSpace(evidence.RefType) ||
                string.IsNullOrWhiteSpace(evidence.RefId))
            {
                AddError(issues, ToolRequestEvidenceInvalid, "EvidenceId, RefType, and RefId are required.", nameof(AgentToolRequestEvidence.EvidenceId));
            }

            if (evidence.IsAuthorityGrant)
                AddError(issues, ToolRequestEvidenceAuthorityBlocked, "Evidence cannot grant authority.", nameof(AgentToolRequestEvidence.IsAuthorityGrant));

            if (evidence.ContainsRawPrivateReasoning)
                AddError(issues, ToolRequestEvidenceRawReasoningBlocked, "Evidence cannot contain raw private reasoning.", nameof(AgentToolRequestEvidence.ContainsRawPrivateReasoning));

            if (evidence.ContainsSecret)
                AddError(issues, ToolRequestEvidenceSecretBlocked, "Evidence cannot contain secret material.", nameof(AgentToolRequestEvidence.ContainsSecret));
        }
    }

    private static void ValidateNonExecutableBoundary(AgentToolRequest request, List<AgentToolRequestValidationIssue> issues)
    {
        if (request.ClaimsApproval)
            AddError(issues, ToolRequestApprovalClaimBlocked, "Tool request cannot claim approval.", nameof(AgentToolRequest.ClaimsApproval));

        if (request.ClaimsExecutionPermission)
            AddError(issues, ToolRequestExecutionPermissionClaimBlocked, "Tool request cannot claim execution permission.", nameof(AgentToolRequest.ClaimsExecutionPermission));

        if (request.ContainsExecutionResult)
            AddError(issues, ToolRequestExecutionResultBlocked, "Tool request cannot contain execution result.", nameof(AgentToolRequest.ContainsExecutionResult));

        if (request.IsExecutableWithoutGate)
            AddError(issues, ToolRequestNotExecutable, "Tool request cannot be executable without a gate.", nameof(AgentToolRequest.IsExecutableWithoutGate));
    }

    private static void ValidateApprovalRequirements(AgentToolRequest request, List<AgentToolRequestValidationIssue> issues)
    {
        var approval = request.ApprovalRequirement ?? new AgentToolRequestApprovalRequirement();

        if (request.RequestType == AgentToolRequestType.SourceMutationRequest && request.ToolKind == AgentToolKind.SourceApply)
        {
            RequireHumanApproval(approval, issues);
            RequireGovernanceGate(approval, issues);
            RequirePolicyApproval(approval, issues);

            if (!approval.RequiresDryRunFirst)
                AddError(issues, ToolRequestDryRunRequired, "Source mutation requests require dry-run first.", nameof(AgentToolRequestApprovalRequirement.RequiresDryRunFirst));
        }

        if (request.RequestType == AgentToolRequestType.ExternalEffectRequest &&
            (request.ToolKind == AgentToolKind.ExternalHttpCall || request.ToolKind == AgentToolKind.GitHubReviewSubmission))
        {
            RequireHumanApproval(approval, issues);
            RequireGovernanceGate(approval, issues);
            RequirePolicyApproval(approval, issues);
        }

        if (request.RequestType is AgentToolRequestType.BuildExecutionRequest or AgentToolRequestType.TestExecutionRequest)
            RequireGovernanceGate(approval, issues);
    }

    private static void ValidateMemoryGovernance(AgentToolRequest request, List<AgentToolRequestValidationIssue> issues)
    {
        if (!ReferencesMemory(request))
            return;

        if (request.ApprovalRequirement?.RequiresMemoryGovernance != true)
            AddError(issues, ToolRequestMemoryGovernanceRequired, "Memory-backed tool requests require memory governance.", nameof(AgentToolRequestApprovalRequirement.RequiresMemoryGovernance));
    }

    private static bool ReferencesMemory(AgentToolRequest request) =>
        request.Inputs.Any(input => IsMemoryRefType(input.RefType)) ||
        request.Evidence.Any(evidence => IsMemoryRefType(evidence.RefType));

    private static bool IsMemoryRefType(string refType) =>
        refType.Contains("Memory", StringComparison.OrdinalIgnoreCase) ||
        refType.Contains("MemoryInfluence", StringComparison.OrdinalIgnoreCase) ||
        refType.Contains("CollectiveMemory", StringComparison.OrdinalIgnoreCase);

    private static void RequireHumanApproval(AgentToolRequestApprovalRequirement approval, List<AgentToolRequestValidationIssue> issues)
    {
        if (!approval.RequiresHumanApproval)
            AddError(issues, ToolRequestHumanApprovalRequired, "Tool request requires human approval metadata.", nameof(AgentToolRequestApprovalRequirement.RequiresHumanApproval));
    }

    private static void RequireGovernanceGate(AgentToolRequestApprovalRequirement approval, List<AgentToolRequestValidationIssue> issues)
    {
        if (!approval.RequiresGovernanceGate)
            AddError(issues, ToolRequestGateRequired, "Tool request requires governance gate metadata.", nameof(AgentToolRequestApprovalRequirement.RequiresGovernanceGate));
    }

    private static void RequirePolicyApproval(AgentToolRequestApprovalRequirement approval, List<AgentToolRequestValidationIssue> issues)
    {
        if (!approval.RequiresPolicyApproval)
            AddError(issues, ToolRequestPolicyApprovalRequired, "Tool request requires policy approval metadata.", nameof(AgentToolRequestApprovalRequirement.RequiresPolicyApproval));
    }

    private static void AddError(List<AgentToolRequestValidationIssue> issues, string code, string message, string field) =>
        issues.Add(new AgentToolRequestValidationIssue
        {
            Code = code,
            Severity = "error",
            Message = message,
            Field = field
        });

    private static IReadOnlySet<AgentToolKind> Set(params AgentToolKind[] tools) => new HashSet<AgentToolKind>(tools);
}
