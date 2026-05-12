using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Models;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Data.Models;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class TicketsWorkspaceViewModel : ObservableObject
{
    private readonly global::IronDev.Services.ITicketService         _ticketService;
    private readonly global::IronDev.Services.IProjectMemoryService  _memoryService;
    private readonly ITicketBuildOrchestrator                        _orchestrator;
    private readonly IDraftTicketService                             _draftService;
    private readonly ICodebaseTicketGeneratorService                 _generatorService;

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

    public string EditTestsUnitTests
    {
        get => _editTestsUnitTests;
        set { if (SetProperty(ref _editTestsUnitTests, value)) SyncTestsToTechnicalNotes(); }
    }
    public string EditTestsIntegrationTests
    {
        get => _editTestsIntegrationTests;
        set { if (SetProperty(ref _editTestsIntegrationTests, value)) SyncTestsToTechnicalNotes(); }
    }
    public string EditTestsManualTests
    {
        get => _editTestsManualTests;
        set { if (SetProperty(ref _editTestsManualTests, value)) SyncTestsToTechnicalNotes(); }
    }
    public string EditTestsRegressionTests
    {
        get => _editTestsRegressionTests;
        set { if (SetProperty(ref _editTestsRegressionTests, value)) SyncTestsToTechnicalNotes(); }
    }
    public string EditTestsBuildValidation
    {
        get => _editTestsBuildValidation;
        set { if (SetProperty(ref _editTestsBuildValidation, value)) SyncTestsToTechnicalNotes(); }
    }

    /// <summary>Full formatted test plan — used by AI builder context.</summary>
    public string FullTestPlan => EditTechnicalNotes;

    // CommunityToolkit.Mvvm partial callback — called whenever [ObservableProperty]
    // EditTechnicalNotes is set (including direct assignment from load methods).
    partial void OnEditTechnicalNotesChanged(string value)
    {
        SyncTechnicalNotesToTests();
    }

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
    [ObservableProperty] private bool               _isBuildingTicket;
    [ObservableProperty] private bool               _hasBuildPreview;
    [ObservableProperty] private TicketBuildPreview? _currentBuildPreview;
    [ObservableProperty] private TicketBuildResult?  _currentBuildResult;
    [ObservableProperty] private string              _buildStatusMessage = string.Empty;

    /// <summary>True when the Build This button should be enabled.</summary>
    public bool CanBuildTicket =>
        SelectedTicket != null
        && !string.IsNullOrWhiteSpace(EditTitle)
        && !string.IsNullOrWhiteSpace(_activeProjectPath)
        && !IsBuildingTicket
        && !IsDraftMode;   // Build This is unavailable while reviewing a draft

    // ── Draft Ticket state ────────────────────────────────────────────────────
    [ObservableProperty] private bool       _isDraftMode;
    [ObservableProperty] private bool       _isDraftGenerating;
    [ObservableProperty] private string     _draftStatusMessage = string.Empty;
    [ObservableProperty] private DraftTicket? _currentDraft;

    // ── Codebase Ticket Generation state ──────────────────────────────────────
    [ObservableProperty] private bool _isGeneratingCodebaseTickets;


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

    // Dropdown options
    public ObservableCollection<string> StatusOptions   { get; } = ["Draft", "Todo", "In Progress", "Done", "Resolved"];
    public ObservableCollection<string> PriorityOptions { get; } = ["Low", "Medium", "High", "Critical"];
    public ObservableCollection<string> TypeOptions     { get; } = ["Task", "Bug", "Feature", "Spike", "Chore"];

    public TicketsWorkspaceViewModel(
        global::IronDev.Services.ITicketService        ticketService,
        global::IronDev.Services.IProjectMemoryService memoryService,
        ITicketBuildOrchestrator                       orchestrator,
        IDraftTicketService                            draftService,
        ICodebaseTicketGeneratorService                generatorService)
    {
        _ticketService    = ticketService;
        _memoryService    = memoryService;
        _orchestrator     = orchestrator;
        _draftService     = draftService;
        _generatorService = generatorService;
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
            DraftStatusMessage = "Draft Codebase Ticket";
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
    }

    private void LoadTicketIntoEditor(TicketItem item)
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

    private async Task LoadTicketIntoEditorAsync(TicketItem item)
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

        // Load plan for this ticket
        await RefreshPlanAsync(item.Id);
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
    }

    // ── Save ────────────────────────────────────────────────────────────────

    [RelayCommand]
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
        }
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

    // ── Ask about this plan ──────────────────────────────────────────────────

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

        if (DraftPreflight == DraftPreflightState.Indexing)
        {
            if (IsProjectIndexed)
            {
                // Happy path: indexing completed, auto-generate the draft.
                if (_shouldGenerateDraftAfterIndex && _pendingChatContext != null)
                {
                    IsDraftIndexing               = false;
                    _shouldGenerateDraftAfterIndex = false;
                    DraftPreflightMessage         = string.Empty;
                    var ctx = _pendingChatContext;
                    // Fire-and-forget on the UI thread; GeneratePendingDraftAsync transitions
                    // DraftPreflight to None and sets HasDetail=true when it completes.
                    _ = GeneratePendingDraftAsync(ctx);
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
            DraftPreflight      = DraftPreflightState.NeedsChoice;
            HasDetail           = false;   // don't show the editor yet
            return;
        }

        // Project is indexed — proceed immediately.
        await GeneratePendingDraftAsync(ctx);
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

        try
        {
            var draft = await _draftService.GenerateDraftAsync(
                _activeProjectId,
                _activeProjectName,
                ctx.ProposedTitle,
                ctx.MessageText,
                ctx.LinkedFilePaths,
                ctx.LinkedSymbols);

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
    [RelayCommand]
    private async Task PreflightContinueAsync()
    {
        if (_pendingChatContext == null || IsDraftIndexing) return;
        var ctx = _pendingChatContext;
        await GeneratePendingDraftAsync(ctx);
    }

    /// <summary>
    /// Preflight: "Index Project First" — disables Continue/Index buttons, shows indexing progress
    /// text, and fires OnRequestIndex. When ShellViewModel calls SetIndexStatus("Ready"), the
    /// pending draft is auto-generated. If indexing fails, state falls back to IndexFailed.
    /// </summary>
    [RelayCommand]
    private void PreflightIndexProject()
    {
        if (_pendingChatContext == null) return;

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

        var created = Tickets.FirstOrDefault(t => t.Id == savedId);
        if (created != null)
        {
            await LoadTicketIntoEditorAsync(created);
            SelectedTicket = created;
        }

        SaveStatus = "Ticket created \u2713";
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
                CurrentDraft.LinkedSymbols);

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
        if (IsGeneratingCodebaseTickets) return;

        IsGeneratingCodebaseTickets = true;
        DraftStatusMessage = "Analyzing codebase...";

        try
        {
            // Remove any existing (unsaved) drafts from the list first
            var existingDrafts = Tickets.Where(t => t.IsDraft).ToList();
            foreach (var d in existingDrafts) Tickets.Remove(d);

            var result = await _generatorService.GenerateTicketsAsync(_activeProjectId);
            if (result.Success)
            {
                foreach (var draft in result.Drafts)
                {
                    var item = new TicketItem
                    {
                        Id                 = 0,
                        Title              = draft.Title,
                        Summary            = draft.Summary,
                        Background         = draft.Background,
                        AcceptanceCriteria = draft.AcceptanceCriteria,
                        Priority           = draft.Priority,
                        TicketType         = draft.TicketType,
                        IsDraft            = true,
                        Status             = "Draft",
                        TechnicalNotes     = PackTechnicalNotes(draft)
                    };
                    Tickets.Insert(0, item);
                }

                DraftStatusMessage = $"Generated {result.Drafts.Count} codebase improvement drafts.";
                
                // Select the first one to start review
                if (Tickets.Count > 0 && Tickets[0].IsDraft)
                {
                    SelectedTicket = Tickets[0];
                }
            }
            else
            {
                DraftStatusMessage = $"Generation failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            DraftStatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsGeneratingCodebaseTickets = false;
        }
    }

    private string PackTechnicalNotes(CodebaseTicketDraft draft)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(draft.UnitTests))        { sb.AppendLine(SecUnit); sb.AppendLine(draft.UnitTests); }
        if (!string.IsNullOrWhiteSpace(draft.IntegrationTests)) { sb.AppendLine(SecIntegration); sb.AppendLine(draft.IntegrationTests); }
        if (!string.IsNullOrWhiteSpace(draft.ManualTests))      { sb.AppendLine(SecManual); sb.AppendLine(draft.ManualTests); }
        if (!string.IsNullOrWhiteSpace(draft.RegressionTests))  { sb.AppendLine(SecRegression); sb.AppendLine(draft.RegressionTests); }
        if (!string.IsNullOrWhiteSpace(draft.BuildValidation))  { sb.AppendLine(SecBuild); sb.AppendLine(draft.BuildValidation); }
        return sb.ToString();
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
                ContextSummary         = CurrentDraft?.Summary, // Or from build preview if generated
                IsGenerated            = true,
                GenerationNote         = CurrentDraft?.GenerationNote
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
