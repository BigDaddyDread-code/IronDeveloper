namespace IronDev.Core.Workflow;

public interface IPatchProposalEvidencePackageWorkflow
{
    PatchProposalEvidencePackageResult Prepare(PatchProposalEvidencePackageRequest? request);
}

public sealed class PatchProposalEvidencePackageWorkflow : IPatchProposalEvidencePackageWorkflow
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
        "patchpayload",
        "patch payload",
        "diffpayload",
        "diff payload",
        "source content",
        "source file contents",
        "wholepatch",
        "whole patch",
        "entirepatch",
        "entire patch",
        "patch applied",
        "source applied",
        "approved",
        "approval granted",
        "approval satisfied",
        "policy satisfied",
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

    public PatchProposalEvidencePackageResult Prepare(PatchProposalEvidencePackageRequest? request)
    {
        if (request is null)
            return Result(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                PatchProposalEvidencePackageStatus.InvalidRequest,
                PatchProposalTargetKind.Unknown,
                [PatchProposalEvidencePackageReason.MissingWorkflowRunId],
                [],
                [],
                [],
                [],
                [],
                ["request"]);

        var workflowRunId = SafeText(request.WorkflowRunId);
        var workflowStepId = SafeText(request.WorkflowStepId);
        var packageReferenceSeed = SafeText(request.PatchProposalEvidencePackageReferenceId);
        var projectReferenceId = SafeText(request.ProjectReferenceId);
        var targetReferenceId = SafeText(request.TargetReferenceId);

        var safeAffectedAreas = SafeAffectedAreas(request.AffectedAreas);
        var safeExpectedValidations = SafeExpectedValidations(request.ExpectedValidationReferences);
        var safeEvidence = SafeEvidence(request.EvidenceReferences);
        var safeGates = SafeGateHints(request.GateHints);
        var safeRisks = SafeRisks(request.Risks);

        var invalidReasons = InvalidReasons(request).ToArray();
        if (invalidReasons.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                packageReferenceSeed,
                string.Empty,
                projectReferenceId,
                targetReferenceId,
                PatchProposalEvidencePackageStatus.InvalidRequest,
                request.TargetKind,
                invalidReasons,
                safeAffectedAreas,
                safeExpectedValidations,
                safeEvidence,
                safeGates,
                safeRisks,
                MissingForInvalid(invalidReasons));

        var gateReasons = GateBlockReasons(request).ToArray();
        if (gateReasons.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                packageReferenceSeed,
                PackageReferenceId(workflowRunId, workflowStepId, packageReferenceSeed, targetReferenceId),
                projectReferenceId,
                targetReferenceId,
                PatchProposalEvidencePackageStatus.BlockedByWorkflowGate,
                request.TargetKind,
                gateReasons,
                safeAffectedAreas,
                safeExpectedValidations,
                safeEvidence,
                safeGates,
                safeRisks,
                ["non-blocking supplied workflow gate material"]);

        var missingEvidence = MissingEvidence(request).ToArray();
        if (missingEvidence.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                packageReferenceSeed,
                PackageReferenceId(workflowRunId, workflowStepId, packageReferenceSeed, targetReferenceId),
                projectReferenceId,
                targetReferenceId,
                PatchProposalEvidencePackageStatus.MissingRequiredPatchProposalEvidence,
                request.TargetKind,
                MissingReasons(request),
                safeAffectedAreas,
                safeExpectedValidations,
                safeEvidence,
                safeGates,
                safeRisks,
                missingEvidence);

        return Result(
            workflowRunId,
            workflowStepId,
            packageReferenceSeed,
            PackageReferenceId(workflowRunId, workflowStepId, packageReferenceSeed, targetReferenceId),
            projectReferenceId,
            targetReferenceId,
            PatchProposalEvidencePackageStatus.PatchProposalEvidencePackageProduced,
            request.TargetKind,
            BoundaryReasons(),
            safeAffectedAreas,
            safeExpectedValidations,
            safeEvidence,
            safeGates,
            safeRisks,
            []);
    }

    private static IEnumerable<PatchProposalEvidencePackageReason> InvalidReasons(PatchProposalEvidencePackageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowRunId))
            yield return PatchProposalEvidencePackageReason.MissingWorkflowRunId;

        if (string.IsNullOrWhiteSpace(request.WorkflowStepId))
            yield return PatchProposalEvidencePackageReason.MissingWorkflowStepId;

        if (string.IsNullOrWhiteSpace(request.PatchProposalEvidencePackageReferenceId))
            yield return PatchProposalEvidencePackageReason.MissingPackageReference;

        if (string.IsNullOrWhiteSpace(request.ProjectReferenceId))
            yield return PatchProposalEvidencePackageReason.MissingProjectReference;

        if (string.IsNullOrWhiteSpace(request.TargetReferenceId))
            yield return PatchProposalEvidencePackageReason.MissingTargetReference;

        if (request.TargetKind == PatchProposalTargetKind.Unknown)
            yield return PatchProposalEvidencePackageReason.InvalidTargetKind;

        if (ContainsUnsafeInput(request) || HasMalformedReferences(request))
            yield return PatchProposalEvidencePackageReason.UnsafeInput;
    }

    private static bool HasMalformedReferences(PatchProposalEvidencePackageRequest request) =>
        request.AffectedAreas.Any(area => area.Kind == PatchProposalAffectedAreaKind.Unknown || string.IsNullOrWhiteSpace(area.ReferenceId)) ||
        request.ExpectedValidationReferences.Any(reference => reference.Kind == PatchProposalExpectedValidationKind.Unknown || string.IsNullOrWhiteSpace(reference.ReferenceId)) ||
        request.EvidenceReferences.Any(reference => reference.Kind == PatchProposalEvidenceKind.Unknown || string.IsNullOrWhiteSpace(reference.ReferenceId)) ||
        request.GateHints.Any(hint => hint.Kind == PatchProposalGateKind.Unknown) ||
        request.Risks.Any(risk => risk.Kind == PatchProposalRiskKind.Unknown);

    private static IEnumerable<PatchProposalEvidencePackageReason> GateBlockReasons(PatchProposalEvidencePackageRequest request)
    {
        if (request.StepEvaluation is not null && request.StepEvaluation.Eligibility != WorkflowStepRunnerEligibility.EligibleForFutureExecution)
            yield return PatchProposalEvidencePackageReason.BlockedByRunnerEvaluation;

        if (request.DryRunResult is not null && request.DryRunResult.Status != WorkflowDryRunStatus.DryRunCompleted)
            yield return PatchProposalEvidencePackageReason.BlockedByDryRun;

        if (request.RouteSuggestion is not null && RouteBlocks(request.RouteSuggestion))
            yield return PatchProposalEvidencePackageReason.BlockedByRouteSuggestion;

        if (request.ImplementationProposal is not null && request.ImplementationProposal.Status != ImplementationProposalPackageCandidateStatus.ProposalPackageProduced)
            yield return PatchProposalEvidencePackageReason.BlockedByImplementationProposal;

        if (request.SourceApplyApprovalRequirement is not null && request.SourceApplyApprovalRequirement.Status != SourceApplyApprovalRequirementStatus.ApprovalRequired)
            yield return PatchProposalEvidencePackageReason.BlockedBySourceApplyApprovalRequirement;

        if (request.HumanApprovalPackage is not null && request.HumanApprovalPackage.Status != HumanApprovalPackageCandidateStatus.ApprovalPackageProduced)
            yield return PatchProposalEvidencePackageReason.BlockedByHumanApprovalPackage;
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
        RouteFlag(route, "Policy", "SatisfactionAllowed") ||
        route.SourceChangeAllowed ||
        RouteFlag(route, "Memory", "PromotionAllowed") ||
        RouteFlag(route, "Retrieval", "ActivationAllowed");

    private static bool RouteFlag(BoxedLangGraphRouteSuggestion route, string prefix, string suffix) =>
        route.GetType().GetProperty(prefix + suffix)?.GetValue(route) is true;

    private static IEnumerable<string> MissingEvidence(PatchProposalEvidencePackageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SafeChangeIntentSummary))
            yield return "safe change intent summary";

        if (request.ImplementationProposal is null)
            yield return "implementation proposal package";

        if (request.SourceApplyApprovalRequirement is null)
            yield return "source apply approval requirement";

        if (request.AffectedAreas.Count == 0)
            yield return "affected area reference";

        if (request.ExpectedValidationReferences.Count == 0)
            yield return "expected validation reference";

        if (request.EvidenceReferences.Count == 0)
            yield return "evidence reference";

        if (request.GateHints.Count == 0)
            yield return "source apply gate hint";
    }

    private static PatchProposalEvidencePackageReason[] MissingReasons(PatchProposalEvidencePackageRequest request)
    {
        var reasons = new List<PatchProposalEvidencePackageReason>();

        if (string.IsNullOrWhiteSpace(request.SafeChangeIntentSummary))
            reasons.Add(PatchProposalEvidencePackageReason.MissingChangeIntentSummary);

        if (request.ImplementationProposal is null)
            reasons.Add(PatchProposalEvidencePackageReason.MissingImplementationProposal);

        if (request.SourceApplyApprovalRequirement is null)
            reasons.Add(PatchProposalEvidencePackageReason.MissingSourceApplyApprovalRequirement);

        if (request.AffectedAreas.Count == 0)
            reasons.Add(PatchProposalEvidencePackageReason.MissingAffectedAreaReference);

        if (request.ExpectedValidationReferences.Count == 0)
            reasons.Add(PatchProposalEvidencePackageReason.MissingExpectedValidationReference);

        if (request.EvidenceReferences.Count == 0)
            reasons.Add(PatchProposalEvidencePackageReason.MissingEvidenceReference);

        if (request.GateHints.Count == 0)
            reasons.Add(PatchProposalEvidencePackageReason.MissingGateHint);

        return reasons.Distinct().OrderBy(reason => reason).ToArray();
    }

    private static PatchProposalEvidencePackageResult Result(
        string workflowRunId,
        string workflowStepId,
        string packageReferenceSeed,
        string packageReferenceId,
        string projectReferenceId,
        string targetReferenceId,
        PatchProposalEvidencePackageStatus status,
        PatchProposalTargetKind targetKind,
        IReadOnlyList<PatchProposalEvidencePackageReason> reasons,
        IReadOnlyList<PatchProposalAffectedAreaReference> affectedAreas,
        IReadOnlyList<PatchProposalExpectedValidationReference> expectedValidationReferences,
        IReadOnlyList<PatchProposalEvidenceReference> evidenceReferences,
        IReadOnlyList<PatchProposalGateHint> gateHints,
        IReadOnlyList<PatchProposalRisk> risks,
        IReadOnlyList<string> missingEvidence) =>
        new()
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            PatchProposalEvidencePackageReferenceId = packageReferenceSeed,
            PackageReferenceId = packageReferenceId,
            ProjectReferenceId = projectReferenceId,
            TargetReferenceId = targetReferenceId,
            Status = status,
            TargetKind = targetKind,
            Reasons = reasons.Concat(BoundaryReasons()).Distinct().OrderBy(reason => reason).ToArray(),
            AffectedAreas = affectedAreas,
            ExpectedValidationReferences = expectedValidationReferences,
            EvidenceReferences = evidenceReferences,
            GateHints = gateHints,
            Risks = risks,
            MissingEvidence = missingEvidence.Where(value => !ContainsUnsafeMarker(value)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            SafePackageSummaryLines = SummaryLines(status).ToArray(),
            IsPackageOnly = true,
            IsPatch = false,
            IsDiff = false,
            IsSourceApply = false,
            IsImplementation = false,
            CanGeneratePatch = false,
            CanApplyPatch = false,
            CanApplySource = false,
            CanMutateFiles = false,
            CanReadSourceFiles = false,
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

    private static IEnumerable<string> SummaryLines(PatchProposalEvidencePackageStatus status)
    {
        yield return "Patch proposal evidence package was produced from supplied references only.";
        yield return "Implementation proposal package is review material only.";
        yield return "Source apply approval requirement is requirement material only.";
        yield return "Human approval package is review material only.";
        yield return "Patch was not generated.";
        yield return "Diff was not generated.";
        yield return "Source was not applied.";
        yield return "Patch was not applied.";
        yield return "Files were not mutated.";
        yield return "Source files were not read.";
        yield return "Approval was not satisfied.";
        yield return "Policy was not satisfied.";
        yield return "Workflow was not transitioned.";
        yield return $"Package status: {status}.";
    }

    private static PatchProposalEvidencePackageReason[] BoundaryReasons() =>
    [
        PatchProposalEvidencePackageReason.PackageOnly,
        PatchProposalEvidencePackageReason.SuppliedEvidenceOnly,
        PatchProposalEvidencePackageReason.PatchNotGenerated,
        PatchProposalEvidencePackageReason.DiffNotGenerated,
        PatchProposalEvidencePackageReason.SourceApplyNotImplemented,
        PatchProposalEvidencePackageReason.SourceNotApplied,
        PatchProposalEvidencePackageReason.PatchNotApplied,
        PatchProposalEvidencePackageReason.FilesNotMutated,
        PatchProposalEvidencePackageReason.SourceFilesNotRead,
        PatchProposalEvidencePackageReason.CommandNotRun,
        PatchProposalEvidencePackageReason.ToolNotInvoked,
        PatchProposalEvidencePackageReason.AgentNotDispatched,
        PatchProposalEvidencePackageReason.ModelNotCalled,
        PatchProposalEvidencePackageReason.PromptNotBuilt,
        PatchProposalEvidencePackageReason.ApprovalNotSatisfied,
        PatchProposalEvidencePackageReason.PolicyNotSatisfied,
        PatchProposalEvidencePackageReason.WorkflowNotTransitioned,
        PatchProposalEvidencePackageReason.TicketNotCreated,
        PatchProposalEvidencePackageReason.MemoryNotPromoted,
        PatchProposalEvidencePackageReason.RetrievalNotActivated,
        PatchProposalEvidencePackageReason.SqlNotWritten
    ];

    private static IReadOnlyList<string> MissingForInvalid(IEnumerable<PatchProposalEvidencePackageReason> reasons) =>
        reasons.Select(reason => reason.ToString()).OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private static string PackageReferenceId(string workflowRunId, string workflowStepId, string packageSeed, string targetReferenceId) =>
        string.IsNullOrWhiteSpace(workflowRunId) ||
        string.IsNullOrWhiteSpace(workflowStepId) ||
        string.IsNullOrWhiteSpace(packageSeed) ||
        string.IsNullOrWhiteSpace(targetReferenceId)
            ? string.Empty
            : $"patch-proposal-evidence-package:{workflowRunId}:{workflowStepId}:{packageSeed}:{targetReferenceId}";

    private static IReadOnlyList<PatchProposalAffectedAreaReference> SafeAffectedAreas(IEnumerable<PatchProposalAffectedAreaReference> references) =>
        references
            .Where(reference => reference.Kind != PatchProposalAffectedAreaKind.Unknown)
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId))
            .Where(reference => !ContainsUnsafeMarker(reference.ReferenceId) && !ContainsUnsafeMarker(reference.SafeSummary))
            .Select(reference => reference with { ReferenceId = SafeText(reference.ReferenceId), SafeSummary = SafeNullableText(reference.SafeSummary) })
            .Distinct()
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<PatchProposalExpectedValidationReference> SafeExpectedValidations(IEnumerable<PatchProposalExpectedValidationReference> references) =>
        references
            .Where(reference => reference.Kind != PatchProposalExpectedValidationKind.Unknown)
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId))
            .Where(reference => !ContainsUnsafeMarker(reference.ReferenceId) && !ContainsUnsafeMarker(reference.SafeSummary))
            .Select(reference => reference with { ReferenceId = SafeText(reference.ReferenceId), SafeSummary = SafeNullableText(reference.SafeSummary) })
            .Distinct()
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<PatchProposalEvidenceReference> SafeEvidence(IEnumerable<PatchProposalEvidenceReference> references) =>
        references
            .Where(reference => reference.Kind != PatchProposalEvidenceKind.Unknown)
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId))
            .Where(reference => !ContainsUnsafeMarker(reference.ReferenceId) && !ContainsUnsafeMarker(reference.SafeSummary))
            .Select(reference => reference with { ReferenceId = SafeText(reference.ReferenceId), SafeSummary = SafeNullableText(reference.SafeSummary) })
            .Distinct()
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<PatchProposalGateHint> SafeGateHints(IEnumerable<PatchProposalGateHint> hints) =>
        hints
            .Where(hint => hint.Kind != PatchProposalGateKind.Unknown)
            .Where(hint => !ContainsUnsafeMarker(hint.SafeSummary))
            .Select(hint => hint with { SafeSummary = SafeNullableText(hint.SafeSummary) })
            .Distinct()
            .OrderBy(hint => hint.Kind)
            .ThenBy(hint => hint.SeverityHint)
            .ThenBy(hint => hint.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<PatchProposalRisk> SafeRisks(IEnumerable<PatchProposalRisk> risks) =>
        risks
            .Where(risk => risk.Kind != PatchProposalRiskKind.Unknown)
            .Where(risk => !ContainsUnsafeMarker(risk.SafeSummary))
            .Select(risk => risk with { SafeSummary = SafeNullableText(risk.SafeSummary) })
            .Distinct()
            .OrderBy(risk => risk.Kind)
            .ThenBy(risk => risk.SeverityHint)
            .ThenBy(risk => risk.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static bool ContainsUnsafeInput(PatchProposalEvidencePackageRequest request) =>
        ContainsUnsafeMarker(request.WorkflowRunId) ||
        ContainsUnsafeMarker(request.WorkflowStepId) ||
        ContainsUnsafeMarker(request.PatchProposalEvidencePackageReferenceId) ||
        ContainsUnsafeMarker(request.ProjectReferenceId) ||
        ContainsUnsafeMarker(request.TargetReferenceId) ||
        ContainsUnsafeMarker(request.SafeChangeIntentSummary) ||
        ContainsUnsafeMarker(request.CorrelationId) ||
        request.AffectedAreas.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
        request.ExpectedValidationReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
        request.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
        request.GateHints.Any(hint => ContainsUnsafeMarker(hint.SafeSummary)) ||
        request.Risks.Any(risk => ContainsUnsafeMarker(risk.SafeSummary)) ||
        ContainsUnsafeImplementationProposal(request.ImplementationProposal) ||
        ContainsUnsafeSourceApplyRequirement(request.SourceApplyApprovalRequirement) ||
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

    private static bool ContainsUnsafeSourceApplyRequirement(SourceApplyApprovalRequirementResult? result) =>
        result is not null &&
        (ContainsUnsafeMarker(result.WorkflowRunId) ||
            ContainsUnsafeMarker(result.WorkflowStepId) ||
            ContainsUnsafeMarker(result.SourceApplyRequestReferenceId) ||
            ContainsUnsafeMarker(result.RequirementReferenceId) ||
            ContainsUnsafeMarker(result.ProjectReferenceId) ||
            ContainsUnsafeMarker(result.TargetReferenceId) ||
            result.SafeSummaryLines.Any(ContainsUnsafeMarker) ||
            result.MissingRequirements.Any(ContainsUnsafeMarker) ||
            result.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.GateHints.Any(hint => ContainsUnsafeMarker(hint.SafeSummary)) ||
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
        !string.IsNullOrWhiteSpace(value) && UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}

public sealed record PatchProposalEvidencePackageRequest
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string PatchProposalEvidencePackageReferenceId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required string TargetReferenceId { get; init; }
    public required PatchProposalTargetKind TargetKind { get; init; }
    public string? SafeChangeIntentSummary { get; init; }
    public ImplementationProposalPackageCandidateResult? ImplementationProposal { get; init; }
    public SourceApplyApprovalRequirementResult? SourceApplyApprovalRequirement { get; init; }
    public HumanApprovalPackageCandidateResult? HumanApprovalPackage { get; init; }
    public IReadOnlyList<PatchProposalAffectedAreaReference> AffectedAreas { get; init; } = [];
    public IReadOnlyList<PatchProposalExpectedValidationReference> ExpectedValidationReferences { get; init; } = [];
    public IReadOnlyList<PatchProposalEvidenceReference> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<PatchProposalGateHint> GateHints { get; init; } = [];
    public IReadOnlyList<PatchProposalRisk> Risks { get; init; } = [];
    public WorkflowStepRunnerEvaluation? StepEvaluation { get; init; }
    public WorkflowDryRunResult? DryRunResult { get; init; }
    public BoxedLangGraphRouteSuggestion? RouteSuggestion { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record PatchProposalEvidencePackageResult
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string PatchProposalEvidencePackageReferenceId { get; init; }
    public required string PackageReferenceId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required string TargetReferenceId { get; init; }
    public required PatchProposalEvidencePackageStatus Status { get; init; }
    public required PatchProposalTargetKind TargetKind { get; init; }
    public required IReadOnlyList<PatchProposalEvidencePackageReason> Reasons { get; init; }
    public required IReadOnlyList<PatchProposalAffectedAreaReference> AffectedAreas { get; init; }
    public required IReadOnlyList<PatchProposalExpectedValidationReference> ExpectedValidationReferences { get; init; }
    public required IReadOnlyList<PatchProposalEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<PatchProposalGateHint> GateHints { get; init; }
    public required IReadOnlyList<PatchProposalRisk> Risks { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required IReadOnlyList<string> SafePackageSummaryLines { get; init; }
    public required bool IsPackageOnly { get; init; }
    public required bool IsPatch { get; init; }
    public required bool IsDiff { get; init; }
    public required bool IsSourceApply { get; init; }
    public required bool IsImplementation { get; init; }
    public required bool CanGeneratePatch { get; init; }
    public required bool CanApplyPatch { get; init; }
    public required bool CanApplySource { get; init; }
    public required bool CanMutateFiles { get; init; }
    public required bool CanReadSourceFiles { get; init; }
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

public sealed record PatchProposalAffectedAreaReference
{
    public required PatchProposalAffectedAreaKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record PatchProposalExpectedValidationReference
{
    public required PatchProposalExpectedValidationKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record PatchProposalEvidenceReference
{
    public required PatchProposalEvidenceKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record PatchProposalGateHint
{
    public required PatchProposalGateKind Kind { get; init; }
    public PatchProposalSeverityHint SeverityHint { get; init; } = PatchProposalSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record PatchProposalRisk
{
    public required PatchProposalRiskKind Kind { get; init; }
    public PatchProposalSeverityHint SeverityHint { get; init; } = PatchProposalSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public enum PatchProposalTargetKind
{
    Unknown = 0,
    ImplementationProposalPackage = 1,
    SourceApplyCandidate = 2,
    PatchReviewCandidate = 3,
    RepositoryChangeCandidate = 4
}

public enum PatchProposalEvidencePackageStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    BlockedByWorkflowGate = 2,
    MissingRequiredPatchProposalEvidence = 3,
    PatchProposalEvidencePackageProduced = 4
}

public enum PatchProposalEvidencePackageReason
{
    Unknown = 0,
    PackageOnly = 1,
    SuppliedEvidenceOnly = 2,
    PatchNotGenerated = 3,
    DiffNotGenerated = 4,
    SourceApplyNotImplemented = 5,
    SourceNotApplied = 6,
    PatchNotApplied = 7,
    FilesNotMutated = 8,
    SourceFilesNotRead = 9,
    CommandNotRun = 10,
    ToolNotInvoked = 11,
    AgentNotDispatched = 12,
    ModelNotCalled = 13,
    PromptNotBuilt = 14,
    ApprovalNotSatisfied = 15,
    PolicyNotSatisfied = 16,
    WorkflowNotTransitioned = 17,
    TicketNotCreated = 18,
    MemoryNotPromoted = 19,
    RetrievalNotActivated = 20,
    SqlNotWritten = 21,
    MissingWorkflowRunId = 22,
    MissingWorkflowStepId = 23,
    MissingPackageReference = 24,
    MissingProjectReference = 25,
    MissingTargetReference = 26,
    InvalidTargetKind = 27,
    MissingChangeIntentSummary = 28,
    MissingImplementationProposal = 29,
    MissingSourceApplyApprovalRequirement = 30,
    MissingAffectedAreaReference = 31,
    MissingExpectedValidationReference = 32,
    MissingEvidenceReference = 33,
    MissingGateHint = 34,
    UnsafeInput = 35,
    BlockedByRunnerEvaluation = 36,
    BlockedByDryRun = 37,
    BlockedByRouteSuggestion = 38,
    BlockedByImplementationProposal = 39,
    BlockedBySourceApplyApprovalRequirement = 40,
    BlockedByHumanApprovalPackage = 41
}

public enum PatchProposalAffectedAreaKind
{
    Unknown = 0,
    ProjectReference = 1,
    ModuleReference = 2,
    FilePathReference = 3,
    TestAreaReference = 4,
    ConfigurationAreaReference = 5,
    DocumentationAreaReference = 6,
    ExternalArtifactReference = 7
}

public enum PatchProposalExpectedValidationKind
{
    Unknown = 0,
    FocusedTestBandReference = 1,
    WorkflowRegressionReference = 2,
    GovernanceRegressionReference = 3,
    BuildValidationReference = 4,
    DiffCheckReference = 5,
    ManualReviewReference = 6
}

public enum PatchProposalEvidenceKind
{
    Unknown = 0,
    ImplementationProposalPackageReference = 1,
    SourceApplyApprovalRequirementReference = 2,
    HumanApprovalPackageReference = 3,
    WorkflowStepEvaluationReference = 4,
    DryRunResultReference = 5,
    PolicyEvidenceReference = 6,
    ExternalArtifactReference = 7
}

public enum PatchProposalGateKind
{
    Unknown = 0,
    HumanApprovalRequired = 1,
    PolicyEvidenceRequired = 2,
    SourceApplyApprovalRequirementRequired = 3,
    PatchMaterialForbidden = 4,
    SourceChangeForbidden = 5,
    PatchApplicationForbidden = 6,
    ValidationRequiredLater = 7,
    ManualReviewRequired = 8
}

public enum PatchProposalRiskKind
{
    Unknown = 0,
    MissingImplementationEvidence = 1,
    MissingApprovalRequirement = 2,
    CandidatePackageConfusedWithPatch = 3,
    PatchMaterialLeakRisk = 4,
    SourceChangeRisk = 5,
    ValidationGapRisk = 6,
    PolicyRisk = 7,
    ApprovalRisk = 8,
    OverclaimRisk = 9
}

public enum PatchProposalSeverityHint
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}
