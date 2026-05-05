using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Models;
using IronDev.Data.Models;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class ImplementationPlansWorkspaceViewModel : ObservableObject
{
    private readonly global::IronDev.Services.IProjectMemoryService _memoryService;
    private readonly global::IronDev.Services.ILookupService _lookupService;

    private int _activeProjectId;

    // ── List panel ──────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<ImplementationPlanSummary> _plans = [];
    [ObservableProperty] private ImplementationPlanSummary? _selectedPlanSummary;

    // ── Detail/editor panel ─────────────────────────────────────────────────
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isNewPlan;
    [ObservableProperty] private bool _hasDetail;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _saveStatus = string.Empty;

    // Editable fields
    [ObservableProperty] private long   _editId;
    [ObservableProperty] private string _editTitle = string.Empty;
    [ObservableProperty] private string _editGoal = string.Empty;
    [ObservableProperty] private string _editScope = string.Empty;
    [ObservableProperty] private string _editProposedSteps = string.Empty;
    [ObservableProperty] private string _editAffectedContext = string.Empty;
    [ObservableProperty] private string _editRisksNotes = string.Empty;
    [ObservableProperty] private string _editStatus = "Draft";
    [ObservableProperty] private string _editLinkedFilePaths = string.Empty;
    [ObservableProperty] private string _editLinkedSymbols = string.Empty;

    // Dropdown options
    public ObservableCollection<string> StatusOptions { get; } = ["Draft", "Reviewed", "Approved", "In Progress", "Completed", "Abandoned"];

    public ImplementationPlansWorkspaceViewModel(
        global::IronDev.Services.IProjectMemoryService memoryService,
        global::IronDev.Services.ILookupService lookupService)
    {
        _memoryService = memoryService;
        _lookupService = lookupService;
    }

    // ── Load ────────────────────────────────────────────────────────────────

    internal async Task LoadAsync(Project project)
    {
        _activeProjectId = project.Id;
        await RefreshListAsync();
    }

    private async Task RefreshListAsync()
    {
        Plans.Clear();
        ClearEditor();

        var plans = await _memoryService.GetRecentPlansAsync(_activeProjectId, take: 50);
        foreach (var p in plans)
        {
            Plans.Add(new ImplementationPlanSummary
            {
                Id = p.Id,
                Title = p.Title,
                Goal = p.Goal,
                CreatedDate = p.CreatedDate
            });
        }
    }

    // ── Selection ───────────────────────────────────────────────────────────

    partial void OnSelectedPlanSummaryChanged(ImplementationPlanSummary? value)
    {
        if (value == null)
        {
            ClearEditor();
            return;
        }

        _ = LoadPlanIntoEditorAsync(value.Id);
    }

    private async Task LoadPlanIntoEditorAsync(long planId)
    {
        var plan = await _memoryService.GetPlanByIdAsync(planId);
        if (plan == null) return;

        IsNewPlan = false;
        IsEditing = true;
        HasDetail = true;

        EditId               = plan.Id;
        EditTitle            = plan.Title;
        EditGoal             = plan.Goal;
        EditScope            = plan.Scope ?? string.Empty;
        EditProposedSteps    = plan.ProposedSteps ?? string.Empty;
        EditAffectedContext  = plan.AffectedContext ?? string.Empty;
        EditRisksNotes       = plan.RisksNotes ?? string.Empty;
        EditStatus           = plan.Status;
        EditLinkedFilePaths  = plan.LinkedFilePaths ?? string.Empty;
        EditLinkedSymbols    = plan.LinkedSymbols ?? string.Empty;

        SaveStatus = string.Empty;
    }

    // ── New Plan ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void NewPlan()
    {
        SelectedPlanSummary = null;
        IsNewPlan = true;
        IsEditing = true;
        HasDetail = true;

        EditId              = 0;
        EditTitle           = string.Empty;
        EditGoal            = string.Empty;
        EditScope           = string.Empty;
        EditProposedSteps   = string.Empty;
        EditAffectedContext = string.Empty;
        EditRisksNotes      = string.Empty;
        EditStatus          = "Draft";
        EditLinkedFilePaths = string.Empty;
        EditLinkedSymbols   = string.Empty;

        SaveStatus = string.Empty;
    }

    // ── Save ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SavePlanAsync()
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
            var plan = new ProjectImplementationPlan
            {
                Id                     = EditId,
                ProjectId              = _activeProjectId,
                Title                  = EditTitle.Trim(),
                Goal                   = EditGoal.Trim(),
                Scope                  = string.IsNullOrWhiteSpace(EditScope) ? null : EditScope.Trim(),
                ProposedSteps          = string.IsNullOrWhiteSpace(EditProposedSteps) ? null : EditProposedSteps.Trim(),
                AffectedContext        = string.IsNullOrWhiteSpace(EditAffectedContext) ? null : EditAffectedContext.Trim(),
                RisksNotes             = string.IsNullOrWhiteSpace(EditRisksNotes) ? null : EditRisksNotes.Trim(),
                Status                 = EditStatus,
                LinkedFilePaths        = string.IsNullOrWhiteSpace(EditLinkedFilePaths) ? null : EditLinkedFilePaths.Trim(),
                LinkedSymbols          = string.IsNullOrWhiteSpace(EditLinkedSymbols) ? null : EditLinkedSymbols.Trim()
            };

            var savedId = await _memoryService.SavePlanAsync(plan);
            EditId = savedId;
            IsNewPlan = false;

            SaveStatus = "Saved ✓";
            await RefreshListAsync();

            // Re-select the saved plan
            SelectedPlanSummary = Plans.FirstOrDefault(p => p.Id == savedId);
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

    // ── Cancel ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void CancelEdit()
    {
        if (SelectedPlanSummary != null)
        {
            _ = LoadPlanIntoEditorAsync(SelectedPlanSummary.Id);
        }
        else
        {
            ClearEditor();
        }
    }

    // ── Prefill from chat (called by ShellViewModel) ────────────────────────

    public void PrefillFromChat(
        string  title,
        string  goal,
        string? steps,
        string? linkedFilePaths,
        string? linkedSymbols,
        string? scope      = null,
        string? risksNotes = null)
    {
        NewPlan();
        EditTitle           = title;
        EditGoal            = goal;
        EditScope           = scope          ?? string.Empty;
        EditProposedSteps   = steps          ?? string.Empty;
        EditAffectedContext = string.Empty;   // caller can set via linked files context if needed
        EditRisksNotes      = risksNotes     ?? string.Empty;
        EditLinkedFilePaths = linkedFilePaths ?? string.Empty;
        EditLinkedSymbols   = linkedSymbols  ?? string.Empty;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void ClearEditor()
    {
        IsEditing = false;
        IsNewPlan = false;
        HasDetail = false;
        EditId = 0;
        EditTitle = string.Empty;
        EditGoal = string.Empty;
        EditScope = string.Empty;
        EditProposedSteps = string.Empty;
        EditAffectedContext = string.Empty;
        EditRisksNotes = string.Empty;
        EditStatus = "Draft";
        EditLinkedFilePaths = string.Empty;
        EditLinkedSymbols = string.Empty;
        SaveStatus = string.Empty;
    }
}
