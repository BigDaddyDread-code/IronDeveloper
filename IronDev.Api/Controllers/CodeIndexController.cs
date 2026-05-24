using IronDev.Data.Models;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:int}/code-index")]
public sealed class CodeIndexController : ControllerBase
{
    private readonly ICodeIndexService _index;

    public CodeIndexController(ICodeIndexService index)
    {
        _index = index;
    }

    [HttpPost]
    public Task<CodeIndexResult> Index(int projectId, IndexRequest request, CancellationToken ct) =>
        _index.IndexDirectoryAsync(projectId, request.DirectoryPath, ct);

    [HttpGet("file-count")]
    public Task<int> FileCount(int projectId, CancellationToken ct) =>
        _index.GetIndexedFileCountAsync(projectId, ct);

    [HttpGet("files/search")]
    public Task<IReadOnlyList<ProjectFile>> SearchFiles(int projectId, [FromQuery] string q, [FromQuery] int take = 5, CancellationToken ct = default) =>
        _index.SearchFilesAsync(projectId, q, take, ct);

    [HttpGet("files/recent")]
    public Task<IReadOnlyList<ProjectFile>> RecentFiles(int projectId, [FromQuery] int take = 20, CancellationToken ct = default) =>
        _index.GetRecentFilesAsync(projectId, take, ct);

    [HttpGet("/api/projects/{projectId:int}/memory/search/snippets")]
    public Task<IReadOnlyList<CodeIndexEntry>> Snippets(int projectId, [FromQuery] string q, [FromQuery] int take = 10, CancellationToken ct = default) =>
        _index.GetRelevantSnippetsAsync(projectId, q, take, ct);

    public sealed record IndexRequest(string DirectoryPath);
}
