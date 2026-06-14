using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class SourceApplyApprovalRequirementStaticBoundaryTests
{
    private static readonly string ProductionFile = Path.Combine("IronDev.Core", "Workflow", "SourceApplyApprovalRequirementContractModels.cs");

    [TestMethod]
    public void ProductionFile_Exists()
    {
        Assert.IsTrue(File.Exists(Path.Combine(RepositoryRoot(), ProductionFile)), "PR137 production contract file must exist.");
    }

    [TestMethod]
    public void ProductionFile_HasNoIoRuntimeApiCliOrProviderDependencies()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), ProductionFile));

        AssertDoesNotContainAny(
            text,
            "ProcessStartInfo",
            "HttpClient",
            "SqlConnection",
            "DbConnection",
            "File.ReadAllText",
            "File.Write",
            "File.Delete",
            "Directory.Enumerate",
            "Directory.GetFiles",
            "Directory.CreateDirectory",
            "IHostedService",
            "BackgroundService",
            "ControllerBase",
            "WebApplication",
            "OpenAI",
            "ChatCompletion",
            "ToolInvoker",
            "AgentDispatcher",
            "A2aSender",
            "WorkflowTransitionWriter",
            "ApprovalMutation",
            "ApprovalRepository",
            "VectorStore",
            "Embedding",
            "GitHub",
            " CI ");
    }

    [TestMethod]
    public void ContractInterface_ExposesEvaluateOnly()
    {
        var methods = typeof(ISourceApplyApprovalRequirementContract)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Evaluate" }, methods);
    }

    [DataTestMethod]
    [DataRow("Apply")]
    [DataRow("ApplySource")]
    [DataRow("ApplyPatch")]
    [DataRow("Approve")]
    [DataRow("Reject")]
    [DataRow("GrantApproval")]
    [DataRow("SatisfyApproval")]
    [DataRow("SatisfyPolicy")]
    [DataRow("ContinueWorkflow")]
    [DataRow("TransitionWorkflow")]
    [DataRow("Run")]
    [DataRow("RunAsync")]
    [DataRow("Execute")]
    [DataRow("ExecuteAsync")]
    [DataRow("InvokeTool")]
    [DataRow("DispatchAgent")]
    [DataRow("CallModel")]
    [DataRow("BuildPrompt")]
    [DataRow("CreateTicket")]
    [DataRow("PromoteMemory")]
    [DataRow("ActivateRetrieval")]
    public void ContractTypes_DoNotExposeForbiddenMethodNames(string forbiddenMethod)
    {
        var methodNames = ContractTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(method => method.Name)
            .ToArray();

        Assert.IsFalse(methodNames.Contains(forbiddenMethod, StringComparer.Ordinal), $"Unexpected method {forbiddenMethod}.");
    }

    [DataTestMethod]
    [DataRow("PatchPayload")]
    [DataRow("DiffPayload")]
    [DataRow("SourceContent")]
    [DataRow("SourceFileContents")]
    [DataRow("CommandPayload")]
    [DataRow("RawPrompt")]
    [DataRow("RawCompletion")]
    [DataRow("RawToolOutput")]
    [DataRow("PrivateReasoning")]
    [DataRow("HiddenReasoning")]
    [DataRow("ChainOfThought")]
    [DataRow("ApprovalReceipt")]
    [DataRow("ApprovalResult")]
    [DataRow("AcceptedApprovalRecord")]
    [DataRow("ApprovedBy")]
    [DataRow("ApprovalGrantedAt")]
    public void ContractModels_DoNotExposeForbiddenPayloadOrApprovalRecordProperties(string forbiddenProperty)
    {
        var propertyNames = ContractTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();

        Assert.IsFalse(propertyNames.Contains(forbiddenProperty, StringComparer.Ordinal), $"Unexpected property {forbiddenProperty}.");
    }

    [DataTestMethod]
    [DataRow("Approved")]
    [DataRow("ApprovalSatisfied")]
    [DataRow("SourceApplyReady")]
    [DataRow("SourceApplied")]
    [DataRow("PatchApplied")]
    [DataRow("WorkflowContinued")]
    [DataRow("PolicySatisfied")]
    public void RequirementStatus_DoesNotExposeReadinessExecutionOrSatisfactionStates(string forbiddenStatus)
    {
        var statusNames = Enum.GetNames<SourceApplyApprovalRequirementStatus>();

        Assert.IsFalse(statusNames.Contains(forbiddenStatus, StringComparer.Ordinal), $"Unexpected status {forbiddenStatus}.");
    }

    [TestMethod]
    public void Receipt_StatesSourceApplyRemainsUnimplemented()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR137_SOURCE_APPLY_APPROVAL_REQUIREMENT_CONTRACT.md"));

        StringAssert.Contains(text, "PR137 adds the Source Apply Approval Requirement contract.");
        StringAssert.Contains(text, "Source apply requires explicit approval.");
        StringAssert.Contains(text, "Human approval package is not approval.");
        StringAssert.Contains(text, "Implementation proposal is not implementation.");
        StringAssert.Contains(text, "Requirement result is not approval satisfaction.");
        StringAssert.Contains(text, "Requirement result is not source apply.");
        StringAssert.Contains(text, "Source apply remains unimplemented.");
        StringAssert.Contains(text, "PR137 installs the source-apply brake. It does not press the accelerator.");
    }

    [TestMethod]
    public void Receipt_DoesNotOverclaimReadiness()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR137_SOURCE_APPLY_APPROVAL_REQUIREMENT_CONTRACT.md"));

        AssertDoesNotContainAny(
            text,
            "Source apply is ready",
            "Source applied",
            "Patch applied",
            "Approval satisfied",
            "Policy satisfied",
            "Workflow continued",
            "Implementation proposal can be applied",
            "Human approval package satisfies");
    }

    private static IReadOnlyList<Type> ContractTypes() =>
    [
        typeof(SourceApplyApprovalRequirementContract),
        typeof(ISourceApplyApprovalRequirementContract),
        typeof(SourceApplyApprovalRequirementRequest),
        typeof(SourceApplyApprovalRequirementResult),
        typeof(SourceApplyApprovalEvidenceReference),
        typeof(SourceApplyApprovalGateHint),
        typeof(SourceApplyApprovalRisk)
    ];

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        Assert.IsNotNull(directory, "Could not locate repository root.");
        return directory!.FullName;
    }
}
