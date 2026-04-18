using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IronDev.Agent.ViewModels.Workflow;

public sealed partial class ProjectHubViewModel : ObservableObject
{
    private readonly global::IronDev.Services.IProjectService _projectService;

    [ObservableProperty] private ObservableCollection<global::IronDev.Data.Models.Project> _recentProjects = [];

    internal Action<global::IronDev.Data.Models.Project>? OnOpenProject   { get; set; }
    internal Action?                                     OnCreateProject { get; set; }

    public ProjectHubViewModel(global::IronDev.Services.IProjectService projectService)
    {
        _projectService = projectService;
    }

    internal async Task Refresh()
    {
        RecentProjects.Clear();
        var projects = await _projectService.GetProjectsAsync();
        foreach (var p in projects)
            RecentProjects.Add(p);
    }

    [RelayCommand]
    private void OpenProject(global::IronDev.Data.Models.Project project)
    {
        OnOpenProject?.Invoke(project);
    }

    [RelayCommand]
    private void CreateNewProject() => OnCreateProject?.Invoke();
}
