using System.Reflection;
using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowStepThoughtLedger")]
public sealed class WorkflowStepThoughtLedgerReferenceTests
{
    private readonly WorkflowStepContractValidator _validator = new();

    [TestMethod]
    public void WorkflowStepThoughtLedger_ValidReferencePassesValidation()
    {
        var result = _validator.Validate(WorkflowStepPolicyPreflightCheckerTests.ValidStep());

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues.Select(issue => issue.Code)));
    }

    [DataTestMethod]
    [DataRow("raw prompt")]
    [DataRow("raw completion")]
    [DataRow("raw tool output")]
    [DataRow("private reasoning")]
    [DataRow("hidden reasoning")]
    [DataRow("whole patch")]
    [DataRow("approval granted")]
    [DataRow("policy satisfied")]
    [DataRow("execution succeeded")]
    [DataRow("memory promoted")]
    [DataRow("retrieval activated")]
    public void WorkflowStepThoughtLedger_UnsafeSummaryMarkersFail(string marker)
    {
        var result = _validator.Validate(WorkflowStepPolicyPreflightCheckerTests.ValidStep() with
        {
            ThoughtLedgerReference = WorkflowStepContractTests.ValidThoughtLedgerReference() with
            {
                SafeSummary = marker
            }
        });

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => string.Equals(issue.Code, "WORKFLOW_STEP_CONTRACT_TEXT_UNSAFE", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void WorkflowStepThoughtLedger_DoesNotSatisfyEvidenceRequirements()
    {
        var runner = new WorkflowRunnerSkeleton();
        var result = runner.Evaluate(new WorkflowRunnerEvaluationRequest
        {
            WorkflowRunId = WorkflowStepPolicyPreflightCheckerTests.WorkflowRunId,
            StepContracts = [WorkflowStepPolicyPreflightCheckerTests.ValidStep()],
            AvailableEvidence = []
        });

        Assert.AreEqual(WorkflowStepRunnerEligibility.BlockedMissingEvidence, result.StepEvaluations[0].Eligibility);
        CollectionAssert.Contains(result.StepEvaluations[0].BlockReasons.ToList(), WorkflowRunnerBlockReason.MissingRequiredEvidence);
    }

    [TestMethod]
    public void WorkflowStepThoughtLedger_DoesNotSatisfyPolicyPreflight()
    {
        var checker = new WorkflowStepPolicyPreflightChecker();
        var result = checker.Check(new WorkflowStepPolicyPreflightRequest
        {
            StepContract = WorkflowStepPolicyPreflightCheckerTests.ValidStep(),
            SensitivityKind = WorkflowStepSensitivityKind.RetrievalActivation,
            RequiredPolicyReferences =
            [
                WorkflowStepPolicyPreflightCheckerTests.Requirement(WorkflowStepPolicyRequirementKind.RetrievalApprovalReference)
            ],
            AvailablePolicyEvidence = []
        });

        Assert.AreEqual(WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence, result.Status);
        CollectionAssert.Contains(result.BlockReasons.ToList(), WorkflowStepPolicyBlockReason.MissingPolicyEvidence);
    }

    [TestMethod]
    public void WorkflowStepThoughtLedger_SerializedResultContainsTraceMarkerOnly()
    {
        var runner = new WorkflowRunnerSkeleton();
        var result = runner.Evaluate(new WorkflowRunnerEvaluationRequest
        {
            WorkflowRunId = WorkflowStepPolicyPreflightCheckerTests.WorkflowRunId,
            StepContracts = [WorkflowStepPolicyPreflightCheckerTests.ValidStep()],
            AvailableEvidence = [WorkflowStepPolicyPreflightCheckerTests.EvidenceReference()]
        });

        var serialized = JsonSerializer.Serialize(result);

        StringAssert.Contains(serialized, "thought-ledger-entry-001");
        AssertDoesNotContainAny(serialized, "RawPrompt", "RawCompletion", "RawToolOutput", "PrivateReasoning", "HiddenReasoning", "ChainOfThought", "WholePatch", "PatchPayload");
        AssertDoesNotContainAny(serialized, "ApprovalGranted", "PolicySatisfied", "ExecutionAllowed", "MemoryPromoted", "RetrievalActivated");
    }

    [TestMethod]
    public void WorkflowStepThoughtLedger_StaticBoundaryHasNoReaderWriterRuntimeOrStorageSurface()
    {
        var source = ReadRepoFile("IronDev.Core/Workflow/WorkflowStepContractModels.cs") +
                     "\n" +
                     ReadRepoFile("IronDev.Core/Workflow/WorkflowRunnerSkeletonModels.cs") +
                     "\n" +
                     ReadRepoFile("IronDev.Core/Workflow/WorkflowStepPolicyPreflightModels.cs");

        AssertDoesNotContainAny(
            source,
            "IThoughtLedgerWriter",
            "IThoughtLedgerReader",
            "IThoughtLedgerStore",
            "ReadThoughtLedger",
            "WriteThoughtLedger",
            "CreateThoughtLedger",
            "HydrateThoughtLedger",
            "RunAsync",
            "ExecuteAsync",
            "DispatchAsync",
            "InvokeToolAsync",
            "IWorkflowTransitionRecorder",
            "IApprovalMutation",
            "IPolicySatisfaction",
            "IAuthorityGrant",
            "ISourceMutationService",
            "IPatchApplyService",
            "IToolInvoker",
            "IAgentDispatcher",
            "IModelClient",
            "IMemoryPromotionService",
            "IRetrievalService",
            "IVectorSearch",
            "IEmbeddingClient",
            "SqlConnection",
            "Controller",
            "BackgroundService",
            "IHostedService");
    }

    [TestMethod]
    public void WorkflowStepThoughtLedger_NoApiCliOrDatabaseSurfaceAdded()
    {
        var root = RepositoryRoot();
        var apiText = ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Api"));
        var cliText = ReadAllTextIfDirectoryExists(Path.Combine(root, "tools", "IronDev.Cli")) +
                      ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Cli"));
        var databaseText = ReadAllTextIfDirectoryExists(Path.Combine(root, "Database"));

        AssertDoesNotContainAny(apiText, "WorkflowStepThoughtLedger");
        AssertDoesNotContainAny(cliText, "WorkflowStepThoughtLedger");
        AssertDoesNotContainAny(databaseText, "WorkflowStepThoughtLedger");
    }

    [TestMethod]
    public void WorkflowStepThoughtLedger_InterfaceShapeDoesNotExposeLedgerServices()
    {
        var publicMethods = typeof(IWorkflowRunnerSkeleton).GetMethods()
            .Concat(typeof(WorkflowRunnerSkeleton).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(method => method.Name);

        AssertDoesNotContainAny(publicMethods, "ReadLedger", "WriteLedger", "CreateLedger", "RunAsync", "ExecuteAsync", "DispatchAsync", "InvokeToolAsync");
    }

    [TestMethod]
    public void WorkflowStepThoughtLedger_ReceiptRecordsTraceabilityOnlyBoundary()
    {
        var receipt = ReadRepoFile("Docs/receipts/PR120_THOUGHTLEDGER_REQUIRED_PER_WORKFLOW_STEP_RECEIPT.md").ToLowerInvariant();

        StringAssert.Contains(receipt, "thoughtledger reference presence is not approval");
        StringAssert.Contains(receipt, "the runner skeleton remains evaluation-only");
        StringAssert.Contains(receipt, "does not read or write thoughtledger content");
        AssertDoesNotContainAny(receipt, "steps can now execute", "ledger evidence satisfies policy", "thoughtledger now approves");
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
}
