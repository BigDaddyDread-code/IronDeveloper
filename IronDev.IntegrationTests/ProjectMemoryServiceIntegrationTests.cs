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

    [TestMethod]
    public async Task SaveContextDocumentAsync_And_GetRelevantContextDocumentsAsync_ShouldReturnTypedDocument()
    {
        using var scope = ServiceProvider.CreateScope();
        var memoryService = scope.ServiceProvider.GetRequiredService<IProjectMemoryService>();
        var projectId = await SeedProjectAsync(name: "BookSeller");

        await memoryService.SaveContextDocumentAsync(new ProjectContextDocument
        {
            ProjectId = projectId,
            DocumentType = "Recommendation",
            AuthorityLevel = "Pending",
            Status = "Pending",
            Title = "SQLite plus Dapper persistence",
            Content = "SQLite plus Dapper is recommended for BookSeller persistence.",
            Summary = "SQLite plus Dapper is recommended, but not binding.",
            Tags = "sqlite,dapper,persistence",
            AppliesToCapability = "Book persistence",
            AppliesToArea = "Persistence"
        });

        var documents = await memoryService.GetRelevantContextDocumentsAsync(projectId, "BookSeller Dapper persistence", 10);

        Assert.HasCount(1, documents);
        Assert.AreEqual("Recommendation", documents[0].DocumentType);
        Assert.AreEqual("Pending", documents[0].AuthorityLevel);
        Assert.AreEqual("Book persistence", documents[0].AppliesToCapability);
    }

    [TestMethod]
    public async Task SaveObservableStateAsync_And_GetObservableStateAsync_ShouldReturnLatestState()
    {
        using var scope = ServiceProvider.CreateScope();
        var memoryService = scope.ServiceProvider.GetRequiredService<IProjectMemoryService>();
        var projectId = await SeedProjectAsync(name: "BookSeller");

        await memoryService.SaveObservableStateAsync(new ProjectObservableState
        {
            ProjectId = projectId,
            ActiveCapability = "Book persistence",
            CurrentFocus = "Choose persistence approach",
            BuildReadiness = "BlockedByOpenQuestion",
            LastRecommendation = "SQLite plus Dapper"
        });

        var state = await memoryService.GetObservableStateAsync(projectId);

        Assert.IsNotNull(state);
        Assert.AreEqual("Book persistence", state.ActiveCapability);
        Assert.AreEqual("SQLite plus Dapper", state.LastRecommendation);
    }
}
