using Dapper;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data;
using IronDev.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IronDev.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:int}/services")]
public sealed class ProjectServicesController : ControllerBase
{
    private readonly IProjectService _projects;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ISemanticMemoryService _semanticMemory;
    private readonly IConfiguration _configuration;

    public ProjectServicesController(
        IProjectService projects,
        IDbConnectionFactory connectionFactory,
        ISemanticMemoryService semanticMemory,
        IConfiguration configuration)
    {
        _projects = projects;
        _connectionFactory = connectionFactory;
        _semanticMemory = semanticMemory;
        _configuration = configuration;
    }

    [HttpGet("status")]
    public async Task<ActionResult<ProjectServicesStatusDto>> GetStatus(int projectId, CancellationToken ct)
    {
        var project = await _projects.GetByIdAsync(projectId, ct);
        if (project is null)
            return NotFound();

        var warnings = new List<string>();
        var databaseStatus = await GetDatabaseStatusAsync(ct);
        var memoryStatus = await GetMemoryStatusAsync(projectId, warnings, ct);

        return Ok(new ProjectServicesStatusDto
        {
            ProjectId = projectId,
            ApiStatus = "healthy",
            DatabaseStatus = databaseStatus,
            MemoryStatus = memoryStatus,
            TestAgentAvailability = "not_exposed",
            ConfiguredModelProfiles = GetConfiguredModelProfiles(),
            WorkspacePaths = string.IsNullOrWhiteSpace(project.LocalPath) ? [] : [project.LocalPath],
            Warnings = warnings
        });
    }

    private async Task<string> GetDatabaseStatusAsync(CancellationToken ct)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            var value = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT 1",
                cancellationToken: ct));
            return value == 1 ? "healthy" : "unknown";
        }
        catch (Exception ex)
        {
            return $"unavailable: {ex.GetType().Name}";
        }
    }

    private async Task<string> GetMemoryStatusAsync(int projectId, List<string> warnings, CancellationToken ct)
    {
        try
        {
            var health = await _semanticMemory.GetHealthAsync(projectId, ct);
            if (!string.Equals(health.ProviderStatus, "healthy", StringComparison.OrdinalIgnoreCase))
                warnings.Add($"Memory provider status is {health.ProviderStatus}.");

            return health.ProviderStatus;
        }
        catch (Exception ex)
        {
            warnings.Add($"Memory status check failed: {ex.Message}");
            return $"unavailable: {ex.GetType().Name}";
        }
    }

    private IReadOnlyList<string> GetConfiguredModelProfiles()
    {
        var provider = _configuration["Ai:Provider"];
        var model = _configuration["Ai:Model"];
        var label = string.Join(
            ":",
            new[] { provider, model }.Where(part => !string.IsNullOrWhiteSpace(part)));

        return string.IsNullOrWhiteSpace(label) ? [] : [label];
    }
}
