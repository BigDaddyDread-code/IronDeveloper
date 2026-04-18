namespace IronDev.Agent.Models;

/// <summary>
/// Top-level shell mode. Controls which "screen" the app is in.
/// </summary>
public enum ShellMode
{
    Login,
    ProjectHub,
    CreateProject,
    ProjectActive
}

/// <summary>
/// Workspace area within the ProjectActive shell mode.
/// </summary>
public enum ProjectWorkspace
{
    Overview,
    Chat,
    Tickets,
    Decisions,
    Settings
}
