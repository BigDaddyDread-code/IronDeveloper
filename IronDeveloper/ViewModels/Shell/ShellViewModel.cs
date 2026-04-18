using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Models;
using IronDev.Agent.Services.Interfaces;
using IronDev.Agent.Services;
using IronDev.Agent.ViewModels.Workflow;
using IronDev.Agent.ViewModels.Workspaces;

namespace IronDev.Agent.ViewModels.Shell;

/// <summary>
/// Central shell view model. Owns the application state machine and exposes the
/// current content view model for DataTemplate-based view resolution.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    // ── Injected child VMs ───────────────────────────────────────────────────

    private readonly LoginViewModel       _loginVm;
    private readonly ProjectHubViewModel  _hubVm;
    private readonly CreateProjectViewModel _createVm;
    private readonly ProjectOverviewViewModel _overviewVm;
    private readonly ChatWorkspaceViewModel   _chatVm;
    private readonly TicketsWorkspaceViewModel _ticketsVm;
    private readonly DecisionsWorkspaceViewModel _decisionsVm;
    private readonly SettingsWorkspaceViewModel  _settingsVm;
    private readonly AgentTenantContext          _tenantContext;

    // ── Observable shell state ────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSidebar))]
    [NotifyPropertyChangedFor(nameof(ShowHeader))]
    private ShellMode _currentShellMode = ShellMode.Login;

    [ObservableProperty]
    private ProjectWorkspace _currentWorkspace = ProjectWorkspace.Overview;

    /// <summary>
    /// The active content view model. App.xaml DataTemplates map this to the correct view.
    /// </summary>
    [ObservableProperty]
    private object _currentView = null!;

    [ObservableProperty] private bool _isAuthenticated;
    [ObservableProperty] private global::IronDev.Core.Auth.UserProfileDto? _currentUser;
    [ObservableProperty] private global::IronDev.Core.Auth.TenantDto?      _currentTenant;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSidebar))]
    [NotifyPropertyChangedFor(nameof(ShowHeader))]
    private bool _hasActiveProject;

    [ObservableProperty] private string _activeProjectName = string.Empty;
    [ObservableProperty] private string _activeProjectPath = string.Empty;
    [ObservableProperty] private string _activeModel       = string.Empty;
    [ObservableProperty] private string _activeStatus      = string.Empty;

    // ── Status popup ─────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isStatusPopupOpen;

    // ── Derived ──────────────────────────────────────────────────────────────

    public bool HasActiveTenant => CurrentTenant != null;
    public bool ShowSidebar => HasActiveProject && CurrentShellMode == ShellMode.ProjectActive;
    public bool ShowHeader  => HasActiveProject && CurrentShellMode == ShellMode.ProjectActive;
    public bool StatusNeedsIndex => ActiveStatus == "Needs Index";

    // ── Constructor ──────────────────────────────────────────────────────────

    public ShellViewModel(
        LoginViewModel             loginVm,
        ProjectHubViewModel        hubVm,
        CreateProjectViewModel     createVm,
        ProjectOverviewViewModel   overviewVm,
        ChatWorkspaceViewModel     chatVm,
        TicketsWorkspaceViewModel  ticketsVm,
        DecisionsWorkspaceViewModel decisionsVm,
        SettingsWorkspaceViewModel  settingsVm,
        AgentTenantContext          tenantContext)
    {
        _loginVm     = loginVm;
        _hubVm       = hubVm;
        _createVm    = createVm;
        _overviewVm  = overviewVm;
        _chatVm      = chatVm;
        _ticketsVm   = ticketsVm;
        _decisionsVm = decisionsVm;
        _settingsVm  = settingsVm;
        _tenantContext = tenantContext;

        // Wire child VM navigation callbacks
        _loginVm.OnSignIn = (user, tenant) => 
        {
            CurrentUser = user;
            CurrentTenant = tenant;
            _tenantContext.SetTenant(tenant.Id);
            NavigateToHub();
        };

        _hubVm.OnOpenProject           = OpenProject;
        _hubVm.OnCreateProject         = NavigateToCreateProject;
        _createVm.OnProjectCreated     = CreateAndOpenProject;
        _createVm.OnCancel             = NavigateToHub;

        CurrentView = _loginVm;
    }

    // ── Navigation commands ──────────────────────────────────────────────────

    [RelayCommand]
    private void NavigateWorkspace(string workspaceName)
    {
        if (workspaceName == "ProjectHub")
        {
            NavigateToHub();
            return;
        }

        if (!System.Enum.TryParse<ProjectWorkspace>(workspaceName, out var ws)) return;
        CurrentWorkspace = ws;
        CurrentView = ws switch
        {
            ProjectWorkspace.Overview   => _overviewVm,
            ProjectWorkspace.Chat       => _chatVm,
            ProjectWorkspace.Tickets    => _ticketsVm,
            ProjectWorkspace.Decisions  => _decisionsVm,
            ProjectWorkspace.Settings   => _settingsVm,
            _                           => _overviewVm
        };
    }

    [RelayCommand]
    private void ToggleStatusPopup()
    {
        if (ActiveStatus == "Needs Index")
            IsStatusPopupOpen = !IsStatusPopupOpen;
    }

    [RelayCommand]
    private void IndexNow()
    {
        IsStatusPopupOpen = false;
        // TODO: trigger real indexing in a later sprint
        ActiveStatus = "Indexing…";
    }

    [RelayCommand]
    private void OpenOverview()
    {
        IsStatusPopupOpen = false;
        NavigateWorkspace(nameof(ProjectWorkspace.Overview));
    }

    // ── Private navigation helpers ───────────────────────────────────────────

    private void NavigateToHub()
    {
        IsAuthenticated = true;
        CurrentShellMode = ShellMode.ProjectHub;
        _ = _hubVm.Refresh(); 
        CurrentView = _hubVm;
    }

    private void OpenProject(global::IronDev.Data.Models.Project project)
    {
        ActivateProject(project);
        CurrentView = _overviewVm;
    }

    private void NavigateToCreateProject()
    {
        CurrentShellMode = ShellMode.CreateProject;
        CurrentView = _createVm;
    }

    private void CreateAndOpenProject(global::IronDev.Data.Models.Project project)
    {
        ActivateProject(project);
        CurrentView = _overviewVm;
    }

    private void ActivateProject(global::IronDev.Data.Models.Project project)
    {
        HasActiveProject  = true;
        CurrentShellMode  = ShellMode.ProjectActive;
        CurrentWorkspace  = ProjectWorkspace.Overview;
        ActiveProjectName = project.Name;
        ActiveProjectPath = project.LocalPath ?? string.Empty;
        
        // Mocked for now until LLM/Indexing services are real
        ActiveModel       = "gpt-4o"; 
        ActiveStatus      = "Needs Index";
        
        OnPropertyChanged(nameof(StatusNeedsIndex));
    }
}
