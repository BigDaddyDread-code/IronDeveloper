using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.IntegrationTests;

[TestClass]
public class ProjectContextExportServiceTests : IntegrationTestBase
{
    [TestMethod]
    public async Task ExportProjectContextPackAsync_ShouldReturnPopulatedMarkdown()
    {
        using var scope = ServiceProvider.CreateScope();
        var exportService = scope.ServiceProvider.GetRequiredService<IProjectContextExportService>();
        var memoryService = scope.ServiceProvider.GetRequiredService<IProjectMemoryService>();
        var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();

        // 1. Setup Data
        var projectId = await SeedProjectAsync(name: "Export Test Project", localPath: @"C:\repos\ExportTest");
        
        await SeedProjectProfileAsync(projectId, testFramework: "NUnit");
        await SeedProjectCommandAsync(projectId, "Build", "dotnet build ExportTest.sln");

        await memoryService.SaveSummaryAsync(new ProjectSummary
        {
            ProjectId = projectId,
            Summary = "This is a test blueprint for export verification.",
            UpdatedDate = DateTime.UtcNow
        });

        await memoryService.SaveDecisionAsync(new ProjectDecision
        {
            ProjectId = projectId,
            Title = "Use NUnit for Tests",
            Category = "Testing",
            Status = "Approved",
            Detail = "We decided to use NUnit because reasons.",
            Reason = "Better support for parallel tests."
        });

        await ticketService.SaveTicketAsync(new ProjectTicket
        {
            ProjectId = projectId,
            Title = "Implement Export Feature",
            Summary = "Need to allow exporting project context pack.",
            Status = "In Progress",
            Priority = "High",
            TicketType = "Feature"
        });

        // 2. Execute
        var markdown = await exportService.ExportProjectContextPackAsync(projectId);

        // 3. Verify
        Assert.IsNotNull(markdown);
        StringAssert.Contains(markdown, "# Project Context Pack: Export Test Project");
        StringAssert.Contains(markdown, "## Project Details");
        StringAssert.Contains(markdown, "Export Test Project");
        StringAssert.Contains(markdown, "## Project Profile");
        StringAssert.Contains(markdown, "NUnit");
        StringAssert.Contains(markdown, "Latest Product Summary");
        StringAssert.Contains(markdown, "test blueprint for export verification");
        StringAssert.Contains(markdown, "## Architecture Decisions");
        StringAssert.Contains(markdown, "Use NUnit for Tests");
        StringAssert.Contains(markdown, "## Recent Tickets");
        StringAssert.Contains(markdown, "Implement Export Feature");
    }

    [TestMethod]
    public async Task ExportProjectContextPackAsync_ShouldScrubSecrets()
    {
        using var scope = ServiceProvider.CreateScope();
        var exportService = scope.ServiceProvider.GetRequiredService<IProjectContextExportService>();
        var memoryService = scope.ServiceProvider.GetRequiredService<IProjectMemoryService>();

        var projectId = await SeedProjectAsync();
        
        await memoryService.SaveSummaryAsync(new ProjectSummary
        {
            ProjectId = projectId,
            Summary = "Connection string: Server=myServer;Database=myDb;User Id=myUser;Password=SuperSecretPassword123;",
            UpdatedDate = DateTime.UtcNow
        });

        await memoryService.SaveProjectRuleAsync(new ProjectRule
        {
            ProjectId = projectId,
            Name = "API Usage",
            Type = "Security",
            Description = "Use the following api-key: abcdef-12345-ghijk-67890",
            EnforcementLevel = "Enforced"
        });

        var markdown = await exportService.ExportProjectContextPackAsync(projectId);

        Assert.IsFalse(markdown.Contains("SuperSecretPassword123"));
        Assert.IsFalse(markdown.Contains("abcdef-12345-ghijk-67890"));
        StringAssert.Contains(markdown, "[REDACTED]");
    }
}
