using System.Windows;
using System.Threading.Tasks;
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
    private readonly ImplementationPlansWorkspaceViewModel _plansVm;
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
        DecisionsWorkspaceViewModel         decisionsVm,
        ImplementationPlansWorkspaceViewModel plansVm,
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
        _plansVm     = plansVm;
        _settingsVm  = settingsVm;
        _tenantContext = tenantContext;

        // Wire child VM navigation callbacks
        _loginVm.OnSignIn = (user, tenant) => 
        {
            CurrentUser = user;
            CurrentTenant = tenant;
            IsAuthenticated = true;
            _tenantContext.SetTenant(tenant.Id);
            NavigateToHub();
        };

        _hubVm.OnOpenProject           = (p) => _ = OpenProjectAsync(p);
        _hubVm.OnCreateProject         = NavigateToCreateProject;
        _createVm.OnProjectCreated     = (p) => _ = CreateAndOpenProjectAsync(p);
        _createVm.OnCancel             = NavigateToHub;
        
        // --- SYNC STATUS FROM OVERVIEW TO SHELL ---
        _overviewVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ProjectOverviewViewModel.Status))
            {
                ActiveStatus = _overviewVm.Status;
                System.Diagnostics.Trace.WriteLine($"[Shell] Syncing status from Overview: {ActiveStatus}");
            }
            if (e.PropertyName == nameof(ProjectOverviewViewModel.Model))
                ActiveModel = _overviewVm.Model;
        };

        // Chat → Ticket creation bridge
        _chatVm.OnCreateTicketFromChat = (title, summary, linkedFilePaths, linkedSymbols) =>
        {
            _ticketsVm.PrefillFromChat(title, summary, linkedFilePaths, linkedSymbols);
            CurrentWorkspace = ProjectWorkspace.Tickets;
            CurrentView = _ticketsVm;
        };

        // Chat → Plan creation bridge: navigate to Plans workspace and prefill editor
        _chatVm.OnCreatePlanFromChat = (title, goal, steps, linkedFilePaths, linkedSymbols) =>
        {
            _plansVm.PrefillFromChat(title, goal, steps, linkedFilePaths, linkedSymbols);
            CurrentWorkspace = ProjectWorkspace.Plans;
            CurrentView = _plansVm;
        };

        // Ticket → Plan refinement bridge
        _ticketsVm.OnAskAboutPlan = (ticketId, ticketTitle, planContent, linkedFilePaths, linkedSymbols) =>
        {
            var prompt = $"I am looking at the implementation plan for ticket '{ticketTitle}'.\n\nPLAN DETAILS:\n{planContent}\n\nCan you help me refine this plan, check for risks, or identify missing steps?";
            _chatVm.PromptText = prompt;
            CurrentWorkspace = ProjectWorkspace.Chat;
            CurrentView = _chatVm;
        };

        // Chat → Decision creation bridge
        _chatVm.OnCreateDecisionFromChat = (title, detail, linkedFilePaths, linkedSymbols) =>
        {
            _decisionsVm.PrefillFromChat(title, detail, linkedFilePaths, linkedSymbols);
            CurrentWorkspace = ProjectWorkspace.Decisions;
            CurrentView = _decisionsVm;
        };

        // Chat quick-nav: Plans / Tickets / Decisions
        _chatVm.OnNavigateToPlan = () =>
        {
            CurrentWorkspace = ProjectWorkspace.Plans;
            CurrentView = _plansVm;
        };
        _chatVm.OnNavigateToTicket = () =>
        {
            CurrentWorkspace = ProjectWorkspace.Tickets;
            CurrentView = _ticketsVm;
        };
        _chatVm.OnNavigateToDecision = () =>
        {
            CurrentWorkspace = ProjectWorkspace.Decisions;
            CurrentView = _decisionsVm;
        };

        CurrentView = _loginVm;
    }

    // ── Navigation commands ──────────────────────────────────────────────────

    [RelayCommand]
    private void NavigateWorkspace(string workspaceName)
    {
        if (workspaceName == "ProjectHub" || workspaceName == "SwitchProject")
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
            ProjectWorkspace.Plans      => _plansVm,
            ProjectWorkspace.Decisions  => _decisionsVm,
            ProjectWorkspace.Settings   => _settingsVm,
            _                           => _overviewVm
        };
    }

    [RelayCommand]
    private void SignOut()
    {
        IsAuthenticated = false;
        CurrentUser = null;
        CurrentTenant = null;
        HasActiveProject = false;
        ActiveProjectName = string.Empty;
        ActiveProjectPath = string.Empty;
        
        _tenantContext.SetTenant(0);
        CurrentShellMode = ShellMode.Login;
        CurrentView = _loginVm;
    }

    [RelayCommand]
    private void OpenMyAccount()
    {
        // Placeholder for My Account
        MessageBox.Show("My Account details coming soon.", "IronDev", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        NavigateWorkspace("Settings");
    }

    [RelayCommand]
    private void ToggleStatusPopup()
    {
        IsStatusPopupOpen = !IsStatusPopupOpen;
    }

    [RelayCommand]
    private async Task IndexNow()
    {
        IsStatusPopupOpen = false;
        
        // Trigger the real indexing command on the overview VM
        if (_overviewVm.IndexProjectCommand.CanExecute(null))
        {
            await _overviewVm.IndexProjectCommand.ExecuteAsync(null);
            
            // Sync status back to shell
            ActiveStatus = _overviewVm.Status;
        }
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

    private async Task OpenProjectAsync(global::IronDev.Data.Models.Project project)
    {
        await ActivateProjectAsync(project);
    }

    private void NavigateToCreateProject()
    {
        CurrentShellMode = ShellMode.CreateProject;
        CurrentView = _createVm;
    }

    private async Task CreateAndOpenProjectAsync(global::IronDev.Data.Models.Project project)
    {
        await ActivateProjectAsync(project);
    }

    private async Task ActivateProjectAsync(global::IronDev.Data.Models.Project project)
    {
        System.Diagnostics.Trace.WriteLine($"[Shell] Activating project: {project.Name}");
        
        // Switch to project mode and show overview immediately
        HasActiveProject  = true;
        CurrentShellMode  = ShellMode.ProjectActive;
        CurrentWorkspace  = ProjectWorkspace.Overview;
        CurrentView       = _overviewVm;
        
        ActiveProjectName = project.Name;
        ActiveProjectPath = project.LocalPath ?? string.Empty;
        ActiveModel       = _overviewVm.Model; 
        ActiveStatus      = "Checking...";

        // Push user/tenant context into overview VM for the Current State card
        _overviewVm.CurrentUserDisplayName = CurrentUser?.DisplayName ?? "Unknown";
        _overviewVm.CurrentUserEmail       = CurrentUser?.Email       ?? "Email not available";
        _overviewVm.CurrentTenantName      = CurrentTenant?.Name      ?? "No tenant";
        _overviewVm.CurrentWorkspaceName   = "Overview";
        
        try
        {
            // Populate child ViewModels with real data
            await Task.WhenAll(
                _overviewVm.LoadAsync(project),
                _chatVm.LoadAsync(project),
                _ticketsVm.LoadAsync(project),
                _decisionsVm.LoadAsync(project),
                _plansVm.LoadAsync(project)
            );

            // Fetch final status from the overview VM
            ActiveStatus = _overviewVm.Status;
            System.Diagnostics.Trace.WriteLine($"[Shell] Project activation complete. Status: {ActiveStatus}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[Shell] ERROR activating project {project.Name}: {ex.Message}");
            ActiveStatus = "Error";
        }
        finally
        {
            // Safeguard: do not leave stuck on "Checking..." if success/fail didn't update it
            if (ActiveStatus == "Checking...")
                ActiveStatus = "Offline";

            OnPropertyChanged(nameof(StatusNeedsIndex));
        }
    }
}
