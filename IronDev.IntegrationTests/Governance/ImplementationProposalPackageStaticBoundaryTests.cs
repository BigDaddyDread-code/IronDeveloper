using System.Diagnostics;
using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
public sealed class ImplementationProposalPackageStaticBoundaryTests
{
    [TestMethod]
    public void ImplementationProposalPackage_InterfaceExposesOnlyPrepare()
    {
        var names = typeof(IImplementationProposalPackageCandidateWorkflow)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Prepare" }, names);
    }

    [TestMethod]
    [DataRow("Implement")]
    [DataRow("GenerateCode")]
    [DataRow("GeneratePatch")]
    [DataRow("ApplyPatch")]
    [DataRow("Run")]
    [DataRow("RunAsync")]
    [DataRow("Execute")]
    [DataRow("ExecuteAsync")]
    [DataRow("Dispatch")]
    [DataRow("InvokeTool")]
    [DataRow("CallModel")]
    [DataRow("BuildPrompt")]
    [DataRow("CreateTicket")]
    [DataRow("Approve")]
    [DataRow("PromoteMemory")]
    [DataRow("ActivateRetrieval")]
    public void ImplementationProposalPackage_PublicSurfaceDoesNotExposeForbiddenMethods(string forbiddenMethod)
    {
        var methods = typeof(IImplementationProposalPackageCandidateWorkflow).Assembly.GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" && type.Name.Contains("ImplementationProposal", StringComparison.Ordinal))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.DoesNotContain(methods, forbiddenMethod);
    }

    [TestMethod]
    public void ImplementationProposalPackage_ProductionFileDoesNotReferenceRuntimeIoCodePatchOrMutationDependencies()
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
            "BuilderAgentService",
            "TesterAgentService",
            "CriticAgentService",
            "A2aSender",
            "GitHubClient",
            "WorkflowTransitionRecorder",
            "WorkflowStateWriter",
            "ApprovalMutation",
            "PolicySatisfactionService",
            "SourceMutationService",
            "PatchApplyService",
            "TicketWriter",
            "MemoryPromotionService",
            "RetrievalActivationService");
    }

    [TestMethod]
    public void ImplementationProposalPackage_ModelsDoNotExposeRawPrivatePatchDiffOrSourceContentProperties()
    {
        var propertyNames = typeof(ImplementationProposalPackageCandidateResult).Assembly.GetTypes()
            .Where(type => type.Namespace == "IronDev.Core.Workflow" && type.Name.Contains("ImplementationProposal", StringComparison.Ordinal))
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
            "SourceContent");

        CollectionAssert.DoesNotContain(propertyNames, "Patch");
        CollectionAssert.DoesNotContain(propertyNames, "Diff");
    }

    [TestMethod]
    public void ImplementationProposalPackage_StatusDoesNotExposeImplementationOrPatchCompletionStates()
    {
        var names = Enum.GetNames<ImplementationProposalPackageCandidateStatus>();

        CollectionAssert.DoesNotContain(names, "Implemented");
        CollectionAssert.DoesNotContain(names, "PatchReady");
        CollectionAssert.DoesNotContain(names, "CodeGenerated");
        CollectionAssert.DoesNotContain(names, "Applied");
        CollectionAssert.DoesNotContain(names, "Approved");
        CollectionAssert.DoesNotContain(names, "TicketCreated");
    }

    [TestMethod]
    public void ImplementationProposalPackage_NoSqlApiCliUiOrRuntimeFilesChanged()
    {
        var changedFiles = ChangedFilesSinceMain();

        // Same sentinel rule as the shape guard below: this fires only when the
        // block's protected model is part of the changeset again.
        if (!changedFiles.Contains("IronDev.Core/Workflow/ImplementationProposalPackageCandidateWorkflowModels.cs", StringComparer.Ordinal))
            return;

        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("Database/", StringComparison.Ordinal)), "PR129 must not add SQL migrations.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("IronDev.Api/", StringComparison.Ordinal)), "PR129 must not add API controllers or registrations.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("tools/IronDev.Cli/", StringComparison.Ordinal)), "PR129 must not add CLI commands.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("IronDev.Client/", StringComparison.Ordinal)), "PR129 must not add UI files.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("IronDev.Infrastructure/", StringComparison.Ordinal)), "PR129 must not add infrastructure/runtime wiring.");
    }

    [TestMethod]
    public void ImplementationProposalPackage_ChangedFilesStayInExpectedShape()
    {
        var changedFiles = ChangedFilesSinceMain();

        // This changeset-shape guard belongs to the PR that introduced the block.
        // Post-merge it fires only when the block's protected model changes again;
        // unrelated branches legitimately contain other work.
        if (!changedFiles.Contains("IronDev.Core/Workflow/ImplementationProposalPackageCandidateWorkflowModels.cs", StringComparer.Ordinal))
            return;

        foreach (var file in changedFiles)
        {
            Assert.IsTrue(
                file == "IronDev.Core/Workflow/ImplementationProposalPackageCandidateWorkflowModels.cs" ||
                file == "Docs/receipts/PR129_IMPLEMENTATION_PROPOSAL_PACKAGE_WORKFLOW_RECEIPT.md" ||
                file.StartsWith("IronDev.IntegrationTests/Governance/ImplementationProposalPackage", StringComparison.Ordinal),
                $"Unexpected PR129 file: {file}");
        }
    }

    [TestMethod]
    public void ImplementationProposalPackage_ReceiptDoesNotOverclaimImplementationAutomation()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR129_IMPLEMENTATION_PROPOSAL_PACKAGE_WORKFLOW_RECEIPT.md"));

        StringAssert.Contains(receipt, "Implementation proposal is not implementation.");
        StringAssert.Contains(receipt, "Proposal output cannot grant authority.");
        StringAssert.Contains(receipt, "remains non-mutating");
        AssertDoesNotContainAny(
            receipt,
            "Implementation is generated.",
            "Patch is ready.",
            "Code is generated.",
            "Workflow can apply this.",
            "Ticket is created.",
            "Tests can run.",
            "BuilderAgent is dispatched.",
            "Workflow may continue.");
    }

    private static string ProductionFile() =>
        Path.Combine(RepositoryRoot(), "IronDev.Core", "Workflow", "ImplementationProposalPackageCandidateWorkflowModels.cs");

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
