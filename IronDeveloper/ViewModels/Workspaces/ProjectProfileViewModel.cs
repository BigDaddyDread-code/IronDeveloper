using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Core.Interfaces;
using IronDev.Data.Models;

namespace IronDev.Agent.ViewModels.Workspaces;

public partial class ProjectProfileViewModel : ObservableObject
{
    private readonly IProjectProfileService _profileService;
    private Project? _currentProject;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // Profile Fields
    [ObservableProperty] private bool _isExternalProject;
    [ObservableProperty] private string? _selectedApplicationType;
    [ObservableProperty] private string? _selectedPrimaryLanguage;
    [ObservableProperty] private string? _selectedFramework;
    [ObservableProperty] private string? _selectedDatabaseEngine;
    [ObservableProperty] private string? _selectedDataAccessStyle;
    [ObservableProperty] private string? _selectedTestFramework;
    [ObservableProperty] private string? _solutionFile;
    [ObservableProperty] private string? _safeWriteRoot;
    [ObservableProperty] private bool _allowBuilderApply;
    [ObservableProperty] private bool _allowWritesOutsideProjectRoot;
    [ObservableProperty] private string? _profileNotes;

    // Commands
    [ObservableProperty] private string _buildCommandText = string.Empty;
    [ObservableProperty] private string _testCommandText = string.Empty;

    // Options
    public ObservableCollection<ProjectProfileOption> ApplicationTypes { get; } = new();
    public ObservableCollection<ProjectProfileOption> PrimaryLanguages { get; } = new();
    public ObservableCollection<ProjectProfileOption> Frameworks { get; } = new();
    public ObservableCollection<ProjectProfileOption> DatabaseEngines { get; } = new();
    public ObservableCollection<ProjectProfileOption> DataAccessStyles { get; } = new();
    public ObservableCollection<ProjectProfileOption> TestFrameworks { get; } = new();

    public ProjectProfileViewModel(IProjectProfileService profileService)
    {
        _profileService = profileService;
    }

    public async Task LoadAsync(Project project)
    {
        _currentProject = project;
        IsBusy = true;
        StatusMessage = "Loading project profile...";

        try
        {
            await LoadOptionsAsync();
            
            var profile = await _profileService.GetProjectProfileAsync(project.Id);
            if (profile != null)
            {
                IsExternalProject = profile.IsExternalProject;
                SelectedApplicationType = profile.ApplicationType;
                SelectedPrimaryLanguage = profile.PrimaryLanguage;
                SelectedFramework = profile.Framework;
                SelectedDatabaseEngine = profile.DatabaseEngine;
                SelectedDataAccessStyle = profile.DataAccessStyle;
                SelectedTestFramework = profile.TestFramework;
                SolutionFile = profile.SolutionFile;
                SafeWriteRoot = profile.SafeWriteRoot;
                AllowBuilderApply = profile.AllowBuilderApply;
                AllowWritesOutsideProjectRoot = profile.AllowWritesOutsideProjectRoot;
                ProfileNotes = profile.ProfileNotes;
            }
            else
            {
                // Defaults for new profile
                ResetToDefaults();
                if (string.IsNullOrEmpty(SafeWriteRoot))
                {
                    SafeWriteRoot = project.LocalPath;
                }
            }

            var buildCmd = await _profileService.GetDefaultCommandAsync(project.Id, "Build");
            BuildCommandText = buildCmd?.CommandText ?? string.Empty;

            var testCmd = await _profileService.GetDefaultCommandAsync(project.Id, "Test");
            TestCommandText = testCmd?.CommandText ?? string.Empty;

            if (profile == null && !string.IsNullOrEmpty(project.LocalPath))
            {
                await AutoDetectAsync();
            }
        }
        finally
        {
            IsBusy = false;
            StatusMessage = string.Empty;
        }
    }

    private void ResetToDefaults()
    {
        IsExternalProject = false;
        SelectedApplicationType = "Unknown";
        SelectedPrimaryLanguage = "CSharp";
        SelectedFramework = "DotNet10";
        SelectedDatabaseEngine = "None";
        SelectedDataAccessStyle = "None";
        SelectedTestFramework = "None";
        SolutionFile = string.Empty;
        SafeWriteRoot = _currentProject?.LocalPath ?? string.Empty;
        AllowBuilderApply = false;
        AllowWritesOutsideProjectRoot = false;
        ProfileNotes = string.Empty;
        BuildCommandText = string.Empty;
        TestCommandText = string.Empty;
    }

