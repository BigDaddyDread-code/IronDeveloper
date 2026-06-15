using System.Text.Json;

namespace IronDev.Core.Workflow;

public interface IApplyDryRunStore
{
    Task<ApplyDryRunStoreResult> CreateAsync(ApplyDryRunCreateRequest? request, CancellationToken cancellationToken = default);

    Task<ApplyDryRunRecord?> GetByIdAsync(string dryRunId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApplyDryRunSummary>> ListByWorkflowRunAsync(string workflowRunId, int take = ApplyDryRunStoreValidator.DefaultTake, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApplyDryRunSummary>> ListByControlledApplyPlanAsync(string controlledApplyPlanReferenceId, int take = ApplyDryRunStoreValidator.DefaultTake, CancellationToken cancellationToken = default);
}

public enum ApplyDryRunRecordStatus
{
    Unknown = 0,
    Stored = 1,
    RejectedUnsafeMaterial = 2,
    InvalidRecord = 3
}

public enum ApplyDryRunOutcomeKind
{
    Unknown = 0,
    NotPerformed = 1,
    PreviewOnly = 2,
    BlockedByMissingEvidence = 3,
    BlockedByUnsafeMaterial = 4
}

public enum ApplyDryRunStoreStatus
{
    Unknown = 0,
    Stored = 1,
    DuplicateRejected = 2,
    InvalidRequest = 3,
    UnsafeMaterialRejected = 4,
    NotFound = 5
}

public enum ApplyDryRunReferenceKind
{
    Unknown = 0,
    ControlledApplyPlan = 1,
    SourceApplyApprovalRequirement = 2,
    PatchProposalEvidencePackage = 3,
    HumanApprovalPackage = 4,
    WorkflowStepEvaluation = 5,
    PolicyPreflight = 6,
    A2aValidation = 7,
    ThoughtLedger = 8,
    GovernanceEvent = 9,
    ValidationEvidence = 10,
    RollbackEvidence = 11
}

public enum ApplyDryRunGateKind
{
    Unknown = 0,
    SourceChangeForbidden = 1,
    PatchApplicationForbidden = 2,
    CommandUnavailable = 3,
    ToolUnavailable = 4,
    ApprovalStillRequired = 5,
    PolicyStillRequired = 6,
    WorkflowTransitionForbidden = 7,
    ReviewRequired = 8
}

public enum ApplyDryRunRiskKind
{
    Unknown = 0,
    SourceChangeRisk = 1,
    PatchApplicationRisk = 2,
    ValidationRisk = 3,
    RollbackRisk = 4,
    ApprovalRisk = 5,
    PolicyRisk = 6,
    WorkflowRisk = 7
}

public enum ApplyDryRunRiskSeverity
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

public enum ApplyDryRunReason
{
    Unknown = 0,
    StoreRecordOnly,
    SuppliedEvidenceOnly,
    DryRunNotPerformed,
    SourceNotApplied,
    PatchNotApplied,
    FilesNotRead,
    FilesNotMutated,
    CommandNotRun,
    ToolNotInvoked,
    AgentNotDispatched,
    ModelNotCalled,
    ValidationNotRun,
    RollbackNotRun,
    ApprovalNotSatisfied,
    PolicyNotSatisfied,
    WorkflowNotTransitioned,
    MemoryNotPromoted,
    RetrievalNotActivated,
    SqlRecordOnly,
    MissingRequest,
    MissingDryRunId,
    MissingWorkflowRunId,
    MissingWorkflowStepId,
    MissingControlledApplyPlanReferenceId,
    MissingProjectReferenceId,
    MissingTargetReferenceId,
    MissingSafeSummary,
    MissingEvidenceReference,
    MissingGateReference,
    MissingValidationReference,
    MissingRollbackReference,
    UnknownRecordStatus,
    UnknownOutcomeKind,
    UnknownReferenceKind,
    UnknownGateKind,
    UnknownRiskKind,
    UnknownRiskSeverity,
    UnsafeReferenceText,
    UnsafeSummary,
    UnsafeEvidenceReference,
    UnsafeGateReference,
    UnsafeValidationReference,
    UnsafeRollbackReference,
    UnsafeRiskReference,
    UnsafeMissingEvidence,
    AuthorityFlagRejected,
    InvalidJson,
    DuplicateRejected,
    RecordStored
}

public record ApplyDryRunCreateRequest
{
    public string DryRunId { get; init; } = string.Empty;
    public string WorkflowRunId { get; init; } = string.Empty;
    public string WorkflowStepId { get; init; } = string.Empty;
    public string ControlledApplyPlanReferenceId { get; init; } = string.Empty;
    public string SourceApplyApprovalRequirementReferenceId { get; init; } = string.Empty;
    public string PatchProposalEvidencePackageReferenceId { get; init; } = string.Empty;
    public string ProjectReferenceId { get; init; } = string.Empty;
    public string TargetReferenceId { get; init; } = string.Empty;
    public ApplyDryRunRecordStatus Status { get; init; } = ApplyDryRunRecordStatus.Stored;
    public ApplyDryRunOutcomeKind OutcomeKind { get; init; } = ApplyDryRunOutcomeKind.NotPerformed;
    public string SafeSummary { get; init; } = string.Empty;
    public IReadOnlyList<ApplyDryRunReference> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<ApplyDryRunGateReference> GateReferences { get; init; } = [];
    public IReadOnlyList<ApplyDryRunReference> ValidationReferences { get; init; } = [];
    public IReadOnlyList<ApplyDryRunReference> RollbackReferences { get; init; } = [];
    public IReadOnlyList<ApplyDryRunRisk> Risks { get; init; } = [];
    public IReadOnlyList<ApplyDryRunMissingEvidence> MissingEvidence { get; init; } = [];
    public string CorrelationId { get; init; } = string.Empty;
    public string MetadataJson { get; init; } = "{\"schema\":\"apply.dryrun.store.v1\"}";
    public bool IsStoreRecordOnly { get; init; } = true;
    public bool IsDryRunPerformed { get; init; }
    public bool IsSourceApply { get; init; }
    public bool IsPatchApplication { get; init; }
    public bool IsApproval { get; init; }
    public bool IsApprovalSatisfied { get; init; }
    public bool CanPerformDryRun { get; init; }
    public bool CanApplySource { get; init; }
    public bool CanMutateFiles { get; init; }
    public bool CanReadSourceFiles { get; init; }
    public bool CanRunCommand { get; init; }
    public bool CanInvokeTool { get; init; }
    public bool CanRunValidation { get; init; }
    public bool CanRollback { get; init; }
    public bool CanSatisfyPolicy { get; init; }
    public bool CanTransitionWorkflow { get; init; }
    public bool CanPromoteMemory { get; init; }
    public bool CanActivateRetrieval { get; init; }
}

public sealed record ApplyDryRunRecord : ApplyDryRunCreateRequest
{
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ApplyDryRunSummary
{
    public required string DryRunId { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string ControlledApplyPlanReferenceId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required string TargetReferenceId { get; init; }
    public required ApplyDryRunRecordStatus Status { get; init; }
    public required ApplyDryRunOutcomeKind OutcomeKind { get; init; }
    public required int EvidenceReferenceCount { get; init; }
    public required int GateReferenceCount { get; init; }
    public required int ValidationReferenceCount { get; init; }
    public required int RollbackReferenceCount { get; init; }
    public required int RiskCount { get; init; }
    public required int MissingEvidenceCount { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record ApplyDryRunReference
{
    public ApplyDryRunReferenceKind Kind { get; init; } = ApplyDryRunReferenceKind.Unknown;
    public string ReferenceId { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
}

public sealed record ApplyDryRunGateReference
{
    public ApplyDryRunGateKind Kind { get; init; } = ApplyDryRunGateKind.Unknown;
    public string ReferenceId { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
}

public sealed record ApplyDryRunRisk
{
    public ApplyDryRunRiskKind Kind { get; init; } = ApplyDryRunRiskKind.Unknown;
    public ApplyDryRunRiskSeverity Severity { get; init; } = ApplyDryRunRiskSeverity.Unknown;
    public string RiskId { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
}

public sealed record ApplyDryRunMissingEvidence
{
    public ApplyDryRunReferenceKind Kind { get; init; } = ApplyDryRunReferenceKind.Unknown;
    public string ReferenceId { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
}

public sealed record ApplyDryRunStoreIssue
{
    public required ApplyDryRunReason Reason { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public sealed record ApplyDryRunValidationResult
{
    public required bool IsValid { get; init; }
    public required bool HasUnsafeMaterial { get; init; }
    public IReadOnlyList<ApplyDryRunStoreIssue> Issues { get; init; } = [];
    public ApplyDryRunCreateRequest? NormalizedRequest { get; init; }
}

public sealed record ApplyDryRunStoreResult
{
    public required ApplyDryRunStoreStatus Status { get; init; }
    public ApplyDryRunRecord? Record { get; init; }
    public IReadOnlyList<ApplyDryRunStoreIssue> Issues { get; init; } = [];
    public IReadOnlyList<ApplyDryRunReason> Reasons { get; init; } = [];
}

public sealed class ApplyDryRunStoreValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    private const int MaxJsonLength = 32_000;

    private static readonly ApplyDryRunReason[] BoundaryReasons =
    [
        ApplyDryRunReason.StoreRecordOnly,
        ApplyDryRunReason.SuppliedEvidenceOnly,
        ApplyDryRunReason.DryRunNotPerformed,
        ApplyDryRunReason.SourceNotApplied,
        ApplyDryRunReason.PatchNotApplied,
        ApplyDryRunReason.FilesNotRead,
        ApplyDryRunReason.FilesNotMutated,
        ApplyDryRunReason.CommandNotRun,
        ApplyDryRunReason.ToolNotInvoked,
        ApplyDryRunReason.AgentNotDispatched,
        ApplyDryRunReason.ModelNotCalled,
        ApplyDryRunReason.ValidationNotRun,
        ApplyDryRunReason.RollbackNotRun,
        ApplyDryRunReason.ApprovalNotSatisfied,
        ApplyDryRunReason.PolicyNotSatisfied,
        ApplyDryRunReason.WorkflowNotTransitioned,
        ApplyDryRunReason.MemoryNotPromoted,
        ApplyDryRunReason.RetrievalNotActivated,
        ApplyDryRunReason.SqlRecordOnly
    ];

    private static readonly string[] UnsafeMarkers =
    [
        "private reasoning",
        "hidden reasoning",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "entire patch",
        "entirepatch",
        "patch payload",
        "patch applied",
        "patchapplied",
        "ready to apply",
        "readytoapply",
        "validation passed",
        "validationpassed",
        "rollback completed",
        "rollbackcompleted",
        "approval granted",
        "approvalgranted",
        "policy satisfied",
        "policysatisfied",
        "execution allowed",
        "executionallowed",
        "tool executed",
        "toolexecuted",
        "source mutated",
        "sourcemutated",
        "memory promoted",
        "memorypromoted",
        "authority transferred",
        "authoritytransferred",
        "release approved",
        "releaseapproved"
    ];

    public static int NormalizeTake(int take) => Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);

    public ApplyDryRunValidationResult ValidateCreate(ApplyDryRunCreateRequest? request)
    {
        var issues = new List<ApplyDryRunStoreIssue>();
        if (request is null)
        {
            AddIssue(issues, ApplyDryRunReason.MissingRequest, "Apply dry-run create request is required.", nameof(ApplyDryRunCreateRequest));
            return Result(issues, null);
        }

        RequireId(request.DryRunId, ApplyDryRunReason.MissingDryRunId, nameof(request.DryRunId), issues);
        RequireId(request.WorkflowRunId, ApplyDryRunReason.MissingWorkflowRunId, nameof(request.WorkflowRunId), issues);
        RequireId(request.WorkflowStepId, ApplyDryRunReason.MissingWorkflowStepId, nameof(request.WorkflowStepId), issues);
        RequireId(request.ControlledApplyPlanReferenceId, ApplyDryRunReason.MissingControlledApplyPlanReferenceId, nameof(request.ControlledApplyPlanReferenceId), issues);
        RequireId(request.ProjectReferenceId, ApplyDryRunReason.MissingProjectReferenceId, nameof(request.ProjectReferenceId), issues);
        RequireId(request.TargetReferenceId, ApplyDryRunReason.MissingTargetReferenceId, nameof(request.TargetReferenceId), issues);
        RequireId(request.SafeSummary, ApplyDryRunReason.MissingSafeSummary, nameof(request.SafeSummary), issues);

        if (!Enum.IsDefined(request.Status) || request.Status is ApplyDryRunRecordStatus.Unknown)
            AddIssue(issues, ApplyDryRunReason.UnknownRecordStatus, "Apply dry-run status is required.", nameof(request.Status));
        if (!Enum.IsDefined(request.OutcomeKind) || request.OutcomeKind is ApplyDryRunOutcomeKind.Unknown)
            AddIssue(issues, ApplyDryRunReason.UnknownOutcomeKind, "Apply dry-run outcome kind is required.", nameof(request.OutcomeKind));

        ValidateText(request.DryRunId, ApplyDryRunReason.UnsafeReferenceText, nameof(request.DryRunId), issues);
        ValidateText(request.WorkflowRunId, ApplyDryRunReason.UnsafeReferenceText, nameof(request.WorkflowRunId), issues);
        ValidateText(request.WorkflowStepId, ApplyDryRunReason.UnsafeReferenceText, nameof(request.WorkflowStepId), issues);
        ValidateText(request.ControlledApplyPlanReferenceId, ApplyDryRunReason.UnsafeReferenceText, nameof(request.ControlledApplyPlanReferenceId), issues);
        ValidateText(request.SourceApplyApprovalRequirementReferenceId, ApplyDryRunReason.UnsafeReferenceText, nameof(request.SourceApplyApprovalRequirementReferenceId), issues);
        ValidateText(request.PatchProposalEvidencePackageReferenceId, ApplyDryRunReason.UnsafeReferenceText, nameof(request.PatchProposalEvidencePackageReferenceId), issues);
        ValidateText(request.ProjectReferenceId, ApplyDryRunReason.UnsafeReferenceText, nameof(request.ProjectReferenceId), issues);
        ValidateText(request.TargetReferenceId, ApplyDryRunReason.UnsafeReferenceText, nameof(request.TargetReferenceId), issues);
        ValidateText(request.CorrelationId, ApplyDryRunReason.UnsafeReferenceText, nameof(request.CorrelationId), issues);
        ValidateText(request.SafeSummary, ApplyDryRunReason.UnsafeSummary, nameof(request.SafeSummary), issues);
        ValidateJson(request.MetadataJson, nameof(request.MetadataJson), issues);

        ValidateReferences(request.EvidenceReferences, ApplyDryRunReason.MissingEvidenceReference, ApplyDryRunReason.UnsafeEvidenceReference, issues);
        ValidateGateReferences(request.GateReferences, issues);
        ValidateReferences(request.ValidationReferences, ApplyDryRunReason.MissingValidationReference, ApplyDryRunReason.UnsafeValidationReference, issues);
        ValidateReferences(request.RollbackReferences, ApplyDryRunReason.MissingRollbackReference, ApplyDryRunReason.UnsafeRollbackReference, issues);
        ValidateRisks(request.Risks, issues);
        ValidateMissingEvidence(request.MissingEvidence, issues);
        ValidateFlags(request, issues);

        return Result(issues, Normalize(request));
    }

    private static ApplyDryRunCreateRequest Normalize(ApplyDryRunCreateRequest request) =>
        request with
        {
            DryRunId = NormalizeText(request.DryRunId),
            WorkflowRunId = NormalizeText(request.WorkflowRunId),
            WorkflowStepId = NormalizeText(request.WorkflowStepId),
            ControlledApplyPlanReferenceId = NormalizeText(request.ControlledApplyPlanReferenceId),
            SourceApplyApprovalRequirementReferenceId = NormalizeText(request.SourceApplyApprovalRequirementReferenceId),
            PatchProposalEvidencePackageReferenceId = NormalizeText(request.PatchProposalEvidencePackageReferenceId),
            ProjectReferenceId = NormalizeText(request.ProjectReferenceId),
            TargetReferenceId = NormalizeText(request.TargetReferenceId),
            SafeSummary = NormalizeText(request.SafeSummary),
            CorrelationId = NormalizeText(request.CorrelationId),
            MetadataJson = NormalizeText(request.MetadataJson),
            EvidenceReferences = request.EvidenceReferences.Select(NormalizeReference).ToArray(),
            GateReferences = request.GateReferences.Select(NormalizeGate).ToArray(),
            ValidationReferences = request.ValidationReferences.Select(NormalizeReference).ToArray(),
            RollbackReferences = request.RollbackReferences.Select(NormalizeReference).ToArray(),
            Risks = request.Risks.Select(NormalizeRisk).ToArray(),
            MissingEvidence = request.MissingEvidence.Select(NormalizeMissing).ToArray()
        };

    private static ApplyDryRunReference NormalizeReference(ApplyDryRunReference reference) =>
        reference with
        {
            ReferenceId = NormalizeText(reference.ReferenceId),
            SafeSummary = NormalizeText(reference.SafeSummary)
        };

    private static ApplyDryRunGateReference NormalizeGate(ApplyDryRunGateReference reference) =>
        reference with
        {
            ReferenceId = NormalizeText(reference.ReferenceId),
            SafeSummary = NormalizeText(reference.SafeSummary)
        };

    private static ApplyDryRunRisk NormalizeRisk(ApplyDryRunRisk risk) =>
        risk with
        {
            RiskId = NormalizeText(risk.RiskId),
            SafeSummary = NormalizeText(risk.SafeSummary)
        };

    private static ApplyDryRunMissingEvidence NormalizeMissing(ApplyDryRunMissingEvidence missing) =>
        missing with
        {
            ReferenceId = NormalizeText(missing.ReferenceId),
            SafeSummary = NormalizeText(missing.SafeSummary)
        };

    private static void ValidateReferences(
        IReadOnlyList<ApplyDryRunReference> references,
        ApplyDryRunReason missingReason,
        ApplyDryRunReason unsafeReason,
        List<ApplyDryRunStoreIssue> issues)
    {
        if (references.Count == 0)
        {
            AddIssue(issues, missingReason, "At least one reference is required.", missingReason.ToString());
            return;
        }

        foreach (var reference in references)
        {
            if (!Enum.IsDefined(reference.Kind) || reference.Kind is ApplyDryRunReferenceKind.Unknown)
                AddIssue(issues, ApplyDryRunReason.UnknownReferenceKind, "Reference kind is required.", nameof(ApplyDryRunReference.Kind));
            RequireId(reference.ReferenceId, missingReason, nameof(ApplyDryRunReference.ReferenceId), issues);
            ValidateText(reference.ReferenceId, unsafeReason, nameof(ApplyDryRunReference.ReferenceId), issues);
            ValidateText(reference.SafeSummary, unsafeReason, nameof(ApplyDryRunReference.SafeSummary), issues);
        }
    }

    private static void ValidateGateReferences(IReadOnlyList<ApplyDryRunGateReference> references, List<ApplyDryRunStoreIssue> issues)
    {
        if (references.Count == 0)
        {
            AddIssue(issues, ApplyDryRunReason.MissingGateReference, "At least one gate reference is required.", nameof(ApplyDryRunCreateRequest.GateReferences));
            return;
        }

        foreach (var reference in references)
        {
            if (!Enum.IsDefined(reference.Kind) || reference.Kind is ApplyDryRunGateKind.Unknown)
                AddIssue(issues, ApplyDryRunReason.UnknownGateKind, "Gate kind is required.", nameof(ApplyDryRunGateReference.Kind));
            RequireId(reference.ReferenceId, ApplyDryRunReason.MissingGateReference, nameof(ApplyDryRunGateReference.ReferenceId), issues);
            ValidateText(reference.ReferenceId, ApplyDryRunReason.UnsafeGateReference, nameof(ApplyDryRunGateReference.ReferenceId), issues);
            ValidateText(reference.SafeSummary, ApplyDryRunReason.UnsafeGateReference, nameof(ApplyDryRunGateReference.SafeSummary), issues);
        }
    }

    private static void ValidateRisks(IReadOnlyList<ApplyDryRunRisk> risks, List<ApplyDryRunStoreIssue> issues)
    {
        foreach (var risk in risks)
        {
            if (!Enum.IsDefined(risk.Kind) || risk.Kind is ApplyDryRunRiskKind.Unknown)
                AddIssue(issues, ApplyDryRunReason.UnknownRiskKind, "Risk kind is invalid.", nameof(ApplyDryRunRisk.Kind));
            if (!Enum.IsDefined(risk.Severity) || risk.Severity is ApplyDryRunRiskSeverity.Unknown)
                AddIssue(issues, ApplyDryRunReason.UnknownRiskSeverity, "Risk severity is invalid.", nameof(ApplyDryRunRisk.Severity));
            RequireId(risk.RiskId, ApplyDryRunReason.MissingEvidenceReference, nameof(ApplyDryRunRisk.RiskId), issues);
            ValidateText(risk.RiskId, ApplyDryRunReason.UnsafeRiskReference, nameof(ApplyDryRunRisk.RiskId), issues);
            ValidateText(risk.SafeSummary, ApplyDryRunReason.UnsafeRiskReference, nameof(ApplyDryRunRisk.SafeSummary), issues);
        }
    }

    private static void ValidateMissingEvidence(IReadOnlyList<ApplyDryRunMissingEvidence> missingEvidence, List<ApplyDryRunStoreIssue> issues)
    {
        foreach (var missing in missingEvidence)
        {
            if (!Enum.IsDefined(missing.Kind) || missing.Kind is ApplyDryRunReferenceKind.Unknown)
                AddIssue(issues, ApplyDryRunReason.UnknownReferenceKind, "Missing evidence kind is required.", nameof(ApplyDryRunMissingEvidence.Kind));
            RequireId(missing.ReferenceId, ApplyDryRunReason.MissingEvidenceReference, nameof(ApplyDryRunMissingEvidence.ReferenceId), issues);
            ValidateText(missing.ReferenceId, ApplyDryRunReason.UnsafeMissingEvidence, nameof(ApplyDryRunMissingEvidence.ReferenceId), issues);
            ValidateText(missing.SafeSummary, ApplyDryRunReason.UnsafeMissingEvidence, nameof(ApplyDryRunMissingEvidence.SafeSummary), issues);
        }
    }

    private static void ValidateFlags(ApplyDryRunCreateRequest request, List<ApplyDryRunStoreIssue> issues)
    {
        if (!request.IsStoreRecordOnly)
            AddIssue(issues, ApplyDryRunReason.AuthorityFlagRejected, "Apply dry-run store records must remain record-only.", nameof(request.IsStoreRecordOnly));

        RejectFlag(request.IsDryRunPerformed, nameof(request.IsDryRunPerformed), issues);
        RejectFlag(request.IsSourceApply, nameof(request.IsSourceApply), issues);
        RejectFlag(request.IsPatchApplication, nameof(request.IsPatchApplication), issues);
        RejectFlag(request.IsApproval, nameof(request.IsApproval), issues);
        RejectFlag(request.IsApprovalSatisfied, nameof(request.IsApprovalSatisfied), issues);
        RejectFlag(request.CanPerformDryRun, nameof(request.CanPerformDryRun), issues);
        RejectFlag(request.CanApplySource, nameof(request.CanApplySource), issues);
        RejectFlag(request.CanMutateFiles, nameof(request.CanMutateFiles), issues);
        RejectFlag(request.CanReadSourceFiles, nameof(request.CanReadSourceFiles), issues);
        RejectFlag(request.CanRunCommand, nameof(request.CanRunCommand), issues);
        RejectFlag(request.CanInvokeTool, nameof(request.CanInvokeTool), issues);
        RejectFlag(request.CanRunValidation, nameof(request.CanRunValidation), issues);
        RejectFlag(request.CanRollback, nameof(request.CanRollback), issues);
        RejectFlag(request.CanSatisfyPolicy, nameof(request.CanSatisfyPolicy), issues);
        RejectFlag(request.CanTransitionWorkflow, nameof(request.CanTransitionWorkflow), issues);
        RejectFlag(request.CanPromoteMemory, nameof(request.CanPromoteMemory), issues);
        RejectFlag(request.CanActivateRetrieval, nameof(request.CanActivateRetrieval), issues);
    }

    private static void ValidateJson(string? value, string field, List<ApplyDryRunStoreIssue> issues)
    {
        RequireId(value, ApplyDryRunReason.InvalidJson, field, issues);
        if (value is null)
            return;

        if (value.Length > MaxJsonLength)
            AddIssue(issues, ApplyDryRunReason.InvalidJson, "Metadata JSON is too large.", field);
        ValidateText(value, ApplyDryRunReason.UnsafeReferenceText, field, issues);

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
                AddIssue(issues, ApplyDryRunReason.InvalidJson, "Metadata JSON must be an object.", field);
            ValidateJsonElement(document.RootElement, field, issues);
        }
        catch (JsonException)
        {
            AddIssue(issues, ApplyDryRunReason.InvalidJson, "Metadata JSON must be valid JSON.", field);
        }
    }

    private static void ValidateJsonElement(JsonElement element, string field, List<ApplyDryRunStoreIssue> issues)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    ValidateText(property.Name, ApplyDryRunReason.UnsafeReferenceText, field, issues);
                    ValidateJsonElement(property.Value, field, issues);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    ValidateJsonElement(item, field, issues);
                break;
            case JsonValueKind.String:
                ValidateText(element.GetString(), ApplyDryRunReason.UnsafeReferenceText, field, issues);
                break;
        }
    }

    private static void RequireId(string? value, ApplyDryRunReason reason, string field, List<ApplyDryRunStoreIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            AddIssue(issues, reason, "Required value is missing.", field);
    }

    private static void ValidateText(string? value, ApplyDryRunReason reason, string field, List<ApplyDryRunStoreIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            AddIssue(issues, reason, "Text contains unsafe material or authority-claiming language.", field);
    }

    private static void RejectFlag(bool value, string field, List<ApplyDryRunStoreIssue> issues)
    {
        if (value)
            AddIssue(issues, ApplyDryRunReason.AuthorityFlagRejected, "Apply dry-run store authority/action flags must be false.", field);
    }

    private static ApplyDryRunValidationResult Result(List<ApplyDryRunStoreIssue> issues, ApplyDryRunCreateRequest? normalizedRequest)
    {
        var unsafeMaterial = issues.Any(issue => issue.Reason.ToString().Contains("Unsafe", StringComparison.OrdinalIgnoreCase));
        return new ApplyDryRunValidationResult
        {
            IsValid = issues.Count == 0,
            HasUnsafeMaterial = unsafeMaterial,
            Issues = issues,
            NormalizedRequest = issues.Count == 0 ? normalizedRequest : null
        };
    }

    public static ApplyDryRunStoreResult Invalid(IReadOnlyList<ApplyDryRunStoreIssue> issues, bool unsafeMaterial = false) =>
        new()
        {
            Status = unsafeMaterial ? ApplyDryRunStoreStatus.UnsafeMaterialRejected : ApplyDryRunStoreStatus.InvalidRequest,
            Issues = issues,
            Reasons = BoundaryReasons.Concat(issues.Select(issue => issue.Reason)).Distinct().ToArray()
        };

    public static ApplyDryRunStoreResult Stored(ApplyDryRunRecord record) =>
        new()
        {
            Status = ApplyDryRunStoreStatus.Stored,
            Record = record,
            Reasons = BoundaryReasons.Concat([ApplyDryRunReason.RecordStored]).Distinct().ToArray()
        };

    public static ApplyDryRunStoreResult Duplicate(IReadOnlyList<ApplyDryRunStoreIssue> issues) =>
        new()
        {
            Status = ApplyDryRunStoreStatus.DuplicateRejected,
            Issues = issues,
            Reasons = BoundaryReasons.Concat([ApplyDryRunReason.DuplicateRejected]).Distinct().ToArray()
        };

    private static void AddIssue(List<ApplyDryRunStoreIssue> issues, ApplyDryRunReason reason, string message, string field) =>
        issues.Add(new ApplyDryRunStoreIssue
        {
            Reason = reason,
            Message = message,
            Field = field
        });

    private static string NormalizeText(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
