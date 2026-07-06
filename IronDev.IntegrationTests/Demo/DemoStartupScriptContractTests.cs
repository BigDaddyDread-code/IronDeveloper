using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Demo;

[TestClass]
[TestCategory("DemoStartup")]
[TestCategory("AlphaSmoke")]
[TestCategory("ReleaseReadiness")]
[TestCategory("Contract")]
[TestCategory("Boundary")]
public sealed class DemoStartupScriptContractTests
{
    [TestMethod]
    public void DemoStartup_CheckOnly_ReportsDeterministicBannerAndOneNextSafeAction()
    {
        var result = RunPowerShell(
            "-CheckOnly",
            "-NoStart",
            "-Json",
            "-ApiBaseUrl",
            "http://127.0.0.1:1",
            "-UiBaseUrl",
            "http://127.0.0.1:1");

        Assert.AreEqual(0, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;

        Assert.AreEqual("CheckOnly", root.GetProperty("mode").GetString());
        Assert.AreEqual("Deterministic", root.GetProperty("modelMode").GetString());
        Assert.IsFalse(root.GetProperty("liveModelFallbackAllowed").GetBoolean());
        StringAssert.Contains(root.GetProperty("modelModeBanner").GetString()!, "Deterministic-only local alpha preview");
        StringAssert.Contains(root.GetProperty("modelModeBanner").GetString()!, "not a live model run");
        Assert.IsFalse(string.IsNullOrWhiteSpace(root.GetProperty("nextSafeAction").GetString()));

        AssertHasStage(root, "RootSafetyCheck");
        AssertHasStage(root, "SqlCheck");
        AssertHasStage(root, "ApiUrlCheck");
        AssertHasStage(root, "UiUrlCheck");
        AssertHasStage(root, "ApiCheck");
        AssertHasStage(root, "UiCheck");
        AssertHasStage(root, "DemoSeedCheck");
        AssertHasStage(root, "OpenApp");
    }

    [TestMethod]
    public void DemoStartup_LiveModelModeBlocksWithoutSilentFallback()
    {
        var result = RunPowerShell(
            "-CheckOnly",
            "-NoStart",
            "-Json",
            "-ModelMode",
            "Live",
            "-ApiBaseUrl",
            "http://127.0.0.1:1",
            "-UiBaseUrl",
            "http://127.0.0.1:1");

        Assert.AreEqual(0, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;

        Assert.AreEqual("Live", root.GetProperty("modelMode").GetString());
        Assert.AreEqual("Blocked", root.GetProperty("status").GetString());
        Assert.IsFalse(root.GetProperty("liveModelFallbackAllowed").GetBoolean());
        StringAssert.Contains(result.Output, "DemoStartupLiveModelUnsupported");
        StringAssert.Contains(result.Output, "No silent deterministic fallback is allowed");
        AssertDoesNotContain(result.Output, "fallback to deterministic");
        AssertDoesNotContain(result.Output, "live model run passed");
    }

    [TestMethod]
    public void DemoStartup_LiveModelUnsupportedPreventsProcessStart()
    {
        var result = RunPowerShell(
            "-Json",
            "-ModelMode",
            "Live",
            "-ApiBaseUrl",
            "http://127.0.0.1:1",
            "-UiBaseUrl",
            "http://127.0.0.1:1",
            "-StartTimeoutSeconds",
            "1");

        Assert.AreEqual(1, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;

        Assert.AreEqual("Blocked", root.GetProperty("status").GetString());
        StringAssert.Contains(result.Output, "DemoStartupLiveModelUnsupported");
        AssertStageStatus(root, "ApiCheck", "Skipped");
        AssertStageStatus(root, "UiCheck", "Skipped");
        AssertStageStatus(root, "DemoSeedCheck", "Skipped");
        Assert.AreEqual(0, root.GetProperty("startedProcesses").GetArrayLength());
    }

    [TestMethod]
    public void DemoStartup_BlocksRemoteApiBaseUrl()
    {
        var result = RunPowerShell(
            "-CheckOnly",
            "-NoStart",
            "-Json",
            "-ApiBaseUrl",
            "https://example.com",
            "-UiBaseUrl",
            "http://127.0.0.1:1");

        Assert.AreEqual(0, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;

        Assert.AreEqual("Blocked", root.GetProperty("status").GetString());
        StringAssert.Contains(result.Output, "DemoStartupApiBaseUrlNotLocal");
        StringAssert.Contains(result.Output, "NonLoopbackHost");
        AssertStageStatus(root, "ApiCheck", "Skipped");
        AssertStageStatus(root, "UiCheck", "Skipped");
        Assert.AreEqual(0, root.GetProperty("startedProcesses").GetArrayLength());
    }

    [TestMethod]
    public void DemoStartup_BlocksRemoteUiBaseUrl()
    {
        var result = RunPowerShell(
            "-CheckOnly",
            "-NoStart",
            "-Json",
            "-ApiBaseUrl",
            "http://127.0.0.1:1",
            "-UiBaseUrl",
            "https://example.com");

        Assert.AreEqual(0, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;

        Assert.AreEqual("Blocked", root.GetProperty("status").GetString());
        StringAssert.Contains(result.Output, "DemoStartupUiBaseUrlNotLocal");
        StringAssert.Contains(result.Output, "NonLoopbackHost");
        AssertStageStatus(root, "ApiCheck", "Skipped");
        AssertStageStatus(root, "UiCheck", "Skipped");
        Assert.AreEqual(0, root.GetProperty("startedProcesses").GetArrayLength());
    }

    [TestMethod]
    public void DemoStartup_BlocksUnsafeOutputRootWithoutStartingProcesses()
    {
        var result = RunPowerShell(
            "-CheckOnly",
            "-NoStart",
            "-Json",
            "-OutputDirectory",
            RepoRoot());

        Assert.AreEqual(0, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;

        Assert.AreEqual("Blocked", root.GetProperty("status").GetString());
        StringAssert.Contains(result.Output, "DemoStartupRootUnsafe");
        StringAssert.Contains(result.Output, "UnderRepositoryRoot");
        Assert.AreEqual(0, root.GetProperty("startedProcesses").GetArrayLength());
    }

    [TestMethod]
    public void DemoStartup_DoesNotStartApiWhenEarlierBlockerExists()
    {
        var source = ScriptSource();

        StringAssert.Contains(source, "function Has-StartupBlocker");
        StringAssert.Contains(source, "API start/check skipped because an earlier startup blocker exists.");
        AssertBefore(source, "if (Has-StartupBlocker)", "Start-ManagedProcess -Name \"IronDev.Api\"");
        AssertBefore(source, "API start/check skipped because an earlier startup blocker exists.", "Start-ManagedProcess -Name \"IronDev.Api\"");
    }

    [TestMethod]
    public void DemoStartup_DoesNotStartUiWhenEarlierBlockerExists()
    {
        var source = ScriptSource();

        StringAssert.Contains(source, "UI start/check skipped because an earlier startup blocker exists.");
        AssertBefore(source, "UI start/check skipped because an earlier startup blocker exists.", "Start-ManagedProcess -Name \"IronDev.TauriShell\"");
    }

    [TestMethod]
    public void DemoStartup_DoesNotDelegateSeedWhenEarlierBlockerExists()
    {
        var source = ScriptSource();

        StringAssert.Contains(source, "Demo seed was skipped because an earlier startup blocker exists.");
        AssertBefore(source, "Demo seed was skipped because an earlier startup blocker exists.", "Invoke-ChildScriptQuiet -ScriptPath $seedScript -WorkingDirectory $repoRoot -Arguments $seedArguments");
    }

    [TestMethod]
    public void DemoStartup_StartsOrVerifiesApiAndUiWhenNotCheckOnly()
    {
        var source = ScriptSource();

        StringAssert.Contains(source, "Start-ManagedProcess");
        StringAssert.Contains(source, "Wait-ForEndpoint");
        StringAssert.Contains(source, "dotnet run");
        StringAssert.Contains(source, "IronDev.Api\\IronDev.Api.csproj");
        StringAssert.Contains(source, "npm run dev");
        StringAssert.Contains(source, "VITE_IRONDEV_API_BASE_URL");
        StringAssert.Contains(source, "/health");
        StringAssert.Contains(source, "-WindowStyle Hidden");
    }

    [TestMethod]
    public void DemoStartup_DelegatesSeedToRunningApiPathWithoutDirectStateInsert()
    {
        var source = ScriptSource();

        StringAssert.Contains(source, "Scripts\\demo\\demo-seed.ps1");
        StringAssert.Contains(source, "\"-Seed\"");
        StringAssert.Contains(source, "\"-CheckOnly\"");
        StringAssert.Contains(source, "\"-ApiBaseUrl\"");
        StringAssert.Contains(source, "\"Deterministic\"");
        StringAssert.Contains(source, "\"-CreateLiveChatTicket\"");

        AssertDoesNotContain(source, "INSERT INTO dbo.Runs");
        AssertDoesNotContain(source, "INSERT INTO dbo.RunEvents");
        AssertDoesNotContain(source, "UPDATE dbo.Runs");
        AssertDoesNotContain(source, "UPDATE dbo.ProjectTickets");
        AssertDoesNotContain(source, "Database/local_dev_setup.sql");
    }

    [TestMethod]
    public void DemoStartup_ModelModeHonestyIsVisibleInFlowShell()
    {
        var flowShell = File.ReadAllText(RepoFile("IronDev.TauriShell", "src", "flow", "FlowShell.tsx"));
        var flowTest = File.ReadAllText(RepoFile("IronDev.TauriShell", "tests", "skeleton-run-stages.spec.ts"));

        StringAssert.Contains(flowShell, "flow.modelMode");
        StringAssert.Contains(flowShell, "Deterministic-only local alpha preview; not a live model run");
        StringAssert.Contains(flowShell, "deterministic fallback is never silent");
        StringAssert.Contains(flowTest, "Model mode: Deterministic-only local alpha preview");
        AssertDoesNotContain(flowShell, "live only when backend run evidence says so");
    }

    [TestMethod]
    public void DemoStartup_ReceiptDocumentsBoundaries()
    {
        var receipt = File.ReadAllText(RepoFile("Docs", "receipts", "DEMO5_DEMO6_LOCAL_STARTUP_MODE.md"));

        StringAssert.Contains(receipt, "Make demo startup boring.");
        StringAssert.Contains(receipt, "Deterministic-only local alpha preview");
        StringAssert.Contains(receipt, "No silent fallback from live to deterministic");
        StringAssert.Contains(receipt, "A startup script is a coordinator. It is not authority.");
        StringAssert.Contains(receipt, "The viewer always knows whether the run is deterministic or live.");
    }

    private static void AssertHasStage(JsonElement root, string stageName)
    {
        var found = root.GetProperty("stages")
            .EnumerateArray()
            .Any(stage => stage.GetProperty("stage").GetString() == stageName);

        Assert.IsTrue(found, $"Expected stage '{stageName}' in script output.");
    }

    private static void AssertStageStatus(JsonElement root, string stageName, string expectedStatus)
    {
        var stage = root.GetProperty("stages")
            .EnumerateArray()
            .FirstOrDefault(candidate => candidate.GetProperty("stage").GetString() == stageName);

        Assert.IsNotNull(stage.ValueKind == JsonValueKind.Undefined ? null : stage, $"Expected stage '{stageName}' in script output.");
        Assert.AreEqual(expectedStatus, stage.GetProperty("status").GetString(), $"Unexpected status for stage '{stageName}'.");
    }

    private static void AssertBefore(string text, string earlier, string later)
    {
        var earlierIndex = text.IndexOf(earlier, StringComparison.Ordinal);
        var laterIndex = text.IndexOf(later, StringComparison.Ordinal);

        Assert.IsTrue(earlierIndex >= 0, $"Expected source to contain '{earlier}'.");
        Assert.IsTrue(laterIndex >= 0, $"Expected source to contain '{later}'.");
        Assert.IsTrue(earlierIndex < laterIndex, $"Expected '{earlier}' to appear before '{later}'.");
    }

    private static (int ExitCode, string Output) RunPowerShell(params string[] arguments)
    {
        var script = RepoFile("Scripts", "demo", "start-v0.1-demo.ps1");
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
        Assert.IsTrue(process.WaitForExit(TimeSpan.FromSeconds(180)), "start-v0.1-demo.ps1 contract timed out.");
        Task.WaitAll(stdoutTask, stderrTask);
        return (process.ExitCode, stdoutTask.Result + stderrTask.Result);
    }

    private static string ScriptSource() =>
        File.ReadAllText(RepoFile("Scripts", "demo", "start-v0.1-demo.ps1"));

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
