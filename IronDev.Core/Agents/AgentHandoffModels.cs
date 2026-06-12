using System.Text.Json;

namespace IronDev.Core.Agents;

public enum AgentHandoffStatus
{
    Draft = 1,
    ReadyForReview = 2,
    Offered = 3,
    Received = 4,
    Rejected = 5,
    Cancelled = 6,
    Expired = 7,
    Superseded = 8
}

public enum AgentHandoffType
{
    TaskContext = 1,
    ReviewRequest = 2,
    EvidenceTransfer = 3,
    RequirementTransfer = 4,
    DebugContext = 5,
    ImplementationContext = 6,
    ValidationContext = 7,
    MemoryCandidateContext = 8,
    SourceApplyContext = 9,
    ReleaseEvidenceContext = 10
}

public enum AgentHandoffParticipantRole
{
    Planner = 1,
    Builder = 2,
    Critic = 3,
    Tester = 4,
    Memory = 5,
    Conscience = 6,
    Reviewer = 7,
    Operator = 8,
    ToolGateway = 9,
    Unknown = 10
}

public enum AgentHandoffSubjectType
{
    ToolRequest = 1,
    ApprovalPackage = 2,
    PolicyRequirement = 3,
    DogfoodReceipt = 4,
    ValidationRun = 5,
    RunReport = 6,
    CodePatchCandidate = 7,
    MemoryCandidate = 8,
    DebugSession = 9,
    WorkflowStepCandidate = 10
}

public enum AgentHandoffEvidenceType
{
    GovernanceEvent = 1,
    ToolRequest = 2,
    ToolGateDecision = 3,
    ApprovalRequirementEvaluation = 4,
    ApprovalPackage = 5,
    ApprovalDecision = 6,
    PolicyDecisionEvent = 7,
    DogfoodReceipt = 8,
    ThoughtLedgerReference = 9,
    ValidationOutput = 10,
    RunReport = 11,
    HumanNote = 12,
    CriticReview = 13,
    CodeStandardsReview = 14
}

public enum AgentHandoffConstraintType
{
    RequiresHumanReview = 1,
    RequiresApprovalDecision = 2,
    RequiresPolicyEvaluation = 3,
    RequiresValidation = 4,
    RequiresDogfoodReceipt = 5,
    RequiresSourceApplyApproval = 6,
    RequiresMemoryPromotionApproval = 7,
    EvidenceOnly = 8,
    DoNotExecute = 9,
    DoNotMutateSource = 10,
    DoNotPromoteMemory = 11,
    DoNotContinueWorkflow = 12
}

