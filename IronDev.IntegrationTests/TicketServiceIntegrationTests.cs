using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.IntegrationTests;

[TestClass]
public class TicketServiceIntegrationTests : IntegrationTestBase
{
    [TestMethod]
    public async Task SaveTicketAsync_And_GetRecentTicketsAsync_ShouldReturnSavedTicket()
    {
        using var scope = ServiceProvider.CreateScope();
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();

        var projectId = await SeedProjectAsync();
        var sessionId = Guid.NewGuid();

        var ticketId = await ticketService.SaveTicketAsync(new ProjectTicket
        {
            ProjectId = projectId,
            SessionId = sessionId,
            Title = "Add persistence test for UI save behavior",
            TicketType = "Task",
            Priority = "Medium",
            Status = "Draft",
            Summary = "Create a UI-focused test for save flow.",
            Background = "Need confidence in save behavior.",
            Problem = "No integration coverage exists.",
            AcceptanceCriteria = "- Save persists data correctly",
            Content = "Fallback content",
            TechnicalNotes = ""
        });

        var tickets = await ticketService.GetRecentTicketsAsync(projectId, 10);
        var loaded = await ticketService.GetTicketByIdAsync(ticketId);

        Assert.IsNotNull(loaded);
        Assert.IsTrue(ticketId > 0);
        Assert.HasCount(1, tickets);
        Assert.AreEqual("Add persistence test for UI save behavior", loaded.Title);
        Assert.AreEqual("Task", loaded.TicketType);
        Assert.AreEqual("Draft", loaded.Status);
    }
}
