using System;
using System.Collections.Generic;
using System.Linq;

namespace IronDev.Core.Agents;

public enum AgentModelProviderKind
{
    Unknown = 0,
    Fake = 1,
    OpenAI = 2,
    Anthropic = 3,
    Gemini = 4,
    Ollama = 5,
    LocalOpenAICompatible = 6
}

public enum AgentModelRole
{
    System = 1,
    Developer = 2,
    User = 3,
    Assistant = 4,
    Tool = 5
}

public sealed record AgentModelProfile
{
    public required string ProfileId { get; init; }
    public required string DisplayName { get; init; }
    public required AgentModelProviderKind ProviderKind { get; init; }
    public required string ModelName { get; init; }
    public bool IsEnabled { get; init; }
    public bool AllowsToolCalls { get; init; }
    public bool AllowsJsonOutput { get; init; }
    public bool AllowsStreaming { get; init; }
    public bool AllowsExternalNetwork { get; init; }
    public int MaxInputTokens { get; init; }
    public int MaxOutputTokens { get; init; }
    public decimal? Temperature { get; init; }
    public IReadOnlyList<string> AllowedAgentIds { get; init; } = [];
    public IReadOnlyList<string> AllowedSpecialisationIds { get; init; } = [];
}

public sealed record AgentModelMessage
{
    public required string MessageId { get; init; }
    public required AgentModelRole Role { get; init; }
    public required string Content { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsSystemPromptLeak { get; init; }
    public bool ContainsDeveloperPromptLeak { get; init; }
    public bool IsAuthoritativeForAction { get; init; }
}

public sealed record AgentModelRequestContext
{
    public IReadOnlyList<string> InputRefs { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> MemoryRefs { get; init; } = [];
    public IReadOnlyList<string> AuditRefs { get; init; } = [];
    public bool IncludesRetrievedMemory { get; init; }
    public bool IncludesCollectiveMemoryCandidate { get; init; }
    public bool IncludesLocalMemory { get; init; }
    public bool IncludesRawPromptOrCompletion { get; init; }
    public bool IncludesPrivateReasoning { get; init; }
    public bool IncludesAuthoritySource { get; init; }
}

public sealed record AgentModelResponseFormat
{
    public required string FormatId { get; init; }
    public required string OutputContractName { get; init; }
    public bool RequiresJson { get; init; }
    public bool RequiresSchemaValidation { get; init; }
    public bool AllowsFreeText { get; init; }
    public IReadOnlyList<string> RequiredFields { get; init; } = [];
}

public sealed record AgentModelSafetyFlags
{
    public bool MayGrantApproval { get; init; }
    public bool MayGrantPolicyApproval { get; init; }
    public bool MayRepresentHumanApproval { get; init; }
    public bool MayPromoteMemory { get; init; }
    public bool MayCreateCollectiveMemory { get; init; }
    public bool MayRunTools { get; init; }
    public bool MayMutateSource { get; init; }
    public bool MayCallExternalSystems { get; init; }
    public bool MaySubmitGitHubReview { get; init; }
    public bool MayPersistProposal { get; init; }
}

public sealed record AgentModelRequest
{
    public required string RequestId { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public string? CampaignId { get; init; }
    public string? AgentRunId { get; init; }
    public required string AgentId { get; init; }
    public string? SpecialisationId { get; init; }
    public required AgentModelProfile Profile { get; init; }
    public IReadOnlyList<AgentModelMessage> Messages { get; init; } = [];
    public AgentModelRequestContext Context { get; init; } = new();
    public AgentModelResponseFormat ResponseFormat { get; init; } = new()
    {
        FormatId = string.Empty,
        OutputContractName = string.Empty
    };
    public AgentModelSafetyFlags SafetyFlags { get; init; } = new();
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed record AgentModelUsage
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int TotalTokens => InputTokens + OutputTokens;
    public decimal? EstimatedCost { get; init; }
    public string? Currency { get; init; }
}

public sealed record AgentModelResponse
{
    public required string ResponseId { get; init; }
    public required string RequestId { get; init; }
    public required string AgentId { get; init; }
    public string? SpecialisationId { get; init; }
    public required AgentModelProviderKind ProviderKind { get; init; }
    public required string ModelName { get; init; }
    public required string Content { get; init; }
    public string? StructuredJson { get; init; }
    public AgentModelUsage Usage { get; init; } = new();
    public AgentModelSafetyFlags ClaimedSafetyFlags { get; init; } = new();
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsSystemPromptLeak { get; init; }
    public bool ContainsDeveloperPromptLeak { get; init; }
    public bool ContainsAuthorityClaim { get; init; }
    public bool ContainsToolCommand { get; init; }
    public bool ContainsSourceMutationCommand { get; init; }
    public bool ContainsMemoryPromotionCommand { get; init; }
    public DateTimeOffset CompletedAtUtc { get; init; }
}

public sealed record AgentModelInvocationAudit
{
    public required string AuditId { get; init; }
    public required string RequestId { get; init; }
    public required string AgentId { get; init; }
    public string? SpecialisationId { get; init; }
    public required string ProfileId { get; init; }
    public required AgentModelProviderKind ProviderKind { get; init; }
    public required string ModelName { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public bool Succeeded { get; init; }
    public IReadOnlyList<string> InputRefs { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public AgentModelUsage Usage { get; init; } = new();
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsPromptLeak { get; init; }
    public bool ContainsAuthorityClaim { get; init; }
    public bool ContainsToolCommand { get; init; }
    public bool ContainsSourceMutationCommand { get; init; }
    public bool ContainsMemoryPromotionCommand { get; init; }
    public bool GrantsApproval { get; init; }
    public bool GrantsAuthority { get; init; }
    public bool GrantsMemoryPromotion { get; init; }
}

public sealed record AgentModelAdapterIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string? Field { get; init; }
}

public sealed record AgentModelAdapterResult
{
    public required bool Succeeded { get; init; }
    public AgentModelResponse? Response { get; init; }
    public AgentModelInvocationAudit? Audit { get; init; }
    public IReadOnlyList<AgentModelAdapterIssue> Issues { get; init; } = [];
}

public sealed record AgentModelAdapterOptions
{
    public int MaxMessages { get; init; } = 32;
    public int MaxMessageCharacters { get; init; } = 12000;
    public int MaxContextRefs { get; init; } = 128;
}

public interface IAgentModelAdapter
{
    AgentModelAdapterResult Invoke(
        AgentModelRequest request,
        DateTimeOffset invokedAtUtc);
}

public sealed class AgentModelAdapterValidator
{
    public const string ModelRequestIdRequired = "MODEL_REQUEST_ID_REQUIRED";
    public const string ModelRequestScopeRequired = "MODEL_REQUEST_SCOPE_REQUIRED";
    public const string ModelRequestAgentRequired = "MODEL_REQUEST_AGENT_REQUIRED";
    public const string ModelProfileRequired = "MODEL_PROFILE_REQUIRED";
    public const string ModelProfileInvalid = "MODEL_PROFILE_INVALID";
    public const string ModelMessagesRequired = "MODEL_MESSAGES_REQUIRED";
    public const string ModelMessageContentRequired = "MODEL_MESSAGE_CONTENT_REQUIRED";
    public const string ModelRequestRawReasoningBlocked = "MODEL_REQUEST_RAW_REASONING_BLOCKED";
    public const string ModelRequestPromptLeakBlocked = "MODEL_REQUEST_PROMPT_LEAK_BLOCKED";
    public const string ModelRequestAuthorityBlocked = "MODEL_REQUEST_AUTHORITY_BLOCKED";
    public const string ModelRequestToolBlocked = "MODEL_REQUEST_TOOL_BLOCKED";
    public const string ModelRequestSourceMutationBlocked = "MODEL_REQUEST_SOURCE_MUTATION_BLOCKED";
    public const string ModelRequestExternalCallBlocked = "MODEL_REQUEST_EXTERNAL_CALL_BLOCKED";
    public const string ModelRequestMemoryPromotionBlocked = "MODEL_REQUEST_MEMORY_PROMOTION_BLOCKED";
    public const string ModelRequestCollectiveMemoryBlocked = "MODEL_REQUEST_COLLECTIVE_MEMORY_BLOCKED";
    public const string ModelRequestProposalPersistenceBlocked = "MODEL_REQUEST_PROPOSAL_PERSISTENCE_BLOCKED";
    public const string ModelOutputFormatRequired = "MODEL_OUTPUT_FORMAT_REQUIRED";
    public const string ModelOutputSchemaRequired = "MODEL_OUTPUT_SCHEMA_REQUIRED";
    public const string ModelContextTooLarge = "MODEL_CONTEXT_TOO_LARGE";
    public const string ModelSpecialisationIncompatible = "MODEL_SPECIALISATION_INCOMPATIBLE";

