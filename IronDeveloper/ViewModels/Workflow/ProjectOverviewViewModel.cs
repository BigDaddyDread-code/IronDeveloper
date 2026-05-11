using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Models;
using IronDev.Agent.Services;

namespace IronDev.Agent.ViewModels.Workflow;

public sealed partial class ProjectOverviewViewModel : ObservableObject
{
    private readonly global::IronDev.Services.ITicketService _ticketService;
    private readonly global::IronDev.Services.IProjectMemoryService _memoryService;
    private readonly global::IronDev.Agent.Services.Interfaces.ILocalIndexingService _indexingService;

    private global::IronDev.Data.Models.Project? _currentProject;

    [ObservableProperty] private string _projectName    = string.Empty;
    [ObservableProperty] private string _projectPath    = string.Empty;
    [ObservableProperty] private string _model          = "gpt-4o";
    [ObservableProperty] private string _status         = "Needs Index";
    [ObservableProperty] private string _description    = string.Empty;
    [ObservableProperty] private string _lastIndexed    = "Never";
    [ObservableProperty] private string _indexingTime   = "-";
    [ObservableProperty] private int    _fileCount;
    [ObservableProperty] private bool   _isIndexing;

    // ── Session context (set by ShellViewModel on project activation) ─────────
    [ObservableProperty] private string _currentUserDisplayName = string.Empty;
    [ObservableProperty] private string _currentUserEmail       = string.Empty;
    [ObservableProperty] private string _currentTenantName      = string.Empty;
    [ObservableProperty] private string _currentWorkspaceName   = string.Empty;

    [ObservableProperty] private ObservableCollection<TicketItem>   _recentTickets   = [];
    [ObservableProperty] private ObservableCollection<DecisionItem> _recentDecisions = [];

    // ── Derived state card properties ─────────────────────────────────────────
    public string LastTicketTitle    => RecentTickets.Count > 0   ? RecentTickets[0].Title   : "None yet";
    public string LastDecisionTitle  => RecentDecisions.Count > 0 ? RecentDecisions[0].Title : "None yet";

    public ProjectOverviewViewModel(
        global::IronDev.Services.ITicketService ticketService,
        global::IronDev.Services.IProjectMemoryService memoryService,
        global::IronDev.Agent.Services.Interfaces.ILocalIndexingService indexingService)
    {
        _ticketService   = ticketService;
        _memoryService   = memoryService;
        _indexingService = indexingService;
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
                Detail = d.Detail 
            });
        }

        // Use LastIndexedUtc from project if available
        if (_currentProject.LastIndexedUtc.HasValue)
        {
            Status = _currentProject.IndexingStatus ?? "Ready";
            LastIndexed = _currentProject.LastIndexedUtc.Value.ToLocalTime().ToString("g");
        }
        else
        {
            Status = "Needs Index";
            LastIndexed = "Never";
        }

        // Fetch real indexed file count
        try
        {
            FileCount = await _indexingService.GetIndexedFileCountAsync(_currentProject.Id);
        }
        catch (Exception)
        {
            FileCount = 0;
        }
    }

    [RelayCommand]
    private async Task IndexProjectAsync()
    {
        if (_currentProject == null || string.IsNullOrWhiteSpace(_currentProject.LocalPath))
        {
            Status = "Err: No path configured";
            return;
        }

        IsIndexing = true;
        Status     = "Indexing…";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await _indexingService.IndexProjectAsync(_currentProject);
            sw.Stop();
            IndexingTime = $"{sw.Elapsed.TotalSeconds:F1}s";

            if (result.DirectoryNotFound)
            {
                // Path didn't exist — status already updated in DB by CodeIndexService
                Status    = $"❌ Path not found: {_currentProject.LocalPath}";
                FileCount = 0;
                return;
            }

            if (!string.IsNullOrEmpty(result.ErrorMessage) && result.StoredFileCount == 0)
            {
                Status    = $"⚠️ {result.ErrorMessage}";
                FileCount = 0;
                // Reload so LastIndexed shows correctly
                await RefreshAsync();
                return;
            }

            // Success — use StoredFileCount (actual DB rows) not FilesScanned (just disk walk)
            FileCount = result.StoredFileCount;

            // Reload from DB so that LastIndexedUtc, IndexingStatus come from the DB update
            // performed by CodeIndexService, not from a manual local mutation
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            sw.Stop();
            Status = $"❌ Index failed: {ex.Message}";
        }
        finally
        {
            IsIndexing = false;
        }
    }

    [RelayCommand]
    private void ViewDetails()
    {
        // Placeholder for future navigation to an "Index Details" screen
    }
}
