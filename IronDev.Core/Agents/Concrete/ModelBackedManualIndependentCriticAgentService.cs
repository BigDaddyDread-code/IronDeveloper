using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using IronDev.Core.Agents.Audit;
using AuditThoughtLedgerEntry = IronDev.Core.Agents.Audit.ThoughtLedgerEntry;

namespace IronDev.Core.Agents.Concrete;

public sealed record ModelBackedCriticReviewRequest
{
    public required string ReviewRequestId { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public string? CampaignId { get; init; }
    public string? RunId { get; init; }
    public required string RequestedByUserId { get; init; }
    public required string CorrelationId { get; init; }
    public required CriticReviewSubjectType SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string SpecialisationId { get; init; }
    public required AgentModelProfile ModelProfile { get; init; }
    public required string RequestSummary { get; init; }
    public IReadOnlyList<ManualCriticReviewInputRef> Inputs { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public AgentModelResponseFormat ResponseFormat { get; init; } = new()
    {
        FormatId = string.Empty,
        OutputContractName = string.Empty
    };
}

public sealed record ModelBackedCriticReviewResult
{
    public required bool Succeeded { get; init; }
    public required string ModelBackedCriticRunId { get; init; }
    public CriticReviewResult? CriticReviewResult { get; init; }
    public AgentRunAuditEnvelope? AuditEnvelope { get; init; }
    public AgentModelAdapterResult? ModelAdapterResult { get; init; }
    public AgentModelSanitisationResult? SanitisationResult { get; init; }
    public IReadOnlyList<ModelBackedCriticReviewIssue> Issues { get; init; } = [];
}

public sealed record ModelBackedCriticReviewIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public interface IModelBackedManualIndependentCriticAgentService
{
    ModelBackedCriticReviewResult Review(
        ModelBackedCriticReviewRequest request,
        DateTimeOffset reviewedAtUtc);
}

public sealed class ModelBackedCriticReviewValidator
{
    public const string RequestIdRequired = "MODEL_BACKED_CRITIC_REQUEST_ID_REQUIRED";
    public const string ScopeRequired = "MODEL_BACKED_CRITIC_SCOPE_REQUIRED";
    public const string RequestedByRequired = "MODEL_BACKED_CRITIC_REQUESTED_BY_REQUIRED";
    public const string CorrelationIdRequired = "MODEL_BACKED_CRITIC_CORRELATION_ID_REQUIRED";
    public const string SubjectRequired = "MODEL_BACKED_CRITIC_SUBJECT_REQUIRED";
    public const string SpecialisationRequired = "MODEL_BACKED_CRITIC_SPECIALISATION_REQUIRED";
    public const string ProfileRequired = "MODEL_BACKED_CRITIC_MODEL_PROFILE_REQUIRED";
    public const string RequestSummaryRequired = "MODEL_BACKED_CRITIC_SUMMARY_REQUIRED";
    public const string InputsRequired = "MODEL_BACKED_CRITIC_INPUTS_REQUIRED";
    public const string InputUnsafe = "MODEL_BACKED_CRITIC_INPUT_UNSAFE";
    public const string SpecialisationInvalid = "MODEL_BACKED_CRITIC_SPECIALISATION_INVALID";
    public const string ProfileInvalid = "MODEL_BACKED_CRITIC_MODEL_PROFILE_INVALID";
    public const string ResponseFormatInvalid = "MODEL_BACKED_CRITIC_RESPONSE_FORMAT_INVALID";
    public const string AgentDefinitionInvalid = "MODEL_BACKED_CRITIC_AGENT_DEFINITION_INVALID";

    private readonly AgentSpecialisationValidator _specialisationValidator;
    private readonly AgentDefinitionValidator _agentDefinitionValidator;
    private readonly IReadOnlyList<AgentSpecialisationDefinition> _specialisations;

    public ModelBackedCriticReviewValidator(
        AgentSpecialisationValidator? specialisationValidator = null,
        AgentDefinitionValidator? agentDefinitionValidator = null,
        IReadOnlyList<AgentSpecialisationDefinition>? specialisations = null)
    {
        _specialisationValidator = specialisationValidator ?? new AgentSpecialisationValidator();
        _agentDefinitionValidator = agentDefinitionValidator ?? new AgentDefinitionValidator();
        _specialisations = specialisations ?? AgentSpecialisationCatalog.All;
    }

    public IReadOnlyList<ModelBackedCriticReviewIssue> Validate(ModelBackedCriticReviewRequest request)
    {
        var issues = new List<ModelBackedCriticReviewIssue>();

        if (string.IsNullOrWhiteSpace(request.ReviewRequestId))
            AddError(issues, RequestIdRequired, "ReviewRequestId is required.", nameof(request.ReviewRequestId));

        if (string.IsNullOrWhiteSpace(request.TenantId) ||
            string.IsNullOrWhiteSpace(request.ProjectId) ||
            string.IsNullOrWhiteSpace(request.CampaignId) ||
            string.IsNullOrWhiteSpace(request.RunId))
        {
            AddError(issues, ScopeRequired, "TenantId, ProjectId, CampaignId, and RunId are required.", "Scope");
        }

        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
            AddError(issues, RequestedByRequired, "RequestedByUserId is required.", nameof(request.RequestedByUserId));

        if (string.IsNullOrWhiteSpace(request.CorrelationId))
            AddError(issues, CorrelationIdRequired, "CorrelationId is required.", nameof(request.CorrelationId));

        if (string.IsNullOrWhiteSpace(request.SubjectId) || !Enum.IsDefined(request.SubjectType))
            AddError(issues, SubjectRequired, "SubjectId and valid SubjectType are required.", nameof(request.SubjectId));

        if (string.IsNullOrWhiteSpace(request.SpecialisationId))
            AddError(issues, SpecialisationRequired, "SpecialisationId is required.", nameof(request.SpecialisationId));

        if (request.ModelProfile is null)
            AddError(issues, ProfileRequired, "ModelProfile is required.", nameof(request.ModelProfile));

        if (string.IsNullOrWhiteSpace(request.RequestSummary))
            AddError(issues, RequestSummaryRequired, "RequestSummary is required.", nameof(request.RequestSummary));

        if (request.Inputs.Count == 0)
            AddError(issues, InputsRequired, "At least one safe review input is required.", nameof(request.Inputs));

        foreach (var input in request.Inputs)
        {
            if (string.IsNullOrWhiteSpace(input.InputRefId) ||
                string.IsNullOrWhiteSpace(input.RefType) ||
                string.IsNullOrWhiteSpace(input.RefId) ||
                input.ContainsRawPrivateReasoning ||
                input.IsAuthoritativeForAction)
            {
                AddError(issues, InputUnsafe, "Inputs must be identified, non-authoritative, and free of raw private reasoning.", nameof(request.Inputs));
            }
        }

        ValidateIndependentCriticDefinition(issues);
        ValidateSpecialisation(request, issues);
        ValidateModelProfile(request, issues);
        ValidateResponseFormat(request, issues);

        return issues;
    }

    private void ValidateIndependentCriticDefinition(List<ModelBackedCriticReviewIssue> issues)
    {
        var definition = AgentDefinitionCatalog.IndependentCriticAgent;
        foreach (var issue in _agentDefinitionValidator.Validate(definition))
        {
            AddError(issues, AgentDefinitionInvalid, $"IndependentCriticAgent definition invalid: {issue.Code}.", "AgentDefinition");
        }

        if (definition.Kind != AgentKind.ReviewAgent ||
            definition.ExecutionMode != AgentExecutionMode.OutOfBandReviewOnly ||
            definition.Capabilities?.Contains(AgentCapability.CreateCriticFinding) != true ||
            definition.ForbiddenCapabilities?.Contains(AgentCapability.RunTool) != true ||
            definition.ForbiddenCapabilities?.Contains(AgentCapability.MutateSource) != true)
        {
            AddError(issues, AgentDefinitionInvalid, "IndependentCriticAgent must remain a boxed review-only critic.", "AgentDefinition");
        }
    }

    private void ValidateSpecialisation(ModelBackedCriticReviewRequest request, List<ModelBackedCriticReviewIssue> issues)
    {
        var specialisation = _specialisations.FirstOrDefault(candidate =>
            string.Equals(candidate.SpecialisationId, request.SpecialisationId, StringComparison.Ordinal));
        if (specialisation is null ||
            !AgentSpecialisationCatalog.CriticProfiles.Any(profile => string.Equals(profile.SpecialisationId, request.SpecialisationId, StringComparison.Ordinal)))
        {
            AddError(issues, SpecialisationInvalid, "Only known critic specialisations are allowed.", nameof(request.SpecialisationId));
            return;
        }

        foreach (var issue in _specialisationValidator.Validate(specialisation)
                     .Where(issue => string.Equals(issue.Severity, AgentDefinitionValidator.SeverityError, StringComparison.Ordinal)))
        {
            AddError(issues, SpecialisationInvalid, $"Selected critic specialisation is invalid: {issue.Code}.", nameof(request.SpecialisationId));
        }

        var compatibility = _specialisationValidator.ValidateCompatibility(AgentDefinitionCatalog.IndependentCriticAgent, specialisation);
        if (!compatibility.IsCompatible)
            AddError(issues, SpecialisationInvalid, "Selected critic specialisation is not compatible with IndependentCriticAgent.", nameof(request.SpecialisationId));

        if (specialisation.RequiredAgentKind != AgentKind.ReviewAgent ||
            specialisation.RequiredExecutionMode != AgentExecutionMode.OutOfBandReviewOnly ||
            specialisation.AuthorityBoundary is null ||
            GrantsAuthority(specialisation.AuthorityBoundary) ||
            !specialisation.OutputRequirements.Any(output =>
                string.Equals(output.OutputType, nameof(CriticReviewResult), StringComparison.Ordinal) &&
                output.RequiresHumanReview &&
                output.MustBeReviewOnly &&
                !output.MayCreateAuthority &&
                !output.MayCreateRuntimeAction &&
                !output.MayPromoteMemory))
        {
            AddError(issues, SpecialisationInvalid, "Selected critic specialisation must be review-only, human-reviewed, and non-authoritative.", nameof(request.SpecialisationId));
        }
    }

    private static void ValidateModelProfile(ModelBackedCriticReviewRequest request, List<ModelBackedCriticReviewIssue> issues)
    {
        var profile = request.ModelProfile;
        if (profile is null)
            return;

        if (profile.ProviderKind != AgentModelProviderKind.Fake ||
            !profile.IsEnabled ||
            profile.AllowsToolCalls ||
            profile.AllowsExternalNetwork ||
            string.IsNullOrWhiteSpace(profile.ProfileId) ||
            string.IsNullOrWhiteSpace(profile.ModelName) ||
            profile.AllowedAgentIds.Count == 0 ||
            !profile.AllowedAgentIds.Contains(AgentDefinitionCatalog.IndependentCriticAgent.AgentId, StringComparer.Ordinal) ||
            profile.AllowedSpecialisationIds.Count == 0 ||
            !profile.AllowedSpecialisationIds.Contains(request.SpecialisationId, StringComparer.Ordinal))
        {
            AddError(issues, ProfileInvalid, "PR-35 accepts only fake, non-tool, non-network profiles scoped to IndependentCriticAgent and the selected critic specialisation.", nameof(request.ModelProfile));
        }
    }

    private static void ValidateResponseFormat(ModelBackedCriticReviewRequest request, List<ModelBackedCriticReviewIssue> issues)
    {
        var format = request.ResponseFormat;
        if (format is null ||
            string.IsNullOrWhiteSpace(format.FormatId) ||
            !string.Equals(format.OutputContractName, nameof(CriticReviewResult), StringComparison.Ordinal) ||
            !format.RequiresJson ||
            !format.RequiresSchemaValidation)
        {
            AddError(issues, ResponseFormatInvalid, "ResponseFormat must require schema-bound CriticReviewResult JSON.", nameof(request.ResponseFormat));
        }
    }

    private static bool GrantsAuthority(AgentSpecialisationAuthorityBoundary boundary) =>
        boundary.CanGrantApproval ||
        boundary.CanRepresentHumanDecision ||
        boundary.CanOverridePolicy ||
        boundary.CanExecuteTools ||
        boundary.CanMutateSource ||
        boundary.CanCallExternalSystems ||
        boundary.CanPromoteMemory ||
        boundary.CanCreateAuthority ||
        boundary.CanCreateRuntimeAction ||
        boundary.CanWriteMemory;

    private static void AddError(
        ICollection<ModelBackedCriticReviewIssue> issues,
        string code,
        string message,
        string field) =>
        issues.Add(new ModelBackedCriticReviewIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        });
}

public sealed class ModelBackedCriticPromptBuilder
{
    public AgentModelRequest Build(ModelBackedCriticReviewRequest request, DateTimeOffset createdAtUtc)
    {
        var messageLines = new List<string>
        {
            "You are producing a review-only critic result for human review.",
            "You may identify risks, weaknesses, missing evidence, and recommended fixes.",
            "Keep permission decisions outside this output.",
            "Keep policy decisions outside this output.",
            "Keep human decisions outside this output.",
            "Do not request operational action.",
            "Do not request tool usage.",
            "Do not request repository edits.",
            "Do not request external publication.",
            "Do not claim memory authority.",
            "Return only the requested typed critic output candidate.",
            $"SubjectType: {request.SubjectType}",
            $"SubjectId: {request.SubjectId}",
            $"Summary: {Bound(request.RequestSummary, 480)}"
        };

        foreach (var input in request.Inputs.Take(12))
        {
            messageLines.Add($"Input {input.InputRefId}: {Bound(input.Summary, 360)}");
        }

        return new AgentModelRequest
        {
            RequestId = request.ReviewRequestId,
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            CampaignId = request.CampaignId,
            AgentRunId = request.RunId,
            AgentId = AgentDefinitionCatalog.IndependentCriticAgent.AgentId,
            SpecialisationId = request.SpecialisationId,
            Profile = request.ModelProfile,
            Messages =
            [
                new AgentModelMessage
                {
                    MessageId = $"message-{request.ReviewRequestId}-001",
                    Role = AgentModelRole.User,
                    Content = string.Join(Environment.NewLine, messageLines),
                    EvidenceRefs = request.EvidenceRefs.Concat(request.Inputs.SelectMany(input => input.EvidenceRefs)).Distinct(StringComparer.Ordinal).ToArray()
                }
            ],
            Context = new AgentModelRequestContext
            {
                InputRefs = request.Inputs.Select(input => input.InputRefId).ToArray(),
                EvidenceRefs = request.EvidenceRefs.Concat(request.Inputs.SelectMany(input => input.EvidenceRefs)).Distinct(StringComparer.Ordinal).ToArray()
            },
            ResponseFormat = request.ResponseFormat,
            SafetyFlags = new AgentModelSafetyFlags(),
            CreatedAtUtc = createdAtUtc
        };
    }

