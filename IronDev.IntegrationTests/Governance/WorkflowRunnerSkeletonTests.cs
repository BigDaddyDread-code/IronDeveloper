using System.Reflection;
using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowRunnerSkeleton")]
public sealed class WorkflowRunnerSkeletonTests
{
    private readonly WorkflowRunnerSkeleton _runner = new();

    [TestMethod]
    public void WorkflowRunnerSkeleton_MissingWorkflowRunIdReturnsInvalidRequest()
    {
        var result = _runner.Evaluate(new WorkflowRunnerEvaluationRequest
        {
            WorkflowRunId = " ",
            StepContracts = [ValidStep()],
            AvailableEvidence = [Evidence()]
        });

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.InvalidRequest, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowRunnerBlockReason.MissingWorkflowRunId);
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_EmptyStepContractsReturnsNoSteps()
    {
        var result = _runner.Evaluate(new WorkflowRunnerEvaluationRequest
        {
            WorkflowRunId = WorkflowRunId,
            StepContracts = [],
            AvailableEvidence = []
        });

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.NoSteps, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowRunnerBlockReason.NoStepContracts);
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_ValidStepWithAllEvidenceReturnsEligibleForFutureExecution()
    {
        var result = _runner.Evaluate(Request([ValidStep()], [Evidence()]));

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.HasEligibleSteps, result.Status);
        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, result.StepEvaluations[0].Eligibility);
        Assert.IsFalse(result.StepEvaluations[0].MissingEvidenceRequirements.Any());
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_ValidStepWithMissingEvidenceReturnsBlockedMissingEvidence()
    {
        var result = _runner.Evaluate(Request([ValidStep()], []));

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.AllBlocked, result.Status);
        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedMissingEvidence, result.StepEvaluations[0].Eligibility);
        CollectionAssert.Contains(result.StepEvaluations[0].BlockReasons.ToList(), WorkflowRunnerBlockReason.MissingRequiredEvidence);
        Assert.AreEqual("governance-event-001", result.StepEvaluations[0].MissingEvidenceRequirements[0].RequirementId);
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_InvalidPr117ContractReturnsInvalidContract()
    {
        var invalidStep = ValidStep() with { SafeSummary = "tool executed" };

        var result = _runner.Evaluate(Request([invalidStep], [Evidence()]));

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.AllBlocked, result.Status);
        Assert.AreEqual(WorkflowStepRunnerEligibility.InvalidContract, result.StepEvaluations[0].Eligibility);
        CollectionAssert.Contains(result.StepEvaluations[0].BlockReasons.ToList(), WorkflowRunnerBlockReason.InvalidStepContract);
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_MultipleStepsProduceOneEvaluationPerStep()
    {
        var second = ValidStep("step-contract-002", "review-material-002", "governance-event-002");

        var result = _runner.Evaluate(Request([ValidStep(), second], [Evidence(), Evidence("governance-event-002")]));

        Assert.HasCount(2, result.StepEvaluations);
        CollectionAssert.AreEqual(new[] { "step-contract-001", "step-contract-002" }, result.StepEvaluations.Select(step => step.StepId).ToArray());
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_MixedValidAndBlockedStepsProduceHasEligibleSteps()
    {
        var blocked = ValidStep("step-contract-002", "review-material-002", "governance-event-002");

        var result = _runner.Evaluate(Request([ValidStep(), blocked], [Evidence()]));

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.HasEligibleSteps, result.Status);
        Assert.IsTrue(result.StepEvaluations.Any(step => step.Eligibility == WorkflowStepRunnerEligibility.EligibleForFutureExecution));
        Assert.IsTrue(result.StepEvaluations.Any(step => step.Eligibility == WorkflowStepRunnerEligibility.BlockedMissingEvidence));
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_AllBlockedStepsProduceAllBlocked()
    {
        var result = _runner.Evaluate(Request([ValidStep(), ValidStep("step-contract-002", "review-material-002", "governance-event-002")], []));

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.AllBlocked, result.Status);
        Assert.IsTrue(result.StepEvaluations.All(step => step.Eligibility != WorkflowStepRunnerEligibility.EligibleForFutureExecution));
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_ExpectedToolActorDoesNotInvokeTool()
    {
        var step = ValidStep() with { ExpectedActorKind = WorkflowStepContractActorKind.ToolExpected };

        var result = _runner.Evaluate(Request([step], [Evidence()]));

        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, result.StepEvaluations[0].Eligibility);
        CollectionAssert.Contains(result.StepEvaluations[0].BlockReasons.ToList(), WorkflowRunnerBlockReason.ToolBoundaryPreventsInvocation);
        AssertNoPublicMethod("InvokeAsync", "InvokeToolAsync");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_ExpectedAgentActorDoesNotDispatchAgent()
    {
        var step = ValidStep() with { ExpectedActorKind = WorkflowStepContractActorKind.AgentExpected };

        var result = _runner.Evaluate(Request([step], [Evidence()]));

        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, result.StepEvaluations[0].Eligibility);
        CollectionAssert.Contains(result.StepEvaluations[0].BlockReasons.ToList(), WorkflowRunnerBlockReason.DispatchBoundaryPreventsActorResolution);
        AssertNoPublicMethod("DispatchAsync");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_ExpectedHumanReviewerDoesNotSatisfyApproval()
    {
        var step = ValidStep() with { ExpectedActorKind = WorkflowStepContractActorKind.HumanReviewer };

        var result = _runner.Evaluate(Request([step], [Evidence()]));

        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, result.StepEvaluations[0].Eligibility);
        Assert.IsFalse(result.StepEvaluations[0].BlockReasons.Contains(WorkflowRunnerBlockReason.ApprovalBoundaryPreventsMutation));
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_MemoryProposalReferenceRemainsReviewMaterialOnly()
    {
        var step = ValidStep() with
        {
            InputReference = new WorkflowStepContractReference
            {
                Kind = WorkflowStepContractReferenceKind.MemoryProposalRecord,
                ReferenceId = "memory-proposal-001",
                SafeSummary = "Memory proposal reference is review material only."
            }
        };

        var result = _runner.Evaluate(Request([step], [Evidence()]));

        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, result.StepEvaluations[0].Eligibility);
        CollectionAssert.Contains(result.StepEvaluations[0].BlockReasons.ToList(), WorkflowRunnerBlockReason.MemoryBoundaryPreventsPromotion);
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_MemoryProposalReferenceDoesNotPromoteMemory()
    {
        var serialized = JsonSerializer.Serialize(_runner.Evaluate(Request([ValidStep() with
        {
            InputReference = new WorkflowStepContractReference
            {
                Kind = WorkflowStepContractReferenceKind.MemoryProposalRecord,
                ReferenceId = "memory-proposal-001",
                SafeSummary = "Memory proposal reference is review material only."
            }
        }], [Evidence()])));

        AssertDoesNotContainAny(serialized, "MemoryPromoted", "AcceptedMemory", "CreateAcceptedMemory");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_ApprovalPolicyReferenceRemainsReviewRequirementOnly()
    {
        var step = ValidStep() with
        {
            InputReference = new WorkflowStepContractReference
            {
                Kind = WorkflowStepContractReferenceKind.ApprovalPolicyRecord,
                ReferenceId = "approval-policy-001",
                SafeSummary = "Approval policy reference records the review requirement only."
            },
            EvidenceRequirements =
            [
                new WorkflowStepContractEvidenceRequirement
                {
                    Kind = WorkflowStepContractEvidenceRequirementKind.ApprovalPolicyReference,
                    RequirementId = "approval-policy-ref-001",
                    SafeSummary = "Policy reference is evidence only."
                }
            ]
        };

        var result = _runner.Evaluate(Request([step], [Evidence("approval-policy-ref-001", WorkflowStepContractEvidenceRequirementKind.ApprovalPolicyReference)]));

        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, result.StepEvaluations[0].Eligibility);
        CollectionAssert.Contains(result.StepEvaluations[0].BlockReasons.ToList(), WorkflowRunnerBlockReason.ApprovalBoundaryPreventsMutation);
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_ApprovalPolicyReferenceDoesNotMutateApprovalState()
    {
        var source = ReadRepoFile("IronDev.Core/Workflow/WorkflowRunnerSkeletonModels.cs");

        AssertDoesNotContainAny(source, "IApprovalDecisionStore", "IApprovalMutation", "ApproveAsync", "SatisfyApproval", "ApprovalGranted");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_InputReferenceIsNotHydrated()
    {
        var source = ReadRepoFile("IronDev.Core/Workflow/WorkflowRunnerSkeletonModels.cs");

        AssertDoesNotContainAny(source, "HydrateAsync", "LoadReference", "ResolveReference", "ReadFile", "File.Read");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_ExpectedOutputReferenceIsNotCreated()
    {
        var source = ReadRepoFile("IronDev.Core/Workflow/WorkflowRunnerSkeletonModels.cs");

        AssertDoesNotContainAny(source, "CreateOutput", "WriteOutput", "IArtifactStore", "CreateArtifact", "File.Write");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_NextRecordableTransitionIsReportedButNotRecorded()
    {
        var result = _runner.Evaluate(Request([ValidStep()], [Evidence()]));

        Assert.AreEqual(WorkflowStepContractTransitionKind.DraftToReadyForReview, result.StepEvaluations[0].NextRecordableTransition);
        AssertNoPublicMethod("RecordTransitionAsync", "ContinueAsync", "CompleteAsync");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_BoundaryClosedStepReportsRuntimeBoundaryReason()
    {
        var result = _runner.Evaluate(Request([ValidStep()], [Evidence()]));

        CollectionAssert.Contains(result.StepEvaluations[0].BlockReasons.ToList(), WorkflowRunnerBlockReason.RuntimeBoundaryPreventsExecution);
        CollectionAssert.Contains(result.StepEvaluations[0].BlockReasons.ToList(), WorkflowRunnerBlockReason.RetrievalBoundaryPreventsActivation);
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_EvaluationResultContainsNoRawPromptField()
    {
        AssertSerializedResultDoesNotContain("RawPrompt", "rawPrompt", "raw prompt");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_EvaluationResultContainsNoRawCompletionField()
    {
        AssertSerializedResultDoesNotContain("RawCompletion", "rawCompletion", "raw completion");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_EvaluationResultContainsNoRawToolOutputField()
    {
        AssertSerializedResultDoesNotContain("RawToolOutput", "rawToolOutput", "raw tool output");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_EvaluationResultContainsNoPrivateOrHiddenReasoningField()
    {
        AssertSerializedResultDoesNotContain("PrivateReasoning", "HiddenReasoning", "private reasoning", "hidden reasoning", "ChainOfThought", "Scratchpad");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_EvaluationResultContainsNoWholePatchPayloadField()
    {
        AssertSerializedResultDoesNotContain("WholePatch", "PatchPayload", "EntirePatch", "whole patch", "patch payload", "entire patch");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_SameRequestProducesSameEvaluationResult()
    {
        var request = Request([ValidStep()], [Evidence()]);

        var first = JsonSerializer.Serialize(_runner.Evaluate(request));
        var second = JsonSerializer.Serialize(_runner.Evaluate(request));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_EvaluatorHasNoDependencyOnIoServices()
    {
        var constructors = typeof(WorkflowRunnerSkeleton).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.AreEqual(1, constructors.Length, $"Expected one public constructor. Actual: {constructors.Length}");
        Assert.IsFalse(constructors[0].GetParameters().Any(), "Runner skeleton public constructor should not require IO services.");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_InterfaceHasOnlyEvaluate()
    {
        var names = typeof(IWorkflowRunnerSkeleton).GetMethods().Select(method => method.Name).ToArray();

        CollectionAssert.AreEqual(new[] { "Evaluate" }, names);
        AssertDoesNotContainAny(names, "RunAsync", "ExecuteAsync", "DispatchAsync", "InvokeToolAsync", "ApplyPatchAsync");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_ProductionFileDoesNotReferenceRuntimeDispatchToolSourceRetrievalOrPromotionServices()
    {
        var source = ReadRepoFile("IronDev.Core/Workflow/WorkflowRunnerSkeletonModels.cs");

        AssertDoesNotContainAny(
            source,
            "RunAsync",
            "ExecuteAsync",
            "DispatchAsync",
            "InvokeToolAsync",
            "ApplyPatchAsync",
            "BackgroundService",
            "IHostedService",
            "Scheduler",
            "Controller",
            "IToolInvoker",
            "IToolRouter",
            "IAgentDispatcher",
            "IActorResolver",
            "IModelClient",
            "IEmbeddingClient",
            "IVectorSearch",
            "IRetrievalService",
            "IMemoryPromotionService",
            "ISourceMutationService",
            "IPatchApplyService",
            "SqlConnection",
            "InMemoryWorkflow");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_NoApiCliOrDatabaseSurfaceAdded()
    {
        var root = RepositoryRoot();
        var apiText = ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Api"));
        var cliText = ReadAllTextIfDirectoryExists(Path.Combine(root, "tools", "IronDev.Cli")) +
                      ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Cli"));
        var databaseText = ReadAllTextIfDirectoryExists(Path.Combine(root, "Database"));

        AssertDoesNotContainAny(apiText, "WorkflowRunnerSkeleton");
        AssertDoesNotContainAny(cliText, "WorkflowRunnerSkeleton");
        AssertDoesNotContainAny(databaseText, "WorkflowRunnerSkeleton");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_ModelPropertiesDoNotExposeRawDurableFields()
    {
        var propertyNames = new[]
        {
            typeof(WorkflowRunnerEvaluationRequest),
            typeof(WorkflowEvidenceReference),
            typeof(WorkflowRunnerEvaluation),
            typeof(WorkflowStepRunnerEvaluation)
        }
        .SelectMany(type => type.GetProperties())
        .Select(property => property.Name);

        AssertDoesNotContainAny(propertyNames, "RawPrompt", "RawCompletion", "RawToolOutput", "PrivateReasoning", "HiddenReasoning", "WholePatch", "PatchPayload");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_ReceiptRecordsEligibilityIsNotExecution()
    {
        var receipt = ReadRepoFile("Docs/receipts/PR118_MINIMAL_WORKFLOW_RUNNER_SKELETON_RECEIPT.md").ToLowerInvariant();

        StringAssert.Contains(receipt, "minimal workflow runner skeleton");
        StringAssert.Contains(receipt, "eligibility is not execution");
        StringAssert.Contains(receipt, "sql remains the source of truth");
        StringAssert.Contains(receipt, "does not execute workflow steps");
        StringAssert.Contains(receipt, "does not dispatch agents");
        StringAssert.Contains(receipt, "does not invoke tools");
        StringAssert.Contains(receipt, "does not promote memory");
        StringAssert.Contains(receipt, "does not activate retrieval");
    }

    private static WorkflowRunnerEvaluationRequest Request(
        IReadOnlyList<WorkflowStepContract> steps,
        IReadOnlyList<WorkflowEvidenceReference> evidence) =>
        new()
        {
            WorkflowRunId = WorkflowRunId,
            StepContracts = steps,
            AvailableEvidence = evidence
        };

    private static WorkflowStepContract ValidStep(
        string stepId = "step-contract-001",
        string outputId = "review-material-001",
        string evidenceId = "governance-event-001") =>
        new()
        {
            StepContractId = stepId,
            WorkflowRunId = WorkflowRunId,
            Intent = WorkflowStepContractIntent.PrepareReviewMaterial,
            InputReference = new WorkflowStepContractReference
            {
                Kind = WorkflowStepContractReferenceKind.WorkflowStepRecord,
                ReferenceId = "workflow-step-input-001",
                SafeSummary = "Prior workflow step fact is referenced for review context."
            },
            ExpectedOutputReference = new WorkflowStepContractReference
            {
                Kind = WorkflowStepContractReferenceKind.ReviewMaterial,
                ReferenceId = outputId,
                SafeSummary = "Expected output is review material only."
            },
            ExpectedActorKind = WorkflowStepContractActorKind.AgentExpected,
            AllowedTransitions =
            [
                new WorkflowStepContractTransitionRule
                {
                    Kind = WorkflowStepContractTransitionKind.DraftToReadyForReview,
                    SafeLabel = "Draft review material may become ready for human review."
                }
            ],
            EvidenceRequirements =
            [
                new WorkflowStepContractEvidenceRequirement
                {
                    Kind = WorkflowStepContractEvidenceRequirementKind.GovernanceEventReference,
                    RequirementId = evidenceId,
                    SafeSummary = "Governance event reference is required as evidence."
                }
            ],
            Boundary = new WorkflowStepContractBoundary(),
            SafeSummary = "Typed step contract records intent and evidence requirements."
        };

    private static WorkflowEvidenceReference Evidence(
        string referenceId = "governance-event-001",
        WorkflowStepContractEvidenceRequirementKind kind = WorkflowStepContractEvidenceRequirementKind.GovernanceEventReference) =>
        new()
        {
            Kind = kind,
            ReferenceId = referenceId,
            CorrelationId = "correlation-001"
        };

    private void AssertSerializedResultDoesNotContain(params string[] tokens)
    {
        var serialized = JsonSerializer.Serialize(_runner.Evaluate(Request([ValidStep()], [Evidence()])));
        AssertDoesNotContainAny(serialized, tokens);
    }

    private static void AssertNoPublicMethod(params string[] names)
    {
        var publicNames = typeof(IWorkflowRunnerSkeleton).GetMethods().Select(method => method.Name)
            .Concat(typeof(WorkflowRunnerSkeleton).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(method => method.Name))
            .ToArray();

        AssertDoesNotContainAny(publicNames, names);
    }

    private static void AssertDoesNotContainAny(IEnumerable<string> values, params string[] forbidden) =>
        AssertDoesNotContainAny(string.Join("\n", values), forbidden);

    private static void AssertDoesNotContainAny(string text, params string[] forbidden)
    {
        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found: {token}");
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string ReadAllTextIfDirectoryExists(string directory)
    {
        if (!Directory.Exists(directory))
            return string.Empty;

        return string.Join(
            "\n",
            Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private const string WorkflowRunId = "workflow-run-001";
}
