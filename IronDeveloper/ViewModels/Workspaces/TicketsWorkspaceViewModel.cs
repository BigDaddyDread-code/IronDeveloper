using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Models;
using IronDev.Data.Models;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class TicketsWorkspaceViewModel : ObservableObject
{
    private readonly global::IronDev.Services.ITicketService _ticketService;

    private int _activeProjectId;
    private int? _activeTenantId;

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

    // Dropdown options
    public ObservableCollection<string> StatusOptions { get; } = ["Draft", "Todo", "In Progress", "Done", "Resolved"];
    public ObservableCollection<string> PriorityOptions { get; } = ["Low", "Medium", "High", "Critical"];
    public ObservableCollection<string> TypeOptions { get; } = ["Task", "Bug", "Feature", "Spike", "Chore"];

    public TicketsWorkspaceViewModel(global::IronDev.Services.ITicketService ticketService)
    {
        _ticketService = ticketService;
    }

    // ── Load ────────────────────────────────────────────────────────────────

    internal async Task LoadAsync(Project project)
    {
        _activeProjectId = project.Id;
        _activeTenantId = project.TenantId;
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

        LoadTicketIntoEditor(value);
    }

    private void LoadTicketIntoEditor(TicketItem item)
    {
        IsNewTicket = false;
        IsEditing = true;
        HasDetail = true;

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
        EditLinkedFilePaths      = item.LinkedFilePaths ?? string.Empty;
        EditLinkedSymbols        = item.LinkedSymbols ?? string.Empty;

        SaveStatus = string.Empty;
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

    // ── Cancel ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void CancelEdit()
    {
        if (SelectedTicket != null)
        {
            LoadTicketIntoEditor(SelectedTicket);
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
        IsEditing = false;
        IsNewTicket = false;
        HasDetail = false;
        EditId = 0;
        EditTitle = string.Empty;
        EditStatus = "Draft";
        EditPriority = "Medium";
        EditTicketType = "Task";
        EditSummary = string.Empty;
        EditBackground = string.Empty;
        EditProblem = string.Empty;
        EditAcceptanceCriteria = string.Empty;
        EditTechnicalNotes = string.Empty;
        EditLinkedFilePaths = string.Empty;
        EditLinkedSymbols = string.Empty;
        SaveStatus = string.Empty;
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
}
