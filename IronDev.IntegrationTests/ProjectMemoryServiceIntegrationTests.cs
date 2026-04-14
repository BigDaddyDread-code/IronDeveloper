using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.IntegrationTests;

[TestClass]
public class ProjectMemoryServiceIntegrationTests : IntegrationTestBase
{
    [TestMethod]
    public async Task SaveDecisionAsync_And_GetRecentDecisionsAsync_ShouldReturnSavedDecision()
    {
        using var scope = ServiceProvider.CreateScope();
        var memoryService = scope.ServiceProvider.GetRequiredService<IProjectMemoryService>();

        var projectId = await SeedProjectAsync();

        await memoryService.SaveDecisionAsync(new ProjectDecision
        {
            ProjectId = projectId,
            Title = "Use Dapper",
            Detail = "Persist operational memory in SQL Server using Dapper.",
            Reason = "Simple and testable."
        });

        var decisions = await memoryService.GetRecentDecisionsAsync(projectId, 10);

        Assert.HasCount(1, decisions);
        Assert.AreEqual("Use Dapper", decisions[0].Title);
        Assert.AreEqual("Simple and testable.", decisions[0].Reason);
    }
    [TestMethod]
    public async Task SaveDecisionAsync_ShouldNotInsertDuplicateTitles()
    {
        using var scope = ServiceProvider.CreateScope();
        var memoryService = scope.ServiceProvider.GetRequiredService<IProjectMemoryService>();
        var projectId = await SeedProjectAsync();

        await memoryService.SaveDecisionAsync(new ProjectDecision { ProjectId = projectId, Title = "Architecture", Detail = "V1" });
        await memoryService.SaveDecisionAsync(new ProjectDecision { ProjectId = projectId, Title = "Architecture", Detail = "V2" });

        var decisions = await memoryService.GetRecentDecisionsAsync(projectId, 10);
        Assert.HasCount(1, decisions);
        Assert.AreEqual("V2", decisions[0].Detail);
    }
}