    public const string ModelResponseIdRequired = "MODEL_RESPONSE_ID_REQUIRED";
    public const string ModelResponseRequestMismatch = "MODEL_RESPONSE_REQUEST_MISMATCH";
    public const string ModelResponseAgentMismatch = "MODEL_RESPONSE_AGENT_MISMATCH";
    public const string ModelResponseContentRequired = "MODEL_RESPONSE_CONTENT_REQUIRED";
    public const string ModelResponseRawReasoningBlocked = "MODEL_RESPONSE_RAW_REASONING_BLOCKED";
    public const string ModelResponsePromptLeakBlocked = "MODEL_RESPONSE_PROMPT_LEAK_BLOCKED";
    public const string ModelResponseAuthorityClaimBlocked = "MODEL_RESPONSE_AUTHORITY_CLAIM_BLOCKED";
    public const string ModelResponseToolCommandBlocked = "MODEL_RESPONSE_TOOL_COMMAND_BLOCKED";
    public const string ModelResponseSourceMutationBlocked = "MODEL_RESPONSE_SOURCE_MUTATION_BLOCKED";
    public const string ModelResponseMemoryPromotionBlocked = "MODEL_RESPONSE_MEMORY_PROMOTION_BLOCKED";
    public const string ModelResponseSafetyFlagBlocked = "MODEL_RESPONSE_SAFETY_FLAG_BLOCKED";
    public const string ModelResponseUsageInvalid = "MODEL_RESPONSE_USAGE_INVALID";

