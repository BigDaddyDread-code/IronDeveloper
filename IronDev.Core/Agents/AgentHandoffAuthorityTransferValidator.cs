using System.Text.Json;

namespace IronDev.Core.Agents;

public interface IAgentHandoffAuthorityTransferValidator
{
    AgentHandoffAuthorityTransferValidationResult Validate(AgentHandoff handoff);
}

public sealed record AgentHandoffAuthorityTransferValidationResult
{
    public required bool IsSafe { get; init; }
    public required IReadOnlyList<AgentHandoffAuthorityTransferViolation> Violations { get; init; }
    public required bool GrantsApproval { get; init; }
    public required bool GrantsExecution { get; init; }
    public required bool MutatesSource { get; init; }
    public required bool PromotesMemory { get; init; }
    public required bool StartsWorkflow { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransfersAuthority { get; init; }
}

public sealed record AgentHandoffAuthorityTransferViolation
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Path { get; init; }
}

public sealed class AgentHandoffAuthorityTransferValidator : IAgentHandoffAuthorityTransferValidator
{
    public const string HandoffRequired = "HandoffRequired";
    public const string AuthorityFlagSet = "AuthorityFlagSet";
    public const string ForbiddenHandoffType = "ForbiddenHandoffType";
    public const string ForbiddenHandoffStatus = "ForbiddenHandoffStatus";
    public const string ForbiddenAgentRole = "ForbiddenAgentRole";
    public const string ForbiddenSubjectMeaning = "ForbiddenSubjectMeaning";
    public const string ForbiddenEvidenceType = "ForbiddenEvidenceType";
    public const string ForbiddenEvidenceAllowedUse = "ForbiddenEvidenceAllowedUse";
    public const string ForbiddenConstraintType = "ForbiddenConstraintType";
    public const string AuthorityGrantingMetadata = "AuthorityGrantingMetadata";
    public const string AuthorityGrantingText = "AuthorityGrantingText";
    public const string PrivateReasoningMarker = "PrivateReasoningMarker";

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
        "approval transferred",
        "approval_transferred",
        "approvalTransferred",
        "approval transfer",
        "approval_transfer",
        "approvalTransfer",
        "approval granted",
        "approval_granted",
        "approvalGranted",
        "approved",
        "authorized",
        "execution allowed",
        "execution_allowed",
        "executionAllowed",
        "can execute",
        "can_execute",
        "permission granted",
        "permission_granted",
        "policy satisfied",
        "policy_satisfied",
        "policy satisfaction",
        "policy_satisfaction",
        "satisfy policy",
        "satisfy_policy",
        "workflow continued",
        "workflow_continued",
        "continue workflow",
        "continue_workflow",
        "source apply allowed",
        "source_apply_allowed",
        "source apply permission",
        "source_apply_permission",
        "memory promotion allowed",
        "memory_promotion_allowed",
        "memory promotion permission",
        "memory_promotion_permission",
        "memory ownership transferred",
        "memory_ownership_transferred",
        "release approved",
        "release_approved",
        "release approval",
        "release_approval",
        "can ship",
        "can_ship",
        "gate approved",
        "gate_approved",
        "dogfood approved",
        "dogfood_approved",
        "critic approved",
        "critic_approved",
        "model approved",
        "model_approved",
        "retrieval approved",
        "retrieval_approved",
        "authority transferred",
        "authority_transferred",
        "authority transfer",
        "authority_transfer",
        "sourceApplyAllowed",
        "memoryPromotionAllowed",
        "workflowContinues",
        "releaseApproved",
        "transfersAuthority",
        "authorityTransfer"
    ];

    private static readonly string[] AuthorityPropertyNames =
    [
        "approvalTransferred",
        "approvalTransfer",
        "canExecute",
        "executionAllowed",
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

    public AgentHandoffAuthorityTransferValidationResult Validate(AgentHandoff handoff)
    {
        var violations = new List<AgentHandoffAuthorityTransferViolation>();

        if (handoff is null)
        {
            Add(violations, HandoffRequired, "Handoff is required.", nameof(AgentHandoff));
            return Result(violations);
        }

        ValidateAuthorityFlags(handoff, violations);
        ValidateEnum(handoff.HandoffType, ForbiddenHandoffType, nameof(AgentHandoff.HandoffType), violations);
        ValidateEnum(handoff.Status, ForbiddenHandoffStatus, nameof(AgentHandoff.Status), violations);
        ValidateParticipant(handoff.SourceAgent, nameof(AgentHandoff.SourceAgent), violations);
        ValidateParticipant(handoff.TargetAgent, nameof(AgentHandoff.TargetAgent), violations);
        ValidateSubject(handoff.Subject, violations);
        ValidateEvidence(handoff.EvidenceReferences, violations);
        ValidateConstraints(handoff.Constraints, violations);
        ValidateMetadataJson(handoff.MetadataJson, violations);
        ScanText(handoff.CreatedByActorType, nameof(AgentHandoff.CreatedByActorType), violations);
        ScanText(handoff.CreatedByActorId, nameof(AgentHandoff.CreatedByActorId), violations);

        return Result(violations);
    }

    private static void ValidateAuthorityFlags(AgentHandoff handoff, List<AgentHandoffAuthorityTransferViolation> violations)
    {
        if (handoff.GrantsApproval)
            Add(violations, AuthorityFlagSet, "Handoff cannot grant approval.", nameof(AgentHandoff.GrantsApproval));

        if (handoff.GrantsExecution)
            Add(violations, AuthorityFlagSet, "Handoff cannot grant execution.", nameof(AgentHandoff.GrantsExecution));

        if (handoff.MutatesSource)
            Add(violations, AuthorityFlagSet, "Handoff cannot mutate source.", nameof(AgentHandoff.MutatesSource));

        if (handoff.PromotesMemory)
            Add(violations, AuthorityFlagSet, "Handoff cannot promote memory.", nameof(AgentHandoff.PromotesMemory));

        if (handoff.StartsWorkflow)
            Add(violations, AuthorityFlagSet, "Handoff cannot start workflow.", nameof(AgentHandoff.StartsWorkflow));

        if (handoff.SatisfiesPolicy)
            Add(violations, AuthorityFlagSet, "Handoff cannot satisfy policy.", nameof(AgentHandoff.SatisfiesPolicy));

        if (handoff.TransfersAuthority)
            Add(violations, AuthorityFlagSet, "Handoff cannot transfer authority.", nameof(AgentHandoff.TransfersAuthority));
    }

    private static void ValidateParticipant(AgentHandoffParticipant participant, string path, List<AgentHandoffAuthorityTransferViolation> violations)
    {
        if (participant is null)
            return;

        ValidateEnum(participant.AgentRole, ForbiddenAgentRole, $"{path}.{nameof(AgentHandoffParticipant.AgentRole)}", violations);
        ScanText(participant.AgentId, $"{path}.{nameof(AgentHandoffParticipant.AgentId)}", violations);
        ScanText(participant.DisplayName, $"{path}.{nameof(AgentHandoffParticipant.DisplayName)}", violations);
    }

    private static void ValidateSubject(AgentHandoffSubject subject, List<AgentHandoffAuthorityTransferViolation> violations)
    {
        if (subject is null)
            return;

        ValidateEnum(subject.SubjectType, ForbiddenSubjectMeaning, nameof(AgentHandoffSubject.SubjectType), violations);
        ScanText(subject.SubjectId, nameof(AgentHandoffSubject.SubjectId), violations);
        ScanText(subject.ActionName, nameof(AgentHandoffSubject.ActionName), violations);
        ScanText(subject.Summary, nameof(AgentHandoffSubject.Summary), violations);
    }

    private static void ValidateEvidence(IReadOnlyList<AgentHandoffEvidenceReference> evidenceReferences, List<AgentHandoffAuthorityTransferViolation> violations)
    {
        if (evidenceReferences is null)
            return;

        for (var index = 0; index < evidenceReferences.Count; index++)
        {
            var evidence = evidenceReferences[index];
            if (evidence is null)
                continue;

            var path = $"{nameof(AgentHandoff.EvidenceReferences)}[{index}]";
            ValidateEnum(evidence.EvidenceType, ForbiddenEvidenceType, $"{path}.{nameof(AgentHandoffEvidenceReference.EvidenceType)}", violations);
            ScanText(evidence.EvidenceId, $"{path}.{nameof(AgentHandoffEvidenceReference.EvidenceId)}", violations);
            ScanText(evidence.EvidenceLabel, $"{path}.{nameof(AgentHandoffEvidenceReference.EvidenceLabel)}", violations);
            ScanText(evidence.EvidenceSummary, $"{path}.{nameof(AgentHandoffEvidenceReference.EvidenceSummary)}", violations);

            if (evidence.AllowedUses is null)
                continue;

            for (var useIndex = 0; useIndex < evidence.AllowedUses.Count; useIndex++)
            {
                var allowedUse = evidence.AllowedUses[useIndex];
                ValidateEnum(allowedUse, ForbiddenEvidenceAllowedUse, $"{path}.{nameof(AgentHandoffEvidenceReference.AllowedUses)}[{useIndex}]", violations);
            }
        }
    }

    private static void ValidateConstraints(IReadOnlyList<AgentHandoffConstraint> constraints, List<AgentHandoffAuthorityTransferViolation> violations)
    {
        if (constraints is null)
            return;

        for (var index = 0; index < constraints.Count; index++)
        {
            var constraint = constraints[index];
            if (constraint is null)
                continue;

            var path = $"{nameof(AgentHandoff.Constraints)}[{index}]";
            ValidateEnum(constraint.ConstraintType, ForbiddenConstraintType, $"{path}.{nameof(AgentHandoffConstraint.ConstraintType)}", violations);
            ScanText(constraint.ConstraintCode, $"{path}.{nameof(AgentHandoffConstraint.ConstraintCode)}", violations);
            ScanText(constraint.Description, $"{path}.{nameof(AgentHandoffConstraint.Description)}", violations);
        }
    }

    private static void ValidateMetadataJson(string metadataJson, List<AgentHandoffAuthorityTransferViolation> violations)
    {
        ScanPrivateReasoningText(metadataJson, nameof(AgentHandoff.MetadataJson), violations);

        if (string.IsNullOrWhiteSpace(metadataJson))
            return;

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            ValidateMetadataElement(document.RootElement, violations);
        }
        catch (JsonException)
        {
            ScanText(metadataJson, nameof(AgentHandoff.MetadataJson), violations);
        }
    }

    private static void ValidateMetadataElement(JsonElement element, List<AgentHandoffAuthorityTransferViolation> violations)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    ScanPrivateReasoningText(property.Name, nameof(AgentHandoff.MetadataJson), violations);
                    if (ContainsAny(property.Name, AuthorityPropertyNames) && IsTruthyAuthorityValue(property.Value))
                        Add(violations, AuthorityGrantingMetadata, $"Metadata property cannot grant authority: {property.Name}.", nameof(AgentHandoff.MetadataJson));

                    ValidateMetadataElement(property.Value, violations);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    ValidateMetadataElement(item, violations);

                break;

            case JsonValueKind.String:
                ScanText(element.GetString(), nameof(AgentHandoff.MetadataJson), violations);
                break;
        }
    }

    private static void ValidateEnum<TEnum>(TEnum value, string code, string path, List<AgentHandoffAuthorityTransferViolation> violations)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            Add(violations, code, $"{typeof(TEnum).Name} value is not allowed by the non-authoritative handoff contract.", path);
    }

    private static void ScanText(string? text, string path, List<AgentHandoffAuthorityTransferViolation> violations)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        ScanPrivateReasoningText(text, path, violations);

        if (ContainsAny(text, AuthorityMarkers))
            Add(violations, AuthorityGrantingText, "Handoff text cannot claim transferred authority.", path);
    }

    private static void ScanPrivateReasoningText(string? text, string path, List<AgentHandoffAuthorityTransferViolation> violations)
    {
        if (ContainsAny(text, PrivateReasoningMarkers))
            Add(violations, PrivateReasoningMarker, "Handoff cannot contain hidden/private reasoning markers.", path);
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

    private static bool ContainsAny(string? text, IEnumerable<string> markers) =>
        !string.IsNullOrWhiteSpace(text) &&
        markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static AgentHandoffAuthorityTransferValidationResult Result(IReadOnlyList<AgentHandoffAuthorityTransferViolation> violations) =>
        new()
        {
            IsSafe = violations.Count == 0,
            Violations = violations,
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false
        };

    private static void Add(List<AgentHandoffAuthorityTransferViolation> violations, string code, string message, string path) =>
        violations.Add(new AgentHandoffAuthorityTransferViolation
        {
            Code = code,
            Message = message,
            Path = path
        });
}
