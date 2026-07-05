using System.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Smoke;

[TestClass]
[TestCategory("SkeletonRun")]
public sealed class AlphaSmokeScriptContractTests
{
    [TestMethod]
    public void AlphaSmokeScript_DefaultsToCheckOnly_AndWritesNoSmokeArtifacts()
    {
        var result = RunPowerShell("-Json");

        Assert.AreEqual(0, result.ExitCode, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;

        Assert.AreEqual("CheckOnly", root.GetProperty("runUntil").GetString());
        Assert.AreEqual("Passed", root.GetProperty("status").GetString());
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("outputDirectory").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, root.GetProperty("receiptPath").ValueKind);
        StringAssert.Contains(result.Output, "RootSafetyNotEvaluated");
        StringAssert.Contains(result.Output, "ReceiptWriteSkipped");
    }

    [TestMethod]
    public void AlphaSmokeScript_LiveModeBlocksWithNamedReason_AndDoesNotFallbackToDeterministic()
    {
        var result = RunPowerShell("-ModelMode", "Live", "-RunUntil", "Gate", "-Json");

        Assert.AreNotEqual(0, result.ExitCode, "Live mode is not implemented in D-2a and must block.");
        StringAssert.Contains(result.Output, "LiveModelModeNotImplemented");
        StringAssert.Contains(result.Output, "must never fall back to deterministic");
    }

    [TestMethod]
    public void AlphaSmokeScript_UnsupportedExistingIds_BlockOrAreAbsent()
    {
        var existingTicketResult = RunPowerShell("-ExistingTicketId", "ticket-existing", "-Json");
        var existingRunResult = RunPowerShell("-ExistingRunId", "run-existing", "-Json");
        var source = File.ReadAllText(RepoFile("Scripts", "smoke", "alpha-smoke.ps1"));

        Assert.AreNotEqual(0, existingTicketResult.ExitCode, "D-2a must not silently accept an existing ticket ID.");
        StringAssert.Contains(existingTicketResult.Output, "ExistingTicketIdNotSupported");
        StringAssert.Contains(existingTicketResult.Output, "D-2a does not resume or use existing ticket IDs");

        Assert.AreNotEqual(0, existingRunResult.ExitCode, "D-2a must not silently accept an existing run ID.");
        StringAssert.Contains(existingRunResult.Output, "ExistingRunIdNotSupported");
        StringAssert.Contains(existingRunResult.Output, "D-2a does not resume existing skeleton runs");

        Assert.IsFalse(source.Contains("$NoStartApi", StringComparison.Ordinal),
            "D-2a must not expose an unsupported API start-control switch.");
        Assert.IsFalse(source.Contains("$NoStartUi", StringComparison.Ordinal),
            "D-2a must not expose an unsupported UI start-control switch.");
        Assert.IsFalse(source.Contains("$TimeoutSeconds", StringComparison.Ordinal),
            "D-2a must not expose an unsupported timeout switch.");
    }

    [TestMethod]
    public void AlphaSmokeScript_ContainsRequiredStagesAndReasonCodes()
    {
        var source = File.ReadAllText(RepoFile("Scripts", "smoke", "alpha-smoke.ps1"));

        foreach (var required in new[]
                 {
                     "RepoCheck",
                     "ToolchainCheck",
                     "LocalConfigCheck",
                     "RootSafetyCheck",
                     "SqlCheck",
                     "ApiCheck",
                     "FixtureCheck",
                     "TicketLoad",
                     "TicketPersist",
                     "ReadinessCheck",
                     "SkeletonRunStart",
                     "RunEvidenceRefresh",
                     "CriticPackageFetch",
                     "CriticReviewRequest",
                     "GateStateVerify",
                     "ReportFetch",
                     "ReceiptWrite",
                     "RepoRootNotFound",
                     "BookSellerSampleMissing",
                     "BookSellerTicketsMissing",
                     "TicketKeyNotFound",
                     "ExistingTicketIdNotSupported",
                     "ExistingRunIdNotSupported",
                     "DotnetMissing",
                     "NodeMissing",
                     "ApiUnavailable",
                     "ApiAuthMissing",
                     "SqlUnavailable",
                     "LocalOverrideMissing",
                     "RootSafetyNotEvaluated",
                     "UnsafeRoot",
                     "DeterministicModelNotConfigured",
                     "LiveModelNotConfigured",
                     "LiveModelModeNotImplemented",
                     "TicketPersistFailed",
                     "ReadinessBlocked",
                     "SkeletonRunStartFailed",
                     "CriticPackageMissing",
                     "CriticReviewFailed",
                     "CriticReviewRequestNotAutomated",
                     "GateStateUnexpected",
                     "ReportMissing",
                     "ReceiptWriteFailed",
                     "SourceRepoDirtyBeforeRun",
                     "SourceRepoChangedUnexpectedly"
                 })
        {
            StringAssert.Contains(source, required, $"Missing D-series stage/reason code: {required}");
        }
    }

