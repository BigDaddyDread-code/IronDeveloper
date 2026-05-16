using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Models;
using IronDev.Agent.Services;
using IronDeveloperControls.Primitives;

namespace IronDev.Agent.ViewModels.Workflow;

public sealed partial class ProjectOverviewViewModel : ObservableObject
{
    private readonly global::IronDev.Services.ITicketService _ticketService;
    private readonly global::IronDev.Services.IProjectMemoryService _memoryService;
    private readonly global::IronDev.Agent.Services.Interfaces.ILocalIndexingService _indexingService;
    private readonly global::IronDev.Core.Interfaces.IProjectProfileService _profileService;
    private readonly global::IronDev.Core.Interfaces.IProjectProfileDetectionService _profileDetectionService;
    private readonly global::IronDev.Services.IProjectContextExportService _exportService;

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
    [ObservableProperty] private bool _showIndexHealthCallout;
    [ObservableProperty] private string _indexHealthTitle = string.Empty;
    [ObservableProperty] private string _indexHealthMessage = string.Empty;
    [ObservableProperty] private string _indexHealthBadgeText = "INFO";
    [ObservableProperty] private BadgeStatus _indexHealthBadgeStatus = BadgeStatus.Info;
    [ObservableProperty] private bool _showIndexDetails;
    [ObservableProperty] private string _indexDetailsTitle = "Index details";
    [ObservableProperty] private string _indexDetailsMessage = string.Empty;
    [ObservableProperty] private string _indexDetailsBadgeText = "DETAILS";
    [ObservableProperty] private BadgeStatus _indexDetailsBadgeStatus = BadgeStatus.Info;

    // Session context (set by ShellViewModel on project activation)
    [ObservableProperty] private string _currentUserDisplayName = string.Empty;
    [ObservableProperty] private string _currentUserEmail       = string.Empty;
    [ObservableProperty] private string _currentTenantName      = string.Empty;
    [ObservableProperty] private string _currentWorkspaceName   = string.Empty;

    [ObservableProperty] private ObservableCollection<TicketItem>   _recentTickets   = [];
    [ObservableProperty] private ObservableCollection<DecisionItem> _recentDecisions = [];

    // Project Profile
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
    [ObservableProperty] private bool _profileAllowWritesOutsideProjectRoot;
    [ObservableProperty] private string _profileNotes = string.Empty;

    [ObservableProperty] private string _profileSaveStatus = string.Empty;

    // Derived state card properties
    public string LastTicketTitle    => RecentTickets.Count > 0   ? RecentTickets[0].Title   : "None yet";
    public string LastDecisionTitle  => RecentDecisions.Count > 0 ? RecentDecisions[0].Title : "None yet";

    public ProjectOverviewViewModel(
        global::IronDev.Services.ITicketService ticketService,
        global::IronDev.Services.IProjectMemoryService memoryService,
        global::IronDev.Agent.Services.Interfaces.ILocalIndexingService indexingService,
        global::IronDev.Core.Interfaces.IProjectProfileService profileService,
        global::IronDev.Core.Interfaces.IProjectProfileDetectionService profileDetectionService,
        global::IronDev.Services.IProjectContextExportService exportService)
    {
        _ticketService   = ticketService;
        _memoryService   = memoryService;
        _indexingService = indexingService;
        _profileService  = profileService;
        _profileDetectionService = profileDetectionService;
        _exportService   = exportService;
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

        ApplyIndexHealth();
    }

    [RelayCommand]
    private async Task IndexProjectAsync()
    {
        if (_currentProject == null || string.IsNullOrWhiteSpace(_currentProject.LocalPath))
        {
            Status = "Err: No path configured";
            ApplyIndexHealth();
            return;
        }

        IsIndexing = true;
        Status = "Indexing...";
        ShowIndexDetails = false;
        ApplyIndexHealth();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var path = _currentProject.LocalPath;
        var pathExists = Directory.Exists(path);
        System.Diagnostics.Trace.WriteLine("[Index] ------------------------------------------------");
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
        System.Diagnostics.Trace.WriteLine("[Index] ------------------------------------------------");

        try
        {
            var result = await _indexingService.IndexProjectAsync(_currentProject);
            sw.Stop();
            IndexingTime = $"{sw.Elapsed.TotalSeconds:F1}s";

            System.Diagnostics.Trace.WriteLine(
                $"[Index] Result: Scanned={result.FilesScanned} Added={result.FilesAdded} Updated={result.FilesUpdated} " +
                $"Unchanged={result.FilesUnchanged} Skipped={result.FilesSkipped} Stored={result.StoredFileCount} " +
                $"DirNotFound={result.DirectoryNotFound} Error=[{result.ErrorMessage ?? "none"}]");

            if (result.DirectoryNotFound)
            {
                Status = $"Path not found: {path}";
                FileCount = 0;
                LastIndexingDetails = $"The configured project path does not exist: {path}";
                ApplyIndexHealth();
                return;
            }

            if (!string.IsNullOrEmpty(result.ErrorMessage) && result.StoredFileCount == 0)
            {
                Status = result.ErrorMessage;
                FileCount = 0;
                await RefreshAsync();
                return;
            }

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

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            sw.Stop();
            System.Diagnostics.Trace.WriteLine($"[Index] EXCEPTION: {ex}");
            Status = $"Index failed: {ex.Message}";
            LastIndexingDetails = ex.ToString();
            ApplyIndexHealth();
        }
        finally
        {
            IsIndexing = false;
            ApplyIndexHealth();
        }
    }

