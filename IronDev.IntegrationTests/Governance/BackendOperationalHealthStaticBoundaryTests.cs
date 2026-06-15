using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Core.Operations;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class BackendOperationalHealthStaticBoundaryTests
{
    [TestMethod]
    public void BackendOperationalHealth_ControllerIsGetOnly()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "BackendOperationalHealthController.cs"));

        Assert.IsFalse(text.Contains("HttpPost", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("HttpPut", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("HttpPatch", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("HttpDelete", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BackendOperationalHealth_ControllerRoutesDoNotExposeActionFragments()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers", "BackendOperationalHealthController.cs"));
        foreach (var fragment in ForbiddenRouteFragments())
        {
            Assert.IsFalse(text.Contains($"\"{fragment}\"", StringComparison.OrdinalIgnoreCase), $"Controller route must not expose '{fragment}'.");
        }
    }

    [TestMethod]
    public void BackendOperationalHealth_ProductionFilesDoNotContainForbiddenMethodsOrImplementations()
    {
        foreach (var file in ProductionFiles())
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot(), file));
            foreach (var token in ForbiddenMethodNames().Concat(ForbiddenImplementationMarkers()))
            {
                Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"{file} must not contain forbidden token '{token}'.");
            }
        }
    }

    [TestMethod]
    public void BackendOperationalHealth_StatusNamesDoNotGrantAuthorityOrRecovery()
    {
        var names = Enum.GetNames<BackendOperationalHealthStatus>();
        foreach (var forbidden in ForbiddenStatusNames())
            Assert.IsFalse(names.Any(name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase)), $"Forbidden status name exists: {forbidden}");
    }

    [TestMethod]
    public void BackendOperationalHealth_PublicPropertyNamesDoNotExposeSensitivePayloadFields()
    {
        var types = typeof(BackendOperationalHealthReport).Assembly
            .GetTypes()
            .Where(type => type.Namespace is "IronDev.Core.Operations")
            .Where(type => type.Name.Contains("BackendOperationalHealth", StringComparison.Ordinal) ||
                           type.Name.Contains("BackendDependency", StringComparison.Ordinal))
            .ToArray();

        var propertyNames = types
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();

        foreach (var forbidden in ForbiddenPropertyNames())
            Assert.IsFalse(propertyNames.Any(name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase)), $"Forbidden property name exists: {forbidden}");
    }

    [TestMethod]
    public void BackendOperationalHealth_DoesNotAddSqlCliUiOrRuntimeExecutorFiles()
    {
        var changedFiles = ChangedFilesSinceMain()
            .Where(file => file.Contains("BackendOperationalHealth", StringComparison.OrdinalIgnoreCase) ||
                           file.Contains("PR149", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("Database/", StringComparison.Ordinal)), "PR149 must not add SQL migrations.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("tools/IronDev.Cli/", StringComparison.Ordinal)), "PR149 must not add CLI commands.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("IronDev.Client/", StringComparison.Ordinal)), "PR149 must not add UI/client files.");
        Assert.IsFalse(changedFiles.Any(file => file.Contains("Runner", StringComparison.OrdinalIgnoreCase)), "PR149 must not add runtime runners.");
    }

    [TestMethod]
    public void BackendOperationalHealth_ReceiptRecordsReadOnlyBoundary()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR149_BACKEND_OPERATIONAL_HEALTH_CHECKS.md"));

        StringAssert.Contains(text, "Backend Operational Health Checks are read-only.");
        StringAssert.Contains(text, "Health check is not release readiness.");
        StringAssert.Contains(text, "Healthy status is not approval.");
        StringAssert.Contains(text, "Dependency status is not authority.");
        StringAssert.Contains(text, "Recommendation is not execution.");
        StringAssert.Contains(text, "Report is not backend repair.");
        StringAssert.Contains(text, "Report is not backend restart.");
        StringAssert.Contains(text, "Report is not migration execution.");
        StringAssert.Contains(text, "Report is not workflow execution.");
        StringAssert.Contains(text, "does not administer treatment");
    }

    private static IReadOnlyList<string> ProductionFiles() =>
    [
        Path.Combine("IronDev.Core", "Operations", "BackendOperationalHealthModels.cs"),
        Path.Combine("IronDev.Core", "Operations", "IBackendOperationalHealthService.cs"),
        Path.Combine("IronDev.Infrastructure", "Operations", "BackendOperationalHealthService.cs"),
        Path.Combine("IronDev.Api", "Controllers", "BackendOperationalHealthController.cs")
    ];

    private static IReadOnlyList<string> ForbiddenRouteFragments() =>
    [
        "repair",
        "heal",
        "self-heal",
        "restart",
        "recycle",
        "rerun",
        "retry",
        "resume",
        "execute",
        "migrate",
        "migration",
        "upgrade",
        "seed",
        "rebuild",
        "reindex",
        "compact",
        "purge",
        "flush",
        "clear",
        "approve",
        "release-approve",
        "ship",
        "deploy",
        "transition",
        "continue",
        "dispatch",
        "invoke",
        "call-model",
        "promote-memory",
        "activate-retrieval",
        "apply-source",
        "patch-apply"
    ];

    private static IReadOnlyList<string> ForbiddenMethodNames() =>
    [
        "RestartBackendAsync",
        "RepairBackendAsync",
        "HealAsync",
        "SelfHealAsync",
        "RunMigrationAsync",
        "ExecuteMigrationAsync",
        "ApplyMigrationAsync",
        "RebuildReadModelAsync",
        "ReindexAsync",
        "FlushCacheAsync",
        "ClearQueueAsync",
        "PurgeQueueAsync",
        "RetryWorkflowAsync",
        "ExecuteWorkflowAsync",
        "TransitionWorkflowAsync",
        "DispatchAgentAsync",
        "InvokeToolAsync",
        "CallModelAsync",
        "BuildPromptAsync",
        "ApproveReleaseAsync",
        "SatisfyPolicyAsync",
        "PromoteMemoryAsync",
        "ActivateRetrievalAsync",
        "ApplySourceAsync",
        "ApplyPatchAsync",
        "CreateGovernanceEventAsync",
        "AppendGovernanceEventAsync",
        "CreateApprovalDecisionAsync",
        "CreatePolicyDecisionAsync",
        "CreateToolRequestAsync",
        "CreateDogfoodReceiptAsync",
        "CreateTicketAsync"
    ];

    private static IReadOnlyList<string> ForbiddenStatusNames() =>
    [
        "ReleaseReady",
        "Approved",
        "DeploymentApproved",
        "SelfHealed",
        "Restarted",
        "Migrated",
        "WorkflowExecuted",
        "Recovered",
        "Fixed",
        "PolicySatisfied",
        "ApprovalSatisfied",
        "MemoryPromoted",
        "SourceApplied",
        "PatchApplied"
    ];

    private static IReadOnlyList<string> ForbiddenPropertyNames() =>
    [
        "ConnectionString",
        "RawConnectionString",
        "Password",
        "UserPassword",
        "Secret",
        "ApiKey",
        "Token",
        "Credential",
        "PrivateKey",
        "PayloadJson",
        "RawPayload",
        "RawPrompt",
        "RawCompletion",
        "RawToolOutput",
        "RawCommandOutput",
        "StdOut",
        "StdErr",
        "PrivateReasoning",
        "HiddenReasoning",
        "ChainOfThought",
        "SourceContent",
        "SourceFileContents",
        "PatchPayload",
        "DiffPayload",
        "ExecutionCommand",
        "MigrationCommand",
        "RestartCommand"
    ];

    private static IReadOnlyList<string> ForbiddenImplementationMarkers() =>
    [
        "ProcessStartInfo",
        "Process.Start",
        "File.Write",
        "File.Delete",
        "Directory.Enumerate",
        "Directory.GetFiles",
        "DROP TABLE",
        "ALTER TABLE",
        "CREATE TABLE",
        "INSERT INTO",
        "UPDATE ",
        "DELETE FROM",
        "TRUNCATE",
        "EXEC sp_",
        "ToolInvoker",
        "AgentDispatcher",
        "A2aSender",
        "OpenAI",
        "ChatCompletion",
        "SourceMutation",
        "PatchApply",
        "PatchWriter",
        "DiffBuilder",
        "SourceWriter",
        "RollbackExecutor",
        "ValidationRunner",
        "TestRunner",
        "WorkflowTransitionWriter",
        "ApprovalDecisionWriter",
        "PolicyDecisionWriter",
        "ToolRequestWriter",
        "DogfoodReceiptWriter",
        "TicketWriter",
        "AgentRunWriter"
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
            if (Directory.Exists(Path.Combine(current, ".git")))
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
