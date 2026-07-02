using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
public sealed class MemoryImprovementPackageStaticBoundaryTests
{
    [TestMethod]
    public void CandidateWorkflowInterface_ExposesOnlyPrepare()
    {
        var methods = typeof(IMemoryImprovementPackageCandidateWorkflow)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Prepare" }, methods);
    }

    [DataTestMethod]
    [DataRow("Promote")]
    [DataRow("PromoteMemory")]
    [DataRow("AcceptMemory")]
    [DataRow("MutateMemory")]
    [DataRow("UpdateMemory")]
    [DataRow("DeleteMemory")]
    [DataRow("ResolveConflict")]
    [DataRow("ActivateRetrieval")]
    [DataRow("GenerateEmbedding")]
    [DataRow("QueryVectorStore")]
    [DataRow("Run")]
    [DataRow("RunAsync")]
    [DataRow("Execute")]
    [DataRow("ExecuteAsync")]
    [DataRow("Dispatch")]
    [DataRow("InvokeTool")]
    [DataRow("CallModel")]
    [DataRow("BuildPrompt")]
    [DataRow("CreateTicket")]
    [DataRow("ApplyPatch")]
    public void CandidateWorkflowTypes_DoNotExposeForbiddenMethodNames(string forbiddenName)
    {
        var methodNames = typeof(MemoryImprovementPackageCandidateWorkflow).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" &&
                           type.Name.Contains("MemoryImprovementPackage", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.Name)
            .ToArray();

        Assert.IsFalse(methodNames.Contains(forbiddenName, StringComparer.Ordinal), $"Unexpected method {forbiddenName}.");
    }

    [TestMethod]
    public void ProductionCandidateWorkflow_HasNoRuntimeStorageToolRetrievalOrAuthorityDependencies()
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
            "ApprovalMutation",
            "MemoryPromotionService",
            "AcceptedMemoryMutationService",
            "MemoryPromotionWriter",
            "RetrievalActivationService",
            "VectorStoreClient",
            "EmbeddingService",
            "DuplicateResolverService",
            "ConflictResolverService",
            "StaleMemoryMarkerService");
    }

    [DataTestMethod]
    [DataRow("RawPrompt")]
    [DataRow("RawCompletion")]
    [DataRow("RawToolOutput")]
    [DataRow("PrivateReasoning")]
    [DataRow("HiddenReasoning")]
    [DataRow("ChainOfThought")]
    [DataRow("RawMemoryPayload")]
    [DataRow("AcceptedMemoryPayload")]
    [DataRow("SourceContent")]
    [DataRow("SourceFileContents")]
    [DataRow("WholePatch")]
    [DataRow("PatchPayload")]
    public void CandidateWorkflowModels_DoNotExposeForbiddenPayloadProperties(string forbiddenProperty)
    {
        var propertyNames = typeof(MemoryImprovementPackageCandidateWorkflow).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" &&
                           type.Name.Contains("MemoryImprovement", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();

        Assert.IsFalse(propertyNames.Contains(forbiddenProperty, StringComparer.Ordinal), $"Unexpected property {forbiddenProperty}.");
    }

    [DataTestMethod]
    [DataRow("MemoryPromoted")]
    [DataRow("AcceptedMemoryUpdated")]
    [DataRow("MemoryDeleted")]
    [DataRow("DuplicateResolved")]
    [DataRow("ConflictResolved")]
    [DataRow("RetrievalActivated")]
    [DataRow("SqlWritten")]
    public void CandidateStatus_DoesNotExposePromotionResolutionOrWriteStates(string forbiddenStatus)
    {
        var statusNames = Enum.GetNames<MemoryImprovementPackageCandidateStatus>();

        Assert.IsFalse(statusNames.Contains(forbiddenStatus, StringComparer.Ordinal), $"Unexpected status {forbiddenStatus}.");
    }

    [TestMethod]
    public void Receipt_DoesNotOverclaimMemoryPromotionOrWorkflowAuthority()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR131_MEMORY_IMPROVEMENT_PACKAGE_WORKFLOW_RECEIPT.md"));

        Assert.IsTrue(text.Contains("Memory improvement package is not memory promotion.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Package output cannot mutate accepted memory.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Package output cannot activate retrieval.", StringComparison.Ordinal));
        AssertDoesNotContainAny(text,
            "Memory is promoted.",
            "Accepted memory is updated.",
            "SQL is written.",
            "Vector index is written.",
            "Retrieval is activated.",
            "Duplicate is resolved.",
            "Conflict is resolved.",
            "Approval is satisfied.",
            "Policy is satisfied.",
            "Workflow may continue.");
    }

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }

    private static string ProductionFilePath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Core", "Workflow", "MemoryImprovementPackageCandidateWorkflowModels.cs");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        Assert.IsNotNull(directory, "Could not locate repository root.");
        return directory!.FullName;
    }
}
