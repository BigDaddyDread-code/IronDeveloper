using IronDev.Core.Agents.Audit;

namespace IronDev.Core.Agents.Concrete;

public enum StoredManualAgentExecutionStatus
{
    Stored = 1,
    AlreadyStored = 2,
    Rejected = 3,
    Conflict = 4,
    AgentExecutionRejected = 5,
    AuditAppendFailed = 6,
    InvalidSpecialisation = 7
}

public sealed record StoredManualAgentExecutionIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string? Field { get; init; }
}

public sealed record ManualAgentExecutionSpecialisationSelection
{
    public required string SpecialisationId { get; init; }
    public required string RequestedByUserId { get; init; }
    public required string Reason { get; init; }
}

public sealed record StoredManualAgentExecutionResult<TOutput>
{
    public required StoredManualAgentExecutionStatus Status { get; init; }
    public required string AgentRunId { get; init; }
    public required string AgentId { get; init; }
    public required string SpecialisationId { get; init; }
    public TOutput? Output { get; init; }
    public AgentRunAuditEnvelope? AuditEnvelope { get; init; }
    public AgentRunAuditEnvelopeAppendResult? AppendResult { get; init; }
    public IReadOnlyList<StoredManualAgentExecutionIssue> Issues { get; init; } =
        Array.Empty<StoredManualAgentExecutionIssue>();
}

public interface IStoredManualIndependentCriticAgentService
{
    StoredManualAgentExecutionResult<CriticReviewResult> ExecuteAndStore(
        ManualCriticReviewRequest request,
        ManualAgentExecutionSpecialisationSelection specialisation,
        DateTimeOffset executedAtUtc);
}

public interface IStoredManualMemoryImprovementAgentService
{
    StoredManualAgentExecutionResult<MemoryImprovementDetectionResult> ExecuteAndStore(
        ManualMemoryImprovementDetectionRequest request,
        ManualAgentExecutionSpecialisationSelection specialisation,
        DateTimeOffset executedAtUtc);
}

public sealed class ManualAgentExecutionStoreValidator
{
    private static readonly string[] UnsafeReasonMarkers =
    [
        "raw prompt",
        "raw completion",
        "chain-of-thought",
        "chain of thought",
        "private reasoning",
        "scratchpad",
        "approved for execution",
        "approval granted",
        "policy override",
        "authority granted",
        "can execute",
        "may execute",
        "runtime action"
    ];

    private readonly AgentSpecialisationValidator _specialisationValidator = new();
    private readonly AgentRunAuditEnvelopeValidator _auditValidator = new();
    private readonly ThoughtLedgerSafetyValidator _thoughtLedgerValidator = new();
    private readonly CriticReviewResultValidator _criticReviewValidator = new();
    private readonly MemoryImprovementDetectionResultValidator _memoryImprovementValidator = new();

