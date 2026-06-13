using System.Text.Json;

namespace IronDev.Core.Workflow;

public enum WorkflowRunStatus
{
    Created = 1,
    ReadyForReview = 2,
    Blocked = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
    Superseded = 7
}

public enum WorkflowRunStepType
{
    Planning = 1,
    Review = 2,
    Validation = 3,
    HandoffSummary = 4,
    GroundingSummary = 5,
    HumanDecisionSupport = 6,
    EvidenceCollection = 7,
    Receipt = 8,
    PolicyEvaluationInput = 9,
    ApprovalRequirementEvaluation = 10,
    DebugFinding = 11,
    ReviewFinding = 12
}

public enum WorkflowRunEvidenceType
{
    GovernanceEvent = 1,
    ToolRequest = 2,
    ToolGateDecision = 3,
    ApprovalDecision = 4,
    PolicyDecisionEvent = 5,
    DogfoodReceipt = 6,
    AgentHandoff = 7,
    ThoughtLedgerReference = 8,
    GroundingEvidenceReference = 9,
    CriticReview = 10,
    ValidationOutput = 11,
    HumanNote = 12,
    RunReport = 13,
    ApprovalPackage = 14
}

public enum WorkflowRunEvidenceAllowedUse
{
    Context = 1,
    Review = 2,
    Debugging = 3,
    Validation = 4,
    Traceability = 5,
    HumanDecisionSupport = 6,
    AuditReference = 7,
    PolicyInput = 8,
    HandoffExplanation = 9,
    RequirementEvaluation = 10,
    Grounding = 11
}

public enum WorkflowRunGroundingClaimType
{
    EvidenceSupport = 1,
    RequirementTrace = 2,
    DecisionTrace = 3,
    HandoffTrace = 4,
    PolicyTrace = 5,
    ValidationTrace = 6
}

