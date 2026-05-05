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
    CodeContext,
    Tests
}

/// <summary>
/// Tracks the preflight gate state before a draft ticket is generated.
/// None       — no preflight active; draft flows normally.
/// NeedsChoice — project is not indexed; waiting for user choice (Index / Continue / Cancel).
/// ReadyToGenerate — indexing completed while context was pending; ready to generate draft.
/// </summary>
public enum DraftPreflightState
{
    None,
    NeedsChoice,
    ReadyToGenerate
}
