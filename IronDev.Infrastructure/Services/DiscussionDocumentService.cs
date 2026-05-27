using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Core.RunReports;
using IronDev.Core.Runs;
using IronDev.Core.Tools;
using IronDev.Data.Models;
using IronDev.Infrastructure.Tools.CodeStandards;
using IronDev.Services;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;
public sealed class DiscussionDocumentService : IDiscussionDocumentService
{
    private readonly IProjectDocumentService _documents;

    public DiscussionDocumentService(IProjectDocumentService documents)
    {
        _documents = documents;
    }

    public async Task<SaveDiscussionResponse> SaveDiscussionAsync(
        int projectId,
        SaveDiscussionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Discussion title is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("Discussion content is required.", nameof(request));

        var document = await _documents.CreateDocumentAsync(new CreateProjectDocumentRequest
        {
            ProjectId = projectId,
            Title = request.Title.Trim(),
            DocumentType = "Discussion",
            ContentMarkdown = request.Content,
            ChangeSummary = "Captured discussion.",
            CreatedBy = "IronDev"
        }, cancellationToken).ConfigureAwait(false);

        if (!document.CurrentVersionId.HasValue)
            throw new InvalidOperationException("Discussion document was created without a current version.");

        return new SaveDiscussionResponse
        {
            DocumentId = document.Id,
            DocumentVersionId = document.CurrentVersionId.Value
        };
    }
}

