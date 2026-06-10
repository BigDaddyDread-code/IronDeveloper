using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using IronDev.Core.Agents.Audit;

using AuditAgentRunStatus = IronDev.Core.Agents.Audit.AgentRunStatus;
using AuditThoughtLedgerEntry = IronDev.Core.Agents.Audit.ThoughtLedgerEntry;

namespace IronDev.Core.Agents.Concrete;

public sealed record ModelBackedMemoryImprovementDetectionRequest
{
    public required string DetectionRequestId { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public required string CampaignId { get; init; }
    public required string RunId { get; init; }
    public required string RequestedByUserId { get; init; }
    public string? CorrelationId { get; init; }
    public required string SpecialisationId { get; init; }
    public required AgentModelProfile ModelProfile { get; init; }
    public required string RequestSummary { get; init; }
    public IReadOnlyList<ManualMemoryImprovementInputRef> Inputs { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public required AgentModelResponseFormat ResponseFormat { get; init; }
}

public sealed record ModelBackedMemoryImprovementIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string? Field { get; init; }
}

public sealed record ModelBackedMemoryImprovementDetectionResult
{
    public required bool Succeeded { get; init; }
    public string? ModelBackedMemoryImprovementRunId { get; init; }
    public MemoryImprovementDetectionResult? DetectionResult { get; init; }
    public AgentRunAuditEnvelope? AuditEnvelope { get; init; }
    public AgentModelAdapterResult? ModelAdapterResult { get; init; }
    public AgentModelSanitisationResult? SanitisationResult { get; init; }
    public IReadOnlyList<ModelBackedMemoryImprovementIssue> Issues { get; init; } = [];
}

public interface IModelBackedManualMemoryImprovementAgentService
{
    ModelBackedMemoryImprovementDetectionResult Detect(
        ModelBackedMemoryImprovementDetectionRequest request,
        DateTimeOffset detectedAtUtc);
}

public sealed class ModelBackedMemoryImprovementValidator
{
    public const string DetectionRequestIdRequired = "MODEL_MEMORY_IMPROVEMENT_DETECTION_REQUEST_ID_REQUIRED";
    public const string ScopeRequired = "MODEL_MEMORY_IMPROVEMENT_SCOPE_REQUIRED";
    public const string RequestedByUserIdRequired = "MODEL_MEMORY_IMPROVEMENT_REQUESTED_BY_USER_ID_REQUIRED";
    public const string RequestSummaryRequired = "MODEL_MEMORY_IMPROVEMENT_REQUEST_SUMMARY_REQUIRED";
    public const string InputRequired = "MODEL_MEMORY_IMPROVEMENT_INPUT_REQUIRED";
    public const string InputInvalid = "MODEL_MEMORY_IMPROVEMENT_INPUT_INVALID";
    public const string InputAuthorityBlocked = "MODEL_MEMORY_IMPROVEMENT_INPUT_AUTHORITY_BLOCKED";
    public const string EvidenceRequired = "MODEL_MEMORY_IMPROVEMENT_EVIDENCE_REQUIRED";
    public const string SpecialisationRequired = "MODEL_MEMORY_IMPROVEMENT_SPECIALISATION_REQUIRED";
    public const string SpecialisationInvalid = "MODEL_MEMORY_IMPROVEMENT_SPECIALISATION_INVALID";
    public const string SpecialisationIncompatible = "MODEL_MEMORY_IMPROVEMENT_SPECIALISATION_INCOMPATIBLE";
    public const string SpecialisationAuthorityBlocked = "MODEL_MEMORY_IMPROVEMENT_SPECIALISATION_AUTHORITY_BLOCKED";
    public const string SpecialisationOutputInvalid = "MODEL_MEMORY_IMPROVEMENT_SPECIALISATION_OUTPUT_INVALID";
    public const string ModelProfileRequired = "MODEL_MEMORY_IMPROVEMENT_MODEL_PROFILE_REQUIRED";
    public const string ModelProfileInvalid = "MODEL_MEMORY_IMPROVEMENT_MODEL_PROFILE_INVALID";
    public const string ResponseFormatRequired = "MODEL_MEMORY_IMPROVEMENT_RESPONSE_FORMAT_REQUIRED";
    public const string ResponseFormatInvalid = "MODEL_MEMORY_IMPROVEMENT_RESPONSE_FORMAT_INVALID";

    private readonly IReadOnlyList<AgentSpecialisationDefinition> _specialisations;
    private readonly AgentSpecialisationValidator _specialisationValidator;

    public ModelBackedMemoryImprovementValidator(
        IReadOnlyList<AgentSpecialisationDefinition>? specialisations = null,
        AgentSpecialisationValidator? specialisationValidator = null)
    {
        _specialisations = specialisations ?? AgentSpecialisationCatalog.All;
        _specialisationValidator = specialisationValidator ?? new AgentSpecialisationValidator();
    }

    public IReadOnlyList<ModelBackedMemoryImprovementIssue> ValidateRequest(ModelBackedMemoryImprovementDetectionRequest request)
    {
        var issues = new List<ModelBackedMemoryImprovementIssue>();

        if (string.IsNullOrWhiteSpace(request.DetectionRequestId))
            AddError(issues, DetectionRequestIdRequired, "DetectionRequestId is required.", nameof(request.DetectionRequestId));

        if (string.IsNullOrWhiteSpace(request.TenantId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.CampaignId) ||
            string.IsNullOrWhiteSpace(request.RunId))
        {
            AddError(issues, ScopeRequired, "TenantId, ProjectId, CampaignId, and RunId are required.", "Scope");
        }

        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
            AddError(issues, RequestedByUserIdRequired, "RequestedByUserId is required.", nameof(request.RequestedByUserId));

        if (string.IsNullOrWhiteSpace(request.RequestSummary))
            AddError(issues, RequestSummaryRequired, "RequestSummary is required.", nameof(request.RequestSummary));

        ValidateInputs(request.Inputs, issues);
        ValidateEvidence(request.EvidenceRefs, issues);
        ValidateSpecialisation(request.SpecialisationId, issues);
        ValidateModelProfile(request, issues);
        ValidateResponseFormat(request.ResponseFormat, issues);

        return issues;
    }

    private void ValidateSpecialisation(string specialisationId, List<ModelBackedMemoryImprovementIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(specialisationId))
        {
            AddError(issues, SpecialisationRequired, "SpecialisationId is required.", nameof(ModelBackedMemoryImprovementDetectionRequest.SpecialisationId));
            return;
        }

