namespace IronDev.Core.Workflow;

public interface ITestFailureReviewCandidateWorkflow
{
    TestFailureReviewCandidateResult Review(TestFailureReviewCandidateRequest? request);
}

public sealed class TestFailureReviewCandidateWorkflow : ITestFailureReviewCandidateWorkflow
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
        "approval granted",
        "approval satisfied",
        "execution allowed",
        "run tool",
        "dispatch agent",
        "invoke tool",
        "tool executed",
        "source mutated",
        "apply patch",
        "patch applied",
        "policy satisfied",
        "promote memory",
        "memory promoted",
        "retrieval activated",
        "activate retrieval",
        "release approved",
        "workflow continued",
        "workflow started",
        "root cause found",
        "fix is ready",
        "ticket created"
    ];

    public TestFailureReviewCandidateResult Review(TestFailureReviewCandidateRequest? request)
    {
        if (request is null)
            return Result(string.Empty, string.Empty, string.Empty, TestFailureReviewCandidateStatus.InvalidRequest, TestFailureReviewClassification.Unknown, [TestFailureReviewCandidateReason.Unknown], [], [], [], [], TestFailureReviewConfidence.Unknown);

        var reasons = new List<TestFailureReviewCandidateReason>();
        var workflowRunId = SafeId(request.WorkflowRunId);
        var workflowStepId = SafeId(request.WorkflowStepId);
        var testRunReferenceId = SafeId(request.TestRunReferenceId);

        if (string.IsNullOrWhiteSpace(request.WorkflowRunId))
            reasons.Add(TestFailureReviewCandidateReason.MissingWorkflowRunId);

        if (string.IsNullOrWhiteSpace(request.WorkflowStepId))
            reasons.Add(TestFailureReviewCandidateReason.MissingWorkflowStepId);

        if (string.IsNullOrWhiteSpace(request.TestRunReferenceId))
            reasons.Add(TestFailureReviewCandidateReason.MissingTestRunReference);

        if (ContainsUnsafeInput(request))
            reasons.Add(TestFailureReviewCandidateReason.UnsafeInput);

        if (reasons.Count > 0)
            return Result(workflowRunId, workflowStepId, testRunReferenceId, TestFailureReviewCandidateStatus.InvalidRequest, TestFailureReviewClassification.Unknown, reasons, [], [], [], [], TestFailureReviewConfidence.Unknown);

        var gateBlockReasons = GateBlockReasons(request).ToArray();
        if (gateBlockReasons.Length > 0)
            return Result(workflowRunId, workflowStepId, testRunReferenceId, TestFailureReviewCandidateStatus.BlockedByWorkflowGate, TestFailureReviewClassification.Unknown, gateBlockReasons, [], ["Workflow gate snapshot blocked review material production."], [], [], TestFailureReviewConfidence.Unknown);

        var missingEvidence = MissingEvidence(request).ToArray();
        if (missingEvidence.Length > 0)
        {
            var missingReasons = new List<TestFailureReviewCandidateReason>
            {
                TestFailureReviewCandidateReason.ReviewMaterialOnly,
                TestFailureReviewCandidateReason.SuppliedEvidenceOnly,
                TestFailureReviewCandidateReason.MissingFailureEvidence,
                TestFailureReviewCandidateReason.ClassificationIsAdvisory,
                TestFailureReviewCandidateReason.NoMutationPerformed
            };

            return Result(
                workflowRunId,
                workflowStepId,
                testRunReferenceId,
                TestFailureReviewCandidateStatus.MissingRequiredEvidence,
                request.Failures.Count == 0 ? TestFailureReviewClassification.NoFailureEvidenceSupplied : TestFailureReviewClassification.Unknown,
                missingReasons,
                AffectedTests(request),
                ["Review material was not produced because required supplied evidence is missing."],
                missingEvidence,
                ["Collect the missing evidence references before implementation planning."],
                TestFailureReviewConfidence.Low);
        }

        var classification = Classify(request.Failures);
        var summaryLines = SummaryLines(request, classification).ToArray();
        var suggestions = SuggestionsFor(classification, missingEvidence).ToArray();
        var reviewReasons = BoundaryReasons().ToList();

        if (request.StepEvaluation is null && request.DryRunResult is null)
            reviewReasons.Add(TestFailureReviewCandidateReason.SuppliedEvidenceOnly);

        return Result(
            workflowRunId,
            workflowStepId,
            testRunReferenceId,
            TestFailureReviewCandidateStatus.ReviewMaterialProduced,
            classification,
            reviewReasons,
            AffectedTests(request),
            summaryLines,
            [],
            suggestions,
            ConfidenceFor(classification, request));
    }

    private static IEnumerable<TestFailureReviewCandidateReason> GateBlockReasons(TestFailureReviewCandidateRequest request)
    {
        if (request.StepEvaluation is not null && request.StepEvaluation.Eligibility != WorkflowStepRunnerEligibility.EligibleForFutureExecution)
            yield return TestFailureReviewCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.PolicyPreflightStatus is WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest or WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence)
            yield return TestFailureReviewCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.A2aHandoffValidationStatus is WorkflowA2aHandoffValidationStatus.InvalidRequest or WorkflowA2aHandoffValidationStatus.InvalidStepContract or WorkflowA2aHandoffValidationStatus.InvalidHandoffReference or WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence)
            yield return TestFailureReviewCandidateReason.BlockedByRunnerEvaluation;

        if (request.StepEvaluation?.ApprovalHaltStatus == WorkflowApprovalHaltStatus.ApprovalRequiredHalt)
            yield return TestFailureReviewCandidateReason.BlockedByRunnerEvaluation;

        if (request.DryRunResult is not null && request.DryRunResult.Status != WorkflowDryRunStatus.DryRunCompleted)
            yield return TestFailureReviewCandidateReason.BlockedByDryRun;

        if (request.RouteSuggestion is not null && RouteSuggestionBlocks(request.RouteSuggestion))
            yield return TestFailureReviewCandidateReason.BlockedByRouteSuggestion;
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

    private static IEnumerable<string> MissingEvidence(TestFailureReviewCandidateRequest request)
    {
        if (request.Failures.Count == 0)
            yield return "failure evidence";

        if (request.Failures.Count == 0 || request.Failures.All(failure => string.IsNullOrWhiteSpace(failure.TestName)))
            yield return "failed test name";

        if (request.Failures.Count == 0 || request.Failures.All(failure => string.IsNullOrWhiteSpace(failure.SafeErrorSummary)))
            yield return "safe error summary";

        if (string.IsNullOrWhiteSpace(request.TestCommandDisplay))
            yield return "test command display";

        if (request.SuppliedEvidenceReferences.Count == 0)
            yield return "supplied log or artifact reference";
    }

    private static TestFailureReviewClassification Classify(IReadOnlyList<TestFailureEvidenceItem> failures)
    {
        if (failures.Count == 0)
            return TestFailureReviewClassification.NoFailureEvidenceSupplied;

        var categories = failures
            .Select(ClassifyFailure)
            .Where(category => category != TestFailureReviewClassification.Unknown)
            .Distinct()
            .ToArray();

        if (categories.Length == 0)
            return TestFailureReviewClassification.Unknown;

        return categories.Length == 1 ? categories[0] : TestFailureReviewClassification.MixedFailureSet;
    }

    private static TestFailureReviewClassification ClassifyFailure(TestFailureEvidenceItem failure)
    {
        var text = string.Join(" ", new[] { failure.SafeErrorSummary }.Concat(failure.SafeStackSummaryLines ?? [])).ToLowerInvariant();

        if (text.Contains("cs0", StringComparison.Ordinal) || text.Contains("compiler", StringComparison.Ordinal) || text.Contains("compilation", StringComparison.Ordinal) || text.Contains("build failed", StringComparison.Ordinal))
            return TestFailureReviewClassification.BuildOrCompilationFailure;

        if (text.Contains("timeout", StringComparison.Ordinal) || text.Contains("timed out", StringComparison.Ordinal) || text.Contains("hang", StringComparison.Ordinal) || text.Contains("cancelled", StringComparison.Ordinal) || text.Contains("canceled", StringComparison.Ordinal))
            return TestFailureReviewClassification.TimeoutOrHang;

        if (text.Contains("infrastructure", StringComparison.Ordinal) || text.Contains("ci runner", StringComparison.Ordinal) || text.Contains("hosted agent", StringComparison.Ordinal))
            return TestFailureReviewClassification.InfrastructureFailure;

        if (text.Contains("missing file", StringComparison.Ordinal) || text.Contains("environment", StringComparison.Ordinal) || text.Contains("connection", StringComparison.Ordinal) || text.Contains("container", StringComparison.Ordinal) || text.Contains("dependency", StringComparison.Ordinal) || text.Contains("unavailable", StringComparison.Ordinal))
            return TestFailureReviewClassification.DependencyOrEnvironmentFailure;

        if (text.Contains("fixture", StringComparison.Ordinal) || text.Contains("setup data", StringComparison.Ordinal) || text.Contains("seed data", StringComparison.Ordinal) || text.Contains("test data", StringComparison.Ordinal))
            return TestFailureReviewClassification.DataOrFixtureFailure;

        if (text.Contains("flaky", StringComparison.Ordinal) || text.Contains("order dependent", StringComparison.Ordinal) || text.Contains("intermittent", StringComparison.Ordinal))
            return TestFailureReviewClassification.FlakyOrOrderDependentFailure;

        if (text.Contains("assert", StringComparison.Ordinal) || text.Contains("expected", StringComparison.Ordinal) || text.Contains("actual", StringComparison.Ordinal))
            return TestFailureReviewClassification.TestAssertionFailure;

        return TestFailureReviewClassification.Unknown;
    }

    private static IEnumerable<string> SummaryLines(TestFailureReviewCandidateRequest request, TestFailureReviewClassification classification)
    {
        yield return "Test failure review material was produced from supplied evidence only.";
        yield return $"Advisory classification: {classification}.";
        yield return "Classification is advisory and is not root-cause proof.";
        yield return "No tests were run.";
        yield return "No repository files were inspected.";
        yield return "No source changes were proposed or applied.";

        foreach (var failure in request.Failures.Where(failure => !string.IsNullOrWhiteSpace(failure.SafeErrorSummary)).Take(3))
            yield return $"Supplied failure summary for {failure.TestName.Trim()}: {failure.SafeErrorSummary!.Trim()}";
    }

    private static IEnumerable<string> SuggestionsFor(TestFailureReviewClassification classification, IReadOnlyList<string> missingEvidence)
    {
        yield return "Review the supplied failure summary before implementation planning.";

        if (missingEvidence.Count > 0)
            yield return "Collect the missing command or log reference before implementation planning.";

        switch (classification)
        {
            case TestFailureReviewClassification.TestAssertionFailure:
                yield return "Compare expected and actual values in the supplied summary.";
                yield return "Check whether fixture or setup data changed.";
                break;
            case TestFailureReviewClassification.BuildOrCompilationFailure:
                yield return "Review the supplied compiler or build summary before code planning.";
                break;
            case TestFailureReviewClassification.TimeoutOrHang:
                yield return "Check whether the supplied failure appears timing-dependent.";
                break;
            case TestFailureReviewClassification.DependencyOrEnvironmentFailure:
                yield return "Check whether the supplied failure appears environment-dependent.";
                break;
            case TestFailureReviewClassification.MixedFailureSet:
                yield return "Separate the supplied failures by category before implementation planning.";
                break;
            default:
                yield return "Keep the classification provisional until more evidence is supplied.";
                break;
        }
    }

    private static IReadOnlyList<string> AffectedTests(TestFailureReviewCandidateRequest request) =>
        request.Failures
            .Select(failure => string.IsNullOrWhiteSpace(failure.FullyQualifiedName) ? failure.TestName : failure.FullyQualifiedName)
            .Where(value => !string.IsNullOrWhiteSpace(value) && !ContainsUnsafeMarker(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static TestFailureReviewConfidence ConfidenceFor(TestFailureReviewClassification classification, TestFailureReviewCandidateRequest request)
    {
        if (classification == TestFailureReviewClassification.Unknown || classification == TestFailureReviewClassification.MixedFailureSet)
            return TestFailureReviewConfidence.Low;

        var hasCommand = !string.IsNullOrWhiteSpace(request.TestCommandDisplay);
        var hasEvidenceReference = request.SuppliedEvidenceReferences.Count > 0;
        var hasErrorSummary = request.Failures.Any(failure => !string.IsNullOrWhiteSpace(failure.SafeErrorSummary));

        if (hasCommand && hasEvidenceReference && hasErrorSummary)
            return TestFailureReviewConfidence.High;

        return hasErrorSummary ? TestFailureReviewConfidence.Medium : TestFailureReviewConfidence.Low;
    }

    private static TestFailureReviewCandidateReason[] BoundaryReasons() =>
    [
        TestFailureReviewCandidateReason.ReviewMaterialOnly,
        TestFailureReviewCandidateReason.SuppliedEvidenceOnly,
        TestFailureReviewCandidateReason.ClassificationIsAdvisory,
        TestFailureReviewCandidateReason.ClassificationIsNotRootCauseProof,
        TestFailureReviewCandidateReason.NoMutationPerformed,
        TestFailureReviewCandidateReason.CannotRunTests,
        TestFailureReviewCandidateReason.CannotInvokeTools,
        TestFailureReviewCandidateReason.CannotDispatchAgents,
        TestFailureReviewCandidateReason.CannotCallModels,
        TestFailureReviewCandidateReason.CannotMutateSource,
        TestFailureReviewCandidateReason.CannotApplyPatch,
        TestFailureReviewCandidateReason.CannotCreateTicket,
        TestFailureReviewCandidateReason.CannotPromoteMemory,
        TestFailureReviewCandidateReason.CannotActivateRetrieval,
        TestFailureReviewCandidateReason.CannotSatisfyApproval,
        TestFailureReviewCandidateReason.CannotSatisfyPolicy,
        TestFailureReviewCandidateReason.CannotTransitionWorkflow
    ];

    private static TestFailureReviewCandidateResult Result(
        string workflowRunId,
        string workflowStepId,
        string testRunReferenceId,
        TestFailureReviewCandidateStatus status,
        TestFailureReviewClassification classification,
        IReadOnlyList<TestFailureReviewCandidateReason> reasons,
        IReadOnlyList<string> affectedTests,
        IReadOnlyList<string> safeSummaryLines,
        IReadOnlyList<string> missingEvidence,
        IReadOnlyList<string> suggestions,
        TestFailureReviewConfidence confidence) =>
        new()
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            TestRunReferenceId = testRunReferenceId,
            Status = status,
            Classification = classification,
            Reasons = reasons.Concat(BoundaryReasons()).Distinct().OrderBy(reason => reason).ToArray(),
            AffectedTests = affectedTests.Where(value => !ContainsUnsafeMarker(value)).ToArray(),
            SafeSummaryLines = safeSummaryLines.Where(value => !ContainsUnsafeMarker(value)).ToArray(),
            MissingEvidence = missingEvidence.Where(value => !ContainsUnsafeMarker(value)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            SafeNextReviewSuggestions = suggestions.Where(value => !ContainsUnsafeMarker(value)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Confidence = confidence,
            ReviewPackageReferenceId = ReviewPackageReferenceId(workflowRunId, workflowStepId, testRunReferenceId),
            IsReviewMaterialOnly = true,
            ClassificationIsAdvisory = true,
            IsRootCauseProof = false,
            CanMutateSource = false,
            CanApplyPatch = false,
            CanRunTests = false,
            CanDispatchAgent = false,
            CanInvokeTool = false,
            CanCallModel = false,
            CanCreateTicket = false,
            CanPromoteMemory = false,
            CanActivateRetrieval = false,
            CanSatisfyApproval = false,
            CanSatisfyPolicy = false,
            CanTransitionWorkflow = false
        };

    private static string ReviewPackageReferenceId(string workflowRunId, string workflowStepId, string testRunReferenceId) =>
        string.IsNullOrWhiteSpace(workflowRunId) || string.IsNullOrWhiteSpace(workflowStepId) || string.IsNullOrWhiteSpace(testRunReferenceId)
            ? string.Empty
            : $"test-failure-review:{workflowRunId}:{workflowStepId}:{testRunReferenceId}";

    private static bool ContainsUnsafeInput(TestFailureReviewCandidateRequest request) =>
        ContainsUnsafeMarker(request.WorkflowRunId) ||
        ContainsUnsafeMarker(request.WorkflowStepId) ||
        ContainsUnsafeMarker(request.TestRunReferenceId) ||
        ContainsUnsafeMarker(request.TestCommandDisplay) ||
        ContainsUnsafeMarker(request.TestFramework) ||
        ContainsUnsafeMarker(request.CorrelationId) ||
        request.SafeRunSummaryLines.Any(ContainsUnsafeMarker) ||
        request.SuppliedEvidenceReferences.Any(ContainsUnsafeMarker) ||
        request.Failures.Any(ContainsUnsafeFailure) ||
        ContainsUnsafeMarker(request.StepEvaluation?.StepId) ||
        ContainsUnsafeMarker(request.DryRunResult?.WorkflowRunId) ||
        ContainsUnsafeMarker(request.DryRunResult?.WorkflowStepId) ||
        ContainsUnsafeMarker(request.RouteSuggestion?.WorkflowRunId) ||
        ContainsUnsafeMarker(request.RouteSuggestion?.WorkflowStepId) ||
        (request.RouteSuggestion?.SafeReportLines.Any(ContainsUnsafeMarker) ?? false) ||
        (request.DryRunResult?.SafeReportLines.Any(ContainsUnsafeMarker) ?? false);

    private static bool ContainsUnsafeFailure(TestFailureEvidenceItem failure) =>
        ContainsUnsafeMarker(failure.TestName) ||
        ContainsUnsafeMarker(failure.FullyQualifiedName) ||
        ContainsUnsafeMarker(failure.SafeErrorSummary) ||
        ContainsUnsafeMarker(failure.SourceReference) ||
        failure.SafeStackSummaryLines.Any(ContainsUnsafeMarker);

    private static string SafeId(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? string.Empty : value.Trim();

    private static bool ContainsUnsafeMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) && UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}

public sealed record TestFailureReviewCandidateRequest
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string TestRunReferenceId { get; init; }
    public string? TestCommandDisplay { get; init; }
    public string? TestFramework { get; init; }
    public int? ExitCode { get; init; }
    public IReadOnlyList<TestFailureEvidenceItem> Failures { get; init; } = [];
    public IReadOnlyList<string> SafeRunSummaryLines { get; init; } = [];
    public IReadOnlyList<string> SuppliedEvidenceReferences { get; init; } = [];
    public WorkflowStepRunnerEvaluation? StepEvaluation { get; init; }
    public WorkflowDryRunResult? DryRunResult { get; init; }
    public BoxedLangGraphRouteSuggestion? RouteSuggestion { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record TestFailureEvidenceItem
{
    public required string TestName { get; init; }
    public string? FullyQualifiedName { get; init; }
    public string? SafeErrorSummary { get; init; }
    public IReadOnlyList<string> SafeStackSummaryLines { get; init; } = [];
    public string? SourceReference { get; init; }
}

public sealed record TestFailureReviewCandidateResult
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string TestRunReferenceId { get; init; }
    public required TestFailureReviewCandidateStatus Status { get; init; }
    public required TestFailureReviewClassification Classification { get; init; }
    public required IReadOnlyList<TestFailureReviewCandidateReason> Reasons { get; init; }
    public required IReadOnlyList<string> AffectedTests { get; init; }
    public required IReadOnlyList<string> SafeSummaryLines { get; init; }
    public required IReadOnlyList<string> MissingEvidence { get; init; }
    public required IReadOnlyList<string> SafeNextReviewSuggestions { get; init; }
    public required TestFailureReviewConfidence Confidence { get; init; }
    public required string ReviewPackageReferenceId { get; init; }
    public required bool IsReviewMaterialOnly { get; init; }
    public required bool ClassificationIsAdvisory { get; init; }
    public required bool IsRootCauseProof { get; init; }
    public required bool CanMutateSource { get; init; }
    public required bool CanApplyPatch { get; init; }
    public required bool CanRunTests { get; init; }
    public required bool CanDispatchAgent { get; init; }
    public required bool CanInvokeTool { get; init; }
    public required bool CanCallModel { get; init; }
    public required bool CanCreateTicket { get; init; }
    public required bool CanPromoteMemory { get; init; }
    public required bool CanActivateRetrieval { get; init; }
    public required bool CanSatisfyApproval { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanTransitionWorkflow { get; init; }
}

public enum TestFailureReviewCandidateStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    BlockedByWorkflowGate = 2,
    MissingRequiredEvidence = 3,
    ReviewMaterialProduced = 4
}

public enum TestFailureReviewClassification
{
    Unknown = 0,
    NoFailureEvidenceSupplied = 1,
    TestAssertionFailure = 2,
    BuildOrCompilationFailure = 3,
    DependencyOrEnvironmentFailure = 4,
    TimeoutOrHang = 5,
    DataOrFixtureFailure = 6,
    FlakyOrOrderDependentFailure = 7,
    InfrastructureFailure = 8,
    MixedFailureSet = 9
}

public enum TestFailureReviewConfidence
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

public enum TestFailureReviewCandidateReason
{
    Unknown = 0,
    ReviewMaterialOnly = 1,
    SuppliedEvidenceOnly = 2,
    MissingWorkflowRunId = 3,
    MissingWorkflowStepId = 4,
    MissingTestRunReference = 5,
    MissingFailureEvidence = 6,
    BlockedByRunnerEvaluation = 7,
    BlockedByDryRun = 8,
    BlockedByRouteSuggestion = 9,
    UnsafeInput = 10,
    ClassificationIsAdvisory = 11,
    NoMutationPerformed = 12,
    CannotRunTests = 13,
    CannotInvokeTools = 14,
    CannotDispatchAgents = 15,
    CannotMutateSource = 16,
    CannotApplyPatch = 17,
    CannotCreateTicket = 18,
    CannotPromoteMemory = 19,
    CannotActivateRetrieval = 20,
    CannotCallModels = 21,
    ClassificationIsNotRootCauseProof = 22,
    CannotSatisfyApproval = 23,
    CannotSatisfyPolicy = 24,
    CannotTransitionWorkflow = 25
}
