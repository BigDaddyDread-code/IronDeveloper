using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using IronDev.Agent.Models;
using IronDev.Services;
using IronDev.Data.Models;

namespace IronDev.Agent.ViewModels;

public partial class OutputPanelViewModel : ObservableObject,
    IRecipient<StatusMessage>,
    IRecipient<TicketPreviewMessage>,
    IRecipient<TicketSelectedMessage>
{
    private readonly ITicketService _ticketService;
    private int _projectId = 1;
    private Guid _sessionId = Guid.NewGuid();

    [ObservableProperty]
    private string _statusText = "Ready";

    /// <summary>True when the preview is showing a newly generated ticket (vs a saved/loaded one).</summary>
    [ObservableProperty]
    private bool _isNewPreview;

    /// <summary>True when any ticket (new or saved) is loaded for display.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTicket), nameof(CanOpenWorkbench))]
    private TicketItem? _currentTicket;

    public bool HasTicket => CurrentTicket != null;

    // ── Structured display properties ──

    [ObservableProperty] private string _ticketTitle = string.Empty;
    [ObservableProperty] private string _ticketType = string.Empty;
    [ObservableProperty] private string _ticketPriority = string.Empty;
    [ObservableProperty] private string _ticketSummary = string.Empty;
    [ObservableProperty] private string _ticketBackground = string.Empty;
    [ObservableProperty] private string _ticketProblem = string.Empty;
    [ObservableProperty] private string _ticketAcceptanceCriteria = string.Empty;
    [ObservableProperty] private string _ticketTechnicalNotes = string.Empty;
    [ObservableProperty] private string _ticketStatus = string.Empty;
    [ObservableProperty] private string _ticketCreatedDate = string.Empty;

    public OutputPanelViewModel(ITicketService ticketService)
    {
        _ticketService = ticketService;
        WeakReferenceMessenger.Default.Register<StatusMessage>(this);
        WeakReferenceMessenger.Default.Register<TicketPreviewMessage>(this);
        WeakReferenceMessenger.Default.Register<TicketSelectedMessage>(this);
    }

    public void Receive(StatusMessage message)
    {
        StatusText = message.Status;
    }

    public void Receive(TicketPreviewMessage message)
    {
        LoadTicketFields(message.Ticket);
        IsNewPreview = true;
        StatusText = "Ticket Generated";
    }

    public void Receive(TicketSelectedMessage message)
    {
        LoadTicketFields(message.Ticket);
        IsNewPreview = false;
        StatusText = "Viewing Saved Ticket";
        OpenWorkbenchCommand.NotifyCanExecuteChanged();
    }

    private void LoadTicketFields(TicketItem ticket)
    {
        CurrentTicket = ticket;
        TicketTitle = ticket.Title;
        TicketType = ticket.TicketType;
        TicketPriority = ticket.Priority;
        TicketSummary = ticket.Summary ?? string.Empty;
        TicketBackground = ticket.Background ?? string.Empty;
        TicketProblem = ticket.Problem ?? string.Empty;
        TicketAcceptanceCriteria = ticket.AcceptanceCriteria ?? string.Empty;
        TicketTechnicalNotes = ticket.TechnicalNotes ?? string.Empty;
        TicketStatus = ticket.Status;
        TicketCreatedDate = ticket.CreatedDate == default
            ? string.Empty
            : ticket.CreatedDate.ToString("g");
    }

    [RelayCommand]
    public void ClearTicket()
    {
        CurrentTicket = null;
        IsNewPreview = false;
        TicketTitle = string.Empty;
        TicketType = string.Empty;
        TicketPriority = string.Empty;
        TicketSummary = string.Empty;
        TicketBackground = string.Empty;
        TicketProblem = string.Empty;
        TicketAcceptanceCriteria = string.Empty;
        TicketTechnicalNotes = string.Empty;
        TicketStatus = string.Empty;
        TicketCreatedDate = string.Empty;
        StatusText = "Ready";
    }

    [RelayCommand]
    public async Task SaveTicketAsync()
    {
        if (CurrentTicket == null) return;

        StatusText = "Saving...";
        try
        {
            var saveTitle = CurrentTicket.Title ?? "(Untitled)";
            if (saveTitle.Length > 200) saveTitle = saveTitle.Substring(0, 197) + "...";

            var id = await _ticketService.SaveTicketAsync(new ProjectTicket
            {
                ProjectId = _projectId,
                SessionId = _sessionId,
                Title = saveTitle,
                TicketType = CurrentTicket.TicketType ?? "Task",
                Priority = CurrentTicket.Priority ?? "Medium",
                Summary = CurrentTicket.Summary,
                Background = CurrentTicket.Background,
                Problem = CurrentTicket.Problem,
                AcceptanceCriteria = CurrentTicket.AcceptanceCriteria,
                TechnicalNotes = CurrentTicket.TechnicalNotes,
                Status = "Saved",
                Content = CurrentTicket.Content ?? string.Empty
            });

            StatusText = "Ticket Saved";
            IsNewPreview = false;
            TicketStatus = "Saved";
            if (CurrentTicket != null)
            {
                CurrentTicket.Id = id;
                CurrentTicket.Title = saveTitle;
                CurrentTicket.Status = "Saved";
            }

            WeakReferenceMessenger.Default.Send(new TicketSavedMessage(id));
            OpenWorkbenchCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($">>>>>>>>> SAVE ERROR: {ex}");
            StatusText = $"Error Saving: {ex.Message}";
        }
    }

    public bool CanOpenWorkbench => CurrentTicket != null && CurrentTicket.Id > 0;

    [RelayCommand(CanExecute = nameof(CanOpenWorkbench))]
    public void OpenWorkbench()
    {
        if (CurrentTicket != null && CurrentTicket.Id > 0)
        {
            WeakReferenceMessenger.Default.Send(new OpenWorkbenchMessage(CurrentTicket.Id));
        }
    }
}
