using Dapper;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class EvidenceLinkSafetyTests : IntegrationTestBase
{
    [TestMethod]
    public async Task ArtifactSourceReferences_RejectAndHideCrossProjectTargets()
    {
        var projectOne = await SeedProjectAsync(name: "Evidence project one");
        var projectTwo = await SeedProjectAsync(name: "Evidence project two");
        using var scope = ServiceProvider.CreateScope();
        var tickets = scope.ServiceProvider.GetRequiredService<ITicketService>();
        var references = scope.ServiceProvider.GetRequiredService<IArtifactSourceReferenceService>();
        var artifactId = await tickets.SaveTicketAsync(Ticket(projectOne, "Artifact"));
        var validSourceId = await tickets.SaveTicketAsync(Ticket(projectOne, "Valid source"));
        var foreignSourceId = await tickets.SaveTicketAsync(Ticket(projectTwo, "Foreign source"));

        await references.AddAsync(Reference(projectOne, artifactId, validSourceId));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            references.AddAsync(Reference(projectOne, artifactId, foreignSourceId)));

        using (var connection = ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnection())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.ArtifactSourceReferences
                    (TenantId, ProjectId, ArtifactType, ArtifactId, SourceType, SourceId, ReferenceType)
                VALUES (1, @ProjectId, N'Ticket', @ArtifactId, N'Ticket', @ForeignSourceId, N'References');
                """, new { ProjectId = projectOne, ArtifactId = artifactId, ForeignSourceId = foreignSourceId });
        }

        var visible = await references.GetForArtifactAsync(1, projectOne, "Ticket", artifactId);
        Assert.HasCount(1, visible);
        Assert.AreEqual(validSourceId, visible[0].SourceId);
    }

    [TestMethod]
    public async Task ProjectDocumentLinks_RejectAndHideCrossProjectTargets()
    {
        var projectOne = await SeedProjectAsync(name: "Document evidence one");
        var projectTwo = await SeedProjectAsync(name: "Document evidence two");
        var documents = new ProjectDocumentService(
            ServiceProvider.GetRequiredService<IDbConnectionFactory>(),
            TenantContext);
        var source = await documents.CreateDocumentAsync(Document(projectOne, "Source document"));
        var foreign = await documents.CreateDocumentAsync(Document(projectTwo, "Foreign document"));
        var sourceVersion = (await documents.GetCurrentVersionAsync(source.Id))!;
        var foreignVersion = (await documents.GetCurrentVersionAsync(foreign.Id))!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => documents.LinkVersionAsync(new LinkProjectDocumentVersionRequest
        {
            DocumentVersionId = sourceVersion.Id,
            LinkedEntityType = "ProjectDocumentVersion",
            LinkedEntityId = foreignVersion.Id,
            LinkType = "References"
        }));

        using (var connection = ServiceProvider.GetRequiredService<IDbConnectionFactory>().CreateConnection())
        {
            await connection.ExecuteAsync("""
                INSERT INTO dbo.ProjectDocumentLinks
                    (DocumentVersionId, LinkedEntityType, LinkedEntityId, LinkType, CreatedAtUtc)
                VALUES (@SourceVersionId, N'ProjectDocumentVersion', @ForeignVersionId, N'References', SYSUTCDATETIME());
                """, new { SourceVersionId = sourceVersion.Id, ForeignVersionId = foreignVersion.Id });
        }

        Assert.IsEmpty(await documents.GetLinksForVersionAsync(sourceVersion.Id));
    }

    private static ProjectTicket Ticket(int projectId, string title) => new()
    {
        ProjectId = projectId,
        Title = title,
        Status = "Draft",
        Content = title
    };

    private static ArtifactSourceReference Reference(int projectId, long artifactId, long sourceId) => new()
    {
        TenantId = 1,
        ProjectId = projectId,
        ArtifactType = "Ticket",
        ArtifactId = artifactId,
        SourceType = "Ticket",
        SourceId = sourceId,
        ReferenceType = "References"
    };

    private static CreateProjectDocumentRequest Document(int projectId, string title) => new()
    {
        ProjectId = projectId,
        Title = title,
        ContentMarkdown = $"# {title}"
    };
}