        var specialisation = _specialisations.FirstOrDefault(profile =>
            string.Equals(profile.SpecialisationId, specialisationId, StringComparison.Ordinal));
        if (specialisation is null || !AgentSpecialisationCatalog.MemoryImprovementProfiles.Any(profile =>
                string.Equals(profile.SpecialisationId, specialisationId, StringComparison.Ordinal)))
        {
            AddError(issues, SpecialisationInvalid, "Selected specialisation must be a known memory-improvement profile.", nameof(ModelBackedMemoryImprovementDetectionRequest.SpecialisationId));
            return;
        }

        var validationIssues = _specialisationValidator.Validate(specialisation)
            .Where(issue => string.Equals(issue.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var issue in validationIssues)
            AddError(issues, SpecialisationInvalid, issue.Message, nameof(ModelBackedMemoryImprovementDetectionRequest.SpecialisationId));

        var compatibility = _specialisationValidator.ValidateCompatibility(
            AgentDefinitionCatalog.MemoryImprovementAgent,
            specialisation);
        foreach (var issue in compatibility.Issues.Where(issue =>
                     string.Equals(issue.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase)))
        {
            AddError(issues, SpecialisationIncompatible, issue.Message, nameof(ModelBackedMemoryImprovementDetectionRequest.SpecialisationId));
        }

        if (specialisation.RequiredAgentKind != AgentKind.ProposalAgent ||
            specialisation.RequiredExecutionMode != AgentExecutionMode.ProposalOnly)
        {
            AddError(issues, SpecialisationIncompatible, "Memory-improvement model-backed manual execution requires ProposalAgent and ProposalOnly specialisations.", nameof(ModelBackedMemoryImprovementDetectionRequest.SpecialisationId));
        }

        var boundary = specialisation.AuthorityBoundary;
        if (boundary.CanGrantApproval ||
            boundary.CanRepresentHumanDecision ||
            boundary.CanOverridePolicy ||
            boundary.CanExecuteTools ||
            boundary.CanMutateSource ||
            boundary.CanCallExternalSystems ||
            boundary.CanPromoteMemory ||
            boundary.CanCreateAuthority ||
            boundary.CanCreateRuntimeAction ||
            boundary.CanWriteMemory)
        {
            AddError(issues, SpecialisationAuthorityBlocked, "Memory-improvement specialisations cannot grant authority or operational capability.", nameof(specialisation.AuthorityBoundary));
        }

        var detectionOutput = specialisation.OutputRequirements.FirstOrDefault(output =>
            string.Equals(output.OutputType, nameof(MemoryImprovementDetectionResult), StringComparison.Ordinal));
        if (detectionOutput is null ||
            !detectionOutput.RequiresHumanReview ||
            !detectionOutput.MustBeProposalOnly ||
            detectionOutput.MayCreateAuthority ||
            detectionOutput.MayCreateRuntimeAction ||
            detectionOutput.MayPromoteMemory)
        {
            AddError(issues, SpecialisationOutputInvalid, "Memory-improvement specialisations must require proposal-only, human-reviewed detection output.", nameof(specialisation.OutputRequirements));
        }
    }

    private static void ValidateInputs(
        IReadOnlyList<ManualMemoryImprovementInputRef> inputs,
        List<ModelBackedMemoryImprovementIssue> issues)
    {
        if (inputs.Count == 0)
        {
            AddError(issues, InputRequired, "At least one input reference is required.", nameof(ModelBackedMemoryImprovementDetectionRequest.Inputs));
            return;
        }

        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.InputRefId) ||
                string.IsNullOrWhiteSpace(input.RefType) ||
                string.IsNullOrWhiteSpace(input.RefId))
            {
                AddError(issues, InputInvalid, "InputRefId, RefType, and RefId are required for every input.", nameof(ModelBackedMemoryImprovementDetectionRequest.Inputs));
            }

            if (input.ContainsRawPrivateReasoning)
                AddError(issues, InputInvalid, "Model-backed memory-improvement inputs cannot contain raw private reasoning.", nameof(input.ContainsRawPrivateReasoning));

            if (input.IsAuthoritativeForAction)
                AddError(issues, InputAuthorityBlocked, "Model-backed memory-improvement inputs cannot be authoritative for action.", nameof(input.IsAuthoritativeForAction));
        }
    }

    private static void ValidateEvidence(IReadOnlyList<string> evidenceRefs, List<ModelBackedMemoryImprovementIssue> issues)
    {
        if (evidenceRefs.Count == 0 || evidenceRefs.Any(string.IsNullOrWhiteSpace))
            AddError(issues, EvidenceRequired, "EvidenceRefs must include at least one non-empty evidence reference.", nameof(ModelBackedMemoryImprovementDetectionRequest.EvidenceRefs));
    }

    private static void ValidateModelProfile(
        ModelBackedMemoryImprovementDetectionRequest request,
        List<ModelBackedMemoryImprovementIssue> issues)
    {
        var profile = request.ModelProfile;
        if (profile is null)
        {
            AddError(issues, ModelProfileRequired, "ModelProfile is required.", nameof(request.ModelProfile));
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.ProfileId) ||
            string.IsNullOrWhiteSpace(profile.ModelName) ||
            !profile.IsEnabled ||
            profile.ProviderKind != AgentModelProviderKind.Fake ||
            profile.AllowsToolCalls ||
            profile.AllowsExternalNetwork ||
            !profile.AllowsJsonOutput ||
            profile.MaxInputTokens <= 0 ||
            profile.MaxOutputTokens <= 0)
        {
            AddError(issues, ModelProfileInvalid, "Model profile must be enabled fake JSON output with no tool calls or external network.", nameof(request.ModelProfile));
        }

        if (profile.AllowedAgentIds.Count > 0 &&
            !profile.AllowedAgentIds.Contains(AgentDefinitionCatalog.MemoryImprovementAgent.AgentId, StringComparer.Ordinal))
        {
            AddError(issues, ModelProfileInvalid, "Model profile does not allow MemoryImprovementAgent.", nameof(profile.AllowedAgentIds));
        }

        if (profile.AllowedSpecialisationIds.Count > 0 &&
            !profile.AllowedSpecialisationIds.Contains(request.SpecialisationId, StringComparer.Ordinal))
        {
            AddError(issues, ModelProfileInvalid, "Model profile does not allow the selected memory-improvement specialisation.", nameof(profile.AllowedSpecialisationIds));
        }
    }

    private static void ValidateResponseFormat(
        AgentModelResponseFormat responseFormat,
        List<ModelBackedMemoryImprovementIssue> issues)
    {
        if (responseFormat is null)
        {
            AddError(issues, ResponseFormatRequired, "ResponseFormat is required.", nameof(ModelBackedMemoryImprovementDetectionRequest.ResponseFormat));
            return;
        }

        var requiredFields = responseFormat.RequiredFields;
        if (!responseFormat.RequiresJson ||
            !responseFormat.RequiresSchemaValidation ||
            !string.Equals(responseFormat.OutputContractName, nameof(MemoryImprovementDetectionResult), StringComparison.Ordinal) ||
            !requiredFields.Contains("summary", StringComparer.OrdinalIgnoreCase) ||
            !requiredFields.Contains("patterns", StringComparer.OrdinalIgnoreCase))
        {
            AddError(issues, ResponseFormatInvalid, "Response format must require JSON schema validation for MemoryImprovementDetectionResult with summary and patterns fields.", nameof(ModelBackedMemoryImprovementDetectionRequest.ResponseFormat));
        }
    }

    private static void AddError(
        List<ModelBackedMemoryImprovementIssue> issues,
        string code,
        string message,
        string? field = null) =>
        issues.Add(new ModelBackedMemoryImprovementIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        });
}

