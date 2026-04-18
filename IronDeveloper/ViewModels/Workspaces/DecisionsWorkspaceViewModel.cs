using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IronDev.Agent.Models;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class DecisionsWorkspaceViewModel : ObservableObject
{
    private readonly global::IronDev.Services.IProjectMemoryService _memoryService;

    [ObservableProperty] private ObservableCollection<DecisionItem> _decisions = [];
    [ObservableProperty] private DecisionItem? _selectedDecision;

    public DecisionsWorkspaceViewModel(global::IronDev.Services.IProjectMemoryService memoryService)
    {
        _memoryService = memoryService;

        // TODO: Load real decisions from _memoryService in future sprint
    }
}
