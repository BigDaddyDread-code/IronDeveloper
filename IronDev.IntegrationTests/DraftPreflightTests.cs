using System.Threading.Tasks;
using IronDev.Agent.Models;
using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Core.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// Tests for the Draft Preflight gate: the index-check that runs before
/// BeginDraftFromChatAsync calls IDraftTicketService.
///
/// Acceptance criteria verified:
/// P1  — Ready status → draft generated immediately (no preflight shown).
/// P2  — Needs Index status → preflight shown, draft NOT generated.
/// P3  — PreflightCancel discards pending context, no ticket created.
/// P4  — PreflightContinue generates draft with Limited Context badge.
/// P5  — PreflightIndexProject fires OnRequestIndex callback.
/// P6  — SetIndexStatus("Ready") while NeedsChoice → advances to ReadyToGenerate.
/// P7  — PreflightGenerate (after index) generates draft without limited-context badge.
/// P8  — After Cancel, VM is in clean state (no pending context, DraftPreflight=None).
/// P9  — Cancel invokes OnCancelDraft so shell can navigate back.
/// P10 — PreflightContinue with no pending context is a safe no-op.
/// </summary>
[TestClass]
public class DraftPreflightTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TicketsWorkspaceViewModel CreateVm(IDraftTicketService? draftSvc = null)
        => new(null!, null!, new StubOrchestrator(), draftSvc ?? new StubDraftTicketService());

    private static ChatTicketContext MakeContext(string title = "Preflight Feature")
        => new()
        {
            SessionId       = 99,
            MessageId       = 3,
            MessageText     = "Implement preflight feature.",
            ProposedTitle   = title,
            LinkedFilePaths = null,
            LinkedSymbols   = null,
        };

    // ════════════════════════════════════════════════════════════════════════
    // P1: Ready → draft generated immediately, DraftPreflight stays None
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P1: When project is Ready, BeginDraftFromChatAsync generates draft immediately without showing preflight.")]
    public async Task BeginDraftFromChat_WhenReady_GeneratesDraftImmediately()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Ready");   // indexed

        await vm.BeginDraftFromChatAsync(MakeContext());

        Assert.IsTrue(vm.IsDraftMode,                          "IsDraftMode must be true.");
        Assert.IsNotNull(vm.CurrentDraft,                      "CurrentDraft must be set.");
        Assert.AreEqual(DraftPreflightState.None, vm.DraftPreflight, "DraftPreflight must be None when indexed.");
        Assert.IsFalse(vm.IsDraftGenerating,                   "IsDraftGenerating must be false after completion.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P2: Needs Index → preflight shown, draft NOT generated
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P2: When project is not indexed, BeginDraftFromChatAsync shows preflight gate and does NOT generate a draft.")]
    public async Task BeginDraftFromChat_WhenNotIndexed_ShowsPreflightAndDoesNotGenerate()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");   // not indexed

        await vm.BeginDraftFromChatAsync(MakeContext());

        Assert.AreEqual(DraftPreflightState.NeedsChoice, vm.DraftPreflight,
            "DraftPreflight must be NeedsChoice when project is not indexed.");
        Assert.IsFalse(vm.IsDraftMode,   "IsDraftMode must NOT be true — draft not generated yet.");
        Assert.IsNull(vm.CurrentDraft,   "CurrentDraft must be null — draft not generated yet.");
        Assert.IsFalse(vm.HasDetail,     "HasDetail must be false — editor not shown yet.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P3: PreflightCancel → no draft, no ticket, clean state
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P3: PreflightCancelCommand discards pending context; no draft is generated and no ticket is created.")]
    public async Task PreflightCancel_DiscardsContextAndCreateNoTicket()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");
        await vm.BeginDraftFromChatAsync(MakeContext());

        Assert.AreEqual(DraftPreflightState.NeedsChoice, vm.DraftPreflight, "Sanity: preflight must be active.");

        vm.PreflightCancelCommand.Execute(null);

        Assert.AreEqual(DraftPreflightState.None, vm.DraftPreflight, "DraftPreflight must be None after Cancel.");
        Assert.IsFalse(vm.IsDraftMode,   "IsDraftMode must be false.");
        Assert.IsNull(vm.CurrentDraft,   "CurrentDraft must be null — nothing was generated.");
        Assert.IsFalse(vm.HasDetail,     "HasDetail must be false — editor must not be shown.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P4: PreflightContinue → generates draft with Limited Context badge
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P4: PreflightContinueCommand generates draft with the Limited Context warning in DraftStatusMessage.")]
    public async Task PreflightContinue_GeneratesDraftWithLimitedContextBadge()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");
        await vm.BeginDraftFromChatAsync(MakeContext());

        await vm.PreflightContinueCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsDraftMode,           "IsDraftMode must be true after Continue.");
        Assert.IsNotNull(vm.CurrentDraft,        "CurrentDraft must be set after Continue.");
        Assert.IsTrue(vm.HasDetail,              "HasDetail must be true — editor is now shown.");
        Assert.AreEqual(DraftPreflightState.None, vm.DraftPreflight, "DraftPreflight must be None after generation starts.");
        StringAssert.Contains(vm.DraftStatusMessage, "not indexed",
            "DraftStatusMessage must contain 'not indexed' Limited Context badge when continuing without index.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P5: PreflightIndexProject → fires OnRequestIndex callback
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P5: PreflightIndexProjectCommand fires the OnRequestIndex callback so the shell can trigger indexing.")]
    public async Task PreflightIndexProject_FiresOnRequestIndexCallback()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");
        bool fired = false;
        vm.OnRequestIndex = () => fired = true;

        await vm.BeginDraftFromChatAsync(MakeContext());
        vm.PreflightIndexProjectCommand.Execute(null);

        Assert.IsTrue(fired, "OnRequestIndex must be invoked when PreflightIndexProjectCommand is executed.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P6: SetIndexStatus("Ready") while NeedsChoice → ReadyToGenerate
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P6: When indexing completes (SetIndexStatus Ready) while DraftPreflight=NeedsChoice, state advances to ReadyToGenerate.")]
    public async Task SetIndexStatus_Ready_WhileNeedsChoice_AdvancesToReadyToGenerate()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");
        await vm.BeginDraftFromChatAsync(MakeContext());

        Assert.AreEqual(DraftPreflightState.NeedsChoice, vm.DraftPreflight, "Sanity: must be NeedsChoice.");

        // Simulate indexing completing
        vm.SetIndexStatus("Ready");

        Assert.AreEqual(DraftPreflightState.ReadyToGenerate, vm.DraftPreflight,
            "DraftPreflight must advance to ReadyToGenerate when indexing completes.");
        Assert.IsTrue(vm.IsProjectIndexed, "IsProjectIndexed must be true after Ready.");
        Assert.IsNull(vm.CurrentDraft,     "CurrentDraft must still be null — draft not auto-generated.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P7: PreflightGenerate (after indexing) → generates draft WITHOUT limited-context badge
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P7: PreflightGenerateCommand after indexing completes generates the draft without a Limited Context badge.")]
    public async Task PreflightGenerate_AfterIndexing_GeneratesDraftWithoutLimitedContextBadge()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");
        await vm.BeginDraftFromChatAsync(MakeContext());

        // Simulate indexing completing
        vm.SetIndexStatus("Ready");
        Assert.AreEqual(DraftPreflightState.ReadyToGenerate, vm.DraftPreflight, "Sanity: must be ReadyToGenerate.");

        await vm.PreflightGenerateCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsDraftMode,    "IsDraftMode must be true after PreflightGenerate.");
        Assert.IsNotNull(vm.CurrentDraft, "CurrentDraft must be set.");
        Assert.IsFalse(vm.DraftStatusMessage.Contains("not indexed"),
            "DraftStatusMessage must NOT contain 'not indexed' when project is indexed.");
        Assert.AreEqual(DraftPreflightState.None, vm.DraftPreflight,
            "DraftPreflight must be None after generation.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P8: After Cancel, VM is in clean state
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P8: After PreflightCancel, DraftPreflight is None, HasDetail is false, DraftMode is false.")]
    public async Task PreflightCancel_LeavesVmInCleanState()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");
        await vm.BeginDraftFromChatAsync(MakeContext());

        vm.PreflightCancelCommand.Execute(null);

        Assert.AreEqual(DraftPreflightState.None, vm.DraftPreflight);
        Assert.IsFalse(vm.IsDraftMode);
        Assert.IsFalse(vm.HasDetail);
        Assert.IsNull(vm.CurrentDraft);
        Assert.AreEqual(string.Empty, vm.DraftStatusMessage);
    }

    // ════════════════════════════════════════════════════════════════════════
    // P9: Cancel fires OnCancelDraft callback
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P9: PreflightCancelCommand invokes OnCancelDraft so the shell can navigate back to Chat.")]
    public async Task PreflightCancel_InvokesOnCancelDraftCallback()
    {
        var vm = CreateVm();
        bool callbackFired = false;
        vm.OnCancelDraft = () => callbackFired = true;

        vm.SetIndexStatus("Needs Index");
        await vm.BeginDraftFromChatAsync(MakeContext());

        vm.PreflightCancelCommand.Execute(null);

        Assert.IsTrue(callbackFired, "OnCancelDraft callback must be invoked by PreflightCancel.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P10: PreflightContinue with null pending context is a safe no-op
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P10: PreflightContinueCommand when no pending context is a no-op and does not throw.")]
    public async Task PreflightContinue_WithNoPendingContext_IsNoOp()
    {
        var vm = CreateVm();
        // Do NOT call BeginDraftFromChatAsync — no pending context

        // Must not throw
        await vm.PreflightContinueCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsDraftMode, "IsDraftMode must remain false.");
        Assert.IsNull(vm.CurrentDraft, "CurrentDraft must remain null.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P11: SetIndexStatus Ready with NO pending context does NOT change DraftPreflight
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P11: SetIndexStatus('Ready') when DraftPreflight is already None does not change state.")]
    public void SetIndexStatus_Ready_WhenPreflightNone_DoesNotChangePreflight()
    {
        var vm = CreateVm();
        Assert.AreEqual(DraftPreflightState.None, vm.DraftPreflight, "Sanity: must be None initially.");

        vm.SetIndexStatus("Ready");

        Assert.AreEqual(DraftPreflightState.None, vm.DraftPreflight,
            "DraftPreflight must remain None when there was no pending preflight.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P12: Existing BeginDraftFromChat (indexed) still populates all fields
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P12: Regression — indexed project still populates all draft fields and sets source context.")]
    public async Task BeginDraftFromChat_WhenIndexed_PopulatesAllFieldsAndSourceContext()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Ready");
        var ctx = new ChatTicketContext
        {
            SessionId       = 55,
            MessageId       = 11,
            MessageText     = "Add export to PDF feature.",
            ProposedTitle   = "PDF Export",
            LinkedFilePaths = @"src\Exporter.cs",
            LinkedSymbols   = "ExportService",
        };

        await vm.BeginDraftFromChatAsync(ctx);

        Assert.IsTrue(vm.IsDraftMode,                            "IsDraftMode must be true.");
        Assert.IsNotNull(vm.CurrentDraft,                        "CurrentDraft must be set.");
        Assert.AreEqual(55L, vm.CurrentDraft!.SourceChatSessionId, "SessionId must match.");
        Assert.AreEqual(11L, vm.CurrentDraft.SourceMessageId,     "MessageId must match.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.EditTitle),   "EditTitle must be populated.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.EditSummary), "EditSummary must be populated.");
    }
}