    public const string ModelAuditIdRequired = "MODEL_AUDIT_ID_REQUIRED";
    public const string ModelAuditRequestIdRequired = "MODEL_AUDIT_REQUEST_ID_REQUIRED";
    public const string ModelAuditAgentRequired = "MODEL_AUDIT_AGENT_REQUIRED";
    public const string ModelAuditProfileRequired = "MODEL_AUDIT_PROFILE_REQUIRED";
    public const string ModelAuditPromptLeakBlocked = "MODEL_AUDIT_PROMPT_LEAK_BLOCKED";
    public const string ModelAuditRawReasoningBlocked = "MODEL_AUDIT_RAW_REASONING_BLOCKED";
    public const string ModelAuditAuthorityBlocked = "MODEL_AUDIT_AUTHORITY_BLOCKED";
    public const string ModelAuditToolCommandBlocked = "MODEL_AUDIT_TOOL_COMMAND_BLOCKED";
    public const string ModelAuditSourceMutationBlocked = "MODEL_AUDIT_SOURCE_MUTATION_BLOCKED";
    public const string ModelAuditMemoryPromotionBlocked = "MODEL_AUDIT_MEMORY_PROMOTION_BLOCKED";
    public const string ModelAuditUsageInvalid = "MODEL_AUDIT_USAGE_INVALID";

    private static readonly string[] RawPrivateReasoningMarkers =
    [
        "raw prompt",
        "raw completion",
        "chain-of-thought",
        "chain of thought",
        "scratchpad",
        "scratch pad",
        "private reasoning",
        "hidden reasoning",
        "hidden deliberation",
        "system prompt",
        "developer prompt"
    ];

    private static readonly string[] AuthorityClaimMarkers =
    [
        "approval granted",
        "approved for execution",
        "policy cleared",
        "human approved",
        "authoritative for action",
        "may execute",
        "can execute",
        "grant authority",
        "override policy",
        "bypass governance"
    ];

    private static readonly string[] ToolCommandMarkers =
    [
        "run this tool",
        "call this tool",
        "execute tool",
        "tool command"
    ];

    private static readonly string[] SourceMutationMarkers =
    [
        "apply this patch",
        "mutate source",
        "write file",
        "delete file",
        "modify source"
    ];

    private static readonly string[] MemoryPromotionMarkers =
    [
        "promote memory",
        "accepted memory",
        "create collectivememory",
        "collectivememory creation",
        "persist proposal"
    ];