    private static string Bound(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Length <= maxLength
            ? value
            : string.Concat(value.AsSpan(0, maxLength), "...");
    }
}

public sealed class ModelBackedCriticResponseParser
{
    public const string ResponseMissing = "MODEL_BACKED_CRITIC_RESPONSE_MISSING";
    public const string ResponseJsonMissing = "MODEL_BACKED_CRITIC_RESPONSE_JSON_MISSING";
    public const string ResponseJsonInvalid = "MODEL_BACKED_CRITIC_RESPONSE_JSON_INVALID";
    public const string VerdictInvalid = "MODEL_BACKED_CRITIC_VERDICT_INVALID";
    public const string FindingRequired = "MODEL_BACKED_CRITIC_FINDING_REQUIRED";
    public const string FindingEvidenceRequired = "MODEL_BACKED_CRITIC_FINDING_EVIDENCE_REQUIRED";
    public const string FindingRequiredFixRequired = "MODEL_BACKED_CRITIC_FINDING_REQUIRED_FIX_REQUIRED";
    public const string UnsafeParsedContent = "MODEL_BACKED_CRITIC_PARSED_CONTENT_UNSAFE";

    private static readonly IReadOnlyList<string> UnsafeMarkers =
    [
        "approval granted",
        "approved for execution",
        "policy cleared",
        "human approved",
        "authoritative for action",
        "run this tool",
        "call this tool",
        "execute tool",
        "apply this patch",
        "mutate source",
        "write file",
        "delete file",
        "modify source",
        "promote memory",
        "accepted memory",
        "create CollectiveMemory",
        "persist proposal"
    ];

