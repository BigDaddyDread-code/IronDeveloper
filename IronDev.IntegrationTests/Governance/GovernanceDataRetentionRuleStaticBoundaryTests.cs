using System.Reflection;
using IronDev.Core.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
public sealed class GovernanceDataRetentionRuleStaticBoundaryTests
{
    [TestMethod]
    public void ServiceInterface_ExposesEvaluateOnly()
    {
        var methods = typeof(IGovernanceDataRetentionRuleService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "Evaluate" }, methods);
    }

    [TestMethod]
    public void ServiceInterface_DoesNotExposeForbiddenCleanupMethods()
    {
        var methods = typeof(IGovernanceDataRetentionRuleService)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => method.Name)
            .ToArray();

        foreach (var forbidden in ForbiddenMethodNames())
            Assert.IsFalse(methods.Any(method => string.Equals(method, forbidden, StringComparison.OrdinalIgnoreCase)), $"Forbidden method exists: {forbidden}");
    }

    [TestMethod]
    public void StatusNames_DoNotExposeCleanupExecutionStates()
    {
        var names = Enum.GetNames<GovernanceDataRetentionRuleStatus>();
        foreach (var forbidden in ForbiddenStatusNames())
            Assert.IsFalse(names.Any(name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase)), $"Forbidden status exists: {forbidden}");
    }

    [TestMethod]
    public void PublicPropertyNames_DoNotExposePayloadOrSqlCommandFields()
    {
        var propertyNames = typeof(GovernanceDataRetentionRuleResult).Assembly
            .GetTypes()
            .Where(type => type.Namespace is "IronDev.Core.Governance")
            .Where(type => type.Name.Contains("GovernanceData", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Select(property => property.Name)
            .ToArray();

        foreach (var forbidden in ForbiddenPropertyNames())
            Assert.IsFalse(propertyNames.Any(name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase)), $"Forbidden property exists: {forbidden}");
    }

    [TestMethod]
    public void ProductionFiles_DoNotContainForbiddenImplementationMarkers()
    {
        foreach (var file in ProductionFiles())
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot(), file));
            foreach (var marker in ForbiddenImplementationMarkers())
                Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"{file} must not contain forbidden implementation marker '{marker}'.");
        }
    }

    [TestMethod]
    public void ProductionService_DoesNotContainExecutableCleanupMethodNames()
    {
        var files = new[]
        {
            Path.Combine("IronDev.Core", "Governance", "IGovernanceDataRetentionRuleService.cs"),
            Path.Combine("IronDev.Core", "Governance", "GovernanceDataRetentionRuleService.cs")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot(), file));
            foreach (var forbidden in ForbiddenMethodNames().Where(name => !string.Equals(name, "Cleanup", StringComparison.OrdinalIgnoreCase)))
                Assert.IsFalse(text.Contains($" {forbidden}(", StringComparison.OrdinalIgnoreCase), $"{file} must not expose executable method '{forbidden}'.");
        }
    }

    [TestMethod]
    public void ChangedFiles_DoNotAddApiCliSqlUiHostedServiceOrRuntimeSurface()
    {
        var changedFiles = ChangedFilesSinceMain()
            .Where(file => file.Contains("GovernanceDataRetentionRule", StringComparison.OrdinalIgnoreCase) ||
                           file.Contains("PR150", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("Database/", StringComparison.Ordinal)), "PR150 must not add SQL migrations.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("IronDev.Api/", StringComparison.Ordinal)), "PR150 must not add API surface.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("tools/IronDev.Cli/", StringComparison.Ordinal)), "PR150 must not add CLI commands.");
        Assert.IsFalse(changedFiles.Any(file => file.StartsWith("IronDev.Client/", StringComparison.Ordinal)), "PR150 must not add UI/client files.");
        Assert.IsFalse(changedFiles.Any(file => file.Contains("HostedService", StringComparison.OrdinalIgnoreCase)), "PR150 must not add hosted services.");
        Assert.IsFalse(changedFiles.Any(file => file.Contains("BackgroundService", StringComparison.OrdinalIgnoreCase)), "PR150 must not add background workers.");
    }

    [TestMethod]
    public void Receipt_RecordsRetentionBoundary()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR150_GOVERNANCE_DATA_RETENTION_AND_CLEANUP_RULES.md"));

        StringAssert.Contains(text, "PR150 adds Governance Data Retention and Cleanup Rules.");
        StringAssert.Contains(text, "Retention rule evaluation is not cleanup execution.");
        StringAssert.Contains(text, "Cleanup eligibility is not deletion permission.");
        StringAssert.Contains(text, "Cleanup recommendation is not cleanup approval.");
        StringAssert.Contains(text, "Expired retention window is not purge authority.");
        StringAssert.Contains(text, "Archive recommendation is not archive execution.");
        StringAssert.Contains(text, "Redaction recommendation is not redaction execution.");
        StringAssert.Contains(text, "Legal hold beats cleanup.");
        StringAssert.Contains(text, "Audit hold beats cleanup.");
        StringAssert.Contains(text, "Governance events are append-only and preserved.");
        StringAssert.Contains(text, "Authority decision records are preserved unless a later explicitly governed retention executor exists.");
        StringAssert.Contains(text, "Retention durations are engineering defaults for future governed review, not legal advice and not cleanup execution.");
        StringAssert.Contains(text, "does not swing the broom");
    }

    private static IReadOnlyList<string> ProductionFiles() =>
    [
        Path.Combine("IronDev.Core", "Governance", "GovernanceDataRetentionRuleModels.cs"),
        Path.Combine("IronDev.Core", "Governance", "IGovernanceDataRetentionRuleService.cs"),
        Path.Combine("IronDev.Core", "Governance", "GovernanceDataRetentionRuleService.cs")
    ];

    private static IReadOnlyList<string> ForbiddenMethodNames() =>
    [
        "Delete",
        "DeleteAsync",
        "Purge",
        "PurgeAsync",
        "Archive",
        "ArchiveAsync",
        "Redact",
        "RedactAsync",
        "Cleanup",
        "CleanupAsync",
        "RunCleanup",
        "RunCleanupAsync",
        "ScheduleCleanup",
        "ScheduleCleanupAsync",
        "ExecuteCleanup",
        "ExecuteCleanupAsync",
        "DropTable",
        "Truncate",
        "MutateSql",
        "BypassLegalHold",
        "BypassAuditHold"
    ];

    private static IReadOnlyList<string> ForbiddenStatusNames() =>
    [
        "Deleted",
        "Purged",
        "Archived",
        "Redacted",
        "Cleaned",
        "CleanupExecuted",
        "CleanupApproved",
        "ReadyToDelete",
        "ReadyToPurge",
        "DisposalApproved"
    ];

    private static IReadOnlyList<string> ForbiddenPropertyNames() =>
    [
        "DeleteCommand",
        "PurgeCommand",
        "ArchiveCommand",
        "RedactionCommand",
        "SqlCommand",
        "CleanupSql",
        "DropSql",
        "TruncateSql",
        "ConnectionString",
        "RawPayload",
        "PayloadJson",
        "RawPrompt",
        "RawCompletion",
        "RawToolOutput",
        "PrivateReasoning",
        "HiddenReasoning",
        "ChainOfThought",
        "SourceContent",
        "PatchPayload"
    ];

    private static IReadOnlyList<string> ForbiddenImplementationMarkers() =>
    [
        "File.Delete",
        "Directory.Delete",
        "DROP TABLE",
        "TRUNCATE",
        "DELETE FROM",
        "UPDATE ",
        "INSERT INTO",
        "ALTER TABLE",
        "CREATE TABLE",
        "SqlCommand",
        "ExecuteNonQuery",
        "ExecuteAsync",
        "Process.Start",
        "IHostedService",
        "BackgroundService",
        "Timer",
        "Cron",
        "Scheduler"
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
