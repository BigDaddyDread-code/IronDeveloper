using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Client.Projects;
using IronDev.Data.Models;

namespace IronDev.Agent.ViewModels.Workflow;

public sealed partial class ProjectHubViewModel : ObservableObject
{
    private readonly IProjectsApiClient _projectService;

    [ObservableProperty] private ObservableCollection<Project> _recentProjects = [];
    [ObservableProperty] private bool _isProjectWizardOpen;
    [ObservableProperty] private CreateProjectViewModel? _projectWizard;

    internal Action<Project>? OnOpenProject   { get; set; }

    public ProjectHubViewModel(IProjectsApiClient projectService)
    {
        _projectService = projectService;
    }

    internal async Task Refresh()
    {
        RecentProjects.Clear();
        var projects = await _projectService.GetProjectsAsync();
        
        // Order by most recently updated/created to find "sensible default"
        var sortedProjects = projects
            .Where(p => !IsDogfoodGeneratedProject(p))
            .OrderByDescending(p => p.UpdatedDate ?? p.CreatedDate)
            .ToList();

        foreach (var p in sortedProjects)
            RecentProjects.Add(p);

        // Auto-select rule: If only one project is available, enter it automatically.
        if (RecentProjects.Count == 1)
        {
            await OpenProjectAsync(RecentProjects[0]);
        }
        // NOTE: We could also implement "Prefer last-used if count > 1" but usually 
        // the user wants to see the Hub if they have multiple projects, 
        // unless they specifically set a "Default Project" setting later.
    }

    private static bool IsDogfoodGeneratedProject(Project project)
    {
        var name = project.Name ?? string.Empty;
        var description = project.Description ?? string.Empty;

        return name.StartsWith("IronDevBuilderProposalSafety", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("IronDevMemorySpine", StringComparison.OrdinalIgnoreCase)
            || description.Contains("Disposable project for Memory Spine", StringComparison.OrdinalIgnoreCase)
            || description.Contains("same-tenant wrong-project control for Memory Spine", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task OpenProjectAsync(Project project)
    {
        Serilog.Log.Information("[ProjectHub] Opening project from card: {ProjectId} {ProjectName}", project.Id, project.Name);
        await _projectService.SelectProjectAsync(project.Id);
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
