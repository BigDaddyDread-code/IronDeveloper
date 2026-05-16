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
    public async Task ContextDocuments_ShouldPersistFilterUpdateAndArchive()
    {
        using var scope = ServiceProvider.CreateScope();
        var memoryService = scope.ServiceProvider.GetRequiredService<IProjectMemoryService>();
        var projectId = await SeedProjectAsync();

        var decisionId = await memoryService.SaveContextDocumentAsync(new ProjectContextDocument
        {
            ProjectId = projectId,
            DocumentType = "ArchitectureDecision",
            AuthorityLevel = "Binding",
            Status = "Accepted",
            Title = "Use Dapper",
            Summary = "Dapper is the accepted data access style.",
            Content = "IronDev should use Dapper for SQL persistence in this project.",
            Tags = "data,persistence",
            AppliesToArea = "Persistence",
            Source = "Manual"
        });

        await memoryService.SaveContextDocumentAsync(new ProjectContextDocument
        {
            ProjectId = projectId,
            DocumentType = "OpenQuestion",
            AuthorityLevel = "Pending",
            Status = "Pending",
            Title = "Choose reporting database",
            Content = "Should reporting use the operational database or a read model?"
        });

        var bindingDocs = await memoryService.GetContextDocumentsAsync(
            projectId,
            documentType: "ArchitectureDecision",
            authorityLevel: "Binding",
            status: "Accepted");

        Assert.HasCount(1, bindingDocs);
        Assert.AreEqual(decisionId, bindingDocs[0].Id);
        Assert.AreEqual("Use Dapper", bindingDocs[0].Title);
        Assert.AreEqual("Persistence", bindingDocs[0].AppliesToArea);

        var loaded = await memoryService.GetContextDocumentByIdAsync(decisionId);
        Assert.IsNotNull(loaded);
        loaded.Content = "Updated binding persistence decision.";
        loaded.Status = "Active";

        await memoryService.SaveContextDocumentAsync(loaded);

        var updated = await memoryService.GetContextDocumentByIdAsync(decisionId);
        Assert.IsNotNull(updated);
        Assert.AreEqual("Updated binding persistence decision.", updated.Content);
        Assert.AreEqual("Active", updated.Status);

        var archived = await memoryService.ArchiveContextDocumentAsync(decisionId);
        Assert.IsTrue(archived);

        var activeDocs = await memoryService.GetContextDocumentsAsync(projectId, status: "Active");
        Assert.IsFalse(activeDocs.Any(d => d.Id == decisionId));

        var archivedDocs = await memoryService.GetContextDocumentsAsync(projectId, status: "Archived");
        Assert.IsTrue(archivedDocs.Any(d => d.Id == decisionId));
    }
}
