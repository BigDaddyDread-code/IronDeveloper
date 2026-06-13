using System.Text.Json;

namespace IronDev.Core.Agents;

public sealed record ThoughtLedgerHandoffEntry
{
    public required Guid ThoughtLedgerHandoffEntryId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid AgentHandoffId { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required string HandoffType { get; init; }
    public required string HandoffStatus { get; init; }
    public required string SourceAgentId { get; init; }
    public required string SourceAgentRole { get; init; }
    public required string TargetAgentId { get; init; }
    public required string TargetAgentRole { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public string? ActionName { get; init; }
    public required string SafeSummary { get; init; }
    public required IReadOnlyList<ThoughtLedgerHandoffEvidenceSummary> EvidenceSummaries { get; init; }
    public required IReadOnlyList<ThoughtLedgerHandoffConstraintSummary> ConstraintSummaries { get; init; }
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

public sealed record ThoughtLedgerHandoffEntryCreateRequest
{
    public Guid? ThoughtLedgerHandoffEntryId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid AgentHandoffId { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required string HandoffType { get; init; }
    public required string HandoffStatus { get; init; }
    public required string SourceAgentId { get; init; }
    public required string SourceAgentRole { get; init; }
    public required string TargetAgentId { get; init; }
    public required string TargetAgentRole { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public string? ActionName { get; init; }
    public required string SafeSummary { get; init; }
    public required IReadOnlyList<ThoughtLedgerHandoffEvidenceSummary> EvidenceSummaries { get; init; }
    public required IReadOnlyList<ThoughtLedgerHandoffConstraintSummary> ConstraintSummaries { get; init; }
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
    public DateTimeOffset? CreatedUtc { get; init; }
}

public sealed record ThoughtLedgerHandoffEntrySummary
{
    public required Guid ThoughtLedgerHandoffEntryId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid AgentHandoffId { get; init; }
    public required string SourceAgentId { get; init; }
    public required string TargetAgentId { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string SafeSummary { get; init; }
    public required int EvidenceSummaryCount { get; init; }
    public required int ConstraintSummaryCount { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ThoughtLedgerHandoffEvidenceSummary
{
    public required string EvidenceType { get; init; }
    public required string EvidenceId { get; init; }
    public required IReadOnlyList<string> AllowedUses { get; init; }
    public string? EvidenceLabel { get; init; }
    public string? SafeEvidenceSummary { get; init; }
    public Guid? GovernanceEventId { get; init; }
}

public sealed record ThoughtLedgerHandoffConstraintSummary
{
    public required string ConstraintType { get; init; }
    public required string ConstraintCode { get; init; }
    public required string SafeDescription { get; init; }
}

public sealed record ThoughtLedgerHandoffEntryValidationIssue(string Code, string Message, string Field = "");

public sealed record ThoughtLedgerHandoffEntryValidationResult(IReadOnlyList<ThoughtLedgerHandoffEntryValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public interface IThoughtLedgerHandoffEntryFactory
{
    ThoughtLedgerHandoffEntryCreateRequest CreateFromHandoff(
        AgentHandoff handoff,
        string createdByActorType,
        string createdByActorId);
}

public sealed class ThoughtLedgerHandoffEntryFactory : IThoughtLedgerHandoffEntryFactory
{
    public ThoughtLedgerHandoffEntryCreateRequest CreateFromHandoff(
        AgentHandoff handoff,
        string createdByActorType,
        string createdByActorId)
    {
        ArgumentNullException.ThrowIfNull(handoff);

        return new ThoughtLedgerHandoffEntryCreateRequest
        {
            ThoughtLedgerHandoffEntryId = Guid.NewGuid(),
            ProjectId = handoff.ProjectId,
            AgentHandoffId = handoff.AgentHandoffId,
            GovernanceEventId = handoff.EvidenceReferences.FirstOrDefault(evidence => evidence.EvidenceType == AgentHandoffEvidenceType.GovernanceEvent)?.GovernanceEventId,
            CorrelationId = handoff.CorrelationId,
            CausationId = handoff.CausationId,
            HandoffType = handoff.HandoffType.ToString(),
            HandoffStatus = handoff.Status.ToString(),
            SourceAgentId = handoff.SourceAgent.AgentId,
            SourceAgentRole = handoff.SourceAgent.AgentRole.ToString(),
            TargetAgentId = handoff.TargetAgent.AgentId,
            TargetAgentRole = handoff.TargetAgent.AgentRole.ToString(),
            SubjectType = handoff.Subject.SubjectType.ToString(),
            SubjectId = handoff.Subject.SubjectId,
            ActionName = handoff.Subject.ActionName,
            SafeSummary = BuildSummary(handoff),
            EvidenceSummaries = handoff.EvidenceReferences.Select(ToEvidenceSummary).ToArray(),
            ConstraintSummaries = handoff.Constraints.Select(ToConstraintSummary).ToArray(),
            CreatedByActorType = createdByActorType,
            CreatedByActorId = createdByActorId,
            MetadataVersion = 1,
            MetadataJson = """
                {"schema":"thoughtledger.handoff.entry.v1","recordsHandoffOnly":true,"grantsApproval":false,"grantsExecution":false,"mutatesSource":false,"promotesMemory":false,"startsWorkflow":false,"satisfiesPolicy":false,"transfersAuthority":false}
                """,
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            CreatedUtc = handoff.CreatedUtc
        };
    }

    private static string BuildSummary(AgentHandoff handoff) =>
        $"{handoff.SourceAgent.AgentRole} handed {handoff.HandoffType} context to {handoff.TargetAgent.AgentRole} for {handoff.Subject.SubjectType} review.";

    private static ThoughtLedgerHandoffEvidenceSummary ToEvidenceSummary(AgentHandoffEvidenceReference evidence) =>
        new()
        {
            EvidenceType = evidence.EvidenceType.ToString(),
            EvidenceId = evidence.EvidenceId,
            AllowedUses = evidence.AllowedUses.Select(allowedUse => allowedUse.ToString()).ToArray(),
            EvidenceLabel = evidence.EvidenceLabel,
            SafeEvidenceSummary = evidence.EvidenceSummary,
            GovernanceEventId = evidence.GovernanceEventId
        };

    private static ThoughtLedgerHandoffConstraintSummary ToConstraintSummary(AgentHandoffConstraint constraint) =>
        new()
        {
            ConstraintType = constraint.ConstraintType.ToString(),
            ConstraintCode = constraint.ConstraintCode,
            SafeDescription = constraint.Description
        };
}

public sealed class ThoughtLedgerHandoffEntryValidator
{
    public const string ProjectIdRequired = "thoughtledger_handoff_entry.project_id.required";
    public const string AgentHandoffIdRequired = "thoughtledger_handoff_entry.agent_handoff_id.required";
    public const string HandoffTypeRequired = "thoughtledger_handoff_entry.handoff_type.required";
    public const string HandoffStatusRequired = "thoughtledger_handoff_entry.handoff_status.required";
    public const string HandoffStatusForbidden = "thoughtledger_handoff_entry.handoff_status.forbidden";
    public const string SourceAgentRequired = "thoughtledger_handoff_entry.source_agent.required";
    public const string TargetAgentRequired = "thoughtledger_handoff_entry.target_agent.required";
    public const string SubjectRequired = "thoughtledger_handoff_entry.subject.required";
    public const string SafeSummaryRequired = "thoughtledger_handoff_entry.safe_summary.required";
    public const string AuthorityTextBlocked = "thoughtledger_handoff_entry.authority_text.blocked";
    public const string PrivateReasoningBlocked = "thoughtledger_handoff_entry.private_reasoning.blocked";
    public const string EvidenceInvalid = "thoughtledger_handoff_entry.evidence.invalid";
    public const string ConstraintInvalid = "thoughtledger_handoff_entry.constraint.invalid";
    public const string MetadataVersionInvalid = "thoughtledger_handoff_entry.metadata_version.invalid";
    public const string MetadataJsonRequired = "thoughtledger_handoff_entry.metadata_json.required";
    public const string MetadataJsonInvalid = "thoughtledger_handoff_entry.metadata_json.invalid";
    public const string AuthorityFlagBlocked = "thoughtledger_handoff_entry.authority_flag.blocked";

    private static readonly string[] AuthorityMarkers =
    [
        "approved",
        "authorized",
        "execution allowed",
        "can execute",
        "permission granted",
        "policy satisfied",
        "workflow continued",
        "source apply allowed",
        "memory promotion allowed",
        "memory promoted",
        "release approved",
        "can ship",
        "authority transferred",
        "approval transferred",
        "handoff accepted as approval",
        "target authorized"
    ];

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

    public ThoughtLedgerHandoffEntryValidationResult Validate(ThoughtLedgerHandoffEntryCreateRequest request)
    {
        var issues = new List<ThoughtLedgerHandoffEntryValidationIssue>();

        if (request.ProjectId == Guid.Empty)
            Add(issues, ProjectIdRequired, "ProjectId is required.", nameof(request.ProjectId));

        if (request.AgentHandoffId == Guid.Empty)
            Add(issues, AgentHandoffIdRequired, "AgentHandoffId is required.", nameof(request.AgentHandoffId));

        if (string.IsNullOrWhiteSpace(request.HandoffType))
            Add(issues, HandoffTypeRequired, "HandoffType is required.", nameof(request.HandoffType));

        if (string.IsNullOrWhiteSpace(request.HandoffStatus))
            Add(issues, HandoffStatusRequired, "HandoffStatus is required.", nameof(request.HandoffStatus));
        else if (ContainsAuthorityText(request.HandoffStatus))
            Add(issues, HandoffStatusForbidden, "HandoffStatus must not imply approval, execution, policy satisfaction, workflow continuation, source apply, memory promotion, release approval, or authority transfer.", nameof(request.HandoffStatus));

        ValidateRequiredText(issues, request.SourceAgentId, SourceAgentRequired, nameof(request.SourceAgentId), "SourceAgentId is required.");
        ValidateRequiredText(issues, request.SourceAgentRole, SourceAgentRequired, nameof(request.SourceAgentRole), "SourceAgentRole is required.");
        ValidateRequiredText(issues, request.TargetAgentId, TargetAgentRequired, nameof(request.TargetAgentId), "TargetAgentId is required.");
        ValidateRequiredText(issues, request.TargetAgentRole, TargetAgentRequired, nameof(request.TargetAgentRole), "TargetAgentRole is required.");
        ValidateRequiredText(issues, request.SubjectType, SubjectRequired, nameof(request.SubjectType), "SubjectType is required.");
        ValidateRequiredText(issues, request.SubjectId, SubjectRequired, nameof(request.SubjectId), "SubjectId is required.");
        ValidateRequiredText(issues, request.SafeSummary, SafeSummaryRequired, nameof(request.SafeSummary), "SafeSummary is required.");

        ValidateText(issues, request.HandoffType, nameof(request.HandoffType));
        ValidateText(issues, request.HandoffStatus, nameof(request.HandoffStatus));
        ValidateText(issues, request.SourceAgentId, nameof(request.SourceAgentId));
        ValidateText(issues, request.SourceAgentRole, nameof(request.SourceAgentRole));
        ValidateText(issues, request.TargetAgentId, nameof(request.TargetAgentId));
        ValidateText(issues, request.TargetAgentRole, nameof(request.TargetAgentRole));
        ValidateText(issues, request.SubjectType, nameof(request.SubjectType));
        ValidateText(issues, request.SubjectId, nameof(request.SubjectId));
        ValidateText(issues, request.ActionName, nameof(request.ActionName));
        ValidateText(issues, request.SafeSummary, nameof(request.SafeSummary));

        ValidateEvidence(issues, request.EvidenceSummaries);
        ValidateConstraints(issues, request.ConstraintSummaries);
        ValidateMetadata(issues, request.MetadataVersion, request.MetadataJson);
        ValidateAuthorityFlags(issues, request);

        return new ThoughtLedgerHandoffEntryValidationResult(issues);
    }

    public ThoughtLedgerHandoffEntryValidationResult Validate(ThoughtLedgerHandoffEntry entry) =>
        Validate(new ThoughtLedgerHandoffEntryCreateRequest
        {
            ThoughtLedgerHandoffEntryId = entry.ThoughtLedgerHandoffEntryId,
            ProjectId = entry.ProjectId,
            AgentHandoffId = entry.AgentHandoffId,
            GovernanceEventId = entry.GovernanceEventId,
            CorrelationId = entry.CorrelationId,
            CausationId = entry.CausationId,
            HandoffType = entry.HandoffType,
            HandoffStatus = entry.HandoffStatus,
            SourceAgentId = entry.SourceAgentId,
            SourceAgentRole = entry.SourceAgentRole,
            TargetAgentId = entry.TargetAgentId,
            TargetAgentRole = entry.TargetAgentRole,
            SubjectType = entry.SubjectType,
            SubjectId = entry.SubjectId,
            ActionName = entry.ActionName,
            SafeSummary = entry.SafeSummary,
            EvidenceSummaries = entry.EvidenceSummaries,
            ConstraintSummaries = entry.ConstraintSummaries,
            CreatedByActorType = entry.CreatedByActorType,
            CreatedByActorId = entry.CreatedByActorId,
            MetadataVersion = entry.MetadataVersion,
            MetadataJson = entry.MetadataJson,
            GrantsApproval = entry.GrantsApproval,
            GrantsExecution = entry.GrantsExecution,
            MutatesSource = entry.MutatesSource,
            PromotesMemory = entry.PromotesMemory,
            StartsWorkflow = entry.StartsWorkflow,
            SatisfiesPolicy = entry.SatisfiesPolicy,
            TransfersAuthority = entry.TransfersAuthority,
            CreatedUtc = entry.CreatedUtc
        });

    private static void ValidateRequiredText(List<ThoughtLedgerHandoffEntryValidationIssue> issues, string? value, string code, string field, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            Add(issues, code, message, field);
    }

    private static void ValidateEvidence(List<ThoughtLedgerHandoffEntryValidationIssue> issues, IReadOnlyList<ThoughtLedgerHandoffEvidenceSummary>? evidenceSummaries)
    {
        if (evidenceSummaries is null || evidenceSummaries.Count == 0)
        {
            Add(issues, EvidenceInvalid, "At least one evidence summary is required.", nameof(ThoughtLedgerHandoffEntryCreateRequest.EvidenceSummaries));
            return;
        }

        foreach (var evidence in evidenceSummaries)
        {
            if (evidence is null)
            {
                Add(issues, EvidenceInvalid, "Evidence summary cannot be null.", nameof(ThoughtLedgerHandoffEntryCreateRequest.EvidenceSummaries));
                continue;
            }

            if (string.IsNullOrWhiteSpace(evidence.EvidenceType))
                Add(issues, EvidenceInvalid, "EvidenceType is required.", nameof(ThoughtLedgerHandoffEvidenceSummary.EvidenceType));

            if (string.IsNullOrWhiteSpace(evidence.EvidenceId))
                Add(issues, EvidenceInvalid, "EvidenceId is required.", nameof(ThoughtLedgerHandoffEvidenceSummary.EvidenceId));

            if (evidence.AllowedUses is null || evidence.AllowedUses.Count == 0)
                Add(issues, EvidenceInvalid, "AllowedUses are required.", nameof(ThoughtLedgerHandoffEvidenceSummary.AllowedUses));

            ValidateText(issues, evidence.EvidenceType, nameof(ThoughtLedgerHandoffEvidenceSummary.EvidenceType));
            ValidateText(issues, evidence.EvidenceId, nameof(ThoughtLedgerHandoffEvidenceSummary.EvidenceId));
            ValidateText(issues, evidence.EvidenceLabel, nameof(ThoughtLedgerHandoffEvidenceSummary.EvidenceLabel));
            ValidateText(issues, evidence.SafeEvidenceSummary, nameof(ThoughtLedgerHandoffEvidenceSummary.SafeEvidenceSummary));

            if (evidence.AllowedUses is not null)
            {
                foreach (var allowedUse in evidence.AllowedUses)
                    ValidateText(issues, allowedUse, nameof(ThoughtLedgerHandoffEvidenceSummary.AllowedUses));
            }
        }
    }

    private static void ValidateConstraints(List<ThoughtLedgerHandoffEntryValidationIssue> issues, IReadOnlyList<ThoughtLedgerHandoffConstraintSummary>? constraintSummaries)
    {
        if (constraintSummaries is null || constraintSummaries.Count == 0)
        {
            Add(issues, ConstraintInvalid, "At least one constraint summary is required.", nameof(ThoughtLedgerHandoffEntryCreateRequest.ConstraintSummaries));
            return;
        }

        foreach (var constraint in constraintSummaries)
        {
            if (constraint is null)
            {
                Add(issues, ConstraintInvalid, "Constraint summary cannot be null.", nameof(ThoughtLedgerHandoffEntryCreateRequest.ConstraintSummaries));
                continue;
            }

            if (string.IsNullOrWhiteSpace(constraint.ConstraintType))
                Add(issues, ConstraintInvalid, "ConstraintType is required.", nameof(ThoughtLedgerHandoffConstraintSummary.ConstraintType));

            if (string.IsNullOrWhiteSpace(constraint.ConstraintCode))
                Add(issues, ConstraintInvalid, "ConstraintCode is required.", nameof(ThoughtLedgerHandoffConstraintSummary.ConstraintCode));

            if (string.IsNullOrWhiteSpace(constraint.SafeDescription))
                Add(issues, ConstraintInvalid, "SafeDescription is required.", nameof(ThoughtLedgerHandoffConstraintSummary.SafeDescription));

            ValidateText(issues, constraint.ConstraintType, nameof(ThoughtLedgerHandoffConstraintSummary.ConstraintType));
            ValidateText(issues, constraint.ConstraintCode, nameof(ThoughtLedgerHandoffConstraintSummary.ConstraintCode));
            ValidateText(issues, constraint.SafeDescription, nameof(ThoughtLedgerHandoffConstraintSummary.SafeDescription));
        }
    }

    private static void ValidateMetadata(List<ThoughtLedgerHandoffEntryValidationIssue> issues, int metadataVersion, string? metadataJson)
    {
        if (metadataVersion <= 0)
            Add(issues, MetadataVersionInvalid, "MetadataVersion must be positive.", nameof(ThoughtLedgerHandoffEntryCreateRequest.MetadataVersion));

        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            Add(issues, MetadataJsonRequired, "MetadataJson is required.", nameof(ThoughtLedgerHandoffEntryCreateRequest.MetadataJson));
            return;
        }

        ValidatePrivateReasoningText(issues, metadataJson, nameof(ThoughtLedgerHandoffEntryCreateRequest.MetadataJson));
        ValidateAuthorityText(issues, metadataJson, nameof(ThoughtLedgerHandoffEntryCreateRequest.MetadataJson));

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            ValidateMetadataElement(issues, document.RootElement);
        }
        catch (JsonException)
        {
            Add(issues, MetadataJsonInvalid, "MetadataJson must be valid JSON.", nameof(ThoughtLedgerHandoffEntryCreateRequest.MetadataJson));
        }
    }

    private static void ValidateMetadataElement(List<ThoughtLedgerHandoffEntryValidationIssue> issues, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    ValidatePrivateReasoningText(issues, property.Name, nameof(ThoughtLedgerHandoffEntryCreateRequest.MetadataJson));
                    if (IsAuthorityProperty(property.Name) && property.Value.ValueKind == JsonValueKind.True)
                        Add(issues, AuthorityTextBlocked, $"Metadata property {property.Name} cannot be true.", nameof(ThoughtLedgerHandoffEntryCreateRequest.MetadataJson));
                    ValidateMetadataElement(issues, property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    ValidateMetadataElement(issues, item);
                break;
            case JsonValueKind.String:
                ValidateText(issues, element.GetString(), nameof(ThoughtLedgerHandoffEntryCreateRequest.MetadataJson));
                break;
        }
    }

    private static void ValidateAuthorityFlags(List<ThoughtLedgerHandoffEntryValidationIssue> issues, ThoughtLedgerHandoffEntryCreateRequest request)
    {
        if (request.GrantsApproval)
            Add(issues, AuthorityFlagBlocked, "ThoughtLedger handoff entry cannot grant approval.", nameof(request.GrantsApproval));
        if (request.GrantsExecution)
            Add(issues, AuthorityFlagBlocked, "ThoughtLedger handoff entry cannot grant execution.", nameof(request.GrantsExecution));
        if (request.MutatesSource)
            Add(issues, AuthorityFlagBlocked, "ThoughtLedger handoff entry cannot mutate source.", nameof(request.MutatesSource));
        if (request.PromotesMemory)
            Add(issues, AuthorityFlagBlocked, "ThoughtLedger handoff entry cannot promote memory.", nameof(request.PromotesMemory));
        if (request.StartsWorkflow)
            Add(issues, AuthorityFlagBlocked, "ThoughtLedger handoff entry cannot start workflow.", nameof(request.StartsWorkflow));
        if (request.SatisfiesPolicy)
            Add(issues, AuthorityFlagBlocked, "ThoughtLedger handoff entry cannot satisfy policy.", nameof(request.SatisfiesPolicy));
        if (request.TransfersAuthority)
            Add(issues, AuthorityFlagBlocked, "ThoughtLedger handoff entry cannot transfer authority.", nameof(request.TransfersAuthority));
    }

    private static void ValidateText(List<ThoughtLedgerHandoffEntryValidationIssue> issues, string? text, string field)
    {
        ValidatePrivateReasoningText(issues, text, field);
        ValidateAuthorityText(issues, text, field);
    }

    private static void ValidatePrivateReasoningText(List<ThoughtLedgerHandoffEntryValidationIssue> issues, string? text, string field)
    {
        if (ContainsPrivateReasoning(text))
            Add(issues, PrivateReasoningBlocked, "ThoughtLedger handoff entry cannot contain hidden/private reasoning markers.", field);
    }

    private static void ValidateAuthorityText(List<ThoughtLedgerHandoffEntryValidationIssue> issues, string? text, string field)
    {
        if (ContainsAuthorityText(text))
            Add(issues, AuthorityTextBlocked, "ThoughtLedger handoff entry cannot contain authority-shaped language.", field);
    }

    private static bool ContainsPrivateReasoning(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        PrivateReasoningMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAuthorityText(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        AuthorityMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsAuthorityProperty(string propertyName) =>
        string.Equals(propertyName, "grantsApproval", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(propertyName, "grantsExecution", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(propertyName, "mutatesSource", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(propertyName, "promotesMemory", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(propertyName, "startsWorkflow", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(propertyName, "satisfiesPolicy", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(propertyName, "transfersAuthority", StringComparison.OrdinalIgnoreCase);

    private static void Add(List<ThoughtLedgerHandoffEntryValidationIssue> issues, string code, string message, string field) =>
        issues.Add(new ThoughtLedgerHandoffEntryValidationIssue(code, message, field));
}
