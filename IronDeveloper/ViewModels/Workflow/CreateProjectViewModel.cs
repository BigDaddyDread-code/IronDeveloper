using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Client.Memory;
using IronDev.Client.Projects;
using IronDev.Core.Interfaces;
using IronDev.Data.Models;

namespace IronDev.Agent.ViewModels.Workflow;

public sealed partial class CreateProjectViewModel : ObservableObject
{
    private readonly IProjectsApiClient _projectService;
    private readonly IProjectProfileService _profileService;
    private readonly IProjectProfileDetectionService _profileDetectionService;
    private readonly IMemoryApiClient? _memoryService;

    [ObservableProperty] private string _projectName = string.Empty;
    [ObservableProperty] private string _projectPath = string.Empty;
    [ObservableProperty] private string _selectedModel = "gpt-4o";
    [ObservableProperty] private bool _createNewProjectSkeleton;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private int _wizardStep = 1;
    [ObservableProperty] private string _entryMode = "Existing Project";
    [ObservableProperty] private string _selectedApplicationType = "Console / Service";
    [ObservableProperty] private string _selectedFramework = ".NET 10";
    [ObservableProperty] private string _selectedUiStyle = "None";
    [ObservableProperty] private string _selectedDatabase = "None";
    [ObservableProperty] private string _selectedDataAccess = "None";
    [ObservableProperty] private string _selectedLogging = "Microsoft.Extensions.Logging";
    [ObservableProperty] private string _selectedTestFramework = "MSTest";
    [ObservableProperty] private string _selectedArchitectureStyle = "Simple layered";
    [ObservableProperty] private string _detectedSolutionFile = string.Empty;
    [ObservableProperty] private string _detectedBuildCommand = string.Empty;
    [ObservableProperty] private string _detectedTestCommand = string.Empty;
    [ObservableProperty] private string _safeWriteRoot = string.Empty;
    [ObservableProperty] private bool _allowBuilderApply;
    [ObservableProperty] private string _detectionSummary = string.Empty;
    [ObservableProperty] private bool _createInitialContext = true;
    [ObservableProperty] private bool _isBusy;

    public IReadOnlyList<string> AvailableModels { get; } =
    [
        "gpt-4o",
        "gpt-4o-mini",
        "claude-3-5-sonnet",
        "claude-3-haiku",
        "gemini-2.5-pro"
    ];
    public IReadOnlyList<string> EntryModes { get; } = ["Existing Project", "New Project"];
    public IReadOnlyList<string> ApplicationTypes { get; } = ["Console / Service", "ASP.NET Core API", "WPF", "Class Library"];
    public IReadOnlyList<string> FrameworkOptions { get; } = [".NET 10", ".NET 9", ".NET 8"];
    public IReadOnlyList<string> UiStyles { get; } = ["None", "WPF", "Razor Pages", "Web API only"];
    public IReadOnlyList<string> DatabaseOptions { get; } = ["None", "SQLite", "SQL Server"];
    public IReadOnlyList<string> DataAccessOptions { get; } = ["None", "Dapper", "EF Core"];
    public IReadOnlyList<string> LoggingOptions { get; } = ["Microsoft.Extensions.Logging", "Serilog"];
    public IReadOnlyList<string> TestFrameworkOptions { get; } = ["MSTest", "xUnit", "NUnit"];
    public IReadOnlyList<string> ArchitectureStyleOptions { get; } = ["Simple layered", "Clean Architecture", "Vertical slice"];

    public string WizardTitle => WizardStep switch
    {
        1 => "Project Entry",
        2 => EntryMode == "New Project" ? "Project Shape" : "Detected Profile",
        _ => "Review and Create"
    };
    public bool IsExistingProjectMode => EntryMode == "Existing Project";
    public bool IsNewProjectMode => EntryMode == "New Project";
    public bool ShowEntryStep => WizardStep == 1;
    public bool ShowProfileStep => WizardStep == 2;
    public bool ShowReviewStep => WizardStep == 3;
    public bool CanGoBack => WizardStep > 1;
    public string PrimaryActionText => WizardStep < 3 ? "Next" : (IsNewProjectMode ? "Create Project" : "Import Project");

