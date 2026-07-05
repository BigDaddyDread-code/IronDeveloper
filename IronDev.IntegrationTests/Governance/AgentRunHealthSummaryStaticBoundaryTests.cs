using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
public sealed class AgentRunHealthSummaryStaticBoundaryTests
{
    [TestMethod]
    public void AgentRunHealthSummary_ProductionFilesDoNotContainExecutionOrMutationSeams()
    {
        foreach (var file in ProductionFiles())
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot(), file));
            foreach (var token in ForbiddenImplementationTokens())
            {
                Assert.IsFalse(
                    text.Contains(token, StringComparison.OrdinalIgnoreCase),
                    $"{file} must not contain implementation token '{token}'.");
            }
        }
    }

    [TestMethod]
    public void AgentRunHealthSummary_DoesNotAddSqlOrPersistenceFiles()
    {
        var changedFiles = ChangedFilesSinceMain()
            .Where(file => file.Contains("AgentRunHealthSummary", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("Database/", StringComparison.Ordinal)), "PR148 must not add SQL migrations.");
        Assert.IsFalse(changedFiles.Any(file => file.Contains("Store", StringComparison.OrdinalIgnoreCase)), "PR148 must not add stores.");
        Assert.IsFalse(changedFiles.Any(file => file.Contains("Repository", StringComparison.OrdinalIgnoreCase)), "PR148 must not add repositories.");
    }

    [TestMethod]
    public void AgentRunHealthSummary_ReceiptRecordsReadOnlyBoundary()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR148_AGENT_RUN_HEALTH_SUMMARY.md"));

        StringAssert.Contains(text, "read-only");
        StringAssert.Contains(text, "does not grab the controls");
        StringAssert.Contains(text, "does not");
        StringAssert.Contains(text, "restart");
        StringAssert.Contains(text, "invoke tools");
        StringAssert.Contains(text, "call models");
        StringAssert.Contains(text, "transition workflow state");
        StringAssert.Contains(text, "satisfy approval");
        StringAssert.Contains(text, "satisfy policy");
        StringAssert.Contains(text, "promote memory");
        StringAssert.Contains(text, "apply source");
        StringAssert.Contains(text, "expose raw payload JSON");
    }

    private static IReadOnlyList<string> ProductionFiles() =>
    [
        Path.Combine("IronDev.Core", "Agents", "AgentRunHealthSummaryModels.cs"),
        Path.Combine("IronDev.Core", "Agents", "IAgentRunHealthSummaryService.cs"),
        Path.Combine("IronDev.Infrastructure", "Agents", "AgentRunHealthSummaryService.cs"),
        Path.Combine("IronDev.Api", "Controllers", "AgentRunHealthSummaryController.cs")
    ];

    private static IReadOnlyList<string> ForbiddenImplementationTokens() =>
    [
        "IWorkflowRunner",
        "WorkflowRunner",
        "AgentDispatcher",
        "IToolExecutor",
        "IAgentModelAdapter",
        "PromptBuilder",
        "AppendGovernanceEvent",
        "ApprovalDecisionStore",
        "PolicyDecisionEventStore",
        "ToolRequestStore",
        "DogfoodReceiptStore",
        "CollectiveMemoryPromotion",
        "SourceApplyService",
        "PatchApplyService",
        "TicketService",
        "SqlConnection",
        "CommandType.StoredProcedure",
        "INSERT INTO",
        "UPDATE ",
        "DELETE FROM"
    ];

    private static IReadOnlyList<string> ChangedFilesSinceMain()
    {
        var root = RepositoryRoot();
        var output = RunGit(root, "diff --name-only origin/main...HEAD");
        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private static string RepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "IronDev.slnx")) ||
                Directory.Exists(Path.Combine(current, ".git")) ||
                File.Exists(Path.Combine(current, ".git")))
                return current;

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private static string RunGit(string workingDirectory, string arguments)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {arguments} failed: {error}");

        return output;
    }
}
