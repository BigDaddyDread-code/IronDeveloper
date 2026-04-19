using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using IronDev.Agent.Models;

namespace IronDev.Agent.ViewModels.Workspaces;

public sealed partial class TicketsWorkspaceViewModel : ObservableObject
{
    private readonly global::IronDev.Services.ITicketService _ticketService;

    [ObservableProperty] private ObservableCollection<TicketItem> _tickets = [];
    [ObservableProperty] private TicketItem? _selectedTicket;

    public TicketsWorkspaceViewModel(global::IronDev.Services.ITicketService ticketService)
    {
        _ticketService = ticketService;
    }

    internal async Task LoadAsync(global::IronDev.Data.Models.Project project)
    {
        Tickets.Clear();
        var tickets = await _ticketService.GetRecentTicketsAsync(project.Id, take: 50);
        foreach (var t in tickets)
        {
            Tickets.Add(new TicketItem 
            { 
                Id = t.Id, 
                Title = t.Title, 
                Status = t.Status,
                Priority = t.Priority,
                Summary = t.Summary
            });
        }
    }
}
