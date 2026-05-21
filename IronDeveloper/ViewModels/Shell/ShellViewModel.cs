using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly KnowledgeCompilerViewModel _knowledgeCompilerVm;
    private readonly ChatWorkspaceViewModel   _chatVm;
    private readonly TicketsWorkspaceViewModel _ticketsVm;
    private readonly TestingCompanionViewModel _testingVm;
    private readonly DecisionsWorkspaceViewModel _decisionsVm;
    private readonly DocumentsWorkspaceViewModel _documentsVm;
    private readonly ImplementationPlansWorkspaceViewModel _plansVm;
    private readonly DevToolsWorkspaceViewModel _devToolsVm;
    private readonly SettingsWorkspaceViewModel  _settingsVm;
    private readonly BuilderWorkspaceViewModel   _builderVm;
    private readonly ProjectProfileViewModel     _profileVm;
    private readonly AgentTenantContext          _tenantContext;
    private readonly Dictionary<ProjectWorkspace, bool> _inspectorCollapsedByWorkspace = new();
    private ProjectWorkspace _lastWorkspaceBeforeTesting = ProjectWorkspace.Overview;

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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusNeedsIndex))]
    [NotifyPropertyChangedFor(nameof(StatusCanIndex))]
    private string _activeStatus = string.Empty;

    // ── Status popup ─────────────────────────────────────────────────────────

    [ObservableProperty] private bool _isStatusPopupOpen;

    // ── Derived ──────────────────────────────────────────────────────────────

    public bool HasActiveTenant => CurrentTenant != null;
    public bool ShowSidebar => HasActiveProject && CurrentShellMode == ShellMode.ProjectActive;
    public bool ShowHeader  => HasActiveProject && CurrentShellMode == ShellMode.ProjectActive;
    public bool StatusNeedsIndex => IsIndexActionableStatus(ActiveStatus);
    public bool StatusCanIndex => IsIndexActionableStatus(ActiveStatus);
    public bool CanUseTestingCompanion => true;
    public TestingCompanionViewModel TestingCompanion => _testingVm;
    [ObservableProperty] private bool _isContextInspectorCollapsed;
    public string CurrentWorkspaceDisplayName => GetWorkspaceDisplayName(CurrentWorkspace);
    public string ContextDomain => IsIronDevProductContext() ? "IronDev Product" : "External Project";
    public string MemoryStatusText => string.IsNullOrWhiteSpace(ActiveStatus)
        ? "Memory unknown"
        : $"Index {ActiveStatus}";
    public string SelectedObjectContextText => CurrentView switch
    {
        DocumentsWorkspaceViewModel documents => documents.SelectedDocument?.Title ?? "No document selected",
        TicketsWorkspaceViewModel tickets => tickets.SelectedTicket?.Title ?? (tickets.HasDetail ? tickets.EditTitle : "No ticket selected"),
        DecisionsWorkspaceViewModel => "Project knowledge",
        ChatWorkspaceViewModel => "Current conversation",
        KnowledgeCompilerViewModel knowledgeCompiler => knowledgeCompiler.SelectedDiscussion?.Title ?? "Knowledge Compiler",
        TestingCompanionViewModel => "Testing session",
        BuilderWorkspaceViewModel => "Build workflow",
        DevToolsWorkspaceViewModel => "Diagnostics workspace",
        _ => CurrentWorkspaceDisplayName
    };
    public string SourceVersionContextText => CurrentView switch
    {
        DocumentsWorkspaceViewModel documents => documents.SelectedVersion?.VersionLabel is { Length: > 0 } version
            ? $"Source/version: {version}"
            : "Source/version: none selected",
        TicketsWorkspaceViewModel tickets when tickets.SelectedTicket != null => $"Source: Ticket #{tickets.SelectedTicket.Id}",
        KnowledgeCompilerViewModel => "Source: project summary, discussions, and proposals",
        DevToolsWorkspaceViewModel => "Source: traces, test reports, and prompt runs",
        _ => "Source/version: current workspace"
    };
    public string RelatedContextText => CurrentWorkspace switch
    {
        ProjectWorkspace.Documents => "Related tickets and decisions load from document context.",
        ProjectWorkspace.Tickets => "Related docs, decisions, and build traces load from ticket context.",
        ProjectWorkspace.Chat => "Related memory appears in route and LLM traces.",
        ProjectWorkspace.Discovery => $"Discussions: {_knowledgeCompilerVm.DiscussionDocuments.Count} | Proposals: {_knowledgeCompilerVm.Proposals.Count} | Selected: {_knowledgeCompilerVm.Proposals.Count(p => p.IsSelected)}",
        ProjectWorkspace.DevTools => "LLM traces, test defects, and prompt experiments.",
        _ => "Related items are available in workspace-specific panels."
    };
    public string LatestTraceContextText => CurrentWorkspace switch
    {
        ProjectWorkspace.DevTools => "Trace tools are open in this workspace.",
        ProjectWorkspace.Discovery => "Apply selected proposals to create project memory.",
        _ => "Open Dev Tools for LLM and route traces."
    };

    // ── Constructor ──────────────────────────────────────────────────────────

    public ShellViewModel(
        LoginViewModel             loginVm,
        ProjectHubViewModel        hubVm,
        CreateProjectViewModel     createVm,
        ProjectOverviewViewModel   overviewVm,
        KnowledgeCompilerViewModel knowledgeCompilerVm,
        ChatWorkspaceViewModel     chatVm,
        TicketsWorkspaceViewModel  ticketsVm,
        TestingCompanionViewModel  testingVm,
        DecisionsWorkspaceViewModel          decisionsVm,
        DocumentsWorkspaceViewModel          documentsVm,
        ImplementationPlansWorkspaceViewModel plansVm,
        DevToolsWorkspaceViewModel           devToolsVm,
        SettingsWorkspaceViewModel  settingsVm,
        BuilderWorkspaceViewModel   builderVm,
        ProjectProfileViewModel     profileVm,
        AgentTenantContext          tenantContext)
    {
        _loginVm     = loginVm;
        _hubVm       = hubVm;
        _createVm    = createVm;
        _overviewVm  = overviewVm;
        _knowledgeCompilerVm = knowledgeCompilerVm;
        _chatVm      = chatVm;
        _ticketsVm   = ticketsVm;
        _testingVm   = testingVm;
        _decisionsVm = decisionsVm;
        _documentsVm = documentsVm;
        _plansVm     = plansVm;
        _devToolsVm  = devToolsVm;
        _settingsVm  = settingsVm;
        _builderVm   = builderVm;
        _profileVm   = profileVm;
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
        _hubVm.AttachWizard(_createVm);
        _createVm.OnProjectCreated     = (p) => _ = CreateAndOpenProjectAsync(p);
        _createVm.OnCancel             = _hubVm.CloseWizard;
        
        // ——— SYNC STATUS FROM OVERVIEW TO SHELL AND WORKSPACE VMs ———
        _overviewVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ProjectOverviewViewModel.Status))
            {
                ActiveStatus = _overviewVm.Status;
                System.Diagnostics.Trace.WriteLine($"[Shell] Syncing status from Overview: {ActiveStatus}");

                // Keep Tickets workspace index-awareness in sync
                _ticketsVm.SetIndexStatus(ActiveStatus);
            }
            if (e.PropertyName == nameof(ProjectOverviewViewModel.Model))
                ActiveModel = _overviewVm.Model;
        };

        // Tickets workspace → Index Project: delegate to the existing IndexNowCommand
        _ticketsVm.OnRequestIndex = () =>
        {
            _ = IndexNow();   // existing command — pops status popup and calls overviewVm.IndexProjectCommand
        };

        // Ticket → Builder Proposal bridge
        _ticketsVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TicketsWorkspaceViewModel.SelectedTicket) or nameof(TicketsWorkspaceViewModel.EditTitle) or nameof(TicketsWorkspaceViewModel.HasDetail))
                RaiseContextInspectorProperties();
        };
        _documentsVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DocumentsWorkspaceViewModel.SelectedDocument) or nameof(DocumentsWorkspaceViewModel.SelectedVersion))
                RaiseContextInspectorProperties();
        };

        _ticketsVm.OnRequestProposal = (ticketId) =>
        {
            _ = _builderVm.GenerateProposalForTicketAsync(ticketId);
            CurrentWorkspace = ProjectWorkspace.Builder;
            CurrentView = _builderVm;
        };

        _builderVm.OnProjectIndexStatusChanged = (status) =>
        {
            ActiveStatus = string.IsNullOrWhiteSpace(status) ? "Needs Index" : status;
            _ticketsVm.SetIndexStatus(ActiveStatus);
            OnPropertyChanged(nameof(StatusNeedsIndex));
            OnPropertyChanged(nameof(StatusCanIndex));
        };

        // Chat → Ticket draft review bridge
        _chatVm.OnCreateTicketFromChat = (ctx) =>
        {
            _ = _ticketsVm.BeginDraftFromChatAsync(ctx);
            CurrentWorkspace = ProjectWorkspace.Tickets;
            CurrentView = _ticketsVm;
        };
        _chatVm.OnCreateTicketsFromChat = (contexts) =>
        {
            _ = _ticketsVm.BeginDraftsFromChatAsync(contexts);
            CurrentWorkspace = ProjectWorkspace.Tickets;
            CurrentView = _ticketsVm;
        };

        // Ticket draft cancelled → navigate back to Chat
        _ticketsVm.OnCancelDraft = () =>
        {
            CurrentWorkspace = ProjectWorkspace.Chat;
            CurrentView = _chatVm;
        };

        _testingVm.OnRequestReturnToWork = NavigateBackFromTesting;

        // Ticket draft approved with plan → navigate to Plans after save
        _ticketsVm.OnApproveDraftWithPlan = (title, goal, steps, filePaths, symbols, scope, risks) =>
        {
            _plansVm.PrefillFromChat(title, goal, steps, filePaths, symbols, scope, risks);
            CurrentWorkspace = ProjectWorkspace.Plans;
            CurrentView = _plansVm;
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
        _chatVm.OnCreateDecisionFromChat = (title, detail, linkedFilePaths, linkedSymbols, sourceDocumentId) =>
        {
            _decisionsVm.PrefillFromChat(title, detail, linkedFilePaths, linkedSymbols, sourceDocumentId);
            CurrentWorkspace = ProjectWorkspace.Decisions;
            CurrentView = _decisionsVm;
        };

        _chatVm.OnCreateDocumentFromChat = (title, content, summary, linkedFilePaths, linkedSymbols, sourceDocumentId) =>
        {
            _decisionsVm.PrefillDocumentFromChat(title, content, summary, linkedFilePaths, linkedSymbols, sourceDocumentId);
            CurrentWorkspace = ProjectWorkspace.Decisions;
            CurrentView = _decisionsVm;
        };

        _decisionsVm.OnDiscussDocumentInChat = (prompt) =>
        {
            _chatVm.PromptText = prompt;
            CurrentWorkspace = ProjectWorkspace.Chat;
            CurrentView = _chatVm;
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

        // ── Propagate SettingsWorkspaceViewModel flags → Chat VM ─────────────
        _chatVm.UseContextAgent = _settingsVm.UseContextAgent; // sync initial value
        _settingsVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsWorkspaceViewModel.UseContextAgent))
                _chatVm.UseContextAgent = _settingsVm.UseContextAgent;
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

        if (!HasActiveProject || CurrentShellMode != ShellMode.ProjectActive)
            return;

        if (!System.Enum.TryParse<ProjectWorkspace>(workspaceName, out var ws)) return;
        if (!ConfirmDiscardDirtyWorkspace())
            return;

        if (ws == ProjectWorkspace.Testing && CurrentWorkspace != ProjectWorkspace.Testing)
        {
            _lastWorkspaceBeforeTesting = CurrentWorkspace;
            _testingVm.SetReturnWorkspace(GetWorkspaceDisplayName(_lastWorkspaceBeforeTesting));
        }

        CurrentWorkspace = ws;
        CurrentView = ws switch
        {
            ProjectWorkspace.Overview       => _overviewVm,
            ProjectWorkspace.Discovery      => _knowledgeCompilerVm,
            ProjectWorkspace.Chat           => _chatVm,
            ProjectWorkspace.Tickets        => _ticketsVm,
            ProjectWorkspace.Testing        => _testingVm,
            ProjectWorkspace.Plans          => _plansVm,
            ProjectWorkspace.Decisions      => _decisionsVm,
            ProjectWorkspace.Documents      => _documentsVm,
            ProjectWorkspace.DevTools       => _devToolsVm,
            ProjectWorkspace.Settings       => _settingsVm,
            ProjectWorkspace.Builder        => _builderVm,
            ProjectWorkspace.ProjectProfile => _profileVm,
            _                               => _overviewVm
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

    public async Task MarkTestingMomentAsync()
    {
        if (CurrentWorkspace != ProjectWorkspace.Testing)
        {
            _lastWorkspaceBeforeTesting = CurrentWorkspace;
            _testingVm.SetReturnWorkspace(GetWorkspaceDisplayName(_lastWorkspaceBeforeTesting));
        }

        await _testingVm.EnsureSessionStartedAsync();

        await _testingVm.MarkMomentForWorkspaceAsync(GetActiveTestingContextName());
    }

    public async Task EnsureTestingSessionStartedAsync()
    {
        if (CurrentWorkspace != ProjectWorkspace.Testing)
        {
            _lastWorkspaceBeforeTesting = CurrentWorkspace;
            _testingVm.SetReturnWorkspace(GetWorkspaceDisplayName(_lastWorkspaceBeforeTesting));
        }

        await _testingVm.EnsureSessionStartedAsync();
    }

    private void NavigateBackFromTesting()
    {
        if (!HasActiveProject || CurrentShellMode != ShellMode.ProjectActive)
            return;

        NavigateWorkspace(_lastWorkspaceBeforeTesting.ToString());
    }

    private string GetActiveTestingContextName()
    {
        if (CurrentShellMode != ShellMode.ProjectActive)
            return CurrentShellMode.ToString();

        return GetWorkspaceDisplayName(CurrentWorkspace);
    }

    private static string GetWorkspaceDisplayName(ProjectWorkspace workspace)
        => workspace switch
        {
            ProjectWorkspace.ProjectProfile => "Profile",
            ProjectWorkspace.Discovery => "Discovery",
            _ => workspace.ToString()
        };

    private bool ConfirmDiscardDirtyWorkspace()
    {
        if (CurrentView is not IWorkspaceDirtyState dirty || !dirty.HasDirtyEditState)
            return true;

        var message = string.IsNullOrWhiteSpace(dirty.DirtyEditMessage)
            ? "You have unsaved changes. Leave this workspace and discard them?"
            : dirty.DirtyEditMessage;

        return MessageBox.Show(
            message,
            "Unsaved Changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private bool IsIronDevProductContext()
    {
        if (!string.IsNullOrWhiteSpace(ActiveProjectName) &&
            ActiveProjectName.Contains("IronDev", StringComparison.OrdinalIgnoreCase))
            return true;

        return !string.IsNullOrWhiteSpace(ActiveProjectPath) &&
               ActiveProjectPath.Contains("IronDeveloper", StringComparison.OrdinalIgnoreCase);
    }

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

    private async Task CreateAndOpenProjectAsync(global::IronDev.Data.Models.Project project)
    {
        _hubVm.CloseWizard();
        await ActivateProjectAsync(project);
    }

    private async Task ActivateProjectAsync(global::IronDev.Data.Models.Project project)
    {
        Serilog.Log.Information("[ShellDebug] Activating project: {ProjectName}", project.Name);
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

        RaiseAllActiveProjectProperties();

        // Track active project so cross-cutting VMs (e.g. Playground) resolve the right project
        _tenantContext.SetProject(project.Id);
        System.Diagnostics.Trace.WriteLine($"[Shell] ActiveProjectId set to {project.Id} ({project.Name})");

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
                _knowledgeCompilerVm.LoadAsync(project),
                _chatVm.LoadAsync(project),
                _ticketsVm.LoadAsync(project),
                _testingVm.LoadAsync(project),
                _decisionsVm.LoadAsync(project),
                _documentsVm.LoadAsync(project),
                _plansVm.LoadAsync(project),
                _profileVm.LoadAsync(project)
            );

            // Fetch final status from the overview VM
            ActiveStatus = _overviewVm.Status;
            ActiveModel = _overviewVm.Model;
            _ticketsVm.SetIndexStatus(ActiveStatus);   // propagate initial index state
            System.Diagnostics.Trace.WriteLine($"[Shell] Project activation complete. Status: {ActiveStatus}");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[ShellDebug] ERROR activating project {ProjectName}", project.Name);
            System.Diagnostics.Trace.WriteLine($"[Shell] ERROR activating project {project.Name}: {ex.Message}");
            ActiveStatus = "Error";
        }
        finally
        {
            // Safeguard: do not leave stuck on "Checking..." if success/fail didn't update it
            if (ActiveStatus == "Checking...")
                ActiveStatus = "Offline";

            if (CurrentWorkspace == default)
                CurrentWorkspace = ProjectWorkspace.Overview;

            NavigateWorkspace(CurrentWorkspace.ToString());
            if (CurrentView == null)
                CurrentView = _overviewVm;

            RaiseAllActiveProjectProperties();

            Serilog.Log.Information("[ShellDebug] Project activation diagnostics complete: ProjectName='{ProjectName}', Path='{Path}', Model='{Model}', Status='{Status}', Workspace='{Workspace}', ViewIsNull='{ViewIsNull}', View='{View}', ShowHeader='{ShowHeader}', HasActiveProject='{HasActiveProject}'",
                ActiveProjectName, ActiveProjectPath, ActiveModel, ActiveStatus, CurrentWorkspace, CurrentView == null, CurrentView?.GetType().FullName, ShowHeader, HasActiveProject);

            System.Diagnostics.Trace.WriteLine($"[ShellDebug] Project activation diagnostics:");
            System.Diagnostics.Trace.WriteLine($" - ActiveProjectName: '{ActiveProjectName}'");
            System.Diagnostics.Trace.WriteLine($" - ActiveProjectPath: '{ActiveProjectPath}'");
            System.Diagnostics.Trace.WriteLine($" - ActiveModel: '{ActiveModel}'");
            System.Diagnostics.Trace.WriteLine($" - ActiveStatus: '{ActiveStatus}'");
            System.Diagnostics.Trace.WriteLine($" - CurrentWorkspace: '{CurrentWorkspace}'");
            System.Diagnostics.Trace.WriteLine($" - CurrentView == null: '{CurrentView == null}'");
            System.Diagnostics.Trace.WriteLine($" - CurrentView.GetType().FullName: '{(CurrentView != null ? CurrentView.GetType().FullName : "null")}'");
            System.Diagnostics.Trace.WriteLine($" - ShowHeader: '{ShowHeader}'");
            System.Diagnostics.Trace.WriteLine($" - HasActiveProject: '{HasActiveProject}'");
        }
    }

    private void RaiseAllActiveProjectProperties()
    {
        OnPropertyChanged(nameof(ActiveProjectName));
        OnPropertyChanged(nameof(ActiveProjectPath));
        OnPropertyChanged(nameof(ActiveModel));
        OnPropertyChanged(nameof(ActiveStatus));
        OnPropertyChanged(nameof(ShowHeader));
        OnPropertyChanged(nameof(ShowSidebar));
        OnPropertyChanged(nameof(HasActiveProject));
        OnPropertyChanged(nameof(CurrentView));
        OnPropertyChanged(nameof(CurrentWorkspace));
        OnPropertyChanged(nameof(CurrentWorkspaceDisplayName));
        OnPropertyChanged(nameof(ContextDomain));
        OnPropertyChanged(nameof(MemoryStatusText));
        RaiseContextInspectorProperties();
    }

    private void RaiseContextInspectorProperties()
    {
        OnPropertyChanged(nameof(SelectedObjectContextText));
        OnPropertyChanged(nameof(SourceVersionContextText));
        OnPropertyChanged(nameof(RelatedContextText));
        OnPropertyChanged(nameof(LatestTraceContextText));
    }

    private static bool IsIndexActionableStatus(string status)
    {
        return !string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(status, "Checking...", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(status, "Indexing...", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(status, "Offline", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(status);
    }

    partial void OnActiveStatusChanged(string value)
    {
        RaiseAllActiveProjectProperties();
    }

    partial void OnCurrentWorkspaceChanged(ProjectWorkspace value)
    {
        IsContextInspectorCollapsed = _inspectorCollapsedByWorkspace.TryGetValue(value, out var collapsed) && collapsed;
        RaiseAllActiveProjectProperties();
    }

    partial void OnIsContextInspectorCollapsedChanged(bool value)
    {
        if (CurrentShellMode == ShellMode.ProjectActive)
            _inspectorCollapsedByWorkspace[CurrentWorkspace] = value;
    }

    partial void OnCurrentViewChanged(object value)
    {
        RaiseContextInspectorProperties();
    }

    partial void OnHasActiveProjectChanged(bool value)
    {
        RaiseAllActiveProjectProperties();
    }
}