public static class ModelBackedMemoryImprovementPromptBuilder
{
    public static AgentModelRequest Build(
        ModelBackedMemoryImprovementDetectionRequest request,
        DateTimeOffset createdAtUtc)
    {
        var evidenceRefs = request.EvidenceRefs
            .Concat(request.Inputs.SelectMany(input => input.EvidenceRefs))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var content = string.Join(Environment.NewLine, BuildPromptLines(request));

        return new AgentModelRequest
        {
            RequestId = $"model-memory-request-{request.DetectionRequestId}",
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            CampaignId = request.CampaignId,
            AgentRunId = $"model-memory-run-{request.DetectionRequestId}",
            AgentId = AgentDefinitionCatalog.MemoryImprovementAgent.AgentId,
            SpecialisationId = request.SpecialisationId,
            Profile = request.ModelProfile,
            Messages =
            [
                new AgentModelMessage
                {
                    MessageId = $"model-memory-message-{request.DetectionRequestId}-001",
                    Role = AgentModelRole.User,
                    Content = content,
                    EvidenceRefs = evidenceRefs
                }
            ],
            Context = new AgentModelRequestContext
            {
                InputRefs = request.Inputs.Select(input => input.InputRefId).ToArray(),
                EvidenceRefs = evidenceRefs,
                IncludesRetrievedMemory = request.Inputs.Any(input => input.RefType.Contains("Memory", StringComparison.OrdinalIgnoreCase)),
                IncludesCollectiveMemoryCandidate = false,
                IncludesLocalMemory = request.Inputs.Any(input => input.RefType.Contains("Memory", StringComparison.OrdinalIgnoreCase)),
                IncludesRawPromptOrCompletion = false,
                IncludesPrivateReasoning = false,
                IncludesAuthoritySource = false
            },
            ResponseFormat = request.ResponseFormat,
            SafetyFlags = new AgentModelSafetyFlags(),
            CreatedAtUtc = createdAtUtc
        };
    }

    private static IEnumerable<string> BuildPromptLines(ModelBackedMemoryImprovementDetectionRequest request)
    {
        yield return "You are producing a proposal-only memory-improvement detection result for human review.";
        yield return "You may identify repeated patterns, stale records, contradictions, retrieval gaps, duplicate proposal candidates, and repeated manual corrections.";
        yield return "Keep proposal storage outside this output.";
        yield return "Keep shared-memory creation outside this output.";
        yield return "Keep memory elevation outside this output.";
        yield return "Keep index-writing outside this output.";
        yield return "Keep permission decisions outside this output.";
        yield return "Do not request operational action.";
        yield return "Do not request tool usage.";
        yield return "Do not request repository edits.";
        yield return "Return only the requested typed memory-improvement detection candidate.";
        yield return $"Detection request: {Trim(request.RequestSummary, 600)}";
        yield return $"Specialisation: {request.SpecialisationId}";
        yield return "Inputs:";

        foreach (var input in request.Inputs.Take(16))
        {
            yield return $"- {input.InputRefId} [{input.RefType}:{input.RefId}] {Trim(input.Summary, 300)}";
        }
    }

    private static string Trim(string value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Length <= maxLength
                ? value
                : string.Concat(value.AsSpan(0, maxLength), "...");
}

public sealed class ModelBackedMemoryImprovementResponseParser
{
    public const string StructuredJsonRequired = "MODEL_MEMORY_IMPROVEMENT_STRUCTURED_JSON_REQUIRED";
    public const string StructuredJsonInvalid = "MODEL_MEMORY_IMPROVEMENT_STRUCTURED_JSON_INVALID";
    public const string PatternOrNoProposalRequired = "MODEL_MEMORY_IMPROVEMENT_PATTERN_OR_NO_PROPOSAL_REQUIRED";
    public const string PatternInvalid = "MODEL_MEMORY_IMPROVEMENT_PATTERN_INVALID";
    public const string PatternEvidenceRequired = "MODEL_MEMORY_IMPROVEMENT_PATTERN_EVIDENCE_REQUIRED";
    public const string ProposalInvalid = "MODEL_MEMORY_IMPROVEMENT_PROPOSAL_INVALID";
    public const string ProposalEvidenceRequired = "MODEL_MEMORY_IMPROVEMENT_PROPOSAL_EVIDENCE_REQUIRED";
    public const string ProposalOnlyRequired = "MODEL_MEMORY_IMPROVEMENT_PROPOSAL_ONLY_REQUIRED";
    public const string UnsafeTextBlocked = "MODEL_MEMORY_IMPROVEMENT_UNSAFE_TEXT_BLOCKED";

