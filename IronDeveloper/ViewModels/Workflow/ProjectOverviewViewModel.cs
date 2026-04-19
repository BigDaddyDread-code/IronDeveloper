using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Models;

namespace IronDev.Agent.ViewModels.Workflow;

public sealed partial class ProjectOverviewViewModel : ObservableObject
{
    private readonly global::IronDev.Services.ITicketService _ticketService;
    private readonly global::IronDev.Services.IProjectMemoryService _memoryService;
    private readonly global::IronDev.Services.ICodeIndexService _indexService;

    private global::IronDev.Data.Models.Project? _currentProject;

    [ObservableProperty] private string _projectName    = string.Empty;
    [ObservableProperty] private string _projectPath    = string.Empty;
    [ObservableProperty] private string _model          = "gpt-4o";
    [ObservableProperty] private string _status         = "Needs Index";
    [ObservableProperty] private string _description    = string.Empty;
    [ObservableProperty] private string _lastIndexed    = "Never";
    [ObservableProperty] private bool   _isIndexing;

    [ObservableProperty] private ObservableCollection<TicketItem>   _recentTickets   = [];
    [ObservableProperty] private ObservableCollection<DecisionItem> _recentDecisions = [];

    public ProjectOverviewViewModel(
        global::IronDev.Services.ITicketService ticketService,
        global::IronDev.Services.IProjectMemoryService memoryService,
        global::IronDev.Services.ICodeIndexService indexService)
    {
        _ticketService   = ticketService;
        _memoryService   = memoryService;
        _indexService    = indexService;
    }

    internal async Task LoadAsync(global::IronDev.Data.Models.Project project)
    {
        _currentProject = project;
        ProjectName     = project.Name;
        ProjectPath     = project.LocalPath ?? "No path configured";
        Description     = project.Description ?? string.Empty;
        
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_currentProject == null) return;

        // Load recent tickets
        RecentTickets.Clear();
        var tickets = await _ticketService.GetRecentTicketsAsync(_currentProject.Id, take: 5);
        foreach (var t in tickets)
        {
            RecentTickets.Add(new TicketItem 
            { 
                Id = t.Id, 
                Title = t.Title, 
                Status = t.Status,
                Priority = t.Priority 
            });
        }

        // Load recent decisions
        RecentDecisions.Clear();
        var decisions = await _memoryService.GetRecentDecisionsAsync(_currentProject.Id, take: 5);
        foreach (var d in decisions)
        {
            RecentDecisions.Add(new DecisionItem 
            { 
                Id = d.Id, 
                Title = d.Title, 
                Summary = d.Detail // Map Detail to Summary for UI
            });
        }

        // Load file counts for status
        var files = await _indexService.GetRecentFilesAsync(_currentProject.Id, take: 1);
        if (files.Any())
        {
            Status = "Ready";
            LastIndexed = files.First().LastIndexedDate.ToLocalTime().ToString("g");
        }
        else
        {
            Status = "Needs Index";
            LastIndexed = "Never";
        }
    }

    [RelayCommand]
    private async Task IndexNowAsync()
    {
        if (_currentProject == null || string.IsNullOrWhiteSpace(_currentProject.LocalPath))
        {
            Status = "Err: Invalid Path";
            return;
        }

        IsIndexing = true;
        Status     = "Indexing...";

        try
        {
            await _indexService.IndexDirectoryAsync(_currentProject.Id, _currentProject.LocalPath);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Status = $"Err: {ex.Message}";
        }
        finally
        {
            IsIndexing = false;
        }
    }
}
