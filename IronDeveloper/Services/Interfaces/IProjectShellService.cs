using System.Collections.Generic;
using IronDev.Agent.Models;

namespace IronDev.Agent.Services.Interfaces;

public interface IProjectShellService
{
    IReadOnlyList<ProjectSummary> GetRecentProjects();
    ProjectSummary? GetActiveProject();
    void SetActiveProject(ProjectSummary project);
}