    private async Task LoadOptionsAsync()
    {
        var appTypes = await _profileService.GetOptionsByCategoryAsync("ApplicationType");
        ApplicationTypes.Clear();
        foreach (var opt in appTypes) ApplicationTypes.Add(opt);

        var langs = await _profileService.GetOptionsByCategoryAsync("PrimaryLanguage");
        PrimaryLanguages.Clear();
        foreach (var opt in langs) PrimaryLanguages.Add(opt);

        var fws = await _profileService.GetOptionsByCategoryAsync("Framework");
        Frameworks.Clear();
        foreach (var opt in fws) Frameworks.Add(opt);

        var dbs = await _profileService.GetOptionsByCategoryAsync("DatabaseEngine");
        DatabaseEngines.Clear();
        foreach (var opt in dbs) DatabaseEngines.Add(opt);

        var styles = await _profileService.GetOptionsByCategoryAsync("DataAccessStyle");
        DataAccessStyles.Clear();
        foreach (var opt in styles) DataAccessStyles.Add(opt);

        var tfs = await _profileService.GetOptionsByCategoryAsync("TestFramework");
        TestFrameworks.Clear();
        foreach (var opt in tfs) TestFrameworks.Add(opt);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_currentProject == null) return;

        IsBusy = true;
        StatusMessage = "Saving profile...";

        try
        {
            var profile = new ProjectProfile
            {
                ProjectId = _currentProject.Id,
                IsExternalProject = IsExternalProject,
                ApplicationType = SelectedApplicationType,
                PrimaryLanguage = SelectedPrimaryLanguage,
                Framework = SelectedFramework,
                RuntimeVersion = string.Empty, // TODO
                DatabaseEngine = SelectedDatabaseEngine,
                DataAccessStyle = SelectedDataAccessStyle,
                TestFramework = SelectedTestFramework,
                SolutionFile = SolutionFile,
                SafeWriteRoot = SafeWriteRoot,
                AllowBuilderApply = AllowBuilderApply,
                AllowWritesOutsideProjectRoot = AllowWritesOutsideProjectRoot,
                ProfileNotes = ProfileNotes
            };

            await _profileService.SaveProjectProfileAsync(profile);

            // Save Build Command
            var buildCmd = await _profileService.GetDefaultCommandAsync(_currentProject.Id, "Build") ?? new ProjectCommand { ProjectId = _currentProject.Id, CommandType = "Build" };
            buildCmd.CommandText = BuildCommandText;
            await _profileService.SaveProjectCommandAsync(buildCmd);

            // Save Test Command
            var testCmd = await _profileService.GetDefaultCommandAsync(_currentProject.Id, "Test") ?? new ProjectCommand { ProjectId = _currentProject.Id, CommandType = "Test" };
            testCmd.CommandText = TestCommandText;
            await _profileService.SaveProjectCommandAsync(testCmd);

            StatusMessage = "Saved successfully!";
            await Task.Delay(2000);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AutoDetectAsync()
    {
        if (_currentProject == null || string.IsNullOrEmpty(_currentProject.LocalPath)) return;

        var root = _currentProject.LocalPath;
        if (!Directory.Exists(root)) return;

        // 1. Find Solution
        var sln = Directory.GetFiles(root, "*.slnx", SearchOption.AllDirectories).FirstOrDefault()
               ?? Directory.GetFiles(root, "*.sln", SearchOption.AllDirectories).FirstOrDefault();

        if (sln != null)
        {
            SolutionFile = sln;
            BuildCommandText = $"dotnet build \"{sln}\" --no-incremental -v quiet";
            TestCommandText = $"dotnet test \"{sln}\" --logger \"console;verbosity=minimal\"";
        }

        // 2. Detect Test Framework (very simple heuristic)
        if (string.IsNullOrEmpty(SelectedTestFramework) || SelectedTestFramework == "None" || SelectedTestFramework == "Unknown")
        {
            var csprojFiles = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories);
            foreach (var file in csprojFiles)
            {
                var content = await File.ReadAllTextAsync(file);
                if (content.Contains("xunit", StringComparison.OrdinalIgnoreCase)) { SelectedTestFramework = "xUnit"; break; }
                if (content.Contains("MSTest", StringComparison.OrdinalIgnoreCase)) { SelectedTestFramework = "MSTest"; break; }
                if (content.Contains("nunit", StringComparison.OrdinalIgnoreCase)) { SelectedTestFramework = "NUnit"; break; }
            }
        }

        // 3. Defaults for BookSeller specific if path matches
        if (root.Contains("BookSeller", StringComparison.OrdinalIgnoreCase))
        {
            IsExternalProject = true;
            SelectedApplicationType = "ExternalSandbox";
            AllowBuilderApply = true;
        }
    }
}
