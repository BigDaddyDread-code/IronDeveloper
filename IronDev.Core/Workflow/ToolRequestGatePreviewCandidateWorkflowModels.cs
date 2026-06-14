namespace IronDev.Core.Workflow;

public interface IToolRequestGatePreviewCandidateWorkflow
{
    ToolRequestGatePreviewCandidateResult Preview(ToolRequestGatePreviewCandidateRequest? request);
}

public sealed class ToolRequestGatePreviewCandidateWorkflow : IToolRequestGatePreviewCandidateWorkflow
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
        "tool was executed",
        "tool executed",
        "capability is authorized",
        "tool authorized",
        "tool reserved",
        "approval granted",
        "approval satisfied",
        "policy satisfied",
        "execution allowed",
        "workflow may continue",
        "workflow continued",
        "run this command",
        "invoke tool",
        "dispatch agent",
        "call model",
        "build prompt",
        "create ticket",
        "ticket created",
        "source mutated",
        "apply this patch",
        "apply patch",
        "patch applied",
        "promote memory",
        "memory promoted",
        "activate retrieval",
        "retrieval activated",
        "release approved"
    ];

    private static readonly string[] UnsafeCapabilityMarkers =
    [
        "dotnet",
        "git ",
        "git-",
        "powershell",
        "cmd.exe",
        "bash",
        " rm ",
        "rm -rf",
        "invoke-tool",
        "invoke tool",
        "execute",
        "approve",
        "applypatch",
        "apply patch",
        "mutatesource",
        "mutate source",
        "authorize",
        "dispatch",
        "prompt",
        "model"
    ];

    public ToolRequestGatePreviewCandidateResult Preview(ToolRequestGatePreviewCandidateRequest? request)
    {
        if (request is null)
            return Result(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                ToolRequestGatePreviewCandidateStatus.InvalidRequest,
                string.Empty,
                [ToolRequestGatePreviewCandidateReason.Unknown],
                [],
                [],
                [],
                [],
                [],
                []);

        var workflowRunId = SafeId(request.WorkflowRunId);
        var workflowStepId = SafeId(request.WorkflowStepId);
        var previewReferenceId = SafeId(request.ToolRequestPreviewReferenceId);
        var capabilityName = SafeCapabilityName(request.CapabilityName);
        var invalidReasons = InvalidReasons(request).ToArray();

        if (invalidReasons.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                previewReferenceId,
                capabilityName,
                ToolRequestGatePreviewCandidateStatus.InvalidRequest,
                capabilityName,
                invalidReasons,
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
                previewReferenceId,
                capabilityName,
                ToolRequestGatePreviewCandidateStatus.BlockedByWorkflowGate,
                capabilityName,
                gateReasons,
                [],
                [],
                [],
                [],
                ["Workflow gate snapshot blocked tool request preview production."],
                []);

        var missingGateMaterial = MissingGateMaterial(request).ToArray();
        if (missingGateMaterial.Length > 0)
            return Result(
                workflowRunId,
                workflowStepId,
                previewReferenceId,
                capabilityName,
                ToolRequestGatePreviewCandidateStatus.MissingRequiredPreviewMaterial,
                capabilityName,
                [
                    ToolRequestGatePreviewCandidateReason.PreviewOnly,
                    ToolRequestGatePreviewCandidateReason.SuppliedEvidenceOnly,
                    .. MissingReasons(request)
                ],
                SafeInputReferences(request),
                SafeOutputReferences(request),
                SafeGateHints(request),
                SafeRisks(request),
                ["Tool request gate preview was not produced because required supplied preview material is missing."],
                missingGateMaterial);

        return Result(
            workflowRunId,
            workflowStepId,
            previewReferenceId,
            capabilityName,
            ToolRequestGatePreviewCandidateStatus.GatePreviewProduced,
            capabilityName,
            BoundaryReasons(),
            SafeInputReferences(request),
            SafeOutputReferences(request),
            SafeGateHints(request),
            SafeRisks(request),
            SummaryLines(request).ToArray(),
            MissingGateMaterialForPreview(request));
    }

    private static IEnumerable<ToolRequestGatePreviewCandidateReason> InvalidReasons(ToolRequestGatePreviewCandidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowRunId))
            yield return ToolRequestGatePreviewCandidateReason.MissingWorkflowRunId;

        if (string.IsNullOrWhiteSpace(request.WorkflowStepId))
            yield return ToolRequestGatePreviewCandidateReason.MissingWorkflowStepId;

        if (string.IsNullOrWhiteSpace(request.ToolRequestPreviewReferenceId))
            yield return ToolRequestGatePreviewCandidateReason.MissingPreviewReference;

        if (string.IsNullOrWhiteSpace(request.CapabilityName))
            yield return ToolRequestGatePreviewCandidateReason.MissingCapabilityName;

        if (!string.IsNullOrWhiteSpace(request.CapabilityName) && IsUnsafeCapabilityName(request.CapabilityName))
            yield return ToolRequestGatePreviewCandidateReason.UnsafeInput;

        if (ContainsUnsafeInput(request))
            yield return ToolRequestGatePreviewCandidateReason.UnsafeInput;
    }

    private static IEnumerable<ToolRequestGatePreviewCandidateReason> GateBlockReasons(ToolRequestGatePreviewCandidateRequest request)
    {
        if (request.StepEvaluation is not null && request.StepEvaluation.Eligibility != WorkflowStepRunnerEligibility.EligibleForFutureExecution)
            yield return ToolRequestGatePreviewCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.PolicyPreflightStatus is WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest or WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence)
            yield return ToolRequestGatePreviewCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.A2aHandoffValidationStatus is WorkflowA2aHandoffValidationStatus.InvalidRequest or WorkflowA2aHandoffValidationStatus.InvalidStepContract or WorkflowA2aHandoffValidationStatus.InvalidHandoffReference or WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence)
            yield return ToolRequestGatePreviewCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.ApprovalHaltStatus == WorkflowApprovalHaltStatus.ApprovalRequiredHalt)
            yield return ToolRequestGatePreviewCandidateReason.BlockedByRunnerEvaluation;

        if (request.DryRunResult is not null && request.DryRunResult.Status != WorkflowDryRunStatus.DryRunCompleted)
            yield return ToolRequestGatePreviewCandidateReason.BlockedByDryRun;

        if (request.RouteSuggestion is not null && RouteSuggestionBlocks(request.RouteSuggestion))
            yield return ToolRequestGatePreviewCandidateReason.BlockedByRouteSuggestion;

        if (request.ImplementationProposal is not null && request.ImplementationProposal.Status != ImplementationProposalPackageCandidateStatus.ProposalPackageProduced)
            yield return ToolRequestGatePreviewCandidateReason.BlockedByImplementationProposal;

        if (request.CriticReviewRequest is not null && request.CriticReviewRequest.Status != CriticReviewRequestCandidateStatus.ReviewRequestPackageProduced)
            yield return ToolRequestGatePreviewCandidateReason.BlockedByCriticReviewRequest;

        if (request.TestFailureReview is not null && request.TestFailureReview.Status != TestFailureReviewCandidateStatus.ReviewMaterialProduced)
            yield return ToolRequestGatePreviewCandidateReason.BlockedByTestFailureReview;
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

    private static IEnumerable<string> MissingGateMaterial(ToolRequestGatePreviewCandidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SafePurposeSummary))
            yield return "safe purpose summary";

        if (request.InputReferences.Count == 0)
            yield return "input reference";

        if (request.ExpectedOutputReferences.Count == 0)
            yield return "expected output reference";

        if (request.GateRequirementHints.Count == 0)
            yield return "gate requirement hint";

        if (RequiresImplementationProposal(request.CapabilityName) && request.ImplementationProposal is null)
            yield return "implementation proposal package result";

        if (RequiresCriticReview(request.CapabilityName) && request.CriticReviewRequest is null)
            yield return "critic review request candidate result";

        if (RequiresTestFailureReview(request.CapabilityName) && request.TestFailureReview is null)
            yield return "test failure review candidate result";
    }

    private static IReadOnlyList<string> MissingGateMaterialForPreview(ToolRequestGatePreviewCandidateRequest request)
    {
        var missing = new List<string>();

        if (!request.GateRequirementHints.Any(hint => hint.Kind == ToolRequestGateKind.ApprovalRequired))
            missing.Add("approval gate evidence remains required later");

        if (!request.GateRequirementHints.Any(hint => hint.Kind == ToolRequestGateKind.PolicyEvidenceRequired))
            missing.Add("policy gate evidence remains required later");

        if (!request.GateRequirementHints.Any(hint => hint.Kind == ToolRequestGateKind.HumanReviewRequired))
            missing.Add("human review remains required later");

        return missing.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<ToolRequestGatePreviewCandidateReason> MissingReasons(ToolRequestGatePreviewCandidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SafePurposeSummary))
            yield return ToolRequestGatePreviewCandidateReason.MissingPurposeSummary;

        if (request.InputReferences.Count == 0)
            yield return ToolRequestGatePreviewCandidateReason.MissingInputReference;

        if (request.ExpectedOutputReferences.Count == 0)
            yield return ToolRequestGatePreviewCandidateReason.MissingExpectedOutputReference;

        if (request.GateRequirementHints.Count == 0)
            yield return ToolRequestGatePreviewCandidateReason.MissingGateRequirementHint;
    }

    private static IReadOnlyList<ToolRequestPreviewInputReference> SafeInputReferences(ToolRequestGatePreviewCandidateRequest request)
    {
        var inputs = request.InputReferences
            .Where(reference => reference.Kind != ToolRequestPreviewInputKind.Unknown)
            .Where(reference => !ContainsUnsafeMarker(reference.ReferenceId) && !ContainsUnsafeMarker(reference.SafeSummary))
            .Select(reference => reference with { ReferenceId = SafeText(reference.ReferenceId), SafeSummary = SafeNullableText(reference.SafeSummary) })
            .ToList();

        if (request.ImplementationProposal is not null && !string.IsNullOrWhiteSpace(request.ImplementationProposal.ProposalPackageReferenceId))
        {
            inputs.Add(new ToolRequestPreviewInputReference
            {
                Kind = ToolRequestPreviewInputKind.ImplementationProposalPackageReference,
                ReferenceId = request.ImplementationProposal.ProposalPackageReferenceId,
                SafeSummary = "Supplied implementation proposal package candidate result."
            });
        }

        if (request.CriticReviewRequest is not null && !string.IsNullOrWhiteSpace(request.CriticReviewRequest.ReviewPackageReferenceId))
        {
            inputs.Add(new ToolRequestPreviewInputReference
            {
                Kind = ToolRequestPreviewInputKind.CriticReviewRequestReference,
                ReferenceId = request.CriticReviewRequest.ReviewPackageReferenceId,
                SafeSummary = "Supplied critic review request candidate result."
            });
        }

        if (request.TestFailureReview is not null && !string.IsNullOrWhiteSpace(request.TestFailureReview.ReviewPackageReferenceId))
        {
            inputs.Add(new ToolRequestPreviewInputReference
            {
                Kind = ToolRequestPreviewInputKind.TestFailureReviewReference,
                ReferenceId = request.TestFailureReview.ReviewPackageReferenceId,
                SafeSummary = "Supplied test failure review candidate result."
            });
        }

        return inputs
            .Distinct()
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ToolRequestPreviewOutputReference> SafeOutputReferences(ToolRequestGatePreviewCandidateRequest request) =>
        request.ExpectedOutputReferences
            .Where(reference => reference.Kind != ToolRequestPreviewOutputKind.Unknown)
            .Where(reference => !ContainsUnsafeMarker(reference.ReferenceId) && !ContainsUnsafeMarker(reference.SafeSummary))
            .Select(reference => reference with { ReferenceId = SafeText(reference.ReferenceId), SafeSummary = SafeNullableText(reference.SafeSummary) })
            .Distinct()
            .OrderBy(reference => reference.Kind)
            .ThenBy(reference => reference.ReferenceId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<ToolRequestGateRequirementHint> SafeGateHints(ToolRequestGatePreviewCandidateRequest request) =>
        request.GateRequirementHints
            .Where(hint => hint.Kind != ToolRequestGateKind.Unknown)
            .Where(hint => !ContainsUnsafeMarker(hint.SafeSummary))
            .Select(hint => hint with { SafeSummary = SafeNullableText(hint.SafeSummary) })
            .Distinct()
            .OrderBy(hint => hint.Kind)
            .ThenByDescending(hint => hint.SeverityHint)
            .ThenBy(hint => hint.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<ToolRequestPreviewRisk> SafeRisks(ToolRequestGatePreviewCandidateRequest request) =>
        request.Risks
            .Where(risk => risk.Kind != ToolRequestPreviewRiskKind.Unknown)
            .Where(risk => !ContainsUnsafeMarker(risk.SafeSummary))
            .Select(risk => risk with { SafeSummary = SafeNullableText(risk.SafeSummary) })
            .Distinct()
            .OrderBy(risk => risk.Kind)
            .ThenByDescending(risk => risk.SeverityHint)
            .ThenBy(risk => risk.SafeSummary, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> SummaryLines(ToolRequestGatePreviewCandidateRequest request)
    {
        yield return "Tool request gate preview was produced from supplied evidence only.";
        yield return "No tool was invoked.";
        yield return "No command was run.";
        yield return "No approval was satisfied.";
        yield return "No policy was satisfied.";
        yield return "Gate requirements are preview material only.";
        yield return "Capability label is not authorization.";
        yield return "Expected output references are not produced output.";
        yield return $"Requested capability label: {SafeText(request.CapabilityName)}.";

        if (!string.IsNullOrWhiteSpace(request.SafePurposeSummary))
            yield return $"Safe purpose: {SafeText(request.SafePurposeSummary)}.";
    }

    private static ToolRequestGatePreviewCandidateReason[] BoundaryReasons() =>
    [
        ToolRequestGatePreviewCandidateReason.PreviewOnly,
        ToolRequestGatePreviewCandidateReason.SuppliedEvidenceOnly,
        ToolRequestGatePreviewCandidateReason.ToolNotInvoked,
        ToolRequestGatePreviewCandidateReason.ToolNotAuthorized,
        ToolRequestGatePreviewCandidateReason.ToolNotReserved,
        ToolRequestGatePreviewCandidateReason.CommandNotRun,
        ToolRequestGatePreviewCandidateReason.ModelNotCalled,
        ToolRequestGatePreviewCandidateReason.PromptNotBuilt,
        ToolRequestGatePreviewCandidateReason.AgentNotDispatched,
        ToolRequestGatePreviewCandidateReason.ApprovalNotSatisfied,
        ToolRequestGatePreviewCandidateReason.PolicyNotSatisfied,
        ToolRequestGatePreviewCandidateReason.WorkflowNotTransitioned,
        ToolRequestGatePreviewCandidateReason.SourceNotMutated,
        ToolRequestGatePreviewCandidateReason.PatchNotApplied,
        ToolRequestGatePreviewCandidateReason.TicketNotCreated,
        ToolRequestGatePreviewCandidateReason.MemoryNotPromoted,
        ToolRequestGatePreviewCandidateReason.RetrievalNotActivated
    ];

    private static ToolRequestGatePreviewCandidateResult Result(
        string workflowRunId,
        string workflowStepId,
        string previewReferenceId,
        string packageSeed,
        ToolRequestGatePreviewCandidateStatus status,
        string capabilityName,
        IReadOnlyList<ToolRequestGatePreviewCandidateReason> reasons,
        IReadOnlyList<ToolRequestPreviewInputReference> inputReferences,
        IReadOnlyList<ToolRequestPreviewOutputReference> expectedOutputReferences,
        IReadOnlyList<ToolRequestGateRequirementHint> gateRequirementHints,
        IReadOnlyList<ToolRequestPreviewRisk> risks,
        IReadOnlyList<string> safePreviewSummaryLines,
        IReadOnlyList<string> missingGateMaterial) =>
        new()
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            ToolRequestPreviewReferenceId = previewReferenceId,
            PreviewPackageReferenceId = PreviewPackageReferenceId(workflowRunId, workflowStepId, previewReferenceId, packageSeed),
            Status = status,
            CapabilityName = capabilityName,
            Reasons = reasons.Concat(BoundaryReasons()).Distinct().OrderBy(reason => reason).ToArray(),
            InputReferences = inputReferences,
            ExpectedOutputReferences = expectedOutputReferences,
            GateRequirementHints = gateRequirementHints,
            Risks = risks,
            MissingGateMaterial = missingGateMaterial.Where(value => !ContainsUnsafeMarker(value)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            SafePreviewSummaryLines = safePreviewSummaryLines.Where(value => !ContainsUnsafeMarker(value)).Select(SafeText).ToArray(),
            IsPreviewOnly = true,
            IsToolExecution = false,
            CanInvokeTool = false,
            CanAuthorizeTool = false,
            CanReserveTool = false,
            CanRunCommand = false,
            CanCallModel = false,
            CanBuildPrompt = false,
            CanDispatchAgent = false,
            CanSatisfyApproval = false,
            CanSatisfyPolicy = false,
            CanTransitionWorkflow = false,
            CanMutateSource = false,
            CanApplyPatch = false,
            CanCreateTicket = false,
            CanPromoteMemory = false,
            CanActivateRetrieval = false
        };

    private static string PreviewPackageReferenceId(string workflowRunId, string workflowStepId, string previewReferenceId, string packageSeed) =>
        string.IsNullOrWhiteSpace(workflowRunId) || string.IsNullOrWhiteSpace(workflowStepId) || string.IsNullOrWhiteSpace(previewReferenceId) || string.IsNullOrWhiteSpace(packageSeed)
            ? string.Empty
            : $"tool-request-gate-preview:{workflowRunId}:{workflowStepId}:{previewReferenceId}:{packageSeed}";

    private static bool ContainsUnsafeInput(ToolRequestGatePreviewCandidateRequest request) =>
        ContainsUnsafeMarker(request.WorkflowRunId) ||
        ContainsUnsafeMarker(request.WorkflowStepId) ||
        ContainsUnsafeMarker(request.ToolRequestPreviewReferenceId) ||
        ContainsUnsafeMarker(request.SafePurposeSummary) ||
        ContainsUnsafeMarker(request.CorrelationId) ||
        request.InputReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
        request.ExpectedOutputReferences.Any(reference => ContainsUnsafeMarker(reference.ReferenceId) || ContainsUnsafeMarker(reference.SafeSummary)) ||
        request.GateRequirementHints.Any(hint => ContainsUnsafeMarker(hint.SafeSummary)) ||
        request.Risks.Any(risk => ContainsUnsafeMarker(risk.SafeSummary)) ||
        ContainsUnsafeImplementationProposal(request.ImplementationProposal) ||
        ContainsUnsafeCriticReviewRequest(request.CriticReviewRequest) ||
        ContainsUnsafeTestFailureReview(request.TestFailureReview) ||
        ContainsUnsafeMarker(request.StepEvaluation?.StepId) ||
        ContainsUnsafeMarker(request.DryRunResult?.WorkflowRunId) ||
        ContainsUnsafeMarker(request.DryRunResult?.WorkflowStepId) ||
        ContainsUnsafeMarker(request.RouteSuggestion?.WorkflowRunId) ||
        ContainsUnsafeMarker(request.RouteSuggestion?.WorkflowStepId) ||
        (request.RouteSuggestion?.SafeReportLines.Any(ContainsUnsafeMarker) ?? false) ||
        (request.DryRunResult?.SafeReportLines.Any(ContainsUnsafeMarker) ?? false);

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

    private static bool RequiresImplementationProposal(string? capabilityName) =>
        !string.IsNullOrWhiteSpace(capabilityName) && capabilityName.Contains("implementation", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresCriticReview(string? capabilityName) =>
        !string.IsNullOrWhiteSpace(capabilityName) && capabilityName.Contains("critic", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresTestFailureReview(string? capabilityName) =>
        !string.IsNullOrWhiteSpace(capabilityName) && capabilityName.Contains("test.failure", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnsafeCapabilityName(string value)
    {
        if (ContainsUnsafeMarker(value))
            return true;

        var trimmed = value.Trim();
        if (trimmed.Contains("..", StringComparison.Ordinal) ||
            trimmed.Contains('/', StringComparison.Ordinal) ||
            trimmed.Contains('\\', StringComparison.Ordinal) ||
            trimmed.Contains('|', StringComparison.Ordinal) ||
            trimmed.Contains('&', StringComparison.Ordinal) ||
            trimmed.Contains(';', StringComparison.Ordinal) ||
            trimmed.Contains('`', StringComparison.Ordinal) ||
            trimmed.Contains('$', StringComparison.Ordinal) ||
            trimmed.Contains('>', StringComparison.Ordinal) ||
            trimmed.Contains('<', StringComparison.Ordinal))
            return true;

        if (trimmed.Any(char.IsWhiteSpace))
            return true;

        return UnsafeCapabilityMarkers.Any(marker => trimmed.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string SafeCapabilityName(string? value) =>
        string.IsNullOrWhiteSpace(value) || IsUnsafeCapabilityName(value) ? string.Empty : value.Trim();

    private static string SafeId(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? string.Empty : value.Trim();

    private static string SafeText(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? string.Empty : value.Trim();

    private static string? SafeNullableText(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? null : value.Trim();

    private static bool ContainsUnsafeMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}

public sealed record ToolRequestGatePreviewCandidateRequest
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string ToolRequestPreviewReferenceId { get; init; }
    public required string CapabilityName { get; init; }
    public string? SafePurposeSummary { get; init; }
    public IReadOnlyList<ToolRequestPreviewInputReference> InputReferences { get; init; } = [];
    public IReadOnlyList<ToolRequestPreviewOutputReference> ExpectedOutputReferences { get; init; } = [];
    public IReadOnlyList<ToolRequestGateRequirementHint> GateRequirementHints { get; init; } = [];
    public IReadOnlyList<ToolRequestPreviewRisk> Risks { get; init; } = [];
    public ImplementationProposalPackageCandidateResult? ImplementationProposal { get; init; }
    public CriticReviewRequestCandidateResult? CriticReviewRequest { get; init; }
    public TestFailureReviewCandidateResult? TestFailureReview { get; init; }
    public WorkflowStepRunnerEvaluation? StepEvaluation { get; init; }
    public WorkflowDryRunResult? DryRunResult { get; init; }
    public BoxedLangGraphRouteSuggestion? RouteSuggestion { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record ToolRequestPreviewInputReference
{
    public required ToolRequestPreviewInputKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record ToolRequestPreviewOutputReference
{
    public required ToolRequestPreviewOutputKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record ToolRequestGateRequirementHint
{
    public required ToolRequestGateKind Kind { get; init; }
    public ToolRequestGateSeverityHint SeverityHint { get; init; } = ToolRequestGateSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record ToolRequestPreviewRisk
{
    public required ToolRequestPreviewRiskKind Kind { get; init; }
    public ToolRequestGateSeverityHint SeverityHint { get; init; } = ToolRequestGateSeverityHint.Unknown;
    public string? SafeSummary { get; init; }
}

public sealed record ToolRequestGatePreviewCandidateResult
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string ToolRequestPreviewReferenceId { get; init; }
    public required string PreviewPackageReferenceId { get; init; }
    public required ToolRequestGatePreviewCandidateStatus Status { get; init; }
    public required string CapabilityName { get; init; }
    public required IReadOnlyList<ToolRequestGatePreviewCandidateReason> Reasons { get; init; }
    public required IReadOnlyList<ToolRequestPreviewInputReference> InputReferences { get; init; }
    public required IReadOnlyList<ToolRequestPreviewOutputReference> ExpectedOutputReferences { get; init; }
    public required IReadOnlyList<ToolRequestGateRequirementHint> GateRequirementHints { get; init; }
    public required IReadOnlyList<ToolRequestPreviewRisk> Risks { get; init; }
    public required IReadOnlyList<string> MissingGateMaterial { get; init; }
    public required IReadOnlyList<string> SafePreviewSummaryLines { get; init; }
    public required bool IsPreviewOnly { get; init; }
    public required bool IsToolExecution { get; init; }
    public required bool CanInvokeTool { get; init; }
    public required bool CanAuthorizeTool { get; init; }
    public required bool CanReserveTool { get; init; }
    public required bool CanRunCommand { get; init; }
    public required bool CanCallModel { get; init; }
    public required bool CanBuildPrompt { get; init; }
    public required bool CanDispatchAgent { get; init; }
    public required bool CanSatisfyApproval { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanTransitionWorkflow { get; init; }
    public required bool CanMutateSource { get; init; }
    public required bool CanApplyPatch { get; init; }
    public required bool CanCreateTicket { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanActivateRetrieval { get; init; }
}

public enum ToolRequestPreviewInputKind
{
    Unknown = 0,
    ImplementationProposalPackageReference = 1,
    CriticReviewRequestReference = 2,
    TestFailureReviewReference = 3,
    WorkflowStepEvaluationReference = 4,
    DryRunResultReference = 5,
    A2aValidationReference = 6,
    ApprovalHaltReference = 7,
    ExternalArtifactReference = 8
}

public enum ToolRequestPreviewOutputKind
{
    Unknown = 0,
    ReviewMaterialReference = 1,
    EvidenceReference = 2,
    DiagnosticReference = 3,
    ValidationReportReference = 4,
    DiffPreviewReference = 5,
    ExternalArtifactReference = 6
}

public enum ToolRequestGateKind
{
    Unknown = 0,
    ApprovalRequired = 1,
    PolicyEvidenceRequired = 2,
    A2aValidationRequired = 3,
    ThoughtLedgerReferenceRequired = 4,
    DryRunRequired = 5,
    SourceMutationForbidden = 6,
    ToolExecutionForbidden = 7,
    HumanReviewRequired = 8,
    MissingEvidence = 9
}

public enum ToolRequestGateSeverityHint
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum ToolRequestPreviewRiskKind
{
    Unknown = 0,
    InsufficientEvidence = 1,
    UnsafeCapabilityName = 2,
    ToolExecutionRisk = 3,
    SourceMutationRisk = 4,
    PolicyRisk = 5,
    ApprovalRisk = 6,
    PromptRisk = 7,
    ModelCallRisk = 8,
    OverclaimRisk = 9
}

public enum ToolRequestGatePreviewCandidateStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    BlockedByWorkflowGate = 2,
    MissingRequiredPreviewMaterial = 3,
    GatePreviewProduced = 4
}

public enum ToolRequestGatePreviewCandidateReason
{
    Unknown = 0,
    PreviewOnly = 1,
    SuppliedEvidenceOnly = 2,
    MissingWorkflowRunId = 3,
    MissingWorkflowStepId = 4,
    MissingPreviewReference = 5,
    MissingCapabilityName = 6,
    MissingPurposeSummary = 7,
    MissingInputReference = 8,
    MissingExpectedOutputReference = 9,
    MissingGateRequirementHint = 10,
    UnsafeInput = 11,
    BlockedByRunnerEvaluation = 12,
    BlockedByDryRun = 13,
    BlockedByRouteSuggestion = 14,
    BlockedByImplementationProposal = 15,
    BlockedByCriticReviewRequest = 16,
    BlockedByTestFailureReview = 17,
    ToolNotInvoked = 18,
    ToolNotAuthorized = 19,
    ToolNotReserved = 20,
    CommandNotRun = 21,
    ModelNotCalled = 22,
    PromptNotBuilt = 23,
    AgentNotDispatched = 24,
    ApprovalNotSatisfied = 25,
    PolicyNotSatisfied = 26,
    WorkflowNotTransitioned = 27,
    SourceNotMutated = 28,
    PatchNotApplied = 29,
    TicketNotCreated = 30,
    MemoryNotPromoted = 31,
    RetrievalNotActivated = 32
}
