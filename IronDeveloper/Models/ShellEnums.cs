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
    Plans,
    Decisions,
    Settings
}

/// <summary>
/// Workflow stages for the 2-stage login process.
/// </summary>
public enum LoginStage
{
    Credentials,
    Resolving,
    TenantSelection
}

/// <summary>
/// Navigation sections within the Ticket detail view.
/// </summary>
public enum TicketDetailTab
{
    Overview,
    ImplementationPlan,
    CodeContext
}