    private readonly AgentModelAdapterOptions _options;
    private readonly IReadOnlyList<AgentDefinition> _agentDefinitions;
    private readonly IReadOnlyList<AgentSpecialisationDefinition> _specialisations;
    private readonly AgentSpecialisationValidator _specialisationValidator;

    public AgentModelAdapterValidator(
        AgentModelAdapterOptions? options = null,
        IReadOnlyList<AgentDefinition>? agentDefinitions = null,
        IReadOnlyList<AgentSpecialisationDefinition>? specialisations = null,
        AgentSpecialisationValidator? specialisationValidator = null)
    {
        _options = options ?? new AgentModelAdapterOptions();
        _agentDefinitions = agentDefinitions ?? AgentDefinitionCatalog.All;
        _specialisations = specialisations ?? AgentSpecialisationCatalog.All;
        _specialisationValidator = specialisationValidator ?? new AgentSpecialisationValidator();
    }

    public IReadOnlyList<AgentModelAdapterIssue> ValidateRequest(AgentModelRequest request)
    {
        var issues = new List<AgentModelAdapterIssue>();

        if (string.IsNullOrWhiteSpace(request.RequestId))
            AddError(issues, ModelRequestIdRequired, "RequestId is required.", nameof(request.RequestId));

        if (string.IsNullOrWhiteSpace(request.TenantId) ||
            string.IsNullOrWhiteSpace(request.ProjectId))
        {
            AddError(issues, ModelRequestScopeRequired, "TenantId and ProjectId are required.", "Scope");
        }

        if (string.IsNullOrWhiteSpace(request.AgentId))
            AddError(issues, ModelRequestAgentRequired, "AgentId is required.", nameof(request.AgentId));

        ValidateProfile(request, issues);
        ValidateResponseFormat(request.ResponseFormat, issues);
        ValidateMessages(request, issues);
        ValidateContext(request, issues);
        ValidateRequestSafetyFlags(request.SafetyFlags, issues);
        ValidateSpecialisationCompatibility(request, issues);

        if (request.Context.IncludesRawPromptOrCompletion || request.Context.IncludesPrivateReasoning)
        {
            AddError(issues, ModelRequestRawReasoningBlocked, "Model request context cannot include raw prompt/completion or private reasoning.", nameof(request.Context));
        }

        return issues;
    }

    public IReadOnlyList<AgentModelAdapterIssue> ValidateResponse(
        AgentModelRequest request,
        AgentModelResponse response)
    {
        var issues = new List<AgentModelAdapterIssue>();

        if (string.IsNullOrWhiteSpace(response.ResponseId))
            AddError(issues, ModelResponseIdRequired, "ResponseId is required.", nameof(response.ResponseId));

        if (!string.Equals(response.RequestId, request.RequestId, StringComparison.Ordinal))
            AddError(issues, ModelResponseRequestMismatch, "Response RequestId must match request.", nameof(response.RequestId));

        if (!string.Equals(response.AgentId, request.AgentId, StringComparison.Ordinal))
            AddError(issues, ModelResponseAgentMismatch, "Response AgentId must match request.", nameof(response.AgentId));

        if (string.IsNullOrWhiteSpace(response.Content))
            AddError(issues, ModelResponseContentRequired, "Response content is required.", nameof(response.Content));

        if (response.ContainsRawPrivateReasoning || ContainsAny(response.Content, RawPrivateReasoningMarkers))
            AddError(issues, ModelResponseRawReasoningBlocked, "Model response cannot include raw/private reasoning or prompt text.", nameof(response.Content));

        if (response.ContainsSystemPromptLeak || response.ContainsDeveloperPromptLeak)
            AddError(issues, ModelResponsePromptLeakBlocked, "Model response cannot leak system or developer prompts.", nameof(response.Content));

        if (response.ContainsAuthorityClaim || ContainsAny(response.Content, AuthorityClaimMarkers))
            AddError(issues, ModelResponseAuthorityClaimBlocked, "Model response cannot claim approval, authority, or execution permission.", nameof(response.Content));

        if (response.ContainsToolCommand || ContainsAny(response.Content, ToolCommandMarkers))
            AddError(issues, ModelResponseToolCommandBlocked, "Model response cannot issue tool commands.", nameof(response.Content));

        if (response.ContainsSourceMutationCommand || ContainsAny(response.Content, SourceMutationMarkers))
            AddError(issues, ModelResponseSourceMutationBlocked, "Model response cannot command source mutation.", nameof(response.Content));

        if (response.ContainsMemoryPromotionCommand || ContainsAny(response.Content, MemoryPromotionMarkers))
            AddError(issues, ModelResponseMemoryPromotionBlocked, "Model response cannot command memory promotion or proposal persistence.", nameof(response.Content));

        if (AnySafetyFlag(response.ClaimedSafetyFlags))
            AddError(issues, ModelResponseSafetyFlagBlocked, "Model response claimed safety flags must all remain false.", nameof(response.ClaimedSafetyFlags));

        if (response.Usage.InputTokens < 0 || response.Usage.OutputTokens < 0)
            AddError(issues, ModelResponseUsageInvalid, "Model response usage token counts must be non-negative.", nameof(response.Usage));

        return issues;
    }

