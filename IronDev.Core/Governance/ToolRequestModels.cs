using System.Text.Json;

namespace IronDev.Core.Governance;

public enum ToolRequestStatus
{
    Recorded = 1,
    Cancelled = 2,
    Superseded = 3
}

public sealed record ToolRequest
{
    public required Guid ToolRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid GovernanceEventId { get; init; }
    public required string ToolName { get; init; }
    public required string OperationName { get; init; }
    public required string RequestedByActorType { get; init; }
    public required string RequestedByActorId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string? Purpose { get; init; }
    public required int RequestPayloadVersion { get; init; }
    public required string RequestPayloadJson { get; init; }
    public required ToolRequestStatus Status { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset? CancelledUtc { get; init; }
    public string? CancelledReason { get; init; }
}

public sealed record ToolRequestCreateRequest
{
    public Guid? ToolRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string ToolName { get; init; }
    public required string OperationName { get; init; }
    public required string RequestedByActorType { get; init; }
    public required string RequestedByActorId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string? Purpose { get; init; }
    public required int RequestPayloadVersion { get; init; }
    public required string RequestPayloadJson { get; init; }
}

public sealed record ToolRequestReadModel
{
    public required Guid ToolRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid GovernanceEventId { get; init; }
    public required string ToolName { get; init; }
    public required string OperationName { get; init; }
    public required string RequestedByActorType { get; init; }
    public required string RequestedByActorId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string? Purpose { get; init; }
    public required int RequestPayloadVersion { get; init; }
    public required string RequestPayloadJson { get; init; }
    public required ToolRequestStatus Status { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset? CancelledUtc { get; init; }
    public string? CancelledReason { get; init; }
}

public sealed record ToolRequestSummary
{
    public required Guid ToolRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid GovernanceEventId { get; init; }
    public required string ToolName { get; init; }
    public required string OperationName { get; init; }
    public required string RequestedByActorType { get; init; }
    public required string RequestedByActorId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required ToolRequestStatus Status { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ToolRequestsForProjectQuery
{
    public required Guid ProjectId { get; init; }
    public int Take { get; init; } = ToolRequestValidator.DefaultTake;
}

public sealed record ToolRequestsForCorrelationQuery
{
    public required Guid CorrelationId { get; init; }
    public int Take { get; init; } = ToolRequestValidator.DefaultTake;
}

public sealed record ToolRequestValidationIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public interface IToolRequestStore
{
    Task<ToolRequestReadModel> CreateAsync(ToolRequestCreateRequest request, CancellationToken cancellationToken = default);

    Task<ToolRequestReadModel?> GetAsync(Guid toolRequestId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToolRequestSummary>> ListForProjectAsync(ToolRequestsForProjectQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ToolRequestSummary>> ListForCorrelationAsync(ToolRequestsForCorrelationQuery query, CancellationToken cancellationToken = default);
}

public sealed class ToolRequestValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    public const int MaxPayloadLength = 32_000;

    public const string ProjectIdRequired = "TOOL_REQUEST_PROJECT_ID_REQUIRED";
    public const string ToolNameRequired = "TOOL_REQUEST_TOOL_NAME_REQUIRED";
    public const string OperationNameRequired = "TOOL_REQUEST_OPERATION_NAME_REQUIRED";
    public const string ActorTypeRequired = "TOOL_REQUEST_ACTOR_TYPE_REQUIRED";
    public const string ActorIdRequired = "TOOL_REQUEST_ACTOR_ID_REQUIRED";
    public const string PayloadVersionInvalid = "TOOL_REQUEST_PAYLOAD_VERSION_INVALID";
    public const string PayloadJsonRequired = "TOOL_REQUEST_PAYLOAD_JSON_REQUIRED";
    public const string PayloadJsonInvalid = "TOOL_REQUEST_PAYLOAD_JSON_INVALID";
    public const string PayloadJsonTooLarge = "TOOL_REQUEST_PAYLOAD_JSON_TOO_LARGE";
    public const string PayloadTextUnsafe = "TOOL_REQUEST_PAYLOAD_TEXT_UNSAFE";
    public const string TakeInvalid = "TOOL_REQUEST_TAKE_INVALID";
    public const string CorrelationIdRequired = "TOOL_REQUEST_CORRELATION_ID_REQUIRED";

    private const string SeverityError = "error";

    public IReadOnlyList<ToolRequestValidationIssue> ValidateCreate(ToolRequestCreateRequest? request)
    {
        var issues = new List<ToolRequestValidationIssue>();
        if (request is null)
        {
            issues.Add(Issue(PayloadJsonRequired, "Tool request create request is required.", "Request"));
            return issues;
        }

        if (request.ProjectId == Guid.Empty)
            issues.Add(Issue(ProjectIdRequired, "Project ID is required.", nameof(request.ProjectId)));

        Require(request.ToolName, ToolNameRequired, nameof(request.ToolName), "Tool name is required.", issues);
        Require(request.OperationName, OperationNameRequired, nameof(request.OperationName), "Operation name is required.", issues);
        Require(request.RequestedByActorType, ActorTypeRequired, nameof(request.RequestedByActorType), "Requested-by actor type is required.", issues);
        Require(request.RequestedByActorId, ActorIdRequired, nameof(request.RequestedByActorId), "Requested-by actor ID is required.", issues);

        if (request.RequestPayloadVersion <= 0)
            issues.Add(Issue(PayloadVersionInvalid, "Request payload version must be positive.", nameof(request.RequestPayloadVersion)));

        ValidatePayloadJson(request.RequestPayloadJson, issues);
        return issues;
    }

    public IReadOnlyList<ToolRequestValidationIssue> ValidateProjectQuery(ToolRequestsForProjectQuery? query)
    {
        var issues = new List<ToolRequestValidationIssue>();
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

    public IReadOnlyList<ToolRequestValidationIssue> ValidateCorrelationQuery(ToolRequestsForCorrelationQuery? query)
    {
        var issues = new List<ToolRequestValidationIssue>();
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

    public ToolRequestCreateRequest Normalize(ToolRequestCreateRequest request) =>
        request with
        {
            ToolName = request.ToolName.Trim(),
            OperationName = request.OperationName.Trim(),
            RequestedByActorType = request.RequestedByActorType.Trim(),
            RequestedByActorId = request.RequestedByActorId.Trim(),
            Purpose = NormalizeOptional(request.Purpose)
        };

    public static ToolRequestValidationIssue Issue(string code, string message, string field = "") =>
        new()
        {
            Code = code,
            Severity = SeverityError,
            Message = message,
            Field = field
        };

    private static void ValidatePayloadJson(string? payloadJson, List<ToolRequestValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            issues.Add(Issue(PayloadJsonRequired, "Request payload JSON is required.", nameof(ToolRequestCreateRequest.RequestPayloadJson)));
            return;
        }

        if (payloadJson.Length > MaxPayloadLength)
            issues.Add(Issue(PayloadJsonTooLarge, $"Request payload JSON must be {MaxPayloadLength} characters or fewer.", nameof(ToolRequestCreateRequest.RequestPayloadJson)));

        try
        {
            using var _ = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException)
        {
            issues.Add(Issue(PayloadJsonInvalid, "Request payload JSON must be valid JSON.", nameof(ToolRequestCreateRequest.RequestPayloadJson)));
        }

        if (ContainsUnsafePayloadText(payloadJson))
            issues.Add(Issue(PayloadTextUnsafe, "Tool request payload string values must not contain raw prompt, raw output, private reasoning, approval, execution, source apply, or memory promotion markers.", nameof(ToolRequestCreateRequest.RequestPayloadJson)));
    }

    private static void ValidateTake(int take, List<ToolRequestValidationIssue> issues)
    {
        if (take is < 1 or > MaxTake)
            issues.Add(Issue(TakeInvalid, $"Take must be between 1 and {MaxTake}.", "Take"));
    }

    private static void Require(
        string? value,
        string code,
        string field,
        string message,
        List<ToolRequestValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(Issue(code, message, field));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool ContainsUnsafePayloadText(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return ContainsUnsafePayloadText(document.RootElement);
        }
        catch (JsonException)
        {
            return UnsafePayloadMarkers.Any(value.ToLowerInvariant().Contains);
        }
    }

    private static bool ContainsUnsafePayloadText(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString()?.ToLowerInvariant() ?? string.Empty;
                return UnsafePayloadMarkers.Any(text.Contains);
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (ContainsUnsafePayloadText(property.Value))
                        return true;
                }
                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (ContainsUnsafePayloadText(item))
                        return true;
                }
                return false;
            default:
                return false;
        }
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
        "approval granted",
        "approved for execution",
        "execution permitted",
        "tool executed",
        "tool ran",
        "gate executed",
        "source applied",
        "apply source",
        "apply patch",
        "memory promoted",
        "promote memory",
        "accepted memory",
        "create pull request",
        "submit github review"
    ];
}
