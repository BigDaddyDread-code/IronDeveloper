using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace IronDev.Core.Agents;

public enum AgentModelSanitisationStatus
{
    Safe = 1,
    SafeWithRedactions = 2,
    Rejected = 3
}

public enum AgentModelRetainability
{
    None = 0,
    AuditSummaryOnly = 1,
    RedactedPreview = 2,
    StructuredOutputCandidate = 3
}

public enum AgentModelRedactionKind
{
    RawPrompt = 1,
    RawCompletion = 2,
    ChainOfThought = 3,
    Scratchpad = 4,
    PrivateReasoning = 5,
    HiddenReasoning = 6,
    SystemPrompt = 7,
    DeveloperPrompt = 8,
    Secret = 9,
    Credential = 10,
    ApiKey = 11,
    Token = 12,
    AuthorityClaim = 13,
    ToolCommand = 14,
    SourceMutationCommand = 15,
    MemoryPromotionCommand = 16,
    ExternalCallCommand = 17,
    GitHubReviewCommand = 18
}

public sealed record AgentModelRedaction
{
    public required AgentModelRedactionKind Kind { get; init; }
    public required string Reason { get; init; }
    public string Field { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed record AgentModelSanitisationIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public sealed record AgentModelSanitisedPrompt
{
    public required string RequestId { get; init; }
    public required string AgentId { get; init; }
    public string? SpecialisationId { get; init; }
    public required AgentModelRetainability Retainability { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? RedactedPreview { get; init; }
    public IReadOnlyList<string> InputRefs { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> MemoryRefs { get; init; } = [];
    public IReadOnlyList<string> AuditRefs { get; init; } = [];
    public IReadOnlyList<AgentModelRedaction> Redactions { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsPromptLeak { get; init; }
    public bool ContainsAuthorityClaim { get; init; }
    public bool ContainsToolCommand { get; init; }
    public bool ContainsSourceMutationCommand { get; init; }
    public bool ContainsMemoryPromotionCommand { get; init; }
}

public sealed record AgentModelSanitisedResponse
{
    public required string ResponseId { get; init; }
    public required string RequestId { get; init; }
    public required string AgentId { get; init; }
    public string? SpecialisationId { get; init; }
    public required AgentModelRetainability Retainability { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string? RedactedPreview { get; init; }
    public string? StructuredJsonCandidate { get; init; }
    public AgentModelUsage Usage { get; init; } = new();
    public IReadOnlyList<AgentModelRedaction> Redactions { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsPromptLeak { get; init; }
    public bool ContainsAuthorityClaim { get; init; }
    public bool ContainsToolCommand { get; init; }
    public bool ContainsSourceMutationCommand { get; init; }
    public bool ContainsMemoryPromotionCommand { get; init; }
    public bool GrantsAuthority { get; init; }
    public bool GrantsApproval { get; init; }
    public bool GrantsMemoryPromotion { get; init; }
}

public sealed record AgentModelSanitisedInvocationAudit
{
    public required string AuditId { get; init; }
    public required string RequestId { get; init; }
    public required string AgentId { get; init; }
    public string? SpecialisationId { get; init; }
    public required string ProfileId { get; init; }
    public required AgentModelProviderKind ProviderKind { get; init; }
    public required string ModelName { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public bool Succeeded { get; init; }
    public AgentModelUsage Usage { get; init; } = new();
    public AgentModelSanitisationStatus SanitisationStatus { get; init; }
    public IReadOnlyList<AgentModelRedaction> Redactions { get; init; } = [];
    public IReadOnlyList<string> InputRefs { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<string> MemoryRefs { get; init; } = [];
    public IReadOnlyList<string> AuditRefs { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsPromptLeak { get; init; }
    public bool ContainsAuthorityClaim { get; init; }
    public bool ContainsToolCommand { get; init; }
    public bool ContainsSourceMutationCommand { get; init; }
    public bool ContainsMemoryPromotionCommand { get; init; }
    public bool GrantsAuthority { get; init; }
    public bool GrantsApproval { get; init; }
    public bool GrantsMemoryPromotion { get; init; }
}

public sealed record AgentModelSanitisationRequest
{
    public required AgentModelRequest Request { get; init; }
    public AgentModelResponse? Response { get; init; }
    public AgentModelInvocationAudit? Audit { get; init; }
    public bool AllowRedactedPreview { get; init; }
    public bool AllowStructuredJsonCandidate { get; init; }
}

public sealed record AgentModelSanitisationResult
{
    public required AgentModelSanitisationStatus Status { get; init; }
    public AgentModelSanitisedPrompt? Prompt { get; init; }
    public AgentModelSanitisedResponse? Response { get; init; }
    public AgentModelSanitisedInvocationAudit? Audit { get; init; }
    public IReadOnlyList<AgentModelSanitisationIssue> Issues { get; init; } = [];
    public IReadOnlyList<AgentModelRedaction> Redactions { get; init; } = [];
}

public interface IAgentModelAuditSanitiser
{
    AgentModelSanitisationResult Sanitise(AgentModelSanitisationRequest request);
}

public sealed class AgentModelAuditSanitiser : IAgentModelAuditSanitiser
{
    public const string RequestRejectedByValidator = "MODEL_SANITISATION_REQUEST_REJECTED_BY_VALIDATOR";
    public const string ResponseRejectedByValidator = "MODEL_SANITISATION_RESPONSE_REJECTED_BY_VALIDATOR";
    public const string AuditRejectedByValidator = "MODEL_SANITISATION_AUDIT_REJECTED_BY_VALIDATOR";
    public const string UnsafePromptMaterialRejected = "MODEL_SANITISATION_PROMPT_MATERIAL_REJECTED";
    public const string UnsafeResponseMaterialRejected = "MODEL_SANITISATION_RESPONSE_MATERIAL_REJECTED";
    public const string UnsafeAuditMaterialRejected = "MODEL_SANITISATION_AUDIT_MATERIAL_REJECTED";
    public const string SecretMaterialRedacted = "MODEL_SANITISATION_SECRET_REDACTED";

    private static readonly UnsafeMarker[] RawPrivateMarkers =
    [
        new("raw prompt", AgentModelRedactionKind.RawPrompt, "Raw prompt text cannot be retained."),
        new("raw completion", AgentModelRedactionKind.RawCompletion, "Raw completion text cannot be retained."),
        new("chain-of-thought", AgentModelRedactionKind.ChainOfThought, "Chain-of-thought cannot be retained."),
        new("chain of thought", AgentModelRedactionKind.ChainOfThought, "Chain-of-thought cannot be retained."),
        new("scratchpad", AgentModelRedactionKind.Scratchpad, "Scratchpad content cannot be retained."),
        new("scratch pad", AgentModelRedactionKind.Scratchpad, "Scratchpad content cannot be retained."),
        new("private reasoning", AgentModelRedactionKind.PrivateReasoning, "Private reasoning cannot be retained."),
        new("hidden reasoning", AgentModelRedactionKind.HiddenReasoning, "Hidden reasoning cannot be retained."),
        new("hidden deliberation", AgentModelRedactionKind.HiddenReasoning, "Hidden deliberation cannot be retained.")
    ];

    private static readonly UnsafeMarker[] PromptLeakMarkers =
    [
        new("system prompt", AgentModelRedactionKind.SystemPrompt, "System prompt text cannot be retained."),
        new("developer prompt", AgentModelRedactionKind.DeveloperPrompt, "Developer prompt text cannot be retained.")
    ];

    private static readonly UnsafeMarker[] AuthorityMarkers =
    [
        new("approval granted", AgentModelRedactionKind.AuthorityClaim, "Model material cannot grant approval."),
        new("approved for execution", AgentModelRedactionKind.AuthorityClaim, "Model material cannot approve execution."),
        new("policy cleared", AgentModelRedactionKind.AuthorityClaim, "Model material cannot clear policy."),
        new("human approved", AgentModelRedactionKind.AuthorityClaim, "Model material cannot represent human approval."),
        new("authoritative for action", AgentModelRedactionKind.AuthorityClaim, "Model material cannot be authoritative for action."),
        new("may execute", AgentModelRedactionKind.AuthorityClaim, "Model material cannot grant execution authority."),
        new("can execute", AgentModelRedactionKind.AuthorityClaim, "Model material cannot grant execution authority."),
        new("grant authority", AgentModelRedactionKind.AuthorityClaim, "Model material cannot grant authority."),
        new("override policy", AgentModelRedactionKind.AuthorityClaim, "Model material cannot override policy."),
        new("bypass governance", AgentModelRedactionKind.AuthorityClaim, "Model material cannot bypass governance.")
    ];

    private static readonly UnsafeMarker[] ToolMarkers =
    [
        new("run this tool", AgentModelRedactionKind.ToolCommand, "Model material cannot issue tool commands."),
        new("call this tool", AgentModelRedactionKind.ToolCommand, "Model material cannot issue tool commands."),
        new("execute tool", AgentModelRedactionKind.ToolCommand, "Model material cannot issue tool commands.")
    ];

    private static readonly UnsafeMarker[] SourceMutationMarkers =
    [
        new("apply this patch", AgentModelRedactionKind.SourceMutationCommand, "Model material cannot command source mutation."),
        new("mutate source", AgentModelRedactionKind.SourceMutationCommand, "Model material cannot command source mutation."),
        new("write file", AgentModelRedactionKind.SourceMutationCommand, "Model material cannot command file writes."),
        new("delete file", AgentModelRedactionKind.SourceMutationCommand, "Model material cannot command file deletion."),
        new("modify source", AgentModelRedactionKind.SourceMutationCommand, "Model material cannot command source mutation.")
    ];

    private static readonly UnsafeMarker[] MemoryPromotionMarkers =
    [
        new("promote memory", AgentModelRedactionKind.MemoryPromotionCommand, "Model material cannot promote memory."),
        new("accepted memory", AgentModelRedactionKind.MemoryPromotionCommand, "Model material cannot create accepted memory."),
        new("create collectivememory", AgentModelRedactionKind.MemoryPromotionCommand, "Model material cannot create CollectiveMemory."),
        new("persist proposal", AgentModelRedactionKind.MemoryPromotionCommand, "Model material cannot persist proposals.")
    ];

    private static readonly UnsafeMarker[] ExternalMarkers =
    [
        new("submit github review", AgentModelRedactionKind.GitHubReviewCommand, "Model material cannot submit GitHub reviews."),
        new("external call", AgentModelRedactionKind.ExternalCallCommand, "Model material cannot command external calls."),
        new("call external system", AgentModelRedactionKind.ExternalCallCommand, "Model material cannot command external calls.")
    ];

    private static readonly SecretPattern[] SecretPatterns =
    [
        new(new Regex(@"(?i)\b(api_key|apikey|secret|password|token)\s*=\s*[^\s;,)]+", RegexOptions.Compiled), AgentModelRedactionKind.Credential),
        new(new Regex(@"(?i)\bAuthorization\s*:\s*[^\r\n]+", RegexOptions.Compiled), AgentModelRedactionKind.Credential),
        new(new Regex(@"(?i)\bBearer\s+[A-Za-z0-9._\-]+", RegexOptions.Compiled), AgentModelRedactionKind.Token),
        new(new Regex(@"(?i)\bx-api-key\s*[:=]\s*[^\s;,)]+", RegexOptions.Compiled), AgentModelRedactionKind.ApiKey),
        new(new Regex(@"\bsk-[A-Za-z0-9_\-]{6,}", RegexOptions.Compiled), AgentModelRedactionKind.ApiKey),
        new(new Regex(@"\bghp_[A-Za-z0-9_]{6,}", RegexOptions.Compiled), AgentModelRedactionKind.Token)
    ];

    private readonly AgentModelAdapterValidator _validator;

    public AgentModelAuditSanitiser(AgentModelAdapterValidator? validator = null)
    {
        _validator = validator ?? new AgentModelAdapterValidator();
    }

    public AgentModelSanitisationResult Sanitise(AgentModelSanitisationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Request);

        var issues = new List<AgentModelSanitisationIssue>();
        var redactions = new List<AgentModelRedaction>();

        var requestValidationIssues = _validator.ValidateRequest(request.Request);
        AddValidatorIssues(issues, requestValidationIssues, RequestRejectedByValidator);

        var promptScan = SanitiseRequest(request.Request, issues, redactions);

        TextScanResult? responseScan = null;
        if (request.Response is not null)
        {
            var responseValidationIssues = _validator.ValidateResponse(request.Request, request.Response);
            AddValidatorIssues(issues, responseValidationIssues, ResponseRejectedByValidator);
            responseScan = SanitiseResponse(request.Response, request.AllowStructuredJsonCandidate, issues, redactions);
        }

        if (request.Audit is not null)
        {
            var auditValidationIssues = _validator.ValidateAudit(request.Audit);
            AddValidatorIssues(issues, auditValidationIssues, AuditRejectedByValidator);
            SanitiseAuditFlags(request.Audit, issues, redactions);
        }

        var hasErrors = issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));
        if (hasErrors)
        {
            return new AgentModelSanitisationResult
            {
                Status = AgentModelSanitisationStatus.Rejected,
                Prompt = null,
                Response = null,
                Audit = null,
                Issues = issues,
                Redactions = redactions
            };
        }

        var status = redactions.Count > 0
            ? AgentModelSanitisationStatus.SafeWithRedactions
            : AgentModelSanitisationStatus.Safe;

        return new AgentModelSanitisationResult
        {
            Status = status,
            Prompt = BuildPrompt(request.Request, promptScan, request.AllowRedactedPreview),
            Response = request.Response is null || responseScan is null
                ? null
                : BuildResponse(request.Response, responseScan, request.AllowRedactedPreview, request.AllowStructuredJsonCandidate),
            Audit = request.Audit is null
                ? null
                : BuildAudit(request.Audit, status, redactions),
            Issues = issues,
            Redactions = redactions
        };
    }

    private static TextScanResult SanitiseRequest(
        AgentModelRequest request,
        List<AgentModelSanitisationIssue> issues,
        List<AgentModelRedaction> redactions)
    {
        var text = string.Join(
            Environment.NewLine,
            request.Messages.Select(message => $"{message.Role}: {message.Content}"));
        var scan = ScanText(text, nameof(AgentModelRequest.Messages), UnsafePromptMaterialRejected, issues, redactions);

        foreach (var message in request.Messages)
        {
            if (message.Role == AgentModelRole.System)
            {
                AddUnsafe(issues, redactions, UnsafePromptMaterialRejected, nameof(message.Role), AgentModelRedactionKind.SystemPrompt, "System prompt messages cannot be retained.", 1);
                scan = scan with { ContainsPromptLeak = true };
            }

            if (message.Role == AgentModelRole.Developer)
            {
                AddUnsafe(issues, redactions, UnsafePromptMaterialRejected, nameof(message.Role), AgentModelRedactionKind.DeveloperPrompt, "Developer prompt messages cannot be retained.", 1);
                scan = scan with { ContainsPromptLeak = true };
            }

            if (message.ContainsRawPrivateReasoning)
            {
                AddUnsafe(issues, redactions, UnsafePromptMaterialRejected, nameof(message.ContainsRawPrivateReasoning), AgentModelRedactionKind.PrivateReasoning, "Message is flagged as containing raw private reasoning.", 1);
                scan = scan with { ContainsRawPrivateReasoning = true };
            }

            if (message.ContainsSystemPromptLeak || message.ContainsDeveloperPromptLeak)
            {
                AddUnsafe(issues, redactions, UnsafePromptMaterialRejected, nameof(message.ContainsSystemPromptLeak), AgentModelRedactionKind.SystemPrompt, "Message is flagged as leaking prompt text.", 1);
                scan = scan with { ContainsPromptLeak = true };
            }

            if (message.IsAuthoritativeForAction)
            {
                AddUnsafe(issues, redactions, UnsafePromptMaterialRejected, nameof(message.IsAuthoritativeForAction), AgentModelRedactionKind.AuthorityClaim, "Message is flagged as authoritative for action.", 1);
                scan = scan with { ContainsAuthorityClaim = true };
            }
        }

        if (request.Context.IncludesRawPromptOrCompletion || request.Context.IncludesPrivateReasoning)
        {
            AddUnsafe(issues, redactions, UnsafePromptMaterialRejected, nameof(request.Context), AgentModelRedactionKind.PrivateReasoning, "Request context cannot include raw prompt/completion or private reasoning.", 1);
            scan = scan with { ContainsRawPrivateReasoning = true };
        }

        if (request.Context.IncludesAuthoritySource)
        {
            AddUnsafe(issues, redactions, UnsafePromptMaterialRejected, nameof(request.Context.IncludesAuthoritySource), AgentModelRedactionKind.AuthorityClaim, "Request context cannot include an authority source.", 1);
            scan = scan with { ContainsAuthorityClaim = true };
        }

        ApplySafetyFlagRejections(request.SafetyFlags, nameof(request.SafetyFlags), UnsafePromptMaterialRejected, issues, redactions, ref scan);
        return scan;
    }

    private static TextScanResult SanitiseResponse(
        AgentModelResponse response,
        bool allowStructuredJsonCandidate,
        List<AgentModelSanitisationIssue> issues,
        List<AgentModelRedaction> redactions)
    {
        var scan = ScanText(response.Content, nameof(response.Content), UnsafeResponseMaterialRejected, issues, redactions);

        if (!string.IsNullOrWhiteSpace(response.StructuredJson))
        {
            var jsonScan = ScanText(response.StructuredJson, nameof(response.StructuredJson), UnsafeResponseMaterialRejected, issues, redactions);
            scan = scan.Merge(jsonScan);
        }

        if (response.ContainsRawPrivateReasoning)
        {
            AddUnsafe(issues, redactions, UnsafeResponseMaterialRejected, nameof(response.ContainsRawPrivateReasoning), AgentModelRedactionKind.PrivateReasoning, "Response is flagged as containing raw private reasoning.", 1);
            scan = scan with { ContainsRawPrivateReasoning = true };
        }

        if (response.ContainsSystemPromptLeak || response.ContainsDeveloperPromptLeak)
        {
            AddUnsafe(issues, redactions, UnsafeResponseMaterialRejected, nameof(response.ContainsSystemPromptLeak), AgentModelRedactionKind.SystemPrompt, "Response is flagged as leaking prompt text.", 1);
            scan = scan with { ContainsPromptLeak = true };
        }

        if (response.ContainsAuthorityClaim)
        {
            AddUnsafe(issues, redactions, UnsafeResponseMaterialRejected, nameof(response.ContainsAuthorityClaim), AgentModelRedactionKind.AuthorityClaim, "Response is flagged as containing an authority claim.", 1);
            scan = scan with { ContainsAuthorityClaim = true };
        }

        if (response.ContainsToolCommand)
        {
            AddUnsafe(issues, redactions, UnsafeResponseMaterialRejected, nameof(response.ContainsToolCommand), AgentModelRedactionKind.ToolCommand, "Response is flagged as containing a tool command.", 1);
            scan = scan with { ContainsToolCommand = true };
        }

        if (response.ContainsSourceMutationCommand)
        {
            AddUnsafe(issues, redactions, UnsafeResponseMaterialRejected, nameof(response.ContainsSourceMutationCommand), AgentModelRedactionKind.SourceMutationCommand, "Response is flagged as containing a source mutation command.", 1);
            scan = scan with { ContainsSourceMutationCommand = true };
        }

        if (response.ContainsMemoryPromotionCommand)
        {
            AddUnsafe(issues, redactions, UnsafeResponseMaterialRejected, nameof(response.ContainsMemoryPromotionCommand), AgentModelRedactionKind.MemoryPromotionCommand, "Response is flagged as containing a memory promotion command.", 1);
            scan = scan with { ContainsMemoryPromotionCommand = true };
        }

        ApplySafetyFlagRejections(response.ClaimedSafetyFlags, nameof(response.ClaimedSafetyFlags), UnsafeResponseMaterialRejected, issues, redactions, ref scan);

        return allowStructuredJsonCandidate
            ? scan
            : scan with { SanitisedStructuredJson = null };
    }

    private static void SanitiseAuditFlags(
        AgentModelInvocationAudit audit,
        List<AgentModelSanitisationIssue> issues,
        List<AgentModelRedaction> redactions)
    {
        if (audit.ContainsRawPrivateReasoning)
            AddUnsafe(issues, redactions, UnsafeAuditMaterialRejected, nameof(audit.ContainsRawPrivateReasoning), AgentModelRedactionKind.PrivateReasoning, "Invocation audit cannot contain raw private reasoning.", 1);

        if (audit.ContainsPromptLeak)
            AddUnsafe(issues, redactions, UnsafeAuditMaterialRejected, nameof(audit.ContainsPromptLeak), AgentModelRedactionKind.SystemPrompt, "Invocation audit cannot contain prompt leaks.", 1);

        if (audit.ContainsAuthorityClaim || audit.GrantsAuthority || audit.GrantsApproval)
            AddUnsafe(issues, redactions, UnsafeAuditMaterialRejected, nameof(audit.GrantsAuthority), AgentModelRedactionKind.AuthorityClaim, "Invocation audit cannot grant or claim authority.", 1);

        if (audit.ContainsToolCommand)
            AddUnsafe(issues, redactions, UnsafeAuditMaterialRejected, nameof(audit.ContainsToolCommand), AgentModelRedactionKind.ToolCommand, "Invocation audit cannot contain tool commands.", 1);

        if (audit.ContainsSourceMutationCommand)
            AddUnsafe(issues, redactions, UnsafeAuditMaterialRejected, nameof(audit.ContainsSourceMutationCommand), AgentModelRedactionKind.SourceMutationCommand, "Invocation audit cannot contain source mutation commands.", 1);

        if (audit.ContainsMemoryPromotionCommand || audit.GrantsMemoryPromotion)
            AddUnsafe(issues, redactions, UnsafeAuditMaterialRejected, nameof(audit.GrantsMemoryPromotion), AgentModelRedactionKind.MemoryPromotionCommand, "Invocation audit cannot grant or carry memory promotion.", 1);
    }

    private static AgentModelSanitisedPrompt BuildPrompt(
        AgentModelRequest request,
        TextScanResult scan,
        bool allowRedactedPreview) =>
        new()
        {
            RequestId = request.RequestId,
            AgentId = request.AgentId,
            SpecialisationId = request.SpecialisationId,
            Retainability = allowRedactedPreview ? AgentModelRetainability.RedactedPreview : AgentModelRetainability.AuditSummaryOnly,
            Summary = $"Model request for agent '{request.AgentId}' using profile '{request.Profile.ProfileId}'.",
            RedactedPreview = allowRedactedPreview ? Preview(scan.SanitisedText) : null,
            InputRefs = request.Context.InputRefs,
            EvidenceRefs = request.Context.EvidenceRefs.Concat(request.Messages.SelectMany(message => message.EvidenceRefs)).Distinct(StringComparer.Ordinal).ToArray(),
            MemoryRefs = request.Context.MemoryRefs,
            AuditRefs = request.Context.AuditRefs,
            Redactions = scan.Redactions,
            ContainsRawPrivateReasoning = scan.ContainsRawPrivateReasoning,
            ContainsPromptLeak = scan.ContainsPromptLeak,
            ContainsAuthorityClaim = scan.ContainsAuthorityClaim,
            ContainsToolCommand = scan.ContainsToolCommand,
            ContainsSourceMutationCommand = scan.ContainsSourceMutationCommand,
            ContainsMemoryPromotionCommand = scan.ContainsMemoryPromotionCommand
        };

    private static AgentModelSanitisedResponse BuildResponse(
        AgentModelResponse response,
        TextScanResult scan,
        bool allowRedactedPreview,
        bool allowStructuredJsonCandidate) =>
        new()
        {
            ResponseId = response.ResponseId,
            RequestId = response.RequestId,
            AgentId = response.AgentId,
            SpecialisationId = response.SpecialisationId,
            Retainability = allowStructuredJsonCandidate && !string.IsNullOrWhiteSpace(scan.SanitisedStructuredJson)
                ? AgentModelRetainability.StructuredOutputCandidate
                : allowRedactedPreview
                    ? AgentModelRetainability.RedactedPreview
                    : AgentModelRetainability.AuditSummaryOnly,
            Summary = $"Model response '{response.ResponseId}' for request '{response.RequestId}' was reduced to safe retention metadata.",
            RedactedPreview = allowRedactedPreview ? Preview(scan.SanitisedText) : null,
            StructuredJsonCandidate = allowStructuredJsonCandidate ? scan.SanitisedStructuredJson : null,
            Usage = response.Usage,
            Redactions = scan.Redactions,
            ContainsRawPrivateReasoning = scan.ContainsRawPrivateReasoning,
            ContainsPromptLeak = scan.ContainsPromptLeak,
            ContainsAuthorityClaim = scan.ContainsAuthorityClaim,
            ContainsToolCommand = scan.ContainsToolCommand,
            ContainsSourceMutationCommand = scan.ContainsSourceMutationCommand,
            ContainsMemoryPromotionCommand = scan.ContainsMemoryPromotionCommand,
            GrantsAuthority = false,
            GrantsApproval = false,
            GrantsMemoryPromotion = false
        };

    private static AgentModelSanitisedInvocationAudit BuildAudit(
        AgentModelInvocationAudit audit,
        AgentModelSanitisationStatus status,
        IReadOnlyList<AgentModelRedaction> redactions) =>
        new()
        {
            AuditId = audit.AuditId,
            RequestId = audit.RequestId,
            AgentId = audit.AgentId,
            SpecialisationId = audit.SpecialisationId,
            ProfileId = audit.ProfileId,
            ProviderKind = audit.ProviderKind,
            ModelName = audit.ModelName,
            RequestedAtUtc = audit.RequestedAtUtc,
            CompletedAtUtc = audit.CompletedAtUtc,
            Succeeded = audit.Succeeded,
            Usage = audit.Usage,
            SanitisationStatus = status,
            Redactions = redactions,
            InputRefs = audit.InputRefs,
            EvidenceRefs = audit.EvidenceRefs,
            MemoryRefs = [],
            AuditRefs = [],
            ContainsRawPrivateReasoning = false,
            ContainsPromptLeak = false,
            ContainsAuthorityClaim = false,
            ContainsToolCommand = false,
            ContainsSourceMutationCommand = false,
            ContainsMemoryPromotionCommand = false,
            GrantsAuthority = false,
            GrantsApproval = false,
            GrantsMemoryPromotion = false
        };

    private static TextScanResult ScanText(
        string? value,
        string field,
        string issueCode,
        List<AgentModelSanitisationIssue> issues,
        List<AgentModelRedaction> redactions)
    {
        var sanitised = value ?? string.Empty;
        var localRedactions = new List<AgentModelRedaction>();
        var result = new TextScanResult(sanitised);

        foreach (var marker in RawPrivateMarkers)
            result = result.Merge(ScanUnsafeMarker(sanitised, field, issueCode, marker, issues, redactions, containsRawPrivateReasoning: true));

        foreach (var marker in PromptLeakMarkers)
            result = result.Merge(ScanUnsafeMarker(sanitised, field, issueCode, marker, issues, redactions, containsPromptLeak: true));

        foreach (var marker in AuthorityMarkers)
            result = result.Merge(ScanUnsafeMarker(sanitised, field, issueCode, marker, issues, redactions, containsAuthorityClaim: true));

        foreach (var marker in ToolMarkers)
            result = result.Merge(ScanUnsafeMarker(sanitised, field, issueCode, marker, issues, redactions, containsToolCommand: true));

        foreach (var marker in SourceMutationMarkers)
            result = result.Merge(ScanUnsafeMarker(sanitised, field, issueCode, marker, issues, redactions, containsSourceMutationCommand: true));

        foreach (var marker in MemoryPromotionMarkers)
            result = result.Merge(ScanUnsafeMarker(sanitised, field, issueCode, marker, issues, redactions, containsMemoryPromotionCommand: true));

        foreach (var marker in ExternalMarkers)
            result = result.Merge(ScanUnsafeMarker(sanitised, field, issueCode, marker, issues, redactions));

        foreach (var pattern in SecretPatterns)
        {
            var matches = pattern.Regex.Matches(sanitised);
            if (matches.Count == 0)
                continue;

            sanitised = pattern.Regex.Replace(sanitised, "[REDACTED]");
            var redaction = new AgentModelRedaction
            {
                Kind = pattern.Kind,
                Reason = "Credential-like material was redacted.",
                Field = field,
                Count = matches.Count
            };
            localRedactions.Add(redaction);
            redactions.Add(redaction);
            issues.Add(new AgentModelSanitisationIssue
            {
                Code = SecretMaterialRedacted,
                Severity = "warning",
                Message = "Credential-like material was redacted before retention.",
                Field = field
            });
        }

        return result with
        {
            SanitisedText = sanitised,
            SanitisedStructuredJson = field == nameof(AgentModelResponse.StructuredJson) ? sanitised : result.SanitisedStructuredJson,
            Redactions = result.Redactions.Concat(localRedactions).ToArray()
        };
    }

    private static TextScanResult ScanUnsafeMarker(
        string value,
        string field,
        string issueCode,
        UnsafeMarker marker,
        List<AgentModelSanitisationIssue> issues,
        List<AgentModelRedaction> redactions,
        bool containsRawPrivateReasoning = false,
        bool containsPromptLeak = false,
        bool containsAuthorityClaim = false,
        bool containsToolCommand = false,
        bool containsSourceMutationCommand = false,
        bool containsMemoryPromotionCommand = false)
    {
        var count = CountMarker(value, marker.Text);
        if (count == 0)
            return new TextScanResult(value);

        AddUnsafe(issues, redactions, issueCode, field, marker.Kind, marker.Reason, count);
        return new TextScanResult(value)
        {
            Redactions =
            [
                new AgentModelRedaction
                {
                    Kind = marker.Kind,
                    Reason = marker.Reason,
                    Field = field,
                    Count = count
                }
            ],
            ContainsRawPrivateReasoning = containsRawPrivateReasoning,
            ContainsPromptLeak = containsPromptLeak,
            ContainsAuthorityClaim = containsAuthorityClaim,
            ContainsToolCommand = containsToolCommand,
            ContainsSourceMutationCommand = containsSourceMutationCommand,
            ContainsMemoryPromotionCommand = containsMemoryPromotionCommand
        };
    }

    private static void ApplySafetyFlagRejections(
        AgentModelSafetyFlags flags,
        string field,
        string issueCode,
        List<AgentModelSanitisationIssue> issues,
        List<AgentModelRedaction> redactions,
        ref TextScanResult scan)
    {
        if (flags.MayGrantApproval || flags.MayGrantPolicyApproval || flags.MayRepresentHumanApproval)
        {
            AddUnsafe(issues, redactions, issueCode, field, AgentModelRedactionKind.AuthorityClaim, "Safety flags cannot grant or represent approval.", 1);
            scan = scan with { ContainsAuthorityClaim = true };
        }

        if (flags.MayRunTools)
        {
            AddUnsafe(issues, redactions, issueCode, field, AgentModelRedactionKind.ToolCommand, "Safety flags cannot allow tool execution.", 1);
            scan = scan with { ContainsToolCommand = true };
        }

        if (flags.MayMutateSource)
        {
            AddUnsafe(issues, redactions, issueCode, field, AgentModelRedactionKind.SourceMutationCommand, "Safety flags cannot allow source mutation.", 1);
            scan = scan with { ContainsSourceMutationCommand = true };
        }

        if (flags.MayCallExternalSystems || flags.MaySubmitGitHubReview)
            AddUnsafe(issues, redactions, issueCode, field, AgentModelRedactionKind.ExternalCallCommand, "Safety flags cannot allow external calls or GitHub reviews.", 1);

        if (flags.MayPromoteMemory || flags.MayCreateCollectiveMemory || flags.MayPersistProposal)
        {
            AddUnsafe(issues, redactions, issueCode, field, AgentModelRedactionKind.MemoryPromotionCommand, "Safety flags cannot allow memory promotion, CollectiveMemory creation, or proposal persistence.", 1);
            scan = scan with { ContainsMemoryPromotionCommand = true };
        }
    }

    private static void AddValidatorIssues(
        List<AgentModelSanitisationIssue> issues,
        IReadOnlyList<AgentModelAdapterIssue> adapterIssues,
        string code)
    {
        foreach (var issue in adapterIssues.Where(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(new AgentModelSanitisationIssue
            {
                Code = code,
                Severity = "error",
                Message = $"Adapter validator rejected model material: {issue.Code}.",
                Field = issue.Field ?? string.Empty
            });
        }
    }

    private static void AddUnsafe(
        List<AgentModelSanitisationIssue> issues,
        List<AgentModelRedaction> redactions,
        string code,
        string field,
        AgentModelRedactionKind kind,
        string reason,
        int count)
    {
        issues.Add(new AgentModelSanitisationIssue
        {
            Code = code,
            Severity = "error",
            Message = reason,
            Field = field
        });
        redactions.Add(new AgentModelRedaction
        {
            Kind = kind,
            Reason = reason,
            Field = field,
            Count = Math.Max(1, count)
        });
    }

    private static int CountMarker(string value, string marker)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var count = 0;
        var startIndex = 0;
        while (startIndex < value.Length)
        {
            var index = value.IndexOf(marker, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                break;

            count++;
            startIndex = index + marker.Length;
        }

        return count;
    }

    private static string Preview(string value)
    {
        const int maxLength = 240;
        if (value.Length <= maxLength)
            return value;

        return string.Concat(value.AsSpan(0, maxLength), "...");
    }

    private sealed record UnsafeMarker(
        string Text,
        AgentModelRedactionKind Kind,
        string Reason);

    private sealed record SecretPattern(
        Regex Regex,
        AgentModelRedactionKind Kind);

    private sealed record TextScanResult(string SanitisedText)
    {
        public string? SanitisedStructuredJson { get; init; }
        public IReadOnlyList<AgentModelRedaction> Redactions { get; init; } = [];
        public bool ContainsRawPrivateReasoning { get; init; }
        public bool ContainsPromptLeak { get; init; }
        public bool ContainsAuthorityClaim { get; init; }
        public bool ContainsToolCommand { get; init; }
        public bool ContainsSourceMutationCommand { get; init; }
        public bool ContainsMemoryPromotionCommand { get; init; }

        public TextScanResult Merge(TextScanResult other) =>
            this with
            {
                SanitisedText = other.SanitisedText,
                SanitisedStructuredJson = other.SanitisedStructuredJson ?? SanitisedStructuredJson,
                Redactions = Redactions.Concat(other.Redactions).ToArray(),
                ContainsRawPrivateReasoning = ContainsRawPrivateReasoning || other.ContainsRawPrivateReasoning,
                ContainsPromptLeak = ContainsPromptLeak || other.ContainsPromptLeak,
                ContainsAuthorityClaim = ContainsAuthorityClaim || other.ContainsAuthorityClaim,
                ContainsToolCommand = ContainsToolCommand || other.ContainsToolCommand,
                ContainsSourceMutationCommand = ContainsSourceMutationCommand || other.ContainsSourceMutationCommand,
                ContainsMemoryPromotionCommand = ContainsMemoryPromotionCommand || other.ContainsMemoryPromotionCommand
            };
    }
}
