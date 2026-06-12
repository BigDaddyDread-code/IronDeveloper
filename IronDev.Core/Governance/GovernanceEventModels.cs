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

    Task<GovernanceEvent?> GetAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GovernanceEvent>> ListForProjectAsync(Guid projectId, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GovernanceEvent>> ListForCorrelationAsync(Guid correlationId, int take, CancellationToken cancellationToken = default);
}

public sealed class GovernanceEventValidator
{
    public const string ProjectIdRequired = "GOVERNANCE_EVENT_PROJECT_ID_REQUIRED";
    public const string EventTypeRequired = "GOVERNANCE_EVENT_TYPE_REQUIRED";
    public const string ActorTypeRequired = "GOVERNANCE_EVENT_ACTOR_TYPE_REQUIRED";
    public const string ActorIdRequired = "GOVERNANCE_EVENT_ACTOR_ID_REQUIRED";
    public const string PayloadVersionInvalid = "GOVERNANCE_EVENT_PAYLOAD_VERSION_INVALID";
    public const string PayloadJsonRequired = "GOVERNANCE_EVENT_PAYLOAD_JSON_REQUIRED";
    public const string PayloadJsonInvalid = "GOVERNANCE_EVENT_PAYLOAD_JSON_INVALID";
    public const string PayloadTextUnsafe = "GOVERNANCE_EVENT_PAYLOAD_TEXT_UNSAFE";

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

        if (string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            issues.Add(Issue(PayloadJsonRequired, "Payload JSON is required.", nameof(request.PayloadJson)));
        }
        else
        {
            try
            {
                using var _ = JsonDocument.Parse(request.PayloadJson);
            }
            catch (JsonException)
            {
                issues.Add(Issue(PayloadJsonInvalid, "Payload JSON must be valid JSON.", nameof(request.PayloadJson)));
            }

            if (ContainsUnsafePayloadText(request.PayloadJson))
                issues.Add(Issue(PayloadTextUnsafe, "Governance event payload must not contain raw prompt, raw output, private reasoning, or large evidence markers.", nameof(request.PayloadJson)));
        }

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

    public static GovernanceEventValidationIssue Issue(string code, string message, string field = "") =>
        new()
        {
            Code = code,
            Severity = SeverityError,
            Message = message,
            Field = field
        };

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
