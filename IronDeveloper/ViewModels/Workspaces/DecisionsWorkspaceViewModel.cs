using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Models;
using IronDev.Data.Models;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class DecisionsWorkspaceViewModel : ObservableObject
{
    private readonly global::IronDev.Services.IProjectMemoryService _memoryService;
    private readonly global::IronDev.Services.ILookupService _lookupService;

    private int _activeProjectId;

    // ── List panel ──────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<DecisionItem> _decisions = [];
    [ObservableProperty] private DecisionItem? _selectedDecision;

    // ── Detail/editor panel ─────────────────────────────────────────────────
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isNewDecision;
    [ObservableProperty] private bool _hasDetail;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _saveStatus = string.Empty;

    // Editable fields
    [ObservableProperty] private long   _editId;
    [ObservableProperty] private string _editTitle = string.Empty;
    [ObservableProperty] private string _editDetail = string.Empty;
    [ObservableProperty] private string _editReason = string.Empty;
    [ObservableProperty] private string _editCategory = "Architecture";
    [ObservableProperty] private string _editStatus = "Accepted";
    [ObservableProperty] private string _editLinkedFilePaths = string.Empty;
    [ObservableProperty] private string _editLinkedSymbols = string.Empty;

    // Dropdown options (loaded from SQL lookup tables)
    public ObservableCollection<string> StatusOptions { get; } = [];
    public ObservableCollection<string> CategoryOptions { get; } = [];

    public DecisionsWorkspaceViewModel(
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
        await LoadLookupsAsync();
        await RefreshListAsync();
    }

    private async Task LoadLookupsAsync()
    {
        try
        {
            var categories = await _lookupService.GetDecisionCategoriesAsync();
            var statuses = await _lookupService.GetDecisionStatusesAsync();

            CategoryOptions.Clear();
            foreach (var c in categories)
                CategoryOptions.Add(c.Name);

            StatusOptions.Clear();
            foreach (var s in statuses)
                StatusOptions.Add(s.Name);
        }
        catch
        {
            // Fallback if lookup tables don't exist yet
            if (CategoryOptions.Count == 0)
            {
                foreach (var c in new[] { "Architecture", "Code Standards", "Product", "Data", "Infrastructure",
                    "AI / Prompting", "UX / UI", "Workflow / Process", "Integration", "Security" })
                    CategoryOptions.Add(c);
            }
            if (StatusOptions.Count == 0)
            {
                foreach (var s in new[] { "Proposed", "Accepted", "Superseded", "Rejected" })
                    StatusOptions.Add(s);
            }
        }
    }

    private async Task RefreshListAsync()
    {
        Decisions.Clear();
        ClearEditor();

        var decisions = await _memoryService.GetRecentDecisionsAsync(_activeProjectId, take: 50);
        foreach (var d in decisions)
        {
            Decisions.Add(MapToItem(d));
        }
    }

    // ── Selection ───────────────────────────────────────────────────────────

    partial void OnSelectedDecisionChanged(DecisionItem? value)
    {
        if (value == null)
        {
            ClearEditor();
            return;
        }

        LoadDecisionIntoEditor(value);
    }

    private void LoadDecisionIntoEditor(DecisionItem item)
    {
        IsNewDecision = false;
        IsEditing = true;
        HasDetail = true;

        EditId               = item.Id;
        EditTitle            = item.Title;
        EditDetail           = item.Detail;
        EditReason           = item.Reason ?? string.Empty;
        EditCategory         = item.Category ?? "Architecture";
        EditStatus           = item.Status;
        EditLinkedFilePaths  = item.LinkedFilePaths ?? string.Empty;
        EditLinkedSymbols    = item.LinkedSymbols ?? string.Empty;

        SaveStatus = string.Empty;
    }

    // ── New Decision ────────────────────────────────────────────────────────

    [RelayCommand]
    private void NewDecision()
    {
        SelectedDecision = null;
        IsNewDecision = true;
        IsEditing = true;
        HasDetail = true;

        EditId              = 0;
        EditTitle           = string.Empty;
        EditDetail          = string.Empty;
        EditReason          = string.Empty;
        EditCategory        = "Architecture";
        EditStatus          = "Accepted";
        EditLinkedFilePaths = string.Empty;
        EditLinkedSymbols   = string.Empty;

        SaveStatus = string.Empty;
    }

    // ── Save ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveDecisionAsync()
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
            var decision = new ProjectDecision
            {
                Id                     = EditId,
                ProjectId              = _activeProjectId,
                Title                  = EditTitle.Trim(),
                Detail                 = EditDetail.Trim(),
                Reason                 = string.IsNullOrWhiteSpace(EditReason) ? null : EditReason.Trim(),
                Category               = EditCategory,
                Status                 = EditStatus,
                LinkedFilePaths        = string.IsNullOrWhiteSpace(EditLinkedFilePaths) ? null : EditLinkedFilePaths.Trim(),
                LinkedCodeIndexEntryIds = null,
                LinkedSymbols          = string.IsNullOrWhiteSpace(EditLinkedSymbols) ? null : EditLinkedSymbols.Trim()
            };

            var savedId = await _memoryService.SaveDecisionAsync(decision);
            EditId = savedId;
            IsNewDecision = false;

            SaveStatus = "Saved ✓";
            await RefreshListAsync();

            // Re-select the saved decision
            SelectedDecision = Decisions.FirstOrDefault(d => d.Id == savedId);
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
        if (SelectedDecision != null)
        {
            LoadDecisionIntoEditor(SelectedDecision);
        }
        else
        {
            ClearEditor();
        }
    }

    // ── Prefill from chat (called by ShellViewModel) ────────────────────────

    /// <summary>
    /// Sets up a new decision editor with pre-filled data from chat context.
    /// </summary>
    public void PrefillFromChat(string title, string detail, string? linkedFilePaths, string? linkedSymbols)
    {
        NewDecision();
        EditTitle           = title;
        EditDetail          = detail;
        EditLinkedFilePaths = linkedFilePaths ?? string.Empty;
        EditLinkedSymbols   = linkedSymbols ?? string.Empty;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void ClearEditor()
    {
        IsEditing = false;
        IsNewDecision = false;
        HasDetail = false;
        EditId = 0;
        EditTitle = string.Empty;
        EditDetail = string.Empty;
        EditReason = string.Empty;
        EditCategory = "Architecture";
        EditStatus = "Accepted";
        EditLinkedFilePaths = string.Empty;
        EditLinkedSymbols = string.Empty;
        SaveStatus = string.Empty;
    }

    private static DecisionItem MapToItem(ProjectDecision d) => new()
    {
        Id                     = d.Id,
        Title                  = d.Title,
        Detail                 = d.Detail,
        Reason                 = d.Reason,
        Category               = d.Category,
        Status                 = d.Status,
        LinkedFilePaths        = d.LinkedFilePaths,
        LinkedCodeIndexEntryIds = d.LinkedCodeIndexEntryIds,
        LinkedSymbols          = d.LinkedSymbols,
        CreatedDate            = d.CreatedDate
    };
}