    internal Action<Project>? OnProjectCreated { get; set; }
    internal Action?                                    OnCancel         { get; set; }

    public CreateProjectViewModel(
        IProjectsApiClient projectService,
        IProjectProfileService profileService,
        IProjectProfileDetectionService profileDetectionService,
        IMemoryApiClient? memoryService = null)
    {
        _projectService = projectService;
        _profileService = profileService;
        _profileDetectionService = profileDetectionService;
        _memoryService = memoryService;
    }

    public CreateProjectViewModel(
        object projectService,
        IProjectProfileService profileService,
        IProjectProfileDetectionService profileDetectionService,
        object? memoryService = null)
        : this(
            global::IronDev.Agent.Services.BoundaryCompatibility.Projects(projectService),
            profileService,
            profileDetectionService,
            global::IronDev.Agent.Services.BoundaryCompatibility.Memory(memoryService))
    {
    }

    [RelayCommand]
    private async Task PrimaryActionAsync()
    {
        if (WizardStep < 3)
        {
            await NextAsync();
            return;
        }

        await CreateAsync();
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        ErrorMessage = string.Empty;
        if (!ValidateEntryFields()) return;

        if (WizardStep == 1)
        {
            CreateNewProjectSkeleton = IsNewProjectMode;
            if (IsExistingProjectMode)
            {
                await DetectProfileAsync();
            }
            else
            {
                ApplyNewProjectDefaults();
            }
        }

        WizardStep = Math.Min(3, WizardStep + 1);
        NotifyWizardStateChanged();
    }

    [RelayCommand]
    private void Back()
    {
        WizardStep = Math.Max(1, WizardStep - 1);
        NotifyWizardStateChanged();
    }

    [RelayCommand]
    private async Task DetectProfileAsync()
    {
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            ErrorMessage = "Local path is required before detection.";
            return;
        }

        var projectPath = Path.GetFullPath(ProjectPath.Trim());
        if (!Directory.Exists(projectPath))
        {
            ErrorMessage = "Local path does not exist. Choose an existing project folder.";
            return;
        }

