using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Agent.Services;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Services;
using IronDeveloperControls.Primitives;

namespace IronDev.Agent.ViewModels.Workspaces;

public partial class BuilderWorkspaceViewModel : ObservableObject
{
    private readonly IBuilderProposalService _proposalService;
    private readonly ILlmTraceService _traceService;
    private readonly IProjectProfileService _profileService;
    private readonly IProjectMemoryService _memoryService;
    private readonly IBuilderReadinessService _readinessService;
    private readonly IProjectService _projectService;
    private readonly ITicketService _ticketService;
    private readonly IAppSettingsService? _settingsService;

    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isGenerating;

    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isApplying;
    [ObservableProperty] private BuilderProposal? _currentProposal;
    [ObservableProperty] private bool _hasReconciliation;
    [ObservableProperty] private BuildArchitectureReconciliation? _reconciliation;
    [ObservableProperty] private ProposedFileChange? _selectedFileChange;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _projectName = "No Project Active";
    [ObservableProperty] private string _projectRoot = string.Empty;

    // ── Execution Output ──────────────────────────────────────────────────
    [ObservableProperty] private string _applyStatus = "Not Started";
    [ObservableProperty] private string _buildStatus = "Not Started";
    [ObservableProperty] private string _testStatus = "Not Started";
    [ObservableProperty] private string _buildOutput = string.Empty;
    [ObservableProperty] private string _testOutput = string.Empty;
    [ObservableProperty] private BuildReadinessResult? _readiness;
    [ObservableProperty] private bool _profileAllowsApply;
    [ObservableProperty] private bool _showReadinessCallout;
    [ObservableProperty] private string _readinessTitle = "Build readiness";
    [ObservableProperty] private string _readinessMessage = "Generate a proposal to evaluate build readiness.";
    [ObservableProperty] private string _readinessDetails = string.Empty;
    [ObservableProperty] private string _readinessBadgeText = "NOT EVALUATED";
    [ObservableProperty] private BadgeStatus _readinessBadgeStatus = BadgeStatus.Info;

    public bool IsBusy => IsGenerating || IsApplying;
    public bool IsApplyEnabled => ProfileAllowsApply && (Readiness?.IsReady ?? false);
    public string ApplyModeLabel => IsApplyEnabled ? "Apply Enabled" : "PROPOSAL-ONLY MODE";
    public string ApplyModeColor => IsApplyEnabled ? "#34D16A" : "#7C6BE8";

    public ObservableCollection<ProposedFileChange> ProposedFiles { get; } = new();

    public BuilderWorkspaceViewModel(
        IBuilderProposalService proposalService,
        ILlmTraceService traceService,
        IProjectProfileService profileService,
        IProjectMemoryService memoryService,
        IBuilderReadinessService readinessService,
        IProjectService projectService,
        ITicketService ticketService,
        IAppSettingsService? settingsService = null)
    {
        _proposalService = proposalService;
        _traceService = traceService;
        _profileService = profileService;
        _memoryService = memoryService;
        _readinessService = readinessService;
        _projectService = projectService;
        _ticketService = ticketService;
        _settingsService = settingsService;
    }