    public ParsedCriticReviewResult Parse(
        ModelBackedCriticReviewRequest request,
        AgentModelSanitisedResponse? response,
        DateTimeOffset reviewedAtUtc)
    {
        var issues = new List<ModelBackedCriticReviewIssue>();

        if (response is null)
        {
            AddError(issues, ResponseMissing, "Sanitised response is required.", nameof(response));
            return new ParsedCriticReviewResult(null, issues);
        }

        var json = response.StructuredJsonCandidate;
        if (string.IsNullOrWhiteSpace(json))
        {
            AddError(issues, ResponseJsonMissing, "StructuredJsonCandidate is required for model-backed critic parsing.", nameof(response.StructuredJsonCandidate));
            return new ParsedCriticReviewResult(null, issues);
        }

        ModelCriticJson? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ModelCriticJson>(json, JsonOptions);
        }
        catch (JsonException)
        {
            AddError(issues, ResponseJsonInvalid, "Structured critic JSON candidate could not be parsed.", nameof(response.StructuredJsonCandidate));
            return new ParsedCriticReviewResult(null, issues);
        }

        if (payload is null)
        {
            AddError(issues, ResponseJsonInvalid, "Structured critic JSON candidate was empty.", nameof(response.StructuredJsonCandidate));
            return new ParsedCriticReviewResult(null, issues);
        }

