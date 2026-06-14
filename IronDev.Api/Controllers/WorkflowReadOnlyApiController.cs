using IronDev.Core.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/workflow")]
public sealed class WorkflowReadOnlyApiController : ControllerBase
{
    private const string RedactedPrivateReasoning = "[redacted: sensitive workflow text]";

    private static readonly string[] PrivateReasoningMarkers =
    [
        "hiddenReasoning",
        "hidden reasoning",
        "chainOfThought",
        "chain of thought",
        "chain-of-thought",
        "private reasoning",
        "scratchpad",
        "rawPrompt",
        "raw prompt",
        "rawCompletion",
        "raw completion",
        "rawToolOutput",
        "raw tool output",
        "entirePatch",
        "entire patch"
    ];

    private static readonly HashSet<string> ProjectTakeQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "projectId",
        "take"
    };

    private static readonly HashSet<string> ProjectOnlyQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "projectId"
    };

    private static readonly HashSet<string> SubjectQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "projectId",
        "subjectType",
        "subjectId",
        "take"
    };

    private readonly IWorkflowRunStore _runStore;
    private readonly IWorkflowStepStore _stepStore;
    private readonly IWorkflowCheckpointStore _checkpointStore;

    public WorkflowReadOnlyApiController(
        IWorkflowRunStore runStore,
        IWorkflowStepStore stepStore,
        IWorkflowCheckpointStore checkpointStore)
    {
        _runStore = runStore ?? throw new ArgumentNullException(nameof(runStore));
        _stepStore = stepStore ?? throw new ArgumentNullException(nameof(stepStore));
        _checkpointStore = checkpointStore ?? throw new ArgumentNullException(nameof(checkpointStore));
    }

    [HttpGet("runs")]
    public async Task<ActionResult<WorkflowReadOnlyApiEnvelope<WorkflowRunListResponseDto>>> ListRuns(
        [FromQuery] Guid projectId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateProjectRequest(projectId, ProjectTakeQueryKeys);
        if (errors.Count > 0)
            return BadRequest(Envelope<WorkflowRunListResponseDto>("validation_error", null, errors: errors));

        var summaries = await _runStore.ListByProjectAsync(projectId, take, cancellationToken);
        return Ok(Envelope("succeeded", new WorkflowRunListResponseDto
        {
            Items = summaries.Select(ToRunSummaryDto).ToArray(),
            Take = WorkflowRunValidator.NormalizeTake(take)
        }));
    }

    [HttpGet("runs/by-correlation/{correlationId:guid}")]
    public async Task<ActionResult<WorkflowReadOnlyApiEnvelope<WorkflowRunListResponseDto>>> ListRunsByCorrelation(
        Guid correlationId,
        [FromQuery] Guid projectId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateProjectRequest(projectId, ProjectTakeQueryKeys);
        if (correlationId == Guid.Empty)
            errors.Add(ValidationError("correlationId", "Correlation id is required."));
        if (errors.Count > 0)
            return BadRequest(Envelope<WorkflowRunListResponseDto>("validation_error", null, errors: errors));

        var summaries = await _runStore.ListByCorrelationAsync(projectId, correlationId, take, cancellationToken);
        return Ok(Envelope("succeeded", new WorkflowRunListResponseDto
        {
            Items = summaries.Select(ToRunSummaryDto).ToArray(),
            Take = WorkflowRunValidator.NormalizeTake(take)
        }));
    }

    [HttpGet("runs/by-subject")]
    public async Task<ActionResult<WorkflowReadOnlyApiEnvelope<WorkflowRunListResponseDto>>> ListRunsBySubject(
        [FromQuery] Guid projectId,
        [FromQuery] string? subjectType,
        [FromQuery] string? subjectId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateProjectRequest(projectId, SubjectQueryKeys);
        if (string.IsNullOrWhiteSpace(subjectType))
            errors.Add(ValidationError("subjectType", "Subject type is required."));
        if (string.IsNullOrWhiteSpace(subjectId))
            errors.Add(ValidationError("subjectId", "Subject id is required."));
        if (errors.Count > 0)
            return BadRequest(Envelope<WorkflowRunListResponseDto>("validation_error", null, errors: errors));

        var summaries = await _runStore.ListBySubjectAsync(projectId, subjectType!, subjectId!, take, cancellationToken);
        return Ok(Envelope("succeeded", new WorkflowRunListResponseDto
        {
            Items = summaries.Select(ToRunSummaryDto).ToArray(),
            Take = WorkflowRunValidator.NormalizeTake(take)
        }));
    }

    [HttpGet("runs/{workflowRunId:guid}")]
    public async Task<ActionResult<WorkflowReadOnlyApiEnvelope<WorkflowRunReadDto>>> GetRun(
        Guid workflowRunId,
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateProjectAndId(projectId, workflowRunId, "workflowRunId", ProjectOnlyQueryKeys);
        if (errors.Count > 0)
            return BadRequest(Envelope<WorkflowRunReadDto>("validation_error", null, runId: workflowRunId.ToString(), errors: errors));

        var run = await _runStore.GetAsync(projectId, workflowRunId, cancellationToken);
        if (run is null)
            return NotFound(Envelope<WorkflowRunReadDto>("not_found", null, runId: workflowRunId.ToString(), errors: [NotFoundError("workflowRunId", "Workflow run was not found in the requested project.")]));

        return Ok(Envelope("succeeded", ToRunDto(run), runId: workflowRunId.ToString(), evidenceId: run.EvidenceReferences.FirstOrDefault()?.EvidenceId ?? string.Empty));
    }

    [HttpGet("runs/{workflowRunId:guid}/steps")]
    public async Task<ActionResult<WorkflowReadOnlyApiEnvelope<WorkflowStepListResponseDto>>> ListSteps(
        Guid workflowRunId,
        [FromQuery] Guid projectId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateProjectAndId(projectId, workflowRunId, "workflowRunId", ProjectTakeQueryKeys);
        if (errors.Count > 0)
            return BadRequest(Envelope<WorkflowStepListResponseDto>("validation_error", null, runId: workflowRunId.ToString(), errors: errors));

        if (await _runStore.GetAsync(projectId, workflowRunId, cancellationToken) is null)
            return NotFound(Envelope<WorkflowStepListResponseDto>("not_found", null, runId: workflowRunId.ToString(), errors: [NotFoundError("workflowRunId", "Workflow run was not found in the requested project.")]));

        var steps = await _stepStore.ListByRunAsync(projectId, workflowRunId, take, cancellationToken);
        return Ok(Envelope("succeeded", new WorkflowStepListResponseDto
        {
            Items = steps.Select(ToStepSummaryDto).ToArray(),
            Take = WorkflowStepValidator.NormalizeTake(take)
        }, runId: workflowRunId.ToString()));
    }

    [HttpGet("runs/{workflowRunId:guid}/steps/{workflowRunStepId:guid}")]
    public async Task<ActionResult<WorkflowReadOnlyApiEnvelope<WorkflowStepReadDto>>> GetStep(
        Guid workflowRunId,
        Guid workflowRunStepId,
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateProjectAndId(projectId, workflowRunId, "workflowRunId", ProjectOnlyQueryKeys);
        if (workflowRunStepId == Guid.Empty)
            errors.Add(ValidationError("workflowRunStepId", "Workflow run step id is required."));
        if (errors.Count > 0)
            return BadRequest(Envelope<WorkflowStepReadDto>("validation_error", null, runId: workflowRunId.ToString(), errors: errors));

        var step = await _stepStore.GetAsync(projectId, workflowRunId, workflowRunStepId, cancellationToken);
        if (step is null)
            return NotFound(Envelope<WorkflowStepReadDto>("not_found", null, runId: workflowRunId.ToString(), errors: [NotFoundError("workflowRunStepId", "Workflow step was not found in the requested project and run.")]));

        return Ok(Envelope("succeeded", ToStepDto(step), runId: workflowRunId.ToString(), evidenceId: step.EvidenceReferences.FirstOrDefault()?.EvidenceId ?? string.Empty));
    }

    [HttpGet("runs/{workflowRunId:guid}/checkpoints")]
    public async Task<ActionResult<WorkflowReadOnlyApiEnvelope<WorkflowCheckpointListResponseDto>>> ListCheckpointsByRun(
        Guid workflowRunId,
        [FromQuery] Guid projectId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateProjectAndId(projectId, workflowRunId, "workflowRunId", ProjectTakeQueryKeys);
        if (errors.Count > 0)
            return BadRequest(Envelope<WorkflowCheckpointListResponseDto>("validation_error", null, runId: workflowRunId.ToString(), errors: errors));

        if (await _runStore.GetAsync(projectId, workflowRunId, cancellationToken) is null)
            return NotFound(Envelope<WorkflowCheckpointListResponseDto>("not_found", null, runId: workflowRunId.ToString(), errors: [NotFoundError("workflowRunId", "Workflow run was not found in the requested project.")]));

        var checkpoints = await _checkpointStore.ListByRunAsync(projectId, workflowRunId, take, cancellationToken);
        return Ok(Envelope("succeeded", new WorkflowCheckpointListResponseDto
        {
            Items = checkpoints.Select(ToCheckpointSummaryDto).ToArray(),
            Take = WorkflowCheckpointValidator.NormalizeTake(take)
        }, runId: workflowRunId.ToString()));
    }

    [HttpGet("runs/{workflowRunId:guid}/steps/{workflowRunStepId:guid}/checkpoints")]
    public async Task<ActionResult<WorkflowReadOnlyApiEnvelope<WorkflowCheckpointListResponseDto>>> ListCheckpointsByStep(
        Guid workflowRunId,
        Guid workflowRunStepId,
        [FromQuery] Guid projectId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateProjectAndId(projectId, workflowRunId, "workflowRunId", ProjectTakeQueryKeys);
        if (workflowRunStepId == Guid.Empty)
            errors.Add(ValidationError("workflowRunStepId", "Workflow run step id is required."));
        if (errors.Count > 0)
            return BadRequest(Envelope<WorkflowCheckpointListResponseDto>("validation_error", null, runId: workflowRunId.ToString(), errors: errors));

        if (await _stepStore.GetAsync(projectId, workflowRunId, workflowRunStepId, cancellationToken) is null)
            return NotFound(Envelope<WorkflowCheckpointListResponseDto>("not_found", null, runId: workflowRunId.ToString(), errors: [NotFoundError("workflowRunStepId", "Workflow step was not found in the requested project and run.")]));

        var checkpoints = await _checkpointStore.ListByStepAsync(projectId, workflowRunId, workflowRunStepId, take, cancellationToken);
        return Ok(Envelope("succeeded", new WorkflowCheckpointListResponseDto
        {
            Items = checkpoints.Select(ToCheckpointSummaryDto).ToArray(),
            Take = WorkflowCheckpointValidator.NormalizeTake(take)
        }, runId: workflowRunId.ToString()));
    }

    [HttpGet("runs/{workflowRunId:guid}/checkpoints/{workflowCheckpointId:guid}")]
    public async Task<ActionResult<WorkflowReadOnlyApiEnvelope<WorkflowCheckpointReadDto>>> GetCheckpoint(
        Guid workflowRunId,
        Guid workflowCheckpointId,
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateProjectAndId(projectId, workflowRunId, "workflowRunId", ProjectOnlyQueryKeys);
        if (workflowCheckpointId == Guid.Empty)
            errors.Add(ValidationError("workflowCheckpointId", "Workflow checkpoint id is required."));
        if (errors.Count > 0)
            return BadRequest(Envelope<WorkflowCheckpointReadDto>("validation_error", null, runId: workflowRunId.ToString(), errors: errors));

        var checkpoint = await _checkpointStore.GetAsync(projectId, workflowRunId, workflowCheckpointId, cancellationToken);
        if (checkpoint is null)
            return NotFound(Envelope<WorkflowCheckpointReadDto>("not_found", null, runId: workflowRunId.ToString(), errors: [NotFoundError("workflowCheckpointId", "Workflow checkpoint was not found in the requested project and run.")]));

        return Ok(Envelope("succeeded", ToCheckpointDto(checkpoint), runId: workflowRunId.ToString(), evidenceId: checkpoint.EvidenceReferences.FirstOrDefault()?.EvidenceId ?? string.Empty));
    }

    private List<WorkflowReadOnlyApiErrorDto> ValidateProjectRequest(Guid projectId, IReadOnlySet<string> allowedQueryKeys)
    {
        var errors = UnsupportedQueryKeys(allowedQueryKeys).Select(UnsupportedFilter).ToList();
        if (projectId == Guid.Empty)
            errors.Add(ValidationError("projectId", "Project id is required."));
        return errors;
    }

    private List<WorkflowReadOnlyApiErrorDto> ValidateProjectAndId(Guid projectId, Guid id, string idField, IReadOnlySet<string> allowedQueryKeys)
    {
        var errors = ValidateProjectRequest(projectId, allowedQueryKeys).ToList();
        if (id == Guid.Empty)
            errors.Add(ValidationError(idField, $"{idField} is required."));
        return errors;
    }

    private IReadOnlyList<string> UnsupportedQueryKeys(IReadOnlySet<string> allowed) =>
        Request.Query.Keys.Where(key => !allowed.Contains(key)).OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();

    private static WorkflowRunSummaryReadDto ToRunSummaryDto(WorkflowRunSummary summary) =>
        new()
        {
            WorkflowRunId = summary.WorkflowRunId,
            ProjectId = summary.ProjectId,
            WorkflowType = SanitiseText(summary.WorkflowType) ?? string.Empty,
            WorkflowName = SanitiseText(summary.WorkflowName) ?? string.Empty,
            Status = summary.Status.ToString(),
            SubjectType = SanitiseText(summary.SubjectType) ?? string.Empty,
            SubjectId = SanitiseText(summary.SubjectId) ?? string.Empty,
            CorrelationId = summary.CorrelationId,
            CausationId = summary.CausationId,
            StepCount = summary.StepCount,
            EvidenceReferenceCount = summary.EvidenceReferenceCount,
            GroundingReferenceCount = summary.GroundingReferenceCount,
            AuthorityFlags = AuthorityFlags(),
            CreatedUtc = summary.CreatedUtc
        };

    private static WorkflowRunReadDto ToRunDto(WorkflowRun run) =>
        new()
        {
            WorkflowRunId = run.WorkflowRunId,
            ProjectId = run.ProjectId,
            WorkflowType = SanitiseText(run.WorkflowType) ?? string.Empty,
            WorkflowName = SanitiseText(run.WorkflowName) ?? string.Empty,
            Status = run.Status.ToString(),
            SubjectType = SanitiseText(run.SubjectType) ?? string.Empty,
            SubjectId = SanitiseText(run.SubjectId) ?? string.Empty,
            SubjectSummary = SanitiseText(run.SubjectSummary),
            CorrelationId = run.CorrelationId,
            CausationId = run.CausationId,
            Steps = run.Steps.Select(ToEmbeddedStepDto).ToArray(),
            EvidenceReferences = run.EvidenceReferences.Select(ToEvidenceDto).ToArray(),
            GroundingReferences = run.GroundingReferences.Select(ToGroundingDto).ToArray(),
            AuthorityFlags = AuthorityFlags(),
            CreatedUtc = run.CreatedUtc
        };

    private static WorkflowStepReadDto ToEmbeddedStepDto(WorkflowRunStep step) =>
        new()
        {
            WorkflowRunStepId = step.WorkflowRunStepId,
            WorkflowRunId = step.WorkflowRunId,
            ProjectId = step.ProjectId,
            StepKey = SanitiseText(step.StepKey) ?? string.Empty,
            StepName = SanitiseText(step.StepName) ?? string.Empty,
            StepType = step.StepType.ToString(),
            Status = step.Status.ToString(),
            SequenceNumber = null,
            AgentRole = SanitiseText(step.AgentRole),
            AgentId = SanitiseText(step.AgentId),
            SubjectType = SanitiseText(step.SubjectType),
            SubjectId = SanitiseText(step.SubjectId),
            SafeSummary = SanitiseText(step.SafeSummary),
            CorrelationId = null,
            CausationId = null,
            EvidenceReferences = [],
            GroundingReferences = [],
            AuthorityFlags = AuthorityFlags(),
            CreatedUtc = step.CreatedUtc
        };

    private static WorkflowStepSummaryReadDto ToStepSummaryDto(WorkflowStepSummary step) =>
        new()
        {
            WorkflowRunStepId = step.WorkflowRunStepId,
            WorkflowRunId = step.WorkflowRunId,
            ProjectId = step.ProjectId,
            StepKey = SanitiseText(step.StepKey) ?? string.Empty,
            StepName = SanitiseText(step.StepName) ?? string.Empty,
            StepType = step.StepType.ToString(),
            Status = step.Status.ToString(),
            SequenceNumber = step.SequenceNumber,
            AgentRole = SanitiseText(step.AgentRole),
            AgentId = SanitiseText(step.AgentId),
            SubjectType = SanitiseText(step.SubjectType),
            SubjectId = SanitiseText(step.SubjectId),
            CorrelationId = step.CorrelationId,
            CausationId = step.CausationId,
            EvidenceReferenceCount = step.EvidenceReferenceCount,
            GroundingReferenceCount = step.GroundingReferenceCount,
            AuthorityFlags = AuthorityFlags(),
            CreatedUtc = step.CreatedUtc
        };

    private static WorkflowStepReadDto ToStepDto(WorkflowStep step) =>
        new()
        {
            WorkflowRunStepId = step.WorkflowRunStepId,
            WorkflowRunId = step.WorkflowRunId,
            ProjectId = step.ProjectId,
            StepKey = SanitiseText(step.StepKey) ?? string.Empty,
            StepName = SanitiseText(step.StepName) ?? string.Empty,
            StepType = step.StepType.ToString(),
            Status = step.Status.ToString(),
            SequenceNumber = step.SequenceNumber,
            AgentRole = SanitiseText(step.AgentRole),
            AgentId = SanitiseText(step.AgentId),
            SubjectType = SanitiseText(step.SubjectType),
            SubjectId = SanitiseText(step.SubjectId),
            SafeSummary = SanitiseText(step.SafeSummary),
            CorrelationId = step.CorrelationId,
            CausationId = step.CausationId,
            EvidenceReferences = step.EvidenceReferences.Select(ToEvidenceDto).ToArray(),
            GroundingReferences = step.GroundingReferences.Select(ToGroundingDto).ToArray(),
            AuthorityFlags = AuthorityFlags(),
            CreatedUtc = step.CreatedUtc
        };

    private static WorkflowCheckpointSummaryReadDto ToCheckpointSummaryDto(WorkflowCheckpointSummary checkpoint) =>
        new()
        {
            WorkflowCheckpointId = checkpoint.WorkflowCheckpointId,
            WorkflowRunId = checkpoint.WorkflowRunId,
            WorkflowRunStepId = checkpoint.WorkflowRunStepId,
            ProjectId = checkpoint.ProjectId,
            CheckpointKey = SanitiseText(checkpoint.CheckpointKey) ?? string.Empty,
            CheckpointName = SanitiseText(checkpoint.CheckpointName) ?? string.Empty,
            CheckpointType = checkpoint.CheckpointType.ToString(),
            Status = checkpoint.Status.ToString(),
            SubjectType = SanitiseText(checkpoint.SubjectType),
            SubjectId = SanitiseText(checkpoint.SubjectId),
            StateHashSha256 = SanitiseText(checkpoint.StateHashSha256),
            CorrelationId = checkpoint.CorrelationId,
            CausationId = checkpoint.CausationId,
            EvidenceReferenceCount = checkpoint.EvidenceReferenceCount,
            GroundingReferenceCount = checkpoint.GroundingReferenceCount,
            AuthorityFlags = AuthorityFlags(),
            CreatedUtc = checkpoint.CreatedUtc
        };

    private static WorkflowCheckpointReadDto ToCheckpointDto(WorkflowCheckpoint checkpoint) =>
        new()
        {
            WorkflowCheckpointId = checkpoint.WorkflowCheckpointId,
            WorkflowRunId = checkpoint.WorkflowRunId,
            WorkflowRunStepId = checkpoint.WorkflowRunStepId,
            ProjectId = checkpoint.ProjectId,
            CheckpointKey = SanitiseText(checkpoint.CheckpointKey) ?? string.Empty,
            CheckpointName = SanitiseText(checkpoint.CheckpointName) ?? string.Empty,
            CheckpointType = checkpoint.CheckpointType.ToString(),
            Status = checkpoint.Status.ToString(),
            SubjectType = SanitiseText(checkpoint.SubjectType),
            SubjectId = SanitiseText(checkpoint.SubjectId),
            SafeSummary = SanitiseText(checkpoint.SafeSummary),
            StateVersion = checkpoint.StateVersion,
            StateHashSha256 = SanitiseText(checkpoint.StateHashSha256),
            CorrelationId = checkpoint.CorrelationId,
            CausationId = checkpoint.CausationId,
            EvidenceReferences = checkpoint.EvidenceReferences.Select(ToCheckpointEvidenceDto).ToArray(),
            GroundingReferences = checkpoint.GroundingReferences.Select(ToCheckpointGroundingDto).ToArray(),
            AuthorityFlags = AuthorityFlags(),
            CreatedUtc = checkpoint.CreatedUtc
        };

    private static WorkflowEvidenceReferenceReadDto ToEvidenceDto(WorkflowRunEvidenceReference evidence) =>
        new()
        {
            EvidenceReferenceId = evidence.WorkflowRunEvidenceReferenceId,
            WorkflowRunId = evidence.WorkflowRunId,
            WorkflowRunStepId = evidence.WorkflowRunStepId,
            ProjectId = evidence.ProjectId,
            StepKey = SanitiseText(evidence.StepKey),
            EvidenceType = evidence.EvidenceType.ToString(),
            EvidenceId = SanitiseText(evidence.EvidenceId) ?? string.Empty,
            EvidenceLabel = SanitiseText(evidence.EvidenceLabel),
            SafeSummary = SanitiseText(evidence.SafeSummary),
            AllowedUse = evidence.AllowedUse?.ToString(),
            GovernanceEventId = evidence.GovernanceEventId,
            AgentHandoffId = evidence.AgentHandoffId,
            ThoughtLedgerEntryId = evidence.ThoughtLedgerEntryId,
            GroundingEvidenceReferenceId = evidence.GroundingEvidenceReferenceId,
            EvidenceIsPermission = false,
            CreatedUtc = evidence.CreatedUtc
        };

    private static WorkflowEvidenceReferenceReadDto ToCheckpointEvidenceDto(WorkflowCheckpointEvidenceReference evidence) =>
        new()
        {
            EvidenceReferenceId = evidence.WorkflowCheckpointEvidenceReferenceId,
            WorkflowRunId = evidence.WorkflowRunId,
            WorkflowRunStepId = evidence.WorkflowRunStepId,
            WorkflowCheckpointId = evidence.WorkflowCheckpointId,
            ProjectId = evidence.ProjectId,
            EvidenceType = evidence.EvidenceType.ToString(),
            EvidenceId = SanitiseText(evidence.EvidenceId) ?? string.Empty,
            EvidenceLabel = SanitiseText(evidence.EvidenceLabel),
            SafeSummary = SanitiseText(evidence.SafeSummary),
            AllowedUse = evidence.AllowedUse?.ToString(),
            GovernanceEventId = evidence.GovernanceEventId,
            AgentHandoffId = evidence.HandoffRecordId,
            ThoughtLedgerEntryId = evidence.ThoughtLedgerEntryId,
            GroundingEvidenceReferenceId = evidence.GroundingReferenceId,
            WorkflowRunEvidenceReferenceId = evidence.WorkflowRunEvidenceReferenceId,
            EvidenceIsPermission = false,
            CreatedUtc = evidence.CreatedUtc
        };

    private static WorkflowGroundingReferenceReadDto ToGroundingDto(WorkflowRunGroundingReference grounding) =>
        new()
        {
            GroundingReferenceId = grounding.WorkflowRunGroundingReferenceId,
            WorkflowRunId = grounding.WorkflowRunId,
            WorkflowRunStepId = grounding.WorkflowRunStepId,
            ProjectId = grounding.ProjectId,
            StepKey = SanitiseText(grounding.StepKey),
            GroundingEvidenceReferenceId = grounding.GroundingEvidenceReferenceId,
            ClaimType = grounding.ClaimType.ToString(),
            ClaimId = SanitiseText(grounding.ClaimId) ?? string.Empty,
            SafeSummary = SanitiseText(grounding.SafeSummary),
            GroundingIsAuthority = false,
            CreatedUtc = grounding.CreatedUtc
        };

    private static WorkflowGroundingReferenceReadDto ToCheckpointGroundingDto(WorkflowCheckpointGroundingReference grounding) =>
        new()
        {
            GroundingReferenceId = grounding.WorkflowCheckpointGroundingReferenceId,
            WorkflowRunId = grounding.WorkflowRunId,
            WorkflowRunStepId = grounding.WorkflowRunStepId,
            WorkflowCheckpointId = grounding.WorkflowCheckpointId,
            ProjectId = grounding.ProjectId,
            GroundingEvidenceReferenceId = grounding.GroundingReferenceId,
            ClaimType = grounding.ClaimType.ToString(),
            ClaimId = SanitiseText(grounding.ClaimId) ?? string.Empty,
            SafeSummary = SanitiseText(grounding.SafeSummary),
            GroundingIsAuthority = false,
            CreatedUtc = grounding.CreatedUtc
        };

    private static WorkflowAuthorityFlagsDto AuthorityFlags() =>
        new()
        {
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            ContinuesWorkflow = false,
            ResumesWorkflow = false,
            RetriesWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            ApprovesRelease = false,
            CreatesAcceptedMemory = false
        };

    private static WorkflowReadOnlyApiEnvelope<TData> Envelope<TData>(
        string status,
        TData? data,
        string runId = "",
        string evidenceId = "",
        IReadOnlyList<string>? warnings = null,
        IReadOnlyList<WorkflowReadOnlyApiErrorDto>? errors = null) =>
        new()
        {
            Status = status,
            Data = data,
            WorkflowRunId = runId,
            EvidenceId = SanitiseText(evidenceId) ?? string.Empty,
            Boundary = BoundaryStatus(),
            MutationOccurred = false,
            HumanApprovalRequired = false,
            Warnings = warnings ?? BoundaryWarnings(),
            Errors = errors ?? []
        };

    private static WorkflowReadOnlyBoundaryStatusDto BoundaryStatus() =>
        new()
        {
            ReadOnlyInspection = true,
            WorkflowStatusIsAction = false,
            EvidenceIsPermission = false,
            GroundingIsAuthority = false,
            EndpointAccessIsExecutionPermission = false,
            ApiResponseStatusIsGovernance = false,
            ModelOutputIsAuthority = false,
            SourceApplied = false,
            MemoryPromoted = false,
            ReleaseApproved = false,
            ApprovalSatisfied = false,
            HumanReviewRequiredForSourceApply = true,
            HumanReviewRequiredForMemoryPromotion = true
        };

    private static IReadOnlyList<string> BoundaryWarnings() =>
    [
        "Workflow API v1 is read-only inspection of durable workflow facts.",
        "Workflow statuses are stored facts, not runtime actions.",
        "Evidence and grounding references are traceability only, not approval, policy satisfaction, or execution permission.",
        "No workflow create, update, delete, continue, resume, retry, dispatch, tool call, model call, source apply, memory promotion, accepted memory, release approval, or approval satisfaction is exposed."
    ];

    private static WorkflowReadOnlyApiErrorDto ValidationError(string field, string message) =>
        new()
        {
            Category = "validation_error",
            Code = "WORKFLOW_READ_API_VALIDATION_ERROR",
            Field = field,
            Message = SanitiseText(message) ?? string.Empty
        };

    private static WorkflowReadOnlyApiErrorDto UnsupportedFilter(string field) =>
        new()
        {
            Category = "unsupported_filter",
            Code = "WORKFLOW_READ_API_UNSUPPORTED_FILTER",
            Field = field,
            Message = $"Unsupported query parameter: {SanitiseText(field)}."
        };

    private static WorkflowReadOnlyApiErrorDto NotFoundError(string field, string message) =>
        new()
        {
            Category = "not_found",
            Code = "WORKFLOW_READ_API_NOT_FOUND",
            Field = field,
            Message = SanitiseText(message) ?? string.Empty
        };

    private static string? SanitiseText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        return PrivateReasoningMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? RedactedPrivateReasoning
            : value.Trim();
    }
}

