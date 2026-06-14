using System.Diagnostics;
using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class CriticReviewRequestCandidateStaticBoundaryTests
{
    [TestMethod]
    public void CriticReviewRequestCandidate_InterfaceExposesOnlyPrepare()
    {
        var names = typeof(ICriticReviewRequestCandidateWorkflow)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Prepare" }, names);
    }

    [TestMethod]
    [DataRow("Review")]
    [DataRow("Run")]
    [DataRow("RunAsync")]
    [DataRow("Execute")]
    [DataRow("ExecuteAsync")]
    [DataRow("Dispatch")]
    [DataRow("InvokeCritic")]
    [DataRow("InvokeTool")]
    [DataRow("CallModel")]
    [DataRow("BuildPrompt")]
    [DataRow("PostComment")]
    [DataRow("Approve")]
    [DataRow("Reject")]
    [DataRow("CreateTicket")]
    [DataRow("ApplyPatch")]
    [DataRow("PromoteMemory")]
    [DataRow("ActivateRetrieval")]
    public void CriticReviewRequestCandidate_PublicSurfaceDoesNotExposeForbiddenMethods(string forbiddenMethod)
    {
        var methods = typeof(ICriticReviewRequestCandidateWorkflow).Assembly.GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" && type.Name.Contains("CriticReviewRequestCandidate", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.DoesNotContain(methods, forbiddenMethod);
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ProductionFileDoesNotReferenceRuntimeIoReviewDispatchOrMutationDependencies()
    {
        var text = File.ReadAllText(ProductionFile());

        AssertDoesNotContainAny(
            text,
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
            "OpenAI",
            "ModelClient",
            "PromptBuilder",
            "ToolInvoker",
            "AgentDispatcher",
            "CriticAgentService",
            "A2aSender",
            "GitHubClient",
            "WorkflowTransitionRecorder",
            "WorkflowStateWriter",
            "ApprovalMutation",
            "PolicySatisfactionService",
            "SourceMutationService",
            "PatchApply",
            "TicketWriter",
            "MemoryPromotionService",
            "RetrievalActivationService");
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ModelsDoNotExposeRawPrivateOrFullPayloadProperties()
    {
        var propertyNames = typeof(CriticReviewRequestCandidateResult).Assembly.GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" && type.Name.Contains("CriticReview", StringComparison.Ordinal))
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
            "FullLog");

        CollectionAssert.DoesNotContain(propertyNames, "Approved");
        CollectionAssert.DoesNotContain(propertyNames, "Rejected");
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_NoSqlApiCliUiOrRuntimeFilesChanged()
    {
        var changedFiles = ChangedFilesSinceMain();

        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("Database/", StringComparison.Ordinal)), "PR128 must not add SQL migrations.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("IronDev.Api/", StringComparison.Ordinal)), "PR128 must not add API controllers or registrations.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("tools/IronDev.Cli/", StringComparison.Ordinal)), "PR128 must not add CLI commands.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("IronDev.Client/", StringComparison.Ordinal)), "PR128 must not add UI files.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("IronDev.Infrastructure/", StringComparison.Ordinal)), "PR128 must not add infrastructure/runtime wiring.");
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ChangedFilesStayInExpectedShape()
    {
        var changedFiles = ChangedFilesSinceMain();

        foreach (var file in changedFiles)
        {
            Assert.IsTrue(
                file == "IronDev.Core/Workflow/CriticReviewRequestCandidateWorkflowModels.cs" ||
                file == "Docs/receipts/PR128_CRITIC_REVIEW_REQUEST_WORKFLOW_RECEIPT.md" ||
                file.StartsWith("IronDev.IntegrationTests/Governance/CriticReviewRequestCandidate", StringComparison.Ordinal),
                $"Unexpected PR128 file: {file}");
        }
    }

    [TestMethod]
    public void CriticReviewRequestCandidate_ReceiptDoesNotOverclaimReviewAutomation()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR128_CRITIC_REVIEW_REQUEST_WORKFLOW_RECEIPT.md"));

        StringAssert.Contains(receipt, "turns supplied review material into a safe request package");
        StringAssert.Contains(receipt, "A review request is not a review decision.");
        StringAssert.Contains(receipt, "Candidate workflow output cannot grant authority.");
        AssertDoesNotContainAny(
            receipt,
            "CriticAgent reviews the work.",
            "The critic approves",
            "The critic rejects",
            "The model reviews the request.",
            "Review comment is posted.",
            "Workflow may continue.",
            "Ticket is created.",
            "Patch is ready.");
    }

    private static string ProductionFile() =>
        Path.Combine(RepositoryRoot(), "IronDev.Core", "Workflow", "CriticReviewRequestCandidateWorkflowModels.cs");

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
