using System.Security.Claims;
using IronDev.Core.Governance;

namespace IronDev.Infrastructure.Governance;

public sealed class AcceptedApprovalCreateService : IAcceptedApprovalCreateService
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
        "grants execution",
        "execution permitted",
        "execution allowed",
        "approved for execution",
        "policy satisfied",
        "policy cleared",
        "dry-run executed",
        "patch artifact created",
        "source applied",
        "workflow continued",
        "release ready",
        "release approved",
        "ready to ship"
    ];

    private readonly IAcceptedApprovalStore _store;
    private readonly IAcceptedApprovalQueryService _query;

    public AcceptedApprovalCreateService(
        IAcceptedApprovalStore store,
        IAcceptedApprovalQueryService query)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _query = query ?? throw new ArgumentNullException(nameof(query));
    }

    public async Task<AcceptedApprovalCreateResult> CreateAsync(
        Guid projectId,
        CreateAcceptedApprovalRequest? request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<AcceptedApprovalCreateIssue>();

        if (projectId == Guid.Empty)
        {
            Add(issues, "PROJECT_ID_REQUIRED", "projectId", "Project ID is required.");
        }

        if (request is null)
        {
            Add(issues, "REQUEST_REQUIRED", "request", "Accepted approval create request is required.");
            return Invalid(issues);
        }

        var actor = ResolveActor(principal, issues);
        var acceptedAtUtc = DateTimeOffset.UtcNow;

        var approvalTargetKind = NormalizeRequired(issues, request.ApprovalTargetKind, nameof(request.ApprovalTargetKind), MaxShortTextLength);
        var approvalTargetId = NormalizeRequired(issues, request.ApprovalTargetId, nameof(request.ApprovalTargetId), MaxTextLength, requireSafeId: true);
        var approvalTargetHash = NormalizeRequired(issues, request.ApprovalTargetHash, nameof(request.ApprovalTargetHash), MaxTextLength, requireSafeId: true);
        var capabilityCode = NormalizeRequired(issues, request.CapabilityCode, nameof(request.CapabilityCode), MaxShortTextLength, requireSafeId: true);
        var approvalPurpose = NormalizeRequired(issues, request.ApprovalPurpose, nameof(request.ApprovalPurpose), MaxShortTextLength);
        var correlationId = NormalizeRequired(issues, request.CorrelationId, nameof(request.CorrelationId), MaxTextLength, requireSafeId: true);
        var causationId = NormalizeRequired(issues, request.CausationId, nameof(request.CausationId), MaxTextLength, requireSafeId: true);
        var evidenceReferences = NormalizeRequiredList(issues, request.EvidenceReferences, nameof(request.EvidenceReferences), requireSafeId: true);
        var boundaryMaxims = NormalizeRequiredList(issues, request.BoundaryMaxims, nameof(request.BoundaryMaxims), requireSafeId: false);

        if (!string.IsNullOrWhiteSpace(request.ClientRequestId))
        {
            _ = NormalizeRequired(issues, request.ClientRequestId, nameof(request.ClientRequestId), MaxTextLength, requireSafeId: true);
        }

        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc.Value <= acceptedAtUtc)
        {
            Add(issues, "EXPIRES_AT_UTC_INVALID", nameof(request.ExpiresAtUtc), "Expiry timestamp must be after the server accepted timestamp.");
        }

        if (issues.Count > 0 || actor is null)
        {
            return Invalid(issues);
        }

        var record = new AcceptedApprovalRecord
        {
            AcceptedApprovalId = Guid.NewGuid(),
            ProjectId = projectId,
            ApprovalTargetKind = approvalTargetKind!,
            ApprovalTargetId = approvalTargetId!,
            ApprovalTargetHash = approvalTargetHash!,
            CapabilityCode = capabilityCode!,
            ApprovalPurpose = approvalPurpose!,
            ApprovedByActorId = actor.ActorId,
            ApprovedByActorDisplayName = actor.DisplayName,
            AcceptedAtUtc = acceptedAtUtc,
            ExpiresAtUtc = request.ExpiresAtUtc,
            CorrelationId = correlationId!,
            CausationId = causationId!,
            EvidenceReferences = evidenceReferences,
            BoundaryMaxims = boundaryMaxims
        };

        var validation = AcceptedApprovalValidation.Validate(record);
        if (!validation.IsValid)
        {
            return Invalid(validation.Issues
                .Select(issue => new AcceptedApprovalCreateIssue(issue.Code, issue.Field, issue.Message))
                .ToList());
        }

        await _store.SaveAsync(record, cancellationToken);

        var readModel = await _query.GetAsync(record.ProjectId, record.AcceptedApprovalId, cancellationToken);
        if (readModel is null)
        {
            return Invalid([new AcceptedApprovalCreateIssue("ACCEPTED_APPROVAL_READBACK_FAILED", "acceptedApprovalId", "Accepted approval was saved but could not be read back.")]);
        }

        return new AcceptedApprovalCreateResult { AcceptedApproval = readModel };
    }

    private static ActorIdentity? ResolveActor(ClaimsPrincipal principal, List<AcceptedApprovalCreateIssue> issues)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            Add(issues, "AUTHENTICATED_ACTOR_REQUIRED", "actor", "Authenticated actor is required.");
            return null;
        }

        var actorId = FirstClaim(principal, ClaimTypes.NameIdentifier, "sub", ClaimTypes.Email, "email");
        if (string.IsNullOrWhiteSpace(actorId) || actorId == "0")
        {
            Add(issues, "AUTHENTICATED_ACTOR_REQUIRED", "actor", "Authenticated actor could not be resolved.");
            return null;
        }

        var normalizedActorId = NormalizeActorText(issues, actorId, "actor", nameof(AcceptedApprovalRecord.ApprovedByActorId), MaxTextLength, requireSafeId: true);
        var displayName = FirstClaim(principal, "display_name", ClaimTypes.Name, ClaimTypes.Email, "email");
        var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? null
            : NormalizeActorText(issues, displayName, "actor", nameof(AcceptedApprovalRecord.ApprovedByActorDisplayName), MaxTextLength, requireSafeId: false);

        return string.IsNullOrWhiteSpace(normalizedActorId)
            ? null
            : new ActorIdentity(normalizedActorId, normalizedDisplayName);
    }

    private static string? NormalizeRequired(
        List<AcceptedApprovalCreateIssue> issues,
        string? value,
        string field,
        int maxLength,
        bool requireSafeId = false)
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
            // DOGFOOD-2 finding F-L: the refusal names the allowed alphabet — a
            // named refusal the operator cannot act on is only half a refusal.
            Add(issues, "UNSUPPORTED_CHARACTERS", field, "Value contains unsupported characters. Allowed: letters, digits, '-', '_', '.', ':'.");
        }

        return normalized;
    }

    private static string? NormalizeActorText(
        List<AcceptedApprovalCreateIssue> issues,
        string value,
        string category,
        string field,
        int maxLength,
        bool requireSafeId)
    {
        var normalized = value.Trim();
        ValidateTextSafety(issues, field, normalized);
        ValidateLength(issues, field, normalized, maxLength);
        if (requireSafeId && !IsSafeReference(normalized))
        {
            Add(issues, "UNSUPPORTED_ACTOR_CHARACTERS", category, "Authenticated actor contains unsupported characters.");
        }

        return normalized;
    }

    private static IReadOnlyList<string> NormalizeRequiredList(
        List<AcceptedApprovalCreateIssue> issues,
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

    private static void ValidateTextSafety(List<AcceptedApprovalCreateIssue> issues, string field, string value)
    {
        if (ContainsAny(value, PrivateReasoningMarkers))
        {
            Add(issues, "RAW_PRIVATE_REASONING_REJECTED", field, "Accepted approval create API does not accept raw prompt, hidden reasoning, chain-of-thought, scratchpad, system prompt, developer prompt, or private reasoning.");
        }

        if (ContainsAny(value, SensitiveMarkers))
        {
            Add(issues, "SECRET_MATERIAL_REJECTED", field, "Accepted approval create API does not accept secret-like material.");
        }

        if (ContainsAny(value, AuthorityEscalationMarkers))
        {
            Add(issues, "AUTHORITY_ESCALATION_REJECTED", field, "Accepted approval create API does not accept policy satisfaction, execution, source apply, dry-run, patch artifact, workflow continuation, release approval, or release readiness claims.");
        }
    }

    private static void ValidateLength(List<AcceptedApprovalCreateIssue> issues, string field, string value, int maxLength)
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

    private static string? FirstClaim(ClaimsPrincipal principal, params string[] claimTypes) =>
        claimTypes.Select(claimType => principal.FindFirst(claimType)?.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static void Add(List<AcceptedApprovalCreateIssue> issues, string code, string field, string message) =>
        issues.Add(new AcceptedApprovalCreateIssue(code, field, message));

    private static AcceptedApprovalCreateResult Invalid(IReadOnlyList<AcceptedApprovalCreateIssue> issues) =>
        new() { Issues = issues };

    private sealed record ActorIdentity(string ActorId, string? DisplayName);
}