        if (!Enum.TryParse<CriticReviewVerdict>(payload.Verdict, ignoreCase: true, out var verdict) ||
            !Enum.IsDefined(verdict))
        {
            AddError(issues, VerdictInvalid, "Critic verdict is invalid.", nameof(payload.Verdict));
        }

        var findings = new List<CriticFinding>();
        for (var index = 0; index < payload.Findings.Count; index++)
        {
            var finding = payload.Findings[index];
            var field = $"{nameof(payload.Findings)}[{index}]";

            if (ContainsUnsafe([finding.Title, finding.Problem, finding.WhyItMatters, finding.RequiredFix, .. finding.EvidenceRefs]))
                AddError(issues, UnsafeParsedContent, "Parsed finding contains authority/action/promotion text.", field);

            if (finding.EvidenceRefs.Count == 0 || finding.EvidenceRefs.Any(string.IsNullOrWhiteSpace))
                AddError(issues, FindingEvidenceRequired, "Parsed findings require evidence refs.", $"{field}.evidenceRefs");

            if (string.IsNullOrWhiteSpace(finding.RequiredFix))
                AddError(issues, FindingRequiredFixRequired, "Parsed findings require a required fix.", $"{field}.requiredFix");

            if (!Enum.TryParse<CriticSeverity>(finding.Severity, ignoreCase: true, out var severity) ||
                !Enum.IsDefined(severity))
            {
                severity = CriticSeverity.Medium;
            }

            findings.Add(new CriticFinding
            {
                FindingId = $"critic-finding-{request.ReviewRequestId}-{index + 1:000}",
                Severity = severity,
                Title = finding.Title ?? string.Empty,
                Problem = finding.Problem ?? string.Empty,
                WhyItMatters = finding.WhyItMatters ?? string.Empty,
                RequiredFix = finding.RequiredFix ?? string.Empty,
                EvidenceRefs = finding.EvidenceRefs,
                BlocksMerge = finding.BlocksMerge,
                RequiresHumanReview = true
            });
        }

        if ((verdict is CriticReviewVerdict.RequestChanges or CriticReviewVerdict.RecommendBlock) && findings.Count == 0)
            AddError(issues, FindingRequired, "RequestChanges and RecommendBlock verdicts require findings.", nameof(payload.Findings));

        if (verdict == CriticReviewVerdict.RecommendBlock && findings.All(finding => !finding.BlocksMerge))
            AddError(issues, FindingRequired, "RecommendBlock verdict requires a blocking finding.", nameof(payload.Findings));

        if (ContainsUnsafe([payload.Summary, payload.Verdict]))
            AddError(issues, UnsafeParsedContent, "Parsed critic summary or verdict contains authority/action/promotion text.", nameof(payload.Summary));

        if (issues.Count > 0)
            return new ParsedCriticReviewResult(null, issues);

        return new ParsedCriticReviewResult(
            new CriticReviewResult
            {
                ReviewResultId = $"critic-review-{request.ReviewRequestId}",
                ReviewRequestId = request.ReviewRequestId,
                Verdict = verdict,
                Findings = findings,
                ReviewedAt = reviewedAtUtc,
                ReviewedByAgentId = AgentDefinitionCatalog.IndependentCriticAgent.AgentId,
                CorrelationId = request.CorrelationId,
                Warnings =
                [
                    "Critic findings are recommendations only.",
                    "Critic review does not grant or deny approval.",
                    "Governance and human approval remain separate.",
                    "Critic review does not enforce blocks.",
                    "Critic review does not mutate source."
                ]
            },
            []);
    }

    private static bool ContainsUnsafe(IEnumerable<string?> values) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value) &&
                            UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));

    private static void AddError(
        ICollection<ModelBackedCriticReviewIssue> issues,
        string code,
        string message,
        string field) =>
        issues.Add(new ModelBackedCriticReviewIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        });

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record ModelCriticJson
    {
        public string Summary { get; init; } = string.Empty;
        public string Verdict { get; init; } = string.Empty;
        public IReadOnlyList<ModelCriticFindingJson> Findings { get; init; } = [];
    }

    private sealed record ModelCriticFindingJson
    {
        public string Severity { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Problem { get; init; } = string.Empty;
        public string WhyItMatters { get; init; } = string.Empty;
        public string RequiredFix { get; init; } = string.Empty;
        public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
        public bool BlocksMerge { get; init; }
    }
}