    [RelayCommand]
    public async Task GenerateProposalForTicketAsync(long ticketId)
    {
        IsGenerating = true;
        StatusMessage = "Assembling context and calling AI...";
        ResetExecutionState();
        ProposedFiles.Clear();
        SelectedFileChange = null;
        CurrentProposal = null;

        try
        {
            // Evaluate readiness first
            var ticket = await _ticketService.GetTicketByIdAsync(ticketId);
            if (ticket != null)
            {
                var project = await _projectService.GetByIdAsync(ticket.ProjectId);
                ProjectName = project?.Name ?? ProjectName;
                ProjectRoot = project?.LocalPath ?? ProjectRoot;
                ProfileAllowsApply = (await _profileService.GetProjectProfileAsync(ticket.ProjectId))?.AllowBuilderApply ?? false;

                Readiness = await _readinessService.EvaluateReadinessAsync(ticket.ProjectId, ticketId);
                if (!Readiness.IsReady)
                {
                    StatusMessage = $"Build blocked: {Readiness.Message}";
                    IsGenerating = false;
                    return;
                }
            }

            var proposal = await _proposalService.GenerateProposalAsync(ticketId);
            CurrentProposal = proposal;
            ProjectName = proposal.ProjectName;
            ProjectRoot = proposal.ProjectRoot;

            foreach (var change in proposal.Changes)
            {
                ProposedFiles.Add(change);
            }

            if (ProposedFiles.Count > 0)
            {
                SelectedFileChange = ProposedFiles[0];
            }

            StatusMessage = proposal.IsAllValid 
                ? "Proposal generated successfully." 
                : proposal.HasValidationIssues
                    ? "Proposal generated but validation blocked apply."
                    : "Proposal generated with validation warnings.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Generation failed: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
            ApplyProposalCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyProposalAsync()
    {
        if (CurrentProposal == null) return;

        // ── 1. Confirmation ───────────────────────────────────────────────────
        if (!ConfirmFullBuildCycle())
        {
            StatusMessage = "Apply/build/test cycle cancelled.";
            _traceService.AddTrace(new LlmTraceEntry
            {
                FeatureName = "Builder.ApplyApprovalCancelled",
                WorkspaceName = "Builder",
                ProjectId = CurrentProposal.ProjectId,
                TicketId = CurrentProposal.TicketId,
                ActiveProjectName = CurrentProposal.ProjectName,
                ActiveProjectPath = CurrentProposal.ProjectRoot,
                ProposedFileCount = CurrentProposal.Changes.Count,
                ProposedFilesList = string.Join(", ", CurrentProposal.Changes.Select(c => c.FilePath)),
                WasSuccessful = false,
                ParsedResponseSummary = "User cancelled the apply/build/test approval prompt.",
                CreatedAt = DateTime.UtcNow
            });
            return;
        }

        _traceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.ApplyApprovalGranted",
            WorkspaceName = "Builder",
            ProjectId = CurrentProposal.ProjectId,
            TicketId = CurrentProposal.TicketId,
            ActiveProjectName = CurrentProposal.ProjectName,
            ActiveProjectPath = CurrentProposal.ProjectRoot,
            ProposedFileCount = CurrentProposal.Changes.Count,
            ProposedFilesList = string.Join(", ", CurrentProposal.Changes.Select(c => c.FilePath)),
            WasSuccessful = true,
            ParsedResponseSummary = "User approved the full apply/build/test cycle.",
            CreatedAt = DateTime.UtcNow
        });
        
        IsApplying = true;
        StatusMessage = "Applying changes...";
        
        try
        {
            await _proposalService.ApplyProposalAsync(CurrentProposal);
            
            // Sync status back to VM
            ApplyStatus = CurrentProposal.ApplyStatus;
            BuildStatus = CurrentProposal.BuildStatus;
            TestStatus = CurrentProposal.TestStatus;
            BuildOutput = CurrentProposal.BuildOutput ?? string.Empty;
            TestOutput = CurrentProposal.TestOutput ?? string.Empty;

            if (CurrentProposal.Reconciliation != null)
            {
                Reconciliation = CurrentProposal.Reconciliation;
                HasReconciliation = true;
            }

            StatusMessage = "Apply workflow completed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Apply failed: {ex.Message}";
            ApplyStatus = CurrentProposal.ApplyStatus ?? "Error";
            BuildStatus = CurrentProposal.BuildStatus ?? "Not Started";
            TestStatus = CurrentProposal.TestStatus ?? "Skipped";
            BuildOutput = CurrentProposal.BuildOutput ?? string.Empty;
            TestOutput = CurrentProposal.TestOutput ?? string.Empty;
        }
        finally
        {
            IsApplying = false;
            ApplyProposalCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsApplyEnabled));
        }
    }

    private bool CanApply()
    {
        return CurrentProposal != null 
            && CurrentProposal.IsAllValid 
            && (Readiness?.IsReady ?? false)
            && !IsGenerating 
            && !IsApplying
            && ProfileAllowsApply;
    }

    private bool ConfirmFullBuildCycle()
    {
        if (_settingsService?.Current.RequireBuilderApplyApproval == false)
            return true;

        var fileList = string.Join(Environment.NewLine, CurrentProposal!.Changes.Select(c => $"  - {c.FilePath}"));
        var message =
            $"Apply this proposal and run the full build/test cycle?{Environment.NewLine}{Environment.NewLine}" +
            $"Project: {ProjectName}{Environment.NewLine}" +
            $"Safe write root: {ProjectRoot}{Environment.NewLine}{Environment.NewLine}" +
            $"Files:{Environment.NewLine}{fileList}{Environment.NewLine}{Environment.NewLine}" +
            "This will write the proposed files, then run the configured build and test commands.";

        var result = System.Windows.MessageBox.Show(
            message,
            "Approve Builder Apply / Build / Test",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        return result == System.Windows.MessageBoxResult.Yes;
    }

    private void ResetExecutionState()
    {
        ApplyStatus = "Not Started";
        BuildStatus = "Not Started";
        TestStatus = "Not Started";
        BuildOutput = string.Empty;
        TestOutput = string.Empty;
        HasReconciliation = false;
        Reconciliation = null;
        OnPropertyChanged(nameof(IsApplyEnabled));
        OnPropertyChanged(nameof(ApplyModeLabel));
        OnPropertyChanged(nameof(ApplyModeColor));
    }

    partial void OnReadinessChanged(BuildReadinessResult? value)
    {
        ApplyReadinessPresentation();
        OnPropertyChanged(nameof(IsApplyEnabled));
        OnPropertyChanged(nameof(ApplyModeLabel));
        OnPropertyChanged(nameof(ApplyModeColor));
        ApplyProposalCommand.NotifyCanExecuteChanged();
    }

    partial void OnProfileAllowsApplyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsApplyEnabled));
        OnPropertyChanged(nameof(ApplyModeLabel));
        OnPropertyChanged(nameof(ApplyModeColor));
        ApplyProposalCommand.NotifyCanExecuteChanged();
    }

    private void ApplyReadinessPresentation()
    {
        if (Readiness == null)
        {
            ShowReadinessCallout = true;
            ReadinessTitle = "Build readiness";
            ReadinessMessage = "Open a saved ticket from Tickets to evaluate build readiness.";
            ReadinessDetails = string.Empty;
            ReadinessBadgeText = "NOT EVALUATED";
            ReadinessBadgeStatus = BadgeStatus.Info;
            return;
        }

        ShowReadinessCallout = true;
        ReadinessTitle = Readiness.IsReady ? "Ready to build" : "Build blocked";
        ReadinessMessage = Readiness.Message;
        ReadinessBadgeText = Readiness.Status.ToString();
        ReadinessBadgeStatus = Readiness.Status switch
        {
            BuildReadinessStatus.ReadyToBuild => BadgeStatus.Ready,
            BuildReadinessStatus.NeedsReindex => BadgeStatus.NeedsIndex,
            BuildReadinessStatus.NeedsClarification => BadgeStatus.Warning,
            BuildReadinessStatus.NeedsArchitectureDecision => BadgeStatus.Warning,
            BuildReadinessStatus.NeedsProjectProfileUpdate => BadgeStatus.Warning,
            BuildReadinessStatus.BlockedByConflict => BadgeStatus.Danger,
            BuildReadinessStatus.BlockedByExistingDecision => BadgeStatus.Danger,
            BuildReadinessStatus.Error => BadgeStatus.Danger,
            _ => BadgeStatus.Info
        };

        var details = new System.Text.StringBuilder();
        foreach (var issue in Readiness.BlockingIssues)
        {
            details.AppendLine($"Blocking: {issue}");
        }
        foreach (var warning in Readiness.Warnings)
        {
            details.AppendLine($"Warning: {warning}");
        }
        ReadinessDetails = details.ToString().TrimEnd();
    }

    [RelayCommand]
    private void ViewBuildDetails()
    {
        ShowDetails("Build Details", BuildOutput);
    }

    [RelayCommand]
    private void ViewTestDetails()
    {
        ShowDetails("Test Details", TestOutput);
    }

    private void ShowDetails(string title, string content)
    {
        var displayContent = string.IsNullOrWhiteSpace(content) ? "No details are available for this operation." : content;
        System.Windows.MessageBox.Show(displayContent, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    partial void OnSelectedFileChangeChanged(ProposedFileChange? value)
    {
        // View will bind to this to show the diff
    }

    [RelayCommand]
    private async Task HandleReconciliationActionAsync(ReconciliationAction? action)
    {
        if (action == null || CurrentProposal == null || Reconciliation == null) return;

        _traceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.ReconciliationApproved",
            WorkspaceName = "Builder",
            ProjectId = CurrentProposal.ProjectId,
            TicketId = CurrentProposal.TicketId,
            RequestText = action.ActionType,
            CreatedAt = DateTime.UtcNow
        });

        if (action.ActionType == "Cancel")
        {
            HasReconciliation = false;
            _traceService.AddTrace(new LlmTraceEntry
            {
                FeatureName = "Builder.ReconciliationCancelled",
                WorkspaceName = "Builder",
                ProjectId = CurrentProposal.ProjectId,
                TicketId = CurrentProposal.TicketId,
                CreatedAt = DateTime.UtcNow
            });
            return;
        }

        StatusMessage = $"Applying reconciliation action: {action.Title}...";

        var profile = await _profileService.GetProjectProfileAsync(CurrentProposal.ProjectId)
            ?? new IronDev.Data.Models.ProjectProfile { ProjectId = CurrentProposal.ProjectId };

        if (action.ActionType == "AddPackage_xUnit" || action.ActionType == "UpdateProfileOnly")
        {
            profile.TestFramework = "xUnit";
            await _profileService.SaveProjectProfileAsync(profile);

            // Save decision
            var decision = new IronDev.Data.Models.ProjectDecision
            {
                ProjectId = CurrentProposal.ProjectId,
                Title = "Test Framework: xUnit",
                Detail = "Use xUnit for unit tests. Tests should be generated using xUnit Fact and Theory attributes.",
                Reason = "Determined via Build-Time Architecture Reconciliation.",
                Category = "Architecture",
                Status = "Approved"
            };
            await _memoryService.SaveDecisionAsync(decision);

            _traceService.AddTrace(new LlmTraceEntry
            {
                FeatureName = "Builder.ProfileUpdateProposed",
                WorkspaceName = "Builder",
                ProjectId = CurrentProposal.ProjectId,
                TicketId = CurrentProposal.TicketId,
                ParsedResponseSummary = "Updated profile TestFramework to xUnit",
                CreatedAt = DateTime.UtcNow
            });
            
            _traceService.AddTrace(new LlmTraceEntry
            {
                FeatureName = "Builder.DecisionUpdateProposed",
                WorkspaceName = "Builder",
                ProjectId = CurrentProposal.ProjectId,
                TicketId = CurrentProposal.TicketId,
                ParsedResponseSummary = "Saved decision: Test Framework is xUnit",
                CreatedAt = DateTime.UtcNow
            });
        }
        else if (action.ActionType == "RegenerateTests")
        {
            // Just clear the reconciliation so they can regenerate
            // In a full implementation we would actually trigger a regenerate.
            _traceService.AddTrace(new LlmTraceEntry
            {
                FeatureName = "Builder.DecisionUpdateProposed",
                WorkspaceName = "Builder",
                ProjectId = CurrentProposal.ProjectId,
                TicketId = CurrentProposal.TicketId,
                ParsedResponseSummary = "User chose to regenerate tests.",
                CreatedAt = DateTime.UtcNow
            });
        }

        HasReconciliation = false;
        StatusMessage = "Reconciliation complete. You may need to regenerate the proposal.";
    }
}
