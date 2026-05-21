using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Models;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDeveloperControls.Primitives;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class TicketsWorkspaceViewModel : ObservableObject, IWorkspaceDirtyState
{
    private readonly global::IronDev.Services.ITicketService         _ticketService;
    private readonly global::IronDev.Services.IProjectMemoryService  _memoryService;
    private readonly ITicketBuildOrchestrator                        _orchestrator;
    private readonly IDraftTicketService                             _draftService;
    private readonly ICodebaseTicketGeneratorService                 _generatorService;
    private readonly ILlmTraceService?                               _llmTraceService;
    private readonly IBuilderReadinessService?                       _readinessService;

    private int    _activeProjectId;
    private int?   _activeTenantId;
    private string _activeProjectPath = string.Empty;
    private string _activeProjectName = string.Empty;

    // ── List panel ──────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<TicketItem> _tickets = [];
    [ObservableProperty] private TicketItem? _selectedTicket;

    // ── Detail/editor panel ─────────────────────────────────────────────────
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isNewTicket;
    [ObservableProperty] private bool _hasDetail;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _saveStatus = string.Empty;

    // Editable fields
    [ObservableProperty] private long   _editId;
    [ObservableProperty] private string _editTitle = string.Empty;
    [ObservableProperty] private string _editStatus = "Draft";
    [ObservableProperty] private string _editPriority = "Medium";
    [ObservableProperty] private string _editTicketType = "Task";
    [ObservableProperty] private string _editSummary = string.Empty;
    [ObservableProperty] private string _editBackground = string.Empty;
    [ObservableProperty] private string _editProblem = string.Empty;
    [ObservableProperty] private string _editAcceptanceCriteria = string.Empty;
    [ObservableProperty] private string _editTechnicalNotes = string.Empty;
    [ObservableProperty] private string _editLinkedFilePaths = string.Empty;
    [ObservableProperty] private string _editLinkedSymbols = string.Empty;

    // ── Tests sub-fields (parsed from / serialized to TechnicalNotes) ─────────
    // DB: No schema change. All fields are packed into TechnicalNotes using a
    // section-header convention: "## Unit Tests\n..."
    private string _editTestsUnitTests        = string.Empty;
    private string _editTestsIntegrationTests = string.Empty;
    private string _editTestsManualTests      = string.Empty;
    private string _editTestsRegressionTests  = string.Empty;
    private string _editTestsBuildValidation  = string.Empty;
    private bool _suppressTicketDirtyTracking;
    private bool _hasUnsavedTicketChanges;

    public string EditTestsUnitTests
    {
        get => _editTestsUnitTests;
        set { if (SetProperty(ref _editTestsUnitTests, value)) { MarkTicketDirty(); SyncTestsToTechnicalNotes(); } }
    }
    public string EditTestsIntegrationTests
    {
        get => _editTestsIntegrationTests;
        set { if (SetProperty(ref _editTestsIntegrationTests, value)) { MarkTicketDirty(); SyncTestsToTechnicalNotes(); } }
    }
    public string EditTestsManualTests
    {
        get => _editTestsManualTests;
        set { if (SetProperty(ref _editTestsManualTests, value)) { MarkTicketDirty(); SyncTestsToTechnicalNotes(); } }
    }
    public string EditTestsRegressionTests
    {
        get => _editTestsRegressionTests;
        set { if (SetProperty(ref _editTestsRegressionTests, value)) { MarkTicketDirty(); SyncTestsToTechnicalNotes(); } }
    }
    public string EditTestsBuildValidation
    {
        get => _editTestsBuildValidation;
        set { if (SetProperty(ref _editTestsBuildValidation, value)) { MarkTicketDirty(); SyncTestsToTechnicalNotes(); } }
    }

    /// <summary>Full formatted test plan — used by AI builder context.</summary>
    public string FullTestPlan => EditTechnicalNotes;

    // CommunityToolkit.Mvvm partial callback — called whenever [ObservableProperty]
    // EditTechnicalNotes is set (including direct assignment from load methods).
    partial void OnEditTechnicalNotesChanged(string value)
    {
        MarkTicketDirty();
        if (!_suppressTicketDirtyTracking)
            SyncTechnicalNotesToTests();
    }

    partial void OnEditTitleChanged(string value)
    {
        MarkTicketDirty();
        SaveTicketCommand.NotifyCanExecuteChanged();
    }
    partial void OnEditStatusChanged(string value) => MarkTicketDirty();
    partial void OnEditPriorityChanged(string value) => MarkTicketDirty();
    partial void OnEditTicketTypeChanged(string value) => MarkTicketDirty();
    partial void OnEditSummaryChanged(string value) => MarkTicketDirty();
    partial void OnEditBackgroundChanged(string value) => MarkTicketDirty();
    partial void OnEditProblemChanged(string value) => MarkTicketDirty();
    partial void OnEditAcceptanceCriteriaChanged(string value) => MarkTicketDirty();
    partial void OnEditLinkedFilePathsChanged(string value) => MarkTicketDirty();
    partial void OnEditLinkedSymbolsChanged(string value) => MarkTicketDirty();

    partial void OnHasDetailChanged(bool value) => SaveTicketCommand.NotifyCanExecuteChanged();

    partial void OnIsSavingChanged(bool value) => SaveTicketCommand.NotifyCanExecuteChanged();

    // ── Implementation Plan ──
    [ObservableProperty] private TicketDetailTab _activeTab = TicketDetailTab.Overview;
    [ObservableProperty] private bool _hasPlan;
    [ObservableProperty] private long _planId;
    [ObservableProperty] private string _planTitle = string.Empty;
    [ObservableProperty] private string _planGoal = string.Empty;
    [ObservableProperty] private string _planScope = string.Empty;
    [ObservableProperty] private string _planProposedSteps = string.Empty;
    [ObservableProperty] private string _planAffectedContext = string.Empty;
    [ObservableProperty] private string _planRisksNotes = string.Empty;
    [ObservableProperty] private string _planStatus = "Draft";
    [ObservableProperty] private string _planLinkedFilePaths = string.Empty;
    [ObservableProperty] private string _planLinkedSymbols = string.Empty;
    [ObservableProperty] private string _planSaveStatus = string.Empty;
    [ObservableProperty] private bool _isSavingPlan;

    // ── Build Ticket state ────────────────────────────────────────────────────
    [ObservableProperty] 
    [NotifyCanExecuteChangedFor(nameof(BuildSelectedTicketProposalCommand))]
    private bool               _isBuildingTicket;
    [ObservableProperty] private bool               _hasBuildPreview;
    [ObservableProperty] private TicketBuildPreview? _currentBuildPreview;
    [ObservableProperty] private TicketBuildResult?  _currentBuildResult;
    [ObservableProperty] private string              _buildStatusMessage = string.Empty;
    [ObservableProperty] private BuildReadinessResult? _buildReadiness;
    [ObservableProperty] private bool _showBuildReadiness;
    [ObservableProperty] private string _buildReadinessTitle = "Build readiness";
    [ObservableProperty] private string _buildReadinessMessage = string.Empty;
    [ObservableProperty] private string _buildReadinessDetails = string.Empty;
    [ObservableProperty] private string _buildReadinessBadgeText = "READY";
    [ObservableProperty] private BadgeStatus _buildReadinessBadgeStatus = BadgeStatus.Info;
    [ObservableProperty] private string _buildReadinessActionText = string.Empty;

    /// <summary>True when the Build This button should be enabled.</summary>
    public bool CanBuildTicket =>
        !IsBuildingTicket
        && !IsDraftMode
        && SelectedTicket != null
        && !string.IsNullOrWhiteSpace(EditTitle)
        && !string.IsNullOrWhiteSpace(_activeProjectPath)
        && (_readinessService == null || (BuildReadiness?.IsReady ?? false));

    /// <summary>True when the Archive button should be enabled.</summary>
    public bool CanArchiveTicket => SelectedTicket != null && !IsDraftMode && !IsBuildingTicket && !IsSaving;
    public bool CanSaveTicket => HasDetail && !IsSaving && !IsDraftMode && HasDirtyEditState && !string.IsNullOrWhiteSpace(EditTitle);
    public bool HasDirtyEditState => _hasUnsavedTicketChanges && HasDetail && !IsSaving && !IsDraftMode;
    public string DirtyEditMessage => "This ticket has unsaved edit text. Leave Tickets and discard those changes?";

    // ── Draft Ticket state ────────────────────────────────────────────────────
    [ObservableProperty] 
    [NotifyCanExecuteChangedFor(nameof(BuildSelectedTicketProposalCommand))]
    private bool       _isDraftMode;
    [ObservableProperty] private bool       _isDraftGenerating;
    [ObservableProperty] private string     _draftStatusMessage = string.Empty;
    [ObservableProperty] private DraftTicket? _currentDraft;

    // ── Codebase Ticket Generation state ──────────────────────────────────────
    [ObservableProperty] private bool _isGeneratingCodebaseTickets;
    public CodexTicketReviewViewModel CodexReview { get; }


    // ── Draft Preflight state ─────────────────────────────────────────────────
    /// <summary>
    /// Tracks the preflight gate shown when the user clicks Ticket from Chat
    /// but the project is not yet indexed. None = no preflight active.
    /// </summary>
    [ObservableProperty] private DraftPreflightState _draftPreflight = DraftPreflightState.None;

    /// <summary>True while indexing is running after "Index Project First" was clicked.</summary>
    [ObservableProperty] private bool _isDraftIndexing;

    /// <summary>Status/progress text shown inside the preflight panel.</summary>
    [ObservableProperty] private string _draftPreflightMessage = string.Empty;

    /// <summary>Pending context held while the user decides on the preflight choice.</summary>
    private ChatTicketContext? _pendingChatContext;
    private IReadOnlyList<ChatTicketContext>? _pendingChatContexts;

    /// <summary>
    /// True when "Index Project First" was clicked and we should auto-generate
    /// the draft once SetIndexStatus("Ready") arrives.
    /// </summary>
    private bool _shouldGenerateDraftAfterIndex;

    // ── Project index state ───────────────────────────────────────────────────
    /// <summary>
    /// True when the project code index is Ready. False means Needs Index / unknown.
    /// Defaults to true so no warnings flash before project loads.
    /// Updated by ShellViewModel whenever ActiveStatus changes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBuildTicket))]
    private bool _isProjectIndexed = true;

    /// <summary>Derived: true when index is absent and context quality is limited.</summary>
    public bool IsContextLimited => !IsProjectIndexed;

    // ── Shell navigation callbacks ────────────────────────────────────────────
    /// <summary>Called when the user cancels a draft — shell navigates back to Chat.</summary>
    public Action? OnCancelDraft { get; set; }
    /// <summary>Called after a draft is approved with a plan — shell navigates to Plans.
    /// Params: title, goal, steps, linkedFilePaths, linkedSymbols, scope, risksNotes</summary>
    public Action<string, string, string?, string?, string?, string?, string?>? OnApproveDraftWithPlan { get; set; }
    /// <summary>Called when user clicks "Index Project" from within the Tickets workspace.
    /// Shell wires this to IndexNowCommand on ProjectOverviewViewModel.</summary>
    public Action? OnRequestIndex { get; set; }
    /// <summary>Called when user clicks "Build This (Proposal)" — shell navigates to Builder workbench.</summary>
    public Action<long>? OnRequestProposal { get; set; }

    // Dropdown options
    public ObservableCollection<string> StatusOptions   { get; } = ["Draft", "Todo", "In Progress", "Done", "Resolved"];
    public ObservableCollection<string> PriorityOptions { get; } = ["Low", "Medium", "High", "Critical"];
    public ObservableCollection<string> TypeOptions     { get; } = ["Task", "Bug", "Feature", "Spike", "Chore"];

    public TicketsWorkspaceViewModel(
        global::IronDev.Services.ITicketService        ticketService,
        global::IronDev.Services.IProjectMemoryService memoryService,
        ITicketBuildOrchestrator                       orchestrator,
        IDraftTicketService                            draftService,
        ICodebaseTicketGeneratorService                generatorService,
        IBuilderReadinessService?                      readinessService = null,
        ILlmTraceService?                              llmTraceService = null)
    {
        _ticketService    = ticketService;
        _memoryService    = memoryService;
        _orchestrator     = orchestrator;
        _draftService     = draftService;
        _generatorService = generatorService;
        _readinessService = readinessService;
        _llmTraceService  = llmTraceService;
        CodexReview = new CodexTicketReviewViewModel(
            GenerateCodexTicketsForReviewAsync,
            ImportCodexReviewTicketsAsync);
    }

    // ── Load ────────────────────────────────────────────────────────────────

    internal async Task LoadAsync(IronDev.Data.Models.Project project)
    {
        _activeProjectId   = project.Id;
        _activeTenantId    = project.TenantId;
        _activeProjectPath = project.LocalPath ?? string.Empty;
        _activeProjectName = project.Name;
        await RefreshListAsync();
    }

    private async Task RefreshListAsync()
    {
        Tickets.Clear();
        ClearEditor();

        var tickets = await _ticketService.GetRecentTicketsAsync(_activeProjectId, take: 50);
        foreach (var t in tickets)
        {
            Tickets.Add(MapToItem(t));
        }
    }

    // ── Selection ───────────────────────────────────────────────────────────

    partial void OnSelectedTicketChanged(TicketItem? value)
    {
        if (value == null)
        {
            ClearEditor();
            return;
        }

        // Handle in-list drafts from Codebase Generator
        if (value.IsDraft)
        {
            IsDraftMode        = true;
            IsDraftGenerating  = false;
            if (string.IsNullOrWhiteSpace(DraftStatusMessage) || DraftStatusMessage.StartsWith("Generating", StringComparison.OrdinalIgnoreCase))
                DraftStatusMessage = "Draft ticket";
            HasDetail          = true;
            IsEditing          = true;
            IsNewTicket        = true;
            ActiveTab          = TicketDetailTab.Overview;

            LoadTicketIntoEditor(value);
            return;
        }

        // Guard: if this ticket is already loaded into the editor, skip the reload.
        if (HasDetail && EditId == value.Id && !IsDraftMode)
            return;

        IsDraftMode = false;
        // Clear stale build state when switching tickets
        ClearBuildState();
        _ = LoadTicketIntoEditorAsync(value);
        BuildSelectedTicketProposalCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanArchiveTicket));
    }

    private void LoadTicketIntoEditor(TicketItem item)
    {
        _suppressTicketDirtyTracking = true;
        try
        {
            IsNewTicket = item.Id == 0;
            IsEditing = true;
            HasDetail = true;
            ActiveTab = TicketDetailTab.Overview;

            EditId                   = item.Id;
            EditTitle                = item.Title;
            EditStatus               = item.Status;
            EditPriority             = item.Priority;
            EditTicketType           = item.TicketType;
            EditSummary              = item.Summary ?? string.Empty;
            EditBackground           = item.Background ?? string.Empty;
            EditProblem              = item.Problem ?? string.Empty;
            EditAcceptanceCriteria   = item.AcceptanceCriteria ?? string.Empty;
            EditTechnicalNotes       = item.TechnicalNotes ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(item.UnitTests) || !string.IsNullOrWhiteSpace(item.IntegrationTests))
            {
                _editTestsUnitTests        = item.UnitTests ?? string.Empty;
                _editTestsIntegrationTests = item.IntegrationTests ?? string.Empty;
                _editTestsManualTests      = item.ManualTests ?? string.Empty;
                _editTestsRegressionTests  = item.RegressionTests ?? string.Empty;
                _editTestsBuildValidation  = item.BuildValidation ?? string.Empty;
                SyncTestsToTechnicalNotes();
            }
            else
            {
                SyncTechnicalNotesToTests();
            }
            EditLinkedFilePaths      = item.LinkedFilePaths ?? string.Empty;
            EditLinkedSymbols        = item.LinkedSymbols ?? string.Empty;

            SaveStatus = string.Empty;
        }
        finally
        {
            _suppressTicketDirtyTracking = false;
            ClearTicketDirtyState();
        }
    }

    private async Task LoadTicketIntoEditorAsync(TicketItem item)
    {
        _suppressTicketDirtyTracking = true;
        try
        {
            IsNewTicket = false;
            IsEditing = true;
            HasDetail = true;
            ActiveTab = TicketDetailTab.Overview;

            EditId                   = item.Id;
            EditTitle                = item.Title;
            EditStatus               = item.Status;
            EditPriority             = item.Priority;
            EditTicketType           = item.TicketType;
            EditSummary              = item.Summary ?? string.Empty;
            EditBackground           = item.Background ?? string.Empty;
            EditProblem              = item.Problem ?? string.Empty;
            EditAcceptanceCriteria   = item.AcceptanceCriteria ?? string.Empty;
            EditTechnicalNotes       = item.TechnicalNotes ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(item.UnitTests)) { _editTestsUnitTests = item.UnitTests; SyncTestsToTechnicalNotes(); } else { SyncTechnicalNotesToTests(); }
            EditLinkedFilePaths      = item.LinkedFilePaths ?? string.Empty;
            EditLinkedSymbols        = item.LinkedSymbols ?? string.Empty;

            SaveStatus = string.Empty;
        }
        finally
        {
            _suppressTicketDirtyTracking = false;
            ClearTicketDirtyState();
        }

        // Load plan for this ticket
        await RefreshPlanAsync(item.Id);
        await RefreshBuildReadinessAsync();
    }

    private async Task RefreshPlanAsync(long ticketId)
    {
        var plan = await _memoryService.GetPlanByTicketIdAsync(ticketId);
        if (plan != null)
        {
            HasPlan = true;
            PlanId = plan.Id;
            PlanTitle = plan.Title;
            PlanGoal = plan.Goal;
            PlanScope = plan.Scope ?? string.Empty;
            PlanProposedSteps = plan.ProposedSteps ?? string.Empty;
            PlanAffectedContext = plan.AffectedContext ?? string.Empty;
            PlanRisksNotes = plan.RisksNotes ?? string.Empty;
            PlanStatus = plan.Status;
            PlanLinkedFilePaths = plan.LinkedFilePaths ?? string.Empty;
            PlanLinkedSymbols = plan.LinkedSymbols ?? string.Empty;
        }
        else
        {
            HasPlan = false;
            PlanId = 0;
            PlanTitle = string.Empty;
            PlanGoal = string.Empty;
            PlanScope = string.Empty;
            PlanProposedSteps = string.Empty;
            PlanAffectedContext = string.Empty;
            PlanRisksNotes = string.Empty;
            PlanStatus = "Draft";
            PlanLinkedFilePaths = string.Empty;
            PlanLinkedSymbols = string.Empty;
        }
        PlanSaveStatus = string.Empty;
    }

    // ── New Ticket ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void NewTicket()
    {
        SelectedTicket = null;
        IsNewTicket = true;
        IsEditing = true;
        HasDetail = true;

        EditId                 = 0;
        EditTitle              = string.Empty;
        EditStatus             = "Draft";
        EditPriority           = "Medium";
        EditTicketType         = "Task";
        EditSummary            = string.Empty;
        EditBackground         = string.Empty;
        EditProblem            = string.Empty;
        EditAcceptanceCriteria = string.Empty;
        EditTechnicalNotes     = string.Empty;
        ClearTestSubFields();
        EditLinkedFilePaths    = string.Empty;
        EditLinkedSymbols      = string.Empty;

        SaveStatus = string.Empty;
        ClearTicketDirtyState();
    }

    // ── Save ────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSaveTicket))]
    private async Task SaveTicketAsync()
    {
        if (string.IsNullOrWhiteSpace(EditTitle))
        {
            SaveStatus = "Title is required.";
            return;
        }

        IsSaving = true;
        SaveStatus = "Saving...";

        try
        {
            var ticket = new ProjectTicket
            {
                Id                     = EditId,
                ProjectId              = _activeProjectId,
                SessionId              = Guid.NewGuid(),
                Title                  = EditTitle.Trim(),
                TicketType             = EditTicketType,
                Priority               = EditPriority,
                Summary                = string.IsNullOrWhiteSpace(EditSummary) ? null : EditSummary.Trim(),
                Background             = string.IsNullOrWhiteSpace(EditBackground) ? null : EditBackground.Trim(),
                Problem                = string.IsNullOrWhiteSpace(EditProblem) ? null : EditProblem.Trim(),
                AcceptanceCriteria     = string.IsNullOrWhiteSpace(EditAcceptanceCriteria) ? null : EditAcceptanceCriteria.Trim(),
                TechnicalNotes         = string.IsNullOrWhiteSpace(EditTechnicalNotes) ? null : EditTechnicalNotes.Trim(),
                Status                 = EditStatus,
                Content                = EditSummary?.Trim() ?? string.Empty,
                LinkedFilePaths        = string.IsNullOrWhiteSpace(EditLinkedFilePaths) ? null : EditLinkedFilePaths.Trim(),
                LinkedCodeIndexEntryIds = null,
                LinkedSymbols          = string.IsNullOrWhiteSpace(EditLinkedSymbols) ? null : EditLinkedSymbols.Trim(),
                UnitTests              = string.IsNullOrWhiteSpace(EditTestsUnitTests) ? null : EditTestsUnitTests.Trim(),
                IntegrationTests       = string.IsNullOrWhiteSpace(EditTestsIntegrationTests) ? null : EditTestsIntegrationTests.Trim(),
                ManualTests            = string.IsNullOrWhiteSpace(EditTestsManualTests) ? null : EditTestsManualTests.Trim(),
                RegressionTests        = string.IsNullOrWhiteSpace(EditTestsRegressionTests) ? null : EditTestsRegressionTests.Trim(),
                BuildValidation        = string.IsNullOrWhiteSpace(EditTestsBuildValidation) ? null : EditTestsBuildValidation.Trim(),
                ContextSummary         = CurrentBuildPreview?.ContextSummary,
                IsGenerated            = SelectedTicket?.IsGenerated ?? false,
                GenerationNote         = SelectedTicket?.GenerationNote
            };

            var savedId = await _ticketService.SaveTicketAsync(ticket);
            EditId = savedId;
            IsNewTicket = false;
            ClearTicketDirtyState();

            SaveStatus = "Saved ✓";
            await RefreshListAsync();

            // Re-select the saved ticket
            SelectedTicket = Tickets.FirstOrDefault(t => t.Id == savedId);
        }
        catch (Exception ex)
        {
            SaveStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
            BuildSelectedTicketProposalCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanArchiveTicket));
        }
    }

    [RelayCommand(CanExecute = nameof(CanArchiveTicket))]
    private async Task ArchiveSelectedTicketAsync()
    {
        if (SelectedTicket == null) return;

        var result = System.Windows.MessageBox.Show(
            $"Are you sure you want to archive ticket '{SelectedTicket.Title}'?\n\nIt will be hidden from the active list but remains in the database.",
            "Archive Ticket",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        IsSaving = true;
        SaveStatus = "Archiving...";

        try
        {
            var success = await _ticketService.ArchiveTicketAsync(SelectedTicket.Id);
            if (success)
            {
                SaveStatus = "Ticket archived ✓";
                
                await RefreshListAsync();

                // Selection logic: RefreshListAsync cleared editor. 
                // We don't need to select the archived one. 
                // Select the first one in the list if available.
                SelectedTicket = Tickets.FirstOrDefault();
            }
            else
            {
                SaveStatus = "Archive failed.";
            }
        }
        catch (Exception ex)
        {
            SaveStatus = $"Archive error: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
            OnPropertyChanged(nameof(CanArchiveTicket));
        }
    }

    [RelayCommand]
    private void CopySelectedTicket()
    {
        if (!HasDetail)
            return;

        var sb = new StringBuilder();
        sb.AppendLine($"# {EditTitle}");
        sb.AppendLine();
        sb.AppendLine($"- Status: {EditStatus}");
        sb.AppendLine($"- Priority: {EditPriority}");
        sb.AppendLine($"- Type: {EditTicketType}");
        sb.AppendLine();
        AppendSection(sb, "Summary", EditSummary);
        AppendSection(sb, "Background", EditBackground);
        AppendSection(sb, "Problem", EditProblem);
        AppendSection(sb, "Acceptance Criteria", EditAcceptanceCriteria);
        AppendSection(sb, "Technical Notes", EditTechnicalNotes);
        AppendSection(sb, "Linked Files", EditLinkedFilePaths);
        AppendSection(sb, "Linked Symbols", EditLinkedSymbols);

        Clipboard.SetText(sb.ToString().TrimEnd());
        SaveStatus = "Ticket copied.";
    }

    [RelayCommand]
    private async Task GenerateImplementationPlanAsync()
    {
        if (IsDraftMode && CurrentDraft != null)
        {
            IsDraftGenerating  = true;
            DraftStatusMessage = "Generating implementation plan…";

            try
            {
                var updated = await _draftService.GeneratePlanAsync(_activeProjectId, CurrentDraft);
                CurrentDraft = updated;

                // Update only plan fields; other fields are untouched
                HasPlan           = true;
                PlanTitle         = $"{updated.Title} — Implementation Plan";
                PlanGoal          = updated.Summary;
                PlanProposedSteps = updated.ImplementationPlan ?? string.Empty;
                PlanAffectedContext = updated.LinkedFilePaths ?? string.Empty;

                DraftStatusMessage = "Implementation plan generated.";
            }
            catch (Exception ex)
            {
                DraftStatusMessage = $"Plan generation failed: {ex.Message}";
            }
            finally
            {
                IsDraftGenerating = false;
            }
        }
        else if (SelectedTicket != null)
        {
            HasPlan = true;
            PlanId = 0;
            PlanTitle = $"Implementation Plan for {SelectedTicket.Title}";
            PlanGoal = SelectedTicket.Summary ?? string.Empty;
            ActiveTab = TicketDetailTab.ImplementationPlan;
        }
    }

    [RelayCommand]
    private async Task SavePlanAsync()
    {
        if (SelectedTicket == null) return;
        if (string.IsNullOrWhiteSpace(PlanTitle))
        {
            PlanSaveStatus = "Title is required.";
            return;
        }

        IsSavingPlan = true;
        PlanSaveStatus = "Saving Plan...";

        try
        {
            var plan = new ProjectImplementationPlan
            {
                Id = PlanId,
                ProjectId = _activeProjectId,
                TicketId = SelectedTicket.Id,
                Title = PlanTitle.Trim(),
                Goal = PlanGoal.Trim(),
                Scope = string.IsNullOrWhiteSpace(PlanScope) ? null : PlanScope.Trim(),
                ProposedSteps = string.IsNullOrWhiteSpace(PlanProposedSteps) ? null : PlanProposedSteps.Trim(),
                AffectedContext = string.IsNullOrWhiteSpace(PlanAffectedContext) ? null : PlanAffectedContext.Trim(),
                RisksNotes = string.IsNullOrWhiteSpace(PlanRisksNotes) ? null : PlanRisksNotes.Trim(),
                Status = PlanStatus,
                LinkedFilePaths = string.IsNullOrWhiteSpace(PlanLinkedFilePaths) ? null : PlanLinkedFilePaths.Trim(),
                LinkedSymbols = string.IsNullOrWhiteSpace(PlanLinkedSymbols) ? null : PlanLinkedSymbols.Trim()
            };

            var savedId = await _memoryService.SavePlanAsync(plan);
            PlanId = savedId;
            PlanSaveStatus = "Plan Saved ✓";
        }
        catch (Exception ex)
        {
            PlanSaveStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsSavingPlan = false;
        }
    }

    [RelayCommand]
    private async Task CancelPlanEditAsync()
    {
        if (SelectedTicket != null)
        {
            await RefreshPlanAsync(SelectedTicket.Id);
        }
    }

    public Action<long, string, string, string, string>? OnAskAboutPlan { get; set; }

    [RelayCommand]
    private void AskAboutPlan()
    {
        if (SelectedTicket == null || !HasPlan) return;

        // Bundle context: ticket title, plan goals/steps, linked files
        var planSummary = $"""
            TICKET: {SelectedTicket.Title}
            GOAL: {PlanGoal}
            STEPS:
            {PlanProposedSteps}
            """;

        OnAskAboutPlan?.Invoke(SelectedTicket.Id, SelectedTicket.Title, planSummary, PlanLinkedFilePaths, PlanLinkedSymbols);
    }

    // ── Build Ticket (Phase 3+4A — real context, LLM proposal, dry-run validation) ──

    [RelayCommand(CanExecute = nameof(CanBuildTicket))]
    private async Task BuildSelectedTicketProposalAsync()
    {
        // ── 1. Validate state with visible feedback ──
        if (IsBuildingTicket) return;

        if (SelectedTicket == null)
        {
            SaveStatus = "Select a ticket before building a proposal.";
            return;
        }

        if (EditId <= 0)
        {
            SaveStatus = "Save the ticket before generating a builder proposal.";
            return;
        }

        if (string.IsNullOrWhiteSpace(EditTitle))
        {
            SaveStatus = "Ticket must have a title.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_activeProjectPath))
        {
            SaveStatus = "No active project path found.";
            return;
        }

        await RefreshBuildReadinessAsync();
        if (BuildReadiness is { IsReady: false })
        {
            SaveStatus = $"Build blocked: {BuildReadiness.Message}";
            return;
        }

        // ── 2. Add trace at click boundary ──
        _llmTraceService?.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.BuildThisClicked",
            WorkspaceName = "Builder",
            ProjectId = _activeProjectId,
            TicketId = EditId,
            ActiveProjectName = _activeProjectName,
            ActiveProjectPath = _activeProjectPath,
            ParsedResponseSummary = $"User clicked Build This (Proposal). " +
                                    $"selectedTicketId={EditId}, " +
                                    $"selectedTicketTitle='{EditTitle}', " +
                                    $"activeProjectId={_activeProjectId}, " +
                                    $"activeProjectName='{_activeProjectName}', " +
                                    $"activeProjectPath='{_activeProjectPath}'",
            CreatedAt = DateTime.UtcNow
        });

        // ── 3. Initiate generation ──
        SaveStatus = "Generating builder proposal...";
        OnRequestProposal?.Invoke(EditId);
    }

    [RelayCommand]
    private async Task BuildSelectedTicketAsync()
    {
        if (!CanBuildTicket) return;

        IsBuildingTicket    = true;
        HasBuildPreview     = false;
        CurrentBuildPreview = null;
        CurrentBuildResult  = null;
        BuildStatusMessage  = "Assembling build context…";
        OnPropertyChanged(nameof(CanBuildTicket));

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            var preview = await _orchestrator.CreateBuildPreviewAsync(
                _activeProjectId,
                EditId,
                cts.Token);

            CurrentBuildPreview = preview;
            HasBuildPreview     = true;

            // Prepend index warning to context summary when code context is limited
            if (IsContextLimited)
            {
                preview.ContextSummary =
                    $"\u26a0\ufe0f Limited context: project is not indexed. Code snippets may be unavailable.\n\n{preview.ContextSummary}";
                CurrentBuildPreview = preview;   // re-assign to fire PropertyChanged
            }

            if (preview.IsEmpty)
            {
                BuildStatusMessage = "No changes proposed.";
            }
            else if (preview.ValidationResult.AllValid)
            {
                BuildStatusMessage = $"Validation passed — {preview.ValidationResult.Summary}";
            }
            else
            {
                BuildStatusMessage = $"Validation failed: {preview.ValidationResult.Summary}";
            }
        }
        catch (Exception ex)
        {
            BuildStatusMessage = $"AI proposal failed: {ex.Message}";
        }
        finally
        {
            IsBuildingTicket = false;
            OnPropertyChanged(nameof(CanBuildTicket));
        }
    }

    [RelayCommand]
    private void CancelBuildPreview()
    {
        ClearBuildState();
    }

    /// <summary>
    /// Invokes OnRequestIndex so ShellViewModel can trigger the real indexing command.
    /// Safe to call even if no callback is wired — no-op in that case.
    /// </summary>
    [RelayCommand]
    private void RequestIndex()
    {
        OnRequestIndex?.Invoke();
    }

    /// <summary>
    /// Called by ShellViewModel whenever ActiveStatus changes so the Tickets workspace
    /// stays in sync with the project's index readiness without needing a service reference.
    ///
    /// Preflight auto-generate logic:
    /// - If we are in the Indexing state (user clicked "Index Project First") AND status is now
    ///   Ready, automatically generate the draft from the pending context. This is the happy path.
    /// - If we are in the Indexing state AND status is NOT Ready (indexing failed or was skipped),
    ///   transition to IndexFailed so the user can retry or continue without index.
    /// - Any other state: just update IsProjectIndexed normally.
    /// </summary>
    public void SetIndexStatus(string status)
    {
        IsProjectIndexed = status == "Ready";
        OnPropertyChanged(nameof(IsContextLimited));
        _ = RefreshBuildReadinessAsync();

        if (DraftPreflight == DraftPreflightState.Indexing)
        {
            if (IsProjectIndexed)
            {
                // Happy path: indexing completed, auto-generate the draft.
                if (_shouldGenerateDraftAfterIndex && (_pendingChatContext != null || _pendingChatContexts != null))
                {
                    IsDraftIndexing               = false;
                    _shouldGenerateDraftAfterIndex = false;
                    DraftPreflightMessage         = string.Empty;
                    var ctx = _pendingChatContext;
                    var contexts = _pendingChatContexts;
                    _ = ctx != null
                        ? GeneratePendingDraftAfterIndexAsync(ctx)
                        : GeneratePendingDraftsAfterIndexAsync(contexts!);
                }
            }
            else if (status.StartsWith("Err:", StringComparison.OrdinalIgnoreCase))
            {
                // Explicit failure captured from the status string.
                FailIndexing(status.Substring(4).Trim());
            }
            else if (status != "Indexing..." && status != "Needs Index" && status != "Checking...")
            {
                // Some other non-ready state that isn't a known progress state.
                FailIndexing(string.Empty);
            }
            // else: status is "Indexing...", "Needs Index", etc. -- stay in Indexing state and wait.
        }
    }

    private void FailIndexing(string? message)
    {
        IsDraftIndexing               = false;
        _shouldGenerateDraftAfterIndex = false;
        DraftPreflight                = DraftPreflightState.IndexFailed;
        
        if (string.IsNullOrWhiteSpace(message))
        {
            DraftPreflightMessage = "Indexing did not complete. You can try again or continue without index.";
        }
        else
        {
            DraftPreflightMessage = $"Indexing failed: {message}. You can try again or continue without index.";
        }
    }

    private async Task GeneratePendingDraftAfterIndexAsync(ChatTicketContext ctx)
    {
        try
        {
            await GeneratePendingDraftAsync(ctx);
        }
        catch (Exception ex)
        {
            IsDraftIndexing = false;
            DraftPreflight = DraftPreflightState.IndexFailed;
            DraftPreflightMessage = "Draft generation failed after indexing. You can retry or continue without index.";

            _llmTraceService?.AddTrace(new LlmTraceEntry
            {
                FeatureName = "Tickets.GeneratePendingDraftAfterIndex",
                WorkspaceName = "Tickets",
                ProjectId = _activeProjectId,
                ActiveProjectName = _activeProjectName,
                ActiveProjectPath = _activeProjectPath,
                WasSuccessful = false,
                ErrorMessage = ex.Message,
                ParsedResponseSummary = "Pending draft generation failed after indexing completed.",
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    private async Task GeneratePendingDraftsAfterIndexAsync(IReadOnlyList<ChatTicketContext> contexts)
    {
        try
        {
            await GeneratePendingDraftsAsync(contexts);
        }
        catch (Exception ex)
        {
            IsDraftIndexing = false;
            DraftPreflight = DraftPreflightState.IndexFailed;
            DraftPreflightMessage = "Draft generation failed after indexing. You can retry or continue without index.";

            _llmTraceService?.AddTrace(new LlmTraceEntry
            {
                FeatureName = "Tickets.GeneratePendingDraftsAfterIndex",
                WorkspaceName = "Tickets",
                ProjectId = _activeProjectId,
                ActiveProjectName = _activeProjectName,
                ActiveProjectPath = _activeProjectPath,
                WasSuccessful = false,
                ErrorMessage = ex.Message,
                ParsedResponseSummary = "Pending split ticket drafts failed after indexing completed.",
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    [RelayCommand]
    private void ApplyBuildPreview()
    {
        // Phase 1: apply is not implemented. No files written.
        BuildStatusMessage = "Apply not implemented yet — Phase 2 required.";
    }

    private void ClearBuildState()
    {
        HasBuildPreview     = false;
        CurrentBuildPreview = null;
        CurrentBuildResult  = null;
        BuildStatusMessage  = string.Empty;
        IsBuildingTicket    = false;
        OnPropertyChanged(nameof(CanBuildTicket));
    }

    // ── Cancel ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void CancelEdit()
    {
        if (SelectedTicket != null)
        {
            _ = LoadTicketIntoEditorAsync(SelectedTicket);
        }
        else
        {
            ClearEditor();
        }
        ClearTicketDirtyState();
    }

    // ── Prefill from chat (called by ShellViewModel) ────────────────────────

    /// <summary>
    /// Sets up a new ticket editor with pre-filled data from chat context.
    /// </summary>
    public void PrefillFromChat(string title, string summary, string? linkedFilePaths, string? linkedSymbols)
    {
        NewTicket();
        EditTitle           = title;
        EditSummary         = summary;
        EditLinkedFilePaths = linkedFilePaths ?? string.Empty;
        EditLinkedSymbols   = linkedSymbols ?? string.Empty;
    }

    // ── Draft Ticket — entry point and commands ──────────────────────────────

    /// <summary>
    /// Called by ShellViewModel when the user clicks Ticket in Chat.
    /// If the project is indexed (Ready), generates the draft immediately.
    /// If not indexed (Needs Index), stores the pending context and shows the
    /// preflight choice panel (Index Project First / Continue Without Index / Cancel).
    /// </summary>
    public async Task BeginDraftFromChatAsync(ChatTicketContext ctx)
    {
        if (IsContextLimited)
        {
            // Project is not indexed — show preflight choice instead of generating.
            _pendingChatContext = ctx;
            _pendingChatContexts = null;
            DraftPreflight      = DraftPreflightState.NeedsChoice;
            HasDetail           = false;   // don't show the editor yet
            return;
        }

        // Project is indexed — proceed immediately.
        await GeneratePendingDraftAsync(ctx);
    }

    public async Task BeginDraftsFromChatAsync(IReadOnlyList<ChatTicketContext> contexts)
    {
        if (contexts.Count == 0) return;
        if (contexts.Count == 1)
        {
            await BeginDraftFromChatAsync(contexts[0]);
            return;
        }

        if (IsContextLimited)
        {
            _pendingChatContext = null;
            _pendingChatContexts = contexts;
            DraftPreflight = DraftPreflightState.NeedsChoice;
            HasDetail = false;
            return;
        }

        await GeneratePendingDraftsAsync(contexts);
    }

    /// <summary>Inner generation path — shared by BeginDraftFromChatAsync and PreflightContinue.</summary>
    private async Task GeneratePendingDraftAsync(ChatTicketContext ctx)
    {
        IsDraftMode        = true;
        IsDraftGenerating  = true;
        DraftStatusMessage = "Generating draft…";
        HasDetail          = true;
        IsEditing          = true;
        IsNewTicket        = true;
        ActiveTab          = TicketDetailTab.Overview;

        // Clear any preflight state before generating
        DraftPreflight      = DraftPreflightState.None;
        _pendingChatContext  = null;
        _pendingChatContexts = null;

        try
        {
            var draft = await _draftService.GenerateDraftAsync(
                _activeProjectId,
                _activeProjectName,
                ctx.ProposedTitle,
                ctx.MessageText,
                ctx.LinkedFilePaths,
                ctx.LinkedSymbols,
                ctx.SessionId);

            draft.SourceChatSessionId = ctx.SessionId;
            draft.SourceMessageId     = ctx.MessageId;
            draft.SourceMessageText   = ctx.MessageText;
            CurrentDraft = draft;

            LoadDraftIntoEditor(draft);

            // Short status note in the banner — keep it concise
            DraftStatusMessage = IsContextLimited
                ? "⚠ Limited context — project not indexed"
                : string.Empty;
        }
        catch (Exception ex)
        {
            DraftStatusMessage = $"Draft generation failed: {ex.Message}";
        }
        finally
        {
            IsDraftGenerating = false;
        }
    }

    // ── Preflight commands ────────────────────────────────────────────────────

    /// <summary>
    /// Preflight: "Continue Without Index" — generate draft immediately with limited context.
    /// Guard: no-op while indexing is in progress (belt-and-suspenders; button is also disabled in UI).
    /// </summary>
    private async Task GeneratePendingDraftsAsync(IReadOnlyList<ChatTicketContext> contexts)
    {
        IsDraftMode        = true;
        IsDraftGenerating  = true;
        DraftStatusMessage = $"Generating {contexts.Count} draft tickets...";
        HasDetail          = true;
        IsEditing          = true;
        IsNewTicket        = true;
        ActiveTab          = TicketDetailTab.Overview;

        DraftPreflight      = DraftPreflightState.None;
        _pendingChatContext  = null;
        _pendingChatContexts = null;

        try
        {
            var existingDrafts = Tickets.Where(t => t.IsDraft).ToList();
            foreach (var existingDraft in existingDrafts)
                Tickets.Remove(existingDraft);

            var generated = new List<TicketItem>(contexts.Count);
            foreach (var ctx in contexts)
            {
                var draft = await _draftService.GenerateDraftAsync(
                    _activeProjectId,
                    _activeProjectName,
                    ctx.ProposedTitle,
                    ctx.MessageText,
                    ctx.LinkedFilePaths,
                    ctx.LinkedSymbols,
                    ctx.SessionId);

                draft.SourceChatSessionId = ctx.SessionId;
                draft.SourceMessageId     = ctx.MessageId;
                draft.SourceMessageText   = ctx.MessageText;

                generated.Add(MapDraftToUnsavedTicket(draft));
            }

            for (var i = generated.Count - 1; i >= 0; i--)
                Tickets.Insert(0, generated[i]);

            CurrentDraft = null;
            DraftStatusMessage = $"Generated {generated.Count} draft tickets. Review and save each one.";
            if (Tickets.Count > 0 && Tickets[0].IsDraft)
                SelectedTicket = Tickets[0];
        }
        catch (Exception ex)
        {
            DraftStatusMessage = $"Draft generation failed: {ex.Message}";
        }
        finally
        {
            IsDraftGenerating = false;
        }
    }

    [RelayCommand]
    private async Task PreflightContinueAsync()
    {
        if ((_pendingChatContext == null && _pendingChatContexts == null) || IsDraftIndexing) return;
        if (_pendingChatContext != null)
            await GeneratePendingDraftAsync(_pendingChatContext);
        else
            await GeneratePendingDraftsAsync(_pendingChatContexts!);
    }

    /// <summary>
    /// Preflight: "Index Project First" — disables Continue/Index buttons, shows indexing progress
    /// text, and fires OnRequestIndex. When ShellViewModel calls SetIndexStatus("Ready"), the
    /// pending draft is auto-generated. If indexing fails, state falls back to IndexFailed.
    /// </summary>
    [RelayCommand]
    private void PreflightIndexProject()
    {
        if (_pendingChatContext == null && _pendingChatContexts == null) return;

        IsDraftIndexing              = true;
        _shouldGenerateDraftAfterIndex = true;
        DraftPreflight               = DraftPreflightState.Indexing;
        DraftPreflightMessage        = "Indexing project…";

        OnRequestIndex?.Invoke();
    }

    /// <summary>
    /// Preflight: "Cancel" — discard the pending chat context and return to normal state.
    /// Invokes OnCancelDraft so the shell can navigate back to Chat.
    /// </summary>
    [RelayCommand]
    private void PreflightCancel()
    {
        _pendingChatContext           = null;
        _pendingChatContexts          = null;
        _shouldGenerateDraftAfterIndex = false;
        IsDraftIndexing              = false;
        DraftPreflight               = DraftPreflightState.None;
        DraftPreflightMessage        = string.Empty;
        HasDetail                    = false;
        OnCancelDraft?.Invoke();
    }

    /// <summary>Saves the draft ticket, its plan (if any), exits draft mode, and selects the new ticket.</summary>
    [RelayCommand]
    private async Task ApproveDraftAsync()
    {
        var draftBeingSaved = SelectedTicket?.IsDraft == true ? SelectedTicket : null;
        var remainingDrafts = CaptureRemainingDrafts(draftBeingSaved);

        var savedId = await SaveDraftTicketAsync();
        if (savedId <= 0) return;

        // If a plan was generated during review, save it too and link it
        if (HasPlan)
        {
            await SaveLinkedPlanAsync(savedId);
        }

        IsDraftMode        = false;
        CurrentDraft       = null;
        DraftStatusMessage = string.Empty;

        await RefreshListAsync();  // ClearEditor() is called inside
        RestoreUnsavedDrafts(remainingDrafts);

        var created = Tickets.FirstOrDefault(t => t.Id == savedId);
        if (created != null)
        {
            await LoadTicketIntoEditorAsync(created);
            SelectedTicket = created;
        }
        else if (remainingDrafts.Count > 0)
        {
            SelectedTicket = remainingDrafts[0];
        }

        SaveStatus = remainingDrafts.Count > 0
            ? $"Ticket created \u2713  {remainingDrafts.Count} draft(s) still waiting."
            : "Ticket created \u2713";
    }

    [RelayCommand]
    private async Task SaveAllDraftsAsync()
    {
        var drafts = Tickets.Where(t => t.IsDraft).ToList();
        if (drafts.Count == 0)
        {
            SaveStatus = "No draft tickets to save.";
            return;
        }

        var savedCount = 0;
        foreach (var draft in drafts)
        {
            SelectedTicket = draft;
            LoadTicketIntoEditor(draft);

            var savedId = await SaveDraftTicketAsync();
            if (savedId <= 0)
                break;

            savedCount++;
            Tickets.Remove(draft);
        }

        IsDraftMode = false;
        CurrentDraft = null;
        DraftStatusMessage = string.Empty;
        await RefreshListAsync();
        SaveStatus = $"Saved {savedCount} draft ticket(s).";
    }

    private async Task SaveLinkedPlanAsync(long ticketId)
    {
        if (_memoryService == null)
        {
            SaveStatus = "Ticket created, but plan persistence is unavailable.";
            return;
        }

        try
        {
            var plan = new ProjectImplementationPlan
            {
                Id = PlanId,
                ProjectId = _activeProjectId,
                TicketId = ticketId,
                Title = string.IsNullOrWhiteSpace(PlanTitle) ? $"{EditTitle.Trim()} — Implementation Plan" : PlanTitle.Trim(),
                Goal = string.IsNullOrWhiteSpace(PlanGoal) ? EditSummary.Trim() : PlanGoal.Trim(),
                Scope = string.IsNullOrWhiteSpace(PlanScope) ? (string.IsNullOrWhiteSpace(EditAcceptanceCriteria) ? null : EditAcceptanceCriteria.Trim()) : PlanScope.Trim(),
                SourceChatMessageId = CurrentDraft?.SourceMessageId,
                ProposedSteps = string.IsNullOrWhiteSpace(PlanProposedSteps) ? null : PlanProposedSteps.Trim(),
                AffectedContext = string.IsNullOrWhiteSpace(PlanAffectedContext) ? (string.IsNullOrWhiteSpace(EditLinkedFilePaths) ? null : EditLinkedFilePaths.Trim()) : PlanAffectedContext.Trim(),
                RisksNotes = string.IsNullOrWhiteSpace(PlanRisksNotes) ? null : PlanRisksNotes.Trim(),
                Status = PlanStatus,
                LinkedFilePaths = string.IsNullOrWhiteSpace(PlanLinkedFilePaths) ? null : PlanLinkedFilePaths.Trim(),
                LinkedSymbols = string.IsNullOrWhiteSpace(PlanLinkedSymbols) ? null : PlanLinkedSymbols.Trim()
            };

            var savedPlanId = await _memoryService.SavePlanAsync(plan);
            PlanId = savedPlanId;
        }
        catch (Exception ex)
        {
            // Log or show warning but don't fail ticket save
            SaveStatus = $"Ticket saved, but plan save failed: {ex.Message}";
        }
    }

    /// <summary>Saves the draft ticket and then navigates to Plans to create a plan.</summary>
    [RelayCommand]
    private async Task ApproveDraftWithPlanAsync()
    {
        var draftBeingSaved = SelectedTicket?.IsDraft == true ? SelectedTicket : null;
        var remainingDrafts = CaptureRemainingDrafts(draftBeingSaved);

        // If plan is empty, generate it first
        if (!HasPlan || string.IsNullOrWhiteSpace(PlanProposedSteps))
        {
            await GenerateImplementationPlanAsync();
            if (!HasPlan || string.IsNullOrWhiteSpace(PlanProposedSteps))
            {
                SaveStatus = "Warning: Implementation plan is empty.";
            }
        }

        var savedId = await SaveDraftTicketAsync();
        if (savedId <= 0) return;

        // ✓ SNAPSHOT field values for navigation event (though we are also saving to DB now)
        var planTitle       = string.IsNullOrWhiteSpace(PlanTitle) ? $"{EditTitle.Trim()} — Implementation Plan" : PlanTitle.Trim();
        var planGoal        = string.IsNullOrWhiteSpace(PlanGoal) ? EditSummary.Trim() : PlanGoal.Trim();
        var planScope       = string.IsNullOrWhiteSpace(PlanScope) ? (string.IsNullOrWhiteSpace(EditAcceptanceCriteria) ? null : EditAcceptanceCriteria.Trim()) : PlanScope.Trim();
        var planSteps       = string.IsNullOrWhiteSpace(PlanProposedSteps) ? null : PlanProposedSteps.Trim();
        var planFiles       = string.IsNullOrWhiteSpace(PlanAffectedContext) ? (string.IsNullOrWhiteSpace(EditLinkedFilePaths) ? null : EditLinkedFilePaths.Trim()) : PlanAffectedContext.Trim();
        var planSymbols     = string.IsNullOrWhiteSpace(EditLinkedSymbols) ? null : EditLinkedSymbols.Trim();
        var planRisksNotes  = string.IsNullOrWhiteSpace(PlanRisksNotes) ? null : PlanRisksNotes.Trim();

        // Save the plan to DB and link it
        await SaveLinkedPlanAsync(savedId);

        IsDraftMode        = false;
        CurrentDraft       = null;
        DraftStatusMessage = string.Empty;

        await RefreshListAsync();
        RestoreUnsavedDrafts(remainingDrafts);

        // Re-select the saved ticket
        SelectedTicket = Tickets.FirstOrDefault(t => t.Id == savedId);

        // Navigate to Plans with full prefill
        OnApproveDraftWithPlan?.Invoke(planTitle, planGoal, planSteps, planFiles, planSymbols, planScope, planRisksNotes);
    }

    /// <summary>Re-generates all draft fields from the original chat context.</summary>
    [RelayCommand]
    private async Task RegenerateAllAsync()
    {
        if (CurrentDraft == null) return;

        IsDraftGenerating  = true;
        DraftStatusMessage = "Regenerating…";

        try
        {
            var draft = await _draftService.GenerateDraftAsync(
                _activeProjectId,
                _activeProjectName,
                CurrentDraft.SourceMessageText.Length > 80
                    ? CurrentDraft.SourceMessageText[..80]
                    : CurrentDraft.SourceMessageText,
                CurrentDraft.SourceMessageText,
                CurrentDraft.LinkedFilePaths,
                CurrentDraft.LinkedSymbols,
                CurrentDraft.SourceChatSessionId);

            draft.SourceChatSessionId = CurrentDraft.SourceChatSessionId;
            draft.SourceMessageId     = CurrentDraft.SourceMessageId;
            draft.SourceMessageText   = CurrentDraft.SourceMessageText;
            CurrentDraft = draft;

            LoadDraftIntoEditor(draft);
            DraftStatusMessage = "Draft regenerated — review before saving";
        }
        catch (Exception ex)
        {
            DraftStatusMessage = $"Regeneration failed: {ex.Message}";
        }
        finally
        {
            IsDraftGenerating = false;
        }
    }

    /// <summary>Re-generates only the Tests sub-fields; other fields are preserved.</summary>
    [RelayCommand]
    private async Task RegenerateTestsAsync()
    {
        if (CurrentDraft == null) return;

        IsDraftGenerating  = true;
        DraftStatusMessage = "Regenerating tests…";

        try
        {
            var updated = await _draftService.RegenerateTestsAsync(_activeProjectId, CurrentDraft);
            CurrentDraft = updated;

            // Update only test sub-fields; other fields are untouched
            EditTestsUnitTests        = updated.UnitTests;
            EditTestsIntegrationTests = updated.IntegrationTests;
            EditTestsManualTests      = updated.ManualTests;
            EditTestsRegressionTests  = updated.RegressionTests;
            EditTestsBuildValidation  = updated.BuildValidation;

            DraftStatusMessage = "Tests regenerated. " + updated.GenerationNote;
        }
        catch (Exception ex)
        {
            DraftStatusMessage = $"Test regeneration failed: {ex.Message}";
        }
        finally
        {
            IsDraftGenerating = false;
        }
    }

    /// <summary>Discards the draft and navigates back to Chat.</summary>
    [RelayCommand]
    private void CancelDraft()
    {
        // If this was an in-list draft, we might want to remove it,
        // but for now let's just exit draft mode and clear selection.
        if (SelectedTicket?.IsDraft == true)
        {
            Tickets.Remove(SelectedTicket);
        }

        IsDraftMode        = false;
        CurrentDraft       = null;
        DraftStatusMessage = string.Empty;
        ClearEditor();
        OnCancelDraft?.Invoke();
    }

    // ── Codebase Ticket Generator ───────────────────────────────────────────

    [RelayCommand]
    private async Task GenerateCodebaseTicketsAsync()
    {
        await CodexReview.GenerateCodexTicketsCommand.ExecuteAsync(null);
    }

    private async Task<CodebaseTicketGenerationResult> GenerateCodexTicketsForReviewAsync()
    {
        if (_generatorService == null)
        {
            return new CodebaseTicketGenerationResult
            {
                Success = false,
                ErrorMessage = "Codebase ticket generator is not available."
            };
        }

        if (IsGeneratingCodebaseTickets)
        {
            return new CodebaseTicketGenerationResult
            {
                Success = false,
                ErrorMessage = "Codebase ticket generation is already running."
            };
        }

        IsGeneratingCodebaseTickets = true;
        DraftStatusMessage = "Analyzing codebase...";

        try
        {
            var existingDrafts = Tickets.Where(t => t.IsDraft).ToList();
            foreach (var draft in existingDrafts)
            {
                Tickets.Remove(draft);
            }

            ClearEditor();
            var result = await _generatorService.GenerateTicketsAsync(_activeProjectId);
            DraftStatusMessage = result.Success
                ? $"Generated {result.Drafts.Count} Codex tickets for review."
                : $"Generation failed: {result.ErrorMessage}";
            return result;
        }
        catch (Exception ex)
        {
            DraftStatusMessage = $"Error: {ex.Message}";
            return new CodebaseTicketGenerationResult
            {
                Success = false,
                ErrorMessage = $"Generation failed: {ex.Message}"
            };
        }
        finally
        {
            IsGeneratingCodebaseTickets = false;
        }
    }

    private async Task ImportCodexReviewTicketsAsync(IReadOnlyList<TicketReviewItemViewModel> selectedTickets)
    {
        if (_ticketService == null)
        {
            throw new InvalidOperationException("Ticket service is not available.");
        }

        foreach (var reviewItem in selectedTickets)
        {
            var ticket = BuildCodexImportedTicket(reviewItem);
            await _ticketService.SaveTicketAsync(ticket);
        }

        await RefreshListAsync();
    }

    private ProjectTicket BuildCodexImportedTicket(TicketReviewItemViewModel reviewItem)
    {
        var contextWarnings = GetCodexContextWarnings();
        return new ProjectTicket
        {
            Id = 0,
            ProjectId = _activeProjectId,
            SessionId = Guid.NewGuid(),
            Title = reviewItem.Title.Trim(),
            TicketType = string.IsNullOrWhiteSpace(reviewItem.TicketType) ? "Task" : reviewItem.TicketType,
            Priority = string.IsNullOrWhiteSpace(reviewItem.Priority) ? "Medium" : reviewItem.Priority,
            Summary = string.IsNullOrWhiteSpace(reviewItem.Summary) ? null : reviewItem.Summary.Trim(),
            Background = string.IsNullOrWhiteSpace(reviewItem.Background) ? null : reviewItem.Background.Trim(),
            Problem = string.IsNullOrWhiteSpace(reviewItem.Problem) ? null : reviewItem.Problem.Trim(),
            AcceptanceCriteria = BuildListText(reviewItem.AcceptanceCriteria),
            TechnicalNotes = reviewItem.BuildCodexTechnicalNotes(CodexReview.ContextQualityScore, contextWarnings),
            Status = "Draft",
            Content = string.IsNullOrWhiteSpace(reviewItem.Summary) ? reviewItem.Problem : reviewItem.Summary,
            LinkedFilePaths = reviewItem.AffectedFiles.Count == 0 ? null : string.Join("\n", reviewItem.AffectedFiles),
            LinkedCodeIndexEntryIds = null,
            LinkedSymbols = reviewItem.AffectedSymbols.Count == 0 ? null : string.Join("\n", reviewItem.AffectedSymbols),
            UnitTests = string.IsNullOrWhiteSpace(reviewItem.UnitTests) ? null : reviewItem.UnitTests.Trim(),
            IntegrationTests = string.IsNullOrWhiteSpace(reviewItem.IntegrationTests) ? null : reviewItem.IntegrationTests.Trim(),
            ManualTests = string.IsNullOrWhiteSpace(reviewItem.ManualTests) ? null : reviewItem.ManualTests.Trim(),
            RegressionTests = string.IsNullOrWhiteSpace(reviewItem.RegressionTests) ? null : reviewItem.RegressionTests.Trim(),
            BuildValidation = string.IsNullOrWhiteSpace(reviewItem.BuildValidation) ? null : reviewItem.BuildValidation.Trim(),
            ContextSummary = $"Codex dogfood import. Context quality: {CodexReview.ContextQualityScore}/100.",
            IsGenerated = true,
            GenerationNote = BuildCodexGenerationNote(reviewItem, contextWarnings)
        };
    }

    private IReadOnlyList<string> GetCodexContextWarnings()
        => string.IsNullOrWhiteSpace(CodexReview.ContextWarningText)
            ? []
            : CodexReview.ContextWarningText
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

    private static string? BuildListText(IReadOnlyList<string> values)
        => values.Count == 0 ? null : string.Join("\n", values.Select(value => $"- {value}"));

    private string BuildCodexGenerationNote(
        TicketReviewItemViewModel reviewItem,
        IReadOnlyList<string> contextWarnings)
    {
        var parts = new List<string>
        {
            "Generated by IronDev Self-Dogfood Loop",
            $"Source: Codex",
            $"Context quality: {CodexReview.ContextQualityScore}/100",
            $"Confidence: {reviewItem.ConfidenceScore}/100",
            $"Suggested build order: {reviewItem.SuggestedBuildOrder}"
        };

        if (!string.IsNullOrWhiteSpace(reviewItem.Category))
            parts.Add($"Category: {reviewItem.Category}");
        if (!string.IsNullOrWhiteSpace(reviewItem.RiskLevel))
            parts.Add($"Risk: {reviewItem.RiskLevel}");
        if (reviewItem.AffectedFiles.Count > 0)
            parts.Add($"Affected files: {string.Join("; ", reviewItem.AffectedFiles.Take(6))}");
        if (reviewItem.AffectedSymbols.Count > 0)
            parts.Add($"Affected symbols: {string.Join("; ", reviewItem.AffectedSymbols.Take(6))}");
        if (contextWarnings.Count > 0)
            parts.Add("Context warnings: " + string.Join("; ", contextWarnings.Take(4)));
        if (reviewItem.GroundingWarnings.Count > 0)
            parts.Add("Grounding warnings: " + string.Join("; ", reviewItem.GroundingWarnings.Take(4)));

        return string.Join("\n", parts);
    }

    // ── Private draft helpers ────────────────────────────────────────────────

    /// <summary>
    /// Shared save path used by both ApproveDraftAsync and ApproveDraftWithPlanAsync.
    /// Packs test sub-fields into TechnicalNotes, then calls the existing SaveTicketAsync.
    /// Returns the saved ticket ID, or 0 on failure.
    /// </summary>
    private async Task<long> SaveDraftTicketAsync()
    {
        if (string.IsNullOrWhiteSpace(EditTitle))
        {
            SaveStatus = "Title is required.";
            return 0;
        }

        IsSaving   = true;
        SaveStatus = "Saving…";

        try
        {
            var selectedDraft = SelectedTicket?.IsDraft == true ? SelectedTicket : null;

            // Pack test sub-fields into TechnicalNotes before save
            SyncTestsToTechnicalNotes();

            var ticket = new IronDev.Data.Models.ProjectTicket
            {
                Id                     = 0,            // always a new ticket
                ProjectId              = _activeProjectId,
                SessionId              = Guid.NewGuid(),
                Title                  = EditTitle.Trim(),
                TicketType             = EditTicketType,
                Priority               = EditPriority,
                Summary                = string.IsNullOrWhiteSpace(EditSummary)              ? null : EditSummary.Trim(),
                Background             = string.IsNullOrWhiteSpace(EditBackground)           ? null : EditBackground.Trim(),
                Problem                = string.IsNullOrWhiteSpace(EditProblem)              ? null : EditProblem.Trim(),
                AcceptanceCriteria     = string.IsNullOrWhiteSpace(EditAcceptanceCriteria)   ? null : EditAcceptanceCriteria.Trim(),
                TechnicalNotes         = string.IsNullOrWhiteSpace(EditTechnicalNotes)       ? null : EditTechnicalNotes.Trim(),
                Status                 = EditStatus,
                Content                = EditSummary?.Trim() ?? string.Empty,
                LinkedFilePaths        = string.IsNullOrWhiteSpace(EditLinkedFilePaths)      ? null : EditLinkedFilePaths.Trim(),
                LinkedCodeIndexEntryIds = null,
                LinkedSymbols          = string.IsNullOrWhiteSpace(EditLinkedSymbols)      ? null : EditLinkedSymbols.Trim(),
                UnitTests              = string.IsNullOrWhiteSpace(EditTestsUnitTests)        ? null : EditTestsUnitTests.Trim(),
                IntegrationTests       = string.IsNullOrWhiteSpace(EditTestsIntegrationTests) ? null : EditTestsIntegrationTests.Trim(),
                ManualTests            = string.IsNullOrWhiteSpace(EditTestsManualTests)      ? null : EditTestsManualTests.Trim(),
                RegressionTests        = string.IsNullOrWhiteSpace(EditTestsRegressionTests)  ? null : EditTestsRegressionTests.Trim(),
                BuildValidation        = string.IsNullOrWhiteSpace(EditTestsBuildValidation)  ? null : EditTestsBuildValidation.Trim(),
                ContextSummary         = CurrentDraft?.Summary ?? selectedDraft?.ContextSummary,
                IsGenerated            = true,
                GenerationNote         = CurrentDraft?.GenerationNote ?? selectedDraft?.GenerationNote,
                SourceChatSessionId    = CurrentDraft?.SourceChatSessionId ?? selectedDraft?.SourceChatSessionId,
                SourceChatMessageId    = CurrentDraft?.SourceMessageId ?? selectedDraft?.SourceChatMessageId
            };

            var savedId = await _ticketService.SaveTicketAsync(ticket);
            EditId      = savedId;
            IsNewTicket = false;
            SaveStatus  = "Saved ✓";
            return savedId;
        }
        catch (Exception ex)
        {
            SaveStatus = $"Save failed: {ex.Message}";
            return 0;
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Loads all fields from a DraftTicket into the editor properties,
    /// including the Tests sub-fields (which trigger TechnicalNotes sync).
    /// </summary>
    private void LoadDraftIntoEditor(DraftTicket draft)
    {
        EditId                 = 0;
        EditTitle              = draft.Title;
        EditStatus             = draft.Status;
        EditPriority           = draft.Priority;
        EditTicketType         = draft.TicketType;
        EditSummary            = draft.Summary;
        EditBackground         = draft.Background         ?? string.Empty;
        EditProblem            = string.Empty;
        EditAcceptanceCriteria = draft.AcceptanceCriteria ?? string.Empty;
        EditLinkedFilePaths    = draft.LinkedFilePaths    ?? string.Empty;
        EditLinkedSymbols      = draft.LinkedSymbols      ?? string.Empty;

        // Set test sub-fields directly — each setter calls SyncTestsToTechnicalNotes
        EditTestsUnitTests        = draft.UnitTests;
        EditTestsIntegrationTests = draft.IntegrationTests;
        EditTestsManualTests      = draft.ManualTests;
        EditTestsRegressionTests  = draft.RegressionTests;
        EditTestsBuildValidation  = draft.BuildValidation;

        // Populate Implementation Plan tab if data exists
        if (!string.IsNullOrWhiteSpace(draft.ImplementationPlan))
        {
            HasPlan           = true;
            PlanTitle         = $"{draft.Title} — Implementation Plan";
            PlanGoal          = draft.Summary;
            PlanProposedSteps = draft.ImplementationPlan;
            PlanAffectedContext = draft.LinkedFilePaths ?? string.Empty;
        }
        else
        {
            HasPlan = false;
        }

        SaveStatus = string.Empty;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static TicketItem MapDraftToUnsavedTicket(DraftTicket draft)
        => new()
        {
            Id                 = 0,
            Title              = draft.Title,
            TicketType         = draft.TicketType,
            Priority           = draft.Priority,
            Summary            = draft.Summary,
            Background         = draft.Background,
            AcceptanceCriteria = draft.AcceptanceCriteria,
            Status             = draft.Status,
            LinkedFilePaths    = draft.LinkedFilePaths,
            LinkedSymbols      = draft.LinkedSymbols,
            UnitTests          = draft.UnitTests,
            IntegrationTests   = draft.IntegrationTests,
            ManualTests        = draft.ManualTests,
            RegressionTests    = draft.RegressionTests,
            BuildValidation    = draft.BuildValidation,
            ContextSummary     = draft.Summary,
            IsGenerated        = true,
            GenerationNote     = draft.GenerationNote,
            SourceChatSessionId = draft.SourceChatSessionId,
            SourceChatMessageId = draft.SourceMessageId,
            SourceMessageText   = draft.SourceMessageText,
            IsDraft            = true,
            CreatedDate        = DateTime.UtcNow
        };

    private List<TicketItem> CaptureRemainingDrafts(TicketItem? draftBeingSaved)
        => Tickets
            .Where(t => t.IsDraft && !ReferenceEquals(t, draftBeingSaved))
            .ToList();

    private void RestoreUnsavedDrafts(IReadOnlyList<TicketItem> drafts)
    {
        for (var i = drafts.Count - 1; i >= 0; i--)
        {
            if (!Tickets.Contains(drafts[i]))
                Tickets.Insert(0, drafts[i]);
        }
    }

    private void ClearEditor()
    {
        IsEditing  = false;
        IsNewTicket = false;
        HasDetail  = false;
        EditId     = 0;
        EditTitle  = string.Empty;
        EditStatus = "Draft";
        EditPriority      = "Medium";
        EditTicketType    = "Task";
        EditSummary       = string.Empty;
        EditBackground    = string.Empty;
        EditProblem       = string.Empty;
        EditAcceptanceCriteria = string.Empty;
        EditTechnicalNotes     = string.Empty;
        ClearTestSubFields();
        EditLinkedFilePaths    = string.Empty;
        EditLinkedSymbols      = string.Empty;
        SaveStatus = string.Empty;

        HasPlan           = false;
        PlanId            = 0;
        PlanTitle         = string.Empty;
        PlanGoal          = string.Empty;
        PlanScope         = string.Empty;
        PlanProposedSteps = string.Empty;
        PlanAffectedContext = string.Empty;
        PlanRisksNotes    = string.Empty;
        PlanStatus        = "Draft";
        PlanLinkedFilePaths = string.Empty;
        PlanLinkedSymbols   = string.Empty;
        PlanSaveStatus    = string.Empty;

        ClearBuildState();
        ClearBuildReadiness();
        ClearTicketDirtyState();
    }

    private async Task RefreshBuildReadinessAsync()
    {
        if (_readinessService == null || _activeProjectId <= 0 || EditId <= 0 || IsDraftMode || SelectedTicket == null)
        {
            ClearBuildReadiness();
            return;
        }

        try
        {
            BuildReadiness = await _readinessService.EvaluateReadinessAsync(_activeProjectId, EditId);
            ApplyBuildReadinessPresentation();
        }
        catch (Exception ex)
        {
            BuildReadiness = new BuildReadinessResult
            {
                Status = BuildReadinessStatus.Error,
                Message = $"Readiness check failed: {ex.Message}",
                BlockingIssues = { ex.Message }
            };
            ApplyBuildReadinessPresentation();
        }
        finally
        {
            BuildSelectedTicketProposalCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanBuildTicket));
        }
    }

    private void ClearBuildReadiness()
    {
        BuildReadiness = null;
        ShowBuildReadiness = false;
        BuildReadinessTitle = "Build readiness";
        BuildReadinessMessage = string.Empty;
        BuildReadinessDetails = string.Empty;
        BuildReadinessBadgeText = "READY";
        BuildReadinessBadgeStatus = BadgeStatus.Info;
        BuildReadinessActionText = string.Empty;
        BuildSelectedTicketProposalCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanBuildTicket));
    }

    private void ApplyBuildReadinessPresentation()
    {
        if (BuildReadiness == null)
        {
            ClearBuildReadiness();
            return;
        }

        ShowBuildReadiness = true;
        BuildReadinessTitle = BuildReadiness.IsReady ? "Ready to build" : "Build blocked";
        BuildReadinessMessage = BuildReadiness.Message;
        BuildReadinessBadgeText = BuildReadiness.Status.ToString();
        BuildReadinessActionText = BuildReadiness.Status == BuildReadinessStatus.NeedsReindex ? "Index Project" : string.Empty;
        BuildReadinessBadgeStatus = BuildReadiness.Status switch
        {
            BuildReadinessStatus.ReadyToBuild => BadgeStatus.Ready,
            BuildReadinessStatus.NeedsReindex => BadgeStatus.NeedsIndex,
            BuildReadinessStatus.NeedsClarification => BadgeStatus.Warning,
            BuildReadinessStatus.NeedsArchitectureDecision => BadgeStatus.Warning,
            BuildReadinessStatus.NeedsProjectProfileUpdate => BadgeStatus.Warning,
            BuildReadinessStatus.BlockedByConflict => BadgeStatus.Danger,
            BuildReadinessStatus.BlockedByExistingDecision => BadgeStatus.Danger,
            BuildReadinessStatus.Error => BadgeStatus.Danger,
            _ => BadgeStatus.Info
        };

        var details = new System.Text.StringBuilder();
        foreach (var issue in BuildReadiness.BlockingIssues)
        {
            details.AppendLine($"Blocking: {issue}");
        }
        foreach (var warning in BuildReadiness.Warnings)
        {
            details.AppendLine($"Warning: {warning}");
        }

        BuildReadinessDetails = details.ToString().TrimEnd();
        OnPropertyChanged(nameof(CanBuildTicket));
    }

    private static TicketItem MapToItem(ProjectTicket t) => new()
    {
        Id                     = t.Id,
        Title                  = t.Title,
        Status                 = t.Status,
        Priority               = t.Priority,
        TicketType             = t.TicketType,
        Summary                = t.Summary,
        Background             = t.Background,
        Problem                = t.Problem,
        AcceptanceCriteria     = t.AcceptanceCriteria,
        TechnicalNotes         = t.TechnicalNotes,
        Content                = t.Content,
        LinkedFilePaths        = t.LinkedFilePaths,
        LinkedCodeIndexEntryIds = t.LinkedCodeIndexEntryIds,
        LinkedSymbols          = t.LinkedSymbols,
        UnitTests              = t.UnitTests,
        IntegrationTests       = t.IntegrationTests,
        ManualTests            = t.ManualTests,
        RegressionTests        = t.RegressionTests,
        BuildValidation        = t.BuildValidation,
        ContextSummary         = t.ContextSummary,
        IsGenerated            = t.IsGenerated,
        GenerationNote         = t.GenerationNote,
        SourceChatSessionId    = t.SourceChatSessionId,
        SourceChatMessageId    = t.SourceChatMessageId,
        CreatedDate            = t.CreatedDate
    };

    // ── Tests serialization helpers ───────────────────────────────────────────
    // TechnicalNotes is stored as structured plain-text with section headers:
    //   ## Unit Tests\n<content>\n## Integration Tests\n...
    // This allows backward compatibility: existing TechnicalNotes values are shown
    // under Unit Tests if no section headers are found.

    private const string SecUnit        = "## Unit Tests";
    private const string SecIntegration = "## Integration Tests";
    private const string SecManual      = "## UI / Manual Tests";
    private const string SecRegression  = "## Regression Tests";
    private const string SecBuild       = "## Build Validation";

    private static readonly string[] _sectionHeaders =
        [SecUnit, SecIntegration, SecManual, SecRegression, SecBuild];

    private void SyncTechnicalNotesToTests()
    {
        var raw = EditTechnicalNotes;
        if (string.IsNullOrWhiteSpace(raw))
        {
            ClearTestSubFields();
            return;
        }

        // If no section headers are present, treat entire value as Unit Tests
        // (backward compatibility for existing TechnicalNotes entries).
        if (!_sectionHeaders.Any(h => raw.Contains(h, StringComparison.Ordinal)))
        {
            _editTestsUnitTests        = raw;
            _editTestsIntegrationTests = string.Empty;
            _editTestsManualTests      = string.Empty;
            _editTestsRegressionTests  = string.Empty;
            _editTestsBuildValidation  = string.Empty;
            OnPropertyChanged(nameof(EditTestsUnitTests));
            OnPropertyChanged(nameof(EditTestsIntegrationTests));
            OnPropertyChanged(nameof(EditTestsManualTests));
            OnPropertyChanged(nameof(EditTestsRegressionTests));
            OnPropertyChanged(nameof(EditTestsBuildValidation));
            return;
        }

        _editTestsUnitTests        = ExtractSection(raw, SecUnit);
        _editTestsIntegrationTests = ExtractSection(raw, SecIntegration);
        _editTestsManualTests      = ExtractSection(raw, SecManual);
        _editTestsRegressionTests  = ExtractSection(raw, SecRegression);
        _editTestsBuildValidation  = ExtractSection(raw, SecBuild);
        OnPropertyChanged(nameof(EditTestsUnitTests));
        OnPropertyChanged(nameof(EditTestsIntegrationTests));
        OnPropertyChanged(nameof(EditTestsManualTests));
        OnPropertyChanged(nameof(EditTestsRegressionTests));
        OnPropertyChanged(nameof(EditTestsBuildValidation));
    }

    private void SyncTestsToTechnicalNotes()
    {
        var parts = new System.Text.StringBuilder();
        AppendSection(parts, SecUnit,        _editTestsUnitTests);
        AppendSection(parts, SecIntegration, _editTestsIntegrationTests);
        AppendSection(parts, SecManual,      _editTestsManualTests);
        AppendSection(parts, SecRegression,  _editTestsRegressionTests);
        AppendSection(parts, SecBuild,       _editTestsBuildValidation);
        EditTechnicalNotes = parts.ToString().TrimEnd();
    }

    private void MarkTicketDirty()
    {
        if (_suppressTicketDirtyTracking)
            return;

        if (!HasDetail || !IsEditing)
            return;

        if (_hasUnsavedTicketChanges)
            return;

        _hasUnsavedTicketChanges = true;
        OnPropertyChanged(nameof(HasDirtyEditState));
        OnPropertyChanged(nameof(CanSaveTicket));
        SaveTicketCommand.NotifyCanExecuteChanged();
    }

    private void ClearTicketDirtyState()
    {
        if (!_hasUnsavedTicketChanges)
            return;

        _hasUnsavedTicketChanges = false;
        OnPropertyChanged(nameof(HasDirtyEditState));
        OnPropertyChanged(nameof(CanSaveTicket));
        SaveTicketCommand.NotifyCanExecuteChanged();
    }

    private void ClearTestSubFields()
    {
        _editTestsUnitTests        = string.Empty;
        _editTestsIntegrationTests = string.Empty;
        _editTestsManualTests      = string.Empty;
        _editTestsRegressionTests  = string.Empty;
        _editTestsBuildValidation  = string.Empty;
        OnPropertyChanged(nameof(EditTestsUnitTests));
        OnPropertyChanged(nameof(EditTestsIntegrationTests));
        OnPropertyChanged(nameof(EditTestsManualTests));
        OnPropertyChanged(nameof(EditTestsRegressionTests));
        OnPropertyChanged(nameof(EditTestsBuildValidation));
    }

    private static string ExtractSection(string raw, string header)
    {
        var start = raw.IndexOf(header, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        start += header.Length;
        // Skip leading newline
        while (start < raw.Length && raw[start] is '\r' or '\n') start++;
        // Find next section header
        var end = raw.Length;
        foreach (var h in _sectionHeaders)
        {
            if (h == header) continue;
            var idx = raw.IndexOf(h, start, StringComparison.Ordinal);
            if (idx >= 0 && idx < end) end = idx;
        }
        return raw[start..end].TrimEnd();
    }

    private static void AppendSection(System.Text.StringBuilder sb, string header, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        if (sb.Length > 0) sb.AppendLine();
        sb.AppendLine(header);
        sb.Append(content.TrimEnd());
    }
}
