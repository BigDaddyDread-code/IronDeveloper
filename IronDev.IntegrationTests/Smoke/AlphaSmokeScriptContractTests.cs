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
                     "ApiAvailable",
                     "ApiAuthMissing",
                     "SqlUnavailable",
                     "SqlAvailable",
                     "LocalOverrideMissing",
                     "RootSafetyNotEvaluated",
                     "UnsafeRoot",
                     "RootSafetyBlocked",
                     "DeterministicModelNotConfigured",
                     "LiveModelNotConfigured",
                     "LiveModelModeNotImplemented",
                     "TicketPersistFailed",
                     "TicketPersisted",
                     "ReadinessBlocked",
                     "SkeletonRunStartFailed",
                     "CriticPackageMissing",
                     "CriticReviewFailed",
                     "CriticReviewRequestNotAutomated",
                     "CriticReviewRecorded",
                     "GateStateUnexpected",
                     "AcceptedApprovalRequired",
                     "AcceptedApprovalPersisted",
                     "AcceptedApprovalRecorded",
                     "ApprovalPhraseMissing",
                     "ApprovalPhraseMismatch",
                     "ApprovalTargetHashMismatch",
                     "ContinuationRefused",
                     "ContinuationUnblocked",
                     "ContinuationRequiresCriticReview",
                     "ContinuationRequiresFindingDisposition",
                     "ApplyRefused",
                     "Applied",
                     "ApplyRequiresContinuation",
                     "ApplyTargetMismatch",
                     "ApplyReceiptMissing",
                     "FinalReportMissing",
                     "ReportMissing",
                     "ReceiptWriteFailed",
                     "SourceRootDirty",
                     "SourceRootMutationDetected",
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
        var source = File.ReadAllText(RepoFile("Scripts", "smoke", "alpha-smoke.ps1"));

        StringAssert.Contains(source, "if ($RunUntil -eq \"Readiness\")");
        StringAssert.Contains(source, "$script:ReceiptPath = $null");
        StringAssert.Contains(source, "Remove-Item Env:ALPHA_SMOKE_RECEIPT");
        StringAssert.Contains(source, "ReceiptWriteSkipped");
        StringAssert.Contains(source, "no run receipt exists before skeleton run");
        StringAssert.Contains(source, "Readiness mode writes alpha-smoke result and summary only");
    }

    [TestMethod]
    public void AlphaSmokeScript_AppliedModeRequiresExplicitHumanApprovalPhrase()
    {
        var missingApproval = RunPowerShell("-RunUntil", "Applied", "-Json");
        var missingPhrase = RunPowerShell("-RunUntil", "Applied", "-RecordHumanApproval", "-Json");
        var wrongPhrase = RunPowerShell(
            "-RunUntil",
            "Applied",
            "-RecordHumanApproval",
            "-ApprovalPhrase",
            "approve everything",
            "-Json");
        var source = File.ReadAllText(RepoFile("Scripts", "smoke", "alpha-smoke.ps1"));

        Assert.AreNotEqual(0, missingApproval.ExitCode, "Applied mode must not create approval by default.");
        StringAssert.Contains(missingApproval.Output, "AcceptedApprovalRequired");
        StringAssert.Contains(missingApproval.Output, "The smoke never creates approval by default");

        Assert.AreNotEqual(0, missingPhrase.ExitCode, "Applied mode must require a phrase.");
        StringAssert.Contains(missingPhrase.Output, "ApprovalPhraseMissing");

        Assert.AreNotEqual(0, wrongPhrase.ExitCode, "Applied mode must reject unbound approval text.");
        StringAssert.Contains(wrongPhrase.Output, "ApprovalPhraseMismatch");

        StringAssert.Contains(source, "\"FullyQualifiedName~AlphaLoopSmokeTests.AlphaSmoke_OneTicket_ReachesApplied_WithDeterministicApproval\"");
        StringAssert.Contains(source, "$env:ALPHA_SMOKE_APPROVAL_PHRASE = $ApprovalPhrase");
        StringAssert.Contains(source, "I approve continuation for run <runId> package <hash>");
    }

    [TestMethod]
    public void AlphaSmokeScript_RequireExistingAcceptedApproval_SelectsSqlApiPersistedPath()
    {
        var source = File.ReadAllText(RepoFile("Scripts", "smoke", "alpha-smoke.ps1"));

        StringAssert.Contains(source, "-RequireExistingAcceptedApproval");
        StringAssert.Contains(source, "REL-3 path: the test owns the governed API approval request and proves SQL persistence.");
        StringAssert.Contains(source, "\"FullyQualifiedName~AlphaSmokeApiPersistenceTests.Rel3_OneTicket_ReachesApplied_ThroughSqlBackedApi\"");
        StringAssert.Contains(source, "\"IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj\"");
        StringAssert.Contains(source, "SqlAvailable");
        StringAssert.Contains(source, "ApiAvailable");
        StringAssert.Contains(source, "TicketPersisted");
        StringAssert.Contains(source, "AcceptedApprovalPersisted");
        StringAssert.Contains(source, "runReceipt.sqlPersisted");
        StringAssert.Contains(source, "runReceipt.apiPersisted");
    }

    [TestMethod]
    public void Rel3FullSqlLane_SeedsApiTestOutputConnectionString_AndApiBaseKeepsCiOverride()
    {
        var fullSqlScript = File.ReadAllText(RepoFile("Scripts", "ci", "run-full-sql-integration-ci.ps1"));
        var apiTestBase = File.ReadAllText(RepoFile("IronDev.IntegrationTests.Api", "ApiTestBase.cs"));

        StringAssert.Contains(fullSqlScript, "IronDev.IntegrationTests.Api\\bin\\Debug\\net10.0\\appsettings.Test.json");
        StringAssert.Contains(fullSqlScript, "$env:ConnectionStrings__IronDeveloperDb = $connectionString");
        StringAssert.Contains(fullSqlScript, "REL-3 SQL API alpha smoke");
        StringAssert.Contains(fullSqlScript, "IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj");
        StringAssert.Contains(apiTestBase, "[\"Ai:Provider\"] = \"fake\"");
        StringAssert.Contains(apiTestBase, "services.RemoveAll<ILLMService>()");
        StringAssert.Contains(apiTestBase, "services.AddScoped<ILLMService, FakeLlmService>()");

        var jsonLoadIndex = apiTestBase.IndexOf("cfg.AddJsonFile(path, optional: false)", StringComparison.Ordinal);
        var overrideIndex = apiTestBase.IndexOf("[\"ConnectionStrings:IronDeveloperDb\"] = TestConnectionString()", StringComparison.Ordinal);
        var fakeProviderIndex = apiTestBase.IndexOf("[\"Ai:Provider\"] = \"fake\"", StringComparison.Ordinal);

        Assert.IsTrue(jsonLoadIndex >= 0, "ApiTestBase must load appsettings.Test.json for API integration tests.");
        Assert.IsTrue(overrideIndex > jsonLoadIndex,
            "ApiTestBase must reapply the CI/environment connection string after appsettings.Test.json so Linux SQL CI does not fall back to LocalDB.");
        Assert.IsTrue(fakeProviderIndex > jsonLoadIndex,
            "ApiTestBase must force fake AI after appsettings.Test.json so SQL/API smoke does not require external model credentials.");
    }

    [TestMethod]
    public void AlphaSmokeReceipt_StopsAtGate_AndDoesNotCreateApprovalContinuationOrApply()
    {
        var source = File.ReadAllText(RepoFile("IronDev.IntegrationTests", "AlphaLoopSmokeTests.cs"));

        StringAssert.Contains(source, "AlphaSmoke_OneTicket_ReachesHumanGate_WithADeterministicBuilder");
        StringAssert.Contains(source, "PausedForApproval");
        StringAssert.Contains(source, "AcceptedApprovalCreated: false");
        StringAssert.Contains(source, "AcceptedApprovalRecorded: false");
        StringAssert.Contains(source, "ContinuationRequested: false");
        StringAssert.Contains(source, "ApplyRequested: false");
        StringAssert.Contains(source, "AlphaSmoke_OneTicket_ReachesApplied_WithDeterministicApproval");
        StringAssert.Contains(source, "AcceptedApprovalRecorded: true");
        StringAssert.Contains(source, "Accepted approval only unblocks continuation; it is still not apply permission.");
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
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        Assert.IsTrue(process.WaitForExit(TimeSpan.FromSeconds(180)), "alpha-smoke.ps1 contract timed out.");
        Task.WaitAll(stdoutTask, stderrTask);
        return (process.ExitCode, stdoutTask.Result + stderrTask.Result);
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
