using System.Text;
using IronDev.Core.Interfaces;
using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ProjectDocumentUploadServiceTests
{
    [TestMethod]
    public async Task UploadAsync_ValidMarkdownCreatesBackendOwnedDraftMetadata()
    {
        var documents = new Mock<IProjectDocumentService>();
        CreateProjectDocumentRequest? captured = null;
        documents
            .Setup(service => service.GetDocumentsAsync(It.IsAny<GetProjectDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        documents
            .Setup(service => service.CreateDocumentAsync(It.IsAny<CreateProjectDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateProjectDocumentRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new ProjectDocument
            {
                Id = 42,
                ProjectId = 7,
                Title = "Architecture Notes",
                Origin = "Uploaded",
                ProcessingStatus = "Draft",
                CurrentVersionId = 84
            });
        documents
            .Setup(service => service.GetCurrentVersionAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectDocumentVersion { Id = 84, DocumentId = 42, VersionMajor = 0, VersionMinor = 1 });
        var service = new ProjectDocumentUploadService(documents.Object);
        var bytes = Encoding.UTF8.GetBytes("# Architecture\n\nBackend truth.");

        var result = await service.UploadAsync(Request(bytes, "architecture.md", "Architecture Notes", "Architecture"));

        Assert.IsNotNull(captured);
        Assert.AreEqual("Uploaded", captured!.Origin);
        Assert.AreEqual("Draft", captured.ProcessingStatus);
        Assert.AreEqual("architecture.md", captured.OriginalFileName);
        Assert.AreEqual("text/markdown", captured.MediaType);
        Assert.AreEqual(bytes.Length, captured.ByteSize);
        Assert.AreEqual("# Architecture\n\nBackend truth.", captured.ContentMarkdown);
        Assert.AreEqual("Project", captured.Visibility);
        Assert.AreEqual("Draft", result.ProcessingStatus);
        Assert.AreEqual(84L, result.Version.Id);
        StringAssert.Contains(result.Boundary, "not attached to Chat");
    }

    [TestMethod]
    public async Task UploadAsync_UnsupportedTypeIsRejectedBeforePersistence()
    {
        var documents = EmptyDocumentService();
        var service = new ProjectDocumentUploadService(documents.Object);

        var error = await Assert.ThrowsExactlyAsync<ProjectDocumentUploadException>(() =>
            service.UploadAsync(Request([1, 2, 3], "design.pdf", "Design", "Architecture")));

        Assert.AreEqual(ProjectDocumentUploadFailureKind.UnsupportedType, error.Kind);
        documents.Verify(service => service.CreateDocumentAsync(It.IsAny<CreateProjectDocumentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UploadAsync_OverLimitIsRejectedBeforeReadingOrPersistence()
    {
        var documents = EmptyDocumentService();
        var service = new ProjectDocumentUploadService(documents.Object);
        var request = Request([1], "large.txt", "Large", "DiscussionSummary");
        request.ByteSize = ProjectDocumentUploadService.MaximumFileBytes + 1;

        var error = await Assert.ThrowsExactlyAsync<ProjectDocumentUploadException>(() => service.UploadAsync(request));

        Assert.AreEqual(ProjectDocumentUploadFailureKind.TooLarge, error.Kind);
        documents.Verify(service => service.CreateDocumentAsync(It.IsAny<CreateProjectDocumentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UploadAsync_InvalidUtf8IsRejectedWithoutDocumentIdentity()
    {
        var documents = EmptyDocumentService();
        var service = new ProjectDocumentUploadService(documents.Object);

        var error = await Assert.ThrowsExactlyAsync<ProjectDocumentUploadException>(() =>
            service.UploadAsync(Request([0xC3, 0x28], "broken.txt", "Broken", "DiscussionSummary")));

        Assert.AreEqual(ProjectDocumentUploadFailureKind.InvalidEncoding, error.Kind);
        documents.Verify(service => service.CreateDocumentAsync(It.IsAny<CreateProjectDocumentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task UploadAsync_DuplicateTitleIsAConflictBeforePersistence()
    {
        var documents = EmptyDocumentService([new ProjectDocument { Id = 1, ProjectId = 7, Title = "Architecture Notes" }]);
        var service = new ProjectDocumentUploadService(documents.Object);
        var bytes = Encoding.UTF8.GetBytes("# Duplicate");

        var error = await Assert.ThrowsExactlyAsync<ProjectDocumentUploadException>(() =>
            service.UploadAsync(Request(bytes, "notes.md", "architecture notes", "Architecture")));

        Assert.AreEqual(ProjectDocumentUploadFailureKind.DuplicateTitle, error.Kind);
        documents.Verify(service => service.CreateDocumentAsync(It.IsAny<CreateProjectDocumentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IProjectDocumentService> EmptyDocumentService(IReadOnlyList<ProjectDocument>? existing = null)
    {
        var documents = new Mock<IProjectDocumentService>();
        documents
            .Setup(service => service.GetDocumentsAsync(It.IsAny<GetProjectDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing ?? []);
        return documents;
    }

    private static ProjectDocumentUploadRequest Request(byte[] bytes, string fileName, string title, string documentType) =>
        new()
        {
            ProjectId = 7,
            DisplayName = title,
            DocumentType = documentType,
            OriginalFileName = fileName,
            MediaType = "application/octet-stream",
            ByteSize = bytes.Length,
            Content = new MemoryStream(bytes),
            CreatedBy = "bob@irondev.local"
        };
}
