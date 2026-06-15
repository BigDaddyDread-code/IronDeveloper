using System.Diagnostics;
using System.Reflection;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class BlockLGovernedRunnerReceiptStaticBoundaryTests
{
    [TestMethod]
    public void BlockLGovernedRunnerReceipt_Pr126ChangedFilesAreDocsAndTestsOnly()
    {
        var changedFiles = ChangedFilesSinceMain()
            .Where(file => !IsAllowedPostPr126ApplyPreviewFile(file))
            .ToArray();

        foreach (var file in changedFiles)
        {
            Assert.IsTrue(
                file.StartsWith("Docs/receipts/", StringComparison.Ordinal) ||
                file.StartsWith("IronDev.IntegrationTests/Governance/", StringComparison.Ordinal),
                $"PR126 must stay docs/tests only, but changed: {file}");
        }
    }

    [TestMethod]
    public void BlockLGovernedRunnerReceipt_NoProductionWorkflowCodeChangedByPr126()
    {
        var changedProductionWorkflowFiles = ChangedFilesSinceMain()
            .Where(file => !IsAllowedPostPr126ApplyPreviewFile(file))
            .Where(file => file.StartsWith("IronDev.Core/Workflow/", StringComparison.Ordinal) ||
                           file.StartsWith("IronDev.Infrastructure/", StringComparison.Ordinal) ||
                           file.StartsWith("IronDev.Api/", StringComparison.Ordinal) ||
                           file.StartsWith("tools/IronDev.Cli/", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(0, changedProductionWorkflowFiles.Length, "PR126 must not change production workflow/API/CLI/infrastructure code.");
    }

    [TestMethod]
    public void BlockLGovernedRunnerReceipt_NoSqlApiCliUiOrRuntimeFilesAddedByPr126()
    {
        var changedFiles = ChangedFilesSinceMain()
            .Where(file => !IsAllowedPostPr126ApplyPreviewFile(file))
            .ToArray();

        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("Database/", StringComparison.Ordinal)), "PR126 must not add SQL migrations or SQL scripts.");
        Assert.IsFalse(changedFiles.Any(file => file.Contains("Controller", StringComparison.OrdinalIgnoreCase)), "PR126 must not add API controllers.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("tools/IronDev.Cli/", StringComparison.Ordinal)), "PR126 must not add CLI commands.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("IronDev.Client/", StringComparison.Ordinal)), "PR126 must not add UI files.");
        Assert.IsFalse(changedFiles.Any(file => file.Contains("HostedService", StringComparison.OrdinalIgnoreCase) || file.Contains("BackgroundService", StringComparison.OrdinalIgnoreCase)), "PR126 must not add hosted services.");
        Assert.IsFalse(changedFiles.Any(file => file.Contains("Scheduler", StringComparison.OrdinalIgnoreCase) || file.Contains("Orchestrator", StringComparison.OrdinalIgnoreCase)), "PR126 must not add scheduler/orchestrator files.");
    }

    [TestMethod]
    public void BlockLGovernedRunnerReceipt_ChangedProductionFilesDoNotAddAuthorityRuntimeOrMutationMarkers()
    {
        var changedProductionFiles = ChangedFilesSinceMain()
            .Where(file => !IsAllowedPostPr126ApplyPreviewFile(file))
            .Where(IsProductionSourceFile)
            .ToArray();

        var combinedText = string.Join("\n", changedProductionFiles.Select(file => File.ReadAllText(Path.Combine(RepositoryRoot(), NormalizeForLocalPath(file)))));

        AssertDoesNotContainAny(
            combinedText,
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
            "IHostedService",
            "BackgroundService",
            "ControllerBase",
            "WebApplication",
            "OpenAI",
            "LangGraph runtime",
            "ToolInvoker",
            "AgentDispatcher",
            "WorkflowTransitionRecorder",
            "WorkflowStateWriter",
            "ApprovalMutation",
            "PolicySatisfaction",
            "MemoryPromotion",
            "RetrievalActivation",
            "PatchApply",
            "DispatchAsync",
            "InvokeToolAsync",
            "ApproveAsync",
            "TransitionAsync",
            "ApplyPatch",
            "MutateSource",
            "PromoteMemory",
            "ActivateRetrieval",
            "RawPrompt",
            "RawCompletion",
            "RawToolOutput",
            "PrivateReasoning",
            "HiddenReasoning",
            "ChainOfThought",
            "WholePatch",
            "PatchPayload",
            "approval-required",
            "governance-event-required",
            "policy-required",
            "authority-granted",
            "execution-allowed",
            "dispatch-allowed",
            "source-mutation-approved",
            "memory-promotion-approved",
            "retrieval-activation-approved",
            "dry-run-approved",
            "route-approved");
    }

    [TestMethod]
    public void BlockLGovernedRunnerReceipt_WorkflowInterfacesRemainBoxed()
    {
        AssertPublicMethods<IWorkflowRunnerSkeleton>("Evaluate");
        AssertPublicMethods<IWorkflowDryRunExecutor>("ExecuteDryRun");
        AssertPublicMethods<IBoxedLangGraphRoutingAdapter>("SuggestRoute");
    }

    [TestMethod]
    public void BlockLGovernedRunnerReceipt_ReceiptDocumentsDocsTestsOnlyBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR126_BLOCK_L_GOVERNED_RUNNER_RECEIPT.md"));

        StringAssert.Contains(receipt, "Receipt is not capability.");
        StringAssert.Contains(receipt, "does not claim the runner can drive");
        StringAssert.Contains(receipt, "Block M must not treat Block L receipt as permission");
    }

    private static void AssertPublicMethods<T>(params string[] expectedNames)
    {
        var names = typeof(T)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(expectedNames.OrderBy(name => name, StringComparer.Ordinal).ToArray(), names, typeof(T).Name);
    }

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

    private static bool IsProductionSourceFile(string file) =>
        file.EndsWith(".cs", StringComparison.Ordinal) &&
        !file.StartsWith("IronDev.IntegrationTests/", StringComparison.Ordinal) &&
        !file.StartsWith("IronDev.Tests/", StringComparison.Ordinal);

    private static bool IsAllowedPostPr126ApplyPreviewFile(string file) =>
        file is "IronDev.Core/Workflow/ApplyPreviewModels.cs" or
            "IronDev.Core/Workflow/IApplyPreviewService.cs" or
            "IronDev.Infrastructure/Workflow/ApplyPreviewService.cs" or
            "IronDev.Api/Controllers/ApplyPreviewController.cs" or
            "IronDev.Api/Program.cs" or
            "IronDev.Client/IIronDevApiClient.cs" or
            "IronDev.Client/IronDevApiClient.cs" or
            "tools/IronDev.Cli/CliApplyPreview.cs" or
            "tools/IronDev.Cli/IronDevCli.cs" or
            "IronDev.IntegrationTests/Governance/ApplyPreviewBoundaryTests.cs" or
            "IronDev.IntegrationTests/Governance/ApplyPreviewCliTests.cs" or
            "IronDev.IntegrationTests.Api/ApplyPreviewApiContractTests.cs" or
            "IronDev.IntegrationTests/ApiCliContract/ApiCliCommandMappingTests.cs" or
            "IronDev.IntegrationTests/ApiCliContract/ApiCliBoundaryContractTests.cs" or
            "IronDev.IntegrationTests/ApiCliContract/ApiCliStaticBoundaryTests.cs" or
            "IronDev.IntegrationTests/ApiCliContract/ApiCliContractTestSupport.cs" or
            "Docs/receipts/PR142_APPLY_PREVIEW_CLI.md" or
            "Docs/receipts/PR141_APPLY_PREVIEW_API.md" or
            "IronDev.Core/Workflow/FailedWorkflowDiagnosisReportModels.cs" or
            "IronDev.Core/Workflow/IFailedWorkflowDiagnosisReportService.cs" or
            "IronDev.Infrastructure/Workflow/FailedWorkflowDiagnosisReportService.cs" or
            "IronDev.Api/Controllers/FailedWorkflowDiagnosisReportController.cs" or
            "IronDev.IntegrationTests.Api/FailedWorkflowDiagnosisReportApiContractTests.cs" or
            "IronDev.IntegrationTests/Governance/FailedWorkflowDiagnosisReportBoundaryTests.cs" or
            "IronDev.IntegrationTests/Governance/FailedWorkflowDiagnosisReportStaticBoundaryTests.cs" or
            "Docs/receipts/PR146_FAILED_WORKFLOW_DIAGNOSIS_REPORT.md" or
            "IronDev.Core/Governance/ApprovalGateDogfoodCorrelationReportModels.cs" or
            "IronDev.Core/Governance/IApprovalGateDogfoodCorrelationReportService.cs" or
            "IronDev.Infrastructure/Governance/ApprovalGateDogfoodCorrelationReportService.cs" or
            "IronDev.Api/Controllers/ApprovalGateDogfoodCorrelationReportController.cs" or
            "IronDev.IntegrationTests.Api/ApprovalGateDogfoodCorrelationReportApiContractTests.cs" or
            "IronDev.IntegrationTests/Governance/ApprovalGateDogfoodCorrelationReportBoundaryTests.cs" or
            "IronDev.IntegrationTests/Governance/ApprovalGateDogfoodCorrelationReportStaticBoundaryTests.cs" or
            "Docs/receipts/PR147_APPROVAL_GATE_DOGFOOD_CORRELATION_REPORT.md" or
            "IronDev.Core/Agents/AgentRunHealthSummaryModels.cs" or
            "IronDev.Core/Agents/IAgentRunHealthSummaryService.cs" or
            "IronDev.Infrastructure/Agents/AgentRunHealthSummaryService.cs" or
            "IronDev.Api/Controllers/AgentRunHealthSummaryController.cs" or
            "IronDev.IntegrationTests.Api/AgentRunHealthSummaryApiContractTests.cs" or
            "IronDev.IntegrationTests/Governance/AgentRunHealthSummaryBoundaryTests.cs" or
            "IronDev.IntegrationTests/Governance/AgentRunHealthSummaryStaticBoundaryTests.cs" or
            "Docs/receipts/PR148_AGENT_RUN_HEALTH_SUMMARY.md" or
            "IronDev.Core/Operations/BackendOperationalHealthModels.cs" or
            "IronDev.Core/Operations/IBackendOperationalHealthService.cs" or
            "IronDev.Infrastructure/Operations/BackendOperationalHealthService.cs" or
            "IronDev.Api/Controllers/BackendOperationalHealthController.cs" or
            "IronDev.IntegrationTests.Api/BackendOperationalHealthApiContractTests.cs" or
            "IronDev.IntegrationTests/Governance/BackendOperationalHealthBoundaryTests.cs" or
            "IronDev.IntegrationTests/Governance/BackendOperationalHealthStaticBoundaryTests.cs" or
            "Docs/receipts/PR149_BACKEND_OPERATIONAL_HEALTH_CHECKS.md";

    private static string NormalizeForLocalPath(string file) => file.Replace('/', Path.DirectorySeparatorChar);

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
            Assert.IsFalse(text.Contains(value, StringComparison.OrdinalIgnoreCase), $"Forbidden production marker found: {value}");
    }
}
