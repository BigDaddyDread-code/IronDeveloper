using System;
using System.Collections.ObjectModel;
using System.IO;
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

        // ── Pre-index diagnostics (visible in Visual Studio Output → Debug) ───
        var path      = _currentProject.LocalPath;
        var pathExists = Directory.Exists(path);
        System.Diagnostics.Trace.WriteLine("╔══════════════════════════════════════════════╗");
        System.Diagnostics.Trace.WriteLine("  [Index] Starting Index Project");
        System.Diagnostics.Trace.WriteLine($"  ProjectId  : {_currentProject.Id}");
        System.Diagnostics.Trace.WriteLine($"  Name       : {_currentProject.Name}");
        System.Diagnostics.Trace.WriteLine($"  LocalPath  : [{path}]");
        System.Diagnostics.Trace.WriteLine($"  PathLength : {path.Length} chars");
        System.Diagnostics.Trace.WriteLine($"  PathExists : {pathExists}");
        if (pathExists)
        {
            var slnFiles = Directory.GetFiles(path, "*.sln*", SearchOption.TopDirectoryOnly);
            System.Diagnostics.Trace.WriteLine($"  SolutionFiles: {slnFiles.Length} ({string.Join(", ", slnFiles.Select(Path.GetFileName))})");
            var csFiles = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories).Take(5).ToArray();
            System.Diagnostics.Trace.WriteLine($"  First .cs files: {string.Join(" | ", csFiles.Select(Path.GetFileName))}");
            var csTotal = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories).Length;
            System.Diagnostics.Trace.WriteLine($"  Total .cs files (all dirs): {csTotal}");
        }
        System.Diagnostics.Trace.WriteLine("╚══════════════════════════════════════════════╝");

        try
        {
            var result = await _indexingService.IndexProjectAsync(_currentProject);
            sw.Stop();
            IndexingTime = $"{sw.Elapsed.TotalSeconds:F1}s";

            // Post-index diagnostics
            System.Diagnostics.Trace.WriteLine(
                $"[Index] Result: Scanned={result.FilesScanned} Added={result.FilesAdded} Updated={result.FilesUpdated} " +
                $"Unchanged={result.FilesUnchanged} Skipped={result.FilesSkipped} Stored={result.StoredFileCount} " +
                $"DirNotFound={result.DirectoryNotFound} Error=[{result.ErrorMessage ?? "none"}]");

            if (result.DirectoryNotFound)
            {
                Status    = $"❌ Path not found: [{path}]";
                FileCount = 0;
                return;
            }

            if (!string.IsNullOrEmpty(result.ErrorMessage) && result.StoredFileCount == 0)
            {
                Status    = $"⚠️ {result.ErrorMessage}";
                FileCount = 0;
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
            System.Diagnostics.Trace.WriteLine($"[Index] EXCEPTION: {ex}");
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
