using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class DogfoodEvidenceBundleStaticBoundaryTests
{
    [TestMethod]
    public void DogfoodEvidenceBundleInterface_ExposesOnlyPrepare()
    {
        var methods = typeof(IDogfoodEvidenceBundleCandidateWorkflow)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Prepare" }, methods);
    }

    [DataTestMethod]
    [DataRow("Run")]
    [DataRow("RunAsync")]
    [DataRow("Execute")]
    [DataRow("ExecuteAsync")]
    [DataRow("RunDogfood")]
    [DataRow("RunTests")]
    [DataRow("ReadLogs")]
    [DataRow("ReadTrace")]
    [DataRow("ReadReport")]
    [DataRow("ReadArtifact")]
    [DataRow("FetchGithub")]
    [DataRow("FetchCi")]
    [DataRow("InvokeTool")]
    [DataRow("Dispatch")]
    [DataRow("CallModel")]
    [DataRow("BuildPrompt")]
    [DataRow("Approve")]
    [DataRow("SatisfyPolicy")]
    [DataRow("TransitionWorkflow")]
    [DataRow("CreateTicket")]
    [DataRow("PromoteMemory")]
    [DataRow("ActivateRetrieval")]
    [DataRow("ApplyPatch")]
    public void DogfoodEvidenceBundleTypes_DoNotExposeForbiddenMethodNames(string forbiddenName)
    {
        var methodNames = typeof(DogfoodEvidenceBundleCandidateWorkflow).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" &&
                           type.Name.Contains("DogfoodEvidenceBundle", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.Name)
            .ToArray();

        Assert.IsFalse(methodNames.Contains(forbiddenName, StringComparer.Ordinal), $"Unexpected method {forbiddenName}.");
    }

    [TestMethod]
    public void DogfoodEvidenceBundleProductionFile_HasNoRuntimeIoStorageApiCliOrRunnerDependencies()
    {
        var text = File.ReadAllText(ProductionFilePath());

        AssertDoesNotContainAny(text,
            "ProcessStartInfo",
            "HttpClient",
            "SqlConnection",
            "DbConnection",
            "File.ReadAllText",
            "File.Write",
            "Directory.Enumerate",
            "Directory.GetFiles",
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
            "RunReportService",
            "WorkflowApi",
            "DogfoodRunner",
            "TestRunner",
            "WorkflowTransitionWriter",
            "ApprovalMutation",
            "PolicySatisfactionService",
            "SourceMutationService",
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
    [DataRow("RawLog")]
    [DataRow("RawTrace")]
    [DataRow("RawReport")]
    [DataRow("ArtifactPayload")]
    [DataRow("CommandPayload")]
    [DataRow("Stdout")]
    [DataRow("Stderr")]
    [DataRow("SourceContent")]
    [DataRow("SourceFileContents")]
    [DataRow("WholePatch")]
    [DataRow("PatchPayload")]
    public void DogfoodEvidenceBundleModels_DoNotExposeForbiddenPayloadProperties(string forbiddenProperty)
    {
        var propertyNames = typeof(DogfoodEvidenceBundleCandidateWorkflow).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" &&
                           type.Name.Contains("Dogfood", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();

        Assert.IsFalse(propertyNames.Contains(forbiddenProperty, StringComparer.Ordinal), $"Unexpected property {forbiddenProperty}.");
    }

    [DataTestMethod]
    [DataRow("DogfoodRun")]
    [DataRow("ValidationPassed")]
    [DataRow("ReleaseReady")]
    [DataRow("TestsPassed")]
    [DataRow("Approved")]
    [DataRow("WorkflowContinued")]
    public void DogfoodEvidenceBundleStatus_DoesNotExposeExecutionOrReadinessStates(string forbiddenStatus)
    {
        var statusNames = Enum.GetNames<DogfoodEvidenceBundleCandidateStatus>();

        Assert.IsFalse(statusNames.Contains(forbiddenStatus, StringComparer.Ordinal), $"Unexpected status {forbiddenStatus}.");
    }

    [TestMethod]
    public void Receipt_StatesDogfoodEvidenceBundleBoundary()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR133_DOGFOOD_EVIDENCE_BUNDLE_WORKFLOW_RECEIPT.md"));

        Assert.IsTrue(text.Contains("PR133 adds a Dogfood Evidence Bundle candidate workflow. It turns supplied dogfood evidence references and candidate package material into a safe evidence bundle for later review.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Evidence bundle is not evidence creation.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Supplied validation outcome is not validation proof.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Evidence bundle is not release readiness.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Bundle output cannot grant authority.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("This is a Block M L4 candidate workflow and remains non-mutating.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("PR133 bundles the dogfood receipts. It does not run the dog.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("It does not run dogfood, run tests, run commands, read files, read logs, read traces, read artifacts, invoke tools, dispatch agents, call models, build prompts, satisfy approval, satisfy policy, transition workflow state, mutate source, apply patches, create tickets, promote memory, activate retrieval, write SQL, or add runtime wiring.", StringComparison.Ordinal));
        AssertDoesNotContainAny(text,
            "Dogfood ran.",
            "Tests passed.",
            "Validation passed.",
            "Release ready.",
            "Approved.",
            "Workflow may continue.",
            "Source mutation authorized.",
            "Tool execution authorized.");
    }

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }

    private static string ProductionFilePath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Core", "Workflow", "DogfoodEvidenceBundleCandidateWorkflowModels.cs");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        Assert.IsNotNull(directory, "Could not locate repository root.");
        return directory!.FullName;
    }
}
