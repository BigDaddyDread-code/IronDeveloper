namespace IronDev.Core.Workflow;

public interface IImplementationProposalPackageCandidateWorkflow
{
    ImplementationProposalPackageCandidateResult Prepare(ImplementationProposalPackageCandidateRequest? request);
}

public sealed class ImplementationProposalPackageCandidateWorkflow : IImplementationProposalPackageCandidateWorkflow
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
        "raw log",
        "full log",
        "wholepatch",
        "whole patch",
        "entirepatch",
        "entire patch",
        "patchpayload",
        "patch payload",
        "patch ready",
        "patch is ready",
        "generate code",
        "code generated",
        "implementation complete",
        "implementation done",
        "edit file",
        "source content",
        "diff --git",
        "approval granted",
        "approval satisfied",
        "execution allowed",
        "policy satisfied",
        "workflow may continue",
        "workflow continued",
        "run tests",
        "run this command",
        "invoke tool",
        "tool executed",
        "dispatch agent",
        "call model",
        "build prompt",
        "create ticket",
        "ticket created",
        "source mutated",
        "apply patch",
        "patch applied",
        "promote memory",
        "memory promoted",
        "activate retrieval",
        "retrieval activated",
        "root cause confirmed",
        "root cause found",
        "release approved"
    ];

    public ImplementationProposalPackageCandidateResult Prepare(ImplementationProposalPackageCandidateRequest? request)
    {
        if (request is null)
            return Result(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                ImplementationProposalPackageCandidateStatus.InvalidRequest,
                ImplementationProposalTargetKind.Unknown,
                string.Empty,
                [ImplementationProposalPackageCandidateReason.Unknown],
                [],
                [],
                [],
                [],
                [],
                [],
                []);

        var workflowRunId = SafeId(request.WorkflowRunId);
        var workflowStepId = SafeId(request.WorkflowStepId);
        var proposalReferenceId = SafeId(request.ProposalReferenceId);
        var targetReferenceId = SafeId(request.TargetReferenceId);
        var invalidReasons = InvalidReasons(request).ToArray();

        if (invalidReasons.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                proposalReferenceId,
                targetReferenceId,
                ImplementationProposalPackageCandidateStatus.InvalidRequest,
                request.TargetKind,
                targetReferenceId,
                invalidReasons,
                [],
                [],
                [],
                [],
                [],
                [],
                []);

        var gateReasons = GateBlockReasons(request).ToArray();
        if (gateReasons.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                proposalReferenceId,
                targetReferenceId,
                ImplementationProposalPackageCandidateStatus.BlockedByWorkflowGate,
                request.TargetKind,
                targetReferenceId,
                gateReasons,
                [],
                [],
                [],
                [],
                [],
                ["Workflow gate snapshot blocked implementation proposal package production."],
                []);

        var missingEvidence = MissingEvidence(request).ToArray();
        if (missingEvidence.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                proposalReferenceId,
                targetReferenceId,
                ImplementationProposalPackageCandidateStatus.MissingRequiredProposalMaterial,
                request.TargetKind,
                targetReferenceId,
                [
                    ImplementationProposalPackageCandidateReason.ProposalOnly,
                    ImplementationProposalPackageCandidateReason.SuppliedEvidenceOnly,
                    ..MissingReasons(request)
                ],
                SafeEvidence(request),
                SafeAffectedAreas(request),
                SafeProposedSteps(request),
                SafeValidationSteps(request),
                SafeRisks(request),
                ["Implementation proposal package was not produced because required supplied proposal material is missing."],
                missingEvidence);

        return Result(
            workflowRunId,
            workflowStepId,
            proposalReferenceId,
            targetReferenceId,
            ImplementationProposalPackageCandidateStatus.ProposalPackageProduced,
            request.TargetKind,
            targetReferenceId,
            BoundaryReasons(),
            SafeEvidence(request),
            SafeAffectedAreas(request),
            SafeProposedSteps(request),
            SafeValidationSteps(request),
            SafeRisks(request),
            SummaryLines(request).ToArray(),
            []);
    }

    private static IEnumerable<ImplementationProposalPackageCandidateReason> InvalidReasons(ImplementationProposalPackageCandidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowRunId))
            yield return ImplementationProposalPackageCandidateReason.MissingWorkflowRunId;

        if (string.IsNullOrWhiteSpace(request.WorkflowStepId))
            yield return ImplementationProposalPackageCandidateReason.MissingWorkflowStepId;

        if (string.IsNullOrWhiteSpace(request.ProposalReferenceId))
            yield return ImplementationProposalPackageCandidateReason.MissingProposalReference;

        if (request.TargetKind == ImplementationProposalTargetKind.Unknown)
            yield return ImplementationProposalPackageCandidateReason.InvalidTargetKind;

        if (string.IsNullOrWhiteSpace(request.TargetReferenceId))
            yield return ImplementationProposalPackageCandidateReason.MissingTargetReference;

        if (ContainsUnsafeInput(request))
            yield return ImplementationProposalPackageCandidateReason.UnsafeInput;
    }

    private static IEnumerable<ImplementationProposalPackageCandidateReason> GateBlockReasons(ImplementationProposalPackageCandidateRequest request)
    {
        if (request.StepEvaluation is not null && request.StepEvaluation.Eligibility != WorkflowStepRunnerEligibility.EligibleForFutureExecution)
            yield return ImplementationProposalPackageCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.PolicyPreflightStatus is WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest or WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence)
            yield return ImplementationProposalPackageCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.A2aHandoffValidationStatus is WorkflowA2aHandoffValidationStatus.InvalidRequest or WorkflowA2aHandoffValidationStatus.InvalidStepContract or WorkflowA2aHandoffValidationStatus.InvalidHandoffReference or WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence)
            yield return ImplementationProposalPackageCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.ApprovalHaltStatus == WorkflowApprovalHaltStatus.ApprovalRequiredHalt)
            yield return ImplementationProposalPackageCandidateReason.BlockedByRunnerEvaluation;

        if (request.DryRunResult is not null && request.DryRunResult.Status != WorkflowDryRunStatus.DryRunCompleted)
            yield return ImplementationProposalPackageCandidateReason.BlockedByDryRun;

        if (request.RouteSuggestion is not null && RouteSuggestionBlocks(request.RouteSuggestion))
            yield return ImplementationProposalPackageCandidateReason.BlockedByRouteSuggestion;

        if (request.TestFailureReview is not null && request.TestFailureReview.Status != TestFailureReviewCandidateStatus.ReviewMaterialProduced)
            yield return ImplementationProposalPackageCandidateReason.BlockedByTestFailureReview;

        if (request.CriticReviewRequest is not null && request.CriticReviewRequest.Status != CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced)
            yield return ImplementationProposalPackageCandidateReason.BlockedByCriticReviewRequest;
    }

    private static bool RouteSuggestionBlocks(BoxedLangGraphRouteSuggestion route) =>
        route.Label is BoxedLangGraphRouteLabel.InvalidRoutingSnapshot or
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

    private static IEnumerable<string> MissingEvidence(ImplementationProposalPackageCandidateRequest request)
    {
        if (request.EvidenceReferences.Count == 0)
            yield return "evidence reference";

        if (request.AffectedAreas.Count == 0)
            yield return "affected area reference";

        if (request.ProposedSteps.Count == 0)
            yield return "proposed implementation step";

        if (request.ValidationSteps.Count == 0)
            yield return "proposed validation step";

        if (request.TargetKind == ImplementationProposalTargetKind.TestFailureReviewCandidate && request.TestFailureReview is null)
            yield return "test failure review candidate result";

        if (request.TargetKind == ImplementationProposalTargetKind.CriticReviewRequest && request.CriticReviewRequest is null)
            yield return "critic review request candidate result";
    }

    private static IEnumerable<ImplementationProposalPackageCandidateReason> MissingReasons(ImplementationProposalPackageCandidateRequest request)
    {
        if (request.EvidenceReferences.Count == 0)
            yield return ImplementationProposalPackageCandidateReason.MissingEvidenceReference;

        if (request.AffectedAreas.Count == 0)
            yield return ImplementationProposalPackageCandidateReason.MissingAffectedArea;

        if (request.ProposedSteps.Count == 0)
            yield return ImplementationProposalPackageCandidateReason.MissingProposalStep;

        if (request.ValidationSteps.Count == 0)
            yield return ImplementationProposalPackageCandidateReason.MissingValidationStep;
    }

    private static IReadOnlyList<ImplementationProposalEvidenceReference> SafeEvidence(ImplementationProposalPackageCandidateRequest request)
    {
        var evidence = request.EvidenceReferences
            .Where(reference => reference.Kind != ImplementationProposalEvidenceKind.Unknown)
            .Where(reference => !ContainsUnsafeMarker(reference.ReferenceId) && !ContainsUnsafeMarker(reference.SafeSummary))
            .Select(reference => reference with { ReferenceId = SafeText(reference.ReferenceId), SafeSummary = SafeNullableText(reference.SafeSummary) })
            .ToList();

        if (request.TestFailureReview is not null && !string.IsNullOrWhiteSpace(request.TestFailureReview.ReviewPackageReferenceId))
        {
            evidence.Add(new ImplementationProposalEvidenceReference
            {
                Kind = ImplementationProposalEvidenceKind.TestFailureReviewReference,
                ReferenceId = request.TestFailureReview.ReviewPackageReferenceId,
                SafeSummary = "Supplied test failure review candidate result."
            });
        }

        if (request.CriticReviewRequest is not null && !string.IsNullOrWhiteSpace(request.CriticReviewRequest.ReviewPackageReferenceId))
        {
            evidence.Add(new ImplementationProposalEvidenceReference
            {
                Kind = ImplementationProposalEvidenceKind.CriticReviewRequestReference,
                ReferenceId = request.CriticReviewRequest.ReviewPackageReferenceId,
                SafeSummary = "Supplied critic review request candidate result."
            });
        }

        return evidence
            .Distinct()
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ImplementationAffectedAreaReference> SafeAffectedAreas(ImplementationProposalPackageCandidateRequest request) =>
        request.AffectedAreas
            .Where(area => area.Kind != ImplementationAffectedAreaKind.Unknown)
            .Where(area => !ContainsUnsafeMarker(area.ReferenceId) && !ContainsUnsafeMarker(area.SafeSummary))
            .Select(area => area with { ReferenceId = SafeText(area.ReferenceId), SafeSummary = SafeNullableText(area.SafeSummary) })
            .Distinct()
            .OrderBy(area => area.Kind)
            .ThenBy(area => area.ReferenceId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<ImplementationProposalStep> SafeProposedSteps(ImplementationProposalPackageCandidateRequest request) =>
        request.ProposedSteps
            .Where(step => step.Kind != ImplementationProposalStepKind.Unknown)
            .Where(step => !ContainsUnsafeMarker(step.SafeSummary))
            .Select(step => step with { SafeSummary = SafeText(step.SafeSummary) })
            .Distinct()
            .OrderBy(step => step.Order)
            .ThenBy(step => step.Kind)
            .ThenBy(step => step.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<ImplementationValidationStep> SafeValidationSteps(ImplementationProposalPackageCandidateRequest request) =>
        request.ValidationSteps
            .Where(step => step.Kind != ImplementationValidationStepKind.Unknown)
            .Where(step => !ContainsUnsafeMarker(step.SafeSummary))
            .Select(step => step with { SafeSummary = SafeText(step.SafeSummary) })
            .Distinct()
            .OrderBy(step => step.Order)
            .ThenBy(step => step.Kind)
            .ThenBy(step => step.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<ImplementationProposalRisk> SafeRisks(ImplementationProposalPackageCandidateRequest request) =>
        request.Risks
            .Where(risk => risk.Kind != ImplementationProposalRiskKind.Unknown)
            .Where(risk => !ContainsUnsafeMarker(risk.SafeSummary))
            .Select(risk => risk with { SafeSummary = SafeNullableText(risk.SafeSummary) })
            .Distinct()
            .OrderBy(risk => risk.Kind)
            .ThenByDescending(risk => risk.SeverityHint)
            .ThenBy(risk => risk.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> SummaryLines(ImplementationProposalPackageCandidateRequest request)
    {
        yield return "Implementation proposal package was produced from supplied evidence only.";
        yield return "No implementation was performed.";
        yield return "No code was generated.";
        yield return "No patch was generated.";
        yield return "No source mutation was performed.";
        yield return "No tests were run.";
        yield return "No tools were invoked.";
        yield return "Proposal steps are advisory request material.";
        yield return $"Proposal target kind: {request.TargetKind}.";
        yield return $"Proposal target reference: {SafeText(request.TargetReferenceId)}.";

        if (!string.IsNullOrWhiteSpace(request.SafeTitle))
            yield return $"Safe title: {SafeText(request.SafeTitle)}.";

        if (!string.IsNullOrWhiteSpace(request.SafeSummary))
            yield return $"Safe summary: {SafeText(request.SafeSummary)}.";
    }

    private static ImplementationProposalPackageCandidateReason[] BoundaryReasons() =>
    [
        ImplementationProposalPackageCandidateReason.ProposalOnly,
        ImplementationProposalPackageCandidateReason.SuppliedEvidenceOnly,
        ImplementationProposalPackageCandidateReason.NoImplementationPerformed,
        ImplementationProposalPackageCandidateReason.NoCodeGenerated,
        ImplementationProposalPackageCandidateReason.NoPatchGenerated,
        ImplementationProposalPackageCandidateReason.NoMutationPerformed,
        ImplementationProposalPackageCandidateReason.CannotApplyPatch,
        ImplementationProposalPackageCandidateReason.CannotRunTests,
        ImplementationProposalPackageCandidateReason.CannotDispatchAgents,
        ImplementationProposalPackageCandidateReason.CannotInvokeTools,
        ImplementationProposalPackageCandidateReason.CannotCallModels,
        ImplementationProposalPackageCandidateReason.CannotBuildPrompts,
        ImplementationProposalPackageCandidateReason.CannotCreateTicket,
        ImplementationProposalPackageCandidateReason.CannotSatisfyApproval,
        ImplementationProposalPackageCandidateReason.CannotSatisfyPolicy,
        ImplementationProposalPackageCandidateReason.CannotTransitionWorkflow,
        ImplementationProposalPackageCandidateReason.CannotPromoteMemory,
        ImplementationProposalPackageCandidateReason.CannotActivateRetrieval
    ];

    private static ImplementationProposalPackageCandidateResult Result(
        string workflowRunId,
        string workflowStepId,
        string proposalReferenceId,
        string packageSeed,
        ImplementationProposalPackageCandidateStatus status,
        ImplementationProposalTargetKind targetKind,
        string targetReferenceId,
        IReadOnlyList<ImplementationProposalPackageCandidateReason> reasons,
        IReadOnlyList<ImplementationProposalEvidenceReference> evidenceReferences,
        IReadOnlyList<ImplementationAffectedAreaReference> affectedAreas,
        IReadOnlyList<ImplementationProposalStep> proposedSteps,
        IReadOnlyList<ImplementationValidationStep> validationSteps,
        IReadOnlyList<ImplementationProposalRisk> risks,
        IReadOnlyList<string> safePackageSummaryLines,
        IReadOnlyList<string> missingEvidence) =>
        new()
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            ProposalReferenceId = proposalReferenceId,
            ProposalPackageReferenceId = ProposalPackageReferenceId(workflowRunId, workflowStepId, proposalReferenceId, packageSeed),
            Status = status,
            TargetKind = targetKind,
            TargetReferenceId = targetReferenceId,
            Reasons = reasons.Concat(BoundaryReasons()).Distinct().OrderBy(reason => reason).ToArray(),
            EvidenceReferences = evidenceReferences,
            AffectedAreas = affectedAreas,
            ProposedSteps = proposedSteps,
            ValidationSteps = validationSteps,
            Risks = risks,
            MissingEvidence = missingEvidence.Where(value => !ContainsUnsafeMarker(value)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            SafePackageSummaryLines = safePackageSummaryLines.Where(value => !ContainsUnsafeMarker(value)).Select(SafeText).ToArray(),
            IsProposalOnly = true,
            IsImplementation = false,
            IsPatch = false,
            CanMutateSource = false,
            CanApplyPatch = false,
            CanGenerateCode = false,
            CanRunTests = false,
            CanDispatchAgent = false,
            CanInvokeTool = false,
            CanCallModel = false,
            CanBuildPrompt = false,
            CanCreateTicket = false,
            CanSatisfyApproval = false,
            CanSatisfyPolicy = false,
            CanTransitionWorkflow = false,
            CanPromoteMemory = false,
            CanActivateRetrieval = false
        };

    private static string ProposalPackageReferenceId(string workflowRunId, string workflowStepId, string proposalReferenceId, string packageSeed) =>
        string.IsNullOrWhiteSpace(workflowRunId) || string.IsNullOrWhiteSpace(workflowStepId) || string.IsNullOrWhiteSpace(proposalReferenceId) || string.IsNullOrWhiteSpace(packageSeed)
            ? string.Empty
            : $"implementation-proposal-package:{workflowRunId}:{workflowStepId}:{proposalReferenceId}:{packageSeed}";

    private static bool ContainsUnsafeInput(ImplementationProposalPackageCandidateRequest request) =>
        ContainsUnsafeMarker(request.WorkflowRunId) ||
        ContainsUnsafeMarker(request.WorkflowStepId) ||
        ContainsUnsafeMarker(request.ProposalReferenceId) ||
        ContainsUnsafeMarker(request.TargetReferenceId) ||
        ContainsUnsafeMarker(request.SafeTitle) ||
        ContainsUnsafeMarker(request.SafeSummary) ||
        ContainsUnsafeMarker(request.CorrelationId) ||
        request.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
        request.AffectedAreas.Any(area => ContainsUnsafeMarker(area.ReferenceId) || ContainsUnsafeMarker(area.SafeSummary)) ||
        request.ProposedSteps.Any(step => ContainsUnsafeMarker(step.SafeSummary)) ||
        request.ValidationSteps.Any(step => ContainsUnsafeMarker(step.SafeSummary)) ||
        request.Risks.Any(risk => ContainsUnsafeMarker(risk.SafeSummary)) ||
        ContainsUnsafeTestFailureReview(request.TestFailureReview) ||
        ContainsUnsafeCriticReviewRequest(request.CriticReviewRequest) ||
        ContainsUnsafeMarker(request.StepEvaluation?.StepId) ||
        ContainsUnsafeMarker(request.DryRunResult?.WorkflowRunId) ||
        ContainsUnsafeMarker(request.DryRunResult?.WorkflowStepId) ||
        ContainsUnsafeMarker(request.RouteSuggestion?.WorkflowRunId) ||
        ContainsUnsafeMarker(request.RouteSuggestion?.WorkflowStepId) ||
        (request.RouteSuggestion?.SafeReportLines.Any(ContainsUnsafeMarker) ?? false) ||
        (request.DryRunResult?.SafeReportLines.Any(ContainsUnsafeMarker) ?? false);

    private static bool ContainsUnsafeTestFailureReview(TestFailureReviewCandidateResult? result) =>
        result is not null &&
        (ContainsUnsafeMarker(result.WorkflowRunId) ||
            ContainsUnsafeMarker(result.WorkflowStepId) ||
            ContainsUnsafeMarker(result.TestRunReferenceId) ||
            ContainsUnsafeMarker(result.ReviewPackageReferenceId) ||
            result.AffectedTests.Any(ContainsUnsafeMarker) ||
            result.SafeSummaryLines.Any(ContainsUnsafeMarker) ||
            result.MissingEvidence.Any(ContainsUnsafeMarker) ||
            result.SafeNextReviewSuggestions.Any(ContainsUnsafeMarker));

    private static bool ContainsUnsafeCriticReviewRequest(CriticReviewRequestCandidateResult? result) =>
        result is not null &&
        (ContainsUnsafeMarker(result.WorkflowRunId) ||
            ContainsUnsafeMarker(result.WorkflowStepId) ||
            ContainsUnsafeMarker(result.ReviewRequestReferenceId) ||
            ContainsUnsafeMarker(result.ReviewPackageReferenceId) ||
            ContainsUnsafeMarker(result.TargetReferenceId) ||
            result.ReviewQuestions.Any(question => ContainsUnsafeMarker(question.SafeQuestion)) ||
            result.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.RiskHints.Any(risk => ContainsUnsafeMarker(risk.SafeSummary)) ||
            result.MissingEvidence.Any(ContainsUnsafeMarker) ||
            result.SafePackageSummaryLines.Any(ContainsUnsafeMarker));

    private static string SafeId(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? string.Empty : value.Trim();

    private static string SafeText(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? string.Empty : value.Trim();

    private static string? SafeNullableText(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? null : value.Trim();

    private static bool ContainsUnsafeMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}

public sealed record ImplementationProposalPackageCandidateRequest
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string ProposalReferenceId { get; init; }
    public required ImplementationProposalTargetKind TargetKind { get; init; }
    public required string TargetReferenceId { get; init; }
    public string? SafeTitle { get; init; }
    public string? SafeSummary { get; init; }
    public IReadOnlyList<ImplementationProposalEvidenceReference> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<ImplementationAffectedAreaReference> AffectedAreas { get; init; } = [];
    public IReadOnlyList<ImplementationProposalStep> ProposedSteps { get; init; } = [];
    public IReadOnlyList<ImplementationValidationStep> ValidationSteps { get; init; } = [];
    public IReadOnlyList<ImplementationProposalRisk> Risks { get; init; } = [];
    public TestFailureReviewCandidateResult? TestFailureReview { get; init; }
    public CriticReviewRequestCandidateResult? CriticReviewRequest { get; init; }
    public WorkflowStepRunnerEvaluation? StepEvaluation { get; init; }
    public WorkflowDryRunResult? DryRunResult { get; init; }
    public BoxedLangGraphRouteSuggestion? RouteSuggestion { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record ImplementationProposalEvidenceReference
{
    public required ImplementationProposalEvidenceKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record ImplementationAffectedAreaReference
{
    public required ImplementationAffectedAreaKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record ImplementationProposalStep
{
    public required int Order { get; init; }
    public required ImplementationProposalStepKind Kind { get; init; }
    public required string SafeSummary { get; init; }
}

public sealed record ImplementationValidationStep
{
    public required int Order { get; init; }
    public required ImplementationValidationStepKind Kind { get; init; }
    public required string SafeSummary { get; init; }
}

public sealed record ImplementationProposalRisk
{
    public required ImplementationProposalRiskKind Kind { get; init; }
    public ImplementationProposalSeverityHint SeverityHint { get; init; } = ImplementationProposalSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record ImplementationProposalPackageCandidateResult
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string ProposalReferenceId { get; init; }
    public required string ProposalPackageReferenceId { get; init; }
    public required ImplementationProposalPackageCandidateStatus Status { get; init; }
    public required ImplementationProposalTargetKind TargetKind { get; init; }
    public required string TargetReferenceId { get; init; }
    public required IReadOnlyList<ImplementationProposalPackageCandidateReason> Reasons { get; init; }
    public required IReadOnlyList<ImplementationProposalEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<ImplementationAffectedAreaReference> AffectedAreas { get; init; }
    public required IReadOnlyList<ImplementationProposalStep> ProposedSteps { get; init; }
    public required IReadOnlyList<ImplementationValidationStep> ValidationSteps { get; init; }
    public required IReadOnlyList<ImplementationProposalRisk> Risks { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required IReadOnlyList<string> SafePackageSummaryLines { get; init; }
    public required bool IsProposalOnly { get; init; }
    public required bool IsImplementation { get; init; }
    public required bool IsPatch { get; init; }
    public required bool CanMutateSource { get; init; }
    public required bool CanApplyPatch { get; init; }
    public required bool CanGenerateCode { get; init; }
    public required bool CanRunTests { get; init; }
    public required bool CanDispatchAgent { get; init; }
    public required bool CanInvokeTool { get; init; }
    public required bool CanCallModel { get; init; }
    public required bool CanBuildPrompt { get; init; }
    public required bool CanCreateTicket { get; init; }
    public required bool CanSatisfyApproval { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanTransitionWorkflow { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanActivateRetrieval { get; init; }
}

public enum ImplementationProposalTargetKind
{
    Unknown = 0,
    TestFailureReviewCandidate = 1,
    CriticReviewRequest = 2,
    ToolRequestPreview = 3,
    MemoryProposalReview = 4,
    ApprovalPackageReview = 5,
    DogfoodEvidenceBundle = 6,
    FailurePatternReview = 7
}

public enum ImplementationProposalEvidenceKind
{
    Unknown = 0,
    GovernanceEventReference = 1,
    WorkflowStepEvaluationReference = 2,
    DryRunResultReference = 3,
    TestFailureReviewReference = 4,
    CriticReviewRequestReference = 5,
    A2aValidationReference = 6,
    ApprovalHaltReference = 7,
    ExternalArtifactReference = 8
}

public enum ImplementationAffectedAreaKind
{
    Unknown = 0,
    ProjectArea = 1,
    ComponentReference = 2,
    TestSuiteReference = 3,
    SourceFileReference = 4,
    DocumentationReference = 5,
    ConfigurationReference = 6
}

public enum ImplementationProposalStepKind
{
    Unknown = 0,
    InspectSuppliedEvidence = 1,
    ReviewAffectedArea = 2,
    PlanMinimalCodeChange = 3,
    PlanTestRevision = 4,
    PlanDocumentationRevision = 5,
    RequestMoreEvidence = 6
}

public enum ImplementationValidationStepKind
{
    Unknown = 0,
    ReviewExistingTestEvidence = 1,
    ProposeFocusedTestRunLater = 2,
    ProposeBuildValidationLater = 3,
    ProposeManualReviewLater = 4,
    RequestMissingValidationEvidence = 5
}

public enum ImplementationProposalRiskKind
{
    Unknown = 0,
    InsufficientEvidence = 1,
    SourceMutationRisk = 2,
    TestCoverageRisk = 3,
    RegressionRisk = 4,
    ArchitectureRisk = 5,
    BoundaryRisk = 6,
    ApprovalRequired = 7,
    PolicyRequired = 8,
    OverclaimRisk = 9
}

public enum ImplementationProposalSeverityHint
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum ImplementationProposalPackageCandidateStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    BlockedByWorkflowGate = 2,
    MissingRequiredProposalMaterial = 3,
    ProposalPackageProduced = 4
}

public enum ImplementationProposalPackageCandidateReason
{
    Unknown = 0,
    ProposalOnly = 1,
    SuppliedEvidenceOnly = 2,
    MissingWorkflowRunId = 3,
    MissingWorkflowStepId = 4,
    MissingProposalReference = 5,
    MissingTargetReference = 6,
    InvalidTargetKind = 7,
    MissingEvidenceReference = 8,
    MissingAffectedArea = 9,
    MissingProposalStep = 10,
    MissingValidationStep = 11,
    UnsafeInput = 12,
    BlockedByRunnerEvaluation = 13,
    BlockedByDryRun = 14,
    BlockedByRouteSuggestion = 15,
    BlockedByCriticReviewRequest = 16,
    BlockedByTestFailureReview = 17,
    NoImplementationPerformed = 18,
    NoCodeGenerated = 19,
    NoPatchGenerated = 20,
    NoMutationPerformed = 21,
    CannotApplyPatch = 22,
    CannotRunTests = 23,
    CannotDispatchAgents = 24,
    CannotInvokeTools = 25,
    CannotCallModels = 26,
    CannotBuildPrompts = 27,
    CannotCreateTicket = 28,
    CannotSatisfyApproval = 29,
    CannotSatisfyPolicy = 30,
    CannotTransitionWorkflow = 31,
    CannotPromoteMemory = 32,
    CannotActivateRetrieval = 33
}