public sealed record ParsedCriticReviewResult(
    CriticReviewResult? CriticReviewResult,
    IReadOnlyList<ModelBackedCriticReviewIssue> Issues);

public sealed class ModelBackedManualIndependentCriticAgentService : IModelBackedManualIndependentCriticAgentService
{
    private readonly IAgentModelAdapter _modelAdapter;
    private readonly IAgentModelAuditSanitiser _auditSanitiser;
    private readonly AgentModelAdapterValidator _modelAdapterValidator;
    private readonly ModelBackedCriticReviewValidator _requestValidator;
    private readonly ModelBackedCriticPromptBuilder _promptBuilder;
    private readonly ModelBackedCriticResponseParser _responseParser;
    private readonly CriticReviewResultValidator _criticReviewValidator;
    private readonly AgentRunAuditEnvelopeValidator _auditEnvelopeValidator;
    private readonly ThoughtLedgerSafetyValidator _thoughtLedgerSafetyValidator;

    public ModelBackedManualIndependentCriticAgentService(
        IAgentModelAdapter modelAdapter,
        IAgentModelAuditSanitiser auditSanitiser,
        AgentModelAdapterValidator? modelAdapterValidator = null,
        ModelBackedCriticReviewValidator? requestValidator = null,
        ModelBackedCriticPromptBuilder? promptBuilder = null,
        ModelBackedCriticResponseParser? responseParser = null,
        CriticReviewResultValidator? criticReviewValidator = null,
        AgentRunAuditEnvelopeValidator? auditEnvelopeValidator = null,
        ThoughtLedgerSafetyValidator? thoughtLedgerSafetyValidator = null)
    {
        _modelAdapter = modelAdapter ?? throw new ArgumentNullException(nameof(modelAdapter));
        _auditSanitiser = auditSanitiser ?? throw new ArgumentNullException(nameof(auditSanitiser));
        _modelAdapterValidator = modelAdapterValidator ?? new AgentModelAdapterValidator();
        _requestValidator = requestValidator ?? new ModelBackedCriticReviewValidator();
        _promptBuilder = promptBuilder ?? new ModelBackedCriticPromptBuilder();
        _responseParser = responseParser ?? new ModelBackedCriticResponseParser();
        _criticReviewValidator = criticReviewValidator ?? new CriticReviewResultValidator();
        _auditEnvelopeValidator = auditEnvelopeValidator ?? new AgentRunAuditEnvelopeValidator();
        _thoughtLedgerSafetyValidator = thoughtLedgerSafetyValidator ?? new ThoughtLedgerSafetyValidator();
    }

    public ModelBackedCriticReviewResult Review(
        ModelBackedCriticReviewRequest request,
        DateTimeOffset reviewedAtUtc)
    {
        var runId = BuildRunId(request.ReviewRequestId);
        var issues = new List<ModelBackedCriticReviewIssue>();
        issues.AddRange(_requestValidator.Validate(request));
        if (issues.Count > 0)
            return Failed(runId, issues);

        var modelRequest = _promptBuilder.Build(request, reviewedAtUtc);
        issues.AddRange(ToIssues(_modelAdapterValidator.ValidateRequest(modelRequest), "AgentModelRequest"));
        if (issues.Count > 0)
            return Failed(runId, issues);

        var adapterResult = _modelAdapter.Invoke(modelRequest, reviewedAtUtc);
        if (!adapterResult.Succeeded || adapterResult.Response is null || adapterResult.Audit is null)
        {
            issues.AddRange(ToIssues(adapterResult.Issues, "AgentModelAdapter"));
            if (issues.Count == 0)
                AddError(issues, "MODEL_BACKED_CRITIC_ADAPTER_FAILED", "Model adapter failed without a typed response and audit.", "AgentModelAdapter");

            return Failed(runId, issues, adapterResult);
        }

        var sanitisation = _auditSanitiser.Sanitise(new AgentModelSanitisationRequest
        {
            Request = modelRequest,
            Response = adapterResult.Response,
            Audit = adapterResult.Audit,
            AllowRedactedPreview = false,
            AllowStructuredJsonCandidate = true
        });
        if (sanitisation.Status == AgentModelSanitisationStatus.Rejected ||
            sanitisation.Response is null ||
            sanitisation.Audit is null)
        {
            issues.AddRange(ToIssues(sanitisation.Issues, "AgentModelSanitisation"));
            if (issues.Count == 0)
                AddError(issues, "MODEL_BACKED_CRITIC_SANITISATION_REJECTED", "Model sanitisation rejected retention.", "AgentModelSanitisation");

            return Failed(runId, issues, adapterResult, sanitisation);
        }

        var parsed = _responseParser.Parse(request, sanitisation.Response, reviewedAtUtc);
        if (parsed.CriticReviewResult is null)
            return Failed(runId, parsed.Issues, adapterResult, sanitisation);

        issues.AddRange(ToIssues(_criticReviewValidator.Validate(parsed.CriticReviewResult), "CriticReviewResult"));
        if (issues.Count > 0)
            return Failed(runId, issues, adapterResult, sanitisation);

        var auditEnvelope = BuildAuditEnvelope(request, modelRequest, adapterResult, sanitisation, parsed.CriticReviewResult, runId, reviewedAtUtc);
        issues.AddRange(ToIssues(_auditEnvelopeValidator.Validate(auditEnvelope), "AgentRunAuditEnvelope"));
        issues.AddRange(ToIssues(_thoughtLedgerSafetyValidator.Validate(auditEnvelope.ThoughtLedger), "ThoughtLedger"));
        if (issues.Count > 0)
            return Failed(runId, issues, adapterResult, sanitisation);

        return new ModelBackedCriticReviewResult
        {
            Succeeded = true,
            ModelBackedCriticRunId = runId,
            CriticReviewResult = parsed.CriticReviewResult,
            AuditEnvelope = auditEnvelope,
            ModelAdapterResult = adapterResult,
            SanitisationResult = sanitisation,
            Issues = []
        };
    }

