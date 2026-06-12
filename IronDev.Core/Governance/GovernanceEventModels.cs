using System.Text.Json;

namespace IronDev.Core.Governance;

public sealed record GovernanceEvent
{
    public required Guid EventId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string EventType { get; init; }
    public required string ActorType { get; init; }
    public required string ActorId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public required int PayloadVersion { get; init; }
    public required string PayloadJson { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record GovernanceEventReadModel
{
    public required Guid EventId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string EventType { get; init; }
    public required string ActorType { get; init; }
    public required string ActorId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public required int PayloadVersion { get; init; }
    public required string PayloadJson { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record GovernanceEventSummary
{
    public required Guid EventId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string EventType { get; init; }
    public required string ActorType { get; init; }
    public required string ActorId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public required int PayloadVersion { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record GovernanceEventAppendRequest
{
    public required Guid ProjectId { get; init; }
    public required string EventType { get; init; }
    public required string ActorType { get; init; }
    public required string ActorId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public required int PayloadVersion { get; init; }
    public required string PayloadJson { get; init; }
}

public sealed record GovernanceEventsForProjectQuery
{
    public required Guid ProjectId { get; init; }
    public int Take { get; init; } = GovernanceEventValidator.DefaultTake;
    public DateTimeOffset? BeforeCreatedUtc { get; init; }
}

public sealed record GovernanceEventsForCorrelationQuery
{
    public required Guid CorrelationId { get; init; }
    public int Take { get; init; } = GovernanceEventValidator.DefaultTake;
}

public sealed record GovernanceEventsForSubjectQuery
{
    public required Guid ProjectId { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public int Take { get; init; } = GovernanceEventValidator.DefaultTake;
}

public sealed record GovernanceEventsCausedByQuery
{
    public required Guid CausationId { get; init; }
    public int Take { get; init; } = GovernanceEventValidator.DefaultTake;
}

public sealed record GovernanceEventValidationIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public interface IGovernanceEventStore
{
    Task<GovernanceEvent> AppendAsync(GovernanceEventAppendRequest request, CancellationToken cancellationToken = default);

    Task<GovernanceEventReadModel?> GetAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GovernanceEventSummary>> ListForProjectAsync(GovernanceEventsForProjectQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GovernanceEventSummary>> ListForCorrelationAsync(GovernanceEventsForCorrelationQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GovernanceEventSummary>> ListForSubjectAsync(GovernanceEventsForSubjectQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GovernanceEventSummary>> ListCausedByAsync(GovernanceEventsCausedByQuery query, CancellationToken cancellationToken = default);
}

public sealed class GovernanceEventValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;

    public const string ProjectIdRequired = "GOVERNANCE_EVENT_PROJECT_ID_REQUIRED";
    public const string EventTypeRequired = "GOVERNANCE_EVENT_TYPE_REQUIRED";
    public const string ActorTypeRequired = "GOVERNANCE_EVENT_ACTOR_TYPE_REQUIRED";
    public const string ActorIdRequired = "GOVERNANCE_EVENT_ACTOR_ID_REQUIRED";
    public const string PayloadVersionInvalid = "GOVERNANCE_EVENT_PAYLOAD_VERSION_INVALID";
    public const string PayloadJsonRequired = "GOVERNANCE_EVENT_PAYLOAD_JSON_REQUIRED";
    public const string PayloadJsonInvalid = "GOVERNANCE_EVENT_PAYLOAD_JSON_INVALID";
    public const string PayloadTextUnsafe = "GOVERNANCE_EVENT_PAYLOAD_TEXT_UNSAFE";
    public const string CorrelationIdRequired = "GOVERNANCE_EVENT_CORRELATION_ID_REQUIRED";
    public const string CausationIdRequired = "GOVERNANCE_EVENT_CAUSATION_ID_REQUIRED";
    public const string SubjectTypeRequired = "GOVERNANCE_EVENT_SUBJECT_TYPE_REQUIRED";
    public const string SubjectIdRequired = "GOVERNANCE_EVENT_SUBJECT_ID_REQUIRED";
    public const string TakeInvalid = "GOVERNANCE_EVENT_TAKE_INVALID";

    private const string SeverityError = "error";

    public IReadOnlyList<GovernanceEventValidationIssue> ValidateAppend(GovernanceEventAppendRequest? request)
    {
        var issues = new List<GovernanceEventValidationIssue>();
        if (request is null)
        {
            issues.Add(Issue(PayloadJsonRequired, "Governance event append request is required.", "Request"));
            return issues;
        }

        if (request.ProjectId == Guid.Empty)
            issues.Add(Issue(ProjectIdRequired, "Project ID is required.", nameof(request.ProjectId)));

        Require(request.EventType, EventTypeRequired, nameof(request.EventType), "Event type is required.", issues);
        Require(request.ActorType, ActorTypeRequired, nameof(request.ActorType), "Actor type is required.", issues);
        Require(request.ActorId, ActorIdRequired, nameof(request.ActorId), "Actor ID is required.", issues);

        if (request.PayloadVersion <= 0)
            issues.Add(Issue(PayloadVersionInvalid, "Payload version must be positive.", nameof(request.PayloadVersion)));

        ValidatePayloadJson(request.PayloadJson, issues);
        return issues;
    }

    public IReadOnlyList<GovernanceEventValidationIssue> ValidateProjectQuery(GovernanceEventsForProjectQuery? query)
    {
        var issues = new List<GovernanceEventValidationIssue>();
        if (query is null)
        {
            issues.Add(Issue(ProjectIdRequired, "Project query is required.", "Query"));
            return issues;
        }

        if (query.ProjectId == Guid.Empty)
            issues.Add(Issue(ProjectIdRequired, "Project ID is required.", nameof(query.ProjectId)));
        ValidateTake(query.Take, issues);
        return issues;
    }

    public IReadOnlyList<GovernanceEventValidationIssue> ValidateCorrelationQuery(GovernanceEventsForCorrelationQuery? query)
    {
        var issues = new List<GovernanceEventValidationIssue>();
        if (query is null)
        {
            issues.Add(Issue(CorrelationIdRequired, "Correlation query is required.", "Query"));
            return issues;
        }

        if (query.CorrelationId == Guid.Empty)
            issues.Add(Issue(CorrelationIdRequired, "Correlation ID is required.", nameof(query.CorrelationId)));
        ValidateTake(query.Take, issues);
        return issues;
    }

    public IReadOnlyList<GovernanceEventValidationIssue> ValidateSubjectQuery(GovernanceEventsForSubjectQuery? query)
    {
        var issues = new List<GovernanceEventValidationIssue>();
        if (query is null)
        {
            issues.Add(Issue(ProjectIdRequired, "Subject query is required.", "Query"));
            return issues;
        }

        if (query.ProjectId == Guid.Empty)
            issues.Add(Issue(ProjectIdRequired, "Project ID is required.", nameof(query.ProjectId)));
        Require(query.SubjectType, SubjectTypeRequired, nameof(query.SubjectType), "Subject type is required.", issues);
        Require(query.SubjectId, SubjectIdRequired, nameof(query.SubjectId), "Subject ID is required.", issues);
        ValidateTake(query.Take, issues);
        return issues;
    }

    public IReadOnlyList<GovernanceEventValidationIssue> ValidateCausedByQuery(GovernanceEventsCausedByQuery? query)
    {
        var issues = new List<GovernanceEventValidationIssue>();
        if (query is null)
        {
            issues.Add(Issue(CausationIdRequired, "Causation query is required.", "Query"));
            return issues;
        }

        if (query.CausationId == Guid.Empty)
            issues.Add(Issue(CausationIdRequired, "Causation ID is required.", nameof(query.CausationId)));
        ValidateTake(query.Take, issues);
        return issues;
    }

    public GovernanceEventAppendRequest Normalize(GovernanceEventAppendRequest request) =>
        request with
        {
            EventType = request.EventType.Trim(),
            ActorType = request.ActorType.Trim(),
            ActorId = request.ActorId.Trim(),
            SubjectType = NormalizeOptional(request.SubjectType),
            SubjectId = NormalizeOptional(request.SubjectId)
        };

    public GovernanceEventsForSubjectQuery Normalize(GovernanceEventsForSubjectQuery query) =>
        query with
        {
            SubjectType = query.SubjectType.Trim(),
            SubjectId = query.SubjectId.Trim()
        };

    public static GovernanceEventValidationIssue Issue(string code, string message, string field = "") =>
        new()
        {
            Code = code,
            Severity = SeverityError,
            Message = message,
            Field = field
        };

    private static void ValidatePayloadJson(string? payloadJson, List<GovernanceEventValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            issues.Add(Issue(PayloadJsonRequired, "Payload JSON is required.", nameof(GovernanceEventAppendRequest.PayloadJson)));
            return;
        }

        try
        {
            using var _ = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException)
        {
            issues.Add(Issue(PayloadJsonInvalid, "Payload JSON must be valid JSON.", nameof(GovernanceEventAppendRequest.PayloadJson)));
        }

        if (ContainsUnsafePayloadText(payloadJson))
            issues.Add(Issue(PayloadTextUnsafe, "Governance event payload must not contain raw prompt, raw output, private reasoning, or large evidence markers.", nameof(GovernanceEventAppendRequest.PayloadJson)));
    }

    private static void ValidateTake(int take, List<GovernanceEventValidationIssue> issues)
    {
        if (take is < 1 or > MaxTake)
            issues.Add(Issue(TakeInvalid, $"Take must be between 1 and {MaxTake}.", "Take"));
    }

    private static void Require(
        string? value,
        string code,
        string field,
        string message,
        List<GovernanceEventValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(Issue(code, message, field));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ContainsUnsafePayloadText(string value)
    {
        var text = value.ToLowerInvariant();
        return UnsafePayloadMarkers.Any(text.Contains);
    }

    private static readonly string[] UnsafePayloadMarkers =
    [
        "raw prompt",
        "rawprompt",
        "fullprompt",
        "raw completion",
        "rawcompletion",
        "fullcompletion",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "scratchpad",
        "private reasoning",
        "privatereasoning",
        "hidden reasoning",
        "hidden deliberation",
        "entirepatch",
        "raw tool output",
        "rawtooloutput"
    ];
}