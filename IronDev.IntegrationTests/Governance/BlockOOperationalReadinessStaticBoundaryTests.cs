using System.Reflection;
using System.Text.RegularExpressions;
using IronDev.Core.Agents;
using IronDev.Core.Governance;
using IronDev.Core.Operations;
using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
public sealed class BlockOOperationalReadinessStaticBoundaryTests
{
    [TestMethod]
    public void BlockO_BlockOControllers_ExposeGetOnly()
    {
        foreach (var file in ControllerFiles())
        {
            var text = File.ReadAllText(file);
            StringAssert.Contains(text, "HttpGet");
            AssertDoesNotContainAny(text, "HttpPost", "HttpPut", "HttpPatch", "HttpDelete");
        }
    }

    [TestMethod]
    public void BlockO_BlockOControllers_DoNotExposeControlRouteFragments()
    {
        foreach (var route in ControllerRoutes())
        foreach (var fragment in ForbiddenRouteFragments())
            Assert.IsFalse(RouteContainsForbiddenFragment(route, fragment), $"Route must not expose control fragment '{fragment}': {route}");
    }

    [TestMethod]
    public void BlockO_ProductionServices_DoNotExposeForbiddenControlMethodNames()
    {
        var methodNames = ProductionTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Select(method => method.Name)
            .ToArray();

        foreach (var forbidden in ForbiddenMethodNames())
            Assert.IsFalse(methodNames.Any(name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase)), $"Forbidden method exists: {forbidden}");
    }

    [TestMethod]
    public void BlockO_ProductionModels_DoNotExposeRawPrivatePayloadPropertyNames()
    {
        var propertyNames = ProductionTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(property => property.Name)
            .ToArray();

        foreach (var forbidden in ForbiddenPropertyNames())
            Assert.IsFalse(propertyNames.Any(name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase)), $"Forbidden property exists: {forbidden}");
    }

    [TestMethod]
    public void BlockO_ProductionFiles_DoNotReferenceModelToolAgentExecutionMarkers()
    {
        foreach (var file in ProductionFiles())
        {
            var text = ScrubAllowedReadOnlyAndDenyListText(File.ReadAllText(file));
            foreach (var marker in ExecutionMarkers())
                Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"{Path.GetFileName(file)} must not contain execution marker '{marker}'.");
        }
    }

    [TestMethod]
    public void BlockO_ProductionFiles_DoNotReferenceCleanupDeletePurgeArchiveRedactExecutableMarkers()
    {
        foreach (var file in ProductionFiles())
        {
            var text = ScrubAllowedReadOnlyAndDenyListText(File.ReadAllText(file));
            foreach (var marker in CleanupExecutionMarkers())
                Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"{Path.GetFileName(file)} must not contain cleanup execution marker '{marker}'.");
        }
    }

    [TestMethod]
    public void BlockO_ProductionFiles_DoNotReferenceHostedBackgroundOrSchedulerMarkers()
    {
        foreach (var file in ProductionFiles())
        {
            var text = File.ReadAllText(file);
            AssertDoesNotContainAny(text, "IHostedService", "BackgroundService", "Timer", "Cron", "Scheduler");
        }
    }

    [TestMethod]
    public void BlockO_PR150_DoesNotAddApiCliSqlOrJobSurface()
    {
        var changed = ChangedFilesSinceMain();
        if (!changed.Contains("Docs/receipts/PR152_BLOCK_O_OPERATIONAL_READINESS_RECEIPT.md", StringComparer.Ordinal))
            return;

        Assert.IsFalse(changed.Any(file => file.StartsWith("Database/", StringComparison.Ordinal)), "PR152 must not add SQL.");
        Assert.IsFalse(changed.Any(file => file.StartsWith("IronDev.Api/Controllers/", StringComparison.Ordinal) && file.Contains("Retention", StringComparison.OrdinalIgnoreCase)), "Block O must not add retention API.");
        Assert.IsFalse(changed.Any(file => file.StartsWith("tools/IronDev.Cli/", StringComparison.Ordinal)), "PR152 must not add CLI commands.");
        Assert.IsFalse(changed.Any(file => file.Contains("HostedService", StringComparison.OrdinalIgnoreCase) || file.Contains("BackgroundService", StringComparison.OrdinalIgnoreCase)), "PR152 must not add jobs.");
    }

    [TestMethod]
    public void BlockO_PR149_DoesNotRunMigrationsOrSqlMutations()
    {
        var backendHealthFiles = ProductionFiles().Where(file => file.Contains("BackendOperationalHealth", StringComparison.OrdinalIgnoreCase));
        foreach (var file in backendHealthFiles)
        {
            var text = ScrubAllowedReadOnlyAndDenyListText(File.ReadAllText(file));
            AssertDoesNotContainAny(text, "RunMigration", "ExecuteMigration", "ApplyMigration", "INSERT INTO", "UPDATE ", "DELETE FROM", "DROP TABLE", "TRUNCATE");
        }
    }

    [TestMethod]
    public void BlockO_PR148_DoesNotDispatchAgents()
    {
        var files = ProductionFiles().Where(file => file.Contains("AgentRunHealthSummary", StringComparison.OrdinalIgnoreCase));
        foreach (var file in files)
            Assert.IsFalse(File.ReadAllText(file).Contains("AgentDispatcher", StringComparison.OrdinalIgnoreCase), $"{file} must not dispatch agents.");
    }

    [TestMethod]
    public void BlockO_PR147_DoesNotApproveReleaseOpenGatesOrMarkDogfoodPassed()
    {
        var files = ProductionFiles().Where(file => file.Contains("ApprovalGateDogfoodCorrelationReport", StringComparison.OrdinalIgnoreCase));
        foreach (var file in files)
            AssertDoesNotContainAny(File.ReadAllText(file), "ApproveReleaseAsync", "OpenGateAsync", "MarkDogfoodPassedAsync");
    }

    [TestMethod]
    public void BlockO_PR146_DoesNotRepairRetryOrRerunWorkflows()
    {
        var files = ProductionFiles().Where(file => file.Contains("FailedWorkflowDiagnosisReport", StringComparison.OrdinalIgnoreCase));
        foreach (var file in files)
            AssertDoesNotContainAny(File.ReadAllText(file), "RepairAsync", "RetryWorkflowAsync", "RerunWorkflowAsync", "ResumeWorkflowAsync");
    }

    [TestMethod]
    public void BlockO_PR145_DoesNotReplayGovernanceOrExposeRawPayloads()
    {
        var files = ProductionFiles().Where(file => file.Contains("GovernanceTraceExplorer", StringComparison.OrdinalIgnoreCase));
        foreach (var file in files)
        {
            var text = ScrubAllowedReadOnlyAndDenyListText(File.ReadAllText(file));
            AssertDoesNotContainAny(text, "ReplayGovernance", "RawPayload", "PayloadJson");
        }
    }

    [TestMethod]
    public void BlockO_Receipt_StatesObservationBoundary()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR152_BLOCK_O_OPERATIONAL_READINESS_RECEIPT.md"));

        StringAssert.Contains(text, "Block O adds operational observability and traceability.");
        StringAssert.Contains(text, "Block O does not add operational authority.");
        StringAssert.Contains(text, "Diagnosis is not repair.");
        StringAssert.Contains(text, "Health is not release readiness.");
        StringAssert.Contains(text, "Correlation is not approval.");
        StringAssert.Contains(text, "Recommendation is not execution.");
        StringAssert.Contains(text, "Retention rule evaluation is not cleanup execution.");
        StringAssert.Contains(text, "Traceability is not mutation permission.");
        StringAssert.Contains(text, "does not install the control panel");
    }

    private static IReadOnlyList<string> ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.Core", "Governance", "GovernanceTraceExplorerModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IGovernanceTraceExplorerService.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "GovernanceTraceExplorerService.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "GovernanceTraceExplorerController.cs"),
            Path.Combine(root, "IronDev.Core", "Workflow", "FailedWorkflowDiagnosisReportModels.cs"),
            Path.Combine(root, "IronDev.Core", "Workflow", "IFailedWorkflowDiagnosisReportService.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Workflow", "FailedWorkflowDiagnosisReportService.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "FailedWorkflowDiagnosisReportController.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ApprovalGateDogfoodCorrelationReportModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IApprovalGateDogfoodCorrelationReportService.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "ApprovalGateDogfoodCorrelationReportService.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "ApprovalGateDogfoodCorrelationReportController.cs"),
            Path.Combine(root, "IronDev.Core", "Agents", "AgentRunHealthSummaryModels.cs"),
            Path.Combine(root, "IronDev.Core", "Agents", "IAgentRunHealthSummaryService.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Agents", "AgentRunHealthSummaryService.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "AgentRunHealthSummaryController.cs"),
            Path.Combine(root, "IronDev.Core", "Operations", "BackendOperationalHealthModels.cs"),
            Path.Combine(root, "IronDev.Core", "Operations", "IBackendOperationalHealthService.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Operations", "BackendOperationalHealthService.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "BackendOperationalHealthController.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "GovernanceDataRetentionRuleModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IGovernanceDataRetentionRuleService.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "GovernanceDataRetentionRuleService.cs")
        ];
    }

    private static IReadOnlyList<string> ControllerFiles() =>
        ProductionFiles()
            .Where(file => file.Contains($"{Path.DirectorySeparatorChar}Controllers{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

    private static IReadOnlyList<Type> ProductionTypes() =>
    [
        typeof(GovernanceTraceSummary),
        typeof(GovernanceTraceDetail),
        typeof(GovernanceTraceListResponse),
        typeof(IGovernanceTraceExplorerService),
        typeof(FailedWorkflowDiagnosisReport),
        typeof(FailedWorkflowDiagnosisSignal),
        typeof(FailedWorkflowDiagnosisHypothesis),
        typeof(IFailedWorkflowDiagnosisReportService),
        typeof(ApprovalGateDogfoodCorrelationReport),
        typeof(GovernanceCorrelationRecommendation),
        typeof(IApprovalGateDogfoodCorrelationReportService),
        typeof(AgentRunHealthSummary),
        typeof(AgentRunHealthSummaryBoundary),
        typeof(IAgentRunHealthSummaryService),
        typeof(BackendOperationalHealthReport),
        typeof(BackendOperationalHealthRecommendation),
        typeof(IBackendOperationalHealthService),
        typeof(GovernanceDataRetentionRuleResult),
        typeof(GovernanceDataCleanupRecommendation),
        typeof(IGovernanceDataRetentionRuleService)
    ];

    private static IReadOnlyList<string> ControllerRoutes()
    {
        var routes = new List<string>();
        var routePattern = new Regex(@"\[(?:Route|HttpGet)\(""([^""]*)""\)\]", RegexOptions.Compiled);
        foreach (var file in ControllerFiles())
        {
            var text = File.ReadAllText(file);
            routes.AddRange(routePattern.Matches(text).Select(match => match.Groups[1].Value));
        }

        return routes;
    }

    private static bool RouteContainsForbiddenFragment(string route, string fragment)
    {
        var routeTokens = RouteTokens(route);
        var fragmentTokens = RouteTokens(fragment);
        if (fragmentTokens.Count == 0 || routeTokens.Count < fragmentTokens.Count)
            return false;

        for (var start = 0; start <= routeTokens.Count - fragmentTokens.Count; start++)
        {
            var matches = true;
            for (var offset = 0; offset < fragmentTokens.Count; offset++)
            {
                if (!string.Equals(routeTokens[start + offset], fragmentTokens[offset], StringComparison.OrdinalIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return true;
        }

        return false;
    }

    private static IReadOnlyList<string> RouteTokens(string routeOrFragment)
    {
        var withoutRouteParameters = Regex.Replace(routeOrFragment, @"\{[^}]+\}", string.Empty);
        return Regex.Split(withoutRouteParameters, @"[^A-Za-z0-9]+")
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
    }

    private static string ScrubAllowedReadOnlyAndDenyListText(string text)
    {
        foreach (var allowed in new[]
        {
            "IDbConnectionFactory",
            "connection.Open",
            "QueryAsync",
            "QuerySingleOrDefaultAsync",
            "ContainsUnsafeText",
            "UnsafeMarkers",
            "RedactedUnsafeText",
            "CanRunCleanup",
            "CanScheduleCleanup",
            "CanDeleteData",
            "CanPurgeData",
            "CanArchiveData",
            "CanRedactData",
            "IsDeleteCommand",
            "IsPurgeCommand",
            "IsArchiveCommand",
            "IsRedactionCommand",
            "CanApplyPatch",
            "CanApplySource",
            "CanPromoteMemory",
            "CanActivateRetrieval",
            "CanCallModel",
            "CanInvokeTool",
            "CanDispatchAgent",
            "CanRunMigration",
            "RunMigration",
            "ExecuteMigration",
            "ApplyMigration",
            "PatchApply",
            "SourceApply",
            "MemoryPromotion",
            "RetrievalActivation",
            "ModelExecution",
            "ToolInvocation",
            "AgentDispatch",
            "MigrationExecution",
            "PayloadJson",
            "RawPayload",
            "exposesRawPayloadJson",
            "ExposesRawPayloadJson",
            "EligibleForHumanCleanupReview",
            "GovernanceDataCleanupRecommendation",
            "GovernanceDataCleanupRecommendationKind"
        })
        {
            text = text.Replace(allowed, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private static IReadOnlyList<string> ForbiddenRouteFragments() =>
    [
        "approve",
        "reject",
        "grant",
        "satisfy",
        "transition",
        "continue",
        "execute",
        "invoke",
        "dispatch",
        "replay",
        "rerun",
        "retry",
        "resume",
        "restart",
        "repair",
        "fix",
        "heal",
        "self-heal",
        "migrate",
        "migration",
        "cleanup",
        "delete",
        "purge",
        "archive",
        "redact",
        "release-approve",
        "mark-passed",
        "dogfood-pass",
        "gate-open",
        "gate-reopen",
        "apply-source",
        "patch-apply",
        "promote-memory",
        "activate-retrieval",
        "call-model",
        "build-prompt",
        "create-ticket"
    ];

    private static IReadOnlyList<string> ForbiddenMethodNames() =>
    [
        "ApproveAsync",
        "RejectAsync",
        "GrantAsync",
        "SatisfyPolicyAsync",
        "SatisfyApprovalAsync",
        "TransitionWorkflowAsync",
        "ContinueWorkflowAsync",
        "InvokeToolAsync",
        "DispatchAgentAsync",
        "CallModelAsync",
        "BuildPromptAsync",
        "CreateTicketAsync",
        "PromoteMemoryAsync",
        "ActivateRetrievalAsync",
        "ApplySourceAsync",
        "ApplyPatchAsync",
        "RestartAgentAsync",
        "RestartBackendAsync",
        "RetryAgentRunAsync",
        "RerunAgentAsync",
        "ResumeAgentAsync",
        "RepairAsync",
        "HealAsync",
        "RunMigrationAsync",
        "ExecuteMigrationAsync",
        "RebuildReadModelAsync",
        "ReindexAsync",
        "FlushCacheAsync",
        "PurgeQueueAsync",
        "DeleteAsync",
        "PurgeAsync",
        "ArchiveAsync",
        "RedactAsync",
        "CleanupAsync",
        "RunCleanupAsync",
        "ScheduleCleanupAsync",
        "CreateGovernanceEventAsync",
        "AppendGovernanceEventAsync",
        "CreateApprovalDecisionAsync",
        "CreatePolicyDecisionAsync",
        "CreateToolRequestAsync",
        "CreateDogfoodReceiptAsync",
        "MarkDogfoodPassedAsync",
        "ApproveReleaseAsync"
    ];

    private static IReadOnlyList<string> ForbiddenPropertyNames() =>
    [
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
        "ConnectionString",
        "Password",
        "Secret",
        "ApiKey",
        "Token",
        "Credential",
        "ApprovalToken",
        "ReleaseApprovalToken",
        "ExecutionCommand",
        "MigrationCommand",
        "RestartCommand",
        "DeleteCommand",
        "PurgeCommand",
        "ArchiveCommand",
        "RedactionCommand",
        "SqlCommand"
    ];

    private static IReadOnlyList<string> ExecutionMarkers() =>
    [
        "ProcessStartInfo",
        "Process.Start",
        "File.ReadAllText",
        "File.Write",
        "File.Delete",
        "Directory.Delete",
        "Directory.Enumerate",
        "Directory.GetFiles",
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
        "MemoryPromotion",
        "RetrievalActivation",
        "WorkflowTransitionWriter",
        "ApprovalDecisionWriter",
        "PolicyDecisionWriter",
        "ToolRequestWriter",
        "DogfoodReceiptWriter",
        "TicketWriter",
        "AgentRunWriter"
    ];

    private static IReadOnlyList<string> CleanupExecutionMarkers() =>
    [
        "DROP TABLE",
        "TRUNCATE",
        "DELETE FROM",
        "UPDATE ",
        "INSERT INTO",
        "ALTER TABLE",
        "CREATE TABLE",
        "SqlCommand",
        "ExecuteNonQuery",
        "DeleteAsync",
        "PurgeAsync",
        "ArchiveAsync",
        "RedactAsync",
        "CleanupAsync",
        "RunCleanupAsync",
        "ScheduleCleanupAsync"
    ];

    private static IReadOnlyList<string> ChangedFilesSinceMain()
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var output in new[]
        {
            RunGit("diff --name-only origin/main...HEAD"),
            RunGit("diff --name-only"),
            RunGit("ls-files --others --exclude-standard")
        })
        {
            foreach (var file in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                files.Add(file);
        }

        return files.ToArray();
    }

    private static string RunGit(string arguments)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = RepositoryRoot(),
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

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker: {marker}");
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



