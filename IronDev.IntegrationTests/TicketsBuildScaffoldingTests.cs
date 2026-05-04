using System.Threading.Tasks;
using IronDev.Agent.Models;
using IronDev.Agent.ViewModels.Workspaces;
using IronDev.Core.Builder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

/// <summary>
/// Unit tests for the Phase 1 Build Ticket scaffolding in TicketsWorkspaceViewModel.
/// No LLM, no Weaviate, no database, no file writes — purely ViewModel state logic.
/// </summary>
[TestClass]
public sealed class TicketsBuildScaffoldingTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TicketsWorkspaceViewModel CreateVm()
        => new TicketsWorkspaceViewModel(null!, null!);

    /// <summary>
    /// Sets private project-context fields without invoking real DB calls.
    /// </summary>
    private static void SetProjectPath(TicketsWorkspaceViewModel vm, string path = @"C:\repo\test")
    {
        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectPath",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, path);
    }

    private static void SetProjectId(TicketsWorkspaceViewModel vm, int id = 1)
    {
        typeof(TicketsWorkspaceViewModel)
            .GetField("_activeProjectId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, id);
    }

    /// <summary>
    /// Puts the ViewModel into a state equivalent to having a valid ticket selected.
    /// Uses the public SelectedTicket setter (which calls OnSelectedTicketChanged
    /// — guarded here by null-safe service stubs).
    /// </summary>
    private static void SimulateTicketSelected(
        TicketsWorkspaceViewModel vm,
        string title = "Fix Chat header",
        long id = 1)
    {
        // Directly set SelectedTicket via the public property.
        // OnSelectedTicketChanged will fire but the null services won't be called
        // before we've set HasDetail / EditTitle.
        vm.EditTitle = title;
        vm.HasDetail = true;
        vm.IsEditing = true;

        // Use the backing field to avoid the partial-method side-effect of loading from DB.
        typeof(TicketsWorkspaceViewModel)
            .GetField("_selectedTicket",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new TicketItem { Id = id, Title = title });
    }

    // ── CanBuildTicket guard tests ────────────────────────────────────────────

    [TestMethod]
    [Description("CanBuildTicket is false when no ticket is selected.")]
    public void CanBuildTicket_False_WhenNoTicketSelected()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        // No ticket, no title
        Assert.IsFalse(vm.CanBuildTicket);
    }

    [TestMethod]
    [Description("CanBuildTicket is false when project path is empty.")]
    public void CanBuildTicket_False_WhenProjectPathEmpty()
    {
        var vm = CreateVm();
        SetProjectPath(vm, ""); // empty
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        Assert.IsFalse(vm.CanBuildTicket);
    }

    [TestMethod]
    [Description("CanBuildTicket is false when ticket title is empty.")]
    public void CanBuildTicket_False_WhenTitleEmpty()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm, title: "");

        Assert.IsFalse(vm.CanBuildTicket);
    }

    [TestMethod]
    [Description("CanBuildTicket is false while IsBuildingTicket is true.")]
    public void CanBuildTicket_False_WhileBuilding()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);
        vm.IsBuildingTicket = true;

        Assert.IsFalse(vm.CanBuildTicket);
    }

    [TestMethod]
    [Description("CanBuildTicket is true when all conditions are met.")]
    public void CanBuildTicket_True_WhenAllConditionsMet()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        Assert.IsTrue(vm.CanBuildTicket);
    }

    // ── BuildSelectedTicketCommand tests ─────────────────────────────────────

    [TestMethod]
    [Description("BuildSelectedTicketCommand sets HasBuildPreview=true with a non-null preview.")]
    public async Task BuildSelectedTicketCommand_SetsHasBuildPreview()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.HasBuildPreview);
        Assert.IsNotNull(vm.CurrentBuildPreview);
    }

    [TestMethod]
    [Description("BuildSelectedTicketCommand sets the TicketTitle on the preview.")]
    public async Task BuildSelectedTicketCommand_SetsCorrectTicketTitle()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm, title: "Fix Chat History header clipping");

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.AreEqual("Fix Chat History header clipping", vm.CurrentBuildPreview!.TicketTitle);
    }

    [TestMethod]
    [Description("BuildSelectedTicketCommand clears any pre-existing CurrentBuildResult.")]
    public async Task BuildSelectedTicketCommand_ClearsPreviousBuildResult()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        vm.CurrentBuildResult = new TicketBuildResult { TicketId = 1, BuildSucceeded = true };

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.IsNull(vm.CurrentBuildResult);
    }

    [TestMethod]
    [Description("Phase 1 fake proposal summary contains 'scaffold only'.")]
    public async Task BuildSelectedTicketCommand_FakeProposal_ContainsScaffoldText()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        StringAssert.Contains(vm.CurrentBuildPreview!.Proposal.Summary, "scaffold only");
    }

    [TestMethod]
    [Description("Phase 1 fake preview has exactly one FileChangeProposal stub entry.")]
    public async Task BuildSelectedTicketCommand_FakeProposal_HasOneFileEntry()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);

        Assert.AreEqual(1, vm.CurrentBuildPreview!.Proposal.FileChanges.Count);
    }

    // ── CancelBuildPreviewCommand ─────────────────────────────────────────────

    [TestMethod]
    [Description("CancelBuildPreviewCommand clears HasBuildPreview, CurrentBuildPreview, and BuildStatusMessage.")]
    public async Task CancelBuildPreviewCommand_ClearsPreviewState()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.HasBuildPreview);

        vm.CancelBuildPreviewCommand.Execute(null);

        Assert.IsFalse(vm.HasBuildPreview);
        Assert.IsNull(vm.CurrentBuildPreview);
        Assert.AreEqual(string.Empty, vm.BuildStatusMessage);
    }

    // ── ApplyBuildPreviewCommand ──────────────────────────────────────────────

    [TestMethod]
    [Description("ApplyBuildPreviewCommand does not run a build and sets the stub status message.")]
    public async Task ApplyBuildPreviewCommand_DoesNotApply_SetsStubMessage()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);
        vm.ApplyBuildPreviewCommand.Execute(null);

        Assert.IsNull(vm.CurrentBuildResult,
            "No build should have run in Phase 1.");
        StringAssert.Contains(vm.BuildStatusMessage, "not implemented");
    }

    // ── Regression: existing editor state unaffected ─────────────────────────

    [TestMethod]
    [Description("StatusOptions, PriorityOptions, TypeOptions still contain expected values.")]
    public void ExistingDropdownOptions_Unchanged()
    {
        var vm = CreateVm();

        CollectionAssert.Contains(vm.StatusOptions,   "Draft");
        CollectionAssert.Contains(vm.StatusOptions,   "In Progress");
        CollectionAssert.Contains(vm.PriorityOptions, "Medium");
        CollectionAssert.Contains(vm.PriorityOptions, "Critical");
        CollectionAssert.Contains(vm.TypeOptions,     "Task");
        CollectionAssert.Contains(vm.TypeOptions,     "Feature");
    }

    [TestMethod]
    [Description("CancelBuildPreviewCommand also restores CanBuildTicket=true after preview is dismissed.")]
    public async Task CancelBuildPreview_RestoresCanBuildTicket()
    {
        var vm = CreateVm();
        SetProjectPath(vm);
        SetProjectId(vm);
        SimulateTicketSelected(vm);

        await vm.BuildSelectedTicketCommand.ExecuteAsync(null);
        vm.CancelBuildPreviewCommand.Execute(null);

        Assert.IsTrue(vm.CanBuildTicket,
            "CanBuildTicket should be restored after cancelling preview.");
    }
}
