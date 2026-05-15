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

    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private BuilderProposal? _currentProposal;
    [ObservableProperty] private ProposedFileChange? _selectedFileChange;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _projectName = "No Project Active";
    [ObservableProperty] private string _projectRoot = string.Empty;

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
        }
    }

    partial void OnSelectedFileChangeChanged(ProposedFileChange? value)
    {
        // View will bind to this to show the diff
    }
}
