using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IronDev.Agent.ViewModels.Workflow;

public sealed partial class CreateProjectViewModel : ObservableObject
{
    private readonly global::IronDev.Services.IProjectService _projectService;
    private readonly global::IronDev.Core.Interfaces.IProjectProfileService _profileService;
    private readonly global::IronDev.Core.Interfaces.IProjectProfileDetectionService _profileDetectionService;

    [ObservableProperty] private string _projectName = string.Empty;
    [ObservableProperty] private string _projectPath = string.Empty;
    [ObservableProperty] private string _selectedModel = "gpt-4o";
    [ObservableProperty] private bool _createNewProjectSkeleton;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public IReadOnlyList<string> AvailableModels { get; } =
    [
        "gpt-4o",
        "gpt-4o-mini",
        "claude-3-5-sonnet",
        "claude-3-haiku",
        "gemini-2.5-pro"
    ];

    internal Action<global::IronDev.Data.Models.Project>? OnProjectCreated { get; set; }
    internal Action?                                    OnCancel         { get; set; }

    public CreateProjectViewModel(
        global::IronDev.Services.IProjectService projectService,
        global::IronDev.Core.Interfaces.IProjectProfileService profileService,
        global::IronDev.Core.Interfaces.IProjectProfileDetectionService profileDetectionService)
    {
        _projectService = projectService;
        _profileService = profileService;
        _profileDetectionService = profileDetectionService;
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            ErrorMessage = "Project name is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            ErrorMessage = "Local path is required.";
            return;
        }

        var projectPath = Path.GetFullPath(ProjectPath.Trim());
        if (!Directory.Exists(projectPath) && !CreateNewProjectSkeleton)
        {
            ErrorMessage = "Local path does not exist. Enable skeleton creation for a new project, or choose an existing folder.";
            return;
        }

        var newProject = new global::IronDev.Data.Models.Project
        {
            Name = ProjectName.Trim(),
            LocalPath = projectPath,
            Description = $"Created on {DateTime.Now:yyyy-MM-dd}"
        };

        try
        {
            if (CreateNewProjectSkeleton)
            {
                await CreateSkeletonAsync(projectPath, newProject.Name);
            }

            var id = await _projectService.CreateProjectAsync(newProject);
            newProject.Id = id;
            await SeedDetectedProfileAsync(newProject);
            OnProjectCreated?.Invoke(newProject);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create project: {ex.Message}";
        }
    }

    private async Task SeedDetectedProfileAsync(global::IronDev.Data.Models.Project project)
    {
        var detected = await _profileDetectionService.DetectAsync(project.LocalPath ?? string.Empty, project.Id);

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
    private void Cancel() => OnCancel?.Invoke();
}
