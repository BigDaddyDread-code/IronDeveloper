using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IronDev.Agent.ViewModels.Workflow;

public sealed partial class ProjectHubViewModel : ObservableObject
{
    private readonly global::IronDev.Services.IProjectService _projectService;

    [ObservableProperty] private ObservableCollection<global::IronDev.Data.Models.Project> _recentProjects = [];
    [ObservableProperty] private bool _isProjectWizardOpen;
    [ObservableProperty] private CreateProjectViewModel? _projectWizard;

    internal Action<global::IronDev.Data.Models.Project>? OnOpenProject   { get; set; }

    public ProjectHubViewModel(global::IronDev.Services.IProjectService projectService)
    {
        _projectService = projectService;
    }

    internal async Task Refresh()
    {
        RecentProjects.Clear();
        var projects = await _projectService.GetProjectsAsync();
        
        // Order by most recently updated/created to find "sensible default"
        var sortedProjects = projects
            .OrderByDescending(p => p.UpdatedDate ?? p.CreatedDate)
            .ToList();

        foreach (var p in sortedProjects)
            RecentProjects.Add(p);

        // Auto-select rule: If only one project is available, enter it automatically.
        if (RecentProjects.Count == 1)
        {
            OpenProject(RecentProjects[0]);
        }
        // NOTE: We could also implement "Prefer last-used if count > 1" but usually 
        // the user wants to see the Hub if they have multiple projects, 
        // unless they specifically set a "Default Project" setting later.
    }

    [RelayCommand]
    private void OpenProject(global::IronDev.Data.Models.Project project)
    {
        OnOpenProject?.Invoke(project);
    }

    [RelayCommand]
    private void CreateNewProject() => IsProjectWizardOpen = true;

    internal void AttachWizard(CreateProjectViewModel wizard)
    {
        ProjectWizard = wizard;
    }

    internal void CloseWizard()
    {
        IsProjectWizardOpen = false;
    }
}
