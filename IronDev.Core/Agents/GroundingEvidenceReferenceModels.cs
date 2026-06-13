using System.Text.Json;

namespace IronDev.Core.Agents;

public static class GroundingEvidenceReferenceVocabulary
{
    public static readonly IReadOnlySet<string> AllowedEvidenceTypes = new HashSet<string>(
        [
            "GovernanceEvent",
            "ToolRequest",
            "ToolGateDecision",
            "ApprovalRequirementEvaluation",
            "ApprovalPackage",
            "ApprovalDecision",
            "PolicyDecisionEvent",
            "DogfoodReceipt",
            "ThoughtLedgerReference",
            "ThoughtLedgerHandoffEntry",
            "AgentHandoff",
            "ValidationOutput",
            "RunReport",
            "HumanNote",
            "CriticReview",
            "CodeStandardsReview",
            "SourceFileRange",
            "DocumentSection",
            "ExternalReference"
        ],
        StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> ForbiddenEvidenceTypes = new HashSet<string>(
        [
            "Approval",
            "ExecutionPermission",
            "PolicySatisfied",
            "WorkflowContinuation",
            "SourceApplyPermission",
            "MemoryPromotionPermission",
            "ReleaseApproval",
            "AuthorityTransfer",
            "AcceptedMemory",
            "TrustedTruth"
        ],
        StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> AllowedClaimTypes = new HashSet<string>(
        [
            "HandoffSummary",
            "ThoughtLedgerEntry",
            "ApprovalPackage",
            "PolicyEvaluationInput",
            "ValidationClaim",
            "DebugFinding",
            "ReviewFinding",
            "MemoryCandidateClaim",
            "SourceApplyCandidateClaim",
            "ReleaseEvidenceClaim",
            "HumanDecisionSupportClaim"
        ],
        StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> ForbiddenClaimTypes = new HashSet<string>(
        [
            "ApprovedClaim",
            "ExecutionAllowedClaim",
            "PolicySatisfiedClaim",
            "WorkflowContinuedClaim",
            "SourceApplyApprovedClaim",
            "MemoryPromotedClaim",
            "ReleaseApprovedClaim",
            "AuthorityTransferredClaim",
            "AcceptedMemoryClaim"
        ],
        StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> AllowedUses = new HashSet<string>(
        [
            "Context",
            "Review",
            "Debugging",
            "Validation",
            "Traceability",
            "RequirementEvaluation",
            "HumanDecisionSupport",
            "AuditReference",
            "PolicyInput",
            "HandoffExplanation",
            "ClaimSupport"
        ],
        StringComparer.Ordinal);

    public static readonly IReadOnlySet<string> ForbiddenUses = new HashSet<string>(
        [
            "Approval",
            "Approve",
            "Approved",
            "ExecutionPermission",
            "CanExecute",
            "ExecutionAllowed",
            "PolicySatisfied",
            "WorkflowContinuation",
            "SourceApplyPermission",
            "SourceApplyAllowed",
            "MemoryPromotionPermission",
            "MemoryPromotionAllowed",
            "ReleaseApproval",
            "ReleaseApproved",
            "CanShip",
            "AuthorityTransfer",
            "PermissionTransfer",
            "ApprovalTransfer",
            "AcceptedMemory",
            "TrustedTruth"
        ],
        StringComparer.Ordinal);
}

public sealed record GroundingEvidenceReference
{
    public required Guid GroundingEvidenceReferenceId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string EvidenceType { get; init; }
    public required string EvidenceId { get; init; }
    public required string ClaimType { get; init; }
    public required string ClaimId { get; init; }
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public required IReadOnlyList<string> AllowedUses { get; init; }
    public GroundingEvidenceReferenceLocation? Location { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? AgentHandoffId { get; init; }
    public Guid? ThoughtLedgerEntryId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
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

public sealed record GroundingEvidenceReferenceCreateRequest
{
    public Guid? GroundingEvidenceReferenceId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string EvidenceType { get; init; }
    public required string EvidenceId { get; init; }
    public required string ClaimType { get; init; }
    public required string ClaimId { get; init; }
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public required IReadOnlyList<string> AllowedUses { get; init; }
    public GroundingEvidenceReferenceLocation? Location { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? AgentHandoffId { get; init; }
    public Guid? ThoughtLedgerEntryId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
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

public sealed record GroundingEvidenceReferenceSummary
{
    public required Guid GroundingEvidenceReferenceId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string EvidenceType { get; init; }
    public required string EvidenceId { get; init; }
    public required string ClaimType { get; init; }
    public required string ClaimId { get; init; }
    public required IReadOnlyList<string> AllowedUses { get; init; }
    public required bool IsEvidenceOnly { get; init; }
}

public sealed record GroundingEvidenceReferenceLocation
{
    public string? SourceUri { get; init; }
    public string? SourcePath { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public string? SectionId { get; init; }
    public string? AnchorText { get; init; }
}

public sealed record GroundingEvidenceReferenceValidationResult
{
    public required bool IsValid { get; init; }
    public IReadOnlyList<GroundingEvidenceReferenceValidationIssue> Issues { get; init; } = [];
}

public sealed record GroundingEvidenceReferenceValidationIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public interface IGroundingEvidenceReferenceFactory
{
    GroundingEvidenceReferenceCreateRequest CreateFromHandoffEvidence(
        Guid projectId,
        string claimType,
        string claimId,
        AgentHandoffEvidenceReference evidence,
        string createdByActorType,
        string createdByActorId);
}

public sealed class GroundingEvidenceReferenceFactory : IGroundingEvidenceReferenceFactory
{
    public GroundingEvidenceReferenceCreateRequest CreateFromHandoffEvidence(
        Guid projectId,
        string claimType,
        string claimId,
        AgentHandoffEvidenceReference evidence,
        string createdByActorType,
        string createdByActorId)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var allowedUses = evidence.AllowedUses
            .Select(use => use.ToString())
            .Concat(["ClaimSupport"])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new GroundingEvidenceReferenceCreateRequest
        {
            ProjectId = projectId,
            EvidenceType = evidence.EvidenceType.ToString(),
            EvidenceId = evidence.EvidenceId,
            ClaimType = claimType,
            ClaimId = claimId,
            EvidenceLabel = evidence.EvidenceLabel,
            SafeSummary = evidence.EvidenceSummary,
            AllowedUses = allowedUses,
            GovernanceEventId = evidence.GovernanceEventId,
            CreatedByActorType = createdByActorType,
            CreatedByActorId = createdByActorId,
            MetadataVersion = 1,
            MetadataJson = """
                {
                  "schema": "grounding.evidence.reference.metadata.v1",
                  "notes": "Grounding reference for review claim.",
                  "grantsApproval": false,
                  "grantsExecution": false,
                  "mutatesSource": false,
                  "promotesMemory": false,
                  "startsWorkflow": false,
                  "satisfiesPolicy": false,
                  "transfersAuthority": false
                }
                """,
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false
        };
    }
}

public sealed class GroundingEvidenceReferenceValidator
{
    public const string ReferenceRequired = "GROUNDING_EVIDENCE_REFERENCE_REQUIRED";
    public const string ReferenceIdRequired = "GROUNDING_EVIDENCE_REFERENCE_ID_REQUIRED";
    public const string ProjectIdRequired = "GROUNDING_EVIDENCE_PROJECT_ID_REQUIRED";
    public const string EvidenceTypeRequired = "GROUNDING_EVIDENCE_TYPE_REQUIRED";
    public const string EvidenceTypeInvalid = "GROUNDING_EVIDENCE_TYPE_INVALID";
    public const string EvidenceTypeForbidden = "GROUNDING_EVIDENCE_TYPE_FORBIDDEN";
    public const string EvidenceIdRequired = "GROUNDING_EVIDENCE_ID_REQUIRED";
    public const string ClaimTypeRequired = "GROUNDING_CLAIM_TYPE_REQUIRED";
    public const string ClaimTypeInvalid = "GROUNDING_CLAIM_TYPE_INVALID";
    public const string ClaimTypeForbidden = "GROUNDING_CLAIM_TYPE_FORBIDDEN";
    public const string ClaimIdRequired = "GROUNDING_CLAIM_ID_REQUIRED";
    public const string AllowedUseRequired = "GROUNDING_ALLOWED_USE_REQUIRED";
    public const string AllowedUseInvalid = "GROUNDING_ALLOWED_USE_INVALID";
    public const string AllowedUseForbidden = "GROUNDING_ALLOWED_USE_FORBIDDEN";
    public const string AllowedUseDuplicate = "GROUNDING_ALLOWED_USE_DUPLICATE";
    public const string AuthorityTextBlocked = "GROUNDING_AUTHORITY_TEXT_BLOCKED";
    public const string PrivateReasoningBlocked = "GROUNDING_PRIVATE_REASONING_BLOCKED";
    public const string LocationInvalid = "GROUNDING_LOCATION_INVALID";
    public const string ActorRequired = "GROUNDING_ACTOR_REQUIRED";
    public const string MetadataVersionInvalid = "GROUNDING_METADATA_VERSION_INVALID";
    public const string MetadataJsonRequired = "GROUNDING_METADATA_JSON_REQUIRED";
    public const string MetadataJsonInvalid = "GROUNDING_METADATA_JSON_INVALID";
    public const string AuthorityMetadataBlocked = "GROUNDING_AUTHORITY_METADATA_BLOCKED";
    public const string AuthorityFlagBlocked = "GROUNDING_AUTHORITY_FLAG_BLOCKED";

    private const int MaxMetadataJsonLength = 8192;
    private const int MaxAnchorTextLength = 240;

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
        "approved",
        "approval granted",
        "execution permission",
        "canExecute",
        "execution allowed",
        "policy satisfied",
        "workflow continuation",
        "sourceApplyAllowed",
        "source apply permission",
        "memoryPromotionAllowed",
        "memory promotion permission",
        "releaseApproved",
        "release approval",
        "can ship",
        "authority transfer",
        "accepted memory",
        "trusted truth"
    ];

    private static readonly string[] AuthorityMetadataProperties =
    [
        "approved",
        "canExecute",
        "executionAllowed",
        "policySatisfied",
        "workflowContinuation",
        "sourceApplyAllowed",
        "memoryPromotionAllowed",
        "releaseApproved",
        "acceptedMemory",
        "trustedTruth",
        "grantsApproval",
        "grantsExecution",
        "mutatesSource",
        "promotesMemory",
        "startsWorkflow",
        "satisfiesPolicy",
        "transfersAuthority"
    ];

    public GroundingEvidenceReferenceValidationResult Validate(GroundingEvidenceReferenceCreateRequest? request)
    {
        var issues = new List<GroundingEvidenceReferenceValidationIssue>();

        if (request is null)
        {
            Add(issues, ReferenceRequired, "Grounding evidence reference request is required.");
            return Result(issues);
        }

        if (request.GroundingEvidenceReferenceId == Guid.Empty)
            Add(issues, ReferenceIdRequired, "Grounding evidence reference ID must not be empty.", nameof(request.GroundingEvidenceReferenceId));

        ValidateCommon(
            request.ProjectId,
            request.EvidenceType,
            request.EvidenceId,
            request.ClaimType,
            request.ClaimId,
            request.EvidenceLabel,
            request.SafeSummary,
            request.AllowedUses,
            request.Location,
            request.CreatedByActorType,
            request.CreatedByActorId,
            request.MetadataVersion,
            request.MetadataJson,
            request.GrantsApproval,
            request.GrantsExecution,
            request.MutatesSource,
            request.PromotesMemory,
            request.StartsWorkflow,
            request.SatisfiesPolicy,
            request.TransfersAuthority,
            issues);

        return Result(issues);
    }

    public GroundingEvidenceReferenceValidationResult Validate(GroundingEvidenceReference? reference)
    {
        var issues = new List<GroundingEvidenceReferenceValidationIssue>();

        if (reference is null)
        {
            Add(issues, ReferenceRequired, "Grounding evidence reference is required.");
            return Result(issues);
        }

        if (reference.GroundingEvidenceReferenceId == Guid.Empty)
            Add(issues, ReferenceIdRequired, "Grounding evidence reference ID is required.", nameof(reference.GroundingEvidenceReferenceId));

        ValidateCommon(
            reference.ProjectId,
            reference.EvidenceType,
            reference.EvidenceId,
            reference.ClaimType,
            reference.ClaimId,
            reference.EvidenceLabel,
            reference.SafeSummary,
            reference.AllowedUses,
            reference.Location,
            reference.CreatedByActorType,
            reference.CreatedByActorId,
            reference.MetadataVersion,
            reference.MetadataJson,
            reference.GrantsApproval,
            reference.GrantsExecution,
            reference.MutatesSource,
            reference.PromotesMemory,
            reference.StartsWorkflow,
            reference.SatisfiesPolicy,
            reference.TransfersAuthority,
            issues);

        return Result(issues);
    }

    private static void ValidateCommon(
        Guid projectId,
        string evidenceType,
        string evidenceId,
        string claimType,
        string claimId,
        string? evidenceLabel,
        string? safeSummary,
        IReadOnlyList<string>? allowedUses,
        GroundingEvidenceReferenceLocation? location,
        string createdByActorType,
        string createdByActorId,
        int metadataVersion,
        string metadataJson,
        bool grantsApproval,
        bool grantsExecution,
        bool mutatesSource,
        bool promotesMemory,
        bool startsWorkflow,
        bool satisfiesPolicy,
        bool transfersAuthority,
        List<GroundingEvidenceReferenceValidationIssue> issues)
    {
        if (projectId == Guid.Empty)
            Add(issues, ProjectIdRequired, "Project ID is required.", nameof(GroundingEvidenceReference.ProjectId));

        ValidateEvidenceType(evidenceType, issues);

        if (string.IsNullOrWhiteSpace(evidenceId))
            Add(issues, EvidenceIdRequired, "Evidence ID is required.", nameof(GroundingEvidenceReference.EvidenceId));

        ValidateClaimType(claimType, issues);

        if (string.IsNullOrWhiteSpace(claimId))
            Add(issues, ClaimIdRequired, "Claim ID is required.", nameof(GroundingEvidenceReference.ClaimId));

        ValidateAllowedUses(allowedUses, issues);
        ValidateSafeText(evidenceLabel, nameof(GroundingEvidenceReference.EvidenceLabel), issues);
        ValidateSafeText(safeSummary, nameof(GroundingEvidenceReference.SafeSummary), issues);
        ValidateLocation(location, issues);

        if (string.IsNullOrWhiteSpace(createdByActorType) || string.IsNullOrWhiteSpace(createdByActorId))
            Add(issues, ActorRequired, "Created-by actor type and ID are required.", nameof(GroundingEvidenceReference.CreatedByActorId));

        ValidateMetadata(metadataVersion, metadataJson, issues);

        if (grantsApproval || grantsExecution || mutatesSource || promotesMemory || startsWorkflow || satisfiesPolicy || transfersAuthority)
            Add(issues, AuthorityFlagBlocked, "Grounding evidence reference authority flags must remain false.");
    }

    private static void ValidateEvidenceType(string evidenceType, List<GroundingEvidenceReferenceValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(evidenceType))
        {
            Add(issues, EvidenceTypeRequired, "Evidence type is required.", nameof(GroundingEvidenceReference.EvidenceType));
            return;
        }

        if (GroundingEvidenceReferenceVocabulary.ForbiddenEvidenceTypes.Contains(evidenceType))
        {
            Add(issues, EvidenceTypeForbidden, $"Evidence type is forbidden for grounding: {evidenceType}.", nameof(GroundingEvidenceReference.EvidenceType));
            return;
        }

        if (!GroundingEvidenceReferenceVocabulary.AllowedEvidenceTypes.Contains(evidenceType))
            Add(issues, EvidenceTypeInvalid, $"Evidence type is not in the grounding vocabulary: {evidenceType}.", nameof(GroundingEvidenceReference.EvidenceType));
    }

    private static void ValidateClaimType(string claimType, List<GroundingEvidenceReferenceValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(claimType))
        {
            Add(issues, ClaimTypeRequired, "Claim type is required.", nameof(GroundingEvidenceReference.ClaimType));
            return;
        }

        if (GroundingEvidenceReferenceVocabulary.ForbiddenClaimTypes.Contains(claimType))
        {
            Add(issues, ClaimTypeForbidden, $"Claim type is forbidden for grounding: {claimType}.", nameof(GroundingEvidenceReference.ClaimType));
            return;
        }

        if (!GroundingEvidenceReferenceVocabulary.AllowedClaimTypes.Contains(claimType))
            Add(issues, ClaimTypeInvalid, $"Claim type is not in the grounding vocabulary: {claimType}.", nameof(GroundingEvidenceReference.ClaimType));
    }

    private static void ValidateAllowedUses(IReadOnlyList<string>? allowedUses, List<GroundingEvidenceReferenceValidationIssue> issues)
    {
        if (allowedUses is null || allowedUses.Count == 0)
        {
            Add(issues, AllowedUseRequired, "At least one allowed use is required.", nameof(GroundingEvidenceReference.AllowedUses));
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var allowedUse in allowedUses)
        {
            if (string.IsNullOrWhiteSpace(allowedUse))
            {
                Add(issues, AllowedUseInvalid, "Allowed use must not be blank.", nameof(GroundingEvidenceReference.AllowedUses));
                continue;
            }

            if (GroundingEvidenceReferenceVocabulary.ForbiddenUses.Contains(allowedUse))
            {
                Add(issues, AllowedUseForbidden, $"Allowed use is forbidden for grounding: {allowedUse}.", nameof(GroundingEvidenceReference.AllowedUses));
                continue;
            }

            if (!GroundingEvidenceReferenceVocabulary.AllowedUses.Contains(allowedUse))
            {
                Add(issues, AllowedUseInvalid, $"Allowed use is not in the grounding vocabulary: {allowedUse}.", nameof(GroundingEvidenceReference.AllowedUses));
                continue;
            }

            if (!seen.Add(allowedUse))
                Add(issues, AllowedUseDuplicate, $"Allowed use is duplicated: {allowedUse}.", nameof(GroundingEvidenceReference.AllowedUses));
        }
    }

    private static void ValidateLocation(GroundingEvidenceReferenceLocation? location, List<GroundingEvidenceReferenceValidationIssue> issues)
    {
        if (location is null)
            return;

        if (location.StartLine is <= 0)
            Add(issues, LocationInvalid, "Location start line must be positive when present.", nameof(GroundingEvidenceReference.Location));

        if (location.EndLine.HasValue && location.StartLine.HasValue && location.EndLine.Value < location.StartLine.Value)
            Add(issues, LocationInvalid, "Location end line must be greater than or equal to start line.", nameof(GroundingEvidenceReference.Location));

        if (!string.IsNullOrEmpty(location.AnchorText) && location.AnchorText.Length > MaxAnchorTextLength)
            Add(issues, LocationInvalid, "Location anchor text is too long.", nameof(GroundingEvidenceReference.Location));

        ValidateSafeText(location.SourceUri, nameof(GroundingEvidenceReferenceLocation.SourceUri), issues);
        ValidateSafeText(location.SourcePath, nameof(GroundingEvidenceReferenceLocation.SourcePath), issues);
        ValidateSafeText(location.SectionId, nameof(GroundingEvidenceReferenceLocation.SectionId), issues);
        ValidateSafeText(location.AnchorText, nameof(GroundingEvidenceReferenceLocation.AnchorText), issues);
    }

    private static void ValidateMetadata(int metadataVersion, string metadataJson, List<GroundingEvidenceReferenceValidationIssue> issues)
    {
        if (metadataVersion <= 0)
            Add(issues, MetadataVersionInvalid, "Metadata version must be positive.", nameof(GroundingEvidenceReference.MetadataVersion));

        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            Add(issues, MetadataJsonRequired, "Metadata JSON is required.", nameof(GroundingEvidenceReference.MetadataJson));
            return;
        }

        if (metadataJson.Length > MaxMetadataJsonLength)
            Add(issues, MetadataJsonInvalid, "Metadata JSON is too large.", nameof(GroundingEvidenceReference.MetadataJson));

        if (ContainsPrivateReasoning(metadataJson))
            Add(issues, PrivateReasoningBlocked, "Metadata JSON contains hidden/private reasoning markers.", nameof(GroundingEvidenceReference.MetadataJson));

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            ValidateMetadataElement(document.RootElement, issues);
        }
        catch (JsonException)
        {
            Add(issues, MetadataJsonInvalid, "Metadata JSON must be valid JSON.", nameof(GroundingEvidenceReference.MetadataJson));
        }
    }

    private static void ValidateMetadataElement(JsonElement element, List<GroundingEvidenceReferenceValidationIssue> issues)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (ContainsPrivateReasoning(property.Name))
                        Add(issues, PrivateReasoningBlocked, $"Metadata property contains hidden/private reasoning marker: {property.Name}.", property.Name);

                    if (AuthorityMetadataProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.True)
                    {
                        Add(issues, AuthorityMetadataBlocked, $"Metadata property cannot grant authority: {property.Name}.", property.Name);
                    }

                    ValidateMetadataElement(property.Value, issues);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    ValidateMetadataElement(item, issues);
                break;

            case JsonValueKind.String:
                ValidateSafeText(element.GetString(), nameof(GroundingEvidenceReference.MetadataJson), issues);
                break;
        }
    }

    private static void ValidateSafeText(string? value, string field, List<GroundingEvidenceReferenceValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (ContainsPrivateReasoning(value))
            Add(issues, PrivateReasoningBlocked, $"{field} contains hidden/private reasoning markers.", field);

        if (ContainsAuthorityMarker(value))
            Add(issues, AuthorityTextBlocked, $"{field} contains authority-shaped language.", field);
    }

    private static bool ContainsPrivateReasoning(string value) =>
        PrivateReasoningMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAuthorityMarker(string value) =>
        AuthorityMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static GroundingEvidenceReferenceValidationResult Result(List<GroundingEvidenceReferenceValidationIssue> issues) =>
        new()
        {
            IsValid = issues.Count == 0,
            Issues = issues
        };

    private static void Add(
        List<GroundingEvidenceReferenceValidationIssue> issues,
        string code,
        string message,
        string field = "") =>
        issues.Add(new GroundingEvidenceReferenceValidationIssue
        {
            Code = code,
            Severity = "Error",
            Message = message,
            Field = field
        });
}