    public ParsedModelBackedMemoryImprovementResult Parse(
        ModelBackedMemoryImprovementDetectionRequest request,
        AgentModelSanitisedResponse response,
        DateTimeOffset detectedAtUtc)
    {
        var issues = new List<ModelBackedMemoryImprovementIssue>();

        if (string.IsNullOrWhiteSpace(response.StructuredJsonCandidate))
        {
            AddError(issues, StructuredJsonRequired, "Sanitised model response must include structured JSON.");
            return new ParsedModelBackedMemoryImprovementResult(null, issues);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(response.StructuredJsonCandidate);
        }
        catch (JsonException ex)
        {
            AddError(issues, StructuredJsonInvalid, $"Structured JSON could not be parsed: {ex.Message}");
            return new ParsedModelBackedMemoryImprovementResult(null, issues);
        }

        using (document)
        {
            var root = document.RootElement;
            var summary = GetString(root, "summary");
            var noProposalReason = ParseNoProposalReason(GetString(root, "noProposalReason"), issues);
            var findings = ParsePatterns(request, root, issues);
            var proposals = ParseProposals(request, root, findings, issues);

            if (findings.Count == 0 && noProposalReason is null)
                AddError(issues, PatternOrNoProposalRequired, "Model-backed memory-improvement output requires at least one pattern or a valid no-proposal reason.");

            if (ContainsUnsafeText([summary, .. findings.Select(finding => finding.Summary), .. proposals.Select(proposal => proposal.Title), .. proposals.Select(proposal => proposal.Summary), .. proposals.Select(proposal => proposal.Rationale)]))
                AddError(issues, UnsafeTextBlocked, "Model-backed memory-improvement output contains unsafe authority, private reasoning, or memory authority text.");

            if (issues.Any(issue => string.Equals(issue.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase)))
                return new ParsedModelBackedMemoryImprovementResult(null, issues);

            var result = new MemoryImprovementDetectionResult
            {
                DetectionResultId = $"memory-detection-{request.DetectionRequestId}",
                Findings = findings,
                ProposalDrafts = proposals,
                NoProposalReason = noProposalReason,
                DetectedAt = detectedAtUtc,
                DetectedByAgentId = AgentDefinitionCatalog.MemoryImprovementAgent.AgentId,
                CorrelationId = request.CorrelationId,
                Warnings =
                [
                    "MemoryImprovementAgent output is proposal-only.",
                    "Proposal drafts do not create accepted memory.",
                    "Proposal drafts do not promote memory.",
                    "Proposal drafts require governed review before persistence or promotion.",
                    "Detection result does not create CollectiveMemory.",
                    "Detection result does not write vector index data.",
                    "Detection result does not approve runtime action."
                ]
            };

            return new ParsedModelBackedMemoryImprovementResult(result, issues);
        }
    }

    private static List<MemoryImprovementPatternFinding> ParsePatterns(
        ModelBackedMemoryImprovementDetectionRequest request,
        JsonElement root,
        List<ModelBackedMemoryImprovementIssue> issues)
    {
        var findings = new List<MemoryImprovementPatternFinding>();
        if (!TryGet(root, "patterns", out var patterns) || patterns.ValueKind != JsonValueKind.Array)
            return findings;

        var index = 0;
        foreach (var pattern in patterns.EnumerateArray())
        {
            index++;
            var patternTypeText = GetString(pattern, "patternType");
            var summary = GetString(pattern, "summary");
            var confidence = GetDecimal(pattern, "confidence");
            var evidenceRefs = GetStringArray(pattern, "evidenceRefs");
            var relatedMemoryIds = GetStringArray(pattern, "relatedMemoryIds");
            var relatedProposalIds = GetStringArray(pattern, "relatedProposalIds");
            var requiresHumanReview = GetBoolean(pattern, "requiresHumanReview", defaultValue: true);

            if (!Enum.TryParse<MemoryImprovementPatternType>(patternTypeText, ignoreCase: true, out var patternType) ||
                !Enum.IsDefined(patternType) ||
                string.IsNullOrWhiteSpace(summary) ||
                confidence is null ||
                confidence < 0m ||
                confidence > 1m ||
                !requiresHumanReview)
            {
                AddError(issues, PatternInvalid, $"Pattern at index {index - 1} is invalid.");
                continue;
            }

            if (evidenceRefs.Count == 0 || evidenceRefs.Any(string.IsNullOrWhiteSpace))
            {
                AddError(issues, PatternEvidenceRequired, $"Pattern at index {index - 1} requires evidence references.");
                continue;
            }

            findings.Add(new MemoryImprovementPatternFinding
            {
                PatternFindingId = $"memory-pattern-{request.DetectionRequestId}-{index:000}",
                PatternType = patternType,
                Summary = summary,
                Confidence = confidence.Value,
                EvidenceRefs = evidenceRefs,
                RelatedMemoryIds = relatedMemoryIds,
                RelatedProposalIds = relatedProposalIds,
                IsDuplicateCandidate = GetBoolean(pattern, "isDuplicateCandidate", defaultValue: false),
                RequiresHumanReview = true
            });
        }

        return findings;
    }

    private static List<MemoryImprovementProposalDraft> ParseProposals(
        ModelBackedMemoryImprovementDetectionRequest request,
        JsonElement root,
        IReadOnlyList<MemoryImprovementPatternFinding> findings,
        List<ModelBackedMemoryImprovementIssue> issues)
    {
        var proposals = new List<MemoryImprovementProposalDraft>();
        if (!TryGet(root, "proposalDrafts", out var proposalDrafts) || proposalDrafts.ValueKind != JsonValueKind.Array)
            return proposals;

        var index = 0;
        foreach (var proposal in proposalDrafts.EnumerateArray())
        {
            index++;
            var title = GetString(proposal, "title");
            var summary = GetString(proposal, "summary");
            var rationale = GetString(proposal, "rationale");
            var sourcePatternIndex = GetInt32(proposal, "sourcePatternIndex");
            var evidenceRefs = GetStringArray(proposal, "evidenceRefs");
            var isProposalOnly = GetBoolean(proposal, "isProposalOnly", defaultValue: false);
            var createsCollectiveMemory = GetBoolean(proposal, "createsCollectiveMemory", defaultValue: false);
            var promotesMemory = GetBoolean(proposal, "promotesMemory", defaultValue: false);
            var requiresHumanReview = GetBoolean(proposal, "requiresHumanReview", defaultValue: true);

            if (string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(summary) ||
                string.IsNullOrWhiteSpace(rationale) ||
                sourcePatternIndex is null ||
                sourcePatternIndex < 0 ||
                sourcePatternIndex >= findings.Count)
            {
                AddError(issues, ProposalInvalid, $"Proposal draft at index {index - 1} is invalid.");
                continue;
            }

            if (evidenceRefs.Count == 0 || evidenceRefs.Any(string.IsNullOrWhiteSpace))
            {
                AddError(issues, ProposalEvidenceRequired, $"Proposal draft at index {index - 1} requires evidence references.");
                continue;
            }

            if (!isProposalOnly || createsCollectiveMemory || promotesMemory || !requiresHumanReview)
            {
                AddError(issues, ProposalOnlyRequired, $"Proposal draft at index {index - 1} must remain proposal-only, non-creating, non-elevating, and human-reviewed.");
                continue;
            }

            proposals.Add(new MemoryImprovementProposalDraft
            {
                ProposalDraftId = $"memory-proposal-draft-{request.DetectionRequestId}-{index:000}",
                Title = title,
                Summary = summary,
                Rationale = rationale,
                SourcePattern = findings[sourcePatternIndex.Value],
                EvidenceRefs = evidenceRefs,
                IsProposalOnly = true,
                CreatesCollectiveMemory = false,
                PromotesMemory = false,
                RequiresHumanReview = true
            });
        }

        return proposals;
    }

