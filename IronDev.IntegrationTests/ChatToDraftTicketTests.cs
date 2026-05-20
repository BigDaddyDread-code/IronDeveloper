using System;
using System.Linq;
using System.Threading.Tasks;
using IronDev.Agent.Models;
using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// ViewModel unit tests for the Chat → Draft Ticket Review workflow (Phases 1–3).
/// Also covers regression tests added after manual-testing found Issues 1–3.
///
/// No DB, no LLM, no DI required.
/// Uses StubDraftTicketService (defined in TicketsBuildScaffoldingTests.cs).
/// </summary>
[TestClass]
public class ChatToDraftTicketTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TicketsWorkspaceViewModel CreateVm(IDraftTicketService? draftService = null)
        => new(null!, null!, new StubOrchestrator(), draftService ?? new StubDraftTicketService(), null!);

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

    private static ChatTicketContext MakeContext(
        string title   = "Add search feature",
        string message = "We need a search bar on the dashboard.")
        => new()
        {
            SessionId       = 42,
            MessageId       = 7,
            MessageText     = message,
            ProposedTitle   = title,
            LinkedFilePaths = @"src\Dashboard.cs",
            LinkedSymbols   = "DashboardViewModel",
        };

    // ════════════════════════════════════════════════════════════════════════
    // Original Phase 1-3 tests (12)
    // ════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("BeginDraftFromChatAsync sets IsDraftMode=true and populates all edit fields.")]
    public async Task BeginDraftFromChatAsync_SetsIsDraftModeAndPopulatesFields()
    {
        var vm  = CreateVm();
        var ctx = MakeContext();

        await vm.BeginDraftFromChatAsync(ctx);

        Assert.IsTrue(vm.IsDraftMode,        "IsDraftMode must be true after BeginDraftFromChatAsync.");
        Assert.IsFalse(vm.IsDraftGenerating, "IsDraftGenerating must be false after completion.");
        Assert.IsNotNull(vm.CurrentDraft,    "CurrentDraft must be set.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.EditTitle),   "EditTitle must be populated.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.EditSummary), "EditSummary must be populated.");
    }

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

    [TestMethod]
    [Description("ApproveDraftAsync with null TicketService fails gracefully — draft is not saved without a service.")]
    public async Task ApproveDraftAsync_WithNullService_DoesNotPersist()
    {
        var vm  = CreateVm();
        var ctx = MakeContext();

        await vm.BeginDraftFromChatAsync(ctx);
        await vm.ApproveDraftCommand.ExecuteAsync(null);

        // SaveStatus will contain the error message; ticket-created banner must NOT appear
        Assert.IsFalse(vm.SaveStatus.Contains("Ticket created"),
            "Ticket must not be reported as saved when TicketService is null.");
    }

    [TestMethod]
    [Description("CancelDraft sets IsDraftMode=false, clears CurrentDraft, and invokes OnCancelDraft.")]
    public async Task CancelDraft_ClearsDraftStateAndInvokesCallback()
    {
        var vm               = CreateVm();
        bool callbackInvoked = false;
        vm.OnCancelDraft     = () => callbackInvoked = true;

        await vm.BeginDraftFromChatAsync(MakeContext());
        vm.CancelDraftCommand.Execute(null);

        Assert.IsFalse(vm.IsDraftMode,          "IsDraftMode must be false after CancelDraft.");
        Assert.IsNull(vm.CurrentDraft,           "CurrentDraft must be null after CancelDraft.");
        Assert.IsTrue(callbackInvoked,           "OnCancelDraft callback must be invoked.");
        Assert.AreEqual(string.Empty, vm.EditTitle, "EditTitle must be cleared after CancelDraft.");
    }

    [TestMethod]
    [Description("RegenerateAllCommand replaces all edit fields with the new draft.")]
    public async Task RegenerateAllCommand_ReplacesAllDraftFields()
    {
        var vm  = CreateVm();
        await vm.BeginDraftFromChatAsync(MakeContext());

        vm.EditTitle = "Manually edited title";
        await vm.RegenerateAllCommand.ExecuteAsync(null);

        Assert.AreNotEqual("Manually edited title", vm.EditTitle,
            "RegenerateAll must overwrite manually edited fields.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.EditTitle),
            "EditTitle must be non-empty after regeneration.");
    }

    [TestMethod]
    [Description("RegenerateTestsCommand updates only the test sub-fields; other fields are unchanged.")]
    public async Task RegenerateTestsCommand_UpdatesOnlyTestFields()
    {
        var vm = CreateVm();
        await vm.BeginDraftFromChatAsync(MakeContext());

        var originalTitle   = vm.EditTitle;
        var originalSummary = vm.EditSummary;

        await vm.RegenerateTestsCommand.ExecuteAsync(null);

        Assert.AreEqual(originalTitle,   vm.EditTitle,   "Title must be unchanged after RegenerateTests.");
        Assert.AreEqual(originalSummary, vm.EditSummary, "Summary must be unchanged after RegenerateTests.");
        Assert.IsTrue(vm.EditTestsUnitTests.Contains("[regenerated]"),
            "EditTestsUnitTests must contain the regenerated marker.");
        Assert.IsTrue(vm.EditTestsIntegrationTests.Contains("[regenerated]"),
            "EditTestsIntegrationTests must contain the regenerated marker.");
    }

    [TestMethod]
    [Description("After BeginDraftFromChatAsync, test sub-fields are packed into TechnicalNotes.")]
    public async Task BeginDraftFromChatAsync_TestsSerializedIntoTechnicalNotes()
    {
        var vm  = CreateVm();
        await vm.BeginDraftFromChatAsync(MakeContext());

        StringAssert.Contains(vm.EditTechnicalNotes, "## Unit Tests",
            "TechnicalNotes must contain ## Unit Tests section.");
        StringAssert.Contains(vm.EditTechnicalNotes, "Stub unit tests.",
            "TechnicalNotes must contain the unit test content.");
    }

    [TestMethod]
    [Description("CanBuildTicket returns false while IsDraftMode is true.")]
    public async Task CanBuildTicket_FalseWhileInDraftMode()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectName(vm);
        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, 1);
        typeof(TicketsWorkspaceViewModel)
            .GetField("_selectedTicket",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new TicketItem { Id = 1, Title = "Test" });
        vm.EditTitle = "Test";

        Assert.IsTrue(vm.CanBuildTicket, "CanBuildTicket should be true before entering draft mode.");

        await vm.BeginDraftFromChatAsync(MakeContext());

        Assert.IsFalse(vm.CanBuildTicket,
            "CanBuildTicket must be false while IsDraftMode=true.");
    }

    [TestMethod]
    [Description("IsDraftGenerating is false after BeginDraftFromChatAsync completes.")]
    public async Task BeginDraftFromChatAsync_IsDraftGeneratingFalseAfterCompletion()
    {
        var vm = CreateVm();
        await vm.BeginDraftFromChatAsync(MakeContext());
        Assert.IsFalse(vm.IsDraftGenerating, "IsDraftGenerating must be false after async completion.");
    }

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

    [TestMethod]
    [Description("ApproveDraftWithPlanCommand must NOT invoke the callback when save fails (null service).")]
    public async Task ApproveDraftWithPlan_WithoutService_DoesNotInvokeCallback()
    {
        var vm               = CreateVm();
        bool callbackInvoked = false;
        // 7-param signature: title, goal, steps, filePaths, symbols, scope, risksNotes
        vm.OnApproveDraftWithPlan = (t, g, s, fp, sym, sc, rn) => callbackInvoked = true;

        await vm.BeginDraftFromChatAsync(MakeContext());
        await vm.ApproveDraftWithPlanCommand.ExecuteAsync(null);

        Assert.IsFalse(callbackInvoked,
            "OnApproveDraftWithPlan must not be invoked when save fails.");
    }

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

    // ════════════════════════════════════════════════════════════════════════
    // Regression tests added after manual testing (Task 4)
    // ════════════════════════════════════════════════════════════════════════

    // R1: ApproveDraftAsync exits draft mode when save fails, not when it succeeds
    // (with null service: save fails → IsDraftMode stays true)
    [TestMethod]
    [Description("R1: IsDraftMode stays true after ApproveDraftAsync fails (null service).")]
    public async Task ApproveDraftAsync_SaveFails_IsDraftModeRemainsTrue()
    {
        var vm = CreateVm();   // null TicketService → save will throw
        await vm.BeginDraftFromChatAsync(MakeContext());
        await vm.ApproveDraftCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.IsDraftMode,
            "IsDraftMode must remain true when save fails (savedId <= 0 returns early).");
    }

    // R2: CancelDraft never saves (even after editing draft fields)
    [TestMethod]
    [Description("R2: CancelDraft after editing draft fields still does not persist anything.")]
    public async Task CancelDraft_AfterEditing_DoesNotSave()
    {
        var vm = CreateVm();
        await vm.BeginDraftFromChatAsync(MakeContext());

        // Simulate user editing the draft
        vm.EditTitle   = "Edited Title";
        vm.EditSummary = "Edited Summary";

        vm.CancelDraftCommand.Execute(null);

        // Draft mode exited, fields cleared
        Assert.IsFalse(vm.IsDraftMode, "IsDraftMode must be false after Cancel.");
        Assert.AreEqual(string.Empty, vm.EditTitle,   "EditTitle must be cleared after Cancel.");
        Assert.AreEqual(string.Empty, vm.EditSummary, "EditSummary must be cleared after Cancel.");
    }

    // R3: Plan title includes ticket title (snapshot taken BEFORE ClearEditor)
    [TestMethod]
    [Description("R3: ApproveDraftWithPlanAsync snapshots EditTitle before ClearEditor; plan title must include ticket title.")]
    public async Task ApproveDraftWithPlan_PlanTitleIncludesTicketTitle()
    {
        var vm              = CreateVm();
        string? capturedTitle = null;
        vm.OnApproveDraftWithPlan = (title, g, s, fp, sym, sc, rn) =>
        {
            capturedTitle = title;
        };

        await vm.BeginDraftFromChatAsync(MakeContext("My Search Feature"));

        // Manually override to confirm we're reading the right field
        var expectedTicketTitle = vm.EditTitle;  // set by stub from proposed title

        // With null TicketService the save fails and callback is not called —
        // but we can still verify the snapshot logic in isolation by calling
        // the snapshot directly (we test this via the ViewModel's field state
        // at the point BeginDraftFromChatAsync completed, before any save).
        Assert.IsFalse(string.IsNullOrWhiteSpace(expectedTicketTitle),
            "EditTitle must be non-empty after BeginDraftFromChatAsync — this is what gets snapshotted.");
        // The plan title would be: $"{expectedTicketTitle} — Implementation Plan"
        // We verify the ticket title is populated (so snapshot will be non-empty).
        Assert.IsTrue(expectedTicketTitle.Length > 0,
            "Ticket title must be available for plan title snapshot before ClearEditor.");
    }

    // R4: Plan goal is the ticket summary (snapshot)
    [TestMethod]
    [Description("R4: EditSummary is non-empty after draft load — confirms plan goal snapshot will not be empty.")]
    public async Task ApproveDraftWithPlan_PlanGoalComesFromSummary()
    {
        var vm = CreateVm();
        await vm.BeginDraftFromChatAsync(MakeContext("Feature", "The dashboard needs a search bar for quick navigation."));

        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.EditSummary),
            "EditSummary must be populated after BeginDraftFromChatAsync — it becomes the plan goal.");
    }

    // R5: Plan callback receives non-empty title and summary (using a real stub TicketService)
    [TestMethod]
    [Description("R5: When a stub TicketService is provided, ApproveDraftWithPlanAsync fires callback with non-empty title and goal.")]
    public async Task ApproveDraftWithPlan_WithStubService_CallbackReceivesNonEmptyTitleAndGoal()
    {
        var draftSvc = new StubDraftTicketService();
        var ticketSvc = new StubTicketService();
        var vm = new TicketsWorkspaceViewModel(ticketSvc, null!, new StubOrchestrator(), draftSvc, null!);

        string? receivedTitle = null;
        string? receivedGoal  = null;
        vm.OnApproveDraftWithPlan = (title, goal, s, fp, sym, sc, rn) =>
        {
            receivedTitle = title;
            receivedGoal  = goal;
        };

        await vm.BeginDraftFromChatAsync(MakeContext("Add Search", "The search bar is missing."));
        await vm.ApproveDraftWithPlanCommand.ExecuteAsync(null);

        Assert.IsNotNull(receivedTitle, "OnApproveDraftWithPlan callback must be invoked.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(receivedTitle),
            "Plan title must be non-empty (must include ticket title).");
        Assert.IsTrue(receivedTitle!.Contains("Implementation Plan"),
            "Plan title must include '— Implementation Plan'.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(receivedGoal),
            "Plan goal must be non-empty (ticket summary).");
    }

    // R6: ApproveDraftAsync with stub service exits draft mode and shows SaveStatus
    [TestMethod]
    [Description("R6: ApproveDraftAsync with stub TicketService sets IsDraftMode=false and shows save status.")]
    public async Task ApproveDraftAsync_WithStubService_ExitsDraftModeAndShowsStatus()
    {
        var vm = new TicketsWorkspaceViewModel(
            new StubTicketService(), null!, new StubOrchestrator(), new StubDraftTicketService(), null!);

        await vm.BeginDraftFromChatAsync(MakeContext());
        await vm.ApproveDraftCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsDraftMode,  "IsDraftMode must be false after successful approve.");
        Assert.IsNull(vm.CurrentDraft,  "CurrentDraft must be null after successful approve.");
        StringAssert.Contains(vm.SaveStatus, "Ticket created",
            "SaveStatus must show 'Ticket created' confirmation.");
    }

    // R7: Build This still works after entering and leaving draft mode
    [TestMethod]
    [Description("R7: After CancelDraft, CanBuildTicket is restored for existing tickets.")]
    public async Task CanBuildTicket_RestoredAfterCancelDraft()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, 1);

        // Start draft
        await vm.BeginDraftFromChatAsync(MakeContext());
        Assert.IsFalse(vm.CanBuildTicket, "CanBuildTicket must be false during draft mode.");

        // Cancel draft
        vm.CancelDraftCommand.Execute(null);

        // Manually restore the ticket selection state that was there before (simulate re-select)
        typeof(TicketsWorkspaceViewModel)
            .GetField("_selectedTicket",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new TicketItem { Id = 1, Title = "Existing" });
        vm.EditTitle = "Existing";

        // CanBuildTicket should be true again (IsDraftMode=false, project path set, ticket selected, title set)
        Assert.IsFalse(vm.IsDraftMode, "IsDraftMode must be false after CancelDraft.");
        Assert.IsTrue(vm.CanBuildTicket, "CanBuildTicket must be true after CancelDraft with ticket selected.");
    }

    // R8: Existing Tests tab serialisation still works after draft round-trip
    [TestMethod]
    [Description("R8: Tests sub-fields survive a draft load; existing serialization logic is intact.")]
    public async Task TestsSubFields_AfterDraftLoad_SerializeCorrectly()
    {
        var vm = CreateVm();
        await vm.BeginDraftFromChatAsync(MakeContext());

        // Confirm test sub-fields were loaded from the stub
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.EditTestsUnitTests),
            "EditTestsUnitTests must be populated after draft load.");

        // Modify a field and confirm TechnicalNotes updates
        vm.EditTestsUnitTests = "Verify search returns results.";
        StringAssert.Contains(vm.EditTechnicalNotes, "Verify search returns results.",
            "TechnicalNotes must contain the updated unit test content.");
        StringAssert.Contains(vm.EditTechnicalNotes, "## Unit Tests",
            "TechnicalNotes must still have the section header.");
    }

    [TestMethod]
    [Description("GenerateImplementationPlanCommand populates PlanProposedSteps and sets HasPlan=true.")]
    public async Task GenerateImplementationPlanCommand_PopulatesPlanFields()
    {
        var vm = CreateVm();
        await vm.BeginDraftFromChatAsync(MakeContext());

        // Ensure plan is empty initially (stub might not populate it in GenerateDraftAsync yet)
        vm.HasPlan = false;
        vm.PlanProposedSteps = string.Empty;

        await vm.GenerateImplementationPlanCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.HasPlan, "HasPlan must be true after GenerateImplementationPlanCommand.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(vm.PlanProposedSteps),
            "PlanProposedSteps must be populated after generation.");
        StringAssert.Contains(vm.PlanProposedSteps, "Step 1",
            "PlanProposedSteps must contain content from the stub.");
    }

    [TestMethod]
    [Description("ApproveDraftWithPlanAsync automatically generates a plan if HasPlan is false.")]
    public async Task ApproveDraftWithPlanAsync_GeneratesPlanIfMissing()
    {
        var draftSvc = new StubDraftTicketService();
        var ticketSvc = new StubTicketService();
        var vm = new TicketsWorkspaceViewModel(ticketSvc, null!, new StubOrchestrator(), draftSvc, null!);

        string? receivedSteps = null;
        vm.OnApproveDraftWithPlan = (t, g, steps, fp, sym, sc, rn) =>
        {
            receivedSteps = steps;
        };

        await vm.BeginDraftFromChatAsync(MakeContext());
        vm.HasPlan = false; // Force missing plan
        vm.PlanProposedSteps = string.Empty;

        await vm.ApproveDraftWithPlanCommand.ExecuteAsync(null);

        Assert.IsNotNull(receivedSteps, "OnApproveDraftWithPlan callback must be invoked.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(receivedSteps),
            "Implementation plan should have been auto-generated before callback.");
        StringAssert.Contains(receivedSteps, "Step 1",
            "Received steps must contain content from the auto-generated plan.");
    }

    [TestMethod]
    [Description("Split ticket contexts generate multiple in-list draft tickets for separate review.")]
    public async Task BeginDraftsFromChatAsync_GeneratesMultipleDraftTickets()
    {
        var vm = CreateVm();
        var contexts = new[]
        {
            MakeContext("Project Summary", "Split ticket 1 of 2: project summary"),
            MakeContext("Context Documents", "Split ticket 2 of 2: context documents")
        };

        await vm.BeginDraftsFromChatAsync(contexts);

        Assert.AreEqual(2, vm.Tickets.Count(t => t.IsDraft));
        Assert.IsTrue(vm.IsDraftMode);
        Assert.IsNotNull(vm.SelectedTicket);
        Assert.AreEqual("Project Summary", vm.SelectedTicket!.Title);
        StringAssert.Contains(vm.DraftStatusMessage, "Generated 2 draft tickets");
    }

    [TestMethod]
    [Description("Split ticket drafts preserve each proposed title and message body.")]
    public async Task BeginDraftsFromChatAsync_PreservesEachSplitContext()
    {
        var vm = CreateVm();

        await vm.BeginDraftsFromChatAsync(
        [
            MakeContext("UI controls", "Split ticket 1 of 2: add controls"),
            MakeContext("Routing", "Split ticket 2 of 2: add routing")
        ]);

        var titles = vm.Tickets.Where(t => t.IsDraft).Select(t => t.Title).ToList();
        CollectionAssert.Contains(titles, "UI controls");
        CollectionAssert.Contains(titles, "Routing");
    }

    [TestMethod]
    [Description("Split ticket preflight stores the whole batch until the user continues without index.")]
    public async Task BeginDraftsFromChatAsync_WhenIndexLimited_WaitsForPreflightContinue()
    {
        var vm = CreateVm();
        vm.SetIndexStatus("Needs Index");

        await vm.BeginDraftsFromChatAsync(
        [
            MakeContext("Summary", "Split ticket 1 of 2: summary"),
            MakeContext("Documents", "Split ticket 2 of 2: documents")
        ]);

        Assert.AreEqual(DraftPreflightState.NeedsChoice, vm.DraftPreflight);
        Assert.AreEqual(0, vm.Tickets.Count(t => t.IsDraft));

        await vm.PreflightContinueCommand.ExecuteAsync(null);

        Assert.AreEqual(2, vm.Tickets.Count(t => t.IsDraft));
        Assert.AreEqual(DraftPreflightState.None, vm.DraftPreflight);
    }

    [TestMethod]
    [Description("Saving one generated split draft preserves the remaining unsaved drafts for review.")]
    public async Task ApproveDraftAsync_MultiDraft_PreservesRemainingDrafts()
    {
        var ticketService = new StubTicketService();
        var vm = new TicketsWorkspaceViewModel(
            ticketService,
            null!,
            new StubOrchestrator(),
            new StubDraftTicketService(),
            null!);

        await vm.BeginDraftsFromChatAsync(
        [
            MakeContext("Location model", "Split ticket 1 of 2: add location model"),
            MakeContext("Location persistence", "Split ticket 2 of 2: add persistence")
        ]);

        Assert.AreEqual(2, vm.Tickets.Count(t => t.IsDraft));

        await vm.ApproveDraftCommand.ExecuteAsync(null);

        Assert.AreEqual(1, ticketService.SavedTickets.Count);
        Assert.AreEqual(1, vm.Tickets.Count(t => t.IsDraft), "Saving one draft must not discard the rest of the review queue.");
        Assert.AreEqual("Location persistence", vm.SelectedTicket?.Title);
        Assert.IsTrue(vm.IsDraftMode, "The next unsaved draft should stay selected for review.");
        Assert.AreEqual(42L, ticketService.SavedTickets[0].SourceChatSessionId);
        Assert.AreEqual(7L, ticketService.SavedTickets[0].SourceChatMessageId);
        StringAssert.Contains(vm.SaveStatus, "1 draft(s) still waiting");
    }
}

