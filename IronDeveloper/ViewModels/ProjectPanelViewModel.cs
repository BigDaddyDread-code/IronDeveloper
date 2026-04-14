using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using IronDev.Agent.Models;
using IronDev.Services;

namespace IronDev.Agent.ViewModels;

public partial class ProjectPanelViewModel : ObservableObject,
    IRecipient<TicketSavedMessage>,
    IRecipient<DecisionSavedMessage>,
    IRecipient<SummaryUpdatedMessage>
{
    private readonly IProjectMemoryService _projectMemoryService;
    private readonly ITicketService _ticketService;
    private readonly ICodeIndexService _codeIndexService;
    private int _projectId = 1;

    [ObservableProperty]
    private string _selectedProject = "IronDev";
    
    [ObservableProperty]
    private string _localPath = @"c:\Users\bob\source\repos\AIDeveloper\IronDeveloper";

    [ObservableProperty]
    private string _projectSummary = "Loading...";

    public ObservableCollection<DecisionItem> Decisions { get; } = new();
    public ObservableCollection<TicketItem> Tickets { get; } = new();

    [ObservableProperty]
    private TicketItem? _selectedTicket;

    public ProjectPanelViewModel(
        IProjectMemoryService projectMemoryService,
        ITicketService ticketService,
        ICodeIndexService codeIndexService)
    {
        _projectMemoryService = projectMemoryService;
        _ticketService = ticketService;
        _codeIndexService = codeIndexService;

        WeakReferenceMessenger.Default.Register<TicketSavedMessage>(this);
        WeakReferenceMessenger.Default.Register<DecisionSavedMessage>(this);
        WeakReferenceMessenger.Default.Register<SummaryUpdatedMessage>(this);
    }

    public void Receive(TicketSavedMessage message)
    {
        _ = LoadTicketsAsync();
    }

    public void Receive(DecisionSavedMessage message)
    {
        _ = LoadDecisionsAsync();
    }

    public void Receive(SummaryUpdatedMessage message)
    {
        _ = LoadSummaryAsync();
    }

    [RelayCommand]
    public async Task LoadMemoryAsync()
    {
        await LoadSummaryAsync();
        await LoadDecisionsAsync();
        await LoadTicketsAsync();
    }

    private async Task LoadSummaryAsync()
    {
        var summary = await _projectMemoryService.GetLatestSummaryAsync(_projectId);
        ProjectSummary = summary != null ? summary.Summary : "No summary available.";
    }

    private async Task LoadDecisionsAsync()
    {
        var decisions = await _projectMemoryService.GetRecentDecisionsAsync(_projectId);
        Decisions.Clear();
        foreach (var d in decisions)
        {
            Decisions.Add(new DecisionItem { Title = d.Title, Detail = d.Detail });
        }
    }

    [RelayCommand]
    public async Task LoadTicketsAsync()
    {
        var tickets = await _ticketService.GetRecentTicketsAsync(_projectId, 5);
        Tickets.Clear();
        foreach (var t in tickets)
        {
            Tickets.Add(new TicketItem
            {
                Id = t.Id,
                Title = !string.IsNullOrWhiteSpace(t.Title) ? t.Title : ExtractTitle(t.Content),
                TicketType = t.TicketType,
                Priority = t.Priority,
                Summary = t.Summary,
                Background = t.Background,
                Problem = t.Problem,
                AcceptanceCriteria = t.AcceptanceCriteria,
                TechnicalNotes = t.TechnicalNotes,
                Status = t.Status,
                Content = t.Content,
                CreatedDate = t.CreatedDate
            });
        }
    }

    [RelayCommand]
    public void SelectTicket(TicketItem? ticket)
    {
        if (ticket == null) return;
        SelectedTicket = ticket;
        WeakReferenceMessenger.Default.Send(new TicketSelectedMessage(ticket));
    }

    /// <summary>Fallback title extraction for old tickets that lack a Title field.</summary>
    private static string ExtractTitle(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "(Untitled)";

        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return "(Untitled)";

        var first = lines[0].TrimStart('#', ' ', '-', '*');
        return first.Length > 60 ? first.Substring(0, 57) + "..." : first;
    }

    [RelayCommand]
    public async Task IndexProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(LocalPath))
        {
            WeakReferenceMessenger.Default.Send(new StatusMessage("No local path specified"));
            return;
        }

        WeakReferenceMessenger.Default.Send(new StatusMessage("Indexing project files..."));
        
        try
        {
            var result = await _codeIndexService.IndexDirectoryAsync(_projectId, LocalPath);
            WeakReferenceMessenger.Default.Send(new StatusMessage($"Indexed {result.FilesAdded + result.FilesUpdated} files ({result.FilesUnchanged} unchanged)"));
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new StatusMessage($"Index error: {ex.Message}"));
        }
    }
}