    [TestMethod]
    public void AlphaSmokeScript_DefaultOutputRoot_IsOutsideRepository_AndRootChecked()
    {
        var source = File.ReadAllText(RepoFile("Scripts", "smoke", "alpha-smoke.ps1"));

        StringAssert.Contains(source, "LOCALAPPDATA");
        StringAssert.Contains(source, "UnderRepositoryRoot");
        StringAssert.Contains(source, "PathContainsSymlinkOrReparsePoint");
        Assert.IsFalse(source.Contains("artifacts\\alpha-smoke", StringComparison.OrdinalIgnoreCase),
            "Default smoke output must not be under the source repository.");
        Assert.IsFalse(source.Contains("artifacts/alpha-smoke", StringComparison.OrdinalIgnoreCase),
            "Default smoke output must not be under the source repository.");
    }

    [TestMethod]
    public void AlphaSmokeScript_PostRunSourceDirtyCheck_IsEnforced()
    {
        var source = File.ReadAllText(RepoFile("Scripts", "smoke", "alpha-smoke.ps1"));

        StringAssert.Contains(source, "$postRepoStatus = git -C $repoRoot status --porcelain");
        StringAssert.Contains(source, "The source repository changed during smoke execution.");
        Assert.IsTrue(CountOccurrences(source, "SourceRepoChangedUnexpectedly") >= 2,
            "SourceRepoChangedUnexpectedly must be enforced after the run, not only declared.");
    }

    [TestMethod]
    public void AlphaSmokeScript_ReadinessMode_DoesNotAdvertiseMissingRunReceipt()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), $"irondev-alpha-readiness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);

        try
        {
            var result = RunPowerShell("-RunUntil", "Readiness", "-OutputDirectory", outputRoot, "-Json");

            Assert.AreEqual(0, result.ExitCode, result.Output);
            using var document = JsonDocument.Parse(result.Output);
            var root = document.RootElement;

            Assert.AreEqual("Readiness", root.GetProperty("runUntil").GetString());
            Assert.AreEqual("Passed", root.GetProperty("status").GetString());
            Assert.AreEqual(JsonValueKind.Null, root.GetProperty("receiptPath").ValueKind);
            StringAssert.Contains(result.Output, "ReceiptWriteSkipped");
            StringAssert.Contains(result.Output, "no run receipt exists before skeleton run");
            Assert.IsFalse(File.Exists(Path.Combine(outputRoot, "run-receipt.json")),
                "Readiness mode must not advertise or create a skeleton run receipt.");
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    [TestMethod]
    public void AlphaSmokeReceipt_StopsAtGate_AndDoesNotCreateApprovalContinuationOrApply()
    {
        var source = File.ReadAllText(RepoFile("IronDev.IntegrationTests", "AlphaLoopSmokeTests.cs"));

        StringAssert.Contains(source, "PausedForApproval");
        StringAssert.Contains(source, "AcceptedApprovalCreated: false");
        StringAssert.Contains(source, "ContinuationRequested: false");
        StringAssert.Contains(source, "ApplyRequested: false");
        Assert.IsFalse(source.Contains(".Seed(", StringComparison.Ordinal),
            "D-2a must not seed a fake accepted approval.");
        Assert.IsFalse(source.Contains("ContinueAsync(ProjectId", StringComparison.Ordinal),
            "D-2a must not continue past the human gate.");
        Assert.IsFalse(source.Contains("ApplyAsync(ProjectId", StringComparison.Ordinal),
            "D-2a must not apply source.");
    }

    private static (int ExitCode, string Output) RunPowerShell(params string[] arguments)
    {
        var script = RepoFile("Scripts", "smoke", "alpha-smoke.ps1");
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
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.IsTrue(process.WaitForExit(TimeSpan.FromSeconds(60)), "alpha-smoke.ps1 check-only contract timed out.");
        return (process.ExitCode, stdout + stderr);
    }

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

        Assert.Fail("Neither pwsh nor powershell is available for alpha-smoke.ps1 contract tests.");
        return "powershell";
    }

    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
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

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
