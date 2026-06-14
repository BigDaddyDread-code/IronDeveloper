namespace IronDev.Core.Workflow;

public interface ICriticReviewRequestCandidateWorkflow
{
    CriticReviewRequestCandidateResult Prepare(CriticReviewRequestCandidateRequest? request);
}

public sealed class CriticReviewRequestCandidateWorkflow : ICriticReviewRequestCandidateWorkflow
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
        "raw log",
        "full log",
        "critic approved",
        "critic rejected",
        "approval granted",
        "approval satisfied",
        "execution allowed",
        "policy satisfied",
        "workflow may continue",
        "workflow continued",
        "dispatch critic",
        "dispatch agent",
        "call model",
        "model reviewed",
        "build prompt",
        "post comment",
        "github comment",
        "review comment posted",
        "create ticket",
        "ticket created",
        "source mutated",
        "apply patch",
        "patch applied",
        "patch should be applied",
        "promote memory",
        "memory promoted",
        "activate retrieval",
        "retrieval activated",
        "root cause confirmed",
        "root cause found",
        "fix is ready",
        "release approved"
    ];

    public CriticReviewRequestCandidateResult Prepare(CriticReviewRequestCandidateRequest? request)
    {
        if (request is null)
            return Result(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                CriticReviewRequestCandidateStatus.InvalidRequest,
                CriticReviewTargetKind.Unknown,
                string.Empty,
                [CriticReviewRequestCandidateReason.Unknown],
                [],
                [],
                [],
                [],
                []);

        var workflowRunId = SafeId(request.WorkflowRunId);
        var workflowStepId = SafeId(request.WorkflowStepId);
        var reviewRequestReferenceId = SafeId(request.ReviewRequestReferenceId);
        var targetReferenceId = SafeId(request.TargetReferenceId);
        var invalidReasons = InvalidReasons(request).ToArray();

        if (invalidReasons.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                reviewRequestReferenceId,
                targetReferenceId,
                CriticReviewRequestCandidateStatus.InvalidRequest,
                request.TargetKind,
                targetReferenceId,
                invalidReasons,
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
                reviewRequestReferenceId,
                targetReferenceId,
                CriticReviewRequestCandidateStatus.BlockedByWorkflowGate,
                request.TargetKind,
                targetReferenceId,
                gateReasons,
                [],
                [],
                [],
                ["Workflow gate snapshot blocked critic review request packaging."],
                []);

        var missingEvidence = MissingEvidence(request).ToArray();
        if (missingEvidence.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                reviewRequestReferenceId,
                targetReferenceId,
                CriticReviewRequestCandidateStatus.MissingRequiredReviewMaterial,
                request.TargetKind,
                targetReferenceId,
                [
                    CriticReviewRequestCandidateReason.ReviewRequestOnly,
                    CriticReviewRequestCandidateReason.SuppliedEvidenceOnly,
                    ..MissingReasons(request)
                ],
                SafeQuestions(request),
                SafeEvidence(request),
                SafeRisks(request),
                ["Critic review request package was not produced because required supplied review material is missing."],
                missingEvidence);

        return Result(
            workflowRunId,
            workflowStepId,
            reviewRequestReferenceId,
            targetReferenceId,
            CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced,
            request.TargetKind,
            targetReferenceId,
            BoundaryReasons(),
            SafeQuestions(request),
            SafeEvidence(request),
            SafeRisks(request),
            SummaryLines(request).ToArray(),
            []);
    }

    private static IEnumerable<CriticReviewRequestCandidateReason> InvalidReasons(CriticReviewRequestCandidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowRunId))
            yield return CriticReviewRequestCandidateReason.MissingWorkflowRunId;

        if (string.IsNullOrWhiteSpace(request.WorkflowStepId))
            yield return CriticReviewRequestCandidateReason.MissingWorkflowStepId;

        if (string.IsNullOrWhiteSpace(request.ReviewRequestReferenceId))
            yield return CriticReviewRequestCandidateReason.MissingReviewRequestReference;

        if (request.TargetKind == CriticReviewTargetKind.Unknown)
            yield return CriticReviewRequestCandidateReason.InvalidTargetKind;

        if (string.IsNullOrWhiteSpace(request.TargetReferenceId))
            yield return CriticReviewRequestCandidateReason.MissingTargetReference;

        if (ContainsUnsafeInput(request))
            yield return CriticReviewRequestCandidateReason.UnsafeInput;
    }

    private static IEnumerable<CriticReviewRequestCandidateReason> GateBlockReasons(CriticReviewRequestCandidateRequest request)
    {
        if (request.StepEvaluation is not null && request.StepEvaluation.Eligibility != WorkflowStepRunnerEligibility.EligibleForFutureExecution)
            yield return CriticReviewRequestCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.PolicyPreflightStatus is WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest or WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence)
            yield return CriticReviewRequestCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.A2aHandoffValidationStatus is WorkflowA2aHandoffValidationStatus.InvalidRequest or WorkflowA2aHandoffValidationStatus.InvalidStepContract or WorkflowA2aHandoffValidationStatus.InvalidHandoffReference or WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence)
            yield return CriticReviewRequestCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.ApprovalHaltStatus == WorkflowApprovalHaltStatus.ApprovalRequiredHalt)
            yield return CriticReviewRequestCandidateReason.BlockedByRunnerEvaluation;

        if (request.DryRunResult is not null && request.DryRunResult.Status != WorkflowDryRunStatus.DryRunCompleted)
            yield return CriticReviewRequestCandidateReason.BlockedByDryRun;

        if (request.RouteSuggestion is not null && RouteSuggestionBlocks(request.RouteSuggestion))
            yield return CriticReviewRequestCandidateReason.BlockedByRouteSuggestion;

        if (request.TestFailureReview is not null && request.TestFailureReview.Status != TestFailureReviewCandidateStatus.ReviewMaterialProduced)
            yield return CriticReviewRequestCandidateReason.BlockedByRunnerEvaluation;
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

    private static IEnumerable<string> MissingEvidence(CriticReviewRequestCandidateRequest request)
    {
        if (request.ReviewQuestions.Count == 0)
            yield return "review question";

        if (request.EvidenceReferences.Count == 0)
            yield return "evidence reference";

        if (string.IsNullOrWhiteSpace(request.SafeSummary))
            yield return "safe target summary";

        if (request.TargetKind == CriticReviewTargetKind.TestFailureReviewCandidate && request.TestFailureReview is null)
            yield return "test failure review candidate result";
    }

    private static IEnumerable<CriticReviewRequestCandidateReason> MissingReasons(CriticReviewRequestCandidateRequest request)
    {
        if (request.ReviewQuestions.Count == 0)
            yield return CriticReviewRequestCandidateReason.MissingReviewQuestion;

        if (request.EvidenceReferences.Count == 0)
            yield return CriticReviewRequestCandidateReason.MissingEvidenceReference;
    }

    private static IReadOnlyList<CriticReviewQuestion> SafeQuestions(CriticReviewRequestCandidateRequest request) =>
        request.ReviewQuestions
            .Where(question => question.Kind != CriticReviewQuestionKind.Unknown)
            .Where(question => !ContainsUnsafeMarker(question.SafeQuestion))
            .Select(question => question with { SafeQuestion = SafeText(question.SafeQuestion) })
            .Distinct()
            .OrderBy(question => question.Kind)
            .ThenByDescending(question => question.SeverityHint)
            .ThenBy(question => question.SafeQuestion, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<CriticReviewEvidenceReference> SafeEvidence(CriticReviewRequestCandidateRequest request)
    {
        var evidence = request.EvidenceReferences
            .Where(reference => reference.Kind != CriticReviewEvidenceKind.Unknown)
            .Where(reference => !ContainsUnsafeMarker(reference.ReferenceId) && !ContainsUnsafeMarker(reference.SafeSummary))
            .Select(reference => reference with { ReferenceId = SafeText(reference.ReferenceId), SafeSummary = SafeNullableText(reference.SafeSummary) })
            .ToList();

        if (request.TestFailureReview is not null && !string.IsNullOrWhiteSpace(request.TestFailureReview.ReviewPackageReferenceId))
        {
            evidence.Add(new CriticReviewEvidenceReference
            {
                Kind = CriticReviewEvidenceKind.TestFailureReviewReference,
                ReferenceId = request.TestFailureReview.ReviewPackageReferenceId,
                SafeSummary = "Supplied test failure review candidate result."
            });
        }

        return evidence
            .Distinct()
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<CriticReviewRiskHint> SafeRisks(CriticReviewRequestCandidateRequest request) =>
        request.RiskHints
            .Where(risk => risk.Kind != CriticReviewRiskKind.Unknown)
            .Where(risk => !ContainsUnsafeMarker(risk.SafeSummary))
            .Select(risk => risk with { SafeSummary = SafeNullableText(risk.SafeSummary) })
            .Distinct()
            .OrderBy(risk => risk.Kind)
            .ThenByDescending(risk => risk.SeverityHint)
            .ThenBy(risk => risk.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> SummaryLines(CriticReviewRequestCandidateRequest request)
    {
        yield return "Critic review request package was produced from supplied evidence only.";
        yield return "No CriticAgent was dispatched.";
        yield return "No model was called.";
        yield return "No prompt was built.";
        yield return "No review decision was made.";
        yield return "No source mutation was performed.";
        yield return "Review questions are advisory request material.";
        yield return $"Review target kind: {request.TargetKind}.";
        yield return $"Review target reference: {SafeText(request.TargetReferenceId)}.";

        if (!string.IsNullOrWhiteSpace(request.SafeTitle))
            yield return $"Safe title: {SafeText(request.SafeTitle)}.";

        foreach (var line in request.SafeContextLines.Where(line => !ContainsUnsafeMarker(line)).Select(SafeText).Take(3))
            yield return $"Supplied safe context: {line}";
    }

    private static CriticReviewRequestCandidateReason[] BoundaryReasons() =>
    [
        CriticReviewRequestCandidateReason.ReviewRequestOnly,
        CriticReviewRequestCandidateReason.SuppliedEvidenceOnly,
        CriticReviewRequestCandidateReason.CriticNotDispatched,
        CriticReviewRequestCandidateReason.ModelNotCalled,
        CriticReviewRequestCandidateReason.PromptNotBuilt,
        CriticReviewRequestCandidateReason.NoReviewDecisionMade,
        CriticReviewRequestCandidateReason.NoMutationPerformed,
        CriticReviewRequestCandidateReason.CannotApprove,
        CriticReviewRequestCandidateReason.CannotReject,
        CriticReviewRequestCandidateReason.CannotSatisfyPolicy,
        CriticReviewRequestCandidateReason.CannotTransitionWorkflow,
        CriticReviewRequestCandidateReason.CannotMutateSource,
        CriticReviewRequestCandidateReason.CannotCreateTicket,
        CriticReviewRequestCandidateReason.CannotPromoteMemory,
        CriticReviewRequestCandidateReason.CannotActivateRetrieval
    ];

    private static CriticReviewRequestCandidateResult Result(
        string workflowRunId,
        string workflowStepId,
        string reviewRequestReferenceId,
        string reviewPackageSeed,
        CriticReviewRequestCandidateStatus status,
        CriticReviewTargetKind targetKind,
        string targetReferenceId,
        IReadOnlyList<CriticReviewRequestCandidateReason> reasons,
        IReadOnlyList<CriticReviewQuestion> reviewQuestions,
        IReadOnlyList<CriticReviewEvidenceReference> evidenceReferences,
        IReadOnlyList<CriticReviewRiskHint> riskHints,
        IReadOnlyList<string> safePackageSummaryLines,
        IReadOnlyList<string> missingEvidence) =>
        new()
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            ReviewRequestReferenceId = reviewRequestReferenceId,
            ReviewPackageReferenceId = ReviewPackageReferenceId(workflowRunId, workflowStepId, reviewRequestReferenceId, reviewPackageSeed),
            Status = status,
            TargetKind = targetKind,
            TargetReferenceId = targetReferenceId,
            Reasons = reasons.Concat(BoundaryReasons()).Distinct().OrderBy(reason => reason).ToArray(),
            ReviewQuestions = reviewQuestions,
            EvidenceReferences = evidenceReferences,
            RiskHints = riskHints,
            MissingEvidence = missingEvidence.Where(value => !ContainsUnsafeMarker(value)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            SafePackageSummaryLines = safePackageSummaryLines.Where(value => !ContainsUnsafeMarker(value)).Select(SafeText).ToArray(),
            IsReviewRequestOnly = true,
            IsReviewDecision = false,
            CanDispatchCriticAgent = false,
            CanCallModel = false,
            CanBuildPrompt = false,
            CanPostReviewComment = false,
            CanApprove = false,
            CanReject = false,
            CanSatisfyPolicy = false,
            CanTransitionWorkflow = false,
            CanMutateSource = false,
            CanCreateTicket = false,
            CanPromoteMemory = false,
            CanActivateRetrieval = false
        };

    private static string ReviewPackageReferenceId(string workflowRunId, string workflowStepId, string reviewRequestReferenceId, string targetReferenceId) =>
        string.IsNullOrWhiteSpace(workflowRunId) || string.IsNullOrWhiteSpace(workflowStepId) || string.IsNullOrWhiteSpace(reviewRequestReferenceId) || string.IsNullOrWhiteSpace(targetReferenceId)
            ? string.Empty
            : $"critic-review-request:{workflowRunId}:{workflowStepId}:{reviewRequestReferenceId}:{targetReferenceId}";

    private static bool ContainsUnsafeInput(CriticReviewRequestCandidateRequest request) =>
        ContainsUnsafeMarker(request.WorkflowRunId) ||
        ContainsUnsafeMarker(request.WorkflowStepId) ||
        ContainsUnsafeMarker(request.ReviewRequestReferenceId) ||
        ContainsUnsafeMarker(request.TargetReferenceId) ||
        ContainsUnsafeMarker(request.SafeTitle) ||
        ContainsUnsafeMarker(request.SafeSummary) ||
        ContainsUnsafeMarker(request.CorrelationId) ||
        request.SafeContextLines.Any(ContainsUnsafeMarker) ||
        request.ReviewQuestions.Any(question => ContainsUnsafeMarker(question.SafeQuestion)) ||
        request.EvidenceReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
        request.RiskHints.Any(risk => ContainsUnsafeMarker(risk.SafeSummary)) ||
        ContainsUnsafeTestFailureReview(request.TestFailureReview) ||
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

    private static string SafeId(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? string.Empty : value.Trim();

    private static string SafeText(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? string.Empty : value.Trim();

    private static string? SafeNullableText(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? null : value.Trim();

    private static bool ContainsUnsafeMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}

public sealed record CriticReviewRequestCandidateRequest
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string ReviewRequestReferenceId { get; init; }
    public required CriticReviewTargetKind TargetKind { get; init; }
    public required string TargetReferenceId { get; init; }
    public string? SafeTitle { get; init; }
    public string? SafeSummary { get; init; }
    public IReadOnlyList<CriticReviewQuestion> ReviewQuestions { get; init; } = [];
    public IReadOnlyList<CriticReviewEvidenceReference> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<CriticReviewRiskHint> RiskHints { get; init; } = [];
    public IReadOnlyList<string> SafeContextLines { get; init; } = [];
    public TestFailureReviewCandidateResult? TestFailureReview { get; init; }
    public WorkflowStepRunnerEvaluation? StepEvaluation { get; init; }
    public WorkflowDryRunResult? DryRunResult { get; init; }
    public BoxedLangGraphRouteSuggestion? RouteSuggestion { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record CriticReviewQuestion
{
    public required CriticReviewQuestionKind Kind { get; init; }
    public required string SafeQuestion { get; init; }
    public CriticReviewSeverityHint SeverityHint { get; init; } = CriticReviewSeverityHint.Unknown;
}

public sealed record CriticReviewEvidenceReference
{
    public required CriticReviewEvidenceKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record CriticReviewRiskHint
{
    public required CriticReviewRiskKind Kind { get; init; }
    public CriticReviewSeverityHint SeverityHint { get; init; } = CriticReviewSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record CriticReviewRequestCandidateResult
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string ReviewRequestReferenceId { get; init; }
    public required string ReviewPackageReferenceId { get; init; }
    public required CriticReviewRequestCandidateStatus Status { get; init; }
    public required CriticReviewTargetKind TargetKind { get; init; }
    public required string TargetReferenceId { get; init; }
    public required IReadOnlyList<CriticReviewRequestCandidateReason> Reasons { get; init; }
    public required IReadOnlyList<CriticReviewQuestion> ReviewQuestions { get; init; }
    public required IReadOnlyList<CriticReviewEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<CriticReviewRiskHint> RiskHints { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required IReadOnlyList<string> SafePackageSummaryLines { get; init; }
    public required bool IsReviewRequestOnly { get; init; }
    public required bool IsReviewDecision { get; init; }
    public required bool CanDispatchCriticAgent { get; init; }
    public required bool CanCallModel { get; init; }
    public required bool CanBuildPrompt { get; init; }
    public required bool CanPostReviewComment { get; init; }
    public required bool CanApprove { get; init; }
    public required bool CanReject { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanTransitionWorkflow { get; init; }
    public required bool CanMutateSource { get; init; }
    public required bool CanCreateTicket { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanActivateRetrieval { get; init; }
}

public enum CriticReviewTargetKind
{
    Unknown = 0,
    TestFailureReviewCandidate = 1,
    ImplementationProposal = 2,
    ToolRequestPreview = 3,
    MemoryProposalReview = 4,
    ApprovalPackageReview = 5,
    DogfoodEvidenceBundle = 6,
    FailurePatternReview = 7
}

public enum CriticReviewQuestionKind
{
    Unknown = 0,
    CorrectnessRisk = 1,
    BoundaryRisk = 2,
    EvidenceSufficiency = 3,
    MissingEvidence = 4,
    ArchitectureRisk = 5,
    TestRisk = 6,
    SecurityRisk = 7,
    ReleaseRisk = 8,
    OverclaimRisk = 9
}

public enum CriticReviewEvidenceKind
{
    Unknown = 0,
    GovernanceEventReference = 1,
    WorkflowStepEvaluationReference = 2,
    DryRunResultReference = 3,
    A2aValidationReference = 4,
    ApprovalHaltReference = 5,
    TestFailureReviewReference = 6,
    BoxedRouteSuggestionReference = 7,
    ExternalArtifactReference = 8
}

public enum CriticReviewRiskKind
{
    Unknown = 0,
    InsufficientEvidence = 1,
    UnsafeBoundaryClaim = 2,
    PossibleOverclaim = 3,
    MissingApproval = 4,
    MissingPolicyEvidence = 5,
    MissingA2aValidation = 6,
    SourceMutationRisk = 7,
    MemoryPromotionRisk = 8,
    RetrievalAuthorityRisk = 9,
    TestEvidenceRisk = 10,
    ReleaseReadinessRisk = 11
}

public enum CriticReviewSeverityHint
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum CriticReviewRequestCandidateStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    BlockedByWorkflowGate = 2,
    MissingRequiredReviewMaterial = 3,
    ReviewRequestPackageProduced = 4
}

public enum CriticReviewRequestCandidateReason
{
    Unknown = 0,
    ReviewRequestOnly = 1,
    SuppliedEvidenceOnly = 2,
    MissingWorkflowRunId = 3,
    MissingWorkflowStepId = 4,
    MissingReviewRequestReference = 5,
    MissingTargetReference = 6,
    MissingReviewQuestion = 7,
    MissingEvidenceReference = 8,
    InvalidTargetKind = 9,
    UnsafeInput = 10,
    BlockedByRunnerEvaluation = 11,
    BlockedByDryRun = 12,
    BlockedByRouteSuggestion = 13,
    CriticNotDispatched = 14,
    ModelNotCalled = 15,
    PromptNotBuilt = 16,
    NoReviewDecisionMade = 17,
    NoMutationPerformed = 18,
    CannotApprove = 19,
    CannotReject = 20,
    CannotSatisfyPolicy = 21,
    CannotTransitionWorkflow = 22,
    CannotMutateSource = 23,
    CannotCreateTicket = 24,
    CannotPromoteMemory = 25,
    CannotActivateRetrieval = 26
}
