using System.Text.Json;

namespace IronDev.Core.Governance;

public enum DogfoodReceiptOutcome
{
    Passed = 1,
    Failed = 2,
    Partial = 3,
    Inconclusive = 4,
    NotRun = 5
}

public sealed record DogfoodReceipt
{
    public required Guid DogfoodReceiptId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid GovernanceEventId { get; init; }
    public required string ReceiptType { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string Outcome { get; init; }
    public required string SummaryCode { get; init; }
    public string? Summary { get; init; }
    public required string RecordedByActorType { get; init; }
    public required string RecordedByActorId { get; init; }
    public Guid? RelatedToolRequestId { get; init; }
    public Guid? RelatedToolGateDecisionId { get; init; }
    public Guid? RelatedApprovalDecisionId { get; init; }
    public Guid? RelatedPolicyDecisionEventId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required int EvidenceVersion { get; init; }
    public required string EvidenceJson { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record DogfoodReceiptRecordRequest
{
    public Guid? DogfoodReceiptId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string ReceiptType { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string Outcome { get; init; }
    public required string SummaryCode { get; init; }
    public string? Summary { get; init; }
    public required string RecordedByActorType { get; init; }
    public required string RecordedByActorId { get; init; }
    public Guid? RelatedToolRequestId { get; init; }
    public Guid? RelatedToolGateDecisionId { get; init; }
    public Guid? RelatedApprovalDecisionId { get; init; }
    public Guid? RelatedPolicyDecisionEventId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required int EvidenceVersion { get; init; }
    public required string EvidenceJson { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public DateTimeOffset? CreatedUtc { get; init; }
}

public sealed record DogfoodReceiptReadModel
{
    public required Guid DogfoodReceiptId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid GovernanceEventId { get; init; }
    public required string ReceiptType { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string Outcome { get; init; }
    public required string SummaryCode { get; init; }
    public string? Summary { get; init; }
    public required string RecordedByActorType { get; init; }
    public required string RecordedByActorId { get; init; }
    public Guid? RelatedToolRequestId { get; init; }
    public Guid? RelatedToolGateDecisionId { get; init; }
    public Guid? RelatedApprovalDecisionId { get; init; }
    public Guid? RelatedPolicyDecisionEventId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required int EvidenceVersion { get; init; }
    public required string EvidenceJson { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record DogfoodReceiptSummary
{
    public required Guid DogfoodReceiptId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid GovernanceEventId { get; init; }
    public required string ReceiptType { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string Outcome { get; init; }
    public required string SummaryCode { get; init; }
    public required string RecordedByActorType { get; init; }
    public required string RecordedByActorId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record DogfoodReceiptsForSubjectQuery
{
    public required Guid ProjectId { get; init; }
    public required string ReceiptType { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public int Take { get; init; } = DogfoodReceiptValidator.DefaultTake;
}

public sealed record DogfoodReceiptsForProjectQuery
{
    public required Guid ProjectId { get; init; }
    public int Take { get; init; } = DogfoodReceiptValidator.DefaultTake;
}

public sealed record DogfoodReceiptsForCorrelationQuery
{
    public required Guid ProjectId { get; init; }
    public required Guid CorrelationId { get; init; }
    public int Take { get; init; } = DogfoodReceiptValidator.DefaultTake;
}

public sealed record DogfoodReceiptValidationIssue(string Code, string Message, string Field = "");

public sealed record DogfoodReceiptValidationResult(IReadOnlyList<DogfoodReceiptValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

public interface IDogfoodReceiptStore
{
    Task<DogfoodReceiptReadModel> RecordAsync(DogfoodReceiptRecordRequest request, CancellationToken cancellationToken = default);

    Task<DogfoodReceiptReadModel?> GetAsync(Guid dogfoodReceiptId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DogfoodReceiptSummary>> ListForSubjectAsync(DogfoodReceiptsForSubjectQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DogfoodReceiptSummary>> ListForProjectAsync(DogfoodReceiptsForProjectQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DogfoodReceiptSummary>> ListForCorrelationAsync(DogfoodReceiptsForCorrelationQuery query, CancellationToken cancellationToken = default);
}

public sealed class DogfoodReceiptValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    public const int MaxEvidenceJsonLength = 64_000;

    private static readonly string[] ValidOutcomes = Enum.GetNames<DogfoodReceiptOutcome>();

    private static readonly string[] ForbiddenOutcomeNames =
    [
        "Approved",
        "ReleaseApproved",
        "ReadyToRelease",
        "ReleaseReady",
        "Authorized",
        "Accepted",
        "Promoted",
        "Certified",
        "CanShip"
    ];

    private static readonly string[] PrivateReasoningMarkers =
    [
        "raw prompt",
        "raw_prompt",
        "rawprompt",
        "raw completion",
        "raw_completion",
        "rawcompletion",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "scratchpad",
        "private reasoning",
        "privatereasoning",
        "hidden reasoning",
        "hiddenreasoning",
        "system prompt",
        "developer prompt"
    ];

    private static readonly string[] OversizedEvidenceMarkers =
    [
        "raw tool output",
        "raw_tool_output",
        "rawtooloutput",
        "entire patch",
        "entire_patch",
        "entirepatch"
    ];

    public DogfoodReceiptValidationResult ValidateRecord(DogfoodReceiptRecordRequest? request)
    {
        var issues = new List<DogfoodReceiptValidationIssue>();
        if (request is null)
        {
            issues.Add(new DogfoodReceiptValidationIssue("DOGFOOD_RECEIPT_REQUEST_REQUIRED", "Dogfood receipt record request is required."));
            return new DogfoodReceiptValidationResult(issues);
        }

        if (request.ProjectId == Guid.Empty)
            issues.Add(new DogfoodReceiptValidationIssue("PROJECT_REQUIRED", "ProjectId is required.", nameof(request.ProjectId)));

        Require(request.ReceiptType, "RECEIPT_TYPE_REQUIRED", "ReceiptType is required.", nameof(request.ReceiptType), issues);
        Require(request.SubjectType, "SUBJECT_TYPE_REQUIRED", "SubjectType is required.", nameof(request.SubjectType), issues);
        Require(request.SubjectId, "SUBJECT_ID_REQUIRED", "SubjectId is required.", nameof(request.SubjectId), issues);
        Require(request.SummaryCode, "SUMMARY_CODE_REQUIRED", "SummaryCode is required.", nameof(request.SummaryCode), issues);
        Require(request.RecordedByActorType, "ACTOR_TYPE_REQUIRED", "RecordedByActorType is required.", nameof(request.RecordedByActorType), issues);
        Require(request.RecordedByActorId, "ACTOR_ID_REQUIRED", "RecordedByActorId is required.", nameof(request.RecordedByActorId), issues);

        if (string.IsNullOrWhiteSpace(request.Outcome) || !ValidOutcomes.Any(value => string.Equals(value, request.Outcome, StringComparison.OrdinalIgnoreCase)))
            issues.Add(new DogfoodReceiptValidationIssue("OUTCOME_INVALID", "Outcome must be Passed, Failed, Partial, Inconclusive, or NotRun.", nameof(request.Outcome)));

        if (ContainsForbiddenOutcomeName(request.Outcome))
            issues.Add(new DogfoodReceiptValidationIssue("OUTCOME_AUTHORITY_LANGUAGE_FORBIDDEN", "Outcome must not use approval, authorization, release readiness, shipping, acceptance, certification, or promotion language.", nameof(request.Outcome)));

        if (request.EvidenceVersion <= 0)
            issues.Add(new DogfoodReceiptValidationIssue("EVIDENCE_VERSION_INVALID", "EvidenceVersion must be positive.", nameof(request.EvidenceVersion)));

        ValidatePrivateText(request.ReceiptType, "RECEIPT_TYPE_UNSAFE", nameof(request.ReceiptType), issues);
        ValidatePrivateText(request.SubjectType, "SUBJECT_TYPE_UNSAFE", nameof(request.SubjectType), issues);
        ValidatePrivateText(request.SubjectId, "SUBJECT_ID_UNSAFE", nameof(request.SubjectId), issues);
        ValidatePrivateText(request.SummaryCode, "SUMMARY_CODE_UNSAFE", nameof(request.SummaryCode), issues);
        ValidatePrivateText(request.Summary, "SUMMARY_UNSAFE", nameof(request.Summary), issues);
        ValidateEvidenceJson(request.EvidenceJson, issues);

        return new DogfoodReceiptValidationResult(issues);
    }

    public DogfoodReceiptValidationResult ValidateSubjectQuery(DogfoodReceiptsForSubjectQuery query)
    {
        var issues = new List<DogfoodReceiptValidationIssue>();
        ValidateProject(query.ProjectId, issues);
        Require(query.ReceiptType, "RECEIPT_TYPE_REQUIRED", "ReceiptType is required.", nameof(query.ReceiptType), issues);
        Require(query.SubjectType, "SUBJECT_TYPE_REQUIRED", "SubjectType is required.", nameof(query.SubjectType), issues);
        Require(query.SubjectId, "SUBJECT_ID_REQUIRED", "SubjectId is required.", nameof(query.SubjectId), issues);
        ValidateTake(query.Take, issues);
        return new DogfoodReceiptValidationResult(issues);
    }

    public DogfoodReceiptValidationResult ValidateProjectQuery(DogfoodReceiptsForProjectQuery query)
    {
        var issues = new List<DogfoodReceiptValidationIssue>();
        ValidateProject(query.ProjectId, issues);
        ValidateTake(query.Take, issues);
        return new DogfoodReceiptValidationResult(issues);
    }

    public DogfoodReceiptValidationResult ValidateCorrelationQuery(DogfoodReceiptsForCorrelationQuery query)
    {
        var issues = new List<DogfoodReceiptValidationIssue>();
        ValidateProject(query.ProjectId, issues);
        if (query.CorrelationId == Guid.Empty)
            issues.Add(new DogfoodReceiptValidationIssue("CORRELATION_REQUIRED", "CorrelationId is required.", nameof(query.CorrelationId)));
        ValidateTake(query.Take, issues);
        return new DogfoodReceiptValidationResult(issues);
    }

    public static int NormalizeTake(int take) => Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);

    public static string NormalizeOutcome(string outcome) =>
        ValidOutcomes.FirstOrDefault(value => string.Equals(value, outcome, StringComparison.OrdinalIgnoreCase)) ?? NormalizeText(outcome);

    public static string NormalizeText(string value) => value.Trim();

    private static void ValidateProject(Guid projectId, List<DogfoodReceiptValidationIssue> issues)
    {
        if (projectId == Guid.Empty)
            issues.Add(new DogfoodReceiptValidationIssue("PROJECT_REQUIRED", "ProjectId is required.", nameof(projectId)));
    }

    private static void Require(string? value, string code, string message, string field, List<DogfoodReceiptValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(new DogfoodReceiptValidationIssue(code, message, field));
    }

    private static void ValidateTake(int take, List<DogfoodReceiptValidationIssue> issues)
    {
        if (take < 0)
            issues.Add(new DogfoodReceiptValidationIssue("TAKE_INVALID", "Take must not be negative.", "Take"));
    }

    private static void ValidatePrivateText(string? value, string code, string field, List<DogfoodReceiptValidationIssue> issues)
    {
        if (ContainsAny(value, PrivateReasoningMarkers))
            issues.Add(new DogfoodReceiptValidationIssue(code, "Text must not contain raw prompt, raw completion, hidden reasoning, chain-of-thought, scratchpad, system prompt, developer prompt, or private reasoning markers.", field));
    }

    private static void ValidateEvidenceJson(string? evidenceJson, List<DogfoodReceiptValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
        {
            issues.Add(new DogfoodReceiptValidationIssue("EVIDENCE_REQUIRED", "EvidenceJson is required.", nameof(DogfoodReceiptRecordRequest.EvidenceJson)));
            return;
        }

        if (evidenceJson.Length > MaxEvidenceJsonLength)
            issues.Add(new DogfoodReceiptValidationIssue("EVIDENCE_TOO_LARGE", "EvidenceJson exceeds the maximum allowed length.", nameof(DogfoodReceiptRecordRequest.EvidenceJson)));

        if (ContainsAny(evidenceJson, PrivateReasoningMarkers))
            issues.Add(new DogfoodReceiptValidationIssue("EVIDENCE_PRIVATE_REASONING_FORBIDDEN", "EvidenceJson must not contain raw prompt, raw completion, hidden reasoning, chain-of-thought, scratchpad, system prompt, developer prompt, or private reasoning markers.", nameof(DogfoodReceiptRecordRequest.EvidenceJson)));

        if (ContainsAny(evidenceJson, OversizedEvidenceMarkers))
            issues.Add(new DogfoodReceiptValidationIssue("EVIDENCE_RAW_OUTPUT_FORBIDDEN", "EvidenceJson must reference large logs or patches instead of storing raw tool output or entire patches.", nameof(DogfoodReceiptRecordRequest.EvidenceJson)));

        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new DogfoodReceiptValidationIssue("EVIDENCE_OBJECT_REQUIRED", "EvidenceJson must be a JSON object.", nameof(DogfoodReceiptRecordRequest.EvidenceJson)));
                return;
            }

            var hasSchema = document.RootElement.TryGetProperty("schema", out var schema)
                && schema.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(schema.GetString());
            var hasSchemaVersion = document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion)
                && schemaVersion.ValueKind == JsonValueKind.Number
                && schemaVersion.GetInt32() > 0;

            if (!hasSchema && !hasSchemaVersion)
                issues.Add(new DogfoodReceiptValidationIssue("EVIDENCE_SCHEMA_REQUIRED", "EvidenceJson must include schema or positive schemaVersion.", nameof(DogfoodReceiptRecordRequest.EvidenceJson)));

            RejectTruthy(document.RootElement, issues, "approvesRelease", "EVIDENCE_RELEASE_APPROVAL_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "releaseApproved", "EVIDENCE_RELEASE_APPROVAL_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "releaseReady", "EVIDENCE_RELEASE_READY_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "grantsApproval", "EVIDENCE_APPROVAL_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "approvalGranted", "EVIDENCE_APPROVAL_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "grantsExecution", "EVIDENCE_EXECUTION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "executionAllowed", "EVIDENCE_EXECUTION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "mutatesSource", "EVIDENCE_SOURCE_MUTATION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "sourceApplied", "EVIDENCE_SOURCE_MUTATION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "promotesMemory", "EVIDENCE_MEMORY_PROMOTION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "memoryPromoted", "EVIDENCE_MEMORY_PROMOTION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "startsWorkflow", "EVIDENCE_WORKFLOW_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "workflowStarted", "EVIDENCE_WORKFLOW_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "satisfiesPolicy", "EVIDENCE_POLICY_SATISFACTION_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "transfersAuthority", "EVIDENCE_AUTHORITY_TRANSFER_FORBIDDEN");
            RejectTruthy(document.RootElement, issues, "containsRawPrivateReasoning", "EVIDENCE_PRIVATE_REASONING_FORBIDDEN");
        }
        catch (JsonException)
        {
            issues.Add(new DogfoodReceiptValidationIssue("EVIDENCE_JSON_INVALID", "EvidenceJson must be valid JSON.", nameof(DogfoodReceiptRecordRequest.EvidenceJson)));
        }
    }

    private static void RejectTruthy(JsonElement element, List<DogfoodReceiptValidationIssue> issues, string propertyName, string code)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True)
            issues.Add(new DogfoodReceiptValidationIssue(code, $"EvidenceJson must not set {propertyName} to true.", nameof(DogfoodReceiptRecordRequest.EvidenceJson)));
    }

    private static bool ContainsForbiddenOutcomeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return ForbiddenOutcomeNames.Any(forbidden => string.Equals(forbidden, value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(string? value, IReadOnlyList<string> markers) =>
        !string.IsNullOrWhiteSpace(value) && markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