    public IReadOnlyList<AgentModelAdapterIssue> ValidateAudit(AgentModelInvocationAudit audit)
    {
        var issues = new List<AgentModelAdapterIssue>();

        if (string.IsNullOrWhiteSpace(audit.AuditId))
            AddError(issues, ModelAuditIdRequired, "AuditId is required.", nameof(audit.AuditId));

        if (string.IsNullOrWhiteSpace(audit.RequestId))
            AddError(issues, ModelAuditRequestIdRequired, "Audit RequestId is required.", nameof(audit.RequestId));

        if (string.IsNullOrWhiteSpace(audit.AgentId))
            AddError(issues, ModelAuditAgentRequired, "Audit AgentId is required.", nameof(audit.AgentId));

        if (string.IsNullOrWhiteSpace(audit.ProfileId))
            AddError(issues, ModelAuditProfileRequired, "Audit ProfileId is required.", nameof(audit.ProfileId));

        if (audit.ContainsPromptLeak)
            AddError(issues, ModelAuditPromptLeakBlocked, "Audit cannot contain prompt leaks.", nameof(audit.ContainsPromptLeak));

        if (audit.ContainsRawPrivateReasoning)
            AddError(issues, ModelAuditRawReasoningBlocked, "Audit cannot contain raw private reasoning.", nameof(audit.ContainsRawPrivateReasoning));

        if (audit.ContainsAuthorityClaim || audit.GrantsApproval || audit.GrantsAuthority)
            AddError(issues, ModelAuditAuthorityBlocked, "Audit cannot grant approval or authority.", nameof(audit.GrantsAuthority));

        if (audit.ContainsToolCommand)
            AddError(issues, ModelAuditToolCommandBlocked, "Audit cannot carry tool commands.", nameof(audit.ContainsToolCommand));

        if (audit.ContainsSourceMutationCommand)
            AddError(issues, ModelAuditSourceMutationBlocked, "Audit cannot carry source mutation commands.", nameof(audit.ContainsSourceMutationCommand));

        if (audit.ContainsMemoryPromotionCommand || audit.GrantsMemoryPromotion)
            AddError(issues, ModelAuditMemoryPromotionBlocked, "Audit cannot grant or carry memory promotion.", nameof(audit.GrantsMemoryPromotion));

        if (audit.Usage.InputTokens < 0 || audit.Usage.OutputTokens < 0)
            AddError(issues, ModelAuditUsageInvalid, "Audit usage token counts must be non-negative.", nameof(audit.Usage));

        return issues;
    }

    private void ValidateProfile(AgentModelRequest request, List<AgentModelAdapterIssue> issues)
    {
        var profile = request.Profile;
        if (profile is null)
        {
            AddError(issues, ModelProfileRequired, "Model profile is required.", nameof(request.Profile));
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.ProfileId) ||
            string.IsNullOrWhiteSpace(profile.ModelName) ||
            !Enum.IsDefined(typeof(AgentModelProviderKind), profile.ProviderKind) ||
            profile.ProviderKind == AgentModelProviderKind.Unknown ||
            profile.MaxInputTokens <= 0 ||
            profile.MaxOutputTokens <= 0 ||
            profile.AllowsToolCalls ||
            profile.ProviderKind != AgentModelProviderKind.Fake ||
            (profile.ProviderKind == AgentModelProviderKind.Fake && profile.AllowsExternalNetwork) ||
            (profile.AllowedAgentIds.Count > 0 && !profile.AllowedAgentIds.Contains(request.AgentId, StringComparer.Ordinal)) ||
            (!string.IsNullOrWhiteSpace(request.SpecialisationId) &&
             profile.AllowedSpecialisationIds.Count > 0 &&
             !profile.AllowedSpecialisationIds.Contains(request.SpecialisationId, StringComparer.Ordinal)))
        {
            AddError(issues, ModelProfileInvalid, "Model profile is not executable by the PR-33 adapter boundary.", nameof(request.Profile));
        }
    }