    private static MemoryImprovementNoProposalReason? ParseNoProposalReason(
        string? value,
        List<ModelBackedMemoryImprovementIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (Enum.TryParse<MemoryImprovementNoProposalReason>(value, ignoreCase: true, out var reason) &&
            Enum.IsDefined(reason))
        {
            return reason;
        }

        AddError(issues, StructuredJsonInvalid, $"NoProposalReason '{value}' is invalid.");
        return null;
    }

    private static bool TryGet(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string GetString(JsonElement element, string propertyName) =>
        TryGet(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static decimal? GetDecimal(JsonElement element, string propertyName) =>
        TryGet(element, propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var decimalValue)
            ? decimalValue
            : null;

    private static int? GetInt32(JsonElement element, string propertyName) =>
        TryGet(element, propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue)
            ? intValue
            : null;

    private static bool GetBoolean(JsonElement element, string propertyName, bool defaultValue) =>
        TryGet(element, propertyName, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : defaultValue;

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!TryGet(element, propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
    }

    private static bool ContainsUnsafeText(IEnumerable<string?> values)
    {
        var markers = new[]
        {
            "raw" + "prompt",
            "raw" + "completion",
            "chain" + "-of-" + "thought",
            "scratch" + "pad",
            "private" + "reasoning",
            "approval granted",
            "approved for execution",
            "policy cleared",
            "human approved",
            "authoritative for action",
            "run this tool",
            "apply this patch",
            "mutate source",
            "promote memory",
            "accepted memory",
            "create collectivememory",
            "persist proposal"
        };

        return values.Any(value => !string.IsNullOrWhiteSpace(value) &&
                                   markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }

    private static void AddError(
        List<ModelBackedMemoryImprovementIssue> issues,
        string code,
        string message,
        string? field = null) =>
        issues.Add(new ModelBackedMemoryImprovementIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        });
}

public sealed record ParsedModelBackedMemoryImprovementResult(
    MemoryImprovementDetectionResult? DetectionResult,
    IReadOnlyList<ModelBackedMemoryImprovementIssue> Issues);

public sealed class ModelBackedManualMemoryImprovementAgentService : IModelBackedManualMemoryImprovementAgentService
{
    private readonly IAgentModelAdapter _modelAdapter;
    private readonly IAgentModelAuditSanitiser _sanitiser;
    private readonly ModelBackedMemoryImprovementValidator _requestValidator;
    private readonly AgentModelAdapterValidator _adapterValidator;
    private readonly ModelBackedMemoryImprovementResponseParser _responseParser;
    private readonly MemoryImprovementDetectionResultValidator _detectionValidator;
    private readonly AgentRunAuditEnvelopeValidator _auditValidator;
    private readonly ThoughtLedgerSafetyValidator _thoughtLedgerSafetyValidator;

    public ModelBackedManualMemoryImprovementAgentService(
        IAgentModelAdapter modelAdapter,
        IAgentModelAuditSanitiser? sanitiser = null,
        ModelBackedMemoryImprovementValidator? requestValidator = null,
        AgentModelAdapterValidator? adapterValidator = null,
        ModelBackedMemoryImprovementResponseParser? responseParser = null,
        MemoryImprovementDetectionResultValidator? detectionValidator = null,
        AgentRunAuditEnvelopeValidator? auditValidator = null,
        ThoughtLedgerSafetyValidator? thoughtLedgerSafetyValidator = null)
    {
        _modelAdapter = modelAdapter ?? throw new ArgumentNullException(nameof(modelAdapter));
        _adapterValidator = adapterValidator ?? new AgentModelAdapterValidator();
        _sanitiser = sanitiser ?? new AgentModelAuditSanitiser(_adapterValidator);
        _requestValidator = requestValidator ?? new ModelBackedMemoryImprovementValidator();
        _responseParser = responseParser ?? new ModelBackedMemoryImprovementResponseParser();
        _detectionValidator = detectionValidator ?? new MemoryImprovementDetectionResultValidator();
        _auditValidator = auditValidator ?? new AgentRunAuditEnvelopeValidator();
        _thoughtLedgerSafetyValidator = thoughtLedgerSafetyValidator ?? new ThoughtLedgerSafetyValidator();
    }

    public ModelBackedMemoryImprovementDetectionResult Detect(
        ModelBackedMemoryImprovementDetectionRequest request,
        DateTimeOffset detectedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<ModelBackedMemoryImprovementIssue>(_requestValidator.ValidateRequest(request));
        if (issues.Any(IsError))
            return Failed(request, issues);

        var modelRequest = ModelBackedMemoryImprovementPromptBuilder.Build(request, detectedAtUtc);
        AddAdapterIssues(issues, _adapterValidator.ValidateRequest(modelRequest));
        if (issues.Any(IsError))
            return Failed(request, issues);

        var adapterResult = _modelAdapter.Invoke(modelRequest, detectedAtUtc);
        if (!adapterResult.Succeeded || adapterResult.Response is null)
        {
            AddAdapterIssues(issues, adapterResult.Issues, "MODEL_MEMORY_IMPROVEMENT_ADAPTER_FAILED");
            if (adapterResult.Response is null)
                AddError(issues, "MODEL_MEMORY_IMPROVEMENT_RESPONSE_MISSING", "Model adapter did not return a response.");

            return Failed(request, issues, adapterResult);
        }

        var sanitisationResult = _sanitiser.Sanitise(new AgentModelSanitisationRequest
        {
            Request = modelRequest,
            Response = adapterResult.Response,
            Audit = adapterResult.Audit,
            AllowRedactedPreview = false,
            AllowStructuredJsonCandidate = true
        });
        if (sanitisationResult.Status == AgentModelSanitisationStatus.Rejected ||
            sanitisationResult.Response is null)
        {
            AddSanitisationIssues(issues, sanitisationResult.Issues);
            if (sanitisationResult.Response is null)
                AddError(issues, "MODEL_MEMORY_IMPROVEMENT_SANITISED_RESPONSE_MISSING", "Sanitised response is required.");

            return Failed(request, issues, adapterResult, sanitisationResult);
        }

        var parsed = _responseParser.Parse(request, sanitisationResult.Response, detectedAtUtc);
        issues.AddRange(parsed.Issues);
        if (parsed.DetectionResult is null || issues.Any(IsError))
            return Failed(request, issues, adapterResult, sanitisationResult);

        foreach (var validationIssue in _detectionValidator.Validate(parsed.DetectionResult))
        {
            AddError(issues, validationIssue.Code, validationIssue.Message);
        }

        if (issues.Any(IsError))
            return Failed(request, issues, adapterResult, sanitisationResult);

        var envelope = BuildAuditEnvelope(request, parsed.DetectionResult, sanitisationResult, detectedAtUtc);
        foreach (var validationIssue in _auditValidator.Validate(envelope))
            AddError(issues, validationIssue.Code, validationIssue.Message);

        foreach (var validationIssue in _thoughtLedgerSafetyValidator.Validate(envelope.ThoughtLedger))
            AddError(issues, validationIssue.Code, validationIssue.Message);

        if (issues.Any(IsError))
            return Failed(request, issues, adapterResult, sanitisationResult);

        return new ModelBackedMemoryImprovementDetectionResult
        {
            Succeeded = true,
            ModelBackedMemoryImprovementRunId = envelope.Run.AgentRunId,
            DetectionResult = parsed.DetectionResult,
            AuditEnvelope = envelope,
            ModelAdapterResult = adapterResult,
            SanitisationResult = sanitisationResult,
            Issues = issues
        };
    }

    private static AgentRunAuditEnvelope BuildAuditEnvelope(
        ModelBackedMemoryImprovementDetectionRequest request,
        MemoryImprovementDetectionResult detectionResult,
        AgentModelSanitisationResult sanitisationResult,
        DateTimeOffset detectedAtUtc)
    {
        var agentRunId = $"model-memory-improvement-{request.DetectionRequestId}";
        var definition = AgentDefinitionCatalog.MemoryImprovementAgent;
        var evidenceRefs = request.EvidenceRefs.Count > 0
            ? request.EvidenceRefs
            : ["model-memory-evidence"];

        return new AgentRunAuditEnvelope
        {
            Run = new AgentRunRecord
            {
                AgentRunId = agentRunId,
                TenantId = request.TenantId,
                ProjectId = request.ProjectId,
                CampaignId = request.CampaignId,
                RunId = request.RunId,
                AgentId = definition.AgentId,
                AgentName = definition.Name,
                RequestedByUserId = request.RequestedByUserId,
                TriggerType = AgentRunTriggerType.ManualUserRequest,
                Status = detectionResult.ProposalDrafts.Count > 0
                    ? AuditAgentRunStatus.Completed
                    : AuditAgentRunStatus.CompletedWithWarnings,
                RequestSummary = request.RequestSummary,
                Purpose = "Model-backed manual memory-improvement detection.",
                CreatedAtUtc = detectedAtUtc,
                StartedAtUtc = detectedAtUtc,
                CompletedAtUtc = detectedAtUtc
            },
            AgentDefinitionSnapshot = definition,
            Inputs = BuildInputs(request, sanitisationResult, agentRunId),
            Outputs = BuildOutputs(detectionResult, agentRunId),
            Steps = BuildSteps(agentRunId, detectedAtUtc, evidenceRefs),
            CapabilityUses = BuildCapabilityUses(definition, agentRunId, evidenceRefs[0]),
            BoundaryDecisions = BuildBoundaryDecisions(agentRunId, request, detectionResult, sanitisationResult, evidenceRefs),
            ThoughtLedger = BuildThoughtLedger(agentRunId, detectedAtUtc, evidenceRefs)
        };
    }

    private static IReadOnlyList<AgentRunInputRef> BuildInputs(
        ModelBackedMemoryImprovementDetectionRequest request,
        AgentModelSanitisationResult sanitisationResult,
        string agentRunId)
    {
        var inputs = request.Inputs.Select(input => new AgentRunInputRef
        {
            InputRefId = input.InputRefId,
            AgentRunId = agentRunId,
            RefType = input.RefType,
            RefId = input.RefId,
            Source = input.Source,
            Summary = input.Summary,
            IsAuthoritativeForAction = false,
            ContainsRawPrivateReasoning = false
        }).ToList();

        inputs.Add(new AgentRunInputRef
        {
            InputRefId = $"specialisation-{request.SpecialisationId}",
            AgentRunId = agentRunId,
            RefType = nameof(AgentSpecialisationDefinition),
            RefId = request.SpecialisationId,
            Summary = "Selected memory-improvement specialisation is non-authoritative input.",
            IsAuthoritativeForAction = false,
            ContainsRawPrivateReasoning = false
        });

        inputs.Add(new AgentRunInputRef
        {
            InputRefId = $"model-profile-{request.ModelProfile.ProfileId}",
            AgentRunId = agentRunId,
            RefType = nameof(AgentModelProfile),
            RefId = request.ModelProfile.ProfileId,
            Summary = "Fake model profile used for manual memory-improvement detection.",
            IsAuthoritativeForAction = false,
            ContainsRawPrivateReasoning = false
        });

        if (sanitisationResult.Prompt is not null)
        {
            inputs.Add(new AgentRunInputRef
            {
                InputRefId = $"sanitised-prompt-{sanitisationResult.Prompt.RequestId}",
                AgentRunId = agentRunId,
                RefType = nameof(AgentModelSanitisedPrompt),
                RefId = sanitisationResult.Prompt.RequestId,
                Summary = sanitisationResult.Prompt.Summary,
                IsAuthoritativeForAction = false,
                ContainsRawPrivateReasoning = sanitisationResult.Prompt.ContainsRawPrivateReasoning
            });
        }

        if (sanitisationResult.Audit is not null)
        {
            inputs.Add(new AgentRunInputRef
            {
                InputRefId = $"sanitised-model-audit-{sanitisationResult.Audit.AuditId}",
                AgentRunId = agentRunId,
                RefType = nameof(AgentModelSanitisedInvocationAudit),
                RefId = sanitisationResult.Audit.AuditId,
                Summary = "Sanitised model invocation audit is non-authoritative evidence.",
                IsAuthoritativeForAction = false,
                ContainsRawPrivateReasoning = sanitisationResult.Audit.ContainsRawPrivateReasoning
            });
        }

        return inputs;
    }

    private static IReadOnlyList<AgentRunOutputRef> BuildOutputs(
        MemoryImprovementDetectionResult detectionResult,
        string agentRunId)
    {
        var outputs = new List<AgentRunOutputRef>
        {
            new()
            {
                OutputRefId = $"output-{detectionResult.DetectionResultId}",
                AgentRunId = agentRunId,
                RefType = nameof(MemoryImprovementDetectionResult),
                RefId = detectionResult.DetectionResultId,
                Summary = "Proposal-only memory-improvement detection result for human review.",
                IsProposalOnly = true,
                CreatesAuthority = false,
                CreatesRuntimeAction = false,
                ContainsRawPrivateReasoning = false,
                EvidenceRefs = detectionResult.Findings.SelectMany(finding => finding.EvidenceRefs).Distinct(StringComparer.Ordinal).ToArray()
            }
        };

        outputs.AddRange(detectionResult.ProposalDrafts.Select(draft => new AgentRunOutputRef
        {
            OutputRefId = $"output-{draft.ProposalDraftId}",
            AgentRunId = agentRunId,
            RefType = nameof(MemoryImprovementProposalDraft),
            RefId = draft.ProposalDraftId,
            Summary = "Proposal draft for governed human review.",
            IsProposalOnly = true,
            CreatesAuthority = false,
            CreatesRuntimeAction = false,
            ContainsRawPrivateReasoning = false,
            EvidenceRefs = draft.EvidenceRefs
        }));

        return outputs;
    }

    private static IReadOnlyList<AgentRunStep> BuildSteps(
        string agentRunId,
        DateTimeOffset detectedAtUtc,
        IReadOnlyList<string> evidenceRefs) =>
    [
        Step(agentRunId, 1, AgentRunStepType.Created, detectedAtUtc, "Model-backed manual memory-improvement run created.", evidenceRefs),
        Step(agentRunId, 2, AgentRunStepType.InputBound, detectedAtUtc, "Supplied evidence summaries were bound as non-authoritative inputs.", evidenceRefs),
        Step(agentRunId, 3, AgentRunStepType.CapabilityEvaluated, detectedAtUtc, "Fake model adapter and sanitiser boundaries were evaluated.", evidenceRefs),
        Step(agentRunId, 4, AgentRunStepType.OutputRecorded, detectedAtUtc, "Proposal-only memory-improvement detection output was recorded.", evidenceRefs),
        Step(agentRunId, 5, AgentRunStepType.Completed, detectedAtUtc, "Model-backed manual memory-improvement run completed without operational authority.", evidenceRefs)
    ];

    private static AgentRunStep Step(
        string agentRunId,
        int sequence,
        AgentRunStepType stepType,
        DateTimeOffset occurredAtUtc,
        string summary,
        IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            StepId = $"{agentRunId}-step-{sequence:000}",
            AgentRunId = agentRunId,
            Sequence = sequence,
            StepType = stepType,
            OccurredAtUtc = occurredAtUtc,
            Summary = summary,
            EvidenceRefs = evidenceRefs,
            ContainsRawPrivateReasoning = false
        };

    private static IReadOnlyList<AgentCapabilityUseRecord> BuildCapabilityUses(
        AgentDefinition definition,
        string agentRunId,
        string evidenceRef)
    {
        var allowed = new[]
        {
            (AgentCapability.CreateMemoryProposal, "Created proposal-only memory-improvement output."),
            (AgentCapability.CreateReport, "Created manual memory-improvement report output.")
        };
        var blocked = new[]
        {
            (AgentCapability.PromoteCollectiveMemory, "Shared-memory elevation is unavailable."),
            (AgentCapability.RunTool, "Tool use is unavailable."),
            (AgentCapability.MutateSource, "Repository editing is unavailable."),
            (AgentCapability.CallExternalSystem, "External calls are unavailable."),
            (AgentCapability.BlockExecution, "Execution blocking is unavailable."),
            (AgentCapability.RepresentHumanApproval, "Human decision representation is unavailable."),
            (AgentCapability.RepresentHumanPromotionDecision, "Human elevation decision representation is unavailable.")
        };

        return allowed
            .Select((entry, index) => Capability(definition, agentRunId, entry.Item1, AgentCapabilityUseOutcome.Allowed, entry.Item2, evidenceRef, index + 1))
            .Concat(blocked.Select((entry, index) => Capability(definition, agentRunId, entry.Item1, AgentCapabilityUseOutcome.Blocked, entry.Item2, evidenceRef, index + 101)))
            .ToArray();
    }

    private static AgentCapabilityUseRecord Capability(
        AgentDefinition definition,
        string agentRunId,
        AgentCapability capability,
        AgentCapabilityUseOutcome outcome,
        string summary,
        string evidenceRef,
        int index) =>
        new()
        {
            CapabilityUseId = $"{agentRunId}-capability-{index:000}",
            AgentRunId = agentRunId,
            Capability = capability,
            Outcome = outcome,
            Summary = summary,
            EvidenceRef = evidenceRef,
            WasDeclaredOnAgent = definition.Capabilities?.Contains(capability) == true,
            WasForbiddenOnAgent = definition.ForbiddenCapabilities?.Contains(capability) == true
        };

    private static IReadOnlyList<AgentBoundaryDecision> BuildBoundaryDecisions(
        string agentRunId,
        ModelBackedMemoryImprovementDetectionRequest request,
        MemoryImprovementDetectionResult detectionResult,
        AgentModelSanitisationResult sanitisationResult,
        IReadOnlyList<string> evidenceRefs) =>
    [
        Boundary(agentRunId, 1, AgentBoundaryDecisionType.Safety, "allowed", "Fake/scripted model adapter boundary returned safe candidate.", request.SpecialisationId, evidenceRefs),
        Boundary(agentRunId, 2, AgentBoundaryDecisionType.Safety, "allowed", $"Model audit sanitiser returned {sanitisationResult.Status}.", request.SpecialisationId, evidenceRefs),
        Boundary(agentRunId, 3, AgentBoundaryDecisionType.OutputValidation, "allowed", "Memory-improvement output validated as proposal-only and human-reviewed.", detectionResult.DetectionResultId, evidenceRefs),
        Boundary(agentRunId, 4, AgentBoundaryDecisionType.Capability, "blocked", "Tool use remains unavailable.", nameof(AgentCapability.RunTool), evidenceRefs),
        Boundary(agentRunId, 5, AgentBoundaryDecisionType.Capability, "blocked", "Repository editing remains unavailable.", nameof(AgentCapability.MutateSource), evidenceRefs),
        Boundary(agentRunId, 6, AgentBoundaryDecisionType.Capability, "blocked", "External publication remains unavailable.", nameof(AgentCapability.CallExternalSystem), evidenceRefs),
        Boundary(agentRunId, 7, AgentBoundaryDecisionType.Capability, "blocked", "Shared-memory elevation remains unavailable.", nameof(AgentCapability.PromoteCollectiveMemory), evidenceRefs),
        Boundary(agentRunId, 8, AgentBoundaryDecisionType.Output, "blocked", "Proposal storage remains unavailable.", detectionResult.DetectionResultId, evidenceRefs),
        Boundary(agentRunId, 9, AgentBoundaryDecisionType.Output, "blocked", "Index writing remains unavailable.", detectionResult.DetectionResultId, evidenceRefs),
        Boundary(agentRunId, 10, AgentBoundaryDecisionType.ThoughtLedgerSafety, "allowed", "Thought ledger entries were validated as non-authoritative.", detectionResult.DetectionResultId, evidenceRefs)
    ];

    private static AgentBoundaryDecision Boundary(
        string agentRunId,
        int index,
        AgentBoundaryDecisionType type,
        string decision,
        string reason,
        string sourceRefId,
        IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            BoundaryDecisionId = $"{agentRunId}-boundary-{index:000}",
            AgentRunId = agentRunId,
            BoundaryType = type,
            Decision = decision,
            Reason = reason,
            SourceRefId = sourceRefId,
            GrantsAuthority = false,
            GrantsHumanApproval = false,
            GrantsPolicyApproval = false,
            GrantsMemoryPromotion = false,
            EvidenceRefs = evidenceRefs
        };

    private static IReadOnlyList<AuditThoughtLedgerEntry> BuildThoughtLedger(
        string agentRunId,
        DateTimeOffset detectedAtUtc,
        IReadOnlyList<string> evidenceRefs) =>
    [
        Thought(agentRunId, 1, ThoughtLedgerEntryType.DecisionRationale, "Model memory-improvement request was built from supplied evidence summaries.", detectedAtUtc, evidenceRefs),
        Thought(agentRunId, 2, ThoughtLedgerEntryType.EvidenceUsed, "Supplied evidence references were used to produce a proposal-only detection candidate.", detectedAtUtc, evidenceRefs),
        Thought(agentRunId, 3, ThoughtLedgerEntryType.BoundaryDecision, "Fake model adapter, sanitiser, output validation, and dangerous capability blocks were checked.", detectedAtUtc, evidenceRefs),
        Thought(agentRunId, 4, ThoughtLedgerEntryType.OutputRationale, "Detection result remains proposal-only and requires governed human review.", detectedAtUtc, evidenceRefs),
        Thought(agentRunId, 5, ThoughtLedgerEntryType.FollowUp, "Proposal storage, shared-memory creation, memory elevation, and index writing remain unavailable.", detectedAtUtc, evidenceRefs)
    ];

    private static AuditThoughtLedgerEntry Thought(
        string agentRunId,
        int index,
        ThoughtLedgerEntryType type,
        string summary,
        DateTimeOffset recordedAtUtc,
        IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            ThoughtLedgerEntryId = $"{agentRunId}-thought-{index:000}",
            AgentRunId = agentRunId,
            EntryType = type,
            Summary = summary,
            EvidenceRefs = evidenceRefs,
            ContainsRawPrivateReasoning = false,
            GrantsAuthority = false,
            GrantsApproval = false,
            GrantsMemoryPromotion = false,
            RecordedAtUtc = recordedAtUtc
        };

    private static ModelBackedMemoryImprovementDetectionResult Failed(
        ModelBackedMemoryImprovementDetectionRequest request,
        IReadOnlyList<ModelBackedMemoryImprovementIssue> issues,
        AgentModelAdapterResult? adapterResult = null,
        AgentModelSanitisationResult? sanitisationResult = null) =>
        new()
        {
            Succeeded = false,
            ModelBackedMemoryImprovementRunId = string.IsNullOrWhiteSpace(request.DetectionRequestId)
                ? null
                : $"model-memory-improvement-{request.DetectionRequestId}",
            DetectionResult = null,
            AuditEnvelope = null,
            ModelAdapterResult = adapterResult,
            SanitisationResult = sanitisationResult,
            Issues = issues
        };

    private static void AddAdapterIssues(
        List<ModelBackedMemoryImprovementIssue> issues,
        IReadOnlyList<AgentModelAdapterIssue> adapterIssues,
        string? fallbackCode = null)
    {
        if (adapterIssues.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(fallbackCode))
                AddError(issues, fallbackCode, "Model adapter rejected the request or response.");
            return;
        }

        foreach (var issue in adapterIssues)
        {
            issues.Add(new ModelBackedMemoryImprovementIssue
            {
                Code = issue.Code,
                Severity = issue.Severity,
                Message = issue.Message,
                Field = issue.Field
            });
        }
    }

    private static void AddSanitisationIssues(
        List<ModelBackedMemoryImprovementIssue> issues,
        IReadOnlyList<AgentModelSanitisationIssue> sanitisationIssues)
    {
        if (sanitisationIssues.Count == 0)
        {
            AddError(issues, "MODEL_MEMORY_IMPROVEMENT_SANITISATION_REJECTED", "Model audit sanitiser rejected model material.");
            return;
        }

        foreach (var issue in sanitisationIssues)
        {
            issues.Add(new ModelBackedMemoryImprovementIssue
            {
                Code = issue.Code,
                Severity = issue.Severity,
                Message = issue.Message,
                Field = issue.Field
            });
        }
    }

    private static void AddError(
        List<ModelBackedMemoryImprovementIssue> issues,
        string code,
        string message,
        string? field = null) =>
        issues.Add(new ModelBackedMemoryImprovementIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        });

    private static bool IsError(ModelBackedMemoryImprovementIssue issue) =>
        string.Equals(issue.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase);
}
