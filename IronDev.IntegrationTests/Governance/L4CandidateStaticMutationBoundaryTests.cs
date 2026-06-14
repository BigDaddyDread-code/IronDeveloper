using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class L4CandidateStaticMutationBoundaryTests
{
    private static readonly string[] CandidateProductionFiles =
    [
        "TestFailureReviewCandidateWorkflowModels.cs",
        "CriticReviewRequestCandidateWorkflowModels.cs",
        "ImplementationProposalPackageCandidateWorkflowModels.cs",
        "ToolRequestGatePreviewCandidateWorkflowModels.cs",
        "MemoryImprovementPackageCandidateWorkflowModels.cs",
        "HumanApprovalPackageCandidateWorkflowModels.cs",
        "DogfoodEvidenceBundleCandidateWorkflowModels.cs",
        "RepeatedFailurePatternReviewCandidateWorkflowModels.cs"
    ];

    [TestMethod]
    public void CandidateProductionFiles_HaveNoRuntimeIoStorageApiCliOrRunnerDependencies()
    {
        foreach (var fileName in CandidateProductionFiles)
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Core", "Workflow", fileName));

            AssertDoesNotContainAny(text,
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
                "ToolRegistry.Execute",
                "AgentDispatcher",
                "A2aSender",
                "WorkflowTransitionRecorder",
                "WorkflowStateWriter",
                "ApprovalMutation",
                "ApprovalRepository",
                "SourceMutationService",
                "PatchApplyService",
                "MemoryPromotionService",
                "AcceptedMemoryMutationService",
                "RetrievalActivationService",
                "VectorStoreClient",
                "EmbeddingService",
                "RunReportReader",
                "DogfoodRunner",
                "TestRunner");
        }
    }

    [DataTestMethod]
    [DataRow(typeof(ITestFailureReviewCandidateWorkflow), "Review")]
    [DataRow(typeof(ICriticReviewRequestCandidateWorkflow), "Prepare")]
    [DataRow(typeof(IImplementationProposalPackageCandidateWorkflow), "Prepare")]
    [DataRow(typeof(IToolRequestGatePreviewCandidateWorkflow), "Preview")]
    [DataRow(typeof(IMemoryImprovementPackageCandidateWorkflow), "Prepare")]
    [DataRow(typeof(IHumanApprovalPackageCandidateWorkflow), "Prepare")]
    [DataRow(typeof(IDogfoodEvidenceBundleCandidateWorkflow), "Prepare")]
    [DataRow(typeof(IRepeatedFailurePatternReviewCandidateWorkflow), "Prepare")]
    public void CandidateWorkflowInterfaces_ExposeOnlyAllowedMethod(Type interfaceType, string allowedMethod)
    {
        var methods = interfaceType.GetMethods(BindingFlags.Instance | BindingFlags.Public).Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        CollectionAssert.AreEqual(new[] { allowedMethod }, methods, interfaceType.Name);
    }

    [DataTestMethod]
    [DataRow("Run")]
    [DataRow("RunAsync")]
    [DataRow("Execute")]
    [DataRow("ExecuteAsync")]
    [DataRow("Dispatch")]
    [DataRow("Invoke")]
    [DataRow("InvokeTool")]
    [DataRow("CallModel")]
    [DataRow("BuildPrompt")]
    [DataRow("ApplyPatch")]
    [DataRow("MutateSource")]
    [DataRow("PromoteMemory")]
    [DataRow("ActivateRetrieval")]
    [DataRow("AcceptMemory")]
    [DataRow("UpdateMemory")]
    [DataRow("DeleteMemory")]
    [DataRow("ResolveConflict")]
    [DataRow("RunDogfood")]
    [DataRow("RunTests")]
    [DataRow("ReadLogs")]
    [DataRow("ReadTrace")]
    [DataRow("ReadReport")]
    [DataRow("ReadArtifact")]
    [DataRow("QueryHistory")]
    [DataRow("QueryMemory")]
    [DataRow("SearchMemory")]
    [DataRow("CreateTicket")]
    [DataRow("CreateIncident")]
    [DataRow("Approve")]
    [DataRow("Reject")]
    [DataRow("Deny")]
    [DataRow("GrantApproval")]
    [DataRow("SatisfyApproval")]
    [DataRow("SatisfyPolicy")]
    [DataRow("TransitionWorkflow")]
    [DataRow("ContinueWorkflow")]
    public void CandidateWorkflowTypes_DoNotExposeForbiddenMethodNames(string forbiddenName)
    {
        var methodNames = CandidateTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(method => method.Name)
            .ToArray();

        Assert.IsFalse(methodNames.Contains(forbiddenName, StringComparer.Ordinal), $"Unexpected method {forbiddenName}.");
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
    [DataRow("StdOut")]
    [DataRow("StdErr")]
    [DataRow("SourceContent")]
    [DataRow("SourceFileContents")]
    [DataRow("PatchPayload")]
    [DataRow("WholePatch")]
    [DataRow("DiffPayload")]
    [DataRow("ApprovalReceipt")]
    [DataRow("ApprovalResult")]
    [DataRow("PolicySatisfaction")]
    [DataRow("AcceptedMemoryPayload")]
    [DataRow("RawMemory")]
    [DataRow("EmbeddingVector")]
    [DataRow("VectorPayload")]
    [DataRow("HistoryQuery")]
    [DataRow("MemoryQuery")]
    [DataRow("ToolInputPayload")]
    [DataRow("ToolOutputPayload")]
    public void CandidateModels_DoNotExposeForbiddenPayloadOrAuthorityProperties(string forbiddenPropertyFragment)
    {
        var propertyNames = CandidateTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();

        Assert.IsFalse(propertyNames.Any(name => name.Contains(forbiddenPropertyFragment, StringComparison.Ordinal)), $"Unexpected property fragment {forbiddenPropertyFragment}.");
    }

    [DataTestMethod]
    [DataRow("Approved")]
    [DataRow("Rejected")]
    [DataRow("Denied")]
    [DataRow("ApprovalGranted")]
    [DataRow("ApprovalSatisfied")]
    [DataRow("PolicySatisfied")]
    [DataRow("WorkflowContinued")]
    [DataRow("ToolExecuted")]
    [DataRow("ToolAuthorized")]
    [DataRow("CommandRun")]
    [DataRow("Implemented")]
    [DataRow("PatchReady")]
    [DataRow("CodeGenerated")]
    [DataRow("Applied")]
    [DataRow("MemoryPromoted")]
    [DataRow("AcceptedMemoryUpdated")]
    [DataRow("RetrievalActivated")]
    [DataRow("SqlWritten")]
    [DataRow("ValidationPassed")]
    [DataRow("ReleaseReady")]
    [DataRow("PatternDetected")]
    [DataRow("PatternProven")]
    [DataRow("RootCauseFound")]
    [DataRow("IncidentCreated")]
    [DataRow("TicketCreated")]
    public void CandidateStatusEnums_DoNotExposeAuthorityExecutionProofOrReadinessStates(string forbiddenStatus)
    {
        var statusNames = CandidateTypes()
            .Where(type => type.IsEnum && type.Name.EndsWith("CandidateStatus", StringComparison.Ordinal))
            .SelectMany(Enum.GetNames)
            .ToArray();

        Assert.IsFalse(statusNames.Contains(forbiddenStatus, StringComparer.Ordinal), $"Unexpected status {forbiddenStatus}.");
    }

    [TestMethod]
    public void Receipt_StatesPr135IsTestsAndDocsOnly()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR135_L4_CANDIDATE_CANNOT_MUTATE_SOURCE_OR_MEMORY_TESTS.md"));

        Assert.IsTrue(text.Contains("PR135 adds regression tests proving Block M L4 candidate workflow outputs cannot mutate source or memory.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("This PR adds no new workflow capability.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Candidate packages are review material only.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Candidate outputs cannot satisfy approval, satisfy policy, transition workflow, invoke tools, dispatch agents, call models, mutate source, apply patches, promote memory, mutate accepted memory, activate retrieval, create tickets, create incidents, write SQL, prove validation, prove release readiness, prove root cause, or prove repeated patterns.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("Evidence is not approval. Proposal is not implementation. Preview is not execution. Package is not promotion. Approval package is not approval. Bundle is not proof. Pattern review is not diagnosis.", StringComparison.Ordinal));
        Assert.IsTrue(text.Contains("PR135 checks the locks on every Block M package. It does not add a key.", StringComparison.Ordinal));
        AssertDoesNotContainAny(text,
            "L4 workflow execution is complete.",
            "Candidates are ready for source apply.",
            "Memory improvement can be promoted.",
            "Human approval is satisfied.",
            "Dogfood proves release readiness.",
            "Repeated failure pattern is proven.");
    }

    private static IReadOnlyList<Type> CandidateTypes() =>
    [
        typeof(TestFailureReviewCandidateWorkflow),
        typeof(TestFailureReviewCandidateRequest),
        typeof(TestFailureEvidenceItem),
        typeof(TestFailureReviewCandidateResult),
        typeof(TestFailureReviewCandidateStatus),
        typeof(CriticReviewRequestCandidateWorkflow),
        typeof(CriticReviewRequestCandidateRequest),
        typeof(CriticReviewQuestion),
        typeof(CriticReviewEvidenceReference),
        typeof(CriticReviewRequestCandidateResult),
        typeof(CriticReviewRequestCandidateStatus),
        typeof(ImplementationProposalPackageCandidateWorkflow),
        typeof(ImplementationProposalPackageCandidateRequest),
        typeof(ImplementationProposalPackageCandidateResult),
        typeof(ImplementationProposalPackageCandidateStatus),
        typeof(ToolRequestGatePreviewCandidateWorkflow),
        typeof(ToolRequestGatePreviewCandidateRequest),
        typeof(ToolRequestGatePreviewCandidateResult),
        typeof(ToolRequestGatePreviewCandidateStatus),
        typeof(MemoryImprovementPackageCandidateWorkflow),
        typeof(MemoryImprovementPackageCandidateRequest),
        typeof(MemoryImprovementPackageCandidateResult),
        typeof(MemoryImprovementPackageCandidateStatus),
        typeof(HumanApprovalPackageCandidateWorkflow),
        typeof(HumanApprovalPackageCandidateRequest),
        typeof(HumanApprovalPackageCandidateResult),
        typeof(HumanApprovalPackageCandidateStatus),
        typeof(DogfoodEvidenceBundleCandidateWorkflow),
        typeof(DogfoodEvidenceBundleCandidateRequest),
        typeof(DogfoodEvidenceBundleCandidateResult),
        typeof(DogfoodEvidenceBundleCandidateStatus),
        typeof(RepeatedFailurePatternReviewCandidateWorkflow),
        typeof(RepeatedFailurePatternReviewCandidateRequest),
        typeof(RepeatedFailurePatternReviewCandidateResult),
        typeof(RepeatedFailurePatternReviewCandidateStatus)
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