    private static AgentRunAuditEnvelope BuildAuditEnvelope(
        ModelBackedCriticReviewRequest request,
        AgentModelRequest modelRequest,
        AgentModelAdapterResult adapterResult,
        AgentModelSanitisationResult sanitisation,
        CriticReviewResult criticReviewResult,
        string runId,
        DateTimeOffset reviewedAtUtc)
    {
        var evidenceRefs = BuildEvidenceRefs(request, modelRequest, sanitisation, criticReviewResult);
        var status = criticReviewResult.Verdict == CriticReviewVerdict.NoObjection
            ? IronDev.Core.Agents.Audit.AgentRunStatus.Completed
            : IronDev.Core.Agents.Audit.AgentRunStatus.CompletedWithWarnings;

        return new AgentRunAuditEnvelope
        {
            Run = new AgentRunRecord
            {
                AgentRunId = runId,
                TenantId = request.TenantId,
                ProjectId = request.ProjectId,
                CampaignId = request.CampaignId ?? string.Empty,
                RunId = request.RunId ?? string.Empty,
                AgentId = AgentDefinitionCatalog.IndependentCriticAgent.AgentId,
                AgentName = AgentDefinitionCatalog.IndependentCriticAgent.Name,
                RequestedByUserId = request.RequestedByUserId,
                TriggerType = AgentRunTriggerType.ManualUserRequest,
                Status = status,
                RequestSummary = "Model-backed manual IndependentCriticAgent review over supplied evidence.",
                Purpose = "Model-backed manual critic review through adapter and sanitiser boundaries.",
                CreatedAtUtc = reviewedAtUtc,
                StartedAtUtc = reviewedAtUtc,
                CompletedAtUtc = reviewedAtUtc
            },
            AgentDefinitionSnapshot = AgentDefinitionCatalog.IndependentCriticAgent,
            Inputs = BuildInputs(request, modelRequest, sanitisation, runId),
            Outputs =
            [
                new AgentRunOutputRef
                {
                    OutputRefId = $"output-{criticReviewResult.ReviewResultId}",
                    AgentRunId = runId,
                    RefType = nameof(CriticReviewResult),
                    RefId = criticReviewResult.ReviewResultId,
                    Summary = "Model-backed IndependentCriticAgent produced a review-only CriticReviewResult from sanitised model output.",
                    IsReviewOnly = true,
                    IsProposalOnly = false,
                    CreatesAuthority = false,
                    CreatesRuntimeAction = false,
                    ContainsRawPrivateReasoning = false,
                    EvidenceRefs = evidenceRefs
                }
            ],
            Steps = BuildSteps(runId, evidenceRefs, reviewedAtUtc),
            CapabilityUses = BuildCapabilityUses(runId),
            BoundaryDecisions = BuildBoundaryDecisions(runId, adapterResult, sanitisation, evidenceRefs),
            ThoughtLedger = BuildThoughtLedger(runId, evidenceRefs, reviewedAtUtc)
        };
    }

    private static IReadOnlyList<AgentRunInputRef> BuildInputs(
        ModelBackedCriticReviewRequest request,
        AgentModelRequest modelRequest,
        AgentModelSanitisationResult sanitisation,
        string runId)
    {
        var inputs = request.Inputs.Select(input => new AgentRunInputRef
        {
            InputRefId = input.InputRefId,
            AgentRunId = runId,
            RefType = input.RefType,
            RefId = input.RefId,
            Source = input.Source,
            Summary = input.Summary,
            IsAuthoritativeForAction = false,
            ContainsRawPrivateReasoning = false
        }).ToList();

        inputs.Add(new AgentRunInputRef
        {
            InputRefId = $"input-specialisation-{request.SpecialisationId}",
            AgentRunId = runId,
            RefType = "AgentSpecialisationDefinition",
            RefId = request.SpecialisationId,
            Source = "AgentSpecialisationCatalog.CriticProfiles",
            Summary = "Selected critic specialisation is non-authoritative review focus metadata.",
            IsAuthoritativeForAction = false,
            ContainsRawPrivateReasoning = false
        });

        inputs.Add(new AgentRunInputRef
        {
            InputRefId = $"input-model-audit-{modelRequest.RequestId}",
            AgentRunId = runId,
            RefType = "AgentModelSanitisedInvocationAudit",
            RefId = sanitisation.Audit?.AuditId ?? $"model-audit-{modelRequest.RequestId}",
            Source = "IAgentModelAuditSanitiser",
            Summary = $"Model provider {request.ModelProfile.ProviderKind} invocation metadata was sanitised with status {sanitisation.Status}.",
            IsAuthoritativeForAction = false,
            ContainsRawPrivateReasoning = false
        });

        return inputs;
    }

