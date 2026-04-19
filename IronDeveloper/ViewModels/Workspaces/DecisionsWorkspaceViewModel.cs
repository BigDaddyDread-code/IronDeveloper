using System.Collections.ObjectModel;
using System.Threading.Tasks;
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
    }

    internal async Task LoadAsync(global::IronDev.Data.Models.Project project)
    {
        Decisions.Clear();
        var decisions = await _memoryService.GetRecentDecisionsAsync(project.Id, take: 50);
        foreach (var d in decisions)
        {
            Decisions.Add(new DecisionItem 
            { 
                Id = d.Id, 
                Title = d.Title, 
                Summary = d.Detail,
                Reason = d.Reason
            });
        }
    }
}
