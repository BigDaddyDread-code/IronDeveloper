namespace IronDev.Core.Workflow;

public interface ISourceApplyApprovalRequirementContract
{
    SourceApplyApprovalRequirementResult Evaluate(SourceApplyApprovalRequirementRequest? request);
}

public sealed class SourceApplyApprovalRequirementContract : ISourceApplyApprovalRequirementContract
{
    private static readonly string[] UnsafeMarkers =
    [
        "private reasoning",
        "hidden reasoning",
        "chainofthought",
        "chain of thought",
        "chain-of-thought",
        "scratchpad",
        "rawprompt",
        "raw prompt",
        "rawcompletion",
        "raw completion",
        "rawtooloutput",
        "raw tool output",
        "wholepatch",
        "whole patch",
        "entirepatch",
        "entire patch",
        "patchpayload",
        "patch payload",
        "source content",
        "approved",
        "approval granted",
        "approval satisfied",
        "policy satisfied",
        "source applied",
        "patch applied",
        "workflow continued",
        "execution allowed",
        "tool invoked",
        "command executed",
        "agent dispatched",
        "model called",
        "prompt built",
        "ticket created",
        "memory promoted",
        "retrieval activated",
        "sql written",
        "release ready"
    ];

    public SourceApplyApprovalRequirementResult Evaluate(SourceApplyApprovalRequirementRequest? request)
    {
        if (request is null)
            return Result(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                SourceApplyApprovalRequirementStatus.InvalidRequest,
                SourceApplyTargetKind.Unknown,
                SourceApplyApprovalRequirementKind.Unknown,
                [SourceApplyApprovalRequirementReason.MissingWorkflowRunId],
                [],
                [],
                [],
                ["request"]);

        var workflowRunId = SafeText(request.WorkflowRunId);
        var workflowStepId = SafeText(request.WorkflowStepId);
        var sourceApplyRequestReferenceId = SafeText(request.SourceApplyRequestReferenceId);
        var projectReferenceId = SafeText(request.ProjectReferenceId);
        var targetReferenceId = SafeText(request.TargetReferenceId);
        var invalidReasons = InvalidReasons(request).ToArray();

        var safeEvidence = SafeEvidence(request.EvidenceReferences);
        var safeGates = SafeGateHints(request.GateHints);
        var safeRisks = SafeRisks(request.Risks);

        if (invalidReasons.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                sourceApplyRequestReferenceId,
                string.Empty,
                projectReferenceId,
                targetReferenceId,
                SourceApplyApprovalRequirementStatus.InvalidRequest,
                request.TargetKind,
                request.RequirementKind,
                invalidReasons,
                safeEvidence,
                safeGates,
                safeRisks,
                MissingRequirementsForInvalid(invalidReasons));

        var gateBlockReasons = GateBlockReasons(request).ToArray();
        if (gateBlockReasons.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                sourceApplyRequestReferenceId,
                RequirementReferenceId(workflowRunId, workflowStepId, sourceApplyRequestReferenceId, targetReferenceId),
                projectReferenceId,
                targetReferenceId,
                SourceApplyApprovalRequirementStatus.BlockedByWorkflowGate,
                request.TargetKind,
                request.RequirementKind,
                gateBlockReasons,
                safeEvidence,
                safeGates,
                safeRisks,
                ["non-blocking workflow gate material"]);

        var missing = MissingApprovalMaterial(request).ToArray();
        if (missing.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                sourceApplyRequestReferenceId,
                RequirementReferenceId(workflowRunId, workflowStepId, sourceApplyRequestReferenceId, targetReferenceId),
                projectReferenceId,
                targetReferenceId,
                SourceApplyApprovalRequirementStatus.MissingRequiredApprovalMaterial,
                request.TargetKind,
                request.RequirementKind,
                MissingMaterialReasons(request),
                safeEvidence,
                safeGates,
                safeRisks,
                missing);

        return Result(
            workflowRunId,
            workflowStepId,
            sourceApplyRequestReferenceId,
            RequirementReferenceId(workflowRunId, workflowStepId, sourceApplyRequestReferenceId, targetReferenceId),
            projectReferenceId,
            targetReferenceId,
            SourceApplyApprovalRequirementStatus.ApprovalRequired,
            request.TargetKind,
            request.RequirementKind,
            BoundaryReasons(),
            safeEvidence,
            safeGates,
            safeRisks,
            []);
    }