    [RelayCommand]
    private void ViewDetails()
    {
        var hasDetails = !string.IsNullOrWhiteSpace(LastIndexingDetails);

        IndexDetailsTitle = hasDetails ? "Index run details" : "No index details yet";
        IndexDetailsMessage = hasDetails
            ? "These are the latest captured indexing diagnostics for this project."
            : "Run Index Project to collect file counts, duration, skipped files, and any indexing errors.";
        IndexDetailsBadgeText = IsIndexReady() ? "READY" : "DETAILS";
        IndexDetailsBadgeStatus = IsIndexReady() ? BadgeStatus.Ready : BadgeStatus.Info;
        LastIndexingDetails = hasDetails ? LastIndexingDetails : BuildIndexSummary();
        ShowIndexDetails = true;
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
            ProfileAllowWritesOutsideProjectRoot = profile.AllowWritesOutsideProjectRoot;
            ProfileNotes = profile.ProfileNotes ?? string.Empty;
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
            ProfileAllowWritesOutsideProjectRoot = false;
            ProfileNotes = string.Empty;
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
        profile.AllowWritesOutsideProjectRoot = ProfileAllowWritesOutsideProjectRoot;
        profile.ProfileNotes = ProfileNotes;
        
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

        ProfileSaveStatus = "Saved";
    }

    [RelayCommand]
    private async Task DetectProfileAsync()
    {
        if (_currentProject == null || string.IsNullOrWhiteSpace(_currentProject.LocalPath)) return;

        var detected = await _profileDetectionService.DetectAsync(_currentProject.LocalPath, _currentProject.Id);
        ProfileApplicationType = detected.Profile.ApplicationType ?? string.Empty;
        ProfilePrimaryLanguage = detected.Profile.PrimaryLanguage ?? string.Empty;
        ProfileFramework = detected.Profile.Framework ?? string.Empty;
        ProfileDatabaseEngine = detected.Profile.DatabaseEngine ?? string.Empty;
        ProfileDataAccessStyle = detected.Profile.DataAccessStyle ?? string.Empty;
        ProfileTestFramework = detected.Profile.TestFramework ?? string.Empty;
        ProfileSolutionFile = detected.Profile.SolutionFile ?? string.Empty;
        ProfileSafeWriteRoot = detected.Profile.SafeWriteRoot ?? string.Empty;
        ProfileAllowBuilderApply = detected.Profile.AllowBuilderApply;
        ProfileAllowWritesOutsideProjectRoot = detected.Profile.AllowWritesOutsideProjectRoot;
        ProfileNotes = detected.Profile.ProfileNotes ?? string.Empty;
        ProfileBuildCommand = detected.BuildCommand.CommandText;
        ProfileTestCommand = detected.TestCommand.CommandText;
        ProfileSaveStatus = detected.Warnings.Count > 0
            ? $"Detected with warnings: {string.Join(" ", detected.Warnings)}"
            : "Detected. Unsaved.";
    }

    [RelayCommand]
    private async Task ExportProjectContextPackAsync()
    {
        if (_currentProject == null) return;

        try
        {
            var content = await _exportService.ExportProjectContextPackAsync(_currentProject.Id);
            var fileName = $"IronDev_ProjectContextPack_{DateTime.Now:yyyyMMdd_HHmm}.md";
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var filePath = Path.Combine(desktopPath, fileName);

            await File.WriteAllTextAsync(filePath, content);
            
            System.Windows.MessageBox.Show(
                $"Project context pack exported to:\n{filePath}",
                "Export Successful",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Failed to export context pack:\n{ex.Message}",
                "Export Failed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private void ApplyIndexHealth()
    {
        if (IsIndexing)
        {
            IndexHealthTitle = "Indexing project";
            IndexHealthMessage = "IronDev is refreshing the code index. Builder actions should wait for this to finish.";
            IndexHealthBadgeText = "INDEXING";
            IndexHealthBadgeStatus = BadgeStatus.InProgress;
            ShowIndexHealthCallout = true;
            return;
        }

        if (Status.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            Status.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            Status.StartsWith("Err:", StringComparison.OrdinalIgnoreCase))
        {
            IndexHealthTitle = "Index failed";
            IndexHealthMessage = "The project index is not ready. Check the details, fix the cause, then index again before building proposals.";
            IndexHealthBadgeText = "FAILED";
            IndexHealthBadgeStatus = BadgeStatus.Danger;
            ShowIndexHealthCallout = true;
            return;
        }

        if (!IsIndexReady())
        {
            IndexHealthTitle = "Project needs indexing";
            IndexHealthMessage = "Index this project before creating build-ready tickets or generating Builder proposals.";
            IndexHealthBadgeText = "NEEDS INDEX";
            IndexHealthBadgeStatus = BadgeStatus.NeedsIndex;
            ShowIndexHealthCallout = true;
            return;
        }

        ShowIndexHealthCallout = false;
    }

    private bool IsIndexReady()
    {
        return string.Equals(Status, "Ready", StringComparison.OrdinalIgnoreCase)
            && LastIndexed != "Never"
            && FileCount > 0;
    }

    private string BuildIndexSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Project: {ProjectName}");
        sb.AppendLine($"Scope Path: {ProjectPath}");
        sb.AppendLine($"Status: {Status}");
        sb.AppendLine($"Indexed Files: {FileCount}");
        sb.AppendLine($"Last Indexed: {LastIndexed}");

        if (!string.IsNullOrWhiteSpace(LastIndexingDetails))
        {
            sb.AppendLine();
            sb.AppendLine("Indexing Output:");
            sb.AppendLine(LastIndexingDetails);
        }

        return sb.ToString();
    }
}
