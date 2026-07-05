using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Demo;

[TestClass]
[TestCategory("DemoSeed")]
[TestCategory("AlphaSmoke")]
[TestCategory("ReleaseReadiness")]
[TestCategory("Contract")]
[TestCategory("Boundary")]
public sealed class DemoSeedScriptContractTests
{
    [TestMethod]
    public void DemoSeed_CheckOnly_DoesNotMutate()
    {
        var result = RunPowerShell("-CheckOnly", "-Json");

        Assert.AreEqual(0, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;

        Assert.AreEqual("CheckOnly", root.GetProperty("mode").GetString());
        Assert.AreEqual("RunningApi", root.GetProperty("seedTarget").GetString());
        Assert.IsFalse(root.GetProperty("createLiveChatTicket").GetBoolean());
        Assert.AreEqual("Passed", root.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("outputDirectory").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("receiptPath").ValueKind);
        StringAssert.Contains(result.Output, "DemoRootSafetyNotEvaluated");
        StringAssert.Contains(result.Output, "DemoReceiptWriteSkipped");
    }

    [TestMethod]
    public void DemoSeed_BlocksWhenRootSafetyBlocked()
    {
        var result = RunPowerShell("-Seed", "-OutputDirectory", RepoRoot(), "-Json");

        Assert.AreNotEqual(0, result.ExitCode, "DEMO-1 seed must refuse repository-root output.");
        StringAssert.Contains(result.Output, "DemoRootSafetyBlocked");
        StringAssert.Contains(result.Output, "UnderRepositoryRoot");
    }

    [TestMethod]
    public void DemoSeed_UsesProductApisForGovernedActions()
    {
        var source = ScriptSource();

        StringAssert.Contains(source, "[string]$SeedTarget = \"RunningApi\"");
        StringAssert.Contains(source, "Invoke-DemoApi");
        StringAssert.Contains(source, "/api/auth/login");
        StringAssert.Contains(source, "/api/tenants/select");
        StringAssert.Contains(source, "/api/projects");
        StringAssert.Contains(source, "/skeleton-runs");
        StringAssert.Contains(source, "/critic-review");
        StringAssert.Contains(source, "/accepted-approvals");
        StringAssert.Contains(source, "/continue");
        StringAssert.Contains(source, "/apply");
        StringAssert.Contains(source, "/report");
        StringAssert.Contains(source, "Running API health endpoint responded.");
        Assert.IsFalse(source.Contains("TestFixtures\\frontend", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("IronDev.TauriShell", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void DemoSeed_ProofHarnessModeRemainsExplicitForCiEvidence()
    {
        var source = ScriptSource();

        StringAssert.Contains(source, "SeedTarget -eq \"ProofHarness\"");
        StringAssert.Contains(source, "IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj");
        StringAssert.Contains(source, "DemoSeedApiDrivenTests.DemoSeed_BaselineHistory_IsApiDrivenAndSqlPersisted");
        StringAssert.Contains(source, "DEMO-1a uses the API integration test host with SQL-backed stores.");
        StringAssert.Contains(source, "DEMO-1a drives authenticated API routes in-process.");
    }

    [TestMethod]
    public void DemoSeed_RunningApiSeedIsIdempotentAndDoesNotOverwriteDemoSource()
    {
        var source = ScriptSource();

        StringAssert.Contains(source, "Get-ExistingRunningApiReceipt");
        StringAssert.Contains(source, "Existing DEMO-1b receipt was verified against the running API.");
        StringAssert.Contains(source, "BookSeller demo source copy already exists without a verified seed receipt.");
        StringAssert.Contains(source, "DemoIdempotencyConflict");
        StringAssert.Contains(source, "Resolve-DemoProject");
        StringAssert.Contains(source, "Resolve-DemoTicket");
    }

    [TestMethod]
    public void DemoSeed_DoesNotInsertFinalSqlState()
    {
        var script = ScriptSource();
        var apiProof = File.ReadAllText(RepoFile("IronDev.IntegrationTests.Api", "Demo", "DemoSeedApiDrivenTests.cs"));

        foreach (var source in new[] { script, apiProof })
        {
            AssertDoesNotContain(source, "INSERT INTO dbo.Runs");
            AssertDoesNotContain(source, "INSERT INTO dbo.RunEvents");
            AssertDoesNotContain(source, "INSERT INTO dbo.ProjectTickets");
            AssertDoesNotContain(source, "UPDATE dbo.Runs");
            AssertDoesNotContain(source, "UPDATE dbo.ProjectTickets");
            AssertDoesNotContain(source, "sqlcmd");
        }

        StringAssert.Contains(apiProof, "PostJsonAsync<TicketBuildRunDto>");
        StringAssert.Contains(apiProof, "CreateAcceptedApprovalAsync");
        StringAssert.Contains(apiProof, "/skeleton-runs/{started.RunId}/continue");
    }

    [TestMethod]
    public void DemoSeed_CreatesAppliedAndPausedBaselineWithoutLiveChatTicket()
    {
        var apiProof = File.ReadAllText(RepoFile("IronDev.IntegrationTests.Api", "Demo", "DemoSeedApiDrivenTests.cs"));

        StringAssert.Contains(apiProof, "DemoSeed_BaselineHistory_IsApiDrivenAndSqlPersisted");
        StringAssert.Contains(apiProof, "validate-book");
        StringAssert.Contains(apiProof, "search-by-author");
        StringAssert.Contains(apiProof, "Assert.AreEqual(\"Applied\", appliedState)");
        StringAssert.Contains(apiProof, "Assert.AreEqual(\"PausedForApproval\", pausedState)");
        StringAssert.Contains(apiProof, "DEMO-1 must not seed the live chat ticket ahead of the demo.");
        StringAssert.Contains(apiProof, "LiveChatTicketSeeded: false");
    }

    [TestMethod]
    public void DemoSeed_ReceiptRedactsSecretsAndUserPaths()
    {
        var script = ScriptSource();
        var apiProof = File.ReadAllText(RepoFile("IronDev.IntegrationTests.Api", "Demo", "DemoSeedApiDrivenTests.cs"));

        StringAssert.Contains(script, "Redact-UserPath");
        StringAssert.Contains(apiProof, "RedactPath");
        StringAssert.Contains(apiProof, "RedactionConfirmation");
        AssertDoesNotContain(apiProof, "ConnectionStrings__IronDeveloperDb");
        AssertDoesNotContain(apiProof, "Password=");
        AssertDoesNotContain(apiProof, "ApiKey");
    }

    [TestMethod]
    public void DemoSeed_ReportReconstructsFromSql()
    {
        var apiProof = File.ReadAllText(RepoFile("IronDev.IntegrationTests.Api", "Demo", "DemoSeedApiDrivenTests.cs"));

        StringAssert.Contains(apiProof, "/skeleton-runs/{started.RunId}/report");
        StringAssert.Contains(apiProof, "finalReport.LoopComplete");
        StringAssert.Contains(apiProof, "AssertBaselineSqlPersistenceAsync");
        StringAssert.Contains(apiProof, "SELECT State FROM dbo.Runs");
        StringAssert.Contains(apiProof, "SELECT EventType FROM dbo.RunEvents");
    }

    [TestMethod]
    public void Demo2_ChatConfirmedTicket_IsVisibleAndStartable()
    {
        var apiProof = File.ReadAllText(RepoFile("IronDev.IntegrationTests.Api", "Demo", "DemoSeedApiDrivenTests.cs"));
        var flowScreen = File.ReadAllText(RepoFile("IronDev.TauriShell", "src", "flow", "workitem", "WorkItemScreen.tsx"));
        var source = ScriptSource();

        StringAssert.Contains(apiProof, "Demo2_ChatConfirmedTicket_IsVisibleAndStartableThroughApi");
        StringAssert.Contains(apiProof, "/chat/sessions");
        StringAssert.Contains(apiProof, "/tickets/draft/confirm");
        StringAssert.Contains(apiProof, "/tickets/{ticket.Id}/skeleton-runs");
        StringAssert.Contains(apiProof, "BulkDiscountKey");
        StringAssert.Contains(apiProof, "tickets.Any(item => item.Id == ticket.Id)");
        StringAssert.Contains(apiProof, "Assert.AreEqual(\"PausedForApproval\", started.Status)");

        StringAssert.Contains(flowScreen, "flow.shape.promote");
        StringAssert.Contains(flowScreen, "flow.ticket.startRun");
        StringAssert.Contains(flowScreen, "Readiness gate: satisfied. Promotion creates the ticket");
        StringAssert.Contains(flowScreen, "Starting a run builds and tests in a disposable workspace");

        StringAssert.Contains(source, "[switch]$CreateLiveChatTicket");
        StringAssert.Contains(source, "Invoke-LiveChatTicketProof");
        StringAssert.Contains(source, "DEMO-2b created a live chat-confirmed ticket and started it to PausedForApproval.");
        StringAssert.Contains(source, "unless explicitly requested");
    }

    [TestMethod]
    public void DemoSeed_ReceiptDocumentsBoundary()
    {
        var receipt = File.ReadAllText(RepoFile("Docs", "receipts", "DEMO1_API_DRIVEN_DEMO_SEED.md"));

        StringAssert.Contains(receipt, "A demo seed may replay history. It may not invent authority.");
        StringAssert.Contains(receipt, "No direct SQL final-state insert");
        StringAssert.Contains(receipt, "No live chat ticket is seeded ahead of the demo");
        StringAssert.Contains(receipt, "Evidence is not approval");
        StringAssert.Contains(receipt, "DEMO-2");
    }

    [TestMethod]
    public void DemoSeed_LongLivedReceiptDocumentsUiReadableSeedBoundary()
    {
        var receipt = File.ReadAllText(RepoFile("Docs", "receipts", "DEMO1B_LONG_LIVED_DEMO_SEED.md"));

        StringAssert.Contains(receipt, "DEMO-1b");
        StringAssert.Contains(receipt, "long-lived local API");
        StringAssert.Contains(receipt, "UI can read");
        StringAssert.Contains(receipt, "No direct SQL final-state insert");
        StringAssert.Contains(receipt, "No frontend fixtures");
        StringAssert.Contains(receipt, "DEMO-2b");
        StringAssert.Contains(receipt, "CreateLiveChatTicket");
    }

    private static (int ExitCode, string Output) RunPowerShell(params string[] arguments)
    {
        var script = RepoFile("Scripts", "demo", "demo-seed.ps1");
        var shell = ResolvePowerShell();
        var startInfo = new ProcessStartInfo(shell)
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        Assert.IsTrue(process.WaitForExit(TimeSpan.FromSeconds(180)), "demo-seed.ps1 contract timed out.");
        Task.WaitAll(stdoutTask, stderrTask);
        return (process.ExitCode, stdoutTask.Result + stderrTask.Result);
    }

    private static string ScriptSource() =>
        File.ReadAllText(RepoFile("Scripts", "demo", "demo-seed.ps1"));

    private static void AssertDoesNotContain(string text, string forbidden) =>
        Assert.IsFalse(text.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"Forbidden text found: {forbidden}");

    private static string RepoFile(params string[] parts) =>
        Path.Combine(RepoRoot(), Path.Combine(parts));

    private static string ResolvePowerShell()
    {
        foreach (var candidate in new[] { "pwsh", "powershell" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(candidate, "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                });
                if (process is not null && process.WaitForExit(TimeSpan.FromSeconds(10)) && process.ExitCode == 0)
                    return candidate;
            }
            catch
            {
                // Try the next shell.
            }
        }

        Assert.Fail("PowerShell executable not found.");
        return "powershell";
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IronDev.slnx.");
    }
}
