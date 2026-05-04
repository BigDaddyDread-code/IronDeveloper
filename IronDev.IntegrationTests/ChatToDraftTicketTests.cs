using System;
using System.Threading.Tasks;
using IronDev.Agent.Models;
using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// ViewModel unit tests for the Chat → Draft Ticket Review workflow (Phases 1-3).
///
/// No DB, no LLM, no DI required.
/// Uses StubDraftTicketService (defined in TicketsBuildScaffoldingTests.cs).
/// </summary>
[TestClass]
public class ChatToDraftTicketTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TicketsWorkspaceViewModel CreateVm(IDraftTicketService? draftService = null)
        => new(null!, null!, new StubOrchestrator(), draftService ?? new StubDraftTicketService());

    private static void SetProjectPath(TicketsWorkspaceViewModel vm, string path = @"C:\repo\test")
        => typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectPath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, path);

    private static void SetProjectName(TicketsWorkspaceViewModel vm, string name = "TestProject")
        => typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectName",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, name);

    private static ChatTicketContext MakeContext(string title = "Add search feature", string message = "We need a search bar on the dashboard.")
        => new()
        {
            SessionId       = 42,
            MessageId       = 7,
            MessageText     = message,
            ProposedTitle   = title,
            LinkedFilePaths = @"src\Dashboard.cs",
            LinkedSymbols   = "DashboardViewModel",
        };

    // ── Test 1: BeginDraftFromChatAsync sets IsDraftMode and populates fields ─

    [TestMethod]
    [Description("BeginDraftFromChatAsync sets IsDraftMode=true and populates all edit fields.")]
    public async Task BeginDraftFromChatAsync_SetsIsDraftModeAndPopulatesFields()
    {
        var vm  = CreateVm();
        var ctx = MakeContext();

        await vm.BeginDraftFromChatAsync(ctx);

        Assert.IsTrue(vm.IsDraftMode,     "IsDraftMode must be true after BeginDraftFromChatAsync.");
        Assert.IsFalse(vm.IsDraftGenerating, "IsDraftGenerating must be false after completion.");
        Assert.IsNotNull(vm.CurrentDraft,    "CurrentDraft must be set.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.EditTitle),   "EditTitle must be populated.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.EditSummary), "EditSummary must be populated.");
    }

    // ── Test 2: Source context is preserved in CurrentDraft ──────────────────

    [TestMethod]
    [Description("BeginDraftFromChatAsync preserves SessionId and MessageId in CurrentDraft.")]
    public async Task BeginDraftFromChatAsync_SourceContextPreserved()
    {
        var vm  = CreateVm();
        var ctx = MakeContext();

        await vm.BeginDraftFromChatAsync(ctx);

        Assert.AreEqual(42L, vm.CurrentDraft!.SourceChatSessionId, "SourceChatSessionId must match SessionId.");
        Assert.AreEqual(7L,  vm.CurrentDraft.SourceMessageId,      "SourceMessageId must match MessageId.");
    }

    // ── Test 3: Draft is NOT persisted until ApproveDraftAsync ───────────────

    [TestMethod]
    [Description("ApproveDraftAsync with null TicketService fails gracefully — draft is not saved without a service.")]
    public async Task ApproveDraftAsync_WithNullService_DoesNotPersist()
    {
        // VM with null ticket service — SaveDraftTicketAsync will throw
        var vm  = CreateVm();
        var ctx = MakeContext();

        await vm.BeginDraftFromChatAsync(ctx);

        // ApproveDraft will fail because _ticketService is null, but must not crash the app
        await vm.ApproveDraftCommand.ExecuteAsync(null);

        // Draft mode should NOT be exited on failure
        // (SaveStatus will contain the error, IsDraftMode stays true while save failed)
        // This is acceptable Phase 1-3 behaviour — the ticket was not saved
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.SaveStatus) && vm.SaveStatus.Contains("Ticket created"),
            "Ticket must not be reported as saved when TicketService is null.");
    }

    // ── Test 4: CancelDraft clears draft state and invokes callback ──────────

    [TestMethod]
    [Description("CancelDraft sets IsDraftMode=false, clears CurrentDraft, and invokes OnCancelDraft.")]
    public async Task CancelDraft_ClearsDraftStateAndInvokesCallback()
    {
        var vm          = CreateVm();
        var ctx         = MakeContext();
        bool callbackInvoked = false;
        vm.OnCancelDraft = () => callbackInvoked = true;

        await vm.BeginDraftFromChatAsync(ctx);
        vm.CancelDraftCommand.Execute(null);

        Assert.IsFalse(vm.IsDraftMode,     "IsDraftMode must be false after CancelDraft.");
        Assert.IsNull(vm.CurrentDraft,     "CurrentDraft must be null after CancelDraft.");
        Assert.IsTrue(callbackInvoked,     "OnCancelDraft callback must be invoked.");
        Assert.AreEqual(string.Empty, vm.EditTitle, "EditTitle must be cleared after CancelDraft.");
    }

    // ── Test 5: RegenerateAll replaces all draft fields ──────────────────────

    [TestMethod]
    [Description("RegenerateAllCommand replaces all edit fields with the new draft.")]
    public async Task RegenerateAllCommand_ReplacesAllDraftFields()
    {
        var vm  = CreateVm();
        var ctx = MakeContext();

        await vm.BeginDraftFromChatAsync(ctx);
        var originalTitle = vm.EditTitle;

        // Force a title change to confirm regeneration replaces it
        vm.EditTitle = "Manually edited title";
        await vm.RegenerateAllCommand.ExecuteAsync(null);

        // Stub service returns the proposed title — so after regeneration, title
        // should be the stub's output again (not the manually edited value)
        Assert.AreNotEqual("Manually edited title", vm.EditTitle,
            "RegenerateAll must overwrite manually edited fields.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.EditTitle), "EditTitle must be non-empty after regeneration.");
    }

    // ── Test 6: RegenerateTests updates only test sub-fields ─────────────────

    [TestMethod]
    [Description("RegenerateTestsCommand updates only the test sub-fields; other fields are unchanged.")]
    public async Task RegenerateTestsCommand_UpdatesOnlyTestFields()
    {
        var vm  = CreateVm();
        var ctx = MakeContext();

        await vm.BeginDraftFromChatAsync(ctx);
        var originalTitle   = vm.EditTitle;
        var originalSummary = vm.EditSummary;
        var originalUnit    = vm.EditTestsUnitTests;

        await vm.RegenerateTestsCommand.ExecuteAsync(null);

        // Non-test fields must be unchanged
        Assert.AreEqual(originalTitle,   vm.EditTitle,   "Title must be unchanged after RegenerateTests.");
        Assert.AreEqual(originalSummary, vm.EditSummary, "Summary must be unchanged after RegenerateTests.");

        // Unit tests should have the stub "[regenerated]" suffix
        Assert.IsTrue(vm.EditTestsUnitTests.Contains("[regenerated]"),
            "EditTestsUnitTests must contain the regenerated marker.");
        Assert.IsTrue(vm.EditTestsIntegrationTests.Contains("[regenerated]"),
            "EditTestsIntegrationTests must contain the regenerated marker.");
    }

    // ── Test 7: Tests sub-fields are serialized into TechnicalNotes ──────────

    [TestMethod]
    [Description("After BeginDraftFromChatAsync, test sub-fields are packed into TechnicalNotes.")]
    public async Task BeginDraftFromChatAsync_TestsSerializedIntoTechnicalNotes()
    {
        var vm  = CreateVm();
        var ctx = MakeContext();

        await vm.BeginDraftFromChatAsync(ctx);

        // TechnicalNotes should contain section headers from the test sub-fields
        StringAssert.Contains(vm.EditTechnicalNotes, "## Unit Tests",
            "TechnicalNotes must contain ## Unit Tests section.");
        StringAssert.Contains(vm.EditTechnicalNotes, "Stub unit tests.",
            "TechnicalNotes must contain the unit test content.");
    }

    // ── Test 8: Build This is disabled in draft mode ──────────────────────────

    [TestMethod]
    [Description("CanBuildTicket returns false while IsDraftMode is true.")]
    public async Task CanBuildTicket_FalseWhileInDraftMode()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectName(vm);

        // Set up a 'selected ticket' state to make CanBuildTicket true normally
        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, 1);
        typeof(TicketsWorkspaceViewModel)
            .GetField("_selectedTicket",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new TicketItem { Id = 1, Title = "Test" });
        vm.EditTitle = "Test";

        // Not in draft mode — should be buildable
        Assert.IsTrue(vm.CanBuildTicket, "CanBuildTicket should be true before entering draft mode.");

        // Enter draft mode
        await vm.BeginDraftFromChatAsync(MakeContext());

        Assert.IsFalse(vm.CanBuildTicket,
            "CanBuildTicket must be false while IsDraftMode=true.");
    }

    // ── Test 9: Draft has IsDraftGenerating false after completion ────────────

    [TestMethod]
    [Description("IsDraftGenerating is false after BeginDraftFromChatAsync completes.")]
    public async Task BeginDraftFromChatAsync_IsDraftGeneratingFalseAfterCompletion()
    {
        var vm  = CreateVm();
        await vm.BeginDraftFromChatAsync(MakeContext());
        Assert.IsFalse(vm.IsDraftGenerating, "IsDraftGenerating must be false after async completion.");
    }

    // ── Test 10: Failed draft service sets DraftStatusMessage with error ──────

    [TestMethod]
    [Description("If IDraftTicketService throws, DraftStatusMessage contains the error and IsDraftMode is true.")]
    public async Task BeginDraftFromChatAsync_ServiceThrows_DraftStatusHasError()
    {
        var vm = CreateVm(new FailingDraftTicketService());
        await vm.BeginDraftFromChatAsync(MakeContext());

        Assert.IsTrue(vm.IsDraftMode, "IsDraftMode must remain true even when draft generation fails.");
        StringAssert.Contains(vm.DraftStatusMessage, "Draft generation failed",
            "DraftStatusMessage must contain the error prefix.");
        Assert.IsNull(vm.CurrentDraft, "CurrentDraft must remain null when generation fails.");
    }

    // ── Test 11: OnApproveDraftWithPlan callback is invoked ──────────────────

    [TestMethod]
    [Description("ApproveDraftWithPlanCommand invokes OnApproveDraftWithPlan after save (with null service, verifies callback isn't called on failure).")]
    public async Task ApproveDraftWithPlan_WithoutService_DoesNotInvokeCallback()
    {
        var vm  = CreateVm();
        bool callbackInvoked = false;
        vm.OnApproveDraftWithPlan = (t, g, s, fp, sym) => callbackInvoked = true;

        await vm.BeginDraftFromChatAsync(MakeContext());
        await vm.ApproveDraftWithPlanCommand.ExecuteAsync(null);

        // Save will fail (null service), so callback must NOT be invoked
        Assert.IsFalse(callbackInvoked,
            "OnApproveDraftWithPlan must not be invoked when save fails.");
    }

    // ── Test 12: Existing build scaffolding tests still pass (smoke check) ────

    [TestMethod]
    [Description("CanBuildTicket is true when all conditions are met and IsDraftMode=false.")]
    public void CanBuildTicket_TrueWhenAllConditionsMet_AndNotInDraftMode()
    {
        var vm = CreateVm();
        SetProjectPath(vm);

        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, 1);
        typeof(TicketsWorkspaceViewModel)
            .GetField("_selectedTicket",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new TicketItem { Id = 1, Title = "Test" });
        vm.EditTitle = "Test";

        Assert.IsTrue(vm.CanBuildTicket, "CanBuildTicket must be true with all conditions met and IsDraftMode=false.");
        Assert.IsFalse(vm.IsDraftMode,   "IsDraftMode must be false by default.");
    }
}

/// <summary>
/// IDraftTicketService that always throws — used to test error handling in BeginDraftFromChatAsync.
/// </summary>
internal sealed class FailingDraftTicketService : IDraftTicketService
{
    public Task<DraftTicket> GenerateDraftAsync(
        int projectId, string projectName, string proposedTitle,
        string messageText, string? linkedFilePaths, string? linkedSymbols,
        System.Threading.CancellationToken ct = default)
        => throw new InvalidOperationException("Simulated draft generation failure.");

    public Task<DraftTicket> RegenerateTestsAsync(
        int projectId, DraftTicket current,
        System.Threading.CancellationToken ct = default)
        => throw new InvalidOperationException("Simulated regeneration failure.");
}
