using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
public sealed class HumanApprovalPackageStaticBoundaryTests
{
    [TestMethod]
    public void HumanApprovalPackageInterface_ExposesOnlyPrepare()
    {
        var methods = typeof(IHumanApprovalPackageCandidateWorkflow)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Prepare" }, methods);
    }

    [DataTestMethod]
    [DataRow("Approve")]
    [DataRow("Reject")]
    [DataRow("Deny")]
    [DataRow("GrantApproval")]
    [DataRow("SatisfyApproval")]
    [DataRow("SatisfyPolicy")]
    [DataRow("ContinueWorkflow")]
    [DataRow("TransitionWorkflow")]
    [DataRow("Run")]
    [DataRow("RunAsync")]
    [DataRow("Execute")]
    [DataRow("ExecuteAsync")]
    [DataRow("Dispatch")]
    [DataRow("InvokeTool")]
    [DataRow("CallModel")]
    [DataRow("BuildPrompt")]
    [DataRow("ApplyPatch")]
    [DataRow("PromoteMemory")]
    [DataRow("ActivateRetrieval")]
    [DataRow("WriteSql")]
    public void HumanApprovalPackageTypes_DoNotExposeForbiddenMethodNames(string forbiddenName)
    {
        var methodNames = typeof(HumanApprovalPackageCandidateWorkflow).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" &&
                           type.Name.Contains("HumanApproval", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.Name)
            .ToArray();

        Assert.IsFalse(methodNames.Contains(forbiddenName, StringComparer.Ordinal), $"Unexpected method {forbiddenName}.");
    }

    [TestMethod]
    public void HumanApprovalPackageProductionFile_HasNoRuntimeStorageToolOrAuthorityDependencies()
    {
        var text = File.ReadAllText(ProductionFilePath());

        AssertDoesNotContainAny(text,
            "ProcessStartInfo",
            "HttpClient",
            "SqlConnection",
            "DbConnection",
            "File.ReadAllText",
            "File.Write",
            "Directory.CreateDirectory",
            "IHostedService",
            "BackgroundService",
            "ControllerBase",
            "WebApplication",
            "CommandLine",
            "ToolInvoker",
            "IToolInvoker",
            "ToolRegistry",
            "IToolRegistry",
            "AgentDispatcher",
            "IAgentDispatcher",
            "ModelClient",
            "IModelClient",
            "PromptBuilder",
            "IPromptBuilder",
            "A2aSender",
            "IA2aSender",
            "GitHubClient",
            "WorkflowTransitionWriter",
            "ApprovalDecisionStore",
            "ApprovalMutation",
            "PolicyDecisionWriter",
            "MemoryPromotionService",
            "RetrievalActivationService",
            "VectorStoreClient",
            "EmbeddingService");
    }

    [DataTestMethod]
    [DataRow("RawPrompt")]
    [DataRow("RawCompletion")]
    [DataRow("RawToolOutput")]
    [DataRow("PrivateReasoning")]
    [DataRow("HiddenReasoning")]
    [DataRow("ChainOfThought")]
    [DataRow("SourceContent")]
    [DataRow("SourceFileContents")]
    [DataRow("WholePatch")]
    [DataRow("PatchPayload")]
    [DataRow("ApprovalDecisionPayload")]
    [DataRow("PolicyDecisionPayload")]
    public void HumanApprovalPackageModels_DoNotExposeForbiddenPayloadProperties(string forbiddenProperty)
    {
        var propertyNames = typeof(HumanApprovalPackageCandidateWorkflow).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" &&
                           type.Name.Contains("HumanApproval", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();

        Assert.IsFalse(propertyNames.Contains(forbiddenProperty, StringComparer.Ordinal), $"Unexpected property {forbiddenProperty}.");
    }

    [DataTestMethod]
    [DataRow("Approved")]
    [DataRow("Rejected")]
    [DataRow("Denied")]
    [DataRow("ApprovalSatisfied")]
    [DataRow("PolicySatisfied")]
    [DataRow("WorkflowContinued")]
    [DataRow("WorkflowTransitioned")]
    [DataRow("SourceMutated")]
    [DataRow("PatchApplied")]
    [DataRow("ToolInvoked")]
    [DataRow("AgentDispatched")]
    [DataRow("ModelCalled")]
    [DataRow("PromptBuilt")]
    [DataRow("TicketCreated")]
    [DataRow("MemoryPromoted")]
    [DataRow("RetrievalActivated")]
    [DataRow("SqlWritten")]
    public void HumanApprovalPackageStatus_DoesNotExposeActionStates(string forbiddenStatus)
    {
        var statusNames = Enum.GetNames<HumanApprovalPackageCandidateStatus>();

        Assert.IsFalse(statusNames.Contains(forbiddenStatus, StringComparer.Ordinal), $"Unexpected status {forbiddenStatus}.");
    }

    [DataTestMethod]
    [DataRow("RequestHumanReview")]
    [DataRow("RequestApproveOrRejectLater")]
    [DataRow("RequestMoreEvidenceLater")]
    [DataRow("RequestPolicyReviewLater")]
    public void HumanApprovalRequestedDecision_NamesStayRequestShaped(string expectedName)
    {
        var names = Enum.GetNames<HumanApprovalRequestedDecision>();

        CollectionAssert.Contains(names, expectedName);
    }

    [DataTestMethod]
    [DataRow("Approved")]
    [DataRow("Rejected")]
    [DataRow("Denied")]
    [DataRow("Granted")]
    [DataRow("Satisfied")]
    [DataRow("Continued")]
    public void HumanApprovalRequestedDecision_DoesNotExposeCompletedDecisionNames(string forbiddenName)
    {
        var names = Enum.GetNames<HumanApprovalRequestedDecision>();

        Assert.IsFalse(names.Contains(forbiddenName, StringComparer.Ordinal), $"Unexpected decision name {forbiddenName}.");
    }

    [TestMethod]
    public void Receipt_StatesApprovalPackageBoundary()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR132_HUMAN_APPROVAL_PACKAGE_WORKFLOW_RECEIPT.md"));

        Assert.IsTrue(text.Contains("Approval package is not approval.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Requested decision is not decision made.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Gate hint is not gate satisfaction.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Package output cannot grant authority.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("This is a Block M L4 candidate workflow and remains non-mutating.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("PR132 prepares the approval folder. It does not sign it.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("It does not approve, reject, deny, satisfy approval, satisfy policy, transition workflow state, mutate source, apply patches, invoke tools, dispatch agents, call models, build prompts, create tickets, promote memory, activate retrieval, write SQL, or add runtime wiring.", StringComparison.Ordinal));
        AssertDoesNotContainAny(text,
            "Approval is granted.",
            "Request is approved.",
            "Approval is satisfied.",
            "Policy is satisfied.",
            "Workflow may continue.",
            "Source is mutated.",
            "Patch is applied.",
            "Tool is invoked.",
            "Memory is promoted.",
            "Retrieval is activated.");
    }

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }

    private static string ProductionFilePath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Core", "Workflow", "HumanApprovalPackageCandidateWorkflowModels.cs");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        Assert.IsNotNull(directory, "Could not locate repository root.");
        return directory!.FullName;
    }
}
