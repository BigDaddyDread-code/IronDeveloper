using System.Security.Claims;
using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class PolicySatisfactionCreateService : IPolicySatisfactionCreateService
{
    private const int MaxShortTextLength = 128;
    private const int MaxTextLength = 256;
    private const int MaxEvidenceTextLength = 512;

    private static readonly string[] PrivateReasoningMarkers =
    [
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "hidden reasoning",
        "hidden deliberation",
        "private reasoning",
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "scratchpad",
        "system prompt",
        "developer prompt",
        "entire patch",
        "entirepatch"
    ];

    private static readonly string[] SensitiveMarkers =
    [
        "password",
        "api_key",
        "apikey",
        "secret",
        "private key",
        "bearer "
    ];

    private static readonly string[] AuthorityEscalationMarkers =
    [
        "runs dry-run",
        "dry-run executed",
        "creates patch artifact",
        "patch artifact created",
        "applies source",
        "source applied",
        "continues workflow",
        "workflow continued",
        "approves release",
        "release approved",
        "release ready",
        "ready to ship"
    ];

    private readonly IPolicyRequirementSatisfactionEvaluator _evaluator;
    private readonly IPolicySatisfactionStore _store;
    private readonly IPolicySatisfactionQueryService _query;

    public PolicySatisfactionCreateService(
        IPolicyRequirementSatisfactionEvaluator evaluator,
        IPolicySatisfactionStore store,
        IPolicySatisfactionQueryService query)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _query = query ?? throw new ArgumentNullException(nameof(query));
    }

    public async Task<PolicySatisfactionCreateResult> CreateAsync(
        Guid routeProjectId,
        PolicySatisfactionCreateRequest? request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<PolicySatisfactionCreateIssue>();

        if (routeProjectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", "projectId", "Project ID is required.");
        }

        if (principal.Identity?.IsAuthenticated != true)
        {
            Add(issues, "AUTHENTICATED_ACTOR_REQUIRED", "actor", "Authenticated actor is required.");
        }

        if (request is null)
        {
            Add(issues, "REQUEST_REQUIRED", "request", "Policy satisfaction create request is required.");
            return Invalid(issues);
        }

        if (request.PolicyRequirement is null)
        {
            Add(issues, "POLICY_REQUIREMENT_REQUIRED", nameof(request.PolicyRequirement), "Policy requirement is required.");
        }
        else if (request.PolicyRequirement.ProjectId != routeProjectId)
        {
            Add(issues, "PROJECT_ID_MISMATCH", nameof(request.PolicyRequirement.ProjectId), "Policy requirement project must match the route project.");
        }

        var suppliedHash = NormalizeRequired(issues, request.PolicyRequirementHash, nameof(request.PolicyRequirementHash), MaxTextLength, requireSafeId: true);
        var correlationId = NormalizeRequired(issues, request.CorrelationId, nameof(request.CorrelationId), MaxTextLength, requireSafeId: true);
        var causationId = NormalizeRequired(issues, request.CausationId, nameof(request.CausationId), MaxTextLength, requireSafeId: true);
        var evidenceReferences = NormalizeRequiredList(issues, request.EvidenceReferences, nameof(request.EvidenceReferences), requireSafeId: true);
        var boundaryMaxims = NormalizeRequiredList(issues, request.BoundaryMaxims, nameof(request.BoundaryMaxims), requireSafeId: false);

        if (!string.IsNullOrWhiteSpace(request.ClientRequestId))
        {
            _ = NormalizeRequired(issues, request.ClientRequestId, nameof(request.ClientRequestId), MaxTextLength, requireSafeId: true);
        }

        var evaluation = _evaluator.Evaluate(request.PolicyRequirement, request.ApprovalSatisfactionEvaluation);
        foreach (var issue in evaluation.Issues)
        {
            Add(issues, issue.Code, issue.Field, issue.Message);
        }

        if (!evaluation.IsSatisfied)
        {
            Add(issues, "POLICY_REQUIREMENT_NOT_SATISFIED", nameof(evaluation.IsSatisfied), "Policy requirement satisfaction evaluation is not satisfied.");
        }

        if (evaluation.AcceptedApprovalId is null || evaluation.AcceptedApprovalId.Value == Guid.Empty)
        {
            Add(issues, "ACCEPTED_APPROVAL_ID_REQUIRED", nameof(evaluation.AcceptedApprovalId), "Accepted approval ID is required.");
        }

        if (string.IsNullOrWhiteSpace(evaluation.ApprovalRequirementHash))
        {
            Add(issues, "APPROVAL_REQUIREMENT_HASH_REQUIRED", nameof(evaluation.ApprovalRequirementHash), "Approval requirement hash is required.");
        }

        if (string.IsNullOrWhiteSpace(evaluation.PolicyRequirementHash))
        {
            Add(issues, "POLICY_REQUIREMENT_HASH_REQUIRED", nameof(evaluation.PolicyRequirementHash), "Policy requirement hash is required.");
        }
        else if (!string.IsNullOrWhiteSpace(suppliedHash) && !string.Equals(suppliedHash, evaluation.PolicyRequirementHash, StringComparison.Ordinal))
        {
            Add(issues, "POLICY_REQUIREMENT_HASH_MISMATCH", nameof(request.PolicyRequirementHash), "Policy requirement hash does not match the deterministic policy requirement hash.");
        }

        if (issues.Count > 0 || request.PolicyRequirement is null || evaluation.AcceptedApprovalId is null)
        {
            return Invalid(issues, IsConflictIssue(issues));
        }

        var satisfiedAtUtc = DateTimeOffset.UtcNow;
        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc.Value <= satisfiedAtUtc)
        {
            Add(issues, "EXPIRES_AT_UTC_INVALID", nameof(request.ExpiresAtUtc), "Expiry timestamp must be after the server satisfied timestamp.");
            return Invalid(issues);
        }

        var requirement = request.PolicyRequirement;
        var record = new PolicySatisfactionRecord
        {
            PolicySatisfactionId = Guid.NewGuid(),
            ProjectId = routeProjectId,
            PolicyCode = Normalize(requirement.PolicyCode),
            PolicyVersion = Normalize(requirement.PolicyVersion),
            SubjectKind = Normalize(requirement.SubjectKind),
            SubjectId = Normalize(requirement.SubjectId),
            SubjectHash = Normalize(requirement.SubjectHash),
            CapabilityCode = Normalize(requirement.CapabilityCode),
            AcceptedApprovalId = evaluation.AcceptedApprovalId.Value,
            ApprovalRequirementHash = Normalize(evaluation.ApprovalRequirementHash!),
            ApprovalEvaluatedAtUtc = requirement.EvaluatedAtUtc,
            SatisfiedAtUtc = satisfiedAtUtc,
            ExpiresAtUtc = request.ExpiresAtUtc,
            CorrelationId = correlationId!,
            CausationId = causationId!,
            EvidenceReferences = evidenceReferences,
            BoundaryMaxims = boundaryMaxims
        };

        var validation = PolicySatisfactionValidation.Validate(record);
        if (!validation.IsValid)
        {
            return Invalid(validation.Issues
                .Select(issue => new PolicySatisfactionCreateIssue(issue.Code, issue.Field, issue.Message))
                .ToArray());
        }

        try
        {
            await _store.SaveAsync(record, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Invalid([new PolicySatisfactionCreateIssue("POLICY_SATISFACTION_SAVE_REJECTED", "record", exception.Message)], isConflict: true);
        }

        var readModel = await _query.GetAsync(record.ProjectId, record.PolicySatisfactionId, cancellationToken);
        if (readModel is null)
        {
            return Invalid([new PolicySatisfactionCreateIssue("POLICY_SATISFACTION_READBACK_FAILED", nameof(record.PolicySatisfactionId), "Policy satisfaction was saved but could not be read back.")]);
        }

        return new PolicySatisfactionCreateResult { PolicySatisfaction = readModel };
    }

    private static string? NormalizeRequired(
        List<PolicySatisfactionCreateIssue> issues,
        string? value,
        string field,
        int maxLength,
        bool requireSafeId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, "FIELD_REQUIRED", field, "Value is required.");
            return null;
        }

        var normalized = value.Trim();
        ValidateTextSafety(issues, field, normalized);
        ValidateLength(issues, field, normalized, maxLength);

        if (requireSafeId && !IsSafeReference(normalized))
        {
            Add(issues, "UNSUPPORTED_CHARACTERS", field, "Value contains unsupported characters.");
        }

        return normalized;
    }

    private static IReadOnlyList<string> NormalizeRequiredList(
        List<PolicySatisfactionCreateIssue> issues,
        IReadOnlyList<string>? values,
        string field,
        bool requireSafeId)
    {
        if (values is null || values.Count == 0)
        {
            Add(issues, "LIST_REQUIRED", field, "At least one value is required.");
            return [];
        }

        var normalized = new List<string>();
        for (var index = 0; index < values.Count; index++)
        {
            var itemField = $"{field}[{index}]";
            var item = NormalizeRequired(issues, values[index], itemField, MaxEvidenceTextLength, requireSafeId);
            if (!string.IsNullOrWhiteSpace(item))
            {
                normalized.Add(item);
            }
        }

        return normalized;
    }

    private static void ValidateTextSafety(List<PolicySatisfactionCreateIssue> issues, string field, string value)
    {
        if (ContainsAny(value, PrivateReasoningMarkers))
        {
            Add(issues, "RAW_PRIVATE_REASONING_REJECTED", field, "Policy satisfaction create API does not accept raw prompt, hidden reasoning, chain-of-thought, scratchpad, system prompt, developer prompt, or private reasoning.");
        }

        if (ContainsAny(value, SensitiveMarkers))
        {
            Add(issues, "SECRET_MATERIAL_REJECTED", field, "Policy satisfaction create API does not accept secret-like material.");
        }

        if (ContainsAny(value, AuthorityEscalationMarkers))
        {
            Add(issues, "AUTHORITY_ESCALATION_REJECTED", field, "Policy satisfaction create API does not accept dry-run, patch artifact, source apply, workflow continuation, release approval, or release readiness claims.");
        }
    }

    private static void ValidateLength(List<PolicySatisfactionCreateIssue> issues, string field, string value, int maxLength)
    {
        if (value.Length > maxLength)
        {
            Add(issues, "FIELD_TOO_LONG", field, "Value is too long.");
        }
    }

    private static bool IsSafeReference(string value) =>
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static bool IsConflictIssue(IReadOnlyList<PolicySatisfactionCreateIssue> issues) =>
        issues.Any(issue => issue.Code is
            "POLICY_REQUIREMENT_NOT_SATISFIED" or
            "APPROVAL_REQUIREMENT_NOT_SATISFIED" or
            "APPROVAL_EVALUATION_HAS_ISSUES" or
            "ACCEPTED_APPROVAL_ID_REQUIRED" or
            "APPROVAL_REQUIREMENT_HASH_REQUIRED" or
            "POLICY_REQUIREMENT_EXPIRED");

    private static void Add(List<PolicySatisfactionCreateIssue> issues, string code, string field, string message) =>
        issues.Add(new PolicySatisfactionCreateIssue(code, field, message));

    private static PolicySatisfactionCreateResult Invalid(IReadOnlyList<PolicySatisfactionCreateIssue> issues, bool isConflict = false) =>
        new() { Issues = issues, IsConflict = isConflict };
}