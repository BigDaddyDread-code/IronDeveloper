using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class TestFailureReviewCandidateWorkflowTests
{
    private readonly ITestFailureReviewCandidateWorkflow _workflow = new TestFailureReviewCandidateWorkflow();

    [TestMethod]
    public void TestFailureReviewCandidate_NullRequestReturnsInvalidRequest()
    {
        var result = _workflow.Review(null);

        Assert.AreEqual(TestFailureReviewCandidateStatus.InvalidRequest, result.Status);
        Assert.AreEqual(TestFailureReviewClassification.Unknown, result.Classification);
        TestFailureReviewCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    [DataRow("workflowRunId")]
    [DataRow("workflowStepId")]
    [DataRow("testRunReferenceId")]
    public void TestFailureReviewCandidate_MissingRequiredIdentityReturnsInvalidRequest(string field)
    {
        var request = field switch
        {
            "workflowRunId" => TestFailureReviewCandidateFixtures.ValidRequest() with { WorkflowRunId = " " },
            "workflowStepId" => TestFailureReviewCandidateFixtures.ValidRequest() with { WorkflowStepId = " " },
            _ => TestFailureReviewCandidateFixtures.ValidRequest() with { TestRunReferenceId = " " }
        };

        var result = _workflow.Review(request);

        Assert.AreEqual(TestFailureReviewCandidateStatus.InvalidRequest, result.Status);
        TestFailureReviewCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    [DataRow("noFailures")]
    [DataRow("noTestName")]
    [DataRow("noErrorSummary")]
    [DataRow("noCommand")]
    [DataRow("noEvidenceReference")]
    public void TestFailureReviewCandidate_MissingEvidenceReturnsMissingRequiredEvidence(string shape)
    {
        var request = shape switch
        {
            "noFailures" => TestFailureReviewCandidateFixtures.ValidRequest() with { Failures = [] },
            "noTestName" => TestFailureReviewCandidateFixtures.ValidRequest() with { Failures = [TestFailureReviewCandidateFixtures.Failure(testName: " ")] },
            "noErrorSummary" => TestFailureReviewCandidateFixtures.ValidRequest() with { Failures = [TestFailureReviewCandidateFixtures.Failure(summary: " ")] },
            "noCommand" => TestFailureReviewCandidateFixtures.ValidRequest() with { TestCommandDisplay = null },
            _ => TestFailureReviewCandidateFixtures.ValidRequest() with { SuppliedEvidenceReferences = [] }
        };

        var result = _workflow.Review(request);

        Assert.AreEqual(TestFailureReviewCandidateStatus.MissingRequiredEvidence, result.Status);
        Assert.IsTrue(result.MissingEvidence.Count > 0);
        TestFailureReviewCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    [DataRow("raw prompt leaked")]
    [DataRow("raw completion leaked")]
    [DataRow("raw tool output leaked")]
    [DataRow("private reasoning leaked")]
    [DataRow("hidden reasoning leaked")]
    [DataRow("chain of thought leaked")]
    [DataRow("whole patch leaked")]
    [DataRow("patch payload leaked")]
    public void TestFailureReviewCandidate_UnsafeInputFailsClosedWithoutEcho(string marker)
    {
        var request = TestFailureReviewCandidateFixtures.ValidRequest() with
        {
            TestCommandDisplay = marker,
            Failures = [TestFailureReviewCandidateFixtures.Failure(summary: marker, stackLines: [marker])]
        };

        var result = _workflow.Review(request);
        var json = JsonSerializer.Serialize(result);

        Assert.AreEqual(TestFailureReviewCandidateStatus.InvalidRequest, result.Status);
        Assert.IsFalse(json.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unsafe marker was echoed: {marker}");
        TestFailureReviewCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_ValidAssertionFailureProducesReviewMaterial()
    {
        var result = _workflow.Review(TestFailureReviewCandidateFixtures.ValidRequest());

        Assert.AreEqual(TestFailureReviewCandidateStatus.ReviewMaterialProduced, result.Status);
        Assert.AreEqual(TestFailureReviewClassification.TestAssertionFailure, result.Classification);
        Assert.IsTrue(result.AffectedTests.Contains("Project.Tests.WidgetTests.FailsWhenExpectedValueDiffers"));
        Assert.IsTrue(result.SafeSummaryLines.Any(line => line.Contains("Classification is advisory", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.SafeNextReviewSuggestions.Any(line => line.Contains("expected and actual", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(TestFailureReviewConfidence.High, result.Confidence);
        TestFailureReviewCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    [DataRow("CS1002 compiler error: ; expected", TestFailureReviewClassification.BuildOrCompilationFailure)]
    [DataRow("Operation timed out while waiting for result", TestFailureReviewClassification.TimeoutOrHang)]
    [DataRow("Missing file from container environment", TestFailureReviewClassification.DependencyOrEnvironmentFailure)]
    [DataRow("Fixture setup data did not include required row", TestFailureReviewClassification.DataOrFixtureFailure)]
    [DataRow("Flaky order dependent failure", TestFailureReviewClassification.FlakyOrOrderDependentFailure)]
    [DataRow("CI runner infrastructure unavailable", TestFailureReviewClassification.InfrastructureFailure)]
    public void TestFailureReviewCandidate_ClassifiesSuppliedEvidenceDeterministically(string summary, TestFailureReviewClassification expected)
    {
        var result = _workflow.Review(TestFailureReviewCandidateFixtures.ValidRequest(summary: summary));

        Assert.AreEqual(expected, result.Classification);
        Assert.AreEqual(TestFailureReviewCandidateStatus.ReviewMaterialProduced, result.Status);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_MultipleFailureCategoriesProduceMixedClassification()
    {
        var request = TestFailureReviewCandidateFixtures.ValidRequest() with
        {
            Failures =
            [
                TestFailureReviewCandidateFixtures.Failure(testName: "assertion-test", summary: "Assert failed. Expected 1 actual 2."),
                TestFailureReviewCandidateFixtures.Failure(testName: "timeout-test", summary: "Timed out waiting for result.")
            ]
        };

        var result = _workflow.Review(request);

        Assert.AreEqual(TestFailureReviewClassification.MixedFailureSet, result.Classification);
        Assert.AreEqual(TestFailureReviewConfidence.Low, result.Confidence);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_ResultDistinguishesAdvisoryClassificationFromRootCause()
    {
        var result = _workflow.Review(TestFailureReviewCandidateFixtures.ValidRequest());
        var json = JsonSerializer.Serialize(result);

        Assert.IsTrue(result.ClassificationIsAdvisory);
        Assert.IsFalse(result.IsRootCauseProof);
        StringAssert.Contains(json, "Classification is advisory");
        StringAssert.Contains(json, "not root-cause proof");
        AssertDoesNotContainAny(json, "root cause found", "fix is ready", "patch is ready");
    }

    [TestMethod]
    public void TestFailureReviewCandidate_BlockingRunnerEvaluationBlocksCandidateReview()
    {
        var result = _workflow.Review(TestFailureReviewCandidateFixtures.ValidRequest() with { StepEvaluation = TestFailureReviewCandidateFixtures.BlockedEvaluation() });

        Assert.AreEqual(TestFailureReviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToArray(), TestFailureReviewCandidateReason.BlockedByRunnerEvaluation);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_BlockingApprovalHaltBlocksCandidateReview()
    {
        var result = _workflow.Review(TestFailureReviewCandidateFixtures.ValidRequest() with { StepEvaluation = TestFailureReviewCandidateFixtures.ApprovalHaltEvaluation() });

        Assert.AreEqual(TestFailureReviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToArray(), TestFailureReviewCandidateReason.BlockedByRunnerEvaluation);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_BlockingPolicyPreflightBlocksCandidateReview()
    {
        var result = _workflow.Review(TestFailureReviewCandidateFixtures.ValidRequest() with { StepEvaluation = TestFailureReviewCandidateFixtures.PolicyBlockedEvaluation() });

        Assert.AreEqual(TestFailureReviewCandidateStatus.BlockedByWorkflowGate, result.Status);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_BlockingA2aValidationBlocksCandidateReview()
    {
        var result = _workflow.Review(TestFailureReviewCandidateFixtures.ValidRequest() with { StepEvaluation = TestFailureReviewCandidateFixtures.A2aBlockedEvaluation() });

        Assert.AreEqual(TestFailureReviewCandidateStatus.BlockedByWorkflowGate, result.Status);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_BlockingDryRunResultBlocksCandidateReview()
    {
        var result = _workflow.Review(TestFailureReviewCandidateFixtures.ValidRequest() with { DryRunResult = TestFailureReviewCandidateFixtures.BlockedDryRun() });

        Assert.AreEqual(TestFailureReviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToArray(), TestFailureReviewCandidateReason.BlockedByDryRun);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_BlockingRouteSuggestionBlocksCandidateReview()
    {
        var result = _workflow.Review(TestFailureReviewCandidateFixtures.ValidRequest() with { RouteSuggestion = TestFailureReviewCandidateFixtures.Route(BoxedLangGraphRouteLabel.BlockedApprovalRequired) });

        Assert.AreEqual(TestFailureReviewCandidateStatus.BlockedByWorkflowGate, result.Status);
        CollectionAssert.Contains(result.Reasons.ToArray(), TestFailureReviewCandidateReason.BlockedByRouteSuggestion);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_EligibleRunnerEvaluationPermitsReviewMaterial()
    {
        var result = _workflow.Review(TestFailureReviewCandidateFixtures.ValidRequest() with { StepEvaluation = TestFailureReviewCandidateFixtures.EligibleEvaluation() });

        Assert.AreEqual(TestFailureReviewCandidateStatus.ReviewMaterialProduced, result.Status);
        TestFailureReviewCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_CompletedDryRunPermitsReviewMaterial()
    {
        var result = _workflow.Review(TestFailureReviewCandidateFixtures.ValidRequest() with { DryRunResult = TestFailureReviewCandidateFixtures.CompletedDryRun() });

        Assert.AreEqual(TestFailureReviewCandidateStatus.ReviewMaterialProduced, result.Status);
        TestFailureReviewCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_AdvisoryRouteLabelAloneDoesNotGrantAuthority()
    {
        var result = _workflow.Review(TestFailureReviewCandidateFixtures.ValidRequest() with
        {
            StepEvaluation = null,
            DryRunResult = null,
            RouteSuggestion = TestFailureReviewCandidateFixtures.Route(BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable)
        });

        Assert.AreEqual(TestFailureReviewCandidateStatus.ReviewMaterialProduced, result.Status);
        CollectionAssert.Contains(result.Reasons.ToArray(), TestFailureReviewCandidateReason.SuppliedEvidenceOnly);
        TestFailureReviewCandidateFixtures.AssertNoAuthority(result);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_SameRequestProducesSameResult()
    {
        var request = TestFailureReviewCandidateFixtures.ValidRequest();
        var first = JsonSerializer.Serialize(_workflow.Review(request));
        var second = JsonSerializer.Serialize(_workflow.Review(request));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_ResultSerializesWithoutUnsafeAuthorityOrPatchMaterial()
    {
        var result = _workflow.Review(TestFailureReviewCandidateFixtures.ValidRequest());
        var json = JsonSerializer.Serialize(result);

        AssertDoesNotContainAny(
            json,
            "private reasoning",
            "hidden reasoning",
            "raw prompt",
            "raw completion",
            "raw tool output",
            "whole patch",
            "patch payload",
            "approval granted",
            "policy satisfied",
            "execution allowed",
            "apply patch",
            "dispatch agent",
            "invoke tool",
            "promote memory",
            "activate retrieval",
            "create ticket now");
    }

    private static void AssertDoesNotContainAny(string text, params string[] forbidden)
    {
        foreach (var value in forbidden)
            Assert.IsFalse(text.Contains(value, StringComparison.OrdinalIgnoreCase), $"Unexpected unsafe text: {value}");
    }
}

internal static class TestFailureReviewCandidateFixtures
{
    public static TestFailureReviewCandidateRequest ValidRequest(string summary = "Assert failed. Expected value 1 but actual value was 2.") =>
        new()
        {
            WorkflowRunId = "workflow-run-127",
            WorkflowStepId = "workflow-step-test-failure-review",
            TestRunReferenceId = "test-run-reference-127",
            TestCommandDisplay = "dotnet test display reference",
            TestFramework = "MSTest",
            ExitCode = 1,
            Failures = [Failure(summary: summary)],
            SafeRunSummaryLines = ["One supplied test failure was recorded."],
            SuppliedEvidenceReferences = ["log-reference-127", "artifact-reference-127"],
            StepEvaluation = EligibleEvaluation(),
            CorrelationId = "correlation-127"
        };

    public static TestFailureEvidenceItem Failure(string testName = "FailsWhenExpectedValueDiffers", string summary = "Assert failed. Expected value 1 but actual value was 2.", IReadOnlyList<string>? stackLines = null) =>
        new()
        {
            TestName = testName,
            FullyQualifiedName = $"Project.Tests.WidgetTests.{testName}",
            SafeErrorSummary = summary,
            SafeStackSummaryLines = stackLines ?? ["Project.Tests.WidgetTests supplied stack summary line."],
            SourceReference = "source-reference-summary-only"
        };

    public static WorkflowStepRunnerEvaluation EligibleEvaluation() =>
        new()
        {
            StepId = "workflow-step-test-failure-review",
            Eligibility = WorkflowStepRunnerEligibility.EligibleForFutureExecution,
            BlockReasons = [WorkflowRunnerBlockReason.RuntimeBoundaryPreventsExecution, WorkflowRunnerBlockReason.RetrievalBoundaryPreventsActivation],
            MissingEvidenceRequirements = [],
            ThoughtLedgerReference = ThoughtLedgerReference(),
            PolicyPreflightStatus = WorkflowStepPolicyPreflightStatus.PolicyEvidencePresentForFutureExecution,
            PolicyBlockReasons = [],
            MissingPolicyRequirements = [],
            A2aHandoffValidationStatus = WorkflowA2aHandoffValidationStatus.ValidForFutureHandoff,
            A2aHandoffBlockReasons = [],
            MissingA2aHandoffEvidence = [],
            ApprovalHaltStatus = WorkflowApprovalHaltStatus.ApprovalEvidencePresentForFutureExecution,
            ApprovalHaltReasons = [],
            MissingApprovalRequirements = [],
            NextRecordableTransition = WorkflowStepContractTransitionKind.ReadyForReviewToReceiptRecorded
        };

    public static WorkflowStepRunnerEvaluation BlockedEvaluation() => EligibleEvaluation() with
    {
        Eligibility = WorkflowStepRunnerEligibility.BlockedMissingEvidence,
        BlockReasons = [WorkflowRunnerBlockReason.MissingRequiredEvidence],
        MissingEvidenceRequirements =
        [
            new WorkflowStepContractEvidenceRequirement
            {
                Kind = WorkflowStepContractEvidenceRequirementKind.ValidationCommandReference,
                RequirementId = "validation-evidence-127",
                SafeSummary = "Validation evidence reference is still missing."
            }
        ]
    };

    public static WorkflowStepRunnerEvaluation PolicyBlockedEvaluation() => EligibleEvaluation() with
    {
        Eligibility = WorkflowStepRunnerEligibility.BlockedByBoundary,
        PolicyPreflightStatus = WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence,
        BlockReasons = [WorkflowRunnerBlockReason.PolicyPreflightMissingEvidence]
    };

    public static WorkflowStepRunnerEvaluation A2aBlockedEvaluation() => EligibleEvaluation() with
    {
        Eligibility = WorkflowStepRunnerEligibility.BlockedByBoundary,
        A2aHandoffValidationStatus = WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence,
        BlockReasons = [WorkflowRunnerBlockReason.A2aHandoffValidationMissingEvidence]
    };

    public static WorkflowStepRunnerEvaluation ApprovalHaltEvaluation() => EligibleEvaluation() with
    {
        Eligibility = WorkflowStepRunnerEligibility.BlockedApprovalRequired,
        ApprovalHaltStatus = WorkflowApprovalHaltStatus.ApprovalRequiredHalt,
        BlockReasons = [WorkflowRunnerBlockReason.ApprovalRequiredHalt]
    };

    public static WorkflowDryRunResult CompletedDryRun() =>
        new()
        {
            WorkflowRunId = "workflow-run-127",
            WorkflowStepId = "workflow-step-test-failure-review",
            ActionKind = WorkflowDryRunActionKind.ReviewMaterialEligibilityPreview,
            Status = WorkflowDryRunStatus.DryRunCompleted,
            BlockReasons = [WorkflowDryRunBlockReason.DryRunCannotApprove, WorkflowDryRunBlockReason.DryRunCannotDispatch, WorkflowDryRunBlockReason.DryRunCannotInvokeTools],
            SafeReportLines = ["Dry-run result is safe review material only."]
        };

    public static WorkflowDryRunResult BlockedDryRun() => CompletedDryRun() with
    {
        Status = WorkflowDryRunStatus.BlockedByApprovalRequiredHalt,
        BlockReasons = [WorkflowDryRunBlockReason.ApprovalRequiredHalt]
    };

    public static BoxedLangGraphRouteSuggestion Route(BoxedLangGraphRouteLabel label, bool authority = false) =>
        new()
        {
            WorkflowRunId = "workflow-run-127",
            WorkflowStepId = "workflow-step-test-failure-review",
            Label = label,
            Reasons = [BoxedLangGraphRouteReason.AdvisoryOnly, BoxedLangGraphRouteReason.AdapterCannotOwnDecisions],
            SourceStatusReferences = ["DryRunStatus:DryRunCompleted"],
            SafeReportLines = ["Adapter output is labels and reasons only."],
            IsAdvisoryOnly = !authority,
            WorkflowDecisionAuthority = authority,
            WorkflowStateChangeAllowed = false,
            StepWorkAllowed = false,
            AgentSendAllowed = false,
            A2aSendAllowed = false,
            ToolUseAllowed = false,
            ApprovalChangeAllowed = false,
            PolicySatisfactionAllowed = false,
            SourceChangeAllowed = false,
            MemoryPromotionAllowed = false,
            RetrievalActivationAllowed = false,
            IsApprovalEvidence = false,
            IsPolicyEvidence = false,
            IsWorkflowTransitionEvidence = false,
            IsDryRunEvidence = false,
            IsA2aValidationEvidence = false,
            IsMemoryPromotionEvidence = false,
            IsRetrievalApprovalEvidence = false
        };

    public static WorkflowStepThoughtLedgerReference ThoughtLedgerReference() =>
        new()
        {
            ThoughtLedgerEntryId = "thought-ledger-entry-127",
            GovernanceEventId = "governance-event-127",
            TraceId = "trace-127",
            CorrelationId = "correlation-127",
            SafeSummary = "Traceability only."
        };

    public static WorkflowStepContract StepContract(string requirementId = "validation-evidence-127", WorkflowStepContractEvidenceRequirementKind requirementKind = WorkflowStepContractEvidenceRequirementKind.ValidationCommandReference) =>
        new()
        {
            StepContractId = "workflow-step-test-failure-review",
            WorkflowRunId = "workflow-run-127",
            Intent = WorkflowStepContractIntent.PrepareReviewMaterial,
            InputReference = new WorkflowStepContractReference
            {
                Kind = WorkflowStepContractReferenceKind.EvidencePackage,
                ReferenceId = "input-evidence-package-127",
                SafeSummary = "Supplied test failure evidence only."
            },
            ExpectedOutputReference = new WorkflowStepContractReference
            {
                Kind = WorkflowStepContractReferenceKind.ReviewMaterial,
                ReferenceId = "expected-review-material-127",
                SafeSummary = "Review material only."
            },
            ExpectedActorKind = WorkflowStepContractActorKind.SystemRecorder,
            AllowedTransitions =
            [
                new WorkflowStepContractTransitionRule
                {
                    Kind = WorkflowStepContractTransitionKind.ReadyForReviewToReceiptRecorded,
                    SafeLabel = "Record review-material receipt."
                }
            ],
            EvidenceRequirements =
            [
                new WorkflowStepContractEvidenceRequirement
                {
                    Kind = requirementKind,
                    RequirementId = requirementId,
                    SafeSummary = "Required evidence reference."
                }
            ],
            ThoughtLedgerReference = ThoughtLedgerReference(),
            Boundary = new WorkflowStepContractBoundary(),
            SafeSummary = "Test failure review candidate step."
        };

    public static TestFailureReviewCandidateResult ValidResult() =>
        new TestFailureReviewCandidateWorkflow().Review(ValidRequest());

    public static void AssertNoAuthority(TestFailureReviewCandidateResult result)
    {
        Assert.IsTrue(result.IsReviewMaterialOnly);
        Assert.IsTrue(result.ClassificationIsAdvisory);
        Assert.IsFalse(result.IsRootCauseProof);
        Assert.IsFalse(result.CanMutateSource);
        Assert.IsFalse(result.CanApplyPatch);
        Assert.IsFalse(result.CanRunTests);
        Assert.IsFalse(result.CanDispatchAgent);
        Assert.IsFalse(result.CanInvokeTool);
        Assert.IsFalse(result.CanCallModel);
        Assert.IsFalse(result.CanCreateTicket);
        Assert.IsFalse(result.CanPromoteMemory);
        Assert.IsFalse(result.CanActivateRetrieval);
        Assert.IsFalse(result.CanSatisfyApproval);
        Assert.IsFalse(result.CanSatisfyPolicy);
        Assert.IsFalse(result.CanTransitionWorkflow);
    }
}