    private static IReadOnlyList<AgentRunStep> BuildSteps(
        string runId,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset reviewedAtUtc) =>
        [
            Step(runId, 1, AgentRunStepType.Created, "Model-backed manual critic run was created.", evidenceRefs, reviewedAtUtc),
            Step(runId, 2, AgentRunStepType.InputBound, "Model critic request was built from supplied evidence summaries.", evidenceRefs, reviewedAtUtc),
            Step(runId, 3, AgentRunStepType.BoundaryDecision, "Model adapter boundary accepted the fake/scripted model request.", evidenceRefs, reviewedAtUtc),
            Step(runId, 4, AgentRunStepType.BoundaryDecision, "Sanitiser accepted the response for safe retention.", evidenceRefs, reviewedAtUtc),
            Step(runId, 5, AgentRunStepType.OutputRecorded, "Critic result was parsed as review-only output.", evidenceRefs, reviewedAtUtc),
            Step(runId, 6, AgentRunStepType.Completed, "Dangerous capabilities remained blocked and human review remains required.", evidenceRefs, reviewedAtUtc)
        ];

    private static AgentRunStep Step(
        string runId,
        int sequence,
        AgentRunStepType stepType,
        string summary,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset reviewedAtUtc) =>
        new()
        {
            StepId = $"step-{runId}-{sequence:000}",
            AgentRunId = runId,
            Sequence = sequence,
            StepType = stepType,
            OccurredAtUtc = reviewedAtUtc,
            Summary = summary,
            EvidenceRefs = evidenceRefs
        };