public sealed record WorkflowRun
{
    public required Guid WorkflowRunId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string WorkflowType { get; init; }
    public required string WorkflowName { get; init; }
    public required WorkflowRunStatus Status { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public string? SubjectSummary { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public required bool GrantsApproval { get; init; }
    public required bool GrantsExecution { get; init; }
    public required bool MutatesSource { get; init; }
    public required bool PromotesMemory { get; init; }
    public required bool StartsWorkflow { get; init; }
    public required bool ContinuesWorkflow { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransfersAuthority { get; init; }
    public required bool ApprovesRelease { get; init; }
    public required bool CreatesAcceptedMemory { get; init; }
    public required IReadOnlyList<WorkflowRunStep> Steps { get; init; }
    public required IReadOnlyList<WorkflowRunEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<WorkflowRunGroundingReference> GroundingReferences { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowRunStep
{
    public required Guid WorkflowRunStepId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string StepKey { get; init; }
    public required string StepName { get; init; }
    public required WorkflowRunStepType StepType { get; init; }
    public required WorkflowRunStatus Status { get; init; }
    public string? AgentRole { get; init; }
    public string? AgentId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? SafeSummary { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public required bool GrantsApproval { get; init; }
    public required bool GrantsExecution { get; init; }
    public required bool MutatesSource { get; init; }
    public required bool PromotesMemory { get; init; }
    public required bool StartsWorkflow { get; init; }
    public required bool ContinuesWorkflow { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransfersAuthority { get; init; }
    public required bool ApprovesRelease { get; init; }
    public required bool CreatesAcceptedMemory { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowRunEvidenceReference
{
    public required Guid WorkflowRunEvidenceReferenceId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public string? StepKey { get; init; }
    public required Guid ProjectId { get; init; }
    public required WorkflowRunEvidenceType EvidenceType { get; init; }
    public required string EvidenceId { get; init; }
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public WorkflowRunEvidenceAllowedUse? AllowedUse { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? AgentHandoffId { get; init; }
    public Guid? ThoughtLedgerEntryId { get; init; }
    public Guid? GroundingEvidenceReferenceId { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowRunGroundingReference
{
    public required Guid WorkflowRunGroundingReferenceId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public string? StepKey { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid GroundingEvidenceReferenceId { get; init; }
    public required WorkflowRunGroundingClaimType ClaimType { get; init; }
    public required string ClaimId { get; init; }
    public string? SafeSummary { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowRunCreateRequest
{
    public Guid? WorkflowRunId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string WorkflowType { get; init; }
    public required string WorkflowName { get; init; }
    public required WorkflowRunStatus Status { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public string? SubjectSummary { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public IReadOnlyList<WorkflowRunStepCreateRequest> Steps { get; init; } = [];
    public IReadOnlyList<WorkflowRunEvidenceReferenceCreateRequest> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<WorkflowRunGroundingReferenceCreateRequest> GroundingReferences { get; init; } = [];
    public bool GrantsApproval { get; init; }
    public bool GrantsExecution { get; init; }
    public bool MutatesSource { get; init; }
    public bool PromotesMemory { get; init; }
    public bool StartsWorkflow { get; init; }
    public bool ContinuesWorkflow { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransfersAuthority { get; init; }
    public bool ApprovesRelease { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
}

public sealed record WorkflowRunStepCreateRequest
{
    public required string StepKey { get; init; }
    public required string StepName { get; init; }
    public required WorkflowRunStepType StepType { get; init; }
    public required WorkflowRunStatus Status { get; init; }
    public string? AgentRole { get; init; }
    public string? AgentId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? SafeSummary { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public bool GrantsApproval { get; init; }
    public bool GrantsExecution { get; init; }
    public bool MutatesSource { get; init; }
    public bool PromotesMemory { get; init; }
    public bool StartsWorkflow { get; init; }
    public bool ContinuesWorkflow { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransfersAuthority { get; init; }
    public bool ApprovesRelease { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
}

public sealed record WorkflowRunEvidenceReferenceCreateRequest
{
    public string? StepKey { get; init; }
    public required WorkflowRunEvidenceType EvidenceType { get; init; }
    public required string EvidenceId { get; init; }
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public WorkflowRunEvidenceAllowedUse? AllowedUse { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? AgentHandoffId { get; init; }
    public Guid? ThoughtLedgerEntryId { get; init; }
    public Guid? GroundingEvidenceReferenceId { get; init; }
}

public sealed record WorkflowRunGroundingReferenceCreateRequest
{
    public string? StepKey { get; init; }
    public required Guid GroundingEvidenceReferenceId { get; init; }
    public required WorkflowRunGroundingClaimType ClaimType { get; init; }
    public required string ClaimId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record WorkflowRunSummary
{
    public required Guid WorkflowRunId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string WorkflowType { get; init; }
    public required string WorkflowName { get; init; }
    public required WorkflowRunStatus Status { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required int StepCount { get; init; }
    public required int EvidenceReferenceCount { get; init; }
    public required int GroundingReferenceCount { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowRunValidationIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public sealed record WorkflowRunValidationResult
{
    public required bool IsValid { get; init; }
    public IReadOnlyList<WorkflowRunValidationIssue> Issues { get; init; } = [];
}

public interface IWorkflowRunStore
{
    Task<WorkflowRun> CreateAsync(WorkflowRunCreateRequest request, CancellationToken cancellationToken = default);

    Task<WorkflowRun?> GetAsync(Guid projectId, Guid workflowRunId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowRunSummary>> ListByProjectAsync(Guid projectId, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowRunSummary>> ListByCorrelationAsync(Guid projectId, Guid correlationId, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowRunSummary>> ListBySubjectAsync(Guid projectId, string subjectType, string subjectId, int take, CancellationToken cancellationToken = default);
}

public sealed class WorkflowRunValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    private const int MaxMetadataJsonLength = 32_000;

    private static readonly HashSet<string> AllowedWorkflowTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ManualDogfoodLoop",
        "A2aHandoffReview",
        "SourceApplyReview",
        "MemoryPromotionReview",
        "PolicyReview",
        "EvidenceReview",
        "TestFailureRepairReview"
    };

    private static readonly string[] PrivateReasoningMarkers =
    [
        "hiddenReasoning",
        "chainOfThought",
        "chain-of-thought",
        "chain of thought",
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

    private static readonly string[] AuthorityMarkers =
    [
        "approval granted",
        "approved for execution",
        "execution permission",
        "execution allowed",
        "can execute",
        "authorize execution",
        "policy satisfied",
        "satisfy policy",
        "source applied",
        "apply source",
        "apply patch",
        "memory promoted",
        "promote memory",
        "accepted memory",
        "release approved",
        "approve release",
        "ready to ship",
        "can ship",
        "authority transferred",
        "transfer authority",
        "workflow continued",
        "continue workflow",
        "workflow started",
        "start workflow",
        "dispatch agent",
        "tool executed",
        "tool ran"
    ];

    public WorkflowRunValidationResult ValidateCreate(WorkflowRunCreateRequest? request)
    {
        var issues = new List<WorkflowRunValidationIssue>();
        if (request is null)
        {
            AddError(issues, "WORKFLOW_RUN_REQUIRED", "Workflow run create request is required.", nameof(WorkflowRunCreateRequest));
            return Result(issues);
        }

        ValidateCommonRun(
            request.ProjectId,
            request.WorkflowType,
            request.WorkflowName,
            request.Status,
            request.SubjectType,
            request.SubjectId,
            request.SubjectSummary,
            request.CreatedByActorType,
            request.CreatedByActorId,
            request.MetadataVersion,
            request.MetadataJson,
            request.GrantsApproval,
            request.GrantsExecution,
            request.MutatesSource,
            request.PromotesMemory,
            request.StartsWorkflow,
            request.ContinuesWorkflow,
            request.SatisfiesPolicy,
            request.TransfersAuthority,
            request.ApprovesRelease,
            request.CreatesAcceptedMemory,
            issues);

        ValidateSteps(request.Steps, issues);
        ValidateEvidence(request.EvidenceReferences, request.Steps, issues);
        ValidateGrounding(request.GroundingReferences, request.Steps, issues);
        return Result(issues);
    }

    public WorkflowRunValidationResult ValidateMaterialized(WorkflowRun run)
    {
        var issues = new List<WorkflowRunValidationIssue>();
        if (run.WorkflowRunId == Guid.Empty)
            AddError(issues, "WORKFLOW_RUN_ID_REQUIRED", "WorkflowRunId is required.", nameof(WorkflowRun.WorkflowRunId));

        ValidateCommonRun(
            run.ProjectId,
            run.WorkflowType,
            run.WorkflowName,
            run.Status,
            run.SubjectType,
            run.SubjectId,
            run.SubjectSummary,
            run.CreatedByActorType,
            run.CreatedByActorId,
            run.MetadataVersion,
            run.MetadataJson,
            run.GrantsApproval,
            run.GrantsExecution,
            run.MutatesSource,
            run.PromotesMemory,
            run.StartsWorkflow,
            run.ContinuesWorkflow,
            run.SatisfiesPolicy,
            run.TransfersAuthority,
            run.ApprovesRelease,
            run.CreatesAcceptedMemory,
            issues);

        return Result(issues);
    }

    public static int NormalizeTake(int take) => Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);

    public WorkflowRunCreateRequest Normalize(WorkflowRunCreateRequest request) =>
        request with
        {
            WorkflowType = request.WorkflowType.Trim(),
            WorkflowName = request.WorkflowName.Trim(),
            SubjectType = request.SubjectType.Trim(),
            SubjectId = request.SubjectId.Trim(),
            SubjectSummary = NormalizeOptional(request.SubjectSummary),
            CreatedByActorType = request.CreatedByActorType.Trim(),
            CreatedByActorId = request.CreatedByActorId.Trim(),
            Steps = request.Steps.Select(NormalizeStep).ToArray(),
            EvidenceReferences = request.EvidenceReferences.Select(NormalizeEvidence).ToArray(),
            GroundingReferences = request.GroundingReferences.Select(NormalizeGrounding).ToArray()
        };

    private static WorkflowRunStepCreateRequest NormalizeStep(WorkflowRunStepCreateRequest step) =>
        step with
        {
            StepKey = step.StepKey.Trim(),
            StepName = step.StepName.Trim(),
            AgentRole = NormalizeOptional(step.AgentRole),
            AgentId = NormalizeOptional(step.AgentId),
            SubjectType = NormalizeOptional(step.SubjectType),
            SubjectId = NormalizeOptional(step.SubjectId),
            SafeSummary = NormalizeOptional(step.SafeSummary)
        };

    private static WorkflowRunEvidenceReferenceCreateRequest NormalizeEvidence(WorkflowRunEvidenceReferenceCreateRequest evidence) =>
        evidence with
        {
            StepKey = NormalizeOptional(evidence.StepKey),
            EvidenceId = evidence.EvidenceId.Trim(),
            EvidenceLabel = NormalizeOptional(evidence.EvidenceLabel),
            SafeSummary = NormalizeOptional(evidence.SafeSummary)
        };

    private static WorkflowRunGroundingReferenceCreateRequest NormalizeGrounding(WorkflowRunGroundingReferenceCreateRequest grounding) =>
        grounding with
        {
            StepKey = NormalizeOptional(grounding.StepKey),
            ClaimId = grounding.ClaimId.Trim(),
            SafeSummary = NormalizeOptional(grounding.SafeSummary)
        };

    private static void ValidateCommonRun(
        Guid projectId,
        string workflowType,
        string workflowName,
        WorkflowRunStatus status,
        string subjectType,
        string subjectId,
        string? subjectSummary,
        string actorType,
        string actorId,
        int metadataVersion,
        string metadataJson,
        bool grantsApproval,
        bool grantsExecution,
        bool mutatesSource,
        bool promotesMemory,
        bool startsWorkflow,
        bool continuesWorkflow,
        bool satisfiesPolicy,
        bool transfersAuthority,
        bool approvesRelease,
        bool createsAcceptedMemory,
        List<WorkflowRunValidationIssue> issues)
    {
        if (projectId == Guid.Empty)
            AddError(issues, "WORKFLOW_RUN_PROJECT_ID_REQUIRED", "ProjectId is required.", nameof(WorkflowRunCreateRequest.ProjectId));

        if (string.IsNullOrWhiteSpace(workflowType))
            AddError(issues, "WORKFLOW_RUN_TYPE_REQUIRED", "WorkflowType is required.", nameof(WorkflowRunCreateRequest.WorkflowType));
        else if (!AllowedWorkflowTypes.Contains(workflowType.Trim()))
            AddError(issues, "WORKFLOW_RUN_TYPE_INVALID", "WorkflowType is not in the allowed workflow type vocabulary.", nameof(WorkflowRunCreateRequest.WorkflowType));

        Require(workflowName, "WORKFLOW_RUN_NAME_REQUIRED", nameof(WorkflowRunCreateRequest.WorkflowName), issues);
        Require(subjectType, "WORKFLOW_RUN_SUBJECT_TYPE_REQUIRED", nameof(WorkflowRunCreateRequest.SubjectType), issues);
        Require(subjectId, "WORKFLOW_RUN_SUBJECT_ID_REQUIRED", nameof(WorkflowRunCreateRequest.SubjectId), issues);
        Require(actorType, "WORKFLOW_RUN_ACTOR_TYPE_REQUIRED", nameof(WorkflowRunCreateRequest.CreatedByActorType), issues);
        Require(actorId, "WORKFLOW_RUN_ACTOR_ID_REQUIRED", nameof(WorkflowRunCreateRequest.CreatedByActorId), issues);

        if (!Enum.IsDefined(status))
            AddError(issues, "WORKFLOW_RUN_STATUS_INVALID", "Workflow run status is invalid.", nameof(WorkflowRunCreateRequest.Status));

        if (metadataVersion <= 0)
            AddError(issues, "WORKFLOW_RUN_METADATA_VERSION_INVALID", "MetadataVersion must be positive.", nameof(WorkflowRunCreateRequest.MetadataVersion));

        ValidateJson(metadataJson, "WORKFLOW_RUN_METADATA_JSON", nameof(WorkflowRunCreateRequest.MetadataJson), issues);
        ValidateTextSafety(workflowType, nameof(WorkflowRunCreateRequest.WorkflowType), issues);
        ValidateTextSafety(workflowName, nameof(WorkflowRunCreateRequest.WorkflowName), issues);
        ValidateTextSafety(subjectType, nameof(WorkflowRunCreateRequest.SubjectType), issues);
        ValidateTextSafety(subjectId, nameof(WorkflowRunCreateRequest.SubjectId), issues);
        ValidateTextSafety(subjectSummary, nameof(WorkflowRunCreateRequest.SubjectSummary), issues);
        ValidateTextSafety(actorType, nameof(WorkflowRunCreateRequest.CreatedByActorType), issues);
        ValidateTextSafety(actorId, nameof(WorkflowRunCreateRequest.CreatedByActorId), issues);

        RejectAuthorityFlag(grantsApproval, nameof(WorkflowRunCreateRequest.GrantsApproval), issues);
        RejectAuthorityFlag(grantsExecution, nameof(WorkflowRunCreateRequest.GrantsExecution), issues);
        RejectAuthorityFlag(mutatesSource, nameof(WorkflowRunCreateRequest.MutatesSource), issues);
        RejectAuthorityFlag(promotesMemory, nameof(WorkflowRunCreateRequest.PromotesMemory), issues);
        RejectAuthorityFlag(startsWorkflow, nameof(WorkflowRunCreateRequest.StartsWorkflow), issues);
        RejectAuthorityFlag(continuesWorkflow, nameof(WorkflowRunCreateRequest.ContinuesWorkflow), issues);
        RejectAuthorityFlag(satisfiesPolicy, nameof(WorkflowRunCreateRequest.SatisfiesPolicy), issues);
        RejectAuthorityFlag(transfersAuthority, nameof(WorkflowRunCreateRequest.TransfersAuthority), issues);
        RejectAuthorityFlag(approvesRelease, nameof(WorkflowRunCreateRequest.ApprovesRelease), issues);
        RejectAuthorityFlag(createsAcceptedMemory, nameof(WorkflowRunCreateRequest.CreatesAcceptedMemory), issues);
    }

    private static void ValidateSteps(IReadOnlyList<WorkflowRunStepCreateRequest> steps, List<WorkflowRunValidationIssue> issues)
    {
        if (steps is null || steps.Count == 0)
        {
            AddError(issues, "WORKFLOW_RUN_STEP_REQUIRED", "At least one workflow run step is required.", nameof(WorkflowRunCreateRequest.Steps));
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            if (step is null)
            {
                AddError(issues, "WORKFLOW_RUN_STEP_INVALID", "Workflow run step cannot be null.", nameof(WorkflowRunCreateRequest.Steps));
                continue;
            }

            Require(step.StepKey, "WORKFLOW_RUN_STEP_KEY_REQUIRED", nameof(WorkflowRunStepCreateRequest.StepKey), issues);
            Require(step.StepName, "WORKFLOW_RUN_STEP_NAME_REQUIRED", nameof(WorkflowRunStepCreateRequest.StepName), issues);
            if (!string.IsNullOrWhiteSpace(step.StepKey) && !seen.Add(step.StepKey.Trim()))
                AddError(issues, "WORKFLOW_RUN_STEP_KEY_DUPLICATE", "StepKey must be unique within the workflow run.", nameof(WorkflowRunStepCreateRequest.StepKey));
            if (!Enum.IsDefined(step.StepType))
                AddError(issues, "WORKFLOW_RUN_STEP_TYPE_INVALID", "StepType is invalid.", nameof(WorkflowRunStepCreateRequest.StepType));
            if (!Enum.IsDefined(step.Status))
                AddError(issues, "WORKFLOW_RUN_STEP_STATUS_INVALID", "Step status is invalid.", nameof(WorkflowRunStepCreateRequest.Status));
            if (step.MetadataVersion <= 0)
                AddError(issues, "WORKFLOW_RUN_STEP_METADATA_VERSION_INVALID", "Step MetadataVersion must be positive.", nameof(WorkflowRunStepCreateRequest.MetadataVersion));

            ValidateJson(step.MetadataJson, "WORKFLOW_RUN_STEP_METADATA_JSON", nameof(WorkflowRunStepCreateRequest.MetadataJson), issues);
            ValidateTextSafety(step.StepKey, nameof(WorkflowRunStepCreateRequest.StepKey), issues);
            ValidateTextSafety(step.StepName, nameof(WorkflowRunStepCreateRequest.StepName), issues);
            ValidateTextSafety(step.AgentRole, nameof(WorkflowRunStepCreateRequest.AgentRole), issues);
            ValidateTextSafety(step.AgentId, nameof(WorkflowRunStepCreateRequest.AgentId), issues);
            ValidateTextSafety(step.SubjectType, nameof(WorkflowRunStepCreateRequest.SubjectType), issues);
            ValidateTextSafety(step.SubjectId, nameof(WorkflowRunStepCreateRequest.SubjectId), issues);
            ValidateTextSafety(step.SafeSummary, nameof(WorkflowRunStepCreateRequest.SafeSummary), issues);

            RejectAuthorityFlag(step.GrantsApproval, nameof(WorkflowRunStepCreateRequest.GrantsApproval), issues);
            RejectAuthorityFlag(step.GrantsExecution, nameof(WorkflowRunStepCreateRequest.GrantsExecution), issues);
            RejectAuthorityFlag(step.MutatesSource, nameof(WorkflowRunStepCreateRequest.MutatesSource), issues);
            RejectAuthorityFlag(step.PromotesMemory, nameof(WorkflowRunStepCreateRequest.PromotesMemory), issues);
            RejectAuthorityFlag(step.StartsWorkflow, nameof(WorkflowRunStepCreateRequest.StartsWorkflow), issues);
            RejectAuthorityFlag(step.ContinuesWorkflow, nameof(WorkflowRunStepCreateRequest.ContinuesWorkflow), issues);
            RejectAuthorityFlag(step.SatisfiesPolicy, nameof(WorkflowRunStepCreateRequest.SatisfiesPolicy), issues);
            RejectAuthorityFlag(step.TransfersAuthority, nameof(WorkflowRunStepCreateRequest.TransfersAuthority), issues);
            RejectAuthorityFlag(step.ApprovesRelease, nameof(WorkflowRunStepCreateRequest.ApprovesRelease), issues);
            RejectAuthorityFlag(step.CreatesAcceptedMemory, nameof(WorkflowRunStepCreateRequest.CreatesAcceptedMemory), issues);
        }
    }

    private static void ValidateEvidence(IReadOnlyList<WorkflowRunEvidenceReferenceCreateRequest> evidenceReferences, IReadOnlyList<WorkflowRunStepCreateRequest> steps, List<WorkflowRunValidationIssue> issues)
    {
        if (evidenceReferences is null || evidenceReferences.Count == 0)
        {
            AddError(issues, "WORKFLOW_RUN_EVIDENCE_REQUIRED", "At least one evidence reference is required.", nameof(WorkflowRunCreateRequest.EvidenceReferences));
            return;
        }

        var stepKeys = (steps ?? []).Where(step => step is not null).Select(step => step.StepKey).Where(key => !string.IsNullOrWhiteSpace(key)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var evidence in evidenceReferences)
        {
            if (evidence is null)
            {
                AddError(issues, "WORKFLOW_RUN_EVIDENCE_INVALID", "Evidence reference cannot be null.", nameof(WorkflowRunCreateRequest.EvidenceReferences));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(evidence.StepKey) && !stepKeys.Contains(evidence.StepKey.Trim()))
                AddError(issues, "WORKFLOW_RUN_EVIDENCE_STEP_UNKNOWN", "Evidence StepKey must reference a workflow step in the same run.", nameof(WorkflowRunEvidenceReferenceCreateRequest.StepKey));
            if (!Enum.IsDefined(evidence.EvidenceType))
                AddError(issues, "WORKFLOW_RUN_EVIDENCE_TYPE_INVALID", "EvidenceType is invalid.", nameof(WorkflowRunEvidenceReferenceCreateRequest.EvidenceType));
            Require(evidence.EvidenceId, "WORKFLOW_RUN_EVIDENCE_ID_REQUIRED", nameof(WorkflowRunEvidenceReferenceCreateRequest.EvidenceId), issues);
            if (evidence.AllowedUse.HasValue && !Enum.IsDefined(evidence.AllowedUse.Value))
                AddError(issues, "WORKFLOW_RUN_EVIDENCE_ALLOWED_USE_INVALID", "AllowedUse is invalid.", nameof(WorkflowRunEvidenceReferenceCreateRequest.AllowedUse));

            ValidateTextSafety(evidence.StepKey, nameof(WorkflowRunEvidenceReferenceCreateRequest.StepKey), issues);
            ValidateTextSafety(evidence.EvidenceId, nameof(WorkflowRunEvidenceReferenceCreateRequest.EvidenceId), issues);
            ValidateTextSafety(evidence.EvidenceLabel, nameof(WorkflowRunEvidenceReferenceCreateRequest.EvidenceLabel), issues);
            ValidateTextSafety(evidence.SafeSummary, nameof(WorkflowRunEvidenceReferenceCreateRequest.SafeSummary), issues);
        }
    }

    private static void ValidateGrounding(IReadOnlyList<WorkflowRunGroundingReferenceCreateRequest> groundingReferences, IReadOnlyList<WorkflowRunStepCreateRequest> steps, List<WorkflowRunValidationIssue> issues)
    {
        var stepKeys = (steps ?? []).Where(step => step is not null).Select(step => step.StepKey).Where(key => !string.IsNullOrWhiteSpace(key)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var grounding in groundingReferences ?? [])
        {
            if (grounding is null)
            {
                AddError(issues, "WORKFLOW_RUN_GROUNDING_INVALID", "Grounding reference cannot be null.", nameof(WorkflowRunCreateRequest.GroundingReferences));
                continue;
            }

            if (grounding.GroundingEvidenceReferenceId == Guid.Empty)
                AddError(issues, "WORKFLOW_RUN_GROUNDING_ID_REQUIRED", "GroundingEvidenceReferenceId is required.", nameof(WorkflowRunGroundingReferenceCreateRequest.GroundingEvidenceReferenceId));
            if (!string.IsNullOrWhiteSpace(grounding.StepKey) && !stepKeys.Contains(grounding.StepKey.Trim()))
                AddError(issues, "WORKFLOW_RUN_GROUNDING_STEP_UNKNOWN", "Grounding StepKey must reference a workflow step in the same run.", nameof(WorkflowRunGroundingReferenceCreateRequest.StepKey));
            if (!Enum.IsDefined(grounding.ClaimType))
                AddError(issues, "WORKFLOW_RUN_GROUNDING_CLAIM_TYPE_INVALID", "ClaimType is invalid.", nameof(WorkflowRunGroundingReferenceCreateRequest.ClaimType));
            Require(grounding.ClaimId, "WORKFLOW_RUN_GROUNDING_CLAIM_ID_REQUIRED", nameof(WorkflowRunGroundingReferenceCreateRequest.ClaimId), issues);
            ValidateTextSafety(grounding.StepKey, nameof(WorkflowRunGroundingReferenceCreateRequest.StepKey), issues);
            ValidateTextSafety(grounding.ClaimId, nameof(WorkflowRunGroundingReferenceCreateRequest.ClaimId), issues);
            ValidateTextSafety(grounding.SafeSummary, nameof(WorkflowRunGroundingReferenceCreateRequest.SafeSummary), issues);
        }
    }

    private static void ValidateJson(string? value, string codePrefix, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(issues, codePrefix + "_REQUIRED", "JSON metadata is required.", field);
            return;
        }

        if (value.Length > MaxMetadataJsonLength)
            AddError(issues, codePrefix + "_TOO_LARGE", "JSON metadata is too large.", field);

        ValidateTextSafety(value, field, issues);

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                AddError(issues, codePrefix + "_OBJECT_REQUIRED", "JSON metadata must be an object.", field);
            ValidateJsonElement(document.RootElement, field, issues);
        }
        catch (JsonException)
        {
            AddError(issues, codePrefix + "_INVALID", "JSON metadata must be valid JSON.", field);
        }
    }

    private static void ValidateJsonElement(JsonElement element, string field, List<WorkflowRunValidationIssue> issues)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    ValidateTextSafety(property.Name, field, issues);
                    if (IsAuthorityProperty(property.Name) && IsTruthy(property.Value))
                        AddError(issues, "WORKFLOW_RUN_AUTHORITY_METADATA_BLOCKED", $"Metadata property cannot grant authority: {property.Name}.", field);
                    ValidateJsonElement(property.Value, field, issues);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    ValidateJsonElement(item, field, issues);
                break;
            case JsonValueKind.String:
                ValidateTextSafety(element.GetString(), field, issues);
                break;
        }
    }

    private static bool IsAuthorityProperty(string propertyName) =>
        propertyName.Contains("grants", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("execution", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("mutatesSource", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("promotesMemory", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("startsWorkflow", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("continuesWorkflow", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("satisfiesPolicy", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("transfersAuthority", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("approvesRelease", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("createsAcceptedMemory", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("workflowStarted", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("workflowContinued", StringComparison.OrdinalIgnoreCase);

    private static bool IsTruthy(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => false,
            JsonValueKind.String => !string.Equals(value.GetString(), "false", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(value.GetString(), "no", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(value.GetString(), "0", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => value.TryGetInt32(out var number) && number != 0,
            _ => true
        };

    private static void Require(string? value, string code, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            AddError(issues, code, "Required text is blank.", field);
    }

    private static void ValidateTextSafety(string? value, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (ContainsAny(value, PrivateReasoningMarkers))
            AddError(issues, "WORKFLOW_RUN_PRIVATE_REASONING_BLOCKED", "Workflow run text must not contain hidden/private reasoning or raw dump markers.", field);

        if (ContainsAny(value, AuthorityMarkers))
            AddError(issues, "WORKFLOW_RUN_AUTHORITY_LANGUAGE_BLOCKED", "Workflow run text must not claim approval, execution, policy satisfaction, source apply, memory promotion, release approval, workflow continuation, or authority transfer.", field);
    }

    private static void RejectAuthorityFlag(bool value, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (value)
            AddError(issues, "WORKFLOW_RUN_AUTHORITY_FLAG_BLOCKED", "Workflow run authority/action flags must be false.", field);
    }

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static WorkflowRunValidationResult Result(List<WorkflowRunValidationIssue> issues) =>
        new()
        {
            IsValid = issues.All(issue => !string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            Issues = issues
        };

    private static void AddError(List<WorkflowRunValidationIssue> issues, string code, string message, string field) =>
        issues.Add(new WorkflowRunValidationIssue
        {
            Code = code,
            Severity = "error",
            Message = message,
            Field = field
        });
}
