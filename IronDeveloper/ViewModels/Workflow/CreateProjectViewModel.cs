using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IronDev.Agent.ViewModels.Workflow;

public sealed partial class CreateProjectViewModel : ObservableObject
{
    private readonly global::IronDev.Services.IProjectService _projectService;

    [ObservableProperty] private string _projectName = string.Empty;
    [ObservableProperty] private string _projectPath = string.Empty;
    [ObservableProperty] private string _selectedModel = "gpt-4o";
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

    public CreateProjectViewModel(global::IronDev.Services.IProjectService projectService)
    {
        _projectService = projectService;
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

        var newProject = new global::IronDev.Data.Models.Project
        {
            Name = ProjectName.Trim(),
            LocalPath = ProjectPath.Trim(),
            Description = $"Created on {DateTime.Now:yyyy-MM-dd}"
        };

        try
        {
            var id = await _projectService.CreateProjectAsync(newProject);
            newProject.Id = id;
            OnProjectCreated?.Invoke(newProject);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create project: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel() => OnCancel?.Invoke();
}
