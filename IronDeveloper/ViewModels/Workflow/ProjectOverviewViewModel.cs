using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IronDev.Agent.Models;

namespace IronDev.Agent.ViewModels.Workflow;

public sealed partial class ProjectOverviewViewModel : ObservableObject
{
    private readonly global::IronDev.Services.ITicketService _ticketService;

    [ObservableProperty] private string _projectName    = string.Empty;
    [ObservableProperty] private string _projectPath    = string.Empty;
    [ObservableProperty] private string _model          = string.Empty;
    [ObservableProperty] private string _status         = string.Empty;
    [ObservableProperty] private string _description    = string.Empty;

    // Use UiModels for lists for now as they are shared with the mock/shell views
    [ObservableProperty] private ObservableCollection<TicketItem>   _recentTickets   = [];
    [ObservableProperty] private ObservableCollection<DecisionItem> _recentDecisions = [];

    public ProjectOverviewViewModel(global::IronDev.Services.ITicketService ticketService)
    {
        _ticketService   = ticketService;
    }

    internal void Load(global::IronDev.Data.Models.Project project)
    {
        ProjectName = project.Name;
        ProjectPath = project.LocalPath ?? string.Empty;
        Description = project.Description ?? string.Empty;
        
        // Mocked UI state
        Model       = "gpt-4o";
        Status      = "Ready";

        // TODO: Load real tickets from _ticketService in future sprint
        RecentTickets.Clear();
        RecentDecisions.Clear();
    }
}
