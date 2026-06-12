using System.Text.Json;

namespace IronDev.Core.Governance;

public enum ThoughtLedgerGovernanceReferenceType
{
    Observed = 1,
    Explains = 2,
    Supports = 3,
    Cites = 4,
    CausedBy = 5,
    RelatedEvidence = 6
}

public sealed record ThoughtLedgerGovernanceEventReference
{
    public Guid ThoughtLedgerGovernanceEventReferenceId { get; init; }
    public Guid ProjectId { get; init; }
    public string ThoughtLedgerEntryId { get; init; } = string.Empty;
    public Guid GovernanceEventId { get; init; }
    public string ReferenceType { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string CreatedByActorType { get; init; } = string.Empty;
    public string CreatedByActorId { get; init; } = string.Empty;
    public int MetadataVersion { get; init; }
    public string MetadataJson { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ThoughtLedgerGovernanceEventReferenceReadModel
{
    public Guid ThoughtLedgerGovernanceEventReferenceId { get; init; }
    public Guid ProjectId { get; init; }
    public string ThoughtLedgerEntryId { get; init; } = string.Empty;
    public Guid GovernanceEventId { get; init; }
    public string ReferenceType { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string CreatedByActorType { get; init; } = string.Empty;
    public string CreatedByActorId { get; init; } = string.Empty;
    public int MetadataVersion { get; init; }
    public string MetadataJson { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ThoughtLedgerGovernanceEventReferenceSummary
{
    public Guid ThoughtLedgerGovernanceEventReferenceId { get; init; }
    public Guid ProjectId { get; init; }
    public string ThoughtLedgerEntryId { get; init; } = string.Empty;
    public Guid GovernanceEventId { get; init; }
    public string ReferenceType { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string CreatedByActorType { get; init; } = string.Empty;
    public string CreatedByActorId { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ThoughtLedgerGovernanceEventReferenceRecordRequest
{
    public Guid? ThoughtLedgerGovernanceEventReferenceId { get; init; }
    public Guid ProjectId { get; init; }
    public string ThoughtLedgerEntryId { get; init; } = string.Empty;
    public Guid GovernanceEventId { get; init; }
    public string ReferenceType { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string CreatedByActorType { get; init; } = string.Empty;
    public string CreatedByActorId { get; init; } = string.Empty;
    public int MetadataVersion { get; init; }
    public string MetadataJson { get; init; } = string.Empty;
    public DateTimeOffset? CreatedUtc { get; init; }
}

public sealed record ThoughtLedgerGovernanceReferencesForThoughtLedgerEntryQuery
{
    public Guid ProjectId { get; init; }
    public string ThoughtLedgerEntryId { get; init; } = string.Empty;
    public int Take { get; init; } = ThoughtLedgerGovernanceEventReferenceValidator.DefaultTake;
}

public sealed record ThoughtLedgerGovernanceReferencesForGovernanceEventQuery
{
    public Guid ProjectId { get; init; }
    public Guid GovernanceEventId { get; init; }
    public int Take { get; init; } = ThoughtLedgerGovernanceEventReferenceValidator.DefaultTake;
}

public sealed record ThoughtLedgerGovernanceReferencesForCorrelationQuery
{
    public Guid ProjectId { get; init; }
    public Guid CorrelationId { get; init; }
    public int Take { get; init; } = ThoughtLedgerGovernanceEventReferenceValidator.DefaultTake;
}

public sealed record ThoughtLedgerGovernanceEventReferenceValidationIssue(string Code, string Message, string Field = "");

public sealed record ThoughtLedgerGovernanceEventReferenceValidationResult(IReadOnlyList<ThoughtLedgerGovernanceEventReferenceValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public interface IThoughtLedgerGovernanceEventReferenceStore
{
    Task<ThoughtLedgerGovernanceEventReference> RecordAsync(ThoughtLedgerGovernanceEventReferenceRecordRequest request, CancellationToken cancellationToken = default);

    Task<ThoughtLedgerGovernanceEventReferenceReadModel?> GetAsync(Guid referenceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ThoughtLedgerGovernanceEventReferenceSummary>> ListForThoughtLedgerEntryAsync(
        ThoughtLedgerGovernanceReferencesForThoughtLedgerEntryQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ThoughtLedgerGovernanceEventReferenceSummary>> ListForGovernanceEventAsync(
        ThoughtLedgerGovernanceReferencesForGovernanceEventQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ThoughtLedgerGovernanceEventReferenceSummary>> ListForCorrelationAsync(
        ThoughtLedgerGovernanceReferencesForCorrelationQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class ThoughtLedgerGovernanceEventReferenceValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    public const int MaxMetadataJsonLength = 64 * 1024;

    public const string ProjectIdRequired = "thoughtledger_governance_reference.project_id.required";
    public const string ThoughtLedgerEntryIdRequired = "thoughtledger_governance_reference.thought_ledger_entry_id.required";
    public const string GovernanceEventIdRequired = "thoughtledger_governance_reference.governance_event_id.required";
    public const string ReferenceTypeRequired = "thoughtledger_governance_reference.reference_type.required";
    public const string ReferenceTypeUnsupported = "thoughtledger_governance_reference.reference_type.unsupported";
    public const string ReferenceTypeAuthority = "thoughtledger_governance_reference.reference_type.authority";
    public const string ReasonCodeRequired = "thoughtledger_governance_reference.reason_code.required";
    public const string ActorTypeRequired = "thoughtledger_governance_reference.actor_type.required";
    public const string ActorIdRequired = "thoughtledger_governance_reference.actor_id.required";
    public const string MetadataVersionInvalid = "thoughtledger_governance_reference.metadata_version.invalid";
    public const string MetadataJsonRequired = "thoughtledger_governance_reference.metadata_json.required";
    public const string MetadataJsonTooLarge = "thoughtledger_governance_reference.metadata_json.too_large";
    public const string MetadataJsonInvalid = "thoughtledger_governance_reference.metadata_json.invalid";
    public const string MetadataJsonUnsafe = "thoughtledger_governance_reference.metadata_json.unsafe";
    public const string TextUnsafe = "thoughtledger_governance_reference.text.unsafe";
    public const string CorrelationIdRequired = "thoughtledger_governance_reference.correlation_id.required";
    public const string TakeInvalid = "thoughtledger_governance_reference.take.invalid";

    private static readonly HashSet<string> AllowedReferenceTypes = Enum.GetNames<ThoughtLedgerGovernanceReferenceType>()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ForbiddenReferenceTypeNames =
    [
        "Approves",
        "Authorizes",
        "Executes",
        "GrantsPermission",
        "SatisfiesPolicy",
        "PromotesMemory",
        "AppliesSource",
        "Releases",
        "Overrides",
        "Owns",
        "TransfersAuthority"
    ];

    private static readonly string[] ForbiddenAuthorityTextMarkers =
    [
        "approves",
        "authorized",
        "authorizes",
        "executes",
        "grants permission",
        "grants execution",
        "satisfies policy",
        "promotes memory",
        "applies source",
        "release approved",
        "overrides policy",
        "transfers authority"
    ];

    private static readonly string[] PrivateReasoningMarkers =
    [
        "rawprompt",
        "raw prompt",
        "rawcompletion",
        "raw completion",
        "chainofthought",
        "chain of thought",
        "scratchpad",
        "private reasoning",
        "hidden reasoning",
        "system prompt",
        "developer prompt"
    ];

    private static readonly HashSet<string> AuthorityJsonFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "approves",
        "authorizes",
        "executes",
        "grantsPermission",
        "grantsApproval",
        "grantsExecution",
        "satisfiesPolicy",
        "promotesMemory",
        "appliesSource",
        "releases",
        "overrides",
        "owns",
        "transfersAuthority",
        "sourceApplied",
        "memoryPromoted",
        "workflowStarted",
        "policySatisfied",
        "releaseApproved",
        "createsA2aHandoff",
        "createsDogfoodReceipt"
    };

    public ThoughtLedgerGovernanceEventReferenceValidationResult ValidateRecord(ThoughtLedgerGovernanceEventReferenceRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<ThoughtLedgerGovernanceEventReferenceValidationIssue>();

        if (request.ProjectId == Guid.Empty)
            issues.Add(new(ProjectIdRequired, "Project ID is required.", nameof(request.ProjectId)));

        if (string.IsNullOrWhiteSpace(request.ThoughtLedgerEntryId))
            issues.Add(new(ThoughtLedgerEntryIdRequired, "ThoughtLedger entry ID is required.", nameof(request.ThoughtLedgerEntryId)));

        if (request.GovernanceEventId == Guid.Empty)
            issues.Add(new(GovernanceEventIdRequired, "Governance event ID is required.", nameof(request.GovernanceEventId)));

        ValidateReferenceType(request.ReferenceType, issues);

        if (string.IsNullOrWhiteSpace(request.ReasonCode))
            issues.Add(new(ReasonCodeRequired, "Reason code is required.", nameof(request.ReasonCode)));

        if (string.IsNullOrWhiteSpace(request.CreatedByActorType))
            issues.Add(new(ActorTypeRequired, "Actor type is required.", nameof(request.CreatedByActorType)));

        if (string.IsNullOrWhiteSpace(request.CreatedByActorId))
            issues.Add(new(ActorIdRequired, "Actor ID is required.", nameof(request.CreatedByActorId)));

        if (request.MetadataVersion <= 0)
            issues.Add(new(MetadataVersionInvalid, "Metadata version must be positive.", nameof(request.MetadataVersion)));

        ValidateTextSafety(issues, request.ThoughtLedgerEntryId, nameof(request.ThoughtLedgerEntryId));
        ValidateTextSafety(issues, request.ReferenceType, nameof(request.ReferenceType));
        ValidateTextSafety(issues, request.ReasonCode, nameof(request.ReasonCode));
        ValidateTextSafety(issues, request.Reason ?? string.Empty, nameof(request.Reason));
        ValidateTextSafety(issues, request.CreatedByActorType, nameof(request.CreatedByActorType));
        ValidateTextSafety(issues, request.CreatedByActorId, nameof(request.CreatedByActorId));
        ValidateMetadataJson(request.MetadataJson, issues);

        return new(issues);
    }

    public ThoughtLedgerGovernanceEventReferenceValidationResult ValidateEntryQuery(ThoughtLedgerGovernanceReferencesForThoughtLedgerEntryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var issues = new List<ThoughtLedgerGovernanceEventReferenceValidationIssue>();
        if (query.ProjectId == Guid.Empty)
            issues.Add(new(ProjectIdRequired, "Project ID is required.", nameof(query.ProjectId)));
        if (string.IsNullOrWhiteSpace(query.ThoughtLedgerEntryId))
            issues.Add(new(ThoughtLedgerEntryIdRequired, "ThoughtLedger entry ID is required.", nameof(query.ThoughtLedgerEntryId)));
        ValidateTake(query.Take, issues);
        return new(issues);
    }

    public ThoughtLedgerGovernanceEventReferenceValidationResult ValidateGovernanceEventQuery(ThoughtLedgerGovernanceReferencesForGovernanceEventQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var issues = new List<ThoughtLedgerGovernanceEventReferenceValidationIssue>();
        if (query.ProjectId == Guid.Empty)
            issues.Add(new(ProjectIdRequired, "Project ID is required.", nameof(query.ProjectId)));
        if (query.GovernanceEventId == Guid.Empty)
            issues.Add(new(GovernanceEventIdRequired, "Governance event ID is required.", nameof(query.GovernanceEventId)));
        ValidateTake(query.Take, issues);
        return new(issues);
    }

    public ThoughtLedgerGovernanceEventReferenceValidationResult ValidateCorrelationQuery(ThoughtLedgerGovernanceReferencesForCorrelationQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var issues = new List<ThoughtLedgerGovernanceEventReferenceValidationIssue>();
        if (query.ProjectId == Guid.Empty)
            issues.Add(new(ProjectIdRequired, "Project ID is required.", nameof(query.ProjectId)));
        if (query.CorrelationId == Guid.Empty)
            issues.Add(new(CorrelationIdRequired, "Correlation ID is required.", nameof(query.CorrelationId)));
        ValidateTake(query.Take, issues);
        return new(issues);
    }

    public string NormalizeReferenceType(string referenceType)
    {
        if (Enum.TryParse<ThoughtLedgerGovernanceReferenceType>(referenceType.Trim(), ignoreCase: true, out var parsed))
            return parsed.ToString();

        return referenceType.Trim();
    }

    private static void ValidateReferenceType(string referenceType, List<ThoughtLedgerGovernanceEventReferenceValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(referenceType))
        {
            issues.Add(new(ReferenceTypeRequired, "Reference type is required.", nameof(referenceType)));
            return;
        }

        var trimmed = referenceType.Trim();
        if (ForbiddenReferenceTypeNames.Any(name => name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new(ReferenceTypeAuthority, "Reference type must not claim approval, execution, policy, promotion, source apply, release, ownership, or authority transfer.", nameof(referenceType)));
            return;
        }

        if (!AllowedReferenceTypes.Contains(trimmed))
            issues.Add(new(ReferenceTypeUnsupported, "Reference type is not supported.", nameof(referenceType)));
    }

    private static void ValidateTake(int take, List<ThoughtLedgerGovernanceEventReferenceValidationIssue> issues)
    {
        if (take < 0 || take > MaxTake)
            issues.Add(new(TakeInvalid, $"Take must be between 0 and {MaxTake}.", nameof(take)));
    }

    private static void ValidateTextSafety(List<ThoughtLedgerGovernanceEventReferenceValidationIssue> issues, string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var lower = value.ToLowerInvariant();
        if (ForbiddenAuthorityTextMarkers.Any(lower.Contains) || PrivateReasoningMarkers.Any(lower.Contains))
            issues.Add(new(TextUnsafe, "Text must not claim authority or contain raw/private reasoning markers.", field));
    }

    private static void ValidateMetadataJson(string metadataJson, List<ThoughtLedgerGovernanceEventReferenceValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            issues.Add(new(MetadataJsonRequired, "Metadata JSON is required.", nameof(metadataJson)));
            return;
        }

        if (metadataJson.Length > MaxMetadataJsonLength)
        {
            issues.Add(new(MetadataJsonTooLarge, $"Metadata JSON must be {MaxMetadataJsonLength} characters or fewer.", nameof(metadataJson)));
            return;
        }

        if (PrivateReasoningMarkers.Any(marker => metadataJson.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new(MetadataJsonUnsafe, "Metadata JSON must not contain raw/private reasoning markers.", nameof(metadataJson)));

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new(MetadataJsonInvalid, "Metadata JSON must be an object.", nameof(metadataJson)));
                return;
            }

            if (!document.RootElement.TryGetProperty("schema", out _) && !document.RootElement.TryGetProperty("schemaVersion", out _))
                issues.Add(new(MetadataJsonInvalid, "Metadata JSON must include schema or schemaVersion.", nameof(metadataJson)));

            if (ContainsTruthyAuthorityField(document.RootElement))
                issues.Add(new(MetadataJsonUnsafe, "Metadata JSON must not claim approval, execution, source apply, memory promotion, policy satisfaction, workflow progress, dogfood receipt creation, A2A handoff creation, or authority transfer.", nameof(metadataJson)));
        }
        catch (JsonException)
        {
            issues.Add(new(MetadataJsonInvalid, "Metadata JSON must be valid JSON.", nameof(metadataJson)));
        }
    }

    private static bool ContainsTruthyAuthorityField(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (AuthorityJsonFieldNames.Contains(property.Name) && IsTruthy(property.Value))
                    return true;

                if (ContainsTruthyAuthorityField(property.Value))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsTruthyAuthorityField(item))
                    return true;
            }
        }

        return false;
    }

    private static bool IsTruthy(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => element.TryGetInt64(out var number) && number != 0,
            JsonValueKind.String => element.GetString() is { } value &&
                                    (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("1", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
}