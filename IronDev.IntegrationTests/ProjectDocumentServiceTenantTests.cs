using System;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Interfaces;
using IronDev.Data;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ProjectDocumentServiceTenantTests : IntegrationTestBase
{
    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await EnsureProjectDocumentTablesAsync();
    }

    [TestMethod]
    public async Task VersionReads_ShouldEnforceTenantOwnership()
    {
        // Arrange
        var tenantOneProjectId = await SeedProjectAsync(tenantId: 1, name: "Tenant One Project");
        await SeedProjectAsync(tenantId: 2, name: "Tenant Two Project");

        TenantContext.TenantId = 1;
        var tenantOneService = CreateService();
        var document = await tenantOneService.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = tenantOneProjectId,
            Title = "Semantic memory architecture",
            DocumentType = "Architecture",
            ContentMarkdown = "# Semantic memory\nSQL is canonical.",
            CreatedBy = "test"
        });

        var currentVersion = await tenantOneService.GetCurrentVersionAsync(document.Id)
            ?? throw new InvalidOperationException("Expected current version.");

        await tenantOneService.LinkVersionAsync(new LinkProjectDocumentVersionRequest
        {
            DocumentVersionId = currentVersion.Id,
            LinkedEntityType = "Ticket",
            LinkedEntityId = 42,
            LinkType = "References",
            CreatedBy = "test"
        });

        // Act
        TenantContext.TenantId = 2;
        var tenantTwoService = CreateService();
        var crossTenantVersion = await tenantTwoService.GetVersionAsync(currentVersion.Id);
        var crossTenantHistory = await tenantTwoService.GetVersionHistoryAsync(document.Id);
        var crossTenantLinks = await tenantTwoService.GetLinksForVersionAsync(currentVersion.Id);

        // Assert
        Assert.IsNull(crossTenantVersion);
        Assert.AreEqual(0, crossTenantHistory.Count);
        Assert.AreEqual(0, crossTenantLinks.Count);
        var unauthorized = false;
        try
        {
            await tenantTwoService.LinkVersionAsync(new LinkProjectDocumentVersionRequest
            {
                DocumentVersionId = currentVersion.Id,
                LinkedEntityType = "Ticket",
                LinkedEntityId = 43,
                LinkType = "References",
                CreatedBy = "test"
            });
        }
        catch (UnauthorizedAccessException)
        {
            unauthorized = true;
        }

        Assert.IsTrue(unauthorized);
    }

    private ProjectDocumentService CreateService()
        => new(
            ServiceProvider.GetRequiredService<IDbConnectionFactory>(),
            TenantContext);

    private async Task EnsureProjectDocumentTablesAsync()
    {
        using var connection = ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnection();

        await connection.ExecuteAsync("""
            IF OBJECT_ID('dbo.ProjectDocumentLinks', 'U') IS NOT NULL DELETE FROM dbo.ProjectDocumentLinks;
            IF OBJECT_ID('dbo.ProjectDocumentVersions', 'U') IS NOT NULL DELETE FROM dbo.ProjectDocumentVersions;
            IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NOT NULL DELETE FROM dbo.ProjectDocuments;

            IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProjectDocuments
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    Title NVARCHAR(500) NOT NULL,
                    Slug NVARCHAR(500) NOT NULL,
                    DocumentType NVARCHAR(100) NOT NULL,
                    CurrentVersionId BIGINT NULL,
                    Status NVARCHAR(50) NOT NULL,
                    CreatedAtUtc DATETIME2 NOT NULL,
                    UpdatedAtUtc DATETIME2 NULL,
                    CreatedBy NVARCHAR(200) NULL,
                    UpdatedBy NVARCHAR(200) NULL
                );
            END

            IF OBJECT_ID('dbo.ProjectDocumentVersions', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProjectDocumentVersions
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    DocumentId BIGINT NOT NULL,
                    VersionMajor INT NOT NULL,
                    VersionMinor INT NOT NULL,
                    ContentMarkdown NVARCHAR(MAX) NOT NULL,
                    ChangeSummary NVARCHAR(MAX) NULL,
                    ParentVersionId BIGINT NULL,
                    Status NVARCHAR(50) NOT NULL,
                    ContentHash NVARCHAR(128) NULL,
                    CreatedAtUtc DATETIME2 NOT NULL,
                    CreatedBy NVARCHAR(200) NULL
                );
            END

            IF OBJECT_ID('dbo.ProjectDocumentLinks', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProjectDocumentLinks
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    DocumentVersionId BIGINT NOT NULL,
                    LinkedEntityType NVARCHAR(100) NOT NULL,
                    LinkedEntityId BIGINT NOT NULL,
                    LinkType NVARCHAR(100) NOT NULL,
                    CreatedAtUtc DATETIME2 NOT NULL,
                    CreatedBy NVARCHAR(200) NULL
                );
            END
            """);
    }
}
