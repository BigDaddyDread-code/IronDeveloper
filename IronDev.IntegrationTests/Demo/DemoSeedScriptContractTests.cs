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
    public void DemoSeed_BlocksRemoteApiBaseUrl()
    {
        var result = RunPowerShell("-Seed", "-ApiBaseUrl", "http://demo-api.example.com:5118", "-Json");

        Assert.AreNotEqual(0, result.ExitCode, "The demo seed mutates product state and must refuse a non-loopback API base URL.");
        StringAssert.Contains(result.Output, "\"ApiBaseUrlCheck\"");
        StringAssert.Contains(result.Output, "may only target a loopback-local API");
        AssertApiBaseUrlCheck(result.Output, "Blocked", "DemoApiBaseUrlNotLocal");
    }

    [TestMethod]
    public void DemoSeed_AllowsLocalhostApiBaseUrl()
    {
        var result = RunPowerShell("-CheckOnly", "-ApiBaseUrl", "http://localhost:5118", "-Json");

        Assert.AreEqual(0, result.ExitCode, result.Output);
        AssertApiBaseUrlCheck(result.Output, "Passed", "DemoApiBaseUrlLocal");
    }

    [TestMethod]
    public void DemoSeed_AllowsLoopbackApiBaseUrl()
    {
        foreach (var baseUrl in new[] { "http://127.0.0.1:5118", "http://[::1]:5118" })
        {
            var result = RunPowerShell("-CheckOnly", "-ApiBaseUrl", baseUrl, "-Json");

            Assert.AreEqual(0, result.ExitCode, $"{baseUrl}: {result.Output}");
            AssertApiBaseUrlCheck(result.Output, "Passed", "DemoApiBaseUrlLocal");
        }
    }

    private static void AssertApiBaseUrlCheck(string output, string expectedStatus, string expectedReasonCode)
    {
        using var document = JsonDocument.Parse(output);
        var stage = document.RootElement.GetProperty("stages").EnumerateArray()
            .Single(item => item.GetProperty("stage").GetString() == "ApiBaseUrlCheck");
        Assert.AreEqual(expectedStatus, stage.GetProperty("status").GetString());
        Assert.AreEqual(expectedReasonCode, stage.GetProperty("reasonCode").GetString());
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
        StringAssert.Contains(source, "BookSeller demo source copy already exists without a verified seed receipt");
        // Rehearsal residual R3: the deliberate refusal must NAME its remedy.
        StringAssert.Contains(source, "delete that folder, then rerun the seed");
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
    public void DemoSeed_ProvesEnvironmentRemainsUsableForNewTicketsAndRepeatedRuns()
    {
        var apiProof = File.ReadAllText(RepoFile("IronDev.IntegrationTests.Api", "Demo", "DemoSeedApiDrivenTests.cs"));
        var source = ScriptSource();

        // The proof harness proves usability on every run: a fresh ticket, a repeated
        // governed run, real build/test evidence, the human gate, and no silent approval.
        StringAssert.Contains(apiProof, "ProveRemainsUsableAsync");
        StringAssert.Contains(apiProof, "A fresh post-seed ticket must run to the human gate.");
        StringAssert.Contains(apiProof, "Repeated governed runs must be genuinely distinct runs.");
        StringAssert.Contains(apiProof, "SkeletonEvidencePackaged");
        StringAssert.Contains(apiProof, "report.CriticPackage.HashVerified");
        StringAssert.Contains(apiProof, "The probe must not apply.");
        StringAssert.Contains(apiProof, "AssertBaselineUnchangedAsync");
        StringAssert.Contains(apiProof, "The usability probe must not create accepted approval.");

        // The running-API script offers the same proof live behind an explicit switch.
        StringAssert.Contains(source, "[switch]$ProveUsable");
        StringAssert.Contains(source, "Invoke-UsabilityProbe");
        StringAssert.Contains(source, "DemoUsabilityProbePassed");
        StringAssert.Contains(source, "reached the human gate on real build/test evidence");
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
        StringAssert.Contains(flowScreen, "flow.workItem.primaryAction");
        StringAssert.Contains(flowScreen, "disabled={!workItem.primaryAction.allowed}");
        StringAssert.Contains(flowScreen, "getProjectWorkItem");
        StringAssert.Contains(flowScreen, "Readiness gate: satisfied. Promotion creates the ticket");

        StringAssert.Contains(source, "[switch]$CreateLiveChatTicket");
        StringAssert.Contains(source, "Invoke-LiveChatTicketProof");
        StringAssert.Contains(source, "DEMO-2b created a live chat-confirmed ticket and started it to PausedForApproval.");
        StringAssert.Contains(source, "unless explicitly requested");
    }

    [TestMethod]
    public void Hero_AdvisoryFindingDispositionGate_IsProvenAndExecutedInCi()
    {
        var apiProof = File.ReadAllText(RepoFile("IronDev.IntegrationTests.Api", "Demo", "DemoSeedApiDrivenTests.cs"));

        // The hero proof: one advisory finding, disposition-gated continuation.
        StringAssert.Contains(apiProof, "Hero_BulkDiscountAdvisoryFinding_RequiresDispositionBeforeApplied");
        StringAssert.Contains(apiProof, "finding-bulk-rounding-asymmetry");
        StringAssert.Contains(apiProof, "The hero finding is advisory, not a veto.");
        StringAssert.Contains(apiProof, "Continuation must refuse while the finding is undispositioned, even with a live accepted approval.");
        StringAssert.Contains(apiProof, "/findings/{HeroFindingId}/disposition");
        StringAssert.Contains(apiProof, "A disposition must carry a reason.");
        StringAssert.Contains(apiProof, "A disposition must answer a real finding.");
        StringAssert.Contains(apiProof, "Continuation must have been unblocked only after the disposition.");

        // Selection is not execution: the full SQL lane must EXECUTE the DEMO/HERO
        // proofs by exact name, not merely select their categories.
        var ciScript = File.ReadAllText(RepoFile("Scripts", "ci", "run-full-sql-integration-ci.ps1"));
        StringAssert.Contains(ciScript, "DemoSeedApiDrivenTests.DemoSeed_BaselineHistory_IsApiDrivenAndSqlPersisted");
        StringAssert.Contains(ciScript, "DemoSeedApiDrivenTests.Demo2_ChatConfirmedTicket_IsVisibleAndStartableThroughApi");
        StringAssert.Contains(ciScript, "DemoSeedApiDrivenTests.Hero_BulkDiscountAdvisoryFinding_RequiresDispositionBeforeApplied");
        StringAssert.Contains(ciScript, "DEMO seed and HERO disposition proofs");
    }

    [TestMethod]
    public void Hero2_LiveRealLoop_HasNoFakesAndItsFixesStayPinned()
    {
        var liveWalk = File.ReadAllText(RepoFile("IronDev.IntegrationTests.Api", "Smoke", "LiveModelHeroWalkTests.cs"));

        // The live walk runs real services on a live model and never falls back.
        StringAssert.Contains(liveWalk, "Hero2_LiveModel_BulkDiscount_WalksRealLoopToApplied");
        StringAssert.Contains(liveWalk, "It never falls back to deterministic mode.");
        StringAssert.Contains(liveWalk, "restoring the REAL stored-critic");
        StringAssert.Contains(liveWalk, "must run a real model — never the deterministic fake");
        StringAssert.Contains(liveWalk, "every real finding gets a human-shaped disposition");

        // HERO-3: bounded repair stays ARMED in the live walk, and the receipt keeps
        // recording the repair story honestly — including when no repair happened.
        StringAssert.Contains(liveWalk, "builder.UseSetting(\"SkeletonRepair:MaxAttempts\", \"1\")");
        StringAssert.Contains(liveWalk, "SelfRepairOccurred = finalReport.RepairAttempts.Count > 0");
        StringAssert.Contains(liveWalk, "InitialProposalId = finalReport.InitialProposal?.ProposalId");

        // HERO-2 product fix: the real critic executor's dependency stays registered —
        // without it the live critic route can never resolve in a real host.
        var program = File.ReadAllText(RepoFile("IronDev.Api", "Program.cs"));
        StringAssert.Contains(program, "AddScoped<IManualIndependentCriticAgentService, ManualIndependentCriticAgentService>()");

        // HERO-2 safety fix: the destructive test provisioning/reset connection is
        // pinned to the explicit test connection string and hard-guarded to
        // test-shaped catalogs only (*_Test locally, IronDev_CI_* ephemeral in CI).
        var apiTestBase = File.ReadAllText(RepoFile("IronDev.IntegrationTests.Api", "ApiTestBase.cs"));
        StringAssert.Contains(apiTestBase, "ConnectionString = TestConnectionString();");
        StringAssert.Contains(apiTestBase, "IsTestShapedCatalog");
        StringAssert.Contains(apiTestBase, "EndsWith(\"_Test\"");
        StringAssert.Contains(apiTestBase, "StartsWith(\"IronDev_CI_\"");
        StringAssert.Contains(apiTestBase, "Refusing to provision/reset database");

        // Root cause of the catalog escape: a migration with a USE statement rode
        // the provisioning connection onto another database. The test host now
        // refuses catalog-choosing migrations outright.
        StringAssert.Contains(apiTestBase, "AssertNoCatalogHijack");

        // The guard's own contract tests exist AND execute in the full SQL lane.
        var guardTests = File.ReadAllText(RepoFile("IronDev.IntegrationTests.Api", "ApiTestBaseCatalogGuardContractTests.cs"));
        StringAssert.Contains(guardTests, "ApiTestBase_AllowsLocalTestCatalog");
        StringAssert.Contains(guardTests, "ApiTestBase_AllowsCiEphemeralCatalog");
        StringAssert.Contains(guardTests, "ApiTestBase_RejectsProductionCatalog");
        StringAssert.Contains(guardTests, "ApiTestBase_RejectsLocalDeveloperCatalog");
        StringAssert.Contains(guardTests, "ApiTestBase_RefusesMigrationsThatChooseTheirOwnCatalog");
        var ciScript = File.ReadAllText(RepoFile("Scripts", "ci", "run-full-sql-integration-ci.ps1"));
        StringAssert.Contains(ciScript, "ApiTestBaseCatalogGuardContractTests");
    }

    [TestMethod]
    public void BoundedRepair_IsOffByDefaultBoundedAndExecutedInCi()
    {
        var orchestrator = File.ReadAllText(RepoFile("IronDev.Infrastructure", "Services", "TicketSkeletonRunService.cs"));

        // REPAIR-1 invariants: off unless explicitly configured, hard-clamped so no
        // configuration becomes an unbounded retry loop, terminal named failure when
        // the budget is exhausted, and attempt-scoped paths so history is never erased.
        StringAssert.Contains(orchestrator, "SkeletonRepair:MaxAttempts");
        StringAssert.Contains(orchestrator, "Math.Clamp(configured, 0, 3)");
        StringAssert.Contains(orchestrator, "Default 0: repair is off unless explicitly");
        StringAssert.Contains(orchestrator, "RepairBudgetExhausted");
        StringAssert.Contains(orchestrator, "SkeletonRepairAttemptStarted");
        StringAssert.Contains(orchestrator, "AttemptLabel = attemptNumber == 1 ? string.Empty : $\"repair-{attemptNumber}\"");

        // A repair attempt is proposal-shaped work, never authority.
        StringAssert.Contains(orchestrator, "A repair attempt is a new proposal, not authority");

        var proofs = File.ReadAllText(RepoFile("IronDev.IntegrationTests.Api", "Smoke", "BoundedRepairApiDrivenTests.cs"));
        StringAssert.Contains(proofs, "Repair_FirstAttemptFails_RepairReachesGate_HistoryPreserved");
        StringAssert.Contains(proofs, "Repair_BudgetExhausted_RunFailsWithNamedReason");
        StringAssert.Contains(proofs, "Repair_DisabledByDefault_FailureIsTerminalAndNamed");
        StringAssert.Contains(proofs, "repair earns nothing more");

        // Evidence binding (review-hardened): the gate/critic package/approval hash
        // bind to the FINAL repaired proposal, and the original stays as history.
        StringAssert.Contains(proofs, "Repair_CriticPackageReferencesRepairedProposalEvidence");
        StringAssert.Contains(proofs, "Repair_ReportFinalProposalIsRepairedProposal");
        StringAssert.Contains(proofs, "Repair_OriginalProposalStillExistsButIsNotTheGateProposal");
        StringAssert.Contains(proofs, "Repair_ApprovalHashBindsPackageContainingRepairedProposal");
        StringAssert.Contains(orchestrator, "proposalEvidenceFileName");

        // Selection is not execution: the repair proof class executes in the full SQL lane.
        var ciScript = File.ReadAllText(RepoFile("Scripts", "ci", "run-full-sql-integration-ci.ps1"));
        StringAssert.Contains(ciScript, "FullyQualifiedName~BoundedRepairApiDrivenTests");
    }

    [TestMethod]
    public void FindingDrivenRevision_IsOffByDefaultBoundedHumanDirectedAndExecutedInCi()
    {
        var orchestrator = File.ReadAllText(RepoFile("IronDev.Infrastructure", "Services", "TicketSkeletonRunService.cs"));

        // REVISE-1 invariants: off unless explicitly configured, hard-clamped,
        // human-directed at the gate only, findings never left unanswered, and
        // only a GREEN revision replaces the canonical gate package.
        StringAssert.Contains(orchestrator, "SkeletonRevision:MaxAttempts");
        StringAssert.Contains(orchestrator, "Default 0: revision is off unless explicitly");
        StringAssert.Contains(orchestrator, "RevisionDisabled");
        StringAssert.Contains(orchestrator, "RevisionBudgetExhausted");
        StringAssert.Contains(orchestrator, "UndispositionedFindingsNotCited");
        StringAssert.Contains(orchestrator, "SkeletonRevisionAttemptStarted");
        StringAssert.Contains(orchestrator, "GREEN revision replaces the canonical gate package");

        // A revision is human-directed, proposal-shaped work, never authority —
        // and the revised package needs its OWN review: the gate is hash-scoped.
        StringAssert.Contains(orchestrator, "A revision is human-directed, proposal-shaped work, never authority");
        StringAssert.Contains(orchestrator, "HasRecordedCriticReviewForPackage");

        // AddressedByRevision cannot be claimed directly by a human.
        var dispositions = File.ReadAllText(RepoFile("IronDev.Infrastructure", "Services", "SkeletonFindingDispositionService.cs"));
        StringAssert.Contains(dispositions, "a human cannot claim a revision that never ran");

        // A client can request a revision; it can never generate one.
        var client = File.ReadAllText(RepoFile("IronDev.Client", "Tickets", "TicketsApiClient.cs"));
        StringAssert.Contains(client, "Revision proposals are orchestrated server-side inside bounded skeleton runs.");

        var proofs = File.ReadAllText(RepoFile("IronDev.IntegrationTests.Api", "Smoke", "FindingDrivenRevisionApiDrivenTests.cs"));
        StringAssert.Contains(proofs, "Revise_CitedFinding_RunsRevisionToTheSameGate_AndTheRevisedPackageNeedsItsOwnReview");
        StringAssert.Contains(proofs, "Revise_OffByDefault_RefusedNamed_AndTheGateIsUnchanged");
        StringAssert.Contains(proofs, "Revise_RefusesToLeaveUncitedFindingsUnanswered");
        StringAssert.Contains(proofs, "Revise_FailedRevisionBuild_LeavesThePreviousGateCanonical_AndSpendsTheBudget");
        StringAssert.Contains(proofs, "Revise_AHumanCannotClaimAddressedByRevisionDirectly");

        // Selection is not execution: the revision proof class executes in the full SQL lane.
        var ciScript = File.ReadAllText(RepoFile("Scripts", "ci", "run-full-sql-integration-ci.ps1"));
        StringAssert.Contains(ciScript, "FullyQualifiedName~FindingDrivenRevisionApiDrivenTests");
    }

    [TestMethod]
    public void FlowUi_SurfacesRepairAttemptsHonestly()
    {
        // REPAIR-1 in the UI: a repaired run says so, the failed original is
        // history (never the gate proposal), and the boundary is stated in place.
        var types = File.ReadAllText(RepoFile("IronDev.TauriShell", "src", "api", "types.ts"));
        StringAssert.Contains(types, "SkeletonRunRepairAttemptTrace");
        StringAssert.Contains(types, "repairAttempts: SkeletonRunRepairAttemptTrace[]");
        StringAssert.Contains(types, "initialProposal?: SkeletonRunProposalTrace | null");

        var panel = File.ReadAllText(RepoFile("IronDev.TauriShell", "src", "flow", "workitem", "RepairAttemptsPanel.tsx"));
        StringAssert.Contains(panel, "not authority — the human gate is unchanged");
        StringAssert.Contains(panel, "failed and is preserved as history");
        StringAssert.Contains(panel, "if (repairAttempts.length === 0)");

        var buildStage = File.ReadAllText(RepoFile("IronDev.TauriShell", "src", "flow", "workitem", "BuildStage.tsx"));
        StringAssert.Contains(buildStage, "Gate proposal (repaired)");
        StringAssert.Contains(buildStage, "RepairAttemptsPanel");

        var reviewStage = File.ReadAllText(RepoFile("IronDev.TauriShell", "src", "flow", "workitem", "ReviewStage.tsx"));
        StringAssert.Contains(reviewStage, "the gate below is unchanged");
    }

    [TestMethod]
    public void DemoSeedContract_ExecutesInCi()
    {
        // Selection is not execution: this class must be executed by exact name
        // in a CI lane, not merely selected by category.
        var ciScript = File.ReadAllText(RepoFile("Scripts", "ci", "run-governance-boundary-ci.ps1"));

        StringAssert.Contains(ciScript, "FullyQualifiedName~DemoSeedScriptContractTests");
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
