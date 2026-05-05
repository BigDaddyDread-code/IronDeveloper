using System.Threading.Tasks;
using IronDev.Agent.Models;
using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// Tests for Task 5: Project index awareness in draft ticket and Build This flows.
///
/// Acceptance criteria:
/// 1. Draft mode shows index warning when project status is Needs Index.
/// 2. Draft mode hides index warning when project status is Ready.
/// 3. Build This context summary includes limited-context warning when not indexed.
/// 4. Code Context tab handles not-indexed state without throwing (UI binding guard).
/// 5. Existing draft/ticket/build tests still pass (verified via full test run).
/// </summary>
[TestClass]
public class IndexAwarenessTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TicketsWorkspaceViewModel CreateVm(
        IDraftTicketService?    draftSvc       = null,
        ITicketBuildOrchestrator? orchestrator = null)
        => new(
            null!,
            null!,
            orchestrator ?? new StubOrchestrator(),
            draftSvc     ?? new StubDraftTicketService(),
            null!);

    private static void SetProjectPath(TicketsWorkspaceViewModel vm, string path = @"C:\repo\test")
        => typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectPath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, path);

    private static ChatTicketContext MakeContext(string title = "Feature X")
        => new()
        {
            SessionId       = 1,
            MessageId       = 1,
            MessageText     = "Implement feature X.",
            ProposedTitle   = title,
            LinkedFilePaths = null,
            LinkedSymbols   = null,
        };

    // ════════════════════════════════════════════════════════════════════════
    // Test 1: IsProjectIndexed defaults to true (no false-alarm on load)
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("IsProjectIndexed defaults to true so no false-alarm warnings appear before project loads.")]
    public void IsProjectIndexed_DefaultsToTrue()
    {
        var vm = CreateVm();
        Assert.IsTrue(vm.IsProjectIndexed,  "Default must be true — no spurious warnings before project loads.");
        Assert.IsFalse(vm.IsContextLimited, "IsContextLimited must be false when indexed.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 2: SetIndexStatus("Needs Index") → IsProjectIndexed=false, warning shown
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("SetIndexStatus('Needs Index') sets IsProjectIndexed=false and IsContextLimited=true — warning is shown.")]
    public void SetIndexStatus_NeedsIndex_SetsIsProjectIndexedFalse()
    {
        var vm = CreateVm();

        vm.SetIndexStatus("Needs Index");

        Assert.IsFalse(vm.IsProjectIndexed,  "IsProjectIndexed must be false after 'Needs Index' status.");
        Assert.IsTrue(vm.IsContextLimited,   "IsContextLimited must be true when not indexed.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 3: SetIndexStatus("Ready") → IsProjectIndexed=true, warning hidden
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("SetIndexStatus('Ready') sets IsProjectIndexed=true and IsContextLimited=false — warning is hidden.")]
    public void SetIndexStatus_Ready_SetsIsProjectIndexedTrue()
    {
        var vm = CreateVm();

        // First mark as not-indexed, then mark Ready
        vm.SetIndexStatus("Needs Index");
        Assert.IsFalse(vm.IsProjectIndexed, "Sanity: must be false before setting Ready.");

        vm.SetIndexStatus("Ready");

        Assert.IsTrue(vm.IsProjectIndexed,   "IsProjectIndexed must be true after 'Ready' status.");
        Assert.IsFalse(vm.IsContextLimited,  "IsContextLimited must be false when project is indexed.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 4: Not indexed → preflight gate shown, draft NOT generated
    // (New behaviour: preflight gate replaces immediate draft generation)
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("When project is not indexed, BeginDraftFromChatAsync shows the preflight gate (DraftPreflight=NeedsChoice) and does NOT generate a draft immediately.")]
    public async Task BeginDraftFromChatAsync_NotIndexed_ShowsPreflightGateInsteadOfGenerating()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");   // mark as not indexed BEFORE starting draft

        await vm.BeginDraftFromChatAsync(MakeContext());

        // New contract: preflight gate is active, draft not yet generated
        Assert.AreEqual(DraftPreflightState.NeedsChoice, vm.DraftPreflight,
            "DraftPreflight must be NeedsChoice when project is not indexed.");
        Assert.IsFalse(vm.IsDraftMode,
            "IsDraftMode must NOT be true — draft is gated behind the preflight choice.");
        Assert.IsNull(vm.CurrentDraft,
            "CurrentDraft must be null — draft has not been generated yet.");

        // After continuing without index, the warning does appear in DraftStatusMessage
        await vm.PreflightContinueCommand.ExecuteAsync(null);
        StringAssert.Contains(vm.DraftStatusMessage, "not indexed",
            "DraftStatusMessage must contain 'not indexed' after user chooses Continue Without Index.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 5: Draft mode — DraftStatusMessage has NO warning when indexed
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("When project IS indexed, BeginDraftFromChatAsync does NOT prepend a context-limited warning.")]
    public async Task BeginDraftFromChatAsync_WhenIndexed_DraftStatusMessageHasNoWarning()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Ready");   // mark as indexed

        await vm.BeginDraftFromChatAsync(MakeContext());

        Assert.IsFalse(vm.DraftStatusMessage.Contains("not indexed"),
            "DraftStatusMessage must NOT contain 'not indexed' when project is indexed.");
        Assert.IsFalse(vm.DraftStatusMessage.Contains("⚠"),
            "DraftStatusMessage must NOT contain warning emoji when project is indexed.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 6: Build This — ContextSummary is prefixed with warning when not indexed
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("When project is not indexed, BuildSelectedTicketAsync prepends limited-context warning to ContextSummary.")]
    public async Task BuildSelectedTicketAsync_NotIndexed_ContextSummaryContainsWarning()
    {
        var orch = new StubOrchestrator();
        var vm   = new TicketsWorkspaceViewModel(
            new StubTicketService(), null!, orch, new StubDraftTicketService(), null!);

        SetProjectPath(vm);
        vm.SetIndexStatus("Needs Index");   // not indexed

        // Wire up a selected ticket and title so CanBuildTicket is true
        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, 1);
        typeof(TicketsWorkspaceViewModel)
            .GetField("_selectedTicket",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new TicketItem { Id = 10, Title = "Build test ticket" });
        vm.EditTitle = "Build test ticket";
        vm.EditId    = 10;

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.HasBuildPreview, "HasBuildPreview must be true after Build This completes.");
        Assert.IsNotNull(vm.CurrentBuildPreview, "CurrentBuildPreview must not be null.");
        StringAssert.Contains(vm.CurrentBuildPreview!.ContextSummary, "not indexed",
            "ContextSummary must contain 'not indexed' warning when project index is missing.");
        StringAssert.Contains(vm.CurrentBuildPreview.ContextSummary, "Limited context",
            "ContextSummary must contain 'Limited context' prefix.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 7: Build This — ContextSummary has NO warning when indexed
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("When project IS indexed, BuildSelectedTicketAsync does NOT prefix ContextSummary with a warning.")]
    public async Task BuildSelectedTicketAsync_WhenIndexed_ContextSummaryHasNoWarning()
    {
        var orch = new StubOrchestrator();
        var vm   = new TicketsWorkspaceViewModel(
            new StubTicketService(), null!, orch, new StubDraftTicketService(), null!);

        SetProjectPath(vm);
        vm.SetIndexStatus("Ready");   // indexed

        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, 1);
        typeof(TicketsWorkspaceViewModel)
            .GetField("_selectedTicket",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new TicketItem { Id = 11, Title = "Indexed ticket" });
        vm.EditTitle = "Indexed ticket";
        vm.EditId    = 11;

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.HasBuildPreview, "HasBuildPreview must be true.");
        Assert.IsFalse(vm.CurrentBuildPreview!.ContextSummary.Contains("Limited context"),
            "ContextSummary must NOT contain 'Limited context' when project is indexed.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 8: OnRequestIndex callback is invoked by RequestIndexCommand
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("RequestIndexCommand invokes OnRequestIndex callback when wired.")]
    public void RequestIndexCommand_InvokesOnRequestIndex()
    {
        var vm              = CreateVm();
        bool callbackFired  = false;
        vm.OnRequestIndex   = () => callbackFired = true;

        vm.RequestIndexCommand.Execute(null);

        Assert.IsTrue(callbackFired, "OnRequestIndex must be invoked when RequestIndexCommand is executed.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 9: RequestIndexCommand is a no-op (no crash) when no callback wired
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("RequestIndexCommand is safe to call when OnRequestIndex callback is not wired (no crash).")]
    public void RequestIndexCommand_NoopWhenCallbackNotWired()
    {
        var vm = CreateVm();
        vm.OnRequestIndex = null;   // not wired

        // Must not throw
        vm.RequestIndexCommand.Execute(null);
        Assert.IsTrue(true, "RequestIndexCommand must not throw when OnRequestIndex is null.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 10: Code Context tab — setting not-indexed does not crash VM
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Switching to Code Context tab while project is not indexed does not throw or crash the VM.")]
    public void CodeContextTab_NotIndexed_DoesNotThrow()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");

        // Simulate switching to the Code Context tab
        vm.ActiveTab = TicketDetailTab.CodeContext;

        // Verify state is consistent
        Assert.IsTrue(vm.IsContextLimited,           "IsContextLimited must be true.");
        Assert.AreEqual(TicketDetailTab.CodeContext, vm.ActiveTab, "ActiveTab must be CodeContext.");
        Assert.IsFalse(vm.IsProjectIndexed,          "IsProjectIndexed must be false.");
        // No exception thrown = pass
    }

    // ════════════════════════════════════════════════════════════════════════
    // Test 11: Index status round-trip — Needs Index → Ready → Needs Index
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("SetIndexStatus round-trip: Needs Index → Ready → Needs Index updates IsProjectIndexed correctly each time.")]
    public void SetIndexStatus_RoundTrip_UpdatesCorrectly()
    {
        var vm = CreateVm();

        vm.SetIndexStatus("Needs Index");
        Assert.IsFalse(vm.IsProjectIndexed, "After 'Needs Index': must be false.");
        Assert.IsTrue(vm.IsContextLimited,  "After 'Needs Index': IsContextLimited must be true.");

        vm.SetIndexStatus("Ready");
        Assert.IsTrue(vm.IsProjectIndexed,  "After 'Ready': must be true.");
        Assert.IsFalse(vm.IsContextLimited, "After 'Ready': IsContextLimited must be false.");

        vm.SetIndexStatus("Needs Index");
        Assert.IsFalse(vm.IsProjectIndexed, "After second 'Needs Index': must be false again.");
        Assert.IsTrue(vm.IsContextLimited,  "After second 'Needs Index': IsContextLimited must be true again.");
    }
}
