namespace IronDev.Core.Workflow;

public interface IRepeatedFailurePatternReviewCandidateWorkflow
{
    RepeatedFailurePatternReviewCandidateResult Prepare(RepeatedFailurePatternReviewCandidateRequest? request);
}

public sealed class RepeatedFailurePatternReviewCandidateWorkflow : IRepeatedFailurePatternReviewCandidateWorkflow
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
        "raw logs",
        "rawtrace",
        "raw trace",
        "rawreport",
        "raw report",
        "history query",
        "query history",
        "memory query",
        "query memory",
        "search memory",
        "source content",
        "source file contents",
        "wholepatch",
        "whole patch",
        "entirepatch",
        "entire patch",
        "patchpayload",
        "patch payload",
        "pattern detected",
        "pattern proven",
        "root cause found",
        "incident created",
        "ticket created",
        "memory promoted",
        "workflow may continue",
        "release ready",
        "approval granted",
        "approval satisfied",
        "policy satisfied",
        "execution allowed",
        "agent dispatched",
        "tool invoked",
        "source mutated",
        "patch applied",
        "sql written",
        "model called",
        "prompt built"
    ];

    public RepeatedFailurePatternReviewCandidateResult Prepare(RepeatedFailurePatternReviewCandidateRequest? request)
    {
        if (request is null)
        {
            return Result(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                RepeatedFailurePatternReviewCandidateStatus.InvalidRequest,
                RepeatedFailurePatternCategoryHint.Unknown,
                RepeatedFailureFrequencyHint.Unknown,
                RepeatedFailureRecencyHint.Unknown,
                RepeatedFailureConfidenceHint.Unknown,
                [RepeatedFailurePatternReviewCandidateReason.MissingWorkflowRunId],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                []);
        }

        var workflowRunId = SafeId(request.WorkflowRunId);
        var workflowStepId = SafeId(request.WorkflowStepId);
        var patternReviewReferenceId = SafeId(request.PatternReviewReferenceId);
        var projectReferenceId = SafeId(request.ProjectReferenceId);
        var invalidReasons = InvalidReasons(request).ToArray();
        var occurrenceReferences = SafeOccurrenceReferences(request.OccurrenceReferences).ToList();
        var evidenceReferences = SafeEvidenceReferences(request.EvidenceReferences).ToList();
        var validationReferences = SafeValidationReferences(request.ValidationReferences).ToList();
        var candidatePackageReferences = SafeCandidatePackageReferences(request.CandidatePackageReferences).ToList();
        var gateHints = SafeGateHints(request.GateHints).ToList();
        var risks = SafeRisks(request.Risks).ToList();

        if (invalidReasons.Length > 0)
        {
            return Result(
                workflowRunId,
                workflowStepId,
                patternReviewReferenceId,
                projectReferenceId,
                RepeatedFailurePatternReviewCandidateStatus.InvalidRequest,
                request.CategoryHint,
                request.FrequencyHint,
                request.RecencyHint,
                request.ConfidenceHint,
                invalidReasons,
                occurrenceReferences,
                evidenceReferences,
                validationReferences,
                candidatePackageReferences,
                gateHints,
                risks,
                [],
                [],
                []);
        }

        var gateBlockReasons = WorkflowGateBlockReasons(request).ToArray();
        if (gateBlockReasons.Length > 0)
        {
            return Result(
                workflowRunId,
                workflowStepId,
                patternReviewReferenceId,
                projectReferenceId,
                RepeatedFailurePatternReviewCandidateStatus.BlockedByWorkflowGate,
                request.CategoryHint,
                request.FrequencyHint,
                request.RecencyHint,
                request.ConfidenceHint,
                gateBlockReasons,
                occurrenceReferences,
                evidenceReferences,
                validationReferences,
                candidatePackageReferences,
                gateHints,
                risks,
                [],
                [],
                []);
        }

        AddUpstreamPackageReferences(request, occurrenceReferences, evidenceReferences, candidatePackageReferences);

        var missingEvidence = MissingEvidenceFor(request, occurrenceReferences, evidenceReferences, validationReferences, gateHints).ToArray();
        if (missingEvidence.Length > 0)
        {
            return Result(
                workflowRunId,
                workflowStepId,
                patternReviewReferenceId,
                projectReferenceId,
                RepeatedFailurePatternReviewCandidateStatus.MissingRequiredPatternEvidence,
                request.CategoryHint,
                request.FrequencyHint,
                request.RecencyHint,
                request.ConfidenceHint,
                MissingReasonsFor(missingEvidence).ToArray(),
                occurrenceReferences,
                evidenceReferences,
                validationReferences,
                candidatePackageReferences,
                gateHints,
                risks,
                missingEvidence,
                [],
                []);
        }

        return Result(
            workflowRunId,
            workflowStepId,
            patternReviewReferenceId,
            projectReferenceId,
            RepeatedFailurePatternReviewCandidateStatus.PatternReviewPackageProduced,
            request.CategoryHint,
            request.FrequencyHint,
            request.RecencyHint,
            request.ConfidenceHint,
            [
                RepeatedFailurePatternReviewCandidateReason.ReviewOnly,
                RepeatedFailurePatternReviewCandidateReason.SuppliedEvidenceOnly
            ],
            occurrenceReferences,
            evidenceReferences,
            validationReferences,
            candidatePackageReferences,
            gateHints,
            risks,
            [],
            SummaryLines(request).ToArray(),
            FollowUpReviewQuestions());
    }

    private static IEnumerable<RepeatedFailurePatternReviewCandidateReason> InvalidReasons(RepeatedFailurePatternReviewCandidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowRunId))
            yield return RepeatedFailurePatternReviewCandidateReason.MissingWorkflowRunId;

        if (string.IsNullOrWhiteSpace(request.WorkflowStepId))
            yield return RepeatedFailurePatternReviewCandidateReason.MissingWorkflowStepId;

        if (string.IsNullOrWhiteSpace(request.PatternReviewReferenceId))
            yield return RepeatedFailurePatternReviewCandidateReason.MissingPatternReviewReference;

        if (string.IsNullOrWhiteSpace(request.ProjectReferenceId))
            yield return RepeatedFailurePatternReviewCandidateReason.MissingProjectReference;

        if (request.CategoryHint == RepeatedFailurePatternCategoryHint.Unknown)
            yield return RepeatedFailurePatternReviewCandidateReason.InvalidPatternCategory;

        if (request.FrequencyHint == RepeatedFailureFrequencyHint.Unknown)
            yield return RepeatedFailurePatternReviewCandidateReason.InvalidFrequencyHint;

        if (request.RecencyHint == RepeatedFailureRecencyHint.Unknown)
            yield return RepeatedFailurePatternReviewCandidateReason.InvalidRecencyHint;

        if (request.ConfidenceHint == RepeatedFailureConfidenceHint.Unknown)
            yield return RepeatedFailurePatternReviewCandidateReason.InvalidConfidenceHint;

        if (request.OccurrenceReferences.Any(reference => reference.Kind == RepeatedFailureOccurrenceKind.Unknown || string.IsNullOrWhiteSpace(reference.ReferenceId)))
            yield return RepeatedFailurePatternReviewCandidateReason.MissingOccurrenceReference;

        if (request.EvidenceReferences.Any(reference => reference.Kind == RepeatedFailureEvidenceKind.Unknown || string.IsNullOrWhiteSpace(reference.ReferenceId)))
            yield return RepeatedFailurePatternReviewCandidateReason.MissingEvidenceReference;

        if (request.ValidationReferences.Any(reference => reference.Kind == RepeatedFailureValidationKind.Unknown || reference.OutcomeHint == RepeatedFailureValidationOutcomeHint.Unknown || string.IsNullOrWhiteSpace(reference.ReferenceId)))
            yield return RepeatedFailurePatternReviewCandidateReason.MissingValidationReference;

        if (request.CandidatePackageReferences.Any(reference => reference.Kind == RepeatedFailureCandidatePackageKind.Unknown || string.IsNullOrWhiteSpace(reference.ReferenceId)))
            yield return RepeatedFailurePatternReviewCandidateReason.MissingEvidenceReference;

        if (request.GateHints.Any(hint => hint.Kind == RepeatedFailureReviewGateKind.Unknown))
            yield return RepeatedFailurePatternReviewCandidateReason.MissingGateHint;

        if (request.Risks.Any(risk => risk.Kind == RepeatedFailureRiskKind.Unknown))
            yield return RepeatedFailurePatternReviewCandidateReason.UnsafeInput;

        if (ContainsUnsafeInput(request))
            yield return RepeatedFailurePatternReviewCandidateReason.UnsafeInput;
    }

    private static IEnumerable<RepeatedFailurePatternReviewCandidateReason> WorkflowGateBlockReasons(RepeatedFailurePatternReviewCandidateRequest request)
    {
        if (request.StepEvaluation is not null && request.StepEvaluation.Eligibility != WorkflowStepRunnerEligibility.EligibleForFutureExecution)
            yield return RepeatedFailurePatternReviewCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.PolicyPreflightStatus is WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest or WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence)
            yield return RepeatedFailurePatternReviewCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.A2aHandoffValidationStatus is WorkflowA2aHandoffValidationStatus.InvalidRequest or WorkflowA2aHandoffValidationStatus.InvalidStepContract or WorkflowA2aHandoffValidationStatus.InvalidHandoffReference or WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence)
            yield return RepeatedFailurePatternReviewCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.ApprovalHaltStatus == WorkflowApprovalHaltStatus.ApprovalRequiredHalt)
            yield return RepeatedFailurePatternReviewCandidateReason.BlockedByRunnerEvaluation;

        if (request.DryRunResult is not null && request.DryRunResult.Status != WorkflowDryRunStatus.DryRunCompleted)
            yield return RepeatedFailurePatternReviewCandidateReason.BlockedByDryRun;

        if (request.RouteSuggestion is not null && RouteSuggestionBlocks(request.RouteSuggestion))
            yield return RepeatedFailurePatternReviewCandidateReason.BlockedByRouteSuggestion;

        if (request.DogfoodEvidenceBundle is not null && request.DogfoodEvidenceBundle.Status != DogfoodEvidenceBundleCandidateStatus.EvidenceBundleProduced)
            yield return RepeatedFailurePatternReviewCandidateReason.BlockedByDogfoodEvidenceBundle;

        if (request.HumanApprovalPackage is not null && request.HumanApprovalPackage.Status != HumanApprovalPackageCandidateStatus.ApprovalPackageProduced)
            yield return RepeatedFailurePatternReviewCandidateReason.BlockedByHumanApprovalPackage;

        if (request.MemoryImprovementPackage is not null && request.MemoryImprovementPackage.Status != MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced)
            yield return RepeatedFailurePatternReviewCandidateReason.BlockedByMemoryImprovementPackage;

        if (request.ToolRequestGatePreview is not null && request.ToolRequestGatePreview.Status != ToolRequestGatePreviewCandidateStatus.GatePreviewProduced)
            yield return RepeatedFailurePatternReviewCandidateReason.BlockedByToolRequestGatePreview;

        if (request.ImplementationProposal is not null && request.ImplementationProposal.Status != ImplementationProposalPackageCandidateStatus.ProposalPackageProduced)
            yield return RepeatedFailurePatternReviewCandidateReason.BlockedByImplementationProposal;

        if (request.CriticReviewRequest is not null && request.CriticReviewRequest.Status != CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced)
            yield return RepeatedFailurePatternReviewCandidateReason.BlockedByCriticReviewRequest;

        if (request.TestFailureReview is not null && request.TestFailureReview.Status != TestFailureReviewCandidateStatus.ReviewMaterialProduced)
            yield return RepeatedFailurePatternReviewCandidateReason.BlockedByTestFailureReview;
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

    private static void AddUpstreamPackageReferences(
        RepeatedFailurePatternReviewCandidateRequest request,
        List<RepeatedFailureOccurrenceReference> occurrenceReferences,
        List<RepeatedFailureEvidenceReference> evidenceReferences,
        List<RepeatedFailureCandidatePackageReference> candidatePackageReferences)
    {
        AddDogfoodBundle(request.DogfoodEvidenceBundle, evidenceReferences, candidatePackageReferences);
        AddHumanApprovalPackage(request.HumanApprovalPackage, evidenceReferences, candidatePackageReferences);
        AddMemoryPackage(request.MemoryImprovementPackage, evidenceReferences, candidatePackageReferences);
        AddToolPreview(request.ToolRequestGatePreview, evidenceReferences, candidatePackageReferences);
        AddImplementationProposal(request.ImplementationProposal, evidenceReferences, candidatePackageReferences);
        AddCriticReview(request.CriticReviewRequest, evidenceReferences, candidatePackageReferences);
        AddTestFailureReview(request.TestFailureReview, occurrenceReferences, evidenceReferences, candidatePackageReferences);
    }

    private static void AddDogfoodBundle(
        DogfoodEvidenceBundleCandidateResult? bundle,
        List<RepeatedFailureEvidenceReference> evidenceReferences,
        List<RepeatedFailureCandidatePackageReference> candidatePackageReferences)
    {
        if (bundle?.Status != DogfoodEvidenceBundleCandidateStatus.EvidenceBundleProduced || string.IsNullOrWhiteSpace(SafeId(bundle.BundleReferenceId)))
            return;

        evidenceReferences.Add(new RepeatedFailureEvidenceReference
        {
            Kind = RepeatedFailureEvidenceKind.DogfoodEvidenceBundleReference,
            ReferenceId = bundle.BundleReferenceId,
            SafeSummary = "Dogfood evidence bundle supplied as review material."
        });

        candidatePackageReferences.Add(new RepeatedFailureCandidatePackageReference
        {
            Kind = RepeatedFailureCandidatePackageKind.DogfoodEvidenceBundleCandidate,
            ReferenceId = bundle.BundleReferenceId,
            SafeSummary = "Dogfood evidence bundle candidate."
        });
    }

    private static void AddHumanApprovalPackage(
        HumanApprovalPackageCandidateResult? package,
        List<RepeatedFailureEvidenceReference> evidenceReferences,
        List<RepeatedFailureCandidatePackageReference> candidatePackageReferences)
    {
        if (package?.Status != HumanApprovalPackageCandidateStatus.ApprovalPackageProduced || string.IsNullOrWhiteSpace(SafeId(package.PackageReferenceId)))
            return;

        evidenceReferences.Add(new RepeatedFailureEvidenceReference
        {
            Kind = RepeatedFailureEvidenceKind.HumanApprovalPackageReference,
            ReferenceId = package.PackageReferenceId,
            SafeSummary = "Human approval package supplied as review material."
        });

        candidatePackageReferences.Add(new RepeatedFailureCandidatePackageReference
        {
            Kind = RepeatedFailureCandidatePackageKind.HumanApprovalPackageCandidate,
            ReferenceId = package.PackageReferenceId,
            SafeSummary = "Human approval package candidate."
        });
    }

    private static void AddMemoryPackage(
        MemoryImprovementPackageCandidateResult? package,
        List<RepeatedFailureEvidenceReference> evidenceReferences,
        List<RepeatedFailureCandidatePackageReference> candidatePackageReferences)
    {
        if (package?.Status != MemoryImprovementPackageCandidateStatus.MemoryImprovementPackageProduced || string.IsNullOrWhiteSpace(SafeId(package.PackageReferenceId)))
            return;

        evidenceReferences.Add(new RepeatedFailureEvidenceReference
        {
            Kind = RepeatedFailureEvidenceKind.MemoryImprovementPackageReference,
            ReferenceId = package.PackageReferenceId,
            SafeSummary = "Memory improvement package supplied as review material."
        });

        candidatePackageReferences.Add(new RepeatedFailureCandidatePackageReference
        {
            Kind = RepeatedFailureCandidatePackageKind.MemoryImprovementPackageCandidate,
            ReferenceId = package.PackageReferenceId,
            SafeSummary = "Memory improvement package candidate."
        });
    }

    private static void AddToolPreview(
        ToolRequestGatePreviewCandidateResult? preview,
        List<RepeatedFailureEvidenceReference> evidenceReferences,
        List<RepeatedFailureCandidatePackageReference> candidatePackageReferences)
    {
        if (preview?.Status != ToolRequestGatePreviewCandidateStatus.GatePreviewProduced || string.IsNullOrWhiteSpace(SafeId(preview.PreviewPackageReferenceId)))
            return;

        evidenceReferences.Add(new RepeatedFailureEvidenceReference
        {
            Kind = RepeatedFailureEvidenceKind.ToolRequestPreviewReference,
            ReferenceId = preview.PreviewPackageReferenceId,
            SafeSummary = "Tool request gate preview supplied as review material."
        });

        candidatePackageReferences.Add(new RepeatedFailureCandidatePackageReference
        {
            Kind = RepeatedFailureCandidatePackageKind.ToolRequestGatePreviewCandidate,
            ReferenceId = preview.PreviewPackageReferenceId,
            SafeSummary = "Tool request gate preview candidate."
        });
    }

    private static void AddImplementationProposal(
        ImplementationProposalPackageCandidateResult? proposal,
        List<RepeatedFailureEvidenceReference> evidenceReferences,
        List<RepeatedFailureCandidatePackageReference> candidatePackageReferences)
    {
        if (proposal?.Status != ImplementationProposalPackageCandidateStatus.ProposalPackageProduced || string.IsNullOrWhiteSpace(SafeId(proposal.ProposalPackageReferenceId)))
            return;

        evidenceReferences.Add(new RepeatedFailureEvidenceReference
        {
            Kind = RepeatedFailureEvidenceKind.ImplementationProposalReference,
            ReferenceId = proposal.ProposalPackageReferenceId,
            SafeSummary = "Implementation proposal package supplied as review material."
        });

        candidatePackageReferences.Add(new RepeatedFailureCandidatePackageReference
        {
            Kind = RepeatedFailureCandidatePackageKind.ImplementationProposalPackageCandidate,
            ReferenceId = proposal.ProposalPackageReferenceId,
            SafeSummary = "Implementation proposal package candidate."
        });
    }

    private static void AddCriticReview(
        CriticReviewRequestCandidateResult? review,
        List<RepeatedFailureEvidenceReference> evidenceReferences,
        List<RepeatedFailureCandidatePackageReference> candidatePackageReferences)
    {
        if (review?.Status != CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced || string.IsNullOrWhiteSpace(SafeId(review.ReviewPackageReferenceId)))
            return;

        evidenceReferences.Add(new RepeatedFailureEvidenceReference
        {
            Kind = RepeatedFailureEvidenceKind.CriticReviewRequestReference,
            ReferenceId = review.ReviewPackageReferenceId,
            SafeSummary = "Critic review request package supplied as review material."
        });

        candidatePackageReferences.Add(new RepeatedFailureCandidatePackageReference
        {
            Kind = RepeatedFailureCandidatePackageKind.CriticReviewRequestCandidate,
            ReferenceId = review.ReviewPackageReferenceId,
            SafeSummary = "Critic review request candidate."
        });
    }

    private static void AddTestFailureReview(
        TestFailureReviewCandidateResult? review,
        List<RepeatedFailureOccurrenceReference> occurrenceReferences,
        List<RepeatedFailureEvidenceReference> evidenceReferences,
        List<RepeatedFailureCandidatePackageReference> candidatePackageReferences)
    {
        if (review?.Status != TestFailureReviewCandidateStatus.ReviewMaterialProduced || string.IsNullOrWhiteSpace(SafeId(review.ReviewPackageReferenceId)))
            return;

        occurrenceReferences.Add(new RepeatedFailureOccurrenceReference
        {
            Kind = RepeatedFailureOccurrenceKind.TestFailureReviewReference,
            ReferenceId = review.ReviewPackageReferenceId,
            SafeSummary = "Test failure review supplied as occurrence material."
        });

        evidenceReferences.Add(new RepeatedFailureEvidenceReference
        {
            Kind = RepeatedFailureEvidenceKind.TestFailureReviewReference,
            ReferenceId = review.ReviewPackageReferenceId,
            SafeSummary = "Test failure review supplied as review material."
        });

        candidatePackageReferences.Add(new RepeatedFailureCandidatePackageReference
        {
            Kind = RepeatedFailureCandidatePackageKind.TestFailureReviewCandidate,
            ReferenceId = review.ReviewPackageReferenceId,
            SafeSummary = "Test failure review candidate."
        });
    }

    private static IEnumerable<string> MissingEvidenceFor(
        RepeatedFailurePatternReviewCandidateRequest request,
        IReadOnlyList<RepeatedFailureOccurrenceReference> occurrenceReferences,
        IReadOnlyList<RepeatedFailureEvidenceReference> evidenceReferences,
        IReadOnlyList<RepeatedFailureValidationReference> validationReferences,
        IReadOnlyList<RepeatedFailureReviewGateHint> gateHints)
    {
        if (string.IsNullOrWhiteSpace(request.SafePatternTitle))
            yield return "safe pattern title";

        if (string.IsNullOrWhiteSpace(request.SafePatternSummary))
            yield return "safe pattern summary";

        if (request.FrequencyHint == RepeatedFailureFrequencyHint.SingleOccurrenceOnly || occurrenceReferences.Count < 2)
            yield return "at least two supplied occurrence references";

        if (evidenceReferences.Count == 0)
            yield return "repeated failure evidence reference";

        if (validationReferences.Count == 0)
            yield return "validation reference";

        if (gateHints.Count == 0)
            yield return "review gate hint";
    }

    private static IEnumerable<RepeatedFailurePatternReviewCandidateReason> MissingReasonsFor(IEnumerable<string> missingEvidence)
    {
        foreach (var value in missingEvidence)
        {
            yield return value switch
            {
                "safe pattern title" => RepeatedFailurePatternReviewCandidateReason.MissingPatternTitle,
                "safe pattern summary" => RepeatedFailurePatternReviewCandidateReason.MissingPatternSummary,
                "at least two supplied occurrence references" => RepeatedFailurePatternReviewCandidateReason.MissingOccurrenceReference,
                "repeated failure evidence reference" => RepeatedFailurePatternReviewCandidateReason.MissingEvidenceReference,
                "validation reference" => RepeatedFailurePatternReviewCandidateReason.MissingValidationReference,
                "review gate hint" => RepeatedFailurePatternReviewCandidateReason.MissingGateHint,
                _ => RepeatedFailurePatternReviewCandidateReason.MissingEvidenceReference
            };
        }
    }

    private static IReadOnlyList<string> SummaryLines(RepeatedFailurePatternReviewCandidateRequest request) =>
    [
        "Repeated failure pattern review package was produced from supplied references only.",
        "Pattern is not proven.",
        "Root cause is not proven.",
        "History was not queried.",
        "Memory was not queried.",
        "Logs and reports were not read.",
        "Ticket or incident was not created.",
        "Workflow was not transitioned.",
        $"Category hint: {request.CategoryHint}.",
        $"Frequency hint: {request.FrequencyHint}.",
        $"Recency hint: {request.RecencyHint}.",
        $"Confidence hint: {request.ConfidenceHint}.",
        $"Safe pattern title: {SafeText(request.SafePatternTitle)}.",
        $"Safe pattern summary: {SafeText(request.SafePatternSummary)}."
    ];

    private static IReadOnlyList<string> FollowUpReviewQuestions() =>
    [
        "Which supplied occurrences share the same failure shape?",
        "What source-of-truth evidence is still required?",
        "Does the pattern need a critic review before any proposal?",
        "Should memory improvement be proposed later through governed review?"
    ];

    private static RepeatedFailurePatternReviewCandidateReason[] BoundaryReasons() =>
    [
        RepeatedFailurePatternReviewCandidateReason.PatternNotProven,
        RepeatedFailurePatternReviewCandidateReason.RootCauseNotProven,
        RepeatedFailurePatternReviewCandidateReason.HistoryNotQueried,
        RepeatedFailurePatternReviewCandidateReason.MemoryNotQueried,
        RepeatedFailurePatternReviewCandidateReason.LogsNotRead,
        RepeatedFailurePatternReviewCandidateReason.ReportsNotRead,
        RepeatedFailurePatternReviewCandidateReason.TestsNotRun,
        RepeatedFailurePatternReviewCandidateReason.CommandNotRun,
        RepeatedFailurePatternReviewCandidateReason.ToolNotInvoked,
        RepeatedFailurePatternReviewCandidateReason.AgentNotDispatched,
        RepeatedFailurePatternReviewCandidateReason.ModelNotCalled,
        RepeatedFailurePatternReviewCandidateReason.PromptNotBuilt,
        RepeatedFailurePatternReviewCandidateReason.TicketNotCreated,
        RepeatedFailurePatternReviewCandidateReason.IncidentNotCreated,
        RepeatedFailurePatternReviewCandidateReason.MemoryNotPromoted,
        RepeatedFailurePatternReviewCandidateReason.RetrievalNotActivated,
        RepeatedFailurePatternReviewCandidateReason.ApprovalNotSatisfied,
        RepeatedFailurePatternReviewCandidateReason.PolicyNotSatisfied,
        RepeatedFailurePatternReviewCandidateReason.WorkflowNotTransitioned,
        RepeatedFailurePatternReviewCandidateReason.SourceNotMutated,
        RepeatedFailurePatternReviewCandidateReason.PatchNotApplied,
        RepeatedFailurePatternReviewCandidateReason.SqlNotWritten
    ];

    private static RepeatedFailurePatternReviewCandidateResult Result(
        string workflowRunId,
        string workflowStepId,
        string patternReviewReferenceId,
        string projectReferenceId,
        RepeatedFailurePatternReviewCandidateStatus status,
        RepeatedFailurePatternCategoryHint categoryHint,
        RepeatedFailureFrequencyHint frequencyHint,
        RepeatedFailureRecencyHint recencyHint,
        RepeatedFailureConfidenceHint confidenceHint,
        IReadOnlyList<RepeatedFailurePatternReviewCandidateReason> reasons,
        IReadOnlyList<RepeatedFailureOccurrenceReference> occurrenceReferences,
        IReadOnlyList<RepeatedFailureEvidenceReference> evidenceReferences,
        IReadOnlyList<RepeatedFailureValidationReference> validationReferences,
        IReadOnlyList<RepeatedFailureCandidatePackageReference> candidatePackageReferences,
        IReadOnlyList<RepeatedFailureReviewGateHint> gateHints,
        IReadOnlyList<RepeatedFailureRisk> risks,
        IReadOnlyList<string> missingEvidence,
        IReadOnlyList<string> safeSummaryLines,
        IReadOnlyList<string> safeFollowUpReviewQuestions) =>
        new()
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            PatternReviewReferenceId = patternReviewReferenceId,
            PackageReferenceId = PackageReferenceId(workflowRunId, workflowStepId, patternReviewReferenceId),
            ProjectReferenceId = projectReferenceId,
            Status = status,
            CategoryHint = categoryHint,
            FrequencyHint = frequencyHint,
            RecencyHint = recencyHint,
            ConfidenceHint = confidenceHint,
            Reasons = reasons.Concat(BoundaryReasons()).Where(reason => reason != RepeatedFailurePatternReviewCandidateReason.Unknown).Distinct().OrderBy(reason => reason).ToArray(),
            OccurrenceReferences = occurrenceReferences.Where(reference => reference.Kind != RepeatedFailureOccurrenceKind.Unknown).OrderBy(reference => reference.Kind).ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal).ToArray(),
            EvidenceReferences = evidenceReferences.Where(reference => reference.Kind != RepeatedFailureEvidenceKind.Unknown).OrderBy(reference => reference.Kind).ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal).ToArray(),
            ValidationReferences = validationReferences.Where(reference => reference.Kind != RepeatedFailureValidationKind.Unknown).OrderBy(reference => reference.Kind).ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal).ToArray(),
            CandidatePackageReferences = candidatePackageReferences.Where(reference => reference.Kind != RepeatedFailureCandidatePackageKind.Unknown).OrderBy(reference => reference.Kind).ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal).ToArray(),
            GateHints = gateHints.Where(hint => hint.Kind != RepeatedFailureReviewGateKind.Unknown).OrderBy(hint => hint.Kind).ThenByDescending(hint => hint.SeverityHint).ThenBy(hint => hint.SafeSummary, StringComparer.Ordinal).ToArray(),
            Risks = risks.Where(risk => risk.Kind != RepeatedFailureRiskKind.Unknown).OrderBy(risk => risk.Kind).ThenByDescending(risk => risk.SeverityHint).ThenBy(risk => risk.SafeSummary, StringComparer.Ordinal).ToArray(),
            MissingEvidence = missingEvidence.Where(value => !ContainsUnsafeMarker(value)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            SafeSummaryLines = safeSummaryLines.Where(value => !ContainsUnsafeMarker(value)).Select(SafeText).Distinct(StringComparer.Ordinal).ToArray(),
            SafeFollowUpReviewQuestions = safeFollowUpReviewQuestions.Where(value => !ContainsUnsafeMarker(value)).Select(SafeText).Distinct(StringComparer.Ordinal).ToArray(),
            IsReviewOnly = true,
            IsPatternProof = false,
            IsRootCauseProof = false,
            CanQueryHistory = false,
            CanQueryMemory = false,
            CanReadLogs = false,
            CanReadReports = false,
            CanReadTrace = false,
            CanRunTests = false,
            CanRunCommand = false,
            CanInvokeTool = false,
            CanDispatchAgent = false,
            CanCallModel = false,
            CanBuildPrompt = false,
            CanCreateTicket = false,
            CanCreateIncident = false,
            CanPromoteMemory = false,
            CanActivateRetrieval = false,
            CanSatisfyApproval = false,
            CanSatisfyPolicy = false,
            CanTransitionWorkflow = false,
            CanMutateSource = false,
            CanApplyPatch = false,
            CanWriteSql = false
        };

    private static string PackageReferenceId(string workflowRunId, string workflowStepId, string patternReviewReferenceId) =>
        string.IsNullOrWhiteSpace(workflowRunId) || string.IsNullOrWhiteSpace(workflowStepId) || string.IsNullOrWhiteSpace(patternReviewReferenceId)
            ? string.Empty
            : $"repeated-failure-pattern-review:{workflowRunId}:{workflowStepId}:{patternReviewReferenceId}";

    private static IReadOnlyList<RepeatedFailureOccurrenceReference> SafeOccurrenceReferences(IEnumerable<RepeatedFailureOccurrenceReference> references) =>
        references
            .Where(reference => reference.Kind != RepeatedFailureOccurrenceKind.Unknown)
            .Where(reference => !ContainsUnsafeMarker(reference.ReferenceId) && !ContainsUnsafeMarker(reference.SafeSummary))
            .Select(reference => reference with { ReferenceId = SafeText(reference.ReferenceId), SafeSummary = SafeNullableText(reference.SafeSummary) })
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId))
            .Distinct()
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<RepeatedFailureEvidenceReference> SafeEvidenceReferences(IEnumerable<RepeatedFailureEvidenceReference> references) =>
        references
            .Where(reference => reference.Kind != RepeatedFailureEvidenceKind.Unknown)
            .Where(reference => !ContainsUnsafeMarker(reference.ReferenceId) && !ContainsUnsafeMarker(reference.SafeSummary))
            .Select(reference => reference with { ReferenceId = SafeText(reference.ReferenceId), SafeSummary = SafeNullableText(reference.SafeSummary) })
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId))
            .Distinct()
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<RepeatedFailureValidationReference> SafeValidationReferences(IEnumerable<RepeatedFailureValidationReference> references) =>
        references
            .Where(reference => reference.Kind != RepeatedFailureValidationKind.Unknown && reference.OutcomeHint != RepeatedFailureValidationOutcomeHint.Unknown)
            .Where(reference => !ContainsUnsafeMarker(reference.ReferenceId) && !ContainsUnsafeMarker(reference.SafeSummary))
            .Select(reference => reference with { ReferenceId = SafeText(reference.ReferenceId), SafeSummary = SafeNullableText(reference.SafeSummary) })
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId))
            .Distinct()
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<RepeatedFailureCandidatePackageReference> SafeCandidatePackageReferences(IEnumerable<RepeatedFailureCandidatePackageReference> references) =>
        references
            .Where(reference => reference.Kind != RepeatedFailureCandidatePackageKind.Unknown)
            .Where(reference => !ContainsUnsafeMarker(reference.ReferenceId) && !ContainsUnsafeMarker(reference.SafeSummary))
            .Select(reference => reference with { ReferenceId = SafeText(reference.ReferenceId), SafeSummary = SafeNullableText(reference.SafeSummary) })
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferenceId))
            .Distinct()
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<RepeatedFailureReviewGateHint> SafeGateHints(IEnumerable<RepeatedFailureReviewGateHint> hints) =>
        hints
            .Where(hint => hint.Kind != RepeatedFailureReviewGateKind.Unknown)
            .Where(hint => !ContainsUnsafeMarker(hint.SafeSummary))
            .Select(hint => hint with { SafeSummary = SafeNullableText(hint.SafeSummary) })
            .Distinct()
            .OrderBy(hint => hint.Kind)
            .ThenByDescending(hint => hint.SeverityHint)
            .ThenBy(hint => hint.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<RepeatedFailureRisk> SafeRisks(IEnumerable<RepeatedFailureRisk> risks) =>
        risks
            .Where(risk => risk.Kind != RepeatedFailureRiskKind.Unknown)
            .Where(risk => !ContainsUnsafeMarker(risk.SafeSummary))
            .Select(risk => risk with { SafeSummary = SafeNullableText(risk.SafeSummary) })
            .Distinct()
            .OrderBy(risk => risk.Kind)
            .ThenByDescending(risk => risk.SeverityHint)
            .ThenBy(risk => risk.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static bool ContainsUnsafeInput(RepeatedFailurePatternReviewCandidateRequest request) =>
        ContainsUnsafeMarker(request.WorkflowRunId) ||
        ContainsUnsafeMarker(request.WorkflowStepId) ||
        ContainsUnsafeMarker(request.PatternReviewReferenceId) ||
        ContainsUnsafeMarker(request.ProjectReferenceId) ||
        ContainsUnsafeMarker(request.SafePatternTitle) ||
        ContainsUnsafeMarker(request.SafePatternSummary) ||
        ContainsUnsafeMarker(request.CorrelationId) ||
        request.OccurrenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
        request.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
        request.ValidationReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
        request.CandidatePackageReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
        request.GateHints.Any(hint => ContainsUnsafeMarker(hint.SafeSummary)) ||
        request.Risks.Any(risk => ContainsUnsafeMarker(risk.SafeSummary)) ||
        ContainsUnsafeDogfood(request.DogfoodEvidenceBundle) ||
        ContainsUnsafeHumanApproval(request.HumanApprovalPackage) ||
        ContainsUnsafeMemoryPackage(request.MemoryImprovementPackage) ||
        ContainsUnsafeToolPreview(request.ToolRequestGatePreview) ||
        ContainsUnsafeImplementationProposal(request.ImplementationProposal) ||
        ContainsUnsafeCriticReview(request.CriticReviewRequest) ||
        ContainsUnsafeTestFailureReview(request.TestFailureReview) ||
        ContainsUnsafeMarker(request.StepEvaluation?.StepId) ||
        ContainsUnsafeMarker(request.DryRunResult?.WorkflowRunId) ||
        ContainsUnsafeMarker(request.DryRunResult?.WorkflowStepId) ||
        (request.DryRunResult?.SafeReportLines.Any(ContainsUnsafeMarker) ?? false) ||
        ContainsUnsafeMarker(request.RouteSuggestion?.WorkflowRunId) ||
        ContainsUnsafeMarker(request.RouteSuggestion?.WorkflowStepId) ||
        (request.RouteSuggestion?.SourceStatusReferences.Any(ContainsUnsafeMarker) ?? false) ||
        (request.RouteSuggestion?.SafeReportLines.Any(ContainsUnsafeMarker) ?? false);

    private static bool ContainsUnsafeDogfood(DogfoodEvidenceBundleCandidateResult? result) =>
        result is not null &&
        (ContainsUnsafeMarker(result.WorkflowRunId) ||
            ContainsUnsafeMarker(result.WorkflowStepId) ||
            ContainsUnsafeMarker(result.DogfoodEvidenceBundleReferenceId) ||
            ContainsUnsafeMarker(result.BundleReferenceId) ||
            ContainsUnsafeMarker(result.ProjectReferenceId) ||
            result.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.ValidationReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.ArtifactReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.CandidatePackageReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.MissingEvidence.Any(ContainsUnsafeMarker) ||
            result.SafeBundleSummaryLines.Any(ContainsUnsafeMarker));

    private static bool ContainsUnsafeHumanApproval(HumanApprovalPackageCandidateResult? result) =>
        result is not null &&
        (ContainsUnsafeMarker(result.WorkflowRunId) ||
            ContainsUnsafeMarker(result.WorkflowStepId) ||
            ContainsUnsafeMarker(result.ApprovalPackageReferenceId) ||
            ContainsUnsafeMarker(result.PackageReferenceId) ||
            ContainsUnsafeMarker(result.TargetReferenceId) ||
            result.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.CandidatePackageReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.MissingEvidence.Any(ContainsUnsafeMarker) ||
            result.SafePackageSummaryLines.Any(ContainsUnsafeMarker));

    private static bool ContainsUnsafeMemoryPackage(MemoryImprovementPackageCandidateResult? result) =>
        result is not null &&
        (ContainsUnsafeMarker(result.WorkflowRunId) ||
            ContainsUnsafeMarker(result.WorkflowStepId) ||
            ContainsUnsafeMarker(result.MemoryImprovementPackageReferenceId) ||
            ContainsUnsafeMarker(result.PackageReferenceId) ||
            ContainsUnsafeMarker(result.TargetReferenceId) ||
            result.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.SourceOfTruthReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.MissingEvidence.Any(ContainsUnsafeMarker) ||
            result.SafePackageSummaryLines.Any(ContainsUnsafeMarker));

    private static bool ContainsUnsafeToolPreview(ToolRequestGatePreviewCandidateResult? result) =>
        result is not null &&
        (ContainsUnsafeMarker(result.WorkflowRunId) ||
            ContainsUnsafeMarker(result.WorkflowStepId) ||
            ContainsUnsafeMarker(result.ToolRequestPreviewReferenceId) ||
            ContainsUnsafeMarker(result.PreviewPackageReferenceId) ||
            ContainsUnsafeMarker(result.CapabilityName) ||
            result.InputReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
            result.ExpectedOutputReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
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
            result.MissingEvidence.Any(ContainsUnsafeMarker) ||
            result.SafePackageSummaryLines.Any(ContainsUnsafeMarker));

    private static bool ContainsUnsafeCriticReview(CriticReviewRequestCandidateResult? result) =>
        result is not null &&
        (ContainsUnsafeMarker(result.WorkflowRunId) ||
            ContainsUnsafeMarker(result.WorkflowStepId) ||
            ContainsUnsafeMarker(result.ReviewRequestReferenceId) ||
            ContainsUnsafeMarker(result.ReviewPackageReferenceId) ||
            ContainsUnsafeMarker(result.TargetReferenceId) ||
            result.ReviewQuestions.Any(question => ContainsUnsafeMarker(question.SafeQuestion)) ||
            result.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
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

public sealed record RepeatedFailurePatternReviewCandidateRequest
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string PatternReviewReferenceId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public string? SafePatternTitle { get; init; }
    public string? SafePatternSummary { get; init; }
    public RepeatedFailurePatternCategoryHint CategoryHint { get; init; } = RepeatedFailurePatternCategoryHint.Unknown;
    public RepeatedFailureFrequencyHint FrequencyHint { get; init; } = RepeatedFailureFrequencyHint.Unknown;
    public RepeatedFailureRecencyHint RecencyHint { get; init; } = RepeatedFailureRecencyHint.Unknown;
    public RepeatedFailureConfidenceHint ConfidenceHint { get; init; } = RepeatedFailureConfidenceHint.Unknown;
    public IReadOnlyList<RepeatedFailureOccurrenceReference> OccurrenceReferences { get; init; } = [];
    public IReadOnlyList<RepeatedFailureEvidenceReference> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<RepeatedFailureValidationReference> ValidationReferences { get; init; } = [];
    public IReadOnlyList<RepeatedFailureCandidatePackageReference> CandidatePackageReferences { get; init; } = [];
    public IReadOnlyList<RepeatedFailureReviewGateHint> GateHints { get; init; } = [];
    public IReadOnlyList<RepeatedFailureRisk> Risks { get; init; } = [];
    public DogfoodEvidenceBundleCandidateResult? DogfoodEvidenceBundle { get; init; }
    public HumanApprovalPackageCandidateResult? HumanApprovalPackage { get; init; }
    public MemoryImprovementPackageCandidateResult? MemoryImprovementPackage { get; init; }
    public ToolRequestGatePreviewCandidateResult? ToolRequestGatePreview { get; init; }
    public ImplementationProposalPackageCandidateResult? ImplementationProposal { get; init; }
    public CriticReviewRequestCandidateResult? CriticReviewRequest { get; init; }
    public TestFailureReviewCandidateResult? TestFailureReview { get; init; }
    public WorkflowStepRunnerEvaluation? StepEvaluation { get; init; }
    public WorkflowDryRunResult? DryRunResult { get; init; }
    public BoxedLangGraphRouteSuggestion? RouteSuggestion { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record RepeatedFailureOccurrenceReference
{
    public required RepeatedFailureOccurrenceKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record RepeatedFailureEvidenceReference
{
    public required RepeatedFailureEvidenceKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record RepeatedFailureValidationReference
{
    public required RepeatedFailureValidationKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public RepeatedFailureValidationOutcomeHint OutcomeHint { get; init; } = RepeatedFailureValidationOutcomeHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record RepeatedFailureCandidatePackageReference
{
    public required RepeatedFailureCandidatePackageKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record RepeatedFailureReviewGateHint
{
    public required RepeatedFailureReviewGateKind Kind { get; init; }
    public RepeatedFailureSeverityHint SeverityHint { get; init; } = RepeatedFailureSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record RepeatedFailureRisk
{
    public required RepeatedFailureRiskKind Kind { get; init; }
    public RepeatedFailureSeverityHint SeverityHint { get; init; } = RepeatedFailureSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record RepeatedFailurePatternReviewCandidateResult
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string PatternReviewReferenceId { get; init; }
    public required string PackageReferenceId { get; init; }
    public required string ProjectReferenceId { get; init; }
    public required RepeatedFailurePatternReviewCandidateStatus Status { get; init; }
    public required RepeatedFailurePatternCategoryHint CategoryHint { get; init; }
    public required RepeatedFailureFrequencyHint FrequencyHint { get; init; }
    public required RepeatedFailureRecencyHint RecencyHint { get; init; }
    public required RepeatedFailureConfidenceHint ConfidenceHint { get; init; }
    public required IReadOnlyList<RepeatedFailurePatternReviewCandidateReason> Reasons { get; init; }
    public required IReadOnlyList<RepeatedFailureOccurrenceReference> OccurrenceReferences { get; init; }
    public required IReadOnlyList<RepeatedFailureEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<RepeatedFailureValidationReference> ValidationReferences { get; init; }
    public required IReadOnlyList<RepeatedFailureCandidatePackageReference> CandidatePackageReferences { get; init; }
    public required IReadOnlyList<RepeatedFailureReviewGateHint> GateHints { get; init; }
    public required IReadOnlyList<RepeatedFailureRisk> Risks { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required IReadOnlyList<string> SafeSummaryLines { get; init; }
    public required IReadOnlyList<string> SafeFollowUpReviewQuestions { get; init; }
    public required bool IsReviewOnly { get; init; }
    public required bool IsPatternProof { get; init; }
    public required bool IsRootCauseProof { get; init; }
    public required bool CanQueryHistory { get; init; }
    public required bool CanQueryMemory { get; init; }
    public required bool CanReadLogs { get; init; }
    public required bool CanReadReports { get; init; }
    public required bool CanReadTrace { get; init; }
    public required bool CanRunTests { get; init; }
    public required bool CanRunCommand { get; init; }
    public required bool CanInvokeTool { get; init; }
    public required bool CanDispatchAgent { get; init; }
    public required bool CanCallModel { get; init; }
    public required bool CanBuildPrompt { get; init; }
    public required bool CanCreateTicket { get; init; }
    public required bool CanCreateIncident { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanActivateRetrieval { get; init; }
    public required bool CanSatisfyApproval { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanTransitionWorkflow { get; init; }
    public required bool CanMutateSource { get; init; }
    public required bool CanApplyPatch { get; init; }
    public required bool CanWriteSql { get; init; }
}

public enum RepeatedFailurePatternCategoryHint
{
    Unknown = 0,
    RepeatedAssertionFailure = 1,
    RepeatedBuildFailure = 2,
    RepeatedTimeoutOrHang = 3,
    RepeatedEnvironmentOrDependencyFailure = 4,
    RepeatedFixtureOrDataFailure = 5,
    RepeatedPolicyOrApprovalBlock = 6,
    RepeatedWorkflowGateBlock = 7,
    MixedOrUnclearPattern = 8
}

public enum RepeatedFailureFrequencyHint
{
    Unknown = 0,
    SingleOccurrenceOnly = 1,
    TwoOccurrencesSupplied = 2,
    ThreeOrMoreOccurrencesSupplied = 3,
    SuppliedFrequent = 4
}

public enum RepeatedFailureRecencyHint
{
    Unknown = 0,
    SuppliedRecent = 1,
    SuppliedOlder = 2,
    SuppliedMixedRecency = 3
}

public enum RepeatedFailureConfidenceHint
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

public enum RepeatedFailureOccurrenceKind
{
    Unknown = 0,
    TestFailureReviewReference = 1,
    DogfoodEvidenceBundleReference = 2,
    ValidationReference = 3,
    RunReportReference = 4,
    GovernanceEventReference = 5,
    ExternalArtifactReference = 6
}

public enum RepeatedFailureEvidenceKind
{
    Unknown = 0,
    GovernanceEventReference = 1,
    WorkflowStepEvaluationReference = 2,
    DryRunResultReference = 3,
    TestFailureReviewReference = 4,
    CriticReviewRequestReference = 5,
    ImplementationProposalReference = 6,
    ToolRequestPreviewReference = 7,
    MemoryImprovementPackageReference = 8,
    HumanApprovalPackageReference = 9,
    DogfoodEvidenceBundleReference = 10,
    ExternalArtifactReference = 11
}

public enum RepeatedFailureValidationKind
{
    Unknown = 0,
    SuppliedFocusedTestBandReference = 1,
    SuppliedWorkflowSweepReference = 2,
    SuppliedGovernanceSweepReference = 3,
    SuppliedBuildReference = 4,
    SuppliedDogfoodRunReference = 5,
    SuppliedManualReviewReference = 6
}

public enum RepeatedFailureValidationOutcomeHint
{
    Unknown = 0,
    SuppliedPassed = 1,
    SuppliedFailed = 2,
    SuppliedBlocked = 3,
    SuppliedPartial = 4,
    SuppliedNotRun = 5
}

public enum RepeatedFailureCandidatePackageKind
{
    Unknown = 0,
    TestFailureReviewCandidate = 1,
    CriticReviewRequestCandidate = 2,
    ImplementationProposalPackageCandidate = 3,
    ToolRequestGatePreviewCandidate = 4,
    MemoryImprovementPackageCandidate = 5,
    HumanApprovalPackageCandidate = 6,
    DogfoodEvidenceBundleCandidate = 7
}

public enum RepeatedFailureReviewGateKind
{
    Unknown = 0,
    HumanReviewRequired = 1,
    EvidenceRequired = 2,
    SourceOfTruthRequired = 3,
    PatternProofNotClaimed = 4,
    RootCauseProofNotClaimed = 5,
    TicketCreationForbidden = 6,
    MemoryPromotionForbidden = 7,
    WorkflowContinuationForbidden = 8
}

public enum RepeatedFailureRiskKind
{
    Unknown = 0,
    InsufficientEvidence = 1,
    PatternOverclaimRisk = 2,
    RootCauseOverclaimRisk = 3,
    StaleEvidenceRisk = 4,
    MissingOccurrenceEvidence = 5,
    MixedFailureCategoryRisk = 6,
    TicketCreationAuthorityRisk = 7,
    MemoryPromotionAuthorityRisk = 8,
    ReleaseReadinessOverclaimRisk = 9,
    WorkflowContinuationRisk = 10
}

public enum RepeatedFailureSeverityHint
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum RepeatedFailurePatternReviewCandidateStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    BlockedByWorkflowGate = 2,
    MissingRequiredPatternEvidence = 3,
    PatternReviewPackageProduced = 4
}

public enum RepeatedFailurePatternReviewCandidateReason
{
    Unknown = 0,
    ReviewOnly = 1,
    SuppliedEvidenceOnly = 2,
    MissingWorkflowRunId = 3,
    MissingWorkflowStepId = 4,
    MissingPatternReviewReference = 5,
    MissingProjectReference = 6,
    MissingPatternTitle = 7,
    MissingPatternSummary = 8,
    MissingOccurrenceReference = 9,
    MissingEvidenceReference = 10,
    MissingValidationReference = 11,
    MissingGateHint = 12,
    InvalidPatternCategory = 13,
    InvalidFrequencyHint = 14,
    InvalidRecencyHint = 15,
    InvalidConfidenceHint = 16,
    UnsafeInput = 17,
    BlockedByRunnerEvaluation = 18,
    BlockedByDryRun = 19,
    BlockedByRouteSuggestion = 20,
    BlockedByDogfoodEvidenceBundle = 21,
    BlockedByHumanApprovalPackage = 22,
    BlockedByMemoryImprovementPackage = 23,
    BlockedByToolRequestGatePreview = 24,
    BlockedByImplementationProposal = 25,
    BlockedByCriticReviewRequest = 26,
    BlockedByTestFailureReview = 27,
    PatternNotProven = 28,
    RootCauseNotProven = 29,
    HistoryNotQueried = 30,
    MemoryNotQueried = 31,
    LogsNotRead = 32,
    ReportsNotRead = 33,
    TestsNotRun = 34,
    CommandNotRun = 35,
    ToolNotInvoked = 36,
    AgentNotDispatched = 37,
    ModelNotCalled = 38,
    PromptNotBuilt = 39,
    TicketNotCreated = 40,
    IncidentNotCreated = 41,
    MemoryNotPromoted = 42,
    RetrievalNotActivated = 43,
    ApprovalNotSatisfied = 44,
    PolicyNotSatisfied = 45,
    WorkflowNotTransitioned = 46,
    SourceNotMutated = 47,
    PatchNotApplied = 48,
    SqlNotWritten = 49
}