public sealed record WorkflowReadOnlyApiEnvelope<TData>
{
    public required string Status { get; init; }
    public TData? Data { get; init; }
    public string WorkflowRunId { get; init; } = string.Empty;
    public string EvidenceId { get; init; } = string.Empty;
    public required WorkflowReadOnlyBoundaryStatusDto Boundary { get; init; }
    public bool MutationOccurred { get; init; }
    public bool HumanApprovalRequired { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<WorkflowReadOnlyApiErrorDto> Errors { get; init; } = [];
}

public sealed record WorkflowReadOnlyBoundaryStatusDto
{
    public bool ReadOnlyInspection { get; init; }
    public bool WorkflowStatusIsAction { get; init; }
    public bool EvidenceIsPermission { get; init; }
    public bool GroundingIsAuthority { get; init; }
    public bool EndpointAccessIsExecutionPermission { get; init; }
    public bool ApiResponseStatusIsGovernance { get; init; }
    public bool ModelOutputIsAuthority { get; init; }
    public bool SourceApplied { get; init; }
    public bool MemoryPromoted { get; init; }
    public bool ReleaseApproved { get; init; }
    public bool ApprovalSatisfied { get; init; }
    public bool HumanReviewRequiredForSourceApply { get; init; }
    public bool HumanReviewRequiredForMemoryPromotion { get; init; }
}

public sealed record WorkflowAuthorityFlagsDto
{
    public bool GrantsApproval { get; init; }
    public bool GrantsExecution { get; init; }
    public bool MutatesSource { get; init; }
    public bool PromotesMemory { get; init; }
    public bool StartsWorkflow { get; init; }
    public bool ContinuesWorkflow { get; init; }
    public bool ResumesWorkflow { get; init; }
    public bool RetriesWorkflow { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransfersAuthority { get; init; }
    public bool ApprovesRelease { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
}

public sealed record WorkflowRunListResponseDto
{
    public required IReadOnlyList<WorkflowRunSummaryReadDto> Items { get; init; }
    public required int Take { get; init; }
}

public sealed record WorkflowStepListResponseDto
{
    public required IReadOnlyList<WorkflowStepSummaryReadDto> Items { get; init; }
    public required int Take { get; init; }
}

public sealed record WorkflowCheckpointListResponseDto
{
    public required IReadOnlyList<WorkflowCheckpointSummaryReadDto> Items { get; init; }
    public required int Take { get; init; }
}

public sealed record WorkflowRunSummaryReadDto
{
    public required Guid WorkflowRunId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string WorkflowType { get; init; }
    public required string WorkflowName { get; init; }
    public required string Status { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public int StepCount { get; init; }
    public int EvidenceReferenceCount { get; init; }
    public int GroundingReferenceCount { get; init; }
    public required WorkflowAuthorityFlagsDto AuthorityFlags { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowRunReadDto
{
    public required Guid WorkflowRunId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string WorkflowType { get; init; }
    public required string WorkflowName { get; init; }
    public required string Status { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public string? SubjectSummary { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required IReadOnlyList<WorkflowStepReadDto> Steps { get; init; }
    public required IReadOnlyList<WorkflowEvidenceReferenceReadDto> EvidenceReferences { get; init; }
    public required IReadOnlyList<WorkflowGroundingReferenceReadDto> GroundingReferences { get; init; }
    public required WorkflowAuthorityFlagsDto AuthorityFlags { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowStepSummaryReadDto
{
    public required Guid WorkflowRunStepId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string StepKey { get; init; }
    public required string StepName { get; init; }
    public required string StepType { get; init; }
    public required string Status { get; init; }
    public int? SequenceNumber { get; init; }
    public string? AgentRole { get; init; }
    public string? AgentId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public int EvidenceReferenceCount { get; init; }
    public int GroundingReferenceCount { get; init; }
    public required WorkflowAuthorityFlagsDto AuthorityFlags { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowStepReadDto
{
    public required Guid WorkflowRunStepId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string StepKey { get; init; }
    public required string StepName { get; init; }
    public required string StepType { get; init; }
    public required string Status { get; init; }
    public int? SequenceNumber { get; init; }
    public string? AgentRole { get; init; }
    public string? AgentId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? SafeSummary { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required IReadOnlyList<WorkflowEvidenceReferenceReadDto> EvidenceReferences { get; init; }
    public required IReadOnlyList<WorkflowGroundingReferenceReadDto> GroundingReferences { get; init; }
    public required WorkflowAuthorityFlagsDto AuthorityFlags { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowCheckpointSummaryReadDto
{
    public required Guid WorkflowCheckpointId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CheckpointKey { get; init; }
    public required string CheckpointName { get; init; }
    public required string CheckpointType { get; init; }
    public required string Status { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? StateHashSha256 { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public int EvidenceReferenceCount { get; init; }
    public int GroundingReferenceCount { get; init; }
    public required WorkflowAuthorityFlagsDto AuthorityFlags { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowCheckpointReadDto
{
    public required Guid WorkflowCheckpointId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CheckpointKey { get; init; }
    public required string CheckpointName { get; init; }
    public required string CheckpointType { get; init; }
    public required string Status { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? SafeSummary { get; init; }
    public int StateVersion { get; init; }
    public string? StateHashSha256 { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required IReadOnlyList<WorkflowEvidenceReferenceReadDto> EvidenceReferences { get; init; }
    public required IReadOnlyList<WorkflowGroundingReferenceReadDto> GroundingReferences { get; init; }
    public required WorkflowAuthorityFlagsDto AuthorityFlags { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowEvidenceReferenceReadDto
{
    public required Guid EvidenceReferenceId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public string? StepKey { get; init; }
    public required Guid ProjectId { get; init; }
    public required string EvidenceType { get; init; }
    public required string EvidenceId { get; init; }
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public string? AllowedUse { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? AgentHandoffId { get; init; }
    public Guid? ThoughtLedgerEntryId { get; init; }
    public Guid? GroundingEvidenceReferenceId { get; init; }
    public Guid? WorkflowRunEvidenceReferenceId { get; init; }
    public bool EvidenceIsPermission { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowGroundingReferenceReadDto
{
    public required Guid GroundingReferenceId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public string? StepKey { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid GroundingEvidenceReferenceId { get; init; }
    public required string ClaimType { get; init; }
    public required string ClaimId { get; init; }
    public string? SafeSummary { get; init; }
    public bool GroundingIsAuthority { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowReadOnlyApiErrorDto
{
    public required string Category { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}
