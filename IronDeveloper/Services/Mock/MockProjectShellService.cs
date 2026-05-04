using System;
using System.Collections.Generic;
using IronDev.Agent.Models;
using IronDev.Agent.Services.Interfaces;

namespace IronDev.Agent.Services.Mock;

/// <summary>
/// Seeded in-memory project service. No DB. Replace with real service in a later sprint.
/// </summary>
public sealed class MockProjectShellService : IProjectShellService
{
    private ProjectSummary? _activeProject;

    private readonly List<ProjectSummary> _projects =
    [
        new()
        {
            Name        = "IronDev",
            LocalPath   = @"C:\repos\AIDeveloper",
            Model       = "gpt-4o",
            Status      = "Needs Index",
            LastOpened  = DateTime.UtcNow.AddDays(-1),
            Description = "The main IronDev agent application — WPF shell + workflow scaffold."
        },
        new()
        {
            Name        = "Contoso API",
            LocalPath   = @"C:\repos\ContosoApi",
            Model       = "gpt-4o-mini",
            Status      = "Ready",
            LastOpened  = DateTime.UtcNow.AddDays(-3),
            Description = "REST API layer for the Contoso insurance platform."
        },
        new()
        {
            Name        = "DataPipeline",
            LocalPath   = @"C:\repos\DataPipeline",
            Model       = "claude-3-5-sonnet",
            Status      = "Ready",
            LastOpened  = DateTime.UtcNow.AddDays(-7),
            Description = "ETL pipeline for nightly data warehouse ingestion."
        }
    ];

    public IReadOnlyList<ProjectSummary> GetRecentProjects() => _projects.AsReadOnly();

    public ProjectSummary? GetActiveProject() => _activeProject;

    public void SetActiveProject(ProjectSummary project) => _activeProject = project;
}
