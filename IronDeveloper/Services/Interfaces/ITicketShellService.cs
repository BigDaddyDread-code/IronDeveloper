using System.Collections.Generic;
using IronDev.Agent.Models;

namespace IronDev.Agent.Services.Interfaces;

public interface ITicketShellService
{
    IReadOnlyList<TicketSummary> GetTickets();
}
