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
/// P2  — Needs Index status → preflight shown (NeedsChoice), draft NOT generated.
/// P3  — PreflightCancel discards pending context, no ticket created.
/// P4  — PreflightContinue generates draft with Limited Context badge.
/// P5  — PreflightIndexProject: enters Indexing state, sets IsDraftIndexing=true, fires OnRequestIndex.
/// P6  — SetIndexStatus("Ready") while Indexing + pending context → auto-generates draft.
/// P7  — Auto-generated draft after indexing has NO Limited Context badge.
/// P8  — After Cancel (from any state), VM is in clean state.
/// P9  — Cancel invokes OnCancelDraft so shell can navigate back.
/// P10 — PreflightContinue with no pending context is a safe no-op.
/// P11 — SetIndexStatus("Ready") with NO pending Indexing state does not change DraftPreflight.
/// P12 — Regression: indexed project still populates all draft fields.
/// P13 — Continue Without Index while indexing is in progress is a no-op (guard).
/// P14 — SetIndexStatus("Needs Index") while in Indexing state → IndexFailed with error message.
/// P15 — Cancel during Indexing state clears IsDraftIndexing and _shouldGenerateDraftAfterIndex.
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
        vm.SetIndexStatus("Ready");

        await vm.BeginDraftFromChatAsync(MakeContext());

        Assert.IsTrue(vm.IsDraftMode,                              "IsDraftMode must be true.");
        Assert.IsNotNull(vm.CurrentDraft,                          "CurrentDraft must be set.");
        Assert.AreEqual(DraftPreflightState.None, vm.DraftPreflight, "DraftPreflight must be None when indexed.");
        Assert.IsFalse(vm.IsDraftGenerating,                       "IsDraftGenerating must be false after completion.");
        Assert.IsFalse(vm.IsDraftIndexing,                         "IsDraftIndexing must be false.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P2: Needs Index → preflight shown, draft NOT generated
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P2: When project is not indexed, BeginDraftFromChatAsync shows preflight gate (NeedsChoice) and does NOT generate a draft.")]
    public async Task BeginDraftFromChat_WhenNotIndexed_ShowsPreflightAndDoesNotGenerate()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");

        await vm.BeginDraftFromChatAsync(MakeContext());

        Assert.AreEqual(DraftPreflightState.NeedsChoice, vm.DraftPreflight,
            "DraftPreflight must be NeedsChoice when project is not indexed.");
        Assert.IsFalse(vm.IsDraftMode,   "IsDraftMode must NOT be true — draft not generated yet.");
        Assert.IsNull(vm.CurrentDraft,   "CurrentDraft must be null — draft not generated yet.");
        Assert.IsFalse(vm.HasDetail,     "HasDetail must be false — editor not shown yet.");
        Assert.IsFalse(vm.IsDraftIndexing, "IsDraftIndexing must be false at NeedsChoice.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P3: PreflightCancel → no draft, no ticket, clean state
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P3: PreflightCancelCommand discards pending context; no draft is generated and no ticket is created.")]
    public async Task PreflightCancel_DiscardsContextAndCreatesNoTicket()
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
    // P5: PreflightIndexProject → enters Indexing state, sets IsDraftIndexing=true, fires OnRequestIndex
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P5: PreflightIndexProjectCommand sets DraftPreflight=Indexing, IsDraftIndexing=true, and fires OnRequestIndex.")]
    public async Task PreflightIndexProject_EntersIndexingStateAndFiresCallback()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");
        bool fired = false;
        vm.OnRequestIndex = () => fired = true;

        await vm.BeginDraftFromChatAsync(MakeContext());
        vm.PreflightIndexProjectCommand.Execute(null);

        Assert.IsTrue(fired, "OnRequestIndex must be invoked when PreflightIndexProjectCommand is executed.");
        Assert.AreEqual(DraftPreflightState.Indexing, vm.DraftPreflight,
            "DraftPreflight must be Indexing after PreflightIndexProject.");
        Assert.IsTrue(vm.IsDraftIndexing, "IsDraftIndexing must be true while indexing is in progress.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.DraftPreflightMessage),
            "DraftPreflightMessage must be non-empty to show progress text.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P6: SetIndexStatus("Ready") while Indexing + pending context → auto-generates draft
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P6: When SetIndexStatus('Ready') arrives while DraftPreflight=Indexing and pending context exists, draft is auto-generated.")]
    public async Task SetIndexStatus_Ready_WhileIndexing_AutoGeneratesDraft()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");
        vm.OnRequestIndex = () => { };   // wire a no-op callback so command doesn't throw

        await vm.BeginDraftFromChatAsync(MakeContext());
        vm.PreflightIndexProjectCommand.Execute(null);

        Assert.AreEqual(DraftPreflightState.Indexing, vm.DraftPreflight, "Sanity: must be Indexing.");

        // Simulate indexing completing
        vm.SetIndexStatus("Ready");

        // Auto-generation fires synchronously in the test (GeneratePendingDraftAsync is awaitable)
        // Give the task a moment to complete (it's fire-and-forget internally)
        await Task.Delay(100);

        Assert.IsTrue(vm.IsProjectIndexed,  "IsProjectIndexed must be true after Ready.");
        Assert.IsTrue(vm.IsDraftMode,       "IsDraftMode must be true — draft was auto-generated.");
        Assert.IsNotNull(vm.CurrentDraft,   "CurrentDraft must be set after auto-generation.");
        Assert.AreEqual(DraftPreflightState.None, vm.DraftPreflight,
            "DraftPreflight must be None after successful auto-generation.");
        Assert.IsFalse(vm.IsDraftIndexing,  "IsDraftIndexing must be false after generation.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P7: Auto-generated draft after indexing has NO Limited Context badge
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P7: Draft auto-generated after successful indexing does NOT show the Limited Context badge.")]
    public async Task AutoGenerate_AfterIndexing_DraftHasNoLimitedContextBadge()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");
        vm.OnRequestIndex = () => { };

        await vm.BeginDraftFromChatAsync(MakeContext());
        vm.PreflightIndexProjectCommand.Execute(null);
        vm.SetIndexStatus("Ready");

        await Task.Delay(100);

        Assert.IsTrue(vm.IsDraftMode,    "IsDraftMode must be true.");
        Assert.IsNotNull(vm.CurrentDraft, "CurrentDraft must be set.");
        Assert.IsFalse(vm.DraftStatusMessage.Contains("not indexed"),
            "DraftStatusMessage must NOT contain 'not indexed' — project is now indexed.");
        Assert.IsFalse(vm.DraftStatusMessage.Contains("Limited context"),
            "DraftStatusMessage must NOT contain 'Limited context' after full indexing.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P8: After Cancel (from any state), VM is in clean state
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P8: After PreflightCancel, DraftPreflight is None, HasDetail is false, DraftMode is false, IsDraftIndexing is false.")]
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
        Assert.IsFalse(vm.IsDraftIndexing);
        Assert.AreEqual(string.Empty, vm.DraftPreflightMessage);
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

        await vm.PreflightContinueCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsDraftMode, "IsDraftMode must remain false.");
        Assert.IsNull(vm.CurrentDraft, "CurrentDraft must remain null.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P11: SetIndexStatus("Ready") with NO pending Indexing does not touch DraftPreflight
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P11: SetIndexStatus('Ready') when DraftPreflight is None does not change state.")]
    public void SetIndexStatus_Ready_WhenPreflightNone_DoesNotChangePreflight()
    {
        var vm = CreateVm();
        Assert.AreEqual(DraftPreflightState.None, vm.DraftPreflight, "Sanity: must be None initially.");

        vm.SetIndexStatus("Ready");

        Assert.AreEqual(DraftPreflightState.None, vm.DraftPreflight,
            "DraftPreflight must remain None when there was no pending preflight.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P12: Regression — indexed project still populates all draft fields
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

        Assert.IsTrue(vm.IsDraftMode,                              "IsDraftMode must be true.");
        Assert.IsNotNull(vm.CurrentDraft,                          "CurrentDraft must be set.");
        Assert.AreEqual(55L, vm.CurrentDraft!.SourceChatSessionId, "SessionId must match.");
        Assert.AreEqual(11L, vm.CurrentDraft.SourceMessageId,      "MessageId must match.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.EditTitle),    "EditTitle must be populated.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.EditSummary),  "EditSummary must be populated.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P13: Continue Without Index while IsDraftIndexing=true is a no-op
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P13: PreflightContinueCommand is a no-op while IsDraftIndexing=true (belt-and-suspenders guard).")]
    public async Task PreflightContinue_WhileIndexing_IsNoOp()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");
        vm.OnRequestIndex = () => { };

        await vm.BeginDraftFromChatAsync(MakeContext());
        vm.PreflightIndexProjectCommand.Execute(null);   // enters Indexing state

        Assert.IsTrue(vm.IsDraftIndexing, "Sanity: must be indexing.");

        // Attempt to continue while indexing — must be ignored
        await vm.PreflightContinueCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsDraftMode,   "IsDraftMode must remain false — continue was blocked.");
        Assert.IsNull(vm.CurrentDraft,   "CurrentDraft must remain null.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P14: SetIndexStatus("Needs Index") while in Indexing state → IndexFailed
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P14: When SetIndexStatus arrives with non-Ready status while DraftPreflight=Indexing, transitions to IndexFailed with error message.")]
    public async Task SetIndexStatus_NotReady_WhileIndexing_TransitionsToIndexFailed()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");
        vm.OnRequestIndex = () => { };

        await vm.BeginDraftFromChatAsync(MakeContext());
        vm.PreflightIndexProjectCommand.Execute(null);

        Assert.AreEqual(DraftPreflightState.Indexing, vm.DraftPreflight, "Sanity: must be Indexing.");

        // Simulate indexing completing but status is still Needs Index (indexing failed)
        vm.SetIndexStatus("Needs Index");

        Assert.AreEqual(DraftPreflightState.IndexFailed, vm.DraftPreflight,
            "DraftPreflight must be IndexFailed when indexing did not result in Ready.");
        Assert.IsFalse(vm.IsDraftIndexing,
            "IsDraftIndexing must be false after failed indexing.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.DraftPreflightMessage),
            "DraftPreflightMessage must describe the failure.");
        StringAssert.Contains(vm.DraftPreflightMessage, "did not complete",
            "DraftPreflightMessage must say indexing did not complete.");
        Assert.IsNull(vm.CurrentDraft,
            "CurrentDraft must remain null — no draft was generated.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // P15: Cancel during Indexing state clears all indexing flags
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("P15: PreflightCancelCommand during Indexing state clears IsDraftIndexing and all pending state.")]
    public async Task PreflightCancel_DuringIndexing_ClearsAllIndexingState()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");
        vm.OnRequestIndex = () => { };

        await vm.BeginDraftFromChatAsync(MakeContext());
        vm.PreflightIndexProjectCommand.Execute(null);

        Assert.IsTrue(vm.IsDraftIndexing, "Sanity: must be indexing.");

        vm.PreflightCancelCommand.Execute(null);

        Assert.AreEqual(DraftPreflightState.None, vm.DraftPreflight,
            "DraftPreflight must be None after Cancel.");
        Assert.IsFalse(vm.IsDraftIndexing,
            "IsDraftIndexing must be false after Cancel.");
        Assert.AreEqual(string.Empty, vm.DraftPreflightMessage,
            "DraftPreflightMessage must be cleared after Cancel.");
        Assert.IsFalse(vm.HasDetail,
            "HasDetail must be false after Cancel.");
    }
}
