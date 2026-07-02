using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
[TestCategory("WorkflowA2aHandoff")]
public sealed class WorkflowA2aHandoffStaticBoundaryTests
{
    [TestMethod]
    public void WorkflowA2aHandoff_InterfaceExposesValidateOnly()
    {
        var methods = typeof(IWorkflowA2aHandoffValidator)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Validate" }, methods);
    }

    [TestMethod]
    public void WorkflowA2aHandoff_ValidatorHasNoDispatchExecuteRunRouteResolveOrInvokeMethods()
    {
        var methodNames = typeof(WorkflowA2aHandoffValidator)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        AssertDoesNotContainAny(methodNames, "Send", "SendHandoff", "Dispatch", "DispatchHandoff", "DispatchAsync", "RouteToAgent", "ResolveAgent", "ResolveAgentAsync", "ExecuteAsync", "RunAsync", "InvokeToolAsync");
    }

    [TestMethod]
    public void WorkflowA2aHandoff_ProductionFileHasNoRuntimeStorageModelRetrievalSourceMutationOrPromotionDependencies()
    {
        var text = ReadRepoFile("IronDev.Core", "Workflow", "WorkflowA2aHandoffValidationModels.cs");

        AssertDoesNotContainAny(
            text,
            "SqlConnection",
            "DbConnection",
            "CommandType",
            "INSERT ",
            "UPDATE ",
            "DELETE ",
            "ControllerBase",
            "WebApplication",
            "IHostedService",
            "BackgroundService",
            "HttpClient",
            "OpenAI",
            "ModelClient",
            "PromptBuilder",
            "VectorSearch",
            "Embedding",
            "Weaviate",
            "PromoteMemory",
            "ApplyPatch",
            "SourceMutation",
            "ToolInvoker",
            "IAgentDispatcher",
            "IWorkflowTransitionRecorder",
            "IApprovalDecisionStore",
            "IPolicyDecisionStore");
    }

    [TestMethod]
    public void WorkflowA2aHandoff_ApiCliAndInfrastructureDoNotReferenceValidatorSurface()
    {
        var root = RepositoryRoot();
        var apiText = ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Api"));
        var cliText = ReadAllTextIfDirectoryExists(Path.Combine(root, "tools"));
        var infrastructureText = ReadAllTextIfDirectoryExists(Path.Combine(root, "IronDev.Infrastructure"));

        AssertDoesNotContainAny(apiText, "WorkflowA2aHandoffValidator", "IWorkflowA2aHandoffValidator");
        AssertDoesNotContainAny(cliText, "WorkflowA2aHandoffValidator", "IWorkflowA2aHandoffValidator");
        AssertDoesNotContainAny(infrastructureText, "WorkflowA2aHandoffValidator", "IWorkflowA2aHandoffValidator");
    }

    [TestMethod]
    public void WorkflowA2aHandoff_ModelsDoNotExposeRawPrivateOrWholePatchFields()
    {
        var types = new[]
        {
            typeof(WorkflowA2aHandoffValidationRequest),
            typeof(WorkflowA2aHandoffReference),
            typeof(WorkflowA2aParticipantReference),
            typeof(WorkflowA2aHandoffEvidenceReference),
            typeof(WorkflowA2aHandoffValidationResult)
        };

        foreach (var property in types.SelectMany(type => type.GetProperties()))
            AssertDoesNotContainAny(property.Name, "RawPrompt", "RawCompletion", "RawToolOutput", "PrivateReasoning", "HiddenReasoning", "ChainOfThought", "WholePatch", "PatchPayload");
    }

    [TestMethod]
    public void WorkflowA2aHandoff_ReceiptDoesNotClaimDispatchExecutionApprovalPolicySatisfactionPromotionOrRetrievalActivation()
    {
        var receipt = ReadRepoFile("Docs", "receipts", "PR121_A2A_VALIDATION_BEFORE_WORKFLOW_HANDOFF_RECEIPT.md");

        StringAssert.Contains(receipt, "A2A validation is not dispatch.");
        StringAssert.Contains(receipt, "The runner skeleton remains evaluation-only.");
        AssertDoesNotContainAny(
            receipt,
            "A2A handoff is now supported",
            "Agents can now receive workflow handoffs",
            "runner can route work",
            "receiver may act",
            "satisfies approval",
            "satisfies policy");
    }

    private static string ReadRepoFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(parts).ToArray()));

    private static string ReadAllTextIfDirectoryExists(string directory)
    {
        if (!Directory.Exists(directory))
            return string.Empty;

        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static void AssertDoesNotContainAny(IEnumerable<string> values, params string[] markers)
    {
        var text = string.Join(Environment.NewLine, values);
        AssertDoesNotContainAny(text, markers);
    }

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }
}
