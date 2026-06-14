using System.Reflection;
using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowStepPolicyPreflight")]
public sealed class WorkflowStepPolicyPreflightCheckerTests
{
    private readonly WorkflowStepPolicyPreflightChecker _checker = new();

    [TestMethod]
    public void WorkflowStepPolicyPreflight_MissingRequestReturnsInvalidPolicyRequest()
    {
        var result = _checker.Check(null);

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.InvalidStepContract);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_InvalidStepContractBlocksPolicyPreflight()
    {
        var result = _checker.Check(Request(
            ValidStep() with { SafeSummary = "tool executed" },
            WorkflowStepSensitivityKind.SourceMutation,
            Requirement(WorkflowStepPolicyRequirementKind.SourceMutationApprovalReference)));

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.InvalidStepContract);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_UnknownSensitivityBlocksPolicyPreflight()
    {
        var result = _checker.Check(Request(ValidStep(), WorkflowStepSensitivityKind.Unknown));

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.UnknownSensitivity);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_NonSensitiveStepDoesNotRequirePolicyEvidence()
    {
        var result = _checker.Check(Request(ValidStep(), WorkflowStepSensitivityKind.None));

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.NotSensitive, result.Status);
        Assert.IsFalse(result.MissingPolicyRequirements.Any());
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_SourceMutationWithoutPolicyEvidenceIsBlocked()
    {
        var result = _checker.Check(Request(
            ValidStep(),
            WorkflowStepSensitivityKind.SourceMutation,
            Requirement(WorkflowStepPolicyRequirementKind.SourceMutationApprovalReference)));

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.MissingPolicyEvidence);
        Assert.AreEqual("policy-ref-001", result.MissingPolicyRequirements[0].ReferenceId);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_SourceMutationWithApprovalReferenceIsEvidencePresentOnly()
    {
        var result = _checker.Check(Request(
            ValidStep(),
            WorkflowStepSensitivityKind.SourceMutation,
            Requirement(WorkflowStepPolicyRequirementKind.SourceMutationApprovalReference),
            Evidence(WorkflowStepPolicyRequirementKind.SourceMutationApprovalReference)));

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.PolicyEvidencePresentForFutureExecution, result.Status);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_ToolInvocationRequiresToolGateReference()
    {
        AssertRequiresKind(WorkflowStepSensitivityKind.ToolInvocation, WorkflowStepPolicyRequirementKind.ToolGateReference);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_AgentDispatchRequiresGovernanceOrHandoffValidationReference()
    {
        AssertRequiresOneOf(
            WorkflowStepSensitivityKind.AgentDispatch,
            WorkflowStepPolicyRequirementKind.GovernanceEventReference,
            WorkflowStepPolicyRequirementKind.A2aHandoffValidationReference);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_MemoryPromotionRequiresMemoryPromotionApprovalReference()
    {
        AssertRequiresKind(WorkflowStepSensitivityKind.MemoryPromotion, WorkflowStepPolicyRequirementKind.MemoryPromotionApprovalReference);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_RetrievalActivationRequiresRetrievalApprovalReference()
    {
        AssertRequiresKind(WorkflowStepSensitivityKind.RetrievalActivation, WorkflowStepPolicyRequirementKind.RetrievalApprovalReference);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_A2aHandoffRequiresHandoffValidationReference()
    {
        AssertRequiresKind(WorkflowStepSensitivityKind.A2aHandoff, WorkflowStepPolicyRequirementKind.A2aHandoffValidationReference);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_PatchApplyRequiresSourceMutationApprovalReference()
    {
        AssertRequiresKind(WorkflowStepSensitivityKind.PatchApply, WorkflowStepPolicyRequirementKind.SourceMutationApprovalReference);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_ApprovalRequiredActionRequiresHumanApprovalReference()
    {
        AssertRequiresKind(WorkflowStepSensitivityKind.ApprovalRequiredAction, WorkflowStepPolicyRequirementKind.HumanApprovalReference);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_ModelCallRequiresGovernanceOrPolicyReference()
    {
        AssertRequiresOneOf(
            WorkflowStepSensitivityKind.ModelCall,
            WorkflowStepPolicyRequirementKind.GovernanceEventReference,
            WorkflowStepPolicyRequirementKind.ApprovalPolicyReference);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_EvidenceReferenceDoesNotCreateApprovalSatisfyPolicyOrExecute()
    {
        var result = _checker.Check(Request(
            ValidStep(),
            WorkflowStepSensitivityKind.ToolInvocation,
            Requirement(WorkflowStepPolicyRequirementKind.ToolGateReference),
            Evidence(WorkflowStepPolicyRequirementKind.ToolGateReference)));

        AssertNoAuthority(result);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.ApprovalCannotBeInferredFromEvidence);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.WorkflowCannotGrantAuthority);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.PolicyPreflightCannotMutateApproval);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.PolicyPreflightCannotExecute);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_SameRequestProducesSameResult()
    {
        var request = Request(
            ValidStep(),
            WorkflowStepSensitivityKind.ToolInvocation,
            Requirement(WorkflowStepPolicyRequirementKind.ToolGateReference),
            Evidence(WorkflowStepPolicyRequirementKind.ToolGateReference));

        var first = JsonSerializer.Serialize(_checker.Check(request));
        var second = JsonSerializer.Serialize(_checker.Check(request));

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_ResultContainsNoRawOrPrivatePayloadFields()
    {
        var serialized = JsonSerializer.Serialize(_checker.Check(Request(
            ValidStep(),
            WorkflowStepSensitivityKind.ToolInvocation,
            Requirement(WorkflowStepPolicyRequirementKind.ToolGateReference),
            Evidence(WorkflowStepPolicyRequirementKind.ToolGateReference))));

        AssertDoesNotContainAny(
            serialized,
            "RawPrompt",
            "rawPrompt",
            "RawCompletion",
            "rawCompletion",
            "RawToolOutput",
            "rawToolOutput",
            "PrivateReasoning",
            "HiddenReasoning",
            "ChainOfThought",
            "WholePatch",
            "PatchPayload",
            "EntirePatch");
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_InterfaceHasOnlyCheck()
    {
        var names = typeof(IWorkflowStepPolicyPreflightChecker).GetMethods().Select(method => method.Name).ToArray();

        CollectionAssert.AreEqual(new[] { "Check" }, names);
        AssertDoesNotContainAny(names, "Approve", "Satisfy", "Execute", "Run", "Dispatch");
    }

    private void AssertRequiresKind(WorkflowStepSensitivityKind sensitivity, WorkflowStepPolicyRequirementKind requiredKind)
    {
        var wrong = _checker.Check(Request(
            ValidStep(),
            sensitivity,
            Requirement(WorkflowStepPolicyRequirementKind.GovernanceEventReference),
            Evidence(WorkflowStepPolicyRequirementKind.GovernanceEventReference)));

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest, wrong.Status);
        CollectionAssert.Contains(wrong.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.MissingRequiredPolicyReference);

        var right = _checker.Check(Request(
            ValidStep(),
            sensitivity,
            Requirement(requiredKind),
            Evidence(requiredKind)));

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.PolicyEvidencePresentForFutureExecution, right.Status);
    }

    private void AssertRequiresOneOf(
        WorkflowStepSensitivityKind sensitivity,
        WorkflowStepPolicyRequirementKind first,
        WorkflowStepPolicyRequirementKind second)
    {
        var wrong = _checker.Check(Request(
            ValidStep(),
            sensitivity,
            Requirement(WorkflowStepPolicyRequirementKind.HumanApprovalReference),
            Evidence(WorkflowStepPolicyRequirementKind.HumanApprovalReference)));

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest, wrong.Status);

        foreach (var kind in new[] { first, second })
        {
            var right = _checker.Check(Request(ValidStep(), sensitivity, Requirement(kind), Evidence(kind)));
            Assert.AreEqual(WorkflowStepPolicyPreflightStatus.PolicyEvidencePresentForFutureExecution, right.Status);
        }
    }

    private static void AssertNoAuthority(WorkflowStepPolicyPreflightResult result)
    {
        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.PolicyEvidencePresentForFutureExecution, result.Status);
        var serialized = JsonSerializer.Serialize(result);
        AssertDoesNotContainAny(
            serialized,
            "Approved",
            "ApprovalGranted",
            "PolicySatisfied",
            "Executable",
            "Executed",
            "CanRun",
            "TransitionCreated",
            "AgentDispatched",
            "ToolInvoked",
            "MemoryPromoted",
            "RetrievalActivated");
    }

    private static WorkflowStepPolicyPreflightRequest Request(
        WorkflowStepContract? step,
        WorkflowStepSensitivityKind sensitivity,
        WorkflowStepPolicyRequirement? requirement = null,
        WorkflowStepPolicyEvidenceReference? evidence = null) =>
        new()
        {
            StepContract = step,
            SensitivityKind = sensitivity,
            RequiredPolicyReferences = requirement is null ? [] : [requirement],
            AvailablePolicyEvidence = evidence is null ? [] : [evidence]
        };

    internal static WorkflowStepContract ValidStep(
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

    internal static WorkflowEvidenceReference EvidenceReference(
        string referenceId = "governance-event-001",
        WorkflowStepContractEvidenceRequirementKind kind = WorkflowStepContractEvidenceRequirementKind.GovernanceEventReference) =>
        new()
        {
            Kind = kind,
            ReferenceId = referenceId,
            CorrelationId = "correlation-001"
        };

    internal static WorkflowStepPolicyRequirement Requirement(
        WorkflowStepPolicyRequirementKind kind,
        string referenceId = "policy-ref-001") =>
        new()
        {
            Kind = kind,
            ReferenceId = referenceId,
            ProjectId = "project-001",
            CorrelationId = "correlation-001"
        };

    internal static WorkflowStepPolicyEvidenceReference Evidence(
        WorkflowStepPolicyRequirementKind kind,
        string referenceId = "policy-ref-001") =>
        new()
        {
            Kind = kind,
            ReferenceId = referenceId,
            ProjectId = "project-001",
            CorrelationId = "correlation-001"
        };

    internal const string WorkflowRunId = "workflow-run-001";

    internal static void AssertDoesNotContainAny(IEnumerable<string> values, params string[] forbidden) =>
        AssertDoesNotContainAny(string.Join("\n", values), forbidden);

    internal static void AssertDoesNotContainAny(string text, params string[] forbidden)
    {
        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found: {token}");
    }
}

[TestClass]
[TestCategory("WorkflowRunnerSkeletonPolicyPreflight")]
public sealed class WorkflowRunnerSkeletonPolicyPreflightTests
{
    private readonly WorkflowRunnerSkeleton _runner = new();

    [TestMethod]
    public void WorkflowRunnerSkeleton_BlocksSensitiveStepMissingPolicyEvidence()
    {
        var step = WorkflowStepPolicyPreflightCheckerTests.ValidStep();
        var result = _runner.Evaluate(Request(
            [step],
            [WorkflowStepPolicyPreflightCheckerTests.EvidenceReference()],
            [PolicyRequest(step, WorkflowStepSensitivityKind.SourceMutation, WorkflowStepPolicyRequirementKind.SourceMutationApprovalReference)]));

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.AllBlocked, result.Status);
        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedByBoundary, result.StepEvaluations[0].Eligibility);
        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, result.StepEvaluations[0].PolicyPreflightStatus);
        CollectionAssert.Contains(result.StepEvaluations[0].BlockReasons.ToList(), WorkflowRunnerBlockReason.PolicyPreflightMissingEvidence);
        CollectionAssert.Contains(result.StepEvaluations[0].PolicyBlockReasons.ToList(), WorkflowStepPolicyBlockReason.MissingPolicyEvidence);
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_MarksSensitiveStepWithPolicyEvidenceAsEligibleForFutureExecutionOnly()
    {
        var step = WorkflowStepPolicyPreflightCheckerTests.ValidStep();
        var result = _runner.Evaluate(Request(
            [step],
            [WorkflowStepPolicyPreflightCheckerTests.EvidenceReference()],
            [PolicyRequest(
                step,
                WorkflowStepSensitivityKind.SourceMutation,
                WorkflowStepPolicyRequirementKind.SourceMutationApprovalReference,
                includeEvidence: true)]));

        Assert.AreEqual(WorkflowRunnerEvaluationStatus.HasEligibleSteps, result.Status);
        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, result.StepEvaluations[0].Eligibility);
        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.PolicyEvidencePresentForFutureExecution, result.StepEvaluations[0].PolicyPreflightStatus);
        CollectionAssert.Contains(result.StepEvaluations[0].PolicyBlockReasons.ToList(), WorkflowStepPolicyBlockReason.PolicyPreflightCannotExecute);
        CollectionAssert.Contains(result.StepEvaluations[0].PolicyBlockReasons.ToList(), WorkflowStepPolicyBlockReason.WorkflowCannotGrantAuthority);
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_DoesNotExecuteTransitionOrApproveAfterPolicyEvidenceIsPresent()
    {
        var step = WorkflowStepPolicyPreflightCheckerTests.ValidStep();
        var result = _runner.Evaluate(Request(
            [step],
            [WorkflowStepPolicyPreflightCheckerTests.EvidenceReference()],
            [PolicyRequest(
                step,
                WorkflowStepSensitivityKind.ToolInvocation,
                WorkflowStepPolicyRequirementKind.ToolGateReference,
                includeEvidence: true)]));

        var serialized = JsonSerializer.Serialize(result);

        Assert.AreEqual(WorkflowStepRunnerEligibility.EligibleForFutureExecution, result.StepEvaluations[0].Eligibility);
        WorkflowStepPolicyPreflightCheckerTests.AssertDoesNotContainAny(
            serialized,
            "ExecutionAllowed",
            "Executed",
            "ApprovalGranted",
            "PolicySatisfied",
            "TransitionRecorded",
            "AgentDispatched",
            "ToolInvoked");
    }

    [TestMethod]
    public void WorkflowRunnerSkeleton_PreservesPr118NoLeverBoundary()
    {
        var publicNames = typeof(IWorkflowRunnerSkeleton).GetMethods().Select(method => method.Name)
            .Concat(typeof(WorkflowRunnerSkeleton).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(method => method.Name))
            .ToArray();

        WorkflowStepPolicyPreflightCheckerTests.AssertDoesNotContainAny(
            publicNames,
            "RunAsync",
            "ExecuteAsync",
            "DispatchAsync",
            "InvokeToolAsync",
            "ApproveAsync",
            "SatisfyPolicyAsync");
    }

    private static WorkflowRunnerEvaluationRequest Request(
        IReadOnlyList<WorkflowStepContract> steps,
        IReadOnlyList<WorkflowEvidenceReference> evidence,
        IReadOnlyList<WorkflowStepPolicyPreflightRequest> policyRequests) =>
        new()
        {
            WorkflowRunId = WorkflowStepPolicyPreflightCheckerTests.WorkflowRunId,
            StepContracts = steps,
            AvailableEvidence = evidence,
            PolicyPreflightRequests = policyRequests
        };

    private static WorkflowStepPolicyPreflightRequest PolicyRequest(
        WorkflowStepContract step,
        WorkflowStepSensitivityKind sensitivity,
        WorkflowStepPolicyRequirementKind requiredKind,
        bool includeEvidence = false) =>
        new()
        {
            StepContract = step,
            SensitivityKind = sensitivity,
            RequiredPolicyReferences = [WorkflowStepPolicyPreflightCheckerTests.Requirement(requiredKind)],
            AvailablePolicyEvidence = includeEvidence ? [WorkflowStepPolicyPreflightCheckerTests.Evidence(requiredKind)] : []
        };
}

[TestClass]
[TestCategory("WorkflowStepPolicyPreflight")]
public sealed class WorkflowStepPolicyPreflightStaticBoundaryTests
{
    [TestMethod]
    public void WorkflowStepPolicyPreflight_ProductionFilesDoNotReferenceRuntimeAuthorityOrStorageServices()
    {
        var source = ReadRepoFile("IronDev.Core/Workflow/WorkflowStepPolicyPreflightModels.cs") +
                     "\n" +
                     ReadRepoFile("IronDev.Core/Workflow/WorkflowRunnerSkeletonModels.cs");

        WorkflowStepPolicyPreflightCheckerTests.AssertDoesNotContainAny(
            source,
            "IApprovalDecisionStore",
            "IApprovalMutation",
            "IPolicySatisfaction",
            "IAuthorityGrant",
            "IWorkflowTransitionRecorder",
            "RunAsync",
            "ExecuteAsync",
            "DispatchAsync",
            "InvokeToolAsync",
            "IToolInvoker",
            "IToolRouter",
            "IAgentDispatcher",
            "IActorResolver",
            "ISourceMutationService",
            "IPatchApplyService",
            "IMemoryPromotionService",
            "IRetrievalService",
            "IVectorSearch",
            "IEmbeddingClient",
            "IModelClient",
            "SqlConnection",
            "DbConnection",
            "Controller",
            "BackgroundService",
            "IHostedService",
            "InMemoryAuthority");
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_NoApiCliOrSqlSurfaceAdded()
    {
        var root = RepositoryRoot();
        var apiText = ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Api"));
        var cliText = ReadAllTextIfDirectoryExists(Path.Combine(root, "tools", "IronDev.Cli")) +
                      ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Cli"));
        var databaseText = ReadAllTextIfDirectoryExists(Path.Combine(root, "Database"));

        WorkflowStepPolicyPreflightCheckerTests.AssertDoesNotContainAny(apiText, "WorkflowStepPolicyPreflight");
        WorkflowStepPolicyPreflightCheckerTests.AssertDoesNotContainAny(cliText, "WorkflowStepPolicyPreflight");
        WorkflowStepPolicyPreflightCheckerTests.AssertDoesNotContainAny(databaseText, "WorkflowStepPolicyPreflight");
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_ModelPropertiesDoNotExposeRawDurableFields()
    {
        var propertyNames = new[]
        {
            typeof(WorkflowStepPolicyRequirement),
            typeof(WorkflowStepPolicyEvidenceReference),
            typeof(WorkflowStepPolicyPreflightRequest),
            typeof(WorkflowStepPolicyPreflightResult),
            typeof(WorkflowStepRunnerEvaluation)
        }
        .SelectMany(type => type.GetProperties())
        .Select(property => property.Name);

        WorkflowStepPolicyPreflightCheckerTests.AssertDoesNotContainAny(
            propertyNames,
            "RawPrompt",
            "RawCompletion",
            "RawToolOutput",
            "PrivateReasoning",
            "HiddenReasoning",
            "WholePatch",
            "PatchPayload");
    }

    [TestMethod]
    public void WorkflowStepPolicyPreflight_ReceiptRecordsEvidenceIsNotApprovalOrExecution()
    {
        var receipt = ReadRepoFile("Docs/receipts/PR119_POLICY_CHECK_BEFORE_SENSITIVE_STEP_RECEIPT.md").ToLowerInvariant();

        StringAssert.Contains(receipt, "policy evidence is not approval");
        StringAssert.Contains(receipt, "policy evidence does not satisfy policy");
        StringAssert.Contains(receipt, "policy evidence does not execute or transition workflow state");
        StringAssert.Contains(receipt, "runner skeleton remains evaluation-only");
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
}
