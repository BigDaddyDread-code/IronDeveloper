using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Agent.ViewModels.Workspaces;

public partial class BuilderWorkspaceViewModel : ObservableObject
{
    private readonly IBuilderProposalService _proposalService;
    private readonly ILlmTraceService _traceService;

    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isGenerating;

    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isApplying;
    [ObservableProperty] private BuilderProposal? _currentProposal;
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

    public bool IsBusy => IsGenerating || IsApplying;

    public ObservableCollection<ProposedFileChange> ProposedFiles { get; } = new();

    public BuilderWorkspaceViewModel(
        IBuilderProposalService proposalService,
        ILlmTraceService traceService)
    {
        _proposalService = proposalService;
        _traceService = traceService;
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
        var msg = $"Apply this proposal to {ProjectRoot}?\n\nThis will modify files under the active project root.";
        // Note: For Phase 1 we use a simple MessageBox or similar via a service if available.
        // For now, we'll assume the USER confirmed via the button click itself, 
        // but in a real app we'd inject a dialog service.
        
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

            StatusMessage = "Apply workflow completed.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Apply failed: {ex.Message}";
            ApplyStatus = "Error";
        }
        finally
        {
            IsApplying = false;
            ApplyProposalCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanApply()
    {
        return CurrentProposal != null 
            && CurrentProposal.IsAllValid 
            && !IsGenerating 
            && !IsApplying 
            && ProjectRoot.Contains("BookSeller", StringComparison.OrdinalIgnoreCase);
    }

    private void ResetExecutionState()
    {
        ApplyStatus = "Not Started";
        BuildStatus = "Not Started";
        TestStatus = "Not Started";
        BuildOutput = string.Empty;
        TestOutput = string.Empty;
    }

    partial void OnSelectedFileChangeChanged(ProposedFileChange? value)
    {
        // View will bind to this to show the diff
    }
}