        IsBusy = true;
        try
        {
            var detected = await _profileDetectionService.DetectAsync(projectPath);
            ApplyDetectedProfile(detected);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        ErrorMessage = string.Empty;

        if (!ValidateEntryFields()) return;

        var projectPath = Path.GetFullPath(ProjectPath.Trim());
        if (!Directory.Exists(projectPath) && !CreateNewProjectSkeleton)
        {
            ErrorMessage = "Local path does not exist. Enable skeleton creation for a new project, or choose an existing folder.";
            return;
        }

        var newProject = new Project
        {
            Name = ProjectName.Trim(),
            LocalPath = projectPath,
            Description = $"Created on {DateTime.Now:yyyy-MM-dd}"
        };

        try
        {
            IsBusy = true;
            if (CreateNewProjectSkeleton)
            {
                await CreateSkeletonAsync(projectPath, newProject.Name);
            }

            var id = await _projectService.CreateProjectAsync(newProject);
            newProject.Id = id;
            await SeedDetectedProfileAsync(newProject);
            if (CreateInitialContext)
                await SeedInitialContextAsync(newProject);
            OnProjectCreated?.Invoke(newProject);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create project: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SeedDetectedProfileAsync(Project project)
    {
        var detected = await _profileDetectionService.DetectAsync(project.LocalPath ?? string.Empty, project.Id);
        detected.Profile.ApplicationType = SelectedApplicationType;
        detected.Profile.Framework = SelectedFramework;
        detected.Profile.DatabaseEngine = SelectedDatabase;
        detected.Profile.DataAccessStyle = SelectedDataAccess;
        detected.Profile.TestFramework = SelectedTestFramework;
        detected.Profile.SafeWriteRoot = string.IsNullOrWhiteSpace(SafeWriteRoot) ? project.LocalPath : SafeWriteRoot;
        detected.Profile.AllowBuilderApply = AllowBuilderApply;
        detected.Profile.ProfileNotes = $"Wizard: UI={SelectedUiStyle}; Logging={SelectedLogging}; Architecture={SelectedArchitectureStyle}.";
        if (!string.IsNullOrWhiteSpace(DetectedSolutionFile))
            detected.Profile.SolutionFile = DetectedSolutionFile;
        if (!string.IsNullOrWhiteSpace(DetectedBuildCommand))
            detected.BuildCommand.CommandText = DetectedBuildCommand;
        if (!string.IsNullOrWhiteSpace(DetectedTestCommand))
            detected.TestCommand.CommandText = DetectedTestCommand;

        await _profileService.SaveProjectProfileAsync(detected.Profile);
        await _profileService.SaveProjectCommandAsync(detected.BuildCommand);
        await _profileService.SaveProjectCommandAsync(detected.TestCommand);
    }

    private static async Task CreateSkeletonAsync(string projectPath, string projectName)
    {
        Directory.CreateDirectory(projectPath);

        if (Directory.EnumerateFileSystemEntries(projectPath).Any())
            throw new InvalidOperationException("Skeleton creation requires an empty folder.");

        var safeName = ToSafeProjectName(projectName);
        var srcDir = Path.Combine(projectPath, "src", safeName);
        var testDir = Path.Combine(projectPath, "tests", $"{safeName}.Tests");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(testDir);

            await File.WriteAllTextAsync(Path.Combine(projectPath, "README.md"),
            $"""
            # {projectName}

            Created by IronDev Alpha 0.1.

            ## Commands

            ```powershell
            dotnet build .\src\{safeName}\{safeName}.csproj
            dotnet test .\tests\{safeName}.Tests\{safeName}.Tests.csproj
            ```
            """);

        await File.WriteAllTextAsync(Path.Combine(srcDir, $"{safeName}.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(srcDir, "Program.cs"),
            $$"""
            Console.WriteLine("{{projectName}} is ready.");
            """);

        await File.WriteAllTextAsync(Path.Combine(testDir, $"{safeName}.Tests.csproj"),
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
                <PackageReference Include="MSTest.TestAdapter" Version="4.0.1" />
                <PackageReference Include="MSTest.TestFramework" Version="4.0.1" />
              </ItemGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(testDir, "SmokeTests.cs"),
            $$"""
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            namespace {{safeName}}.Tests;

            [TestClass]
            public sealed class SmokeTests
            {
                [TestMethod]
                public void ProjectSkeleton_ShouldBeCreated()
                {
                    Assert.IsTrue(true);
                }
            }
            """);
    }

    private static string ToSafeProjectName(string projectName)
    {
        var chars = projectName
            .Where(c => char.IsLetterOrDigit(c) || c == '_')
            .ToArray();

        var safeName = new string(chars);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "NewProject";

        if (!char.IsLetter(safeName[0]) && safeName[0] != '_')
            safeName = $"Project{safeName}";

        return safeName;
    }

    [RelayCommand]
    private void Cancel()
    {
        ResetWizard();
        OnCancel?.Invoke();
    }

    partial void OnEntryModeChanged(string value)
    {
        CreateNewProjectSkeleton = IsNewProjectMode;
        NotifyWizardStateChanged();
    }

    partial void OnWizardStepChanged(int value) => NotifyWizardStateChanged();

    private bool ValidateEntryFields()
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            ErrorMessage = "Project name is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            ErrorMessage = "Local path is required.";
            return false;
        }

        var projectPath = Path.GetFullPath(ProjectPath.Trim());
        if (!CreateNewProjectSkeleton && !Directory.Exists(projectPath))
        {
            ErrorMessage = "Local path does not exist. Choose an existing project folder.";
            return false;
        }

        return true;
    }

    private void ApplyNewProjectDefaults()
    {
        var projectPath = Path.GetFullPath(ProjectPath.Trim());
        SafeWriteRoot = projectPath;
        DetectedSolutionFile = Path.Combine(projectPath, "src", ToSafeProjectName(ProjectName), $"{ToSafeProjectName(ProjectName)}.csproj");
        DetectedBuildCommand = $"dotnet build \"{DetectedSolutionFile}\" --no-incremental -v quiet";
        DetectedTestCommand = $"dotnet test \"{Path.Combine(projectPath, "tests", $"{ToSafeProjectName(ProjectName)}.Tests", $"{ToSafeProjectName(ProjectName)}.Tests.csproj")}\" --logger \"console;verbosity=minimal\"";
        DetectionSummary = "New project profile will be seeded from wizard selections.";
    }

    private void ApplyDetectedProfile(ProjectProfileDetectionResult detected)
    {
        SelectedApplicationType = string.IsNullOrWhiteSpace(detected.Profile.ApplicationType) ? SelectedApplicationType : detected.Profile.ApplicationType;
        SelectedFramework = string.IsNullOrWhiteSpace(detected.Profile.Framework) ? SelectedFramework : detected.Profile.Framework;
        SelectedDatabase = string.IsNullOrWhiteSpace(detected.Profile.DatabaseEngine) ? SelectedDatabase : detected.Profile.DatabaseEngine;
        SelectedDataAccess = string.IsNullOrWhiteSpace(detected.Profile.DataAccessStyle) ? SelectedDataAccess : detected.Profile.DataAccessStyle;
        SelectedTestFramework = string.IsNullOrWhiteSpace(detected.Profile.TestFramework) ? SelectedTestFramework : detected.Profile.TestFramework;
        DetectedSolutionFile = detected.Profile.SolutionFile ?? string.Empty;
        DetectedBuildCommand = detected.BuildCommand.CommandText;
        DetectedTestCommand = detected.TestCommand.CommandText;
        SafeWriteRoot = detected.Profile.SafeWriteRoot ?? Path.GetFullPath(ProjectPath.Trim());
        AllowBuilderApply = detected.Profile.AllowBuilderApply;
        DetectionSummary = detected.Warnings.Count > 0
            ? string.Join(" ", detected.Warnings)
            : string.Join(" ", detected.DetectedFacts.DefaultIfEmpty("Profile detected. Review before importing."));
    }

    private async Task SeedInitialContextAsync(Project project)
    {
        if (_memoryService == null) return;

        await _memoryService.SaveSummaryAsync(new ProjectSummary
        {
            ProjectId = project.Id,
            Summary = $"{project.Name} was onboarded through the IronDev project wizard as a {SelectedApplicationType} project using {SelectedFramework}."
        });

        await _memoryService.SaveContextDocumentAsync(new ProjectContextDocument
        {
            ProjectId = project.Id,
            DocumentType = "ArchitectureDecision",
            AuthorityLevel = "Binding",
            Status = "Active",
            Title = $"Architecture style: {SelectedArchitectureStyle}",
            Content = $"Use {SelectedArchitectureStyle} as the starting architecture style for this project.",
            Source = "Project Wizard"
        });

        await _memoryService.SaveContextDocumentAsync(new ProjectContextDocument
        {
            ProjectId = project.Id,
            DocumentType = "ProjectStandard",
            AuthorityLevel = "StrongGuidance",
            Status = "Active",
            Title = $"Testing standard: {SelectedTestFramework}",
            Content = $"Use {SelectedTestFramework} for generated and manually-authored tests unless a later decision supersedes this.",
            Source = "Project Wizard"
        });
    }

    private void ResetWizard()
    {
        WizardStep = 1;
        ErrorMessage = string.Empty;
        DetectionSummary = string.Empty;
    }

    private void NotifyWizardStateChanged()
    {
        OnPropertyChanged(nameof(WizardTitle));
        OnPropertyChanged(nameof(IsExistingProjectMode));
        OnPropertyChanged(nameof(IsNewProjectMode));
        OnPropertyChanged(nameof(ShowEntryStep));
        OnPropertyChanged(nameof(ShowProfileStep));
        OnPropertyChanged(nameof(ShowReviewStep));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(PrimaryActionText));
    }
}
