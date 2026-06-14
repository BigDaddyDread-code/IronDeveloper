using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowCannotGrantAuthority")]
public sealed class WorkflowCannotGrantAuthorityStaticBoundaryTests
{
    [TestMethod]
    public void WorkflowCannotGrantAuthority_CoreInterfacesExposeOnlyExistingEvaluationMethods()
    {
        CollectionAssert.AreEqual(new[] { "Evaluate" }, PublicMethodNames(typeof(IWorkflowRunnerSkeleton)));
        CollectionAssert.AreEqual(new[] { "ExecuteDryRun" }, PublicMethodNames(typeof(IWorkflowDryRunExecutor)));
        CollectionAssert.AreEqual(new[] { "SuggestRoute" }, PublicMethodNames(typeof(IBoxedLangGraphRoutingAdapter)));
    }

    [DataTestMethod]
    [DataRow("Approve")]
    [DataRow("ApproveAsync")]
    [DataRow("Grant")]
    [DataRow("GrantAuthority")]
    [DataRow("SatisfyPolicy")]
    [DataRow("Execute")]
    [DataRow("RunAsync")]
    [DataRow("Dispatch")]
    [DataRow("DispatchAsync")]
    [DataRow("InvokeTool")]
    [DataRow("InvokeToolAsync")]
    [DataRow("CallModel")]
    [DataRow("BuildPrompt")]
    [DataRow("Transition")]
    [DataRow("TransitionAsync")]
    [DataRow("ApplyPatch")]
    [DataRow("MutateSource")]
    [DataRow("PromoteMemory")]
    [DataRow("ActivateRetrieval")]
    [DataRow("VectorSearch")]
    [DataRow("WriteSql")]
    public void WorkflowCannotGrantAuthority_NoWorkflowTypeExposesForbiddenAuthorityMethod(string forbiddenMethod)
    {
        var methods = typeof(WorkflowStepContract).Assembly.GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow")
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.DoesNotContain(methods, forbiddenMethod);
    }

    [TestMethod]
    public void WorkflowCannotGrantAuthority_ProductionWorkflowFilesDoNotReferenceRuntimeIoOrAuthorityServices()
    {
        var text = ReadAllTextIfDirectoryExists(Path.Combine(RepositoryRoot(), "IronDev.Core", "Workflow"));

        AssertDoesNotContainAny(
            text,
            "SqlConnection",
            "DbConnection",
            "INSERT ",
            "UPDATE ",
            "DELETE ",
            "HttpClient",
            "ProcessStartInfo",
            "File.Write",
            "File.Delete",
            "Directory.CreateDirectory",
            "IHostedService",
            "BackgroundService",
            "ControllerBase",
            "WebApplication",
            "OpenAI",
            "IToolInvoker",
            "IAgentDispatcher",
            "IWorkflowTransitionRecorder",
            "IWorkflowStateWriter",
            "IApprovalMutationService",
            "IPolicySatisfactionService",
            "IMemoryPromotionService",
            "IRetrievalActivationService",
            "IPatchApplyService");
    }

    [TestMethod]
    public void WorkflowCannotGrantAuthority_NoFakeAuthorityPlaceholderIdsAreEmittedByWorkflowCore()
    {
        var text = ReadAllTextIfDirectoryExists(Path.Combine(RepositoryRoot(), "IronDev.Core", "Workflow"));

        AssertDoesNotContainAny(
            text,
            "approval-required",
            "governance-event-required",
            "policy-required",
            "authority-granted",
            "execution-allowed",
            "dispatch-allowed",
            "source-mutation-approved",
            "memory-promotion-approved",
            "retrieval-activation-approved",
            "dry-run-approved",
            "route-approved");
    }

    [TestMethod]
    public void WorkflowCannotGrantAuthority_ModelPropertiesDoNotExposeRawPrivateOrFullPayloadFields()
    {
        var propertyNames = typeof(WorkflowStepContract).Assembly.GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow")
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(property => property.Name)
            .ToArray();

        AssertDoesNotContainAny(propertyNames, "RawPrompt", "RawCompletion", "RawToolOutput", "PrivateReasoning", "HiddenReasoning", "ChainOfThought", "WholePatch", "PatchPayload");
    }

    [TestMethod]
    public void WorkflowCannotGrantAuthority_FakeLookingIdsAreOrdinaryStringsAndGrantNothingWhenKindDoesNotMatch()
    {
        var approval = new WorkflowApprovalHaltEvaluator().Evaluate(new WorkflowApprovalHaltEvaluationRequest
        {
            WorkflowStepId = "workflow-step-001",
            RequiredApprovals = [new() { Kind = WorkflowApprovalRequirementKind.HumanApprovalReference, RequirementId = "authority-granted", SafeSummary = "Human approval evidence required." }],
            AvailableApprovalEvidence = [new() { Kind = WorkflowApprovalRequirementKind.GovernanceEventReference, ReferenceId = "authority-granted", SafeSummary = "Governance event reference only." }]
        });

        Assert.AreEqual(WorkflowApprovalHaltStatus.ApprovalRequiredHalt, approval.Status);
    }

    [TestMethod]
    public void WorkflowCannotGrantAuthority_ReceiptDoesNotOverclaimAuthorityImplementation()
    {
        var text = ReadRepoFile("Docs/receipts/PR125_WORKFLOW_CANNOT_GRANT_AUTHORITY_TEST_PACK_RECEIPT.md");

        StringAssert.Contains(text, "PR125 adds a workflow authority-boundary regression test pack.");
        StringAssert.Contains(text, "Evidence is not approval.");
        StringAssert.Contains(text, "Traceability is not authority.");
        StringAssert.Contains(text, "Validation is not dispatch.");
        StringAssert.Contains(text, "Halt is not approval.");
        StringAssert.Contains(text, "Dry-run is not execution.");
        StringAssert.Contains(text, "Route label is not decision ownership.");
        AssertDoesNotContainAny(text, "Workflow authority is now implemented", "Approval is now enforced", "Execution is now safe", "Tools can now be invoked under governance", "Routing can now drive workflow execution");
    }

    private static string[] PublicMethodNames(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(method => method.Name).OrderBy(name => name).ToArray();

    private static void AssertDoesNotContainAny(IEnumerable<string> values, params string[] markers) => AssertDoesNotContainAny(string.Join("\n", values), markers);

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string ReadAllTextIfDirectoryExists(string directory)
    {
        if (!Directory.Exists(directory))
            return string.Empty;

        return string.Join("\n", Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
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
