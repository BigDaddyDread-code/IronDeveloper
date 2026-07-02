using System.Diagnostics;
using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
public sealed class TestFailureReviewCandidateStaticBoundaryTests
{
    [TestMethod]
    public void TestFailureReviewCandidate_InterfaceExposesOnlyReview()
    {
        var names = typeof(ITestFailureReviewCandidateWorkflow)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Review" }, names);
    }

    [TestMethod]
    [DataRow("Run")]
    [DataRow("RunAsync")]
    [DataRow("Execute")]
    [DataRow("ExecuteAsync")]
    [DataRow("RunTests")]
    [DataRow("Dispatch")]
    [DataRow("InvokeTool")]
    [DataRow("CallModel")]
    [DataRow("BuildPrompt")]
    [DataRow("ApplyPatch")]
    [DataRow("MutateSource")]
    [DataRow("CreateTicket")]
    [DataRow("PromoteMemory")]
    [DataRow("ActivateRetrieval")]
    public void TestFailureReviewCandidate_PublicSurfaceDoesNotExposeForbiddenMethods(string forbiddenMethod)
    {
        var methods = typeof(ITestFailureReviewCandidateWorkflow).Assembly.GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" && type.Name.Contains("TestFailureReviewCandidate", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.DoesNotContain(methods, forbiddenMethod);
    }

    [TestMethod]
    public void TestFailureReviewCandidate_ProductionFileDoesNotReferenceRuntimeIoOrMutationDependencies()
    {
        var text = File.ReadAllText(ProductionFile());

        AssertDoesNotContainAny(
            text,
            "ProcessStartInfo",
            "dotnet test",
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
            "OpenAI",
            "ToolInvoker",
            "AgentDispatcher",
            "A2aSender",
            "WorkflowTransitionRecorder",
            "WorkflowStateWriter",
            "ApprovalMutation",
            "PolicySatisfactionService",
            "SourceMutationService",
            "PatchApply",
            "MemoryPromotionService",
            "RetrievalActivationService");
    }

    [TestMethod]
    public void TestFailureReviewCandidate_ModelsDoNotExposeRawPrivateOrFullPayloadProperties()
    {
        var propertyNames = typeof(TestFailureReviewCandidateResult).Assembly.GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" && type.Name.Contains("TestFailure", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(property => property.Name)
            .ToArray();

        AssertDoesNotContainAny(
            string.Join("\n", propertyNames),
            "RawPrompt",
            "RawCompletion",
            "RawToolOutput",
            "PrivateReasoning",
            "HiddenReasoning",
            "ChainOfThought",
            "WholePatch",
            "PatchPayload",
            "RawLog",
            "StdOut",
            "StdErr",
            "FullStackTrace",
            "ExceptionDump");
    }

    [TestMethod]
    public void TestFailureReviewCandidate_NoSqlApiCliUiOrRuntimeFilesChanged()
    {
        var changedFiles = ChangedFilesSinceMain();

        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("Database/", StringComparison.Ordinal)), "PR127 must not add SQL migrations.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("IronDev.Api/", StringComparison.Ordinal)), "PR127 must not add API controllers or registrations.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("tools/IronDev.Cli/", StringComparison.Ordinal)), "PR127 must not add CLI commands.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("IronDev.Client/", StringComparison.Ordinal)), "PR127 must not add UI files.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("IronDev.Infrastructure/", StringComparison.Ordinal)), "PR127 must not add infrastructure/runtime wiring.");
    }

    [TestMethod]
    public void TestFailureReviewCandidate_ChangedFilesStayInExpectedShape()
    {
        var changedFiles = ChangedFilesSinceMain();

        foreach (var file in changedFiles)
        {
            Assert.IsTrue(
                file == "IronDev.Core/Workflow/TestFailureReviewCandidateWorkflowModels.cs" ||
                file == "Docs/receipts/PR127_TEST_FAILURE_REVIEW_CANDIDATE_WORKFLOW_RECEIPT.md" ||
                file.StartsWith("IronDev.IntegrationTests/Governance/TestFailureReviewCandidate", StringComparison.Ordinal),
                $"Unexpected PR127 file: {file}");
        }
    }

    [TestMethod]
    public void TestFailureReviewCandidate_ReceiptDoesNotOverclaimDebuggingAutomation()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR127_TEST_FAILURE_REVIEW_CANDIDATE_WORKFLOW_RECEIPT.md"));

        StringAssert.Contains(receipt, "turns supplied test failure evidence into safe review material only");
        StringAssert.Contains(receipt, "Classification is advisory. It is not root-cause proof.");
        StringAssert.Contains(receipt, "Candidate workflow output cannot grant authority.");
        AssertDoesNotContainAny(
            receipt,
            "IronDev can debug tests.",
            "IronDev can fix failing tests.",
            "Workflow can run test failure review automatically.",
            "Root cause is identified.",
            "Patch is ready.",
            "Ticket is created.",
            "Agent handoff is sent.");
    }

    private static string ProductionFile() =>
        Path.Combine(RepositoryRoot(), "IronDev.Core", "Workflow", "TestFailureReviewCandidateWorkflowModels.cs");

    private static IReadOnlyList<string> ChangedFilesSinceMain()
    {
        var files = new SortedSet<string>(StringComparer.Ordinal);
        AddGitFiles(files, "diff --name-only main...HEAD");
        AddGitFiles(files, "diff --name-only");
        AddGitFiles(files, "diff --cached --name-only");
        return files.ToArray();
    }

    private static void AddGitFiles(ISet<string> files, string arguments)
    {
        var result = RunGit(arguments);
        if (result.ExitCode != 0)
            return;

        foreach (var line in result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            files.Add(line.Replace('\\', '/'));
    }

    private static (int ExitCode, string Output) RunGit(string arguments)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd();
        output += process.StandardError.ReadToEnd();
        process.WaitForExit(10_000);
        return (process.ExitCode, output);
    }

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "IronDev.slnx")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static void AssertDoesNotContainAny(string text, params string[] forbidden)
    {
        foreach (var value in forbidden)
            Assert.IsFalse(text.Contains(value, StringComparison.OrdinalIgnoreCase), $"Unexpected forbidden text: {value}");
    }
}
