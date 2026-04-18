using System;
using System.Collections.Generic;
using IronDev.Agent.Models;
using IronDev.Agent.Services.Interfaces;

namespace IronDev.Agent.Services.Mock;

/// <summary>
/// Seeded in-memory ticket service. No DB. Replace with real TicketService in a later sprint.
/// </summary>
public sealed class MockTicketShellService : ITicketShellService
{
    private readonly List<TicketSummary> _tickets =
    [
        new() { Id = 1, Title = "Implement shell navigation state machine",  Type = "Feature",  Priority = "High",   Status = "In Progress", CreatedDate = DateTime.UtcNow.AddDays(-2) },
        new() { Id = 2, Title = "Port IronDeveloperControls theme to app",   Type = "Task",     Priority = "High",   Status = "Done",        CreatedDate = DateTime.UtcNow.AddDays(-1) },
        new() { Id = 3, Title = "Add mock services layer",                   Type = "Task",     Priority = "Medium", Status = "Done",        CreatedDate = DateTime.UtcNow.AddDays(-1) },
        new() { Id = 4, Title = "Wire real LLM service to ChatWorkspace",    Type = "Feature",  Priority = "High",   Status = "Draft",       CreatedDate = DateTime.UtcNow              },
        new() { Id = 5, Title = "Add project indexing pipeline",             Type = "Feature",  Priority = "Medium", Status = "Draft",       CreatedDate = DateTime.UtcNow              },
        new() { Id = 6, Title = "Login screen — integrate real auth",        Type = "Security", Priority = "High",   Status = "Draft",       CreatedDate = DateTime.UtcNow              }
    ];

    public IReadOnlyList<TicketSummary> GetTickets() => _tickets.AsReadOnly();
}