    public IReadOnlyList<StoredManualAgentExecutionIssue> ValidateCriticSpecialisation(
        ManualAgentExecutionSpecialisationSelection selection,
        out AgentSpecialisationDefinition? specialisation)
    {
        var issues = ValidateSelection(selection);
        specialisation = null;

        if (!string.IsNullOrWhiteSpace(selection.SpecialisationId))
        {
            specialisation = AgentSpecialisationCatalog.GetById(selection.SpecialisationId);
        }

        if (specialisation is null)
        {
            issues.Add(Error(
                "SPECIALISATION_NOT_FOUND",
                $"Specialisation '{selection.SpecialisationId}' was not found.",
                nameof(selection.SpecialisationId)));
            return issues;
        }

        if (!AgentSpecialisationCatalog.CriticProfiles.Any(item =>
                string.Equals(item.SpecialisationId, selection.SpecialisationId, StringComparison.Ordinal)))
        {
            issues.Add(Error(
                "SPECIALISATION_NOT_CRITIC_PROFILE",
                "Selected specialisation is not a critic profile.",
                nameof(selection.SpecialisationId)));
        }

        AddSpecialisationValidationIssues(issues, specialisation);

        var compatibility = _specialisationValidator.ValidateCompatibility(
            AgentDefinitionCatalog.IndependentCriticAgent,
            specialisation);

        foreach (var issue in compatibility.Issues.Where(item =>
                     string.Equals(item.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(ConvertIssue(issue, "SPECIALISATION_INCOMPATIBLE"));
        }

        if (specialisation.RequiredAgentKind != AgentKind.ReviewAgent)
        {
            issues.Add(Error(
                "SPECIALISATION_WRONG_AGENT_KIND",
                "Critic execution requires a review-agent specialisation.",
                nameof(specialisation.RequiredAgentKind)));
        }

        if (specialisation.RequiredExecutionMode != AgentExecutionMode.OutOfBandReviewOnly)
        {
            issues.Add(Error(
                "SPECIALISATION_WRONG_EXECUTION_MODE",
                "Critic execution requires an out-of-band-review-only specialisation.",
                nameof(specialisation.RequiredExecutionMode)));
        }

        if (!HasRequiredOutput(
                specialisation,
                nameof(CriticReviewResult),
                output => output.MustBeReviewOnly && output.RequiresHumanReview))
        {
            issues.Add(Error(
                "SPECIALISATION_OUTPUT_NOT_REVIEW_ONLY",
                "Critic specialisation must require review-only CriticReviewResult output with human review.",
                nameof(specialisation.OutputRequirements)));
        }

        AddAuthorityBoundaryIssues(issues, specialisation);

        return issues;
    }

    public IReadOnlyList<StoredManualAgentExecutionIssue> ValidateMemorySpecialisation(
        ManualAgentExecutionSpecialisationSelection selection,
        out AgentSpecialisationDefinition? specialisation)
    {
        var issues = ValidateSelection(selection);
        specialisation = null;

        if (!string.IsNullOrWhiteSpace(selection.SpecialisationId))
        {
            specialisation = AgentSpecialisationCatalog.GetById(selection.SpecialisationId);
        }

        if (specialisation is null)
        {
            issues.Add(Error(
                "SPECIALISATION_NOT_FOUND",
                $"Specialisation '{selection.SpecialisationId}' was not found.",
                nameof(selection.SpecialisationId)));
            return issues;
        }

        if (!AgentSpecialisationCatalog.MemoryImprovementProfiles.Any(item =>
                string.Equals(item.SpecialisationId, selection.SpecialisationId, StringComparison.Ordinal)))
        {
            issues.Add(Error(
                "SPECIALISATION_NOT_MEMORY_PROFILE",
                "Selected specialisation is not a memory-improvement profile.",
                nameof(selection.SpecialisationId)));
        }

        AddSpecialisationValidationIssues(issues, specialisation);

        var compatibility = _specialisationValidator.ValidateCompatibility(
            AgentDefinitionCatalog.MemoryImprovementAgent,
            specialisation);

        foreach (var issue in compatibility.Issues.Where(item =>
                     string.Equals(item.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(ConvertIssue(issue, "SPECIALISATION_INCOMPATIBLE"));
        }

        if (specialisation.RequiredAgentKind != AgentKind.ProposalAgent)
        {
            issues.Add(Error(
                "SPECIALISATION_WRONG_AGENT_KIND",
                "Memory-improvement execution requires a proposal-agent specialisation.",
                nameof(specialisation.RequiredAgentKind)));
        }

        if (specialisation.RequiredExecutionMode != AgentExecutionMode.ProposalOnly)
        {
            issues.Add(Error(
                "SPECIALISATION_WRONG_EXECUTION_MODE",
                "Memory-improvement execution requires a proposal-only specialisation.",
                nameof(specialisation.RequiredExecutionMode)));
        }

        if (!HasRequiredOutput(
                specialisation,
                nameof(MemoryImprovementDetectionResult),
                output => output.MustBeProposalOnly && output.RequiresHumanReview))
        {
            issues.Add(Error(
                "SPECIALISATION_OUTPUT_NOT_PROPOSAL_ONLY",
                "Memory-improvement specialisation must require proposal-only MemoryImprovementDetectionResult output with human review.",
                nameof(specialisation.OutputRequirements)));
        }

        AddAuthorityBoundaryIssues(issues, specialisation);

        return issues;
    }

    public IReadOnlyList<StoredManualAgentExecutionIssue> ValidateCriticExecution(
        CriticReviewResult? output,
        AgentRunAuditEnvelope? auditEnvelope)
    {
        var issues = new List<StoredManualAgentExecutionIssue>();

        if (output is null)
        {
            issues.Add(Error("CRITIC_OUTPUT_MISSING", "Critic output is required.", nameof(output)));
        }
        else
        {
            foreach (var issue in _criticReviewValidator.Validate(output).Where(item =>
                         string.Equals(item.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(ConvertIssue(issue, "CRITIC_OUTPUT_INVALID"));
            }
        }

        AddAuditValidationIssues(issues, auditEnvelope);
        return issues;
    }

    public IReadOnlyList<StoredManualAgentExecutionIssue> ValidateMemoryExecution(
        MemoryImprovementDetectionResult? output,
        AgentRunAuditEnvelope? auditEnvelope)
    {
        var issues = new List<StoredManualAgentExecutionIssue>();

        if (output is null)
        {
            issues.Add(Error("MEMORY_OUTPUT_MISSING", "Memory-improvement output is required.", nameof(output)));
        }
        else
        {
            foreach (var issue in _memoryImprovementValidator.Validate(output).Where(item =>
                         string.Equals(item.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(ConvertIssue(issue, "MEMORY_OUTPUT_INVALID"));
            }

            foreach (var draft in output.ProposalDrafts)
            {
                if (!draft.IsProposalOnly)
                {
                    issues.Add(Error(
                        "MEMORY_OUTPUT_NOT_PROPOSAL_ONLY",
                        "Memory-improvement proposal drafts must remain proposal-only.",
                        nameof(output.ProposalDrafts)));
                }

                if (draft.CreatesCollectiveMemory)
                {
                    issues.Add(Error(
                        "MEMORY_OUTPUT_CREATES_COLLECTIVE_MEMORY",
                        "Memory-improvement output must not create collective memory.",
                        nameof(output.ProposalDrafts)));
                }

                if (draft.PromotesMemory)
                {
                    issues.Add(Error(
                        "MEMORY_OUTPUT_PROMOTES_MEMORY",
                        "Memory-improvement output must not promote memory.",
                        nameof(output.ProposalDrafts)));
                }

                if (!draft.RequiresHumanReview)
                {
                    issues.Add(Error(
                        "MEMORY_OUTPUT_MISSING_HUMAN_REVIEW",
                        "Memory-improvement proposal drafts must require human review.",
                        nameof(output.ProposalDrafts)));
                }
            }
        }

        AddAuditValidationIssues(issues, auditEnvelope);
        return issues;
    }

    private static List<StoredManualAgentExecutionIssue> ValidateSelection(
        ManualAgentExecutionSpecialisationSelection selection)
    {
        var issues = new List<StoredManualAgentExecutionIssue>();

        if (string.IsNullOrWhiteSpace(selection.SpecialisationId))
        {
            issues.Add(Error(
                "SPECIALISATION_ID_REQUIRED",
                "SpecialisationId is required.",
                nameof(selection.SpecialisationId)));
        }

        if (string.IsNullOrWhiteSpace(selection.RequestedByUserId))
        {
            issues.Add(Error(
                "REQUESTED_BY_USER_REQUIRED",
                "RequestedByUserId is required.",
                nameof(selection.RequestedByUserId)));
        }

        if (string.IsNullOrWhiteSpace(selection.Reason))
        {
            issues.Add(Error(
                "SELECTION_REASON_REQUIRED",
                "Reason is required.",
                nameof(selection.Reason)));
        }
        else if (ContainsUnsafeReason(selection.Reason))
        {
            issues.Add(Error(
                "SELECTION_REASON_UNSAFE",
                "Reason must not contain raw/private reasoning or authority claims.",
                nameof(selection.Reason)));
        }

        return issues;
    }

    private void AddSpecialisationValidationIssues(
        List<StoredManualAgentExecutionIssue> issues,
        AgentSpecialisationDefinition specialisation)
    {
        foreach (var issue in _specialisationValidator.Validate(specialisation).Where(item =>
                     string.Equals(item.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(ConvertIssue(issue, "SPECIALISATION_INVALID"));
        }
    }

    private void AddAuditValidationIssues(
        List<StoredManualAgentExecutionIssue> issues,
        AgentRunAuditEnvelope? auditEnvelope)
    {
        if (auditEnvelope is null)
        {
            issues.Add(Error("AUDIT_ENVELOPE_MISSING", "Audit envelope is required.", nameof(auditEnvelope)));
            return;
        }

        foreach (var issue in _auditValidator.Validate(auditEnvelope).Where(item =>
                     string.Equals(item.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(ConvertIssue(issue, "AUDIT_ENVELOPE_INVALID"));
        }

        foreach (var issue in _thoughtLedgerValidator.Validate(auditEnvelope.ThoughtLedger).Where(item =>
                     string.Equals(item.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add(ConvertIssue(issue, "THOUGHT_LEDGER_INVALID"));
        }

        foreach (var output in auditEnvelope.Outputs)
        {
            if (output.CreatesAuthority)
            {
                issues.Add(Error(
                    "AUDIT_OUTPUT_CREATES_AUTHORITY",
                    "Manual agent audit output must not create authority.",
                    nameof(auditEnvelope.Outputs)));
            }

            if (output.CreatesRuntimeAction)
            {
                issues.Add(Error(
                    "AUDIT_OUTPUT_CREATES_RUNTIME_ACTION",
                    "Manual agent audit output must not create a runtime action.",
                    nameof(auditEnvelope.Outputs)));
            }

        }
    }

    private static bool HasRequiredOutput(
        AgentSpecialisationDefinition specialisation,
        string outputType,
        Func<AgentSpecialisationOutputRequirement, bool> predicate)
    {
        return specialisation.OutputRequirements.Any(output =>
            string.Equals(output.OutputType, outputType, StringComparison.Ordinal) &&
            predicate(output));
    }

    private static void AddAuthorityBoundaryIssues(
        List<StoredManualAgentExecutionIssue> issues,
        AgentSpecialisationDefinition specialisation)
    {
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
            issues.Add(Error(
                "SPECIALISATION_GRANTS_AUTHORITY",
                "Manual execution specialisation must not grant authority.",
                nameof(specialisation.AuthorityBoundary)));
        }
    }

    private static bool ContainsUnsafeReason(string text)
    {
        return UnsafeReasonMarkers.Any(marker =>
            text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static StoredManualAgentExecutionIssue ConvertIssue(
        AgentDefinitionValidationIssue issue,
        string fallbackCode)
    {
        return new StoredManualAgentExecutionIssue
        {
            Code = string.IsNullOrWhiteSpace(issue.Code) ? fallbackCode : issue.Code,
            Severity = issue.Severity,
            Message = issue.Message
        };
    }

    private static StoredManualAgentExecutionIssue Error(
        string code,
        string message,
        string? field = null)
    {
        return new StoredManualAgentExecutionIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        };
    }
}

public sealed class StoredManualIndependentCriticAgentService : IStoredManualIndependentCriticAgentService
{
    private const string AgentId = "IndependentCriticAgent";

    private readonly IManualIndependentCriticAgentService _manualAgent;
    private readonly IAgentRunAuditEnvelopeStore _auditStore;
    private readonly ManualAgentExecutionStoreValidator _validator;

    public StoredManualIndependentCriticAgentService(
        IManualIndependentCriticAgentService manualAgent,
        IAgentRunAuditEnvelopeStore auditStore)
        : this(manualAgent, auditStore, new ManualAgentExecutionStoreValidator())
    {
    }

    public StoredManualIndependentCriticAgentService(
        IManualIndependentCriticAgentService manualAgent,
        IAgentRunAuditEnvelopeStore auditStore,
        ManualAgentExecutionStoreValidator validator)
    {
        _manualAgent = manualAgent ?? throw new ArgumentNullException(nameof(manualAgent));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public StoredManualAgentExecutionResult<CriticReviewResult> ExecuteAndStore(
        ManualCriticReviewRequest request,
        ManualAgentExecutionSpecialisationSelection specialisation,
        DateTimeOffset executedAtUtc)
    {
        var agentRunId = BuildRunId(request);
        var specialisationIssues = _validator.ValidateCriticSpecialisation(specialisation, out _);
        if (StoredManualAgentExecutionServiceHelpers.HasErrors(specialisationIssues))
        {
            return StoredManualAgentExecutionServiceHelpers.BuildResult<CriticReviewResult>(
                StoredManualAgentExecutionStatus.InvalidSpecialisation,
                agentRunId,
                AgentId,
                specialisation.SpecialisationId,
                issues: specialisationIssues);
        }

        var manualResult = _manualAgent.Review(request, executedAtUtc);
        if (!manualResult.Succeeded || manualResult.CriticReviewResult is null || manualResult.AuditEnvelope is null)
        {
            return StoredManualAgentExecutionServiceHelpers.BuildResult<CriticReviewResult>(
                StoredManualAgentExecutionStatus.AgentExecutionRejected,
                manualResult.ManualCriticRunId,
                AgentId,
                specialisation.SpecialisationId,
                output: manualResult.CriticReviewResult,
                auditEnvelope: manualResult.AuditEnvelope,
                issues: StoredManualAgentExecutionServiceHelpers.ConvertManualIssues(
                    manualResult.Issues,
                    "Manual critic execution was rejected."));
        }

        var auditEnvelope = StoredManualAgentExecutionServiceHelpers.AddSpecialisationInput(
            manualResult.AuditEnvelope,
            specialisation);
        var executionIssues = _validator.ValidateCriticExecution(manualResult.CriticReviewResult, auditEnvelope);
        if (StoredManualAgentExecutionServiceHelpers.HasErrors(executionIssues))
        {
            return StoredManualAgentExecutionServiceHelpers.BuildResult(
                StoredManualAgentExecutionStatus.Rejected,
                manualResult.ManualCriticRunId,
                AgentId,
                specialisation.SpecialisationId,
                manualResult.CriticReviewResult,
                auditEnvelope,
                issues: executionIssues);
        }

        var appendResult = _auditStore.Append(auditEnvelope, executedAtUtc);
        return StoredManualAgentExecutionServiceHelpers.BuildResult(
            StoredManualAgentExecutionServiceHelpers.MapAppendStatus(appendResult.Status),
            manualResult.ManualCriticRunId,
            AgentId,
            specialisation.SpecialisationId,
            manualResult.CriticReviewResult,
            auditEnvelope,
            appendResult,
            StoredManualAgentExecutionServiceHelpers.ConvertAppendIssues(appendResult));
    }

    private static string BuildRunId(ManualCriticReviewRequest request)
    {
        return $"manual-independent-critic-{request.ReviewRequestId}";
    }
}

public sealed class StoredManualMemoryImprovementAgentService : IStoredManualMemoryImprovementAgentService
{
    private const string AgentId = "MemoryImprovementAgent";

    private readonly IManualMemoryImprovementAgentService _manualAgent;
    private readonly IAgentRunAuditEnvelopeStore _auditStore;
    private readonly ManualAgentExecutionStoreValidator _validator;

    public StoredManualMemoryImprovementAgentService(
        IManualMemoryImprovementAgentService manualAgent,
        IAgentRunAuditEnvelopeStore auditStore)
        : this(manualAgent, auditStore, new ManualAgentExecutionStoreValidator())
    {
    }

    public StoredManualMemoryImprovementAgentService(
        IManualMemoryImprovementAgentService manualAgent,
        IAgentRunAuditEnvelopeStore auditStore,
        ManualAgentExecutionStoreValidator validator)
    {
        _manualAgent = manualAgent ?? throw new ArgumentNullException(nameof(manualAgent));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public StoredManualAgentExecutionResult<MemoryImprovementDetectionResult> ExecuteAndStore(
        ManualMemoryImprovementDetectionRequest request,
        ManualAgentExecutionSpecialisationSelection specialisation,
        DateTimeOffset executedAtUtc)
    {
        var agentRunId = BuildRunId(request);
        var specialisationIssues = _validator.ValidateMemorySpecialisation(specialisation, out _);
        if (StoredManualAgentExecutionServiceHelpers.HasErrors(specialisationIssues))
        {
            return StoredManualAgentExecutionServiceHelpers.BuildResult<MemoryImprovementDetectionResult>(
                StoredManualAgentExecutionStatus.InvalidSpecialisation,
                agentRunId,
                AgentId,
                specialisation.SpecialisationId,
                issues: specialisationIssues);
        }

        var manualResult = _manualAgent.Detect(request, executedAtUtc);
        if (!manualResult.Succeeded || manualResult.DetectionResult is null || manualResult.AuditEnvelope is null)
        {
            return StoredManualAgentExecutionServiceHelpers.BuildResult<MemoryImprovementDetectionResult>(
                StoredManualAgentExecutionStatus.AgentExecutionRejected,
                manualResult.ManualMemoryImprovementRunId,
                AgentId,
                specialisation.SpecialisationId,
                output: manualResult.DetectionResult,
                auditEnvelope: manualResult.AuditEnvelope,
                issues: StoredManualAgentExecutionServiceHelpers.ConvertManualIssues(
                    manualResult.Issues,
                    "Manual memory-improvement execution was rejected."));
        }

        var auditEnvelope = StoredManualAgentExecutionServiceHelpers.AddSpecialisationInput(
            manualResult.AuditEnvelope,
            specialisation);
        var executionIssues = _validator.ValidateMemoryExecution(manualResult.DetectionResult, auditEnvelope);
        if (StoredManualAgentExecutionServiceHelpers.HasErrors(executionIssues))
        {
            return StoredManualAgentExecutionServiceHelpers.BuildResult(
                StoredManualAgentExecutionStatus.Rejected,
                manualResult.ManualMemoryImprovementRunId,
                AgentId,
                specialisation.SpecialisationId,
                manualResult.DetectionResult,
                auditEnvelope,
                issues: executionIssues);
        }

        var appendResult = _auditStore.Append(auditEnvelope, executedAtUtc);
        return StoredManualAgentExecutionServiceHelpers.BuildResult(
            StoredManualAgentExecutionServiceHelpers.MapAppendStatus(appendResult.Status),
            manualResult.ManualMemoryImprovementRunId,
            AgentId,
            specialisation.SpecialisationId,
            manualResult.DetectionResult,
            auditEnvelope,
            appendResult,
            StoredManualAgentExecutionServiceHelpers.ConvertAppendIssues(appendResult));
    }

    private static string BuildRunId(ManualMemoryImprovementDetectionRequest request)
    {
        return $"manual-memory-improvement-{request.DetectionRequestId}";
    }
}

file static class StoredManualAgentExecutionServiceHelpers
{
    public static bool HasErrors(IReadOnlyList<StoredManualAgentExecutionIssue> issues)
    {
        return issues.Any(issue =>
            string.Equals(issue.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase));
    }

    public static AgentRunAuditEnvelope AddSpecialisationInput(
        AgentRunAuditEnvelope envelope,
        ManualAgentExecutionSpecialisationSelection specialisation)
    {
        var inputRefId = $"input-{envelope.Run.AgentRunId}-selected-specialisation";
        if (envelope.Inputs.Any(input => string.Equals(input.InputRefId, inputRefId, StringComparison.Ordinal)))
        {
            return envelope;
        }

        var specialisationInput = new AgentRunInputRef
        {
            InputRefId = inputRefId,
            AgentRunId = envelope.Run.AgentRunId,
            RefType = "AgentSpecialisationDefinition",
            RefId = specialisation.SpecialisationId,
            Source = "manual-agent-execution-store",
            Summary = "Selected manual agent specialisation.",
            IsAuthoritativeForAction = false,
            ContainsRawPrivateReasoning = false
        };

        return envelope with
        {
            Inputs = [.. envelope.Inputs, specialisationInput]
        };
    }

    public static StoredManualAgentExecutionStatus MapAppendStatus(AgentRunAuditEnvelopeAppendStatus status)
    {
        return status switch
        {
            AgentRunAuditEnvelopeAppendStatus.Appended => StoredManualAgentExecutionStatus.Stored,
            AgentRunAuditEnvelopeAppendStatus.AlreadyExists => StoredManualAgentExecutionStatus.AlreadyStored,
            AgentRunAuditEnvelopeAppendStatus.Conflict => StoredManualAgentExecutionStatus.Conflict,
            AgentRunAuditEnvelopeAppendStatus.Rejected => StoredManualAgentExecutionStatus.AuditAppendFailed,
            _ => StoredManualAgentExecutionStatus.AuditAppendFailed
        };
    }

    public static StoredManualAgentExecutionResult<TOutput> BuildResult<TOutput>(
        StoredManualAgentExecutionStatus status,
        string agentRunId,
        string agentId,
        string specialisationId,
        TOutput? output = default,
        AgentRunAuditEnvelope? auditEnvelope = null,
        AgentRunAuditEnvelopeAppendResult? appendResult = null,
        IReadOnlyList<StoredManualAgentExecutionIssue>? issues = null)
    {
        return new StoredManualAgentExecutionResult<TOutput>
        {
            Status = status,
            AgentRunId = agentRunId,
            AgentId = agentId,
            SpecialisationId = specialisationId,
            Output = output,
            AuditEnvelope = auditEnvelope,
            AppendResult = appendResult,
            Issues = issues ?? Array.Empty<StoredManualAgentExecutionIssue>()
        };
    }

    public static IReadOnlyList<StoredManualAgentExecutionIssue> ConvertManualIssues(
        IReadOnlyList<ManualCriticReviewIssue> issues,
        string fallbackMessage)
    {
        if (issues.Count == 0)
        {
            return BuildFallbackManualIssues(fallbackMessage);
        }

        return issues.Select(issue => new StoredManualAgentExecutionIssue
        {
            Code = string.IsNullOrWhiteSpace(issue.Code) ? "MANUAL_AGENT_EXECUTION_REJECTED" : issue.Code,
            Severity = issue.Severity,
            Message = issue.Message,
            Field = issue.Field
        }).ToArray();
    }

    public static IReadOnlyList<StoredManualAgentExecutionIssue> ConvertManualIssues(
        IReadOnlyList<ManualMemoryImprovementIssue> issues,
        string fallbackMessage)
    {
        if (issues.Count == 0)
        {
            return BuildFallbackManualIssues(fallbackMessage);
        }

        return issues.Select(issue => new StoredManualAgentExecutionIssue
        {
            Code = string.IsNullOrWhiteSpace(issue.Code) ? "MANUAL_AGENT_EXECUTION_REJECTED" : issue.Code,
            Severity = issue.Severity,
            Message = issue.Message,
            Field = issue.Field
        }).ToArray();
    }

    private static IReadOnlyList<StoredManualAgentExecutionIssue> BuildFallbackManualIssues(string fallbackMessage) =>
    [
        new StoredManualAgentExecutionIssue
        {
            Code = "MANUAL_AGENT_EXECUTION_REJECTED",
            Severity = AgentDefinitionValidator.SeverityError,
            Message = fallbackMessage
        }
    ];

    public static IReadOnlyList<StoredManualAgentExecutionIssue> ConvertAppendIssues(
        AgentRunAuditEnvelopeAppendResult appendResult)
    {
        if (appendResult.Status is AgentRunAuditEnvelopeAppendStatus.Appended or AgentRunAuditEnvelopeAppendStatus.AlreadyExists)
        {
            return Array.Empty<StoredManualAgentExecutionIssue>();
        }

        if (appendResult.Issues.Count == 0)
        {
            return
            [
                new StoredManualAgentExecutionIssue
                {
                    Code = appendResult.Status == AgentRunAuditEnvelopeAppendStatus.Conflict
                        ? "AUDIT_APPEND_CONFLICT"
                        : "AUDIT_APPEND_FAILED",
                    Severity = AgentDefinitionValidator.SeverityError,
                    Message = appendResult.Status == AgentRunAuditEnvelopeAppendStatus.Conflict
                        ? "Audit envelope append conflicted with an existing envelope."
                        : "Audit envelope append failed."
                }
            ];
        }

        return appendResult.Issues.Select(issue => new StoredManualAgentExecutionIssue
        {
            Code = string.IsNullOrWhiteSpace(issue.Code) ? "AUDIT_APPEND_FAILED" : issue.Code,
            Severity = issue.Severity,
            Message = issue.Message,
            Field = issue.Field
        }).ToArray();
    }
}