    private static void ValidateResponseFormat(AgentModelResponseFormat format, List<AgentModelAdapterIssue> issues)
    {
        if (format is null ||
            string.IsNullOrWhiteSpace(format.FormatId) ||
            string.IsNullOrWhiteSpace(format.OutputContractName))
        {
            AddError(issues, ModelOutputFormatRequired, "Model response format and output contract are required.", nameof(AgentModelRequest.ResponseFormat));
            return;
        }

        if (!format.RequiresSchemaValidation || format.RequiredFields.Count == 0)
            AddError(issues, ModelOutputSchemaRequired, "Model response format must require schema validation and declare required fields.", nameof(AgentModelRequest.ResponseFormat));
    }

    private void ValidateMessages(AgentModelRequest request, List<AgentModelAdapterIssue> issues)
    {
        if (request.Messages.Count == 0)
        {
            AddError(issues, ModelMessagesRequired, "At least one model message is required.", nameof(request.Messages));
            return;
        }

        if (request.Messages.Count > _options.MaxMessages)
            AddError(issues, ModelContextTooLarge, "Model request message count exceeds configured boundary.", nameof(request.Messages));

        foreach (var message in request.Messages)
        {
            if (string.IsNullOrWhiteSpace(message.MessageId) ||
                string.IsNullOrWhiteSpace(message.Content) ||
                !Enum.IsDefined(typeof(AgentModelRole), message.Role))
            {
                AddError(issues, ModelMessageContentRequired, "Model messages require id, role, and content.", nameof(request.Messages));
            }

            if (message.Content?.Length > _options.MaxMessageCharacters)
                AddError(issues, ModelContextTooLarge, "Model message content exceeds configured boundary.", nameof(message.Content));

            if (message.ContainsRawPrivateReasoning || ContainsAny(message.Content, RawPrivateReasoningMarkers))
                AddError(issues, ModelRequestRawReasoningBlocked, "Model request messages cannot contain raw/private reasoning.", nameof(message.Content));

            if (message.ContainsSystemPromptLeak || message.ContainsDeveloperPromptLeak)
                AddError(issues, ModelRequestPromptLeakBlocked, "Model request messages cannot leak reusable system or developer prompts.", nameof(message.Content));

            if (message.IsAuthoritativeForAction || ContainsAny(message.Content, AuthorityClaimMarkers))
                AddError(issues, ModelRequestAuthorityBlocked, "Model request messages cannot be authoritative for action.", nameof(message.Content));

            if (ContainsAny(message.Content, ToolCommandMarkers))
                AddError(issues, ModelRequestToolBlocked, "Model request messages cannot request tool execution.", nameof(message.Content));

            if (ContainsAny(message.Content, SourceMutationMarkers))
                AddError(issues, ModelRequestSourceMutationBlocked, "Model request messages cannot request source mutation.", nameof(message.Content));

            if (ContainsAny(message.Content, MemoryPromotionMarkers))
                AddError(issues, ModelRequestMemoryPromotionBlocked, "Model request messages cannot request memory promotion.", nameof(message.Content));
        }
    }

    private void ValidateContext(AgentModelRequest request, List<AgentModelAdapterIssue> issues)
    {
        var context = request.Context;
        var refCount = context.InputRefs.Count + context.EvidenceRefs.Count + context.MemoryRefs.Count + context.AuditRefs.Count;
        if (refCount > _options.MaxContextRefs)
            AddError(issues, ModelContextTooLarge, "Model request context references exceed configured boundary.", nameof(request.Context));
    }