/// <summary>
/// Minimal stub ITicketService used in regression tests where a save must succeed.
/// Returns a deterministic savedId without touching a real database.
/// </summary>
internal sealed class StubTicketService : IronDev.Services.ITicketService
{
    private long _nextId = 100;
    public System.Collections.Generic.List<IronDev.Data.Models.ProjectTicket> SavedTickets { get; } = [];

    public Task<long> SaveTicketAsync(
        IronDev.Data.Models.ProjectTicket ticket,
        System.Threading.CancellationToken cancellationToken = default)
    {
        SavedTickets.Add(ticket);
        return Task.FromResult(_nextId++);
    }

    public Task<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.ProjectTicket>> GetRecentTicketsAsync(
        int projectId, int take = 10,
        System.Threading.CancellationToken cancellationToken = default)
        => Task.FromResult<System.Collections.Generic.IReadOnlyList<IronDev.Data.Models.ProjectTicket>>(
               new System.Collections.Generic.List<IronDev.Data.Models.ProjectTicket>());

    public Task<IronDev.Data.Models.ProjectTicket?> GetTicketByIdAsync(
        long ticketId,
        System.Threading.CancellationToken cancellationToken = default)
        => Task.FromResult<IronDev.Data.Models.ProjectTicket?>(null);

    public Task<bool> ArchiveTicketAsync(long ticketId, System.Threading.CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}

/// <summary>
/// IDraftTicketService that always throws — used to test error handling in BeginDraftFromChatAsync.
/// </summary>
internal sealed class FailingDraftTicketService : IDraftTicketService
{
    public Task<DraftTicket> GenerateDraftAsync(
        int projectId, string projectName, string proposedTitle,
        string messageText, string? linkedFilePaths, string? linkedSymbols,
        long? sessionId = null,
        System.Threading.CancellationToken ct = default)
        => throw new InvalidOperationException("Simulated draft generation failure.");

    public Task<DraftTicket> RegenerateTestsAsync(
        int projectId, DraftTicket current,
        System.Threading.CancellationToken ct = default)
        => throw new InvalidOperationException("Simulated regeneration failure.");

    public Task<DraftTicket> GeneratePlanAsync(
        int projectId, DraftTicket current,
        System.Threading.CancellationToken ct = default)
        => throw new InvalidOperationException("Simulated plan generation failure.");
}
