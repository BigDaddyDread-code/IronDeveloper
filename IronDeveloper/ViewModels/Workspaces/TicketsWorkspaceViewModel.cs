using System.Collections.ObjectModel;
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
        
        // TODO: Load real tickets from _ticketService in future sprint
        // foreach (var t in _ticketService.GetTicketsAsync(...)) ...
    }
}
