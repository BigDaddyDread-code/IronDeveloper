namespace IronDev.Core.Workflow;

public interface IMemoryImprovementPackageCandidateWorkflow
{
    MemoryImprovementPackageCandidateResult Prepare(MemoryImprovementPackageCandidateRequest? request);
}

public sealed class MemoryImprovementPackageCandidateWorkflow : IMemoryImprovementPackageCandidateWorkflow
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
        "raw memory",
        "raw log",
        "full log",
        "wholepatch",
        "whole patch",
        "entirepatch",
        "entire patch",
        "patchpayload",
        "patch payload",
        "source content",
        "source file contents",
        "accepted memory updated",
        "accepted memory mutated",
        "memory accepted",
        "promote memory",
        "memory promoted",
        "promotion approved",
        "sql written",
        "sql write performed",
        "vector store written",
        "vector store updated",
        "retrieval activated",
        "activate retrieval",
        "embedding generated",
        "duplicate resolved",
        "conflict resolved",
        "stale memory marked",
        "project truth decided",
        "source of truth decided",
        "approval granted",
        "approval satisfied",
        "policy satisfied",
        "execution allowed",
        "workflow may continue",
        "workflow continued",
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
        "release approved"
    ];

    public MemoryImprovementPackageCandidateResult Prepare(MemoryImprovementPackageCandidateRequest? request)
    {
        if (request is null)
            return Result(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                MemoryImprovementPackageCandidateStatus.InvalidRequest,
                MemoryImprovementTargetKind.Unknown,
                MemoryImprovementKind.Unknown,
                string.Empty,
                [MemoryImprovementPackageCandidateReason.Unknown],
                [],
                [],
                [],
                [],
                [],
                [],
                []);

        var workflowRunId = SafeId(request.WorkflowRunId);
        var workflowStepId = SafeId(request.WorkflowStepId);
        var packageReferenceSeed = SafeId(request.MemoryImprovementPackageReferenceId);
        var projectReferenceId = SafeId(request.ProjectReferenceId);
        var targetReferenceId = SafeId(request.TargetReferenceId);
        var invalidReasons = InvalidReasons(request).ToArray();

        if (invalidReasons.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                packageReferenceSeed,
                projectReferenceId,
                targetReferenceId,
                MemoryImprovementPackageCandidateStatus.InvalidRequest,
                request.TargetKind,
                request.ImprovementKind,
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
                packageReferenceSeed,
                projectReferenceId,
                targetReferenceId,
                MemoryImprovementPackageCandidateStatus.BlockedByWorkflowGate,
                request.TargetKind,
                request.ImprovementKind,
                targetReferenceId,
                gateReasons,
                [],
                [],
                [],
                [],
                [],
                ["Workflow gate snapshot blocked memory improvement package production."],
                []);

        var missingEvidence = MissingEvidence(request).ToArray();
        if (missingEvidence.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                packageReferenceSeed,
                projectReferenceId,
                targetReferenceId,
                MemoryImprovementPackageCandidateStatus.MissingRequiredMemoryEvidence,
                request.TargetKind,
                request.ImprovementKind,
                targetReferenceId,
                [
                    MemoryImprovementPackageCandidateReason.PackageOnly,
                    MemoryImprovementPackageCandidateReason.SuppliedEvidenceOnly,
                    .. MissingReasons(request)
                ],
                SafeEvidence(request),
                SafeSourceOfTruth(request),
                SafeConflictHints(request),
                SafePromotionGateHints(request),
                SafeRisks(request),
                ["Memory improvement package was not produced because required supplied review material is missing."],
                missingEvidence);

        return Result(
            workflowRunId,
            workflowStepId,
            packageReferenceSeed,
            projectReferenceId,
            targetReferenceId,
            MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced,
            request.TargetKind,
            request.ImprovementKind,
            targetReferenceId,
            BoundaryReasons(),
            SafeEvidence(request),
            SafeSourceOfTruth(request),
            SafeConflictHints(request),
            SafePromotionGateHints(request),
            SafeRisks(request),
            SummaryLines(request).ToArray(),
            []);
    }

    private static IEnumerable<MemoryImprovementPackageCandidateReason> InvalidReasons(MemoryImprovementPackageCandidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowRunId))
            yield return MemoryImprovementPackageCandidateReason.MissingWorkflowRunId;

        if (string.IsNullOrWhiteSpace(request.WorkflowStepId))
            yield return MemoryImprovementPackageCandidateReason.MissingWorkflowStepId;

        if (string.IsNullOrWhiteSpace(request.MemoryImprovementPackageReferenceId))
            yield return MemoryImprovementPackageCandidateReason.MissingPackageReference;

        if (string.IsNullOrWhiteSpace(request.ProjectReferenceId))
            yield return MemoryImprovementPackageCandidateReason.MissingProjectReference;

        if (request.TargetKind == MemoryImprovementTargetKind.Unknown)
            yield return MemoryImprovementPackageCandidateReason.InvalidTargetKind;

        if (string.IsNullOrWhiteSpace(request.TargetReferenceId))
            yield return MemoryImprovementPackageCandidateReason.MissingTargetReference;

        if (request.ImprovementKind == MemoryImprovementKind.Unknown)
            yield return MemoryImprovementPackageCandidateReason.InvalidImprovementKind;

        if (ContainsUnsafeInput(request))
            yield return MemoryImprovementPackageCandidateReason.UnsafeInput;
    }

    private static IEnumerable<MemoryImprovementPackageCandidateReason> GateBlockReasons(MemoryImprovementPackageCandidateRequest request)
    {
        if (request.StepEvaluation is not null && request.StepEvaluation.Eligibility != WorkflowStepRunnerEligibility.EligibleForFutureExecution)
            yield return MemoryImprovementPackageCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.PolicyPreflightStatus is WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest or WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence)
            yield return MemoryImprovementPackageCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.A2aHandoffValidationStatus is WorkflowA2aHandoffValidationStatus.InvalidRequest or WorkflowA2aHandoffValidationStatus.InvalidStepContract or WorkflowA2aHandoffValidationStatus.InvalidHandoffReference or WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence)
            yield return MemoryImprovementPackageCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.ApprovalHaltStatus == WorkflowApprovalHaltStatus.ApprovalRequiredHalt)
            yield return MemoryImprovementPackageCandidateReason.BlockedByRunnerEvaluation;

        if (request.DryRunResult is not null && request.DryRunResult.Status != WorkflowDryRunStatus.DryRunCompleted)
            yield return MemoryImprovementPackageCandidateReason.BlockedByDryRun;

        if (request.RouteSuggestion is not null && RouteSuggestionBlocks(request.RouteSuggestion))
            yield return MemoryImprovementPackageCandidateReason.BlockedByRouteSuggestion;

        if (request.ToolRequestGatePreview is not null && request.ToolRequestGatePreview.Status != ToolRequestGatePreviewCandidateStatus.GatePreviewProduced)
            yield return MemoryImprovementPackageCandidateReason.BlockedByToolRequestGatePreview;

        if (request.ImplementationProposal is not null && request.ImplementationProposal.Status != ImplementationProposalPackageCandidateStatus.ProposalPackageProduced)
            yield return MemoryImprovementPackageCandidateReason.BlockedByImplementationProposal;

        if (request.CriticReviewRequest is not null && request.CriticReviewRequest.Status != CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced)
            yield return MemoryImprovementPackageCandidateReason.BlockedByCriticReviewRequest;

        if (request.TestFailureReview is not null && request.TestFailureReview.Status != TestFailureReviewCandidateStatus.ReviewMaterialProduced)
            yield return MemoryImprovementPackageCandidateReason.BlockedByTestFailureReview;
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

    private static IEnumerable<string> MissingEvidence(MemoryImprovementPackageCandidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SafeProposedMemorySummary))
            yield return "safe proposed memory summary";

        if (request.EvidenceReferences.Count == 0)
            yield return "memory improvement evidence reference";

        if (request.SourceOfTruthReferences.Count == 0)
            yield return "source of truth reference";

        if (request.PromotionGateHints.Count == 0)
            yield return "promotion gate hint";
    }

    private static IEnumerable<MemoryImprovementPackageCandidateReason> MissingReasons(MemoryImprovementPackageCandidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SafeProposedMemorySummary))
            yield return MemoryImprovementPackageCandidateReason.MissingProposedMemorySummary;

        if (request.EvidenceReferences.Count == 0)
            yield return MemoryImprovementPackageCandidateReason.MissingEvidenceReference;

        if (request.SourceOfTruthReferences.Count == 0)
            yield return MemoryImprovementPackageCandidateReason.MissingSourceOfTruthReference;

        if (request.PromotionGateHints.Count == 0)
            yield return MemoryImprovementPackageCandidateReason.MissingPromotionGateHint;
    }

    private static IReadOnlyList<MemoryImprovementEvidenceReference> SafeEvidence(MemoryImprovementPackageCandidateRequest request)
    {
        var evidence = request.EvidenceReferences
            .Where(reference => reference.Kind != MemoryImprovementEvidenceKind.Unknown)
            .Where(reference => !ContainsUnsafeMarker(reference.ReferenceId) && !ContainsUnsafeMarker(reference.SafeSummary))
            .Select(reference => reference with { ReferenceId = SafeText(reference.ReferenceId), SafeSummary = SafeNullableText(reference.SafeSummary) })
            .ToList();

        if (request.ToolRequestGatePreview is not null && !string.IsNullOrWhiteSpace(request.ToolRequestGatePreview.PreviewPackageReferenceId))
            evidence.Add(new MemoryImprovementEvidenceReference { Kind = MemoryImprovementEvidenceKind.ToolRequestPreviewReference, ReferenceId = request.ToolRequestGatePreview.PreviewPackageReferenceId, SafeSummary = "Supplied tool request gate preview candidate result." });

        if (request.ImplementationProposal is not null && !string.IsNullOrWhiteSpace(request.ImplementationProposal.ProposalPackageReferenceId))
            evidence.Add(new MemoryImprovementEvidenceReference { Kind = MemoryImprovementEvidenceKind.ImplementationProposalReference, ReferenceId = request.ImplementationProposal.ProposalPackageReferenceId, SafeSummary = "Supplied implementation proposal package candidate result." });

        if (request.CriticReviewRequest is not null && !string.IsNullOrWhiteSpace(request.CriticReviewRequest.ReviewPackageReferenceId))
            evidence.Add(new MemoryImprovementEvidenceReference { Kind = MemoryImprovementEvidenceKind.CriticReviewRequestReference, ReferenceId = request.CriticReviewRequest.ReviewPackageReferenceId, SafeSummary = "Supplied critic review request candidate result." });

        if (request.TestFailureReview is not null && !string.IsNullOrWhiteSpace(request.TestFailureReview.ReviewPackageReferenceId))
            evidence.Add(new MemoryImprovementEvidenceReference { Kind = MemoryImprovementEvidenceKind.TestFailureReviewReference, ReferenceId = request.TestFailureReview.ReviewPackageReferenceId, SafeSummary = "Supplied test failure review candidate result." });

        return evidence.Distinct().OrderBy(reference => reference.Kind).ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<MemoryImprovementSourceOfTruthReference> SafeSourceOfTruth(MemoryImprovementPackageCandidateRequest request) =>
        request.SourceOfTruthReferences
            .Where(reference => reference.Kind != MemoryImprovementSourceOfTruthKind.Unknown)
            .Where(reference => !ContainsUnsafeMarker(reference.ReferenceId) && !ContainsUnsafeMarker(reference.SafeSummary))
            .Select(reference => reference with { ReferenceId = SafeText(reference.ReferenceId), SafeSummary = SafeNullableText(reference.SafeSummary) })
            .Distinct()
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<MemoryImprovementConflictHint> SafeConflictHints(MemoryImprovementPackageCandidateRequest request) =>
        request.ConflictHints
            .Where(hint => hint.Kind != MemoryImprovementConflictKind.Unknown)
            .Where(hint => !ContainsUnsafeMarker(hint.SafeSummary))
            .Select(hint => hint with { SafeSummary = SafeNullableText(hint.SafeSummary) })
            .Distinct()
            .OrderBy(hint => hint.Kind)
            .ThenByDescending(hint => hint.SeverityHint)
            .ThenBy(hint => hint.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<MemoryImprovementPromotionGateHint> SafePromotionGateHints(MemoryImprovementPackageCandidateRequest request) =>
        request.PromotionGateHints
            .Where(hint => hint.Kind != MemoryImprovementGateKind.Unknown)
            .Where(hint => !ContainsUnsafeMarker(hint.SafeSummary))
            .Select(hint => hint with { SafeSummary = SafeNullableText(hint.SafeSummary) })
            .Distinct()
            .OrderBy(hint => hint.Kind)
            .ThenByDescending(hint => hint.SeverityHint)
            .ThenBy(hint => hint.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<MemoryImprovementRisk> SafeRisks(MemoryImprovementPackageCandidateRequest request) =>
        request.Risks
            .Where(risk => risk.Kind != MemoryImprovementRiskKind.Unknown)
            .Where(risk => !ContainsUnsafeMarker(risk.SafeSummary))
            .Select(risk => risk with { SafeSummary = SafeNullableText(risk.SafeSummary) })
            .Distinct()
            .OrderBy(risk => risk.Kind)
            .ThenByDescending(risk => risk.SeverityHint)
            .ThenBy(risk => risk.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> SummaryLines(MemoryImprovementPackageCandidateRequest request)
    {
        yield return "Memory improvement package was produced from supplied evidence only.";
        yield return "Package output is proposal material for later governed review.";
        yield return "Existing memory remains unchanged.";
        yield return "No memory promotion occurred.";
        yield return "No SQL write was performed.";
        yield return "No vector write was performed.";
        yield return "No embedding was produced.";
        yield return "Retrieval remained inactive.";
        yield return "Duplicate and conflict hints remain review material only.";
        yield return "Human and governed review remain required before any memory promotion.";
        yield return $"Memory improvement target kind: {request.TargetKind}.";
        yield return $"Memory improvement kind: {request.ImprovementKind}.";
        yield return $"Target reference: {SafeText(request.TargetReferenceId)}.";

        if (!string.IsNullOrWhiteSpace(request.SafeCurrentMemorySummaryReferenceId))
            yield return $"Current memory summary reference: {SafeText(request.SafeCurrentMemorySummaryReferenceId)}.";

        if (!string.IsNullOrWhiteSpace(request.SafeProposedMemorySummary))
            yield return $"Safe proposed summary: {SafeText(request.SafeProposedMemorySummary)}.";
    }

    private static MemoryImprovementPackageCandidateReason[] BoundaryReasons() =>
    [
        MemoryImprovementPackageCandidateReason.PackageOnly,
        MemoryImprovementPackageCandidateReason.SuppliedEvidenceOnly,
        MemoryImprovementPackageCandidateReason.MemoryNotAccepted,
        MemoryImprovementPackageCandidateReason.MemoryNotPromoted,
        MemoryImprovementPackageCandidateReason.AcceptedMemoryNotMutated,
        MemoryImprovementPackageCandidateReason.SqlNotWritten,
        MemoryImprovementPackageCandidateReason.VectorStoreNotWritten,
        MemoryImprovementPackageCandidateReason.EmbeddingNotGenerated,
        MemoryImprovementPackageCandidateReason.RetrievalNotActivated,
        MemoryImprovementPackageCandidateReason.DuplicateNotResolved,
        MemoryImprovementPackageCandidateReason.ConflictNotResolved,
        MemoryImprovementPackageCandidateReason.StaleMemoryNotMarked,
        MemoryImprovementPackageCandidateReason.AgentNotDispatched,
        MemoryImprovementPackageCandidateReason.ToolNotInvoked,
        MemoryImprovementPackageCandidateReason.ModelNotCalled,
        MemoryImprovementPackageCandidateReason.PromptNotBuilt,
        MemoryImprovementPackageCandidateReason.TicketNotCreated,
        MemoryImprovementPackageCandidateReason.ApprovalNotSatisfied,
        MemoryImprovementPackageCandidateReason.PolicyNotSatisfied,
        MemoryImprovementPackageCandidateReason.WorkflowNotTransitioned
    ];

    private static MemoryImprovementPackageCandidateResult Result(
        string workflowRunId,
        string workflowStepId,
        string packageReferenceSeed,
        string projectReferenceId,
        string packageSeed,
        MemoryImprovementPackageCandidateStatus status,
        MemoryImprovementTargetKind targetKind,
        MemoryImprovementKind improvementKind,
        string targetReferenceId,
        IReadOnlyList<MemoryImprovementPackageCandidateReason> reasons,
        IReadOnlyList<MemoryImprovementEvidenceReference> evidenceReferences,
        IReadOnlyList<MemoryImprovementSourceOfTruthReference> sourceOfTruthReferences,
        IReadOnlyList<MemoryImprovementConflictHint> conflictHints,
        IReadOnlyList<MemoryImprovementPromotionGateHint> promotionGateHints,
        IReadOnlyList<MemoryImprovementRisk> risks,
        IReadOnlyList<string> safePackageSummaryLines,
        IReadOnlyList<string> missingEvidence) =>
        new()
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            MemoryImprovementPackageReferenceId = packageReferenceSeed,
            PackageReferenceId = PackageReferenceId(workflowRunId, workflowStepId, packageReferenceSeed, packageSeed),
            ProjectReferenceId = projectReferenceId,
            Status = status,
            TargetKind = targetKind,
            ImprovementKind = improvementKind,
            TargetReferenceId = targetReferenceId,
            Reasons = reasons.Concat(BoundaryReasons()).Distinct().OrderBy(reason => reason).ToArray(),
            EvidenceReferences = evidenceReferences,
            SourceOfTruthReferences = sourceOfTruthReferences,
            ConflictHints = conflictHints,
            PromotionGateHints = promotionGateHints,
            Risks = risks,
            MissingEvidence = missingEvidence.Where(value => !ContainsUnsafeMarker(value)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            SafePackageSummaryLines = safePackageSummaryLines.Where(value => !ContainsUnsafeMarker(value)).Select(SafeText).ToArray(),
            IsPackageOnly = true,
            IsAcceptedMemory = false,
            IsPromotion = false,
            CanMutateAcceptedMemory = false,
            CanPromoteMemory = false,
            CanWriteSql = false,
            CanWriteVectorStore = false,
            CanGenerateEmbedding = false,
            CanActivateRetrieval = false,
            CanResolveDuplicate = false,
            CanResolveConflict = false,
            CanMarkStale = false,
            CanDispatchAgent = false,
            CanInvokeTool = false,
            CanCallModel = false,
            CanBuildPrompt = false,
            CanSatisfyApproval = false,
            CanSatisfyPolicy = false,
            CanTransitionWorkflow = false
        };

    private static string PackageReferenceId(string workflowRunId, string workflowStepId, string packageReferenceSeed, string packageSeed) =>
        string.IsNullOrWhiteSpace(workflowRunId) || string.IsNullOrWhiteSpace(workflowStepId) || string.IsNullOrWhiteSpace(packageReferenceSeed) || string.IsNullOrWhiteSpace(packageSeed)
            ? string.Empty
            : $"memory-improvement-package:{workflowRunId}:{workflowStepId}:{packageReferenceSeed}:{packageSeed}";

    private static bool ContainsUnsafeInput(MemoryImprovementPackageCandidateRequest request) =>
        ContainsUnsafeMarker(request.WorkflowRunId) ||
        ContainsUnsafeMarker(request.WorkflowStepId) ||
        ContainsUnsafeMarker(request.MemoryImprovementPackageReferenceId) ||
        ContainsUnsafeMarker(request.ProjectReferenceId) ||
        ContainsUnsafeMarker(request.TargetReferenceId) ||
        ContainsUnsafeMarker(request.SafeCurrentMemorySummaryReferenceId) ||
        ContainsUnsafeMarker(request.SafeProposedMemorySummary) ||
        ContainsUnsafeMarker(request.CorrelationId) ||
        request.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
        request.SourceOfTruthReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
        request.ConflictHints.Any(hint => ContainsUnsafeMarker(hint.SafeSummary)) ||
        request.PromotionGateHints.Any(hint => ContainsUnsafeMarker(hint.SafeSummary)) ||
        request.Risks.Any(risk => ContainsUnsafeMarker(risk.SafeSummary)) ||
        ContainsUnsafeToolRequestGatePreview(request.ToolRequestGatePreview) ||
        ContainsUnsafeImplementationProposal(request.ImplementationProposal) ||
        ContainsUnsafeCriticReviewRequest(request.CriticReviewRequest) ||
        ContainsUnsafeTestFailureReview(request.TestFailureReview) ||
        ContainsUnsafeMarker(request.StepEvaluation?.StepId) ||
        ContainsUnsafeMarker(request.DryRunResult?.WorkflowRunId) ||
        ContainsUnsafeMarker(request.DryRunResult?.WorkflowStepId) ||
        (request.DryRunResult?.SafeReportLines.Any(ContainsUnsafeMarker) ?? false) ||
        ContainsUnsafeMarker(request.RouteSuggestion?.WorkflowRunId) ||
        ContainsUnsafeMarker(request.RouteSuggestion?.WorkflowStepId) ||
        (request.RouteSuggestion?.SourceStatusReferences.Any(ContainsUnsafeMarker) ?? false) ||
        (request.RouteSuggestion?.SafeReportLines.Any(ContainsUnsafeMarker) ?? false);

    private static bool ContainsUnsafeToolRequestGatePreview(ToolRequestGatePreviewCandidateResult? result) =>
        result is not null &&
        (ContainsUnsafeMarker(result.WorkflowRunId) ||
            ContainsUnsafeMarker(result.WorkflowStepId) ||
            ContainsUnsafeMarker(result.ToolRequestPreviewReferenceId) ||
            ContainsUnsafeMarker(result.PreviewPackageReferenceId) ||
            ContainsUnsafeMarker(result.CapabilityName) ||
            result.InputReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.ExpectedOutputReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.GateRequirementHints.Any(hint => ContainsUnsafeMarker(hint.SafeSummary)) ||
            result.Risks.Any(risk => ContainsUnsafeMarker(risk.SafeSummary)) ||
            result.MissingGateMaterial.Any(ContainsUnsafeMarker) ||
            result.SafePreviewSummaryLines.Any(ContainsUnsafeMarker));

    private static bool ContainsUnsafeImplementationProposal(ImplementationProposalPackageCandidateResult? result) =>
        result is not null &&
        (ContainsUnsafeMarker(result.WorkflowRunId) ||
            ContainsUnsafeMarker(result.WorkflowStepId) ||
            ContainsUnsafeMarker(result.ProposalReferenceId) ||
            ContainsUnsafeMarker(result.ProposalPackageReferenceId) ||
            ContainsUnsafeMarker(result.TargetReferenceId) ||
            result.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.AffectedAreas.Any(area => ContainsUnsafeMarker(area.ReferenceId) || ContainsUnsafeMarker(area.SafeSummary)) ||
            result.ProposedSteps.Any(step => ContainsUnsafeMarker(step.SafeSummary)) ||
            result.ValidationSteps.Any(step => ContainsUnsafeMarker(step.SafeSummary)) ||
            result.Risks.Any(risk => ContainsUnsafeMarker(risk.SafeSummary)) ||
            result.MissingEvidence.Any(ContainsUnsafeMarker) ||
            result.SafePackageSummaryLines.Any(ContainsUnsafeMarker));

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

    private static string SafeId(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? string.Empty : value.Trim();

    private static string SafeText(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? string.Empty : value.Trim();

    private static string? SafeNullableText(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? null : value.Trim();

    private static bool ContainsUnsafeMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}

public sealed record MemoryImprovementPackageCandidateRequest
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string MemoryImprovementPackageReferenceId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required MemoryImprovementTargetKind TargetKind { get; init; }
    public required string TargetReferenceId { get; init; }
    public required MemoryImprovementKind ImprovementKind { get; init; }
    public string? SafeCurrentMemorySummaryReferenceId { get; init; }
    public string? SafeProposedMemorySummary { get; init; }
    public IReadOnlyList<MemoryImprovementEvidenceReference> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<MemoryImprovementSourceOfTruthReference> SourceOfTruthReferences { get; init; } = [];
    public IReadOnlyList<MemoryImprovementConflictHint> ConflictHints { get; init; } = [];
    public IReadOnlyList<MemoryImprovementPromotionGateHint> PromotionGateHints { get; init; } = [];
    public IReadOnlyList<MemoryImprovementRisk> Risks { get; init; } = [];
    public ToolRequestGatePreviewCandidateResult? ToolRequestGatePreview { get; init; }
    public ImplementationProposalPackageCandidateResult? ImplementationProposal { get; init; }
    public CriticReviewRequestCandidateResult? CriticReviewRequest { get; init; }
    public TestFailureReviewCandidateResult? TestFailureReview { get; init; }
    public WorkflowStepRunnerEvaluation? StepEvaluation { get; init; }
    public WorkflowDryRunResult? DryRunResult { get; init; }
    public BoxedLangGraphRouteSuggestion? RouteSuggestion { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record MemoryImprovementEvidenceReference
{
    public required MemoryImprovementEvidenceKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record MemoryImprovementSourceOfTruthReference
{
    public required MemoryImprovementSourceOfTruthKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record MemoryImprovementConflictHint
{
    public required MemoryImprovementConflictKind Kind { get; init; }
    public MemoryImprovementSeverityHint SeverityHint { get; init; } = MemoryImprovementSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record MemoryImprovementPromotionGateHint
{
    public required MemoryImprovementGateKind Kind { get; init; }
    public MemoryImprovementSeverityHint SeverityHint { get; init; } = MemoryImprovementSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record MemoryImprovementRisk
{
    public required MemoryImprovementRiskKind Kind { get; init; }
    public MemoryImprovementSeverityHint SeverityHint { get; init; } = MemoryImprovementSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record MemoryImprovementPackageCandidateResult
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string MemoryImprovementPackageReferenceId { get; init; }
    public required string PackageReferenceId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required MemoryImprovementPackageCandidateStatus Status { get; init; }
    public required MemoryImprovementTargetKind TargetKind { get; init; }
    public required MemoryImprovementKind ImprovementKind { get; init; }
    public required string TargetReferenceId { get; init; }
    public required IReadOnlyList<MemoryImprovementPackageCandidateReason> Reasons { get; init; }
    public required IReadOnlyList<MemoryImprovementEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<MemoryImprovementSourceOfTruthReference> SourceOfTruthReferences { get; init; }
    public required IReadOnlyList<MemoryImprovementConflictHint> ConflictHints { get; init; }
    public required IReadOnlyList<MemoryImprovementPromotionGateHint> PromotionGateHints { get; init; }
    public required IReadOnlyList<MemoryImprovementRisk> Risks { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required IReadOnlyList<string> SafePackageSummaryLines { get; init; }
    public required bool IsPackageOnly { get; init; }
    public required bool IsAcceptedMemory { get; init; }
    public required bool IsPromotion { get; init; }
    public required bool CanMutateAcceptedMemory { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanWriteSql { get; init; }
    public required bool CanWriteVectorStore { get; init; }
    public required bool CanGenerateEmbedding { get; init; }
    public required bool CanActivateRetrieval { get; init; }
    public required bool CanResolveDuplicate { get; init; }
    public required bool CanResolveConflict { get; init; }
    public required bool CanMarkStale { get; init; }
    public required bool CanDispatchAgent { get; init; }
    public required bool CanInvokeTool { get; init; }
    public required bool CanCallModel { get; init; }
    public required bool CanBuildPrompt { get; init; }
    public required bool CanSatisfyApproval { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanTransitionWorkflow { get; init; }
}

public enum MemoryImprovementTargetKind
{
    Unknown = 0,
    ProjectMemoryProposal = 1,
    ExistingMemoryReference = 2,
    PortableEngineeringMemoryCandidate = 3,
    StaleMemoryCandidate = 4,
    DuplicateMemoryCandidate = 5,
    ConflictCandidate = 6,
    EvidenceGapCandidate = 7
}

public enum MemoryImprovementKind
{
    Unknown = 0,
    AddCandidate = 1,
    ClarifyCandidate = 2,
    SplitCandidate = 3,
    MergeCandidate = 4,
    MarkStaleCandidate = 5,
    MarkDuplicateCandidate = 6,
    FlagConflictCandidate = 7,
    AddMissingEvidenceCandidate = 8,
    SanitizeForPortableEngineeringMemoryCandidate = 9
}

public enum MemoryImprovementEvidenceKind
{
    Unknown = 0,
    GovernanceEventReference = 1,
    WorkflowStepEvaluationReference = 2,
    DryRunResultReference = 3,
    ToolRequestPreviewReference = 4,
    ImplementationProposalReference = 5,
    CriticReviewRequestReference = 6,
    TestFailureReviewReference = 7,
    MemoryProposalReference = 8,
    ExternalArtifactReference = 9
}

public enum MemoryImprovementSourceOfTruthKind
{
    Unknown = 0,
    SqlRecordReference = 1,
    GovernanceEventReference = 2,
    UserApprovedSourceReference = 3,
    ProjectDocumentReference = 4,
    ReceiptReference = 5,
    TestEvidenceReference = 6,
    ExternalArtifactReference = 7
}

public enum MemoryImprovementConflictKind
{
    Unknown = 0,
    PossibleDuplicate = 1,
    PossibleStaleMemory = 2,
    PossibleContradiction = 3,
    MissingSourceOfTruth = 4,
    CrossProjectContaminationRisk = 5,
    PortableMemorySanitizationRisk = 6,
    ConfidentialDetailLeakRisk = 7,
    AuthorityTransferRisk = 8
}

public enum MemoryImprovementGateKind
{
    Unknown = 0,
    HumanReviewRequired = 1,
    SourceOfTruthRequired = 2,
    DuplicateCheckRequired = 3,
    StalenessCheckRequired = 4,
    ConflictCheckRequired = 5,
    ProjectBoundaryCheckRequired = 6,
    PortableMemorySanitizationRequired = 7,
    PromotionApprovalRequired = 8,
    RetrievalActivationForbidden = 9,
    AcceptedMemoryMutationForbidden = 10
}

public enum MemoryImprovementRiskKind
{
    Unknown = 0,
    InsufficientEvidence = 1,
    SourceOfTruthRisk = 2,
    DuplicateRisk = 3,
    StaleMemoryRisk = 4,
    ConflictRisk = 5,
    ProjectBoundaryRisk = 6,
    CrossProjectLeakRisk = 7,
    PortableMemoryLeakRisk = 8,
    PromotionAuthorityRisk = 9,
    RetrievalAuthorityRisk = 10,
    OverclaimRisk = 11
}

public enum MemoryImprovementSeverityHint
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum MemoryImprovementPackageCandidateStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    BlockedByWorkflowGate = 2,
    MissingRequiredMemoryEvidence = 3,
    MemoryImprovementPackageProduced = 4
}

public enum MemoryImprovementPackageCandidateReason
{
    Unknown = 0,
    PackageOnly = 1,
    SuppliedEvidenceOnly = 2,
    MissingWorkflowRunId = 3,
    MissingWorkflowStepId = 4,
    MissingPackageReference = 5,
    MissingProjectReference = 6,
    MissingTargetReference = 7,
    InvalidTargetKind = 8,
    InvalidImprovementKind = 9,
    MissingEvidenceReference = 10,
    MissingSourceOfTruthReference = 11,
    MissingProposedMemorySummary = 12,
    MissingPromotionGateHint = 13,
    UnsafeInput = 14,
    BlockedByRunnerEvaluation = 15,
    BlockedByDryRun = 16,
    BlockedByRouteSuggestion = 17,
    BlockedByToolRequestGatePreview = 18,
    BlockedByImplementationProposal = 19,
    BlockedByCriticReviewRequest = 20,
    BlockedByTestFailureReview = 21,
    MemoryNotAccepted = 22,
    MemoryNotPromoted = 23,
    AcceptedMemoryNotMutated = 24,
    SqlNotWritten = 25,
    VectorStoreNotWritten = 26,
    EmbeddingNotGenerated = 27,
    RetrievalNotActivated = 28,
    DuplicateNotResolved = 29,
    ConflictNotResolved = 30,
    StaleMemoryNotMarked = 31,
    AgentNotDispatched = 32,
    ToolNotInvoked = 33,
    ModelNotCalled = 34,
    PromptNotBuilt = 35,
    TicketNotCreated = 36,
    ApprovalNotSatisfied = 37,
    PolicyNotSatisfied = 38,
    WorkflowNotTransitioned = 39
}
