using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
[TestCategory("BoxedLangGraphRoutingAdapter")]
public sealed class BoxedLangGraphRoutingAdapterStaticBoundaryTests
{
    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_CoreInterfacesExposeOnlyAllowedMethods()
    {
        CollectionAssert.AreEqual(new[] { "Evaluate" }, PublicMethodNames(typeof(IWorkflowRunnerSkeleton)));
        CollectionAssert.AreEqual(new[] { "ExecuteDryRun" }, PublicMethodNames(typeof(IWorkflowDryRunExecutor)));
        CollectionAssert.AreEqual(new[] { "SuggestRoute" }, PublicMethodNames(typeof(IBoxedLangGraphRoutingAdapter)));
    }

    [DataTestMethod]
    [DataRow("Run")]
    [DataRow("RunAsync")]
    [DataRow("Execute")]
    [DataRow("ExecuteAsync")]
    [DataRow("Dispatch")]
    [DataRow("DispatchAsync")]
    [DataRow("InvokeTool")]
    [DataRow("InvokeToolAsync")]
    [DataRow("Transition")]
    [DataRow("Approve")]
    [DataRow("Apply")]
    [DataRow("Promote")]
    [DataRow("Retrieve")]
    public void BoxedLangGraphRoutingAdapter_HasNoForbiddenMethodName(string methodName)
    {
        var names = PublicMethodNames(typeof(BoxedLangGraphRoutingAdapter))
            .Concat(PublicMethodNames(typeof(IBoxedLangGraphRoutingAdapter)))
            .ToArray();

        CollectionAssert.DoesNotContain(names, methodName);
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_ProductionFileAddsNoRuntimeStorageToolModelSourceMemoryRetrievalOrPromptSurface()
    {
        var text = ReadRepoFile("IronDev.Core/Workflow/BoxedLangGraphRoutingAdapterModels.cs");

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
            "ControllerBase",
            "WebApplication",
            "IHostedService",
            "BackgroundService",
            "Scheduler",
            "Orchestrator",
            "IAgentDispatcher",
            "IActorResolver",
            "IToolInvoker",
            "IModelClient",
            "PromptBuilder",
            "IWorkflowTransitionRecorder",
            "IWorkflowStateWriter",
            "IApprovalDecisionStore",
            "IPolicySatisfactionService",
            "ISourceMutationService",
            "IPatchApplyService",
            "IMemoryPromotionService",
            "IRetrievalService",
            "IVectorSearch",
            "IEmbeddingClient",
            "InMemoryAuthority",
            "SyntheticEvidence",
            "PlaceholderId");
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_AddsNoSqlMigrationApiCliInfrastructureOrRuntimeReference()
    {
        var root = RepositoryRoot();
        var databaseText = ReadAllTextIfDirectoryExists(Path.Combine(root, "Database"));
        var apiText = ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Api"));
        var cliText = ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Cli")) +
                      ReadAllTextIfDirectoryExists(Path.Combine(root, "tools", "IronDev.Cli"));
        var infrastructureText = ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Infrastructure"));

        AssertDoesNotContainAny(databaseText, "BoxedLangGraphRoutingAdapter", "IBoxedLangGraphRoutingAdapter");
        AssertDoesNotContainAny(apiText, "BoxedLangGraphRoutingAdapter", "IBoxedLangGraphRoutingAdapter");
        AssertDoesNotContainAny(cliText, "BoxedLangGraphRoutingAdapter", "IBoxedLangGraphRoutingAdapter");
        AssertDoesNotContainAny(infrastructureText, "BoxedLangGraphRoutingAdapter", "IBoxedLangGraphRoutingAdapter");
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_ModelsDoNotExposeRawPrivateOrPatchPayloadFields()
    {
        var propertyNames = new[]
        {
            typeof(BoxedLangGraphRoutingRequest),
            typeof(BoxedLangGraphRouteSuggestion)
        }
        .SelectMany(type => type.GetProperties())
        .Select(property => property.Name);

        AssertDoesNotContainAny(
            propertyNames,
            "RawPrompt",
            "RawCompletion",
            "RawToolOutput",
            "PrivateReasoning",
            "HiddenReasoning",
            "ChainOfThought",
            "WholePatch",
            "PatchPayload");
    }

    [TestMethod]
    public void BoxedLangGraphRoutingAdapter_ReceiptDoesNotOverclaimRoutingOrWorkflowAuthority()
    {
        var text = ReadRepoFile("Docs/receipts/PR124_BOXED_LANGGRAPH_ROUTING_ADAPTER_SPIKE_RECEIPT.md");

        StringAssert.Contains(text, "PR124 adds a boxed LangGraph-style routing adapter spike.");
        StringAssert.Contains(text, "The adapter is optional and deletable.");
        StringAssert.Contains(text, "If the adapter starts owning decisions, delete it.");
        AssertDoesNotContainAny(
            text,
            "LangGraph now routes workflows",
            "LangGraph decides the next step",
            "Graph execution is supported",
            "The adapter triggers dry-run execution",
            "The adapter dispatches agents",
            "The adapter owns workflow orchestration");
    }

    private static string[] PublicMethodNames(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

    private static void AssertDoesNotContainAny(IEnumerable<string> values, params string[] markers) =>
        AssertDoesNotContainAny(string.Join("\n", values), markers);

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
