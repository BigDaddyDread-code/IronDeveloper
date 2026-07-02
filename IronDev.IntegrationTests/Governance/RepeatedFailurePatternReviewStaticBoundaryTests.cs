using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
public sealed class RepeatedFailurePatternReviewStaticBoundaryTests
{
    [TestMethod]
    public void RepeatedFailurePatternReviewInterface_ExposesOnlyPrepare()
    {
        var methods = typeof(IRepeatedFailurePatternReviewCandidateWorkflow)
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
    [DataRow("QueryHistory")]
    [DataRow("QueryMemory")]
    [DataRow("ReadLogs")]
    [DataRow("ReadReports")]
    [DataRow("RunTests")]
    [DataRow("RunCommand")]
    [DataRow("InvokeTool")]
    [DataRow("Dispatch")]
    [DataRow("CallModel")]
    [DataRow("BuildPrompt")]
    [DataRow("CreateTicket")]
    [DataRow("CreateIncident")]
    [DataRow("PromoteMemory")]
    [DataRow("ActivateRetrieval")]
    [DataRow("SatisfyApproval")]
    [DataRow("SatisfyPolicy")]
    [DataRow("TransitionWorkflow")]
    [DataRow("ApplyPatch")]
    [DataRow("WriteSql")]
    public void RepeatedFailurePatternReviewTypes_DoNotExposeForbiddenMethodNames(string forbiddenName)
    {
        var methodNames = typeof(RepeatedFailurePatternReviewCandidateWorkflow).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" &&
                           type.Name.Contains("RepeatedFailurePatternReview", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.Name)
            .ToArray();

        Assert.IsFalse(methodNames.Contains(forbiddenName, StringComparer.Ordinal), $"Unexpected method {forbiddenName}.");
    }

    [TestMethod]
    public void RepeatedFailurePatternReviewProductionFile_HasNoRuntimeIoStorageApiCliOrRunnerDependencies()
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
            "HistoryQueryService",
            "MemoryQueryService",
            "LogReaderService",
            "ReportReaderService",
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
            "TicketService",
            "IncidentService",
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
    [DataRow("HistoryPayload")]
    [DataRow("MemoryPayload")]
    [DataRow("SourceContent")]
    [DataRow("SourceFileContents")]
    [DataRow("WholePatch")]
    [DataRow("PatchPayload")]
    public void RepeatedFailurePatternReviewModels_DoNotExposeForbiddenPayloadProperties(string forbiddenProperty)
    {
        var propertyNames = typeof(RepeatedFailurePatternReviewCandidateWorkflow).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" &&
                           type.Name.Contains("RepeatedFailure", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();

        Assert.IsFalse(propertyNames.Contains(forbiddenProperty, StringComparer.Ordinal), $"Unexpected property {forbiddenProperty}.");
    }

    [DataTestMethod]
    [DataRow("PatternDetected")]
    [DataRow("PatternProven")]
    [DataRow("RootCauseFound")]
    [DataRow("TicketCreated")]
    [DataRow("IncidentCreated")]
    [DataRow("MemoryPromoted")]
    [DataRow("WorkflowMayContinue")]
    [DataRow("ReleaseReady")]
    public void RepeatedFailurePatternReviewStatus_DoesNotExposeProofActionOrReadinessStates(string forbiddenStatus)
    {
        var statusNames = Enum.GetNames<RepeatedFailurePatternReviewCandidateStatus>();

        Assert.IsFalse(statusNames.Contains(forbiddenStatus, StringComparer.Ordinal), $"Unexpected status {forbiddenStatus}.");
    }

    [TestMethod]
    public void Receipt_StatesRepeatedFailurePatternReviewBoundary()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR134_REPEATED_FAILURE_PATTERN_REVIEW_WORKFLOW_RECEIPT.md"));

        Assert.IsTrue(text.Contains("PR134 adds a Repeated Failure Pattern Review candidate workflow. It turns supplied failure/evidence references and candidate package material into a safe pattern review package for later review.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Repeated failure pattern review is not pattern proof.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Pattern hint is not diagnosis.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Root cause is not proven.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Review output cannot grant authority.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("This is a Block M L4 candidate workflow and remains non-mutating.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("PR134 lays the repeated failure cards on the table. It does not declare the winning hand.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("It does not query history, query memory, read logs, read reports, run tests, run commands, invoke tools, dispatch agents, call models, build prompts, create tickets, create incidents, promote memory, activate retrieval, satisfy approval, satisfy policy, transition workflow state, mutate source, apply patches, write SQL, or add runtime wiring.", StringComparison.Ordinal));
        AssertDoesNotContainAny(text,
            "Pattern detected.",
            "Pattern proven.",
            "Root cause found.",
            "Incident created.",
            "Ticket created.",
            "Memory promoted.",
            "Workflow may continue.",
            "Release ready.");
    }

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }

    private static string ProductionFilePath() =>
        Path.Combine(RepositoryRoot(), "IronDev.Core", "Workflow", "RepeatedFailurePatternReviewCandidateWorkflowModels.cs");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        Assert.IsNotNull(directory, "Could not locate repository root.");
        return directory!.FullName;
    }
}