    private static void ValidateRequestSafetyFlags(AgentModelSafetyFlags flags, List<AgentModelAdapterIssue> issues)
    {
        if (flags.MayGrantApproval || flags.MayGrantPolicyApproval || flags.MayRepresentHumanApproval)
            AddError(issues, ModelRequestAuthorityBlocked, "Model request cannot grant or represent approval authority.", nameof(AgentModelRequest.SafetyFlags));

        if (flags.MayRunTools)
            AddError(issues, ModelRequestToolBlocked, "Model request cannot allow tool execution.", nameof(AgentModelRequest.SafetyFlags));

        if (flags.MayMutateSource)
            AddError(issues, ModelRequestSourceMutationBlocked, "Model request cannot allow source mutation.", nameof(AgentModelRequest.SafetyFlags));

        if (flags.MayCallExternalSystems || flags.MaySubmitGitHubReview)
            AddError(issues, ModelRequestExternalCallBlocked, "Model request cannot allow external calls or GitHub review submission.", nameof(AgentModelRequest.SafetyFlags));

        if (flags.MayPromoteMemory)
            AddError(issues, ModelRequestMemoryPromotionBlocked, "Model request cannot allow memory promotion.", nameof(AgentModelRequest.SafetyFlags));

        if (flags.MayCreateCollectiveMemory)
            AddError(issues, ModelRequestCollectiveMemoryBlocked, "Model request cannot allow CollectiveMemory creation.", nameof(AgentModelRequest.SafetyFlags));

        if (flags.MayPersistProposal)
            AddError(issues, ModelRequestProposalPersistenceBlocked, "Model request cannot allow proposal persistence.", nameof(AgentModelRequest.SafetyFlags));
    }

    private void ValidateSpecialisationCompatibility(AgentModelRequest request, List<AgentModelAdapterIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(request.SpecialisationId))
            return;

        var specialisation = _specialisations.FirstOrDefault(profile =>
            string.Equals(profile.SpecialisationId, request.SpecialisationId, StringComparison.Ordinal));
        var agentDefinition = _agentDefinitions.FirstOrDefault(definition =>
            string.Equals(definition.AgentId, request.AgentId, StringComparison.Ordinal));

        if (specialisation is null || agentDefinition is null)
        {
            AddError(issues, ModelSpecialisationIncompatible, "Model request specialisation is unknown or agent definition is missing.", nameof(request.SpecialisationId));
            return;
        }

        var compatibility = _specialisationValidator.ValidateCompatibility(agentDefinition, specialisation);
        if (!compatibility.IsCompatible)
        {
            AddError(issues, ModelSpecialisationIncompatible, "Model request specialisation is not compatible with the agent definition.", nameof(request.SpecialisationId));
        }
    }

    private static bool AnySafetyFlag(AgentModelSafetyFlags flags) =>
        flags.MayGrantApproval ||
        flags.MayGrantPolicyApproval ||
        flags.MayRepresentHumanApproval ||
        flags.MayPromoteMemory ||
        flags.MayCreateCollectiveMemory ||
        flags.MayRunTools ||
        flags.MayMutateSource ||
        flags.MayCallExternalSystems ||
        flags.MaySubmitGitHubReview ||
        flags.MayPersistProposal;

    private static bool ContainsAny(string? value, IEnumerable<string> markers) =>
        !string.IsNullOrWhiteSpace(value) && markers.Any(marker => ContainsMarker(value, marker));

    private static bool ContainsMarker(string value, string marker)
    {
        if (marker.Any(char.IsWhiteSpace) || marker.Contains('-', StringComparison.Ordinal))
            return value.Contains(marker, StringComparison.OrdinalIgnoreCase);

        var startIndex = 0;
        while (startIndex < value.Length)
        {
            var index = value.IndexOf(marker, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            var beforeIsBoundary = index == 0 || !char.IsLetterOrDigit(value[index - 1]);
            var afterIndex = index + marker.Length;
            var afterIsBoundary = afterIndex >= value.Length || !char.IsLetterOrDigit(value[afterIndex]);

            if (beforeIsBoundary && afterIsBoundary)
                return true;

            startIndex = index + marker.Length;
        }

        return false;
    }

    private static void AddError(
        ICollection<AgentModelAdapterIssue> issues,
        string code,
        string message,
        string? field = null) =>
        issues.Add(new AgentModelAdapterIssue
        {
            Code = code,
            Severity = "error",
            Message = message,
            Field = field
        });
}

public class ScriptedAgentModelAdapter : IAgentModelAdapter
{
    private readonly AgentModelAdapterValidator _validator;
    private readonly Func<AgentModelRequest, DateTimeOffset, AgentModelResponse> _responseFactory;

