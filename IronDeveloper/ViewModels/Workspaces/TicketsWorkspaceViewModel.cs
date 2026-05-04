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

    private int    _activeProjectId;
    private int?   _activeTenantId;
    private string _activeProjectPath = string.Empty;

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
    partial void OnEditTechnicalNotesChanged(string value) => SyncTechnicalNotesToTests();

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
        && !IsBuildingTicket;

    // Dropdown options
    public ObservableCollection<string> StatusOptions   { get; } = ["Draft", "Todo", "In Progress", "Done", "Resolved"];
    public ObservableCollection<string> PriorityOptions { get; } = ["Low", "Medium", "High", "Critical"];
    public ObservableCollection<string> TypeOptions     { get; } = ["Task", "Bug", "Feature", "Spike", "Chore"];

    public TicketsWorkspaceViewModel(
        global::IronDev.Services.ITicketService        ticketService,
        global::IronDev.Services.IProjectMemoryService memoryService,
        ITicketBuildOrchestrator                       orchestrator)
    {
        _ticketService = ticketService;
        _memoryService = memoryService;
        _orchestrator  = orchestrator;
    }

    // ── Load ────────────────────────────────────────────────────────────────

    internal async Task LoadAsync(Project project)
    {
        _activeProjectId   = project.Id;
        _activeTenantId    = project.TenantId;
        _activeProjectPath = project.LocalPath ?? string.Empty;
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

        // Clear stale build state when switching tickets
        ClearBuildState();
        _ = LoadTicketIntoEditorAsync(value);
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
        SyncTechnicalNotesToTests();
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
                LinkedSymbols          = string.IsNullOrWhiteSpace(EditLinkedSymbols) ? null : EditLinkedSymbols.Trim()
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
    private void CreatePlan()
    {
        if (SelectedTicket == null) return;
        HasPlan = true;
        PlanId = 0;
        PlanTitle = $"Implementation Plan for {SelectedTicket.Title}";
        PlanGoal = SelectedTicket.Summary ?? string.Empty;
        ActiveTab = TicketDetailTab.ImplementationPlan;
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

    // ── Build Ticket (Phase 2 — real context assembly via orchestrator) ────────

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

            BuildStatusMessage = preview.IsEmpty
                ? "No changes proposed."
                : "AI proposal ready. Review and approve to apply.";
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
