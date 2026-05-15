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
    private readonly global::IronDev.Core.Interfaces.IProjectProfileService _profileService;

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
    [ObservableProperty] private string _lastIndexingDetails = string.Empty;

    // ── Session context (set by ShellViewModel on project activation) ─────────
    [ObservableProperty] private string _currentUserDisplayName = string.Empty;
    [ObservableProperty] private string _currentUserEmail       = string.Empty;
    [ObservableProperty] private string _currentTenantName      = string.Empty;
    [ObservableProperty] private string _currentWorkspaceName   = string.Empty;

    [ObservableProperty] private ObservableCollection<TicketItem>   _recentTickets   = [];
    [ObservableProperty] private ObservableCollection<DecisionItem> _recentDecisions = [];

    // ── Project Profile ───────────────────────────────────────────────────────
    [ObservableProperty] private string _profileApplicationType = string.Empty;
    [ObservableProperty] private string _profilePrimaryLanguage = string.Empty;
    [ObservableProperty] private string _profileFramework = string.Empty;
    [ObservableProperty] private string _profileDatabaseEngine = string.Empty;
    [ObservableProperty] private string _profileDataAccessStyle = string.Empty;
    [ObservableProperty] private string _profileTestFramework = string.Empty;
    [ObservableProperty] private string _profileSolutionFile = string.Empty;
    [ObservableProperty] private string _profileBuildCommand = string.Empty;
    [ObservableProperty] private string _profileTestCommand = string.Empty;
    [ObservableProperty] private string _profileSafeWriteRoot = string.Empty;
    [ObservableProperty] private bool _profileAllowBuilderApply;

    [ObservableProperty] private string _profileSaveStatus = string.Empty;

    // ── Derived state card properties ─────────────────────────────────────────
    public string LastTicketTitle    => RecentTickets.Count > 0   ? RecentTickets[0].Title   : "None yet";
    public string LastDecisionTitle  => RecentDecisions.Count > 0 ? RecentDecisions[0].Title : "None yet";

    public ProjectOverviewViewModel(
        global::IronDev.Services.ITicketService ticketService,
        global::IronDev.Services.IProjectMemoryService memoryService,
        global::IronDev.Agent.Services.Interfaces.ILocalIndexingService indexingService,
        global::IronDev.Core.Interfaces.IProjectProfileService profileService)
    {
        _ticketService   = ticketService;
        _memoryService   = memoryService;
        _indexingService = indexingService;
        _profileService  = profileService;
    }

    internal async Task LoadAsync(global::IronDev.Data.Models.Project project)
    {
        _currentProject = project;
        ProjectName     = project.Name;
        ProjectPath     = project.LocalPath ?? "No path configured";
        Description     = project.Description ?? string.Empty;
        
        await RefreshAsync();
        await LoadProfileAsync();
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
            LastIndexingDetails = $"Files Scanned: {result.FilesScanned}\n" +
                                  $"Files Added: {result.FilesAdded}\n" +
                                  $"Files Updated: {result.FilesUpdated}\n" +
                                  $"Files Unchanged: {result.FilesUnchanged}\n" +
                                  $"Files Skipped: {result.FilesSkipped}\n" +
                                  $"Total in Index: {result.StoredFileCount}\n" +
                                  $"Duration: {sw.Elapsed.TotalSeconds:F1}s";

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                LastIndexingDetails += $"\n\nMessages: {result.ErrorMessage}";
            }

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
        // 5. Replace bad View Details modal with a useful panel/dialog
        // We'll show a comprehensive MessageBox that looks good for now.
        // It provides real indexing details instead of just "No details captured."
        
        if (string.IsNullOrWhiteSpace(LastIndexingDetails))
        {
            System.Windows.MessageBox.Show(
                "No index details have been captured yet. Re-index the project to collect details.",
                "Index Details", 
                System.Windows.MessageBoxButton.OK, 
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Project: {ProjectName}");
        sb.AppendLine($"Scope Path: {ProjectPath}");
        sb.AppendLine($"Status: {Status}");
        sb.AppendLine($"Indexed Files: {FileCount}");
        sb.AppendLine($"Last Indexed: {LastIndexed}");
        sb.AppendLine();
        sb.AppendLine("--- Indexing Output ---");
        sb.AppendLine(LastIndexingDetails);

        // Note: MessageBox isn't perfect but is what we have right now without adding a new Window. 
        // We ensure it includes all the requested data.
        System.Windows.MessageBox.Show(
            sb.ToString(),
            "Index Details",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task LoadProfileAsync()
    {
        if (_currentProject == null) return;
        var profile = await _profileService.GetProjectProfileAsync(_currentProject.Id);
        
        if (profile != null)
        {
            ProfileApplicationType = profile.ApplicationType ?? string.Empty;
            ProfilePrimaryLanguage = profile.PrimaryLanguage ?? string.Empty;
            ProfileFramework = profile.Framework ?? string.Empty;
            ProfileDatabaseEngine = profile.DatabaseEngine ?? string.Empty;
            ProfileDataAccessStyle = profile.DataAccessStyle ?? string.Empty;
            ProfileTestFramework = profile.TestFramework ?? string.Empty;
            ProfileSolutionFile = profile.SolutionFile ?? string.Empty;
            ProfileSafeWriteRoot = profile.SafeWriteRoot ?? string.Empty;
            ProfileAllowBuilderApply = profile.AllowBuilderApply;
        }
        else
        {
            // Default empty
            ProfileApplicationType = string.Empty;
            ProfilePrimaryLanguage = string.Empty;
            ProfileFramework = string.Empty;
            ProfileDatabaseEngine = string.Empty;
            ProfileDataAccessStyle = string.Empty;
            ProfileTestFramework = string.Empty;
            ProfileSolutionFile = string.Empty;
            ProfileSafeWriteRoot = string.Empty;
            ProfileAllowBuilderApply = false;
        }

        var buildCmd = await _profileService.GetDefaultCommandAsync(_currentProject.Id, "Build");
        ProfileBuildCommand = buildCmd?.CommandText ?? string.Empty;

        var testCmd = await _profileService.GetDefaultCommandAsync(_currentProject.Id, "Test");
        ProfileTestCommand = testCmd?.CommandText ?? string.Empty;
        
        ProfileSaveStatus = string.Empty;
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (_currentProject == null) return;
        
        ProfileSaveStatus = "Saving...";

        var profile = await _profileService.GetProjectProfileAsync(_currentProject.Id) 
            ?? new IronDev.Data.Models.ProjectProfile { ProjectId = _currentProject.Id };

        profile.ApplicationType = ProfileApplicationType;
        profile.PrimaryLanguage = ProfilePrimaryLanguage;
        profile.Framework = ProfileFramework;
        profile.DatabaseEngine = ProfileDatabaseEngine;
        profile.DataAccessStyle = ProfileDataAccessStyle;
        profile.TestFramework = ProfileTestFramework;
        profile.SolutionFile = ProfileSolutionFile;
        profile.SafeWriteRoot = ProfileSafeWriteRoot;
        profile.AllowBuilderApply = ProfileAllowBuilderApply;
        
        await _profileService.SaveProjectProfileAsync(profile);

        // Save commands
        var buildCmd = await _profileService.GetDefaultCommandAsync(_currentProject.Id, "Build")
            ?? new IronDev.Data.Models.ProjectCommand { ProjectId = _currentProject.Id, CommandType = "Build" };
        buildCmd.CommandText = ProfileBuildCommand;
        await _profileService.SaveProjectCommandAsync(buildCmd);

        var testCmd = await _profileService.GetDefaultCommandAsync(_currentProject.Id, "Test")
            ?? new IronDev.Data.Models.ProjectCommand { ProjectId = _currentProject.Id, CommandType = "Test" };
        testCmd.CommandText = ProfileTestCommand;
        await _profileService.SaveProjectCommandAsync(testCmd);

        ProfileSaveStatus = "Saved ✓";
    }

    [RelayCommand]
    private void DetectProfile()
    {
        if (_currentProject == null || string.IsNullOrWhiteSpace(_currentProject.LocalPath)) return;

        var path = _currentProject.LocalPath;
        if (!Directory.Exists(path)) return;

        ProfileSafeWriteRoot = path;

        // Find solution
        var slnFiles = Directory.GetFiles(path, "*.sln", SearchOption.TopDirectoryOnly);
        if (slnFiles.Length == 0)
        {
            slnFiles = Directory.GetFiles(path, "*.slnx", SearchOption.TopDirectoryOnly);
        }

        if (slnFiles.Length > 0)
        {
            ProfileSolutionFile = slnFiles[0];
            ProfileBuildCommand = $"dotnet build \"{ProfileSolutionFile}\" --no-incremental -v quiet";
            ProfileTestCommand = $"dotnet test \"{ProfileSolutionFile}\" --logger \"console;verbosity=minimal\"";
        }
        else
        {
            ProfileSolutionFile = string.Empty;
            ProfileBuildCommand = "dotnet build";
            ProfileTestCommand = "dotnet test";
        }

        // Try to detect xUnit
        try
        {
            bool hasXUnit = false;
            var projFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories);
            foreach (var proj in projFiles)
            {
                var content = File.ReadAllText(proj);
                if (content.Contains("xunit", StringComparison.OrdinalIgnoreCase))
                {
                    hasXUnit = true;
                    break;
                }
            }

            if (hasXUnit)
            {
                ProfileTestFramework = "xUnit";
            }
        }
        catch { }

        // Some reasonable defaults if missing
        if (string.IsNullOrWhiteSpace(ProfilePrimaryLanguage)) ProfilePrimaryLanguage = "C#";
        
        // BookSeller specific detection
        if (path.Contains("BookSeller", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(ProfileApplicationType)) ProfileApplicationType = "External Sandbox / Class Library";
            if (string.IsNullOrWhiteSpace(ProfileFramework)) ProfileFramework = ".NET 10";
            if (string.IsNullOrWhiteSpace(ProfileDatabaseEngine)) ProfileDatabaseEngine = "None";
            if (string.IsNullOrWhiteSpace(ProfileDataAccessStyle)) ProfileDataAccessStyle = "InMemory";
        }
        
        ProfileSaveStatus = "Detected. Unsaved.";
    }
}