    private static IEnumerable<SourceApplyApprovalRequirementReason> InvalidReasons(SourceApplyApprovalRequirementRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowRunId))
            yield return SourceApplyApprovalRequirementReason.MissingWorkflowRunId;

        if (string.IsNullOrWhiteSpace(request.WorkflowStepId))
            yield return SourceApplyApprovalRequirementReason.MissingWorkflowStepId;

        if (string.IsNullOrWhiteSpace(request.SourceApplyRequestReferenceId))
            yield return SourceApplyApprovalRequirementReason.MissingSourceApplyRequestReference;

        if (string.IsNullOrWhiteSpace(request.ProjectReferenceId))
            yield return SourceApplyApprovalRequirementReason.MissingProjectReference;

        if (string.IsNullOrWhiteSpace(request.TargetReferenceId))
            yield return SourceApplyApprovalRequirementReason.MissingTargetReference;

        if (request.TargetKind == SourceApplyTargetKind.Unknown)
            yield return SourceApplyApprovalRequirementReason.InvalidTargetKind;

        if (request.RequirementKind == SourceApplyApprovalRequirementKind.Unknown)
            yield return SourceApplyApprovalRequirementReason.InvalidRequirementKind;

        if (ContainsUnsafeInput(request) ||
            request.EvidenceReferences.Any(reference => reference.Kind == SourceApplyApprovalEvidenceKind.Unknown || string.IsNullOrWhiteSpace(reference.ReferenceId)) ||
            request.GateHints.Any(hint => hint.Kind == SourceApplyApprovalGateKind.Unknown) ||
            request.Risks.Any(risk => risk.Kind == SourceApplyApprovalRiskKind.Unknown))
        {
            yield return SourceApplyApprovalRequirementReason.UnsafeInput;
        }
    }

    private static IEnumerable<SourceApplyApprovalRequirementReason> GateBlockReasons(SourceApplyApprovalRequirementRequest request)
    {
        if (request.StepEvaluation is not null && request.StepEvaluation.Eligibility != WorkflowStepRunnerEligibility.EligibleForFutureExecution)
            yield return SourceApplyApprovalRequirementReason.BlockedByRunnerEvaluation;

        if (request.DryRunResult is not null && request.DryRunResult.Status != WorkflowDryRunStatus.DryRunCompleted)
            yield return SourceApplyApprovalRequirementReason.BlockedByDryRun;

        if (request.RouteSuggestion is not null && RouteBlocks(request.RouteSuggestion))
            yield return SourceApplyApprovalRequirementReason.BlockedByRouteSuggestion;

        if (request.ImplementationProposal is not null && request.ImplementationProposal.Status != ImplementationProposalPackageCandidateStatus.ProposalPackageProduced)
            yield return SourceApplyApprovalRequirementReason.BlockedByImplementationProposal;

        if (request.HumanApprovalPackage is not null && request.HumanApprovalPackage.Status != HumanApprovalPackageCandidateStatus.ApprovalPackageProduced)
            yield return SourceApplyApprovalRequirementReason.BlockedByHumanApprovalPackage;
    }

    private static bool RouteBlocks(BoxedLangGraphRouteSuggestion route) =>
        route.Label is BoxedLangGraphRouteLabel.Unknown or
            BoxedLangGraphRouteLabel.InvalidRoutingSnapshot or
            BoxedLangGraphRouteLabel.NoRouteSuggested or
            BoxedLangGraphRouteLabel.BlockedInvalidStep or
            BoxedLangGraphRouteLabel.BlockedMissingEvidence or
            BoxedLangGraphRouteLabel.BlockedPolicyPreflight or
            BoxedLangGraphRouteLabel.BlockedA2aValidation or
            BoxedLangGraphRouteLabel.BlockedApprovalRequired ||
        !route.IsAdvisoryOnly ||
        route.WorkflowDecisionAuthority ||
        route.WorkflowStateChangeAllowed ||
        route.StepWorkAllowed ||
        route.AgentSendAllowed ||
        route.A2aSendAllowed ||
        route.ToolUseAllowed ||
        route.ApprovalChangeAllowed ||
        route.PolicySatisfactionAllowed ||
        route.SourceChangeAllowed ||
        route.MemoryPromotionAllowed ||
        route.RetrievalActivationAllowed;

    private static IEnumerable<string> MissingApprovalMaterial(SourceApplyApprovalRequirementRequest request)
    {
        if (request.ImplementationProposal is null)
            yield return "implementation proposal package";

        if (request.HumanApprovalPackage is null)
            yield return "human approval review package";

        if (!request.GateHints.Any(hint => hint.Kind == SourceApplyApprovalGateKind.HumanApprovalRequired ||
                                           hint.Kind == SourceApplyApprovalGateKind.PolicyEvidenceRequired ||
                                           hint.Kind == SourceApplyApprovalGateKind.ApprovalRecordingRequiredLater))
            yield return "source apply approval gate hint";

        if (request.EvidenceReferences.Count == 0)
            yield return "source apply approval evidence reference";
    }

    private static IReadOnlyList<SourceApplyApprovalRequirementReason> MissingMaterialReasons(SourceApplyApprovalRequirementRequest request)
    {
        var reasons = new List<SourceApplyApprovalRequirementReason>();

        if (request.ImplementationProposal is null)
            reasons.Add(SourceApplyApprovalRequirementReason.MissingImplementationProposal);

        if (request.HumanApprovalPackage is null)
            reasons.Add(SourceApplyApprovalRequirementReason.MissingHumanApprovalReviewPackage);

        if (!request.GateHints.Any(hint => hint.Kind == SourceApplyApprovalGateKind.HumanApprovalRequired ||
                                           hint.Kind == SourceApplyApprovalGateKind.PolicyEvidenceRequired ||
                                           hint.Kind == SourceApplyApprovalGateKind.ApprovalRecordingRequiredLater))
            reasons.Add(SourceApplyApprovalRequirementReason.MissingApprovalGateHint);

        reasons.Add(SourceApplyApprovalRequirementReason.ApprovalRequired);
        reasons.Add(SourceApplyApprovalRequirementReason.ApprovalNotGranted);
        return reasons.Distinct().OrderBy(reason => reason).ToArray();
    }

    private static IReadOnlyList<SourceApplyApprovalEvidenceReference> SafeEvidence(IEnumerable<SourceApplyApprovalEvidenceReference> references) =>
        references
            .Where(reference => reference.Kind != SourceApplyApprovalEvidenceKind.Unknown)
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId))
            .Where(reference => !ContainsUnsafeMarker(reference.ReferenceId) && !ContainsUnsafeMarker(reference.SafeSummary))
            .Select(reference => reference with { ReferenceId = SafeText(reference.ReferenceId), SafeSummary = SafeNullableText(reference.SafeSummary) })
            .Distinct()
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<SourceApplyApprovalGateHint> SafeGateHints(IEnumerable<SourceApplyApprovalGateHint> hints) =>
        hints
            .Where(hint => hint.Kind != SourceApplyApprovalGateKind.Unknown)
            .Where(hint => !ContainsUnsafeMarker(hint.SafeSummary))
            .Select(hint => hint with { SafeSummary = SafeNullableText(hint.SafeSummary) })
            .Distinct()
            .OrderBy(hint => hint.Kind)
            .ThenBy(hint => hint.SeverityHint)
            .ThenBy(hint => hint.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<SourceApplyApprovalRisk> SafeRisks(IEnumerable<SourceApplyApprovalRisk> risks) =>
        risks
            .Where(risk => risk.Kind != SourceApplyApprovalRiskKind.Unknown)
            .Where(risk => !ContainsUnsafeMarker(risk.SafeSummary))
            .Select(risk => risk with { SafeSummary = SafeNullableText(risk.SafeSummary) })
            .Distinct()
            .OrderBy(risk => risk.Kind)
            .ThenBy(risk => risk.SeverityHint)
            .ThenBy(risk => risk.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static SourceApplyApprovalRequirementResult Result(
        string workflowRunId,
        string workflowStepId,
        string sourceApplyRequestReferenceId,
        string requirementReferenceId,
        string projectReferenceId,
        string targetReferenceId,
        SourceApplyApprovalRequirementStatus status,
        SourceApplyTargetKind targetKind,
        SourceApplyApprovalRequirementKind requirementKind,
        IReadOnlyList<SourceApplyApprovalRequirementReason> reasons,
        IReadOnlyList<SourceApplyApprovalEvidenceReference> evidenceReferences,
        IReadOnlyList<SourceApplyApprovalGateHint> gateHints,
        IReadOnlyList<SourceApplyApprovalRisk> risks,
        IReadOnlyList<string> missingRequirements) =>
        new()
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            SourceApplyRequestReferenceId = sourceApplyRequestReferenceId,
            RequirementReferenceId = requirementReferenceId,
            ProjectReferenceId = projectReferenceId,
            TargetReferenceId = targetReferenceId,
            Status = status,
            TargetKind = targetKind,
            RequirementKind = requirementKind,
            Reasons = reasons.Concat(BoundaryReasons()).Distinct().OrderBy(reason => reason).ToArray(),
            EvidenceReferences = evidenceReferences,
            GateHints = gateHints,
            Risks = risks,
            MissingRequirements = missingRequirements.Where(value => !ContainsUnsafeMarker(value)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            SafeSummaryLines = SummaryLines(status).ToArray(),
            IsRequirementOnly = true,
            IsSourceApply = false,
            IsApproval = false,
            IsApprovalSatisfied = false,
            CanApplySource = false,
            CanApplyPatch = false,
            CanMutateFiles = false,
            CanRunCommand = false,
            CanInvokeTool = false,
            CanDispatchAgent = false,
            CanCallModel = false,
            CanBuildPrompt = false,
            CanSatisfyApproval = false,
            CanSatisfyPolicy = false,
            CanTransitionWorkflow = false,
            CanCreateTicket = false,
            CanPromoteMemory = false,
            CanActivateRetrieval = false,
            CanWriteSql = false
        };

    private static IEnumerable<string> SummaryLines(SourceApplyApprovalRequirementStatus status)
    {
        yield return "Source apply approval requirement was evaluated from supplied references only.";
        yield return "Source apply requires explicit later approval.";
        yield return "Human approval package is review material only.";
        yield return "Implementation proposal package is review material only.";
        yield return "Approval was not granted.";
        yield return "Approval was not satisfied.";
        yield return "Policy was not satisfied.";
        yield return "Source was not applied.";
        yield return "Patch was not applied.";
        yield return "Workflow was not transitioned.";
        yield return "Source apply remains unimplemented.";
        yield return $"Requirement status: {status}.";
    }

    private static SourceApplyApprovalRequirementReason[] BoundaryReasons() =>
    [
        SourceApplyApprovalRequirementReason.RequirementOnly,
        SourceApplyApprovalRequirementReason.SourceApplyNotImplemented,
        SourceApplyApprovalRequirementReason.ApprovalRequired,
        SourceApplyApprovalRequirementReason.ApprovalNotGranted,
        SourceApplyApprovalRequirementReason.ApprovalNotSatisfied,
        SourceApplyApprovalRequirementReason.PolicyNotSatisfied,
        SourceApplyApprovalRequirementReason.WorkflowNotTransitioned,
        SourceApplyApprovalRequirementReason.SourceNotApplied,
        SourceApplyApprovalRequirementReason.PatchNotApplied,
        SourceApplyApprovalRequirementReason.FilesNotMutated,
        SourceApplyApprovalRequirementReason.CommandNotRun,
        SourceApplyApprovalRequirementReason.ToolNotInvoked,
        SourceApplyApprovalRequirementReason.AgentNotDispatched,
        SourceApplyApprovalRequirementReason.ModelNotCalled,
        SourceApplyApprovalRequirementReason.PromptNotBuilt,
        SourceApplyApprovalRequirementReason.TicketNotCreated,
        SourceApplyApprovalRequirementReason.MemoryNotPromoted,
        SourceApplyApprovalRequirementReason.RetrievalNotActivated,
        SourceApplyApprovalRequirementReason.SqlNotWritten
    ];

    private static IReadOnlyList<string> MissingRequirementsForInvalid(IEnumerable<SourceApplyApprovalRequirementReason> reasons) =>
        reasons.Select(reason => reason.ToString()).OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private static string RequirementReferenceId(string workflowRunId, string workflowStepId, string sourceApplyRequestReferenceId, string targetReferenceId) =>
        string.IsNullOrWhiteSpace(workflowRunId) ||
        string.IsNullOrWhiteSpace(workflowStepId) ||
        string.IsNullOrWhiteSpace(sourceApplyRequestReferenceId) ||
        string.IsNullOrWhiteSpace(targetReferenceId)
            ? string.Empty
            : $"source-apply-approval-requirement:{workflowRunId}:{workflowStepId}:{sourceApplyRequestReferenceId}:{targetReferenceId}";

    private static bool ContainsUnsafeInput(SourceApplyApprovalRequirementRequest request) =>
        ContainsUnsafeMarker(request.WorkflowRunId) ||
        ContainsUnsafeMarker(request.WorkflowStepId) ||
        ContainsUnsafeMarker(request.SourceApplyRequestReferenceId) ||
        ContainsUnsafeMarker(request.ProjectReferenceId) ||
        ContainsUnsafeMarker(request.TargetReferenceId) ||
        ContainsUnsafeMarker(request.CorrelationId) ||
        request.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
        request.GateHints.Any(hint => ContainsUnsafeMarker(hint.SafeSummary)) ||
        request.Risks.Any(risk => ContainsUnsafeMarker(risk.SafeSummary)) ||
        ContainsUnsafeImplementationProposal(request.ImplementationProposal) ||
        ContainsUnsafeHumanApprovalPackage(request.HumanApprovalPackage) ||
        ContainsUnsafeRunnerEvaluation(request.StepEvaluation) ||
        ContainsUnsafeDryRun(request.DryRunResult) ||
        ContainsUnsafeRoute(request.RouteSuggestion);

    private static bool ContainsUnsafeImplementationProposal(ImplementationProposalPackageCandidateResult? result) =>
        result is not null &&
        (ContainsUnsafeMarker(result.WorkflowRunId) ||
            ContainsUnsafeMarker(result.WorkflowStepId) ||
            ContainsUnsafeMarker(result.ProposalReferenceId) ||
            ContainsUnsafeMarker(result.ProposalPackageReferenceId) ||
            ContainsUnsafeMarker(result.TargetReferenceId) ||
            result.SafePackageSummaryLines.Any(ContainsUnsafeMarker) ||
            result.MissingEvidence.Any(ContainsUnsafeMarker) ||
            result.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.AffectedAreas.Any(area => ContainsUnsafeMarker(area.ReferenceId) || ContainsUnsafeMarker(area.SafeSummary)) ||
            result.ProposedSteps.Any(step => ContainsUnsafeMarker(step.SafeSummary)) ||
            result.ValidationSteps.Any(step => ContainsUnsafeMarker(step.SafeSummary)) ||
            result.Risks.Any(risk => ContainsUnsafeMarker(risk.SafeSummary)));

    private static bool ContainsUnsafeHumanApprovalPackage(HumanApprovalPackageCandidateResult? result) =>
        result is not null &&
        (ContainsUnsafeMarker(result.WorkflowRunId) ||
            ContainsUnsafeMarker(result.WorkflowStepId) ||
            ContainsUnsafeMarker(result.ApprovalPackageReferenceId) ||
            ContainsUnsafeMarker(result.PackageReferenceId) ||
            ContainsUnsafeMarker(result.ProjectReferenceId) ||
            ContainsUnsafeMarker(result.TargetReferenceId) ||
            result.SafePackageSummaryLines.Any(ContainsUnsafeMarker) ||
            result.MissingEvidence.Any(ContainsUnsafeMarker) ||
            result.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.CandidatePackageReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.GateHints.Any(hint => ContainsUnsafeMarker(hint.SafeSummary)) ||
            result.Risks.Any(risk => ContainsUnsafeMarker(risk.SafeSummary)));

    private static bool ContainsUnsafeRunnerEvaluation(WorkflowStepRunnerEvaluation? evaluation) =>
        evaluation is not null &&
        (ContainsUnsafeMarker(evaluation.StepId) ||
            evaluation.MissingEvidenceRequirements.Any(requirement => ContainsUnsafeMarker(requirement.RequirementId) || ContainsUnsafeMarker(requirement.SafeSummary)) ||
            evaluation.MissingPolicyRequirements.Any(requirement => ContainsUnsafeMarker(requirement.ReferenceId) || ContainsUnsafeMarker(requirement.CorrelationId)) ||
            evaluation.MissingA2aHandoffEvidence.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.CorrelationId)) ||
            evaluation.MissingApprovalRequirements.Any(requirement => ContainsUnsafeMarker(requirement.RequirementId) || ContainsUnsafeMarker(requirement.SafeSummary)));

    private static bool ContainsUnsafeDryRun(WorkflowDryRunResult? result) =>
        result is not null &&
        (ContainsUnsafeMarker(result.WorkflowRunId) ||
            ContainsUnsafeMarker(result.WorkflowStepId) ||
            result.SafeReportLines.Any(ContainsUnsafeMarker));

    private static bool ContainsUnsafeRoute(BoxedLangGraphRouteSuggestion? route) =>
        route is not null &&
        (ContainsUnsafeMarker(route.WorkflowRunId) ||
            ContainsUnsafeMarker(route.WorkflowStepId) ||
            route.SourceStatusReferences.Any(ContainsUnsafeMarker) ||
            route.SafeReportLines.Any(ContainsUnsafeMarker));

    private static string SafeText(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? string.Empty : value.Trim();

    private static string? SafeNullableText(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? null : value.Trim();

    private static bool ContainsUnsafeMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}

public sealed record SourceApplyApprovalRequirementRequest
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string SourceApplyRequestReferenceId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required string TargetReferenceId { get; init; }
    public required SourceApplyTargetKind TargetKind { get; init; }
    public required SourceApplyApprovalRequirementKind RequirementKind { get; init; }
    public ImplementationProposalPackageCandidateResult? ImplementationProposal { get; init; }
    public HumanApprovalPackageCandidateResult? HumanApprovalPackage { get; init; }
    public IReadOnlyList<SourceApplyApprovalEvidenceReference> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<SourceApplyApprovalGateHint> GateHints { get; init; } = [];
    public IReadOnlyList<SourceApplyApprovalRisk> Risks { get; init; } = [];
    public WorkflowStepRunnerEvaluation? StepEvaluation { get; init; }
    public WorkflowDryRunResult? DryRunResult { get; init; }
    public BoxedLangGraphRouteSuggestion? RouteSuggestion { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record SourceApplyApprovalRequirementResult
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string SourceApplyRequestReferenceId { get; init; }
    public required string RequirementReferenceId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required string TargetReferenceId { get; init; }
    public required SourceApplyApprovalRequirementStatus Status { get; init; }
    public required SourceApplyTargetKind TargetKind { get; init; }
    public required SourceApplyApprovalRequirementKind RequirementKind { get; init; }
    public required IReadOnlyList<SourceApplyApprovalRequirementReason> Reasons { get; init; }
    public required IReadOnlyList<SourceApplyApprovalEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<SourceApplyApprovalGateHint> GateHints { get; init; }
    public required IReadOnlyList<SourceApplyApprovalRisk> Risks { get; init; }
    public required IReadOnlyList<string> MissingRequirements { get; init; }
    public required IReadOnlyList<string> SafeSummaryLines { get; init; }
    public required bool IsRequirementOnly { get; init; }
    public required bool IsSourceApply { get; init; }
    public required bool IsApproval { get; init; }
    public required bool IsApprovalSatisfied { get; init; }
    public required bool CanApplySource { get; init; }
    public required bool CanApplyPatch { get; init; }
    public required bool CanMutateFiles { get; init; }
    public required bool CanRunCommand { get; init; }
    public required bool CanInvokeTool { get; init; }
    public required bool CanDispatchAgent { get; init; }
    public required bool CanCallModel { get; init; }
    public required bool CanBuildPrompt { get; init; }
    public required bool CanSatisfyApproval { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanTransitionWorkflow { get; init; }
    public required bool CanCreateTicket { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanActivateRetrieval { get; init; }
    public required bool CanWriteSql { get; init; }
}

public sealed record SourceApplyApprovalEvidenceReference
{
    public required SourceApplyApprovalEvidenceKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record SourceApplyApprovalGateHint
{
    public required SourceApplyApprovalGateKind Kind { get; init; }
    public SourceApplyApprovalSeverityHint SeverityHint { get; init; } = SourceApplyApprovalSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record SourceApplyApprovalRisk
{
    public required SourceApplyApprovalRiskKind Kind { get; init; }
    public SourceApplyApprovalSeverityHint SeverityHint { get; init; } = SourceApplyApprovalSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public enum SourceApplyTargetKind
{
    Unknown = 0,
    ImplementationProposalPackage = 1,
    SourceApplyCandidate = 2,
    PatchApplyCandidate = 3,
    RepositoryMutationCandidate = 4
}

public enum SourceApplyApprovalRequirementKind
{
    Unknown = 0,
    HumanApprovalRequired = 1,
    PolicyApprovalRequired = 2,
    HumanAndPolicyApprovalRequired = 3
}

public enum SourceApplyApprovalRequirementStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    BlockedByWorkflowGate = 2,
    MissingRequiredApprovalMaterial = 3,
    ApprovalRequired = 4
}

public enum SourceApplyApprovalRequirementReason
{
    Unknown = 0,
    RequirementOnly = 1,
    SourceApplyNotImplemented = 2,
    ApprovalRequired = 3,
    ApprovalNotGranted = 4,
    ApprovalNotSatisfied = 5,
    PolicyNotSatisfied = 6,
    WorkflowNotTransitioned = 7,
    SourceNotApplied = 8,
    PatchNotApplied = 9,
    FilesNotMutated = 10,
    CommandNotRun = 11,
    ToolNotInvoked = 12,
    AgentNotDispatched = 13,
    ModelNotCalled = 14,
    PromptNotBuilt = 15,
    TicketNotCreated = 16,
    MemoryNotPromoted = 17,
    RetrievalNotActivated = 18,
    SqlNotWritten = 19,
    MissingWorkflowRunId = 20,
    MissingWorkflowStepId = 21,
    MissingSourceApplyRequestReference = 22,
    MissingProjectReference = 23,
    MissingTargetReference = 24,
    InvalidTargetKind = 25,
    InvalidRequirementKind = 26,
    MissingImplementationProposal = 27,
    MissingHumanApprovalReviewPackage = 28,
    MissingApprovalGateHint = 29,
    UnsafeInput = 30,
    BlockedByRunnerEvaluation = 31,
    BlockedByDryRun = 32,
    BlockedByRouteSuggestion = 33,
    BlockedByImplementationProposal = 34,
    BlockedByHumanApprovalPackage = 35
}

public enum SourceApplyApprovalEvidenceKind
{
    Unknown = 0,
    ImplementationProposalPackageReference = 1,
    HumanApprovalPackageReference = 2,
    WorkflowStepEvaluationReference = 3,
    ApprovalHaltReference = 4,
    DryRunResultReference = 5,
    PolicyEvidenceReference = 6,
    ExternalArtifactReference = 7
}

public enum SourceApplyApprovalGateKind
{
    Unknown = 0,
    HumanApprovalRequired = 1,
    PolicyEvidenceRequired = 2,
    SourceMutationForbiddenUntilApproved = 3,
    PatchApplyForbiddenUntilApproved = 4,
    WorkflowContinuationForbiddenUntilApproved = 5,
    ApprovalRecordingRequiredLater = 6
}

public enum SourceApplyApprovalRiskKind
{
    Unknown = 0,
    MissingApproval = 1,
    MissingPolicyEvidence = 2,
    CandidatePackageConfusedWithApproval = 3,
    SourceMutationRisk = 4,
    PatchApplyRisk = 5,
    WorkflowContinuationRisk = 6,
    OverclaimRisk = 7
}

public enum SourceApplyApprovalSeverityHint
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}