public sealed record AgentHandoff
{
    public required Guid AgentHandoffId { get; init; }
    public required Guid ProjectId { get; init; }
    public required AgentHandoffType HandoffType { get; init; }
    public required AgentHandoffStatus Status { get; init; }
    public required AgentHandoffParticipant SourceAgent { get; init; }
    public required AgentHandoffParticipant TargetAgent { get; init; }
    public required AgentHandoffSubject Subject { get; init; }
    public required IReadOnlyList<AgentHandoffEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<AgentHandoffConstraint> Constraints { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public Guid? SupersedesHandoffId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public required bool GrantsApproval { get; init; }
    public required bool GrantsExecution { get; init; }
    public required bool MutatesSource { get; init; }
    public required bool PromotesMemory { get; init; }
    public required bool StartsWorkflow { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransfersAuthority { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record AgentHandoffParticipant
{
    public required string AgentId { get; init; }
    public required AgentHandoffParticipantRole AgentRole { get; init; }
    public string? DisplayName { get; init; }
}

public sealed record AgentHandoffSubject
{
    public required AgentHandoffSubjectType SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public string? ActionName { get; init; }
    public string? Summary { get; init; }
}

public sealed record AgentHandoffEvidenceReference
{
    public required AgentHandoffEvidenceType EvidenceType { get; init; }
    public required string EvidenceId { get; init; }
    public string? EvidenceLabel { get; init; }
    public string? EvidenceSummary { get; init; }
    public Guid? GovernanceEventId { get; init; }
}

public sealed record AgentHandoffConstraint
{
    public required AgentHandoffConstraintType ConstraintType { get; init; }
    public required string ConstraintCode { get; init; }
    public required string Description { get; init; }
}

public sealed record AgentHandoffCreateRequest
{
    public required Guid ProjectId { get; init; }
    public required AgentHandoffType HandoffType { get; init; }
    public required AgentHandoffStatus Status { get; init; }
    public required AgentHandoffParticipant SourceAgent { get; init; }
    public required AgentHandoffParticipant TargetAgent { get; init; }
    public required AgentHandoffSubject Subject { get; init; }
    public required IReadOnlyList<AgentHandoffEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<AgentHandoffConstraint> Constraints { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public Guid? SupersedesHandoffId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
}

public sealed record AgentHandoffSummary
{
    public required Guid AgentHandoffId { get; init; }
    public required Guid ProjectId { get; init; }
    public required AgentHandoffType HandoffType { get; init; }
    public required AgentHandoffStatus Status { get; init; }
    public required string SourceAgentId { get; init; }
    public required string TargetAgentId { get; init; }
    public required AgentHandoffSubjectType SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required int EvidenceReferenceCount { get; init; }
    public required int ConstraintCount { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record AgentHandoffValidationResult
{
    public required bool IsValid { get; init; }
    public IReadOnlyList<AgentHandoffValidationIssue> Issues { get; init; } = [];
}

public sealed record AgentHandoffValidationIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public sealed class AgentHandoffValidator
{
    public const string HandoffRequired = "AGENT_HANDOFF_REQUIRED";
    public const string HandoffIdRequired = "AGENT_HANDOFF_ID_REQUIRED";
    public const string ProjectIdRequired = "AGENT_HANDOFF_PROJECT_ID_REQUIRED";
    public const string HandoffTypeInvalid = "AGENT_HANDOFF_TYPE_INVALID";
    public const string HandoffStatusInvalid = "AGENT_HANDOFF_STATUS_INVALID";
    public const string ParticipantRequired = "AGENT_HANDOFF_PARTICIPANT_REQUIRED";
    public const string ParticipantInvalid = "AGENT_HANDOFF_PARTICIPANT_INVALID";
    public const string ParticipantRoleInvalid = "AGENT_HANDOFF_PARTICIPANT_ROLE_INVALID";
    public const string SameParticipantBlocked = "AGENT_HANDOFF_SAME_PARTICIPANT_BLOCKED";
    public const string SubjectRequired = "AGENT_HANDOFF_SUBJECT_REQUIRED";
    public const string SubjectInvalid = "AGENT_HANDOFF_SUBJECT_INVALID";
    public const string EvidenceRequired = "AGENT_HANDOFF_EVIDENCE_REQUIRED";
    public const string EvidenceInvalid = "AGENT_HANDOFF_EVIDENCE_INVALID";
    public const string EvidenceTypeInvalid = "AGENT_HANDOFF_EVIDENCE_TYPE_INVALID";
    public const string ConstraintRequired = "AGENT_HANDOFF_CONSTRAINT_REQUIRED";
    public const string ConstraintInvalid = "AGENT_HANDOFF_CONSTRAINT_INVALID";
    public const string ConstraintTypeInvalid = "AGENT_HANDOFF_CONSTRAINT_TYPE_INVALID";
    public const string ActorRequired = "AGENT_HANDOFF_ACTOR_REQUIRED";
    public const string MetadataVersionInvalid = "AGENT_HANDOFF_METADATA_VERSION_INVALID";
    public const string MetadataJsonRequired = "AGENT_HANDOFF_METADATA_JSON_REQUIRED";
    public const string MetadataJsonInvalid = "AGENT_HANDOFF_METADATA_JSON_INVALID";
    public const string PrivateReasoningBlocked = "AGENT_HANDOFF_PRIVATE_REASONING_BLOCKED";
    public const string AuthorityMetadataBlocked = "AGENT_HANDOFF_AUTHORITY_METADATA_BLOCKED";
    public const string AuthorityFlagBlocked = "AGENT_HANDOFF_AUTHORITY_FLAG_BLOCKED";

    private const int MaxMetadataJsonLength = 8192;

    private static readonly string[] PrivateReasoningMarkers =
    [
        "hiddenReasoning",
        "chainOfThought",
        "chain-of-thought",
        "private reasoning",
        "scratchpad",
        "rawPrompt",
        "rawCompletion",
        "rawToolOutput",
        "entirePatch"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "approvalTransferred",
        "approval granted",
        "canExecute",
        "execution granted",
        "sourceApplyAllowed",
        "memoryPromotionAllowed",
        "workflowContinues",
        "releaseApproved",
        "transfersAuthority",
        "authorityTransfer",
        "grantsApproval",
        "grantsExecution",
        "mutatesSource",
        "promotesMemory",
        "startsWorkflow",
        "satisfiesPolicy"
    ];

    public AgentHandoffValidationResult Validate(AgentHandoffCreateRequest request)
    {
        var issues = new List<AgentHandoffValidationIssue>();

        if (request is null)
        {
            AddError(issues, HandoffRequired, "Agent handoff create request is required.", nameof(AgentHandoffCreateRequest));
            return Result(issues);
        }

        ValidateCommon(
            request.ProjectId,
            request.HandoffType,
            request.Status,
            request.SourceAgent,
            request.TargetAgent,
            request.Subject,
            request.EvidenceReferences,
            request.Constraints,
            request.CreatedByActorType,
            request.CreatedByActorId,
            request.MetadataVersion,
            request.MetadataJson,
            issues);

        return Result(issues);
    }

    public AgentHandoffValidationResult Validate(AgentHandoff handoff)
    {
        var issues = new List<AgentHandoffValidationIssue>();

        if (handoff is null)
        {
            AddError(issues, HandoffRequired, "Agent handoff is required.", nameof(AgentHandoff));
            return Result(issues);
        }

        if (handoff.AgentHandoffId == Guid.Empty)
            AddError(issues, HandoffIdRequired, "AgentHandoffId is required.", nameof(AgentHandoff.AgentHandoffId));

        ValidateCommon(
            handoff.ProjectId,
            handoff.HandoffType,
            handoff.Status,
            handoff.SourceAgent,
            handoff.TargetAgent,
            handoff.Subject,
            handoff.EvidenceReferences,
            handoff.Constraints,
            handoff.CreatedByActorType,
            handoff.CreatedByActorId,
            handoff.MetadataVersion,
            handoff.MetadataJson,
            issues);

        ValidateAuthorityFlags(handoff, issues);

        return Result(issues);
    }

    private static void ValidateCommon(
        Guid projectId,
        AgentHandoffType handoffType,
        AgentHandoffStatus status,
        AgentHandoffParticipant sourceAgent,
        AgentHandoffParticipant targetAgent,
        AgentHandoffSubject subject,
        IReadOnlyList<AgentHandoffEvidenceReference> evidenceReferences,
        IReadOnlyList<AgentHandoffConstraint> constraints,
        string createdByActorType,
        string createdByActorId,
        int metadataVersion,
        string metadataJson,
        List<AgentHandoffValidationIssue> issues)
    {
        if (projectId == Guid.Empty)
            AddError(issues, ProjectIdRequired, "ProjectId is required.", nameof(AgentHandoffCreateRequest.ProjectId));

        if (!Enum.IsDefined(handoffType))
            AddError(issues, HandoffTypeInvalid, "HandoffType is invalid.", nameof(AgentHandoffCreateRequest.HandoffType));

        if (!Enum.IsDefined(status))
            AddError(issues, HandoffStatusInvalid, "Status is invalid.", nameof(AgentHandoffCreateRequest.Status));

        ValidateParticipant(sourceAgent, nameof(AgentHandoffCreateRequest.SourceAgent), issues);
        ValidateParticipant(targetAgent, nameof(AgentHandoffCreateRequest.TargetAgent), issues);

        if (sourceAgent is not null &&
            targetAgent is not null &&
            !string.IsNullOrWhiteSpace(sourceAgent.AgentId) &&
            string.Equals(sourceAgent.AgentId, targetAgent.AgentId, StringComparison.OrdinalIgnoreCase))
        {
            AddError(issues, SameParticipantBlocked, "Source and target agents must be different.", nameof(AgentHandoffCreateRequest.TargetAgent));
        }

        ValidateSubject(subject, issues);
        ValidateEvidence(evidenceReferences, issues);
        ValidateConstraints(constraints, issues);

        if (string.IsNullOrWhiteSpace(createdByActorType))
            AddError(issues, ActorRequired, "CreatedByActorType is required.", nameof(AgentHandoffCreateRequest.CreatedByActorType));

        if (string.IsNullOrWhiteSpace(createdByActorId))
            AddError(issues, ActorRequired, "CreatedByActorId is required.", nameof(AgentHandoffCreateRequest.CreatedByActorId));

        if (metadataVersion <= 0)
            AddError(issues, MetadataVersionInvalid, "MetadataVersion must be positive.", nameof(AgentHandoffCreateRequest.MetadataVersion));

        ValidateMetadataJson(metadataJson, issues);
    }

    private static void ValidateParticipant(AgentHandoffParticipant participant, string field, List<AgentHandoffValidationIssue> issues)
    {
        if (participant is null)
        {
            AddError(issues, ParticipantRequired, "Agent handoff participant is required.", field);
            return;
        }

        if (string.IsNullOrWhiteSpace(participant.AgentId))
            AddError(issues, ParticipantInvalid, "AgentId is required.", nameof(AgentHandoffParticipant.AgentId));

        if (!Enum.IsDefined(participant.AgentRole))
            AddError(issues, ParticipantRoleInvalid, "AgentRole is invalid.", nameof(AgentHandoffParticipant.AgentRole));

        ValidateTextSafety(participant.AgentId, nameof(AgentHandoffParticipant.AgentId), issues);
        ValidateTextSafety(participant.DisplayName, nameof(AgentHandoffParticipant.DisplayName), issues);
    }

    private static void ValidateSubject(AgentHandoffSubject subject, List<AgentHandoffValidationIssue> issues)
    {
        if (subject is null)
        {
            AddError(issues, SubjectRequired, "Subject is required.", nameof(AgentHandoffCreateRequest.Subject));
            return;
        }

        if (!Enum.IsDefined(subject.SubjectType))
            AddError(issues, SubjectInvalid, "SubjectType is invalid.", nameof(AgentHandoffSubject.SubjectType));

        if (string.IsNullOrWhiteSpace(subject.SubjectId))
            AddError(issues, SubjectInvalid, "SubjectId is required.", nameof(AgentHandoffSubject.SubjectId));

        ValidateTextSafety(subject.SubjectId, nameof(AgentHandoffSubject.SubjectId), issues);
        ValidateTextSafety(subject.ActionName, nameof(AgentHandoffSubject.ActionName), issues);
        ValidateTextSafety(subject.Summary, nameof(AgentHandoffSubject.Summary), issues);
    }

    private static void ValidateEvidence(IReadOnlyList<AgentHandoffEvidenceReference> evidenceReferences, List<AgentHandoffValidationIssue> issues)
    {
        if (evidenceReferences is null || evidenceReferences.Count == 0)
        {
            AddError(issues, EvidenceRequired, "At least one evidence reference is required.", nameof(AgentHandoffCreateRequest.EvidenceReferences));
            return;
        }

        foreach (var evidence in evidenceReferences)
        {
            if (evidence is null)
            {
                AddError(issues, EvidenceInvalid, "Evidence reference cannot be null.", nameof(AgentHandoffCreateRequest.EvidenceReferences));
                continue;
            }

            if (!Enum.IsDefined(evidence.EvidenceType))
                AddError(issues, EvidenceTypeInvalid, "EvidenceType is invalid.", nameof(AgentHandoffEvidenceReference.EvidenceType));

            if (string.IsNullOrWhiteSpace(evidence.EvidenceId))
                AddError(issues, EvidenceInvalid, "EvidenceId is required.", nameof(AgentHandoffEvidenceReference.EvidenceId));

            ValidateTextSafety(evidence.EvidenceId, nameof(AgentHandoffEvidenceReference.EvidenceId), issues);
            ValidateTextSafety(evidence.EvidenceLabel, nameof(AgentHandoffEvidenceReference.EvidenceLabel), issues);
            ValidateTextSafety(evidence.EvidenceSummary, nameof(AgentHandoffEvidenceReference.EvidenceSummary), issues);
        }
    }

    private static void ValidateConstraints(IReadOnlyList<AgentHandoffConstraint> constraints, List<AgentHandoffValidationIssue> issues)
    {
        if (constraints is null || constraints.Count == 0)
        {
            AddError(issues, ConstraintRequired, "At least one constraint is required.", nameof(AgentHandoffCreateRequest.Constraints));
            return;
        }

        foreach (var constraint in constraints)
        {
            if (constraint is null)
            {
                AddError(issues, ConstraintInvalid, "Constraint cannot be null.", nameof(AgentHandoffCreateRequest.Constraints));
                continue;
            }

            if (!Enum.IsDefined(constraint.ConstraintType))
                AddError(issues, ConstraintTypeInvalid, "ConstraintType is invalid.", nameof(AgentHandoffConstraint.ConstraintType));

            if (string.IsNullOrWhiteSpace(constraint.ConstraintCode))
                AddError(issues, ConstraintInvalid, "ConstraintCode is required.", nameof(AgentHandoffConstraint.ConstraintCode));

            if (string.IsNullOrWhiteSpace(constraint.Description))
                AddError(issues, ConstraintInvalid, "Description is required.", nameof(AgentHandoffConstraint.Description));

            ValidateTextSafety(constraint.ConstraintCode, nameof(AgentHandoffConstraint.ConstraintCode), issues);
            ValidateTextSafety(constraint.Description, nameof(AgentHandoffConstraint.Description), issues);
        }
    }

    private static void ValidateMetadataJson(string metadataJson, List<AgentHandoffValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            AddError(issues, MetadataJsonRequired, "MetadataJson is required.", nameof(AgentHandoffCreateRequest.MetadataJson));
            return;
        }

        if (metadataJson.Length > MaxMetadataJsonLength)
            AddError(issues, MetadataJsonInvalid, "MetadataJson is too large.", nameof(AgentHandoffCreateRequest.MetadataJson));

        ValidatePrivateReasoningText(metadataJson, nameof(AgentHandoffCreateRequest.MetadataJson), issues);

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            ValidateMetadataElement(document.RootElement, issues);
        }
        catch (JsonException)
        {
            AddError(issues, MetadataJsonInvalid, "MetadataJson must be valid JSON.", nameof(AgentHandoffCreateRequest.MetadataJson));
        }
    }

    private static void ValidateMetadataElement(JsonElement element, List<AgentHandoffValidationIssue> issues)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    ValidatePrivateReasoningText(property.Name, nameof(AgentHandoffCreateRequest.MetadataJson), issues);
                    if (IsAuthorityMarker(property.Name) && IsTruthyAuthorityValue(property.Value))
                    {
                        AddError(
                            issues,
                            AuthorityMetadataBlocked,
                            $"Metadata property cannot grant authority: {property.Name}.",
                            nameof(AgentHandoffCreateRequest.MetadataJson));
                    }

                    ValidateMetadataElement(property.Value, issues);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    ValidateMetadataElement(item, issues);
                break;

            case JsonValueKind.String:
                ValidateTextSafety(element.GetString(), nameof(AgentHandoffCreateRequest.MetadataJson), issues);
                if (ContainsAny(element.GetString(), AuthorityMarkers))
                {
                    AddError(
                        issues,
                        AuthorityMetadataBlocked,
                        "Metadata string cannot claim authority.",
                        nameof(AgentHandoffCreateRequest.MetadataJson));
                }
                break;
        }
    }

    private static bool IsTruthyAuthorityValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => false,
            JsonValueKind.String => !string.Equals(value.GetString(), "false", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(value.GetString(), "no", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(value.GetString(), "0", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => value.TryGetInt32(out var number) && number != 0,
            _ => true
        };

    private static bool IsAuthorityMarker(string text) => ContainsAny(text, AuthorityMarkers);

    private static void ValidateTextSafety(string? text, string field, List<AgentHandoffValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        ValidatePrivateReasoningText(text, field, issues);

        if (ContainsAny(text, AuthorityMarkers))
            AddError(issues, AuthorityMetadataBlocked, "Handoff text cannot claim transferred authority.", field);
    }

    private static void ValidatePrivateReasoningText(string? text, string field, List<AgentHandoffValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (ContainsAny(text, PrivateReasoningMarkers))
            AddError(issues, PrivateReasoningBlocked, "Handoff cannot contain hidden/private reasoning markers.", field);
    }

    private static void ValidateAuthorityFlags(AgentHandoff handoff, List<AgentHandoffValidationIssue> issues)
    {
        if (handoff.GrantsApproval)
            AddError(issues, AuthorityFlagBlocked, "Handoff cannot grant approval.", nameof(AgentHandoff.GrantsApproval));

        if (handoff.GrantsExecution)
            AddError(issues, AuthorityFlagBlocked, "Handoff cannot grant execution.", nameof(AgentHandoff.GrantsExecution));

        if (handoff.MutatesSource)
            AddError(issues, AuthorityFlagBlocked, "Handoff cannot mutate source.", nameof(AgentHandoff.MutatesSource));

        if (handoff.PromotesMemory)
            AddError(issues, AuthorityFlagBlocked, "Handoff cannot promote memory.", nameof(AgentHandoff.PromotesMemory));

        if (handoff.StartsWorkflow)
            AddError(issues, AuthorityFlagBlocked, "Handoff cannot start workflow.", nameof(AgentHandoff.StartsWorkflow));

        if (handoff.SatisfiesPolicy)
            AddError(issues, AuthorityFlagBlocked, "Handoff cannot satisfy policy.", nameof(AgentHandoff.SatisfiesPolicy));

        if (handoff.TransfersAuthority)
            AddError(issues, AuthorityFlagBlocked, "Handoff cannot transfer authority.", nameof(AgentHandoff.TransfersAuthority));
    }

    private static bool ContainsAny(string? text, IEnumerable<string> markers) =>
        !string.IsNullOrWhiteSpace(text) &&
        markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static AgentHandoffValidationResult Result(List<AgentHandoffValidationIssue> issues) =>
        new()
        {
            IsValid = issues.All(issue => !string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            Issues = issues
        };

    private static void AddError(List<AgentHandoffValidationIssue> issues, string code, string message, string field) =>
        issues.Add(new AgentHandoffValidationIssue
        {
            Code = code,
            Severity = "error",
            Message = message,
            Field = field
        });
}