    private static IReadOnlyList<AgentCapabilityUseRecord> BuildCapabilityUses(string runId) =>
        [
            CapabilityUse(runId, AgentCapability.CreateCriticFinding, AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(runId, AgentCapability.CreateReport, AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(runId, AgentCapability.WarnExecution, AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(runId, AgentCapability.BlockExecution, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.RunTool, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.MutateSource, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.CallExternalSystem, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.PromoteCollectiveMemory, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.RepresentHumanApproval, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.RepresentHumanPromotionDecision, AgentCapabilityUseOutcome.Blocked)
        ];

    private static AgentCapabilityUseRecord CapabilityUse(
        string runId,
        AgentCapability capability,
        AgentCapabilityUseOutcome outcome)
    {
        var definition = AgentDefinitionCatalog.IndependentCriticAgent;
        return new AgentCapabilityUseRecord
        {
            CapabilityUseId = $"capability-{runId}-{capability}",
            AgentRunId = runId,
            Capability = capability,
            Outcome = outcome,
            Summary = $"{capability} was {outcome} for model-backed manual critic review.",
            PolicyDecisionId = $"policy-{runId}",
            BoundaryDecisionId = $"boundary-{runId}-{capability}",
            EvidenceRef = $"evidence-{runId}",
            WasDeclaredOnAgent = definition.Capabilities?.Contains(capability) == true,
            WasForbiddenOnAgent = definition.ForbiddenCapabilities?.Contains(capability) == true
        };
    }

    private static IReadOnlyList<AgentBoundaryDecision> BuildBoundaryDecisions(
        string runId,
        AgentModelAdapterResult adapterResult,
        AgentModelSanitisationResult sanitisation,
        IReadOnlyList<string> evidenceRefs) =>
        [
            BoundaryDecision(runId, "model-adapter", AgentBoundaryDecisionType.Safety, "allow", $"Model adapter returned success={adapterResult.Succeeded} through the fake/scripted boundary.", evidenceRefs),
            BoundaryDecision(runId, "model-sanitisation", AgentBoundaryDecisionType.Safety, "allow", $"Model material sanitisation status was {sanitisation.Status}.", evidenceRefs),
            BoundaryDecision(runId, "output-validation", AgentBoundaryDecisionType.OutputValidation, "allow", "CriticReviewResult passed review-only validation.", evidenceRefs),
            BoundaryDecision(runId, "thought-ledger-safety", AgentBoundaryDecisionType.ThoughtLedgerSafety, "allow", "ThoughtLedger entries contain safe rationale only.", evidenceRefs),
            BoundaryDecision(runId, "block-execution", AgentBoundaryDecisionType.Capability, "block", "BlockExecution remains unavailable to the model-backed critic.", evidenceRefs),
            BoundaryDecision(runId, "run-tool", AgentBoundaryDecisionType.Capability, "block", "RunTool remains unavailable to the model-backed critic.", evidenceRefs),
            BoundaryDecision(runId, "mutate-source", AgentBoundaryDecisionType.Capability, "block", "MutateSource remains unavailable to the model-backed critic.", evidenceRefs),
            BoundaryDecision(runId, "external-review-publication", AgentBoundaryDecisionType.Capability, "block", "External review publication remains unavailable to the model-backed critic.", evidenceRefs),
            BoundaryDecision(runId, "shared-memory-authority", AgentBoundaryDecisionType.Capability, "block", "Shared memory authority remains unavailable to the model-backed critic.", evidenceRefs)
        ];

    private static AgentBoundaryDecision BoundaryDecision(
        string runId,
        string suffix,
        AgentBoundaryDecisionType boundaryType,
        string decision,
        string reason,
        IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            BoundaryDecisionId = $"boundary-{runId}-{suffix}",
            AgentRunId = runId,
            BoundaryType = boundaryType,
            Decision = decision,
            Reason = reason,
            SourceRefId = $"model-backed-critic-{suffix}",
            GrantsAuthority = false,
            GrantsHumanApproval = false,
            GrantsPolicyApproval = false,
            GrantsMemoryPromotion = false,
            EvidenceRefs = evidenceRefs
        };

    private static IReadOnlyList<AuditThoughtLedgerEntry> BuildThoughtLedger(
        string runId,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset reviewedAtUtc) =>
        [
            Thought(runId, "request-built", ThoughtLedgerEntryType.EvidenceUsed, "Model critic request was built from supplied evidence summaries.", evidenceRefs, reviewedAtUtc),
            Thought(runId, "adapter-returned", ThoughtLedgerEntryType.BoundaryDecision, "Model adapter returned a response candidate through the fake/scripted boundary.", evidenceRefs, reviewedAtUtc),
            Thought(runId, "sanitiser-accepted", ThoughtLedgerEntryType.BoundaryDecision, "Sanitiser accepted the response for safe retention.", evidenceRefs, reviewedAtUtc),
            Thought(runId, "parsed-review-only", ThoughtLedgerEntryType.OutputRationale, "Critic result was parsed as review-only output.", evidenceRefs, reviewedAtUtc),
            Thought(runId, "capabilities-blocked", ThoughtLedgerEntryType.BoundaryDecision, "Dangerous capabilities remained blocked.", evidenceRefs, reviewedAtUtc),
            Thought(runId, "human-review", ThoughtLedgerEntryType.FollowUp, "Human review remains required before any action.", evidenceRefs, reviewedAtUtc)
        ];

    private static AuditThoughtLedgerEntry Thought(
        string runId,
        string suffix,
        ThoughtLedgerEntryType entryType,
        string summary,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset reviewedAtUtc) =>
        new()
        {
            ThoughtLedgerEntryId = $"thought-{runId}-{suffix}",
            AgentRunId = runId,
            EntryType = entryType,
            Summary = summary,
            EvidenceRefs = evidenceRefs,
            RecordedAtUtc = reviewedAtUtc,
            ContainsRawPrivateReasoning = false,
            GrantsAuthority = false,
            GrantsApproval = false,
            GrantsMemoryPromotion = false
        };

    private static IReadOnlyList<string> BuildEvidenceRefs(
        ModelBackedCriticReviewRequest request,
        AgentModelRequest modelRequest,
        AgentModelSanitisationResult sanitisation,
        CriticReviewResult criticReviewResult)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var evidenceRef in request.EvidenceRefs)
            refs.Add(evidenceRef);
        foreach (var input in request.Inputs)
        {
            refs.Add(input.InputRefId);
            refs.Add(input.RefId);
            foreach (var evidenceRef in input.EvidenceRefs)
                refs.Add(evidenceRef);
        }
        foreach (var evidenceRef in modelRequest.Context.EvidenceRefs)
            refs.Add(evidenceRef);
        foreach (var evidenceRef in sanitisation.Prompt?.EvidenceRefs ?? [])
            refs.Add(evidenceRef);
        foreach (var evidenceRef in sanitisation.Audit?.EvidenceRefs ?? [])
            refs.Add(evidenceRef);
        foreach (var finding in criticReviewResult.Findings)
        {
            foreach (var evidenceRef in finding.EvidenceRefs)
                refs.Add(evidenceRef);
        }
        refs.Add($"model-audit-{modelRequest.RequestId}");
        refs.Add($"sanitisation-{modelRequest.RequestId}");
        refs.Add(criticReviewResult.ReviewResultId);
        return refs.Where(value => !string.IsNullOrWhiteSpace(value)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static ModelBackedCriticReviewResult Failed(
        string runId,
        IReadOnlyList<ModelBackedCriticReviewIssue> issues,
        AgentModelAdapterResult? adapterResult = null,
        AgentModelSanitisationResult? sanitisationResult = null) =>
        new()
        {
            Succeeded = false,
            ModelBackedCriticRunId = runId,
            ModelAdapterResult = adapterResult,
            SanitisationResult = sanitisationResult,
            Issues = issues.Count > 0
                ? issues
                :
                [
                    new ModelBackedCriticReviewIssue
                    {
                        Code = "MODEL_BACKED_CRITIC_FAILED",
                        Severity = AgentDefinitionValidator.SeverityError,
                        Message = "Model-backed critic review failed.",
                        Field = string.Empty
                    }
                ]
        };

    private static IReadOnlyList<ModelBackedCriticReviewIssue> ToIssues(
        IReadOnlyList<AgentModelAdapterIssue> issues,
        string field) =>
        issues.Select(issue => new ModelBackedCriticReviewIssue
        {
            Code = issue.Code,
            Severity = issue.Severity,
            Message = issue.Message,
            Field = issue.Field ?? field
        }).ToArray();

    private static IReadOnlyList<ModelBackedCriticReviewIssue> ToIssues(
        IReadOnlyList<AgentModelSanitisationIssue> issues,
        string field) =>
        issues.Select(issue => new ModelBackedCriticReviewIssue
        {
            Code = issue.Code,
            Severity = issue.Severity,
            Message = issue.Message,
            Field = string.IsNullOrWhiteSpace(issue.Field) ? field : issue.Field
        }).ToArray();

    private static IReadOnlyList<ModelBackedCriticReviewIssue> ToIssues(
        IReadOnlyList<AgentDefinitionValidationIssue> issues,
        string field) =>
        issues.Select(issue => new ModelBackedCriticReviewIssue
        {
            Code = issue.Code,
            Severity = issue.Severity,
            Message = issue.Message,
            Field = field
        }).ToArray();

    private static void AddError(
        ICollection<ModelBackedCriticReviewIssue> issues,
        string code,
        string message,
        string field) =>
        issues.Add(new ModelBackedCriticReviewIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        });

    private static string BuildRunId(string reviewRequestId) =>
        $"model-backed-independent-critic-{(string.IsNullOrWhiteSpace(reviewRequestId) ? "missing-request" : reviewRequestId)}";
}