    public ScriptedAgentModelAdapter(
        Func<AgentModelRequest, DateTimeOffset, AgentModelResponse> responseFactory,
        AgentModelAdapterValidator? validator = null)
    {
        _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        _validator = validator ?? new AgentModelAdapterValidator();
    }

    public AgentModelAdapterResult Invoke(AgentModelRequest request, DateTimeOffset invokedAtUtc)
    {
        var requestIssues = _validator.ValidateRequest(request);
        if (requestIssues.Count > 0)
        {
            return Failed(requestIssues);
        }

        var response = _responseFactory(request, invokedAtUtc);
        var responseIssues = _validator.ValidateResponse(request, response);
        var audit = BuildAudit(request, response, invokedAtUtc, responseIssues.Count == 0);
        var auditIssues = _validator.ValidateAudit(audit);
        var issues = responseIssues.Concat(auditIssues).ToArray();

        if (issues.Length > 0)
        {
            return new AgentModelAdapterResult
            {
                Succeeded = false,
                Response = response,
                Audit = audit,
                Issues = issues
            };
        }

        return new AgentModelAdapterResult
        {
            Succeeded = true,
            Response = response,
            Audit = audit,
            Issues = []
        };
    }

    protected static AgentModelResponse BuildSafeResponse(
        AgentModelRequest request,
        DateTimeOffset completedAtUtc,
        string content = "Fake model response candidate. Human review and governance remain separate.") =>
        new()
        {
            ResponseId = $"model-response-{request.RequestId}",
            RequestId = request.RequestId,
            AgentId = request.AgentId,
            SpecialisationId = request.SpecialisationId,
            ProviderKind = request.Profile.ProviderKind,
            ModelName = request.Profile.ModelName,
            Content = content,
            StructuredJson = request.ResponseFormat.RequiresJson ? "{}" : null,
            Usage = new AgentModelUsage
            {
                InputTokens = EstimateTokens(request.Messages.Sum(message => message.Content?.Length ?? 0)),
                OutputTokens = EstimateTokens(content.Length)
            },
            CompletedAtUtc = completedAtUtc
        };

    protected static AgentModelInvocationAudit BuildAudit(
        AgentModelRequest request,
        AgentModelResponse response,
        DateTimeOffset invokedAtUtc,
        bool succeeded) =>
        new()
        {
            AuditId = $"model-audit-{request.RequestId}",
            RequestId = request.RequestId,
            AgentId = request.AgentId,
            SpecialisationId = request.SpecialisationId,
            ProfileId = request.Profile.ProfileId,
            ProviderKind = request.Profile.ProviderKind,
            ModelName = request.Profile.ModelName,
            RequestedAtUtc = invokedAtUtc,
            CompletedAtUtc = response.CompletedAtUtc,
            Succeeded = succeeded,
            InputRefs = request.Context.InputRefs,
            EvidenceRefs = request.Context.EvidenceRefs.Concat(request.Messages.SelectMany(message => message.EvidenceRefs)).Distinct(StringComparer.Ordinal).ToArray(),
            Usage = response.Usage,
            ContainsRawPrivateReasoning = response.ContainsRawPrivateReasoning,
            ContainsPromptLeak = response.ContainsSystemPromptLeak || response.ContainsDeveloperPromptLeak,
            ContainsAuthorityClaim = response.ContainsAuthorityClaim,
            ContainsToolCommand = response.ContainsToolCommand,
            ContainsSourceMutationCommand = response.ContainsSourceMutationCommand,
            ContainsMemoryPromotionCommand = response.ContainsMemoryPromotionCommand,
            GrantsApproval = false,
            GrantsAuthority = false,
            GrantsMemoryPromotion = false
        };

    private static AgentModelAdapterResult Failed(IReadOnlyList<AgentModelAdapterIssue> issues) =>
        new()
        {
            Succeeded = false,
            Issues = issues
        };

    private static int EstimateTokens(int characters) => Math.Max(0, (characters + 3) / 4);
}

public sealed class FakeAgentModelAdapter : ScriptedAgentModelAdapter
{
    public FakeAgentModelAdapter(AgentModelAdapterValidator? validator = null)
        : base((request, invokedAtUtc) => BuildSafeResponse(request, invokedAtUtc), validator)
    {
    }
}
