using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Builder;

public sealed class BuilderReadinessService : IBuilderReadinessService
{
    private readonly IProjectProfileService _profileService;
    private readonly ITicketService _ticketService;
    private readonly IProjectService _projectService;
    private readonly IProjectMemoryService _memoryService;

    public BuilderReadinessService(
        IProjectProfileService profileService,
        ITicketService ticketService,
        IProjectService projectService,
        IProjectMemoryService memoryService)
    {
        _profileService = profileService;
        _ticketService = ticketService;
        _projectService = projectService;
        _memoryService = memoryService;
    }

    public async Task<BuildReadinessResult> EvaluateReadinessAsync(int projectId, long ticketId, CancellationToken ct = default)
    {
        var result = new BuildReadinessResult();
        
        // 1. Project Profile exists
        var profile = await _profileService.GetProjectProfileAsync(projectId, ct);
        if (profile == null)
        {
            return new BuildReadinessResult 
            { 
                Status = BuildReadinessStatus.NeedsProjectProfileUpdate,
                Message = "Complete Project Profile before building this ticket.",
                BlockingIssues = { "Project Profile missing." }
            };
        }

        // 2. AllowBuilderApply = true
        if (!profile.AllowBuilderApply)
        {
            result.Status = BuildReadinessStatus.BlockedByConflict;
            result.Message = "Builder Apply is disabled for this project.";
            result.BlockingIssues.Add("AllowBuilderApply is false.");
        }

        // 3. Project Root exists
        var project = await _projectService.GetByIdAsync(projectId, ct);
        if (project == null || string.IsNullOrWhiteSpace(project.LocalPath) || !Directory.Exists(project.LocalPath))
        {
            result.Status = BuildReadinessStatus.Error;
            result.Message = "Project local path not found or invalid.";
            result.BlockingIssues.Add("LocalPath invalid.");
            return result;
        }

        // 4. Code index is available and fresh enough for Builder context.
        if (!project.LastIndexedUtc.HasValue)
        {
            result.Status = BuildReadinessStatus.NeedsReindex;
            result.Message = "Index this project before running Builder.";
            result.BlockingIssues.Add("Project has not been indexed.");
        }
        else if (!string.Equals(project.IndexingStatus, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            result.Status = BuildReadinessStatus.NeedsReindex;
            result.Message = $"Project index is not ready: {project.IndexingStatus ?? "Not indexed"}.";
            result.BlockingIssues.Add("Project index status is not Ready.");
        }
        else if ((project.IndexedFileCount ?? 0) <= 0)
        {
            result.Status = BuildReadinessStatus.NeedsReindex;
            result.Message = "Project index contains no files. Re-index before running Builder.";
            result.BlockingIssues.Add("Project index contains no files.");
        }

        // 5. Build/Test commands exist
        var buildCmd = await _profileService.GetDefaultCommandAsync(projectId, "Build", ct);
        if (buildCmd == null || string.IsNullOrWhiteSpace(buildCmd.CommandText))
        {
            result.Status = BuildReadinessStatus.NeedsProjectProfileUpdate;
            result.Message = "Build command missing in Project Profile.";
            result.BlockingIssues.Add("BuildCommand missing.");
        }

        var testCmd = await _profileService.GetDefaultCommandAsync(projectId, "Test", ct);
        if (testCmd == null || string.IsNullOrWhiteSpace(testCmd.CommandText))
        {
            result.Warnings.Add("Test command missing. Tests will be skipped.");
        }

        // 6. Architecture check (Simple for v0.2)
        var ticket = await _ticketService.GetTicketByIdAsync(ticketId, ct);
        if (ticket != null && (ticket.Problem?.Contains("persist", StringComparison.OrdinalIgnoreCase) == true || 
                               ticket.Problem?.Contains("database", StringComparison.OrdinalIgnoreCase) == true))
        {
            if (string.IsNullOrWhiteSpace(profile.DatabaseEngine) || string.IsNullOrWhiteSpace(profile.DataAccessStyle))
            {
                result.Status = BuildReadinessStatus.NeedsArchitectureDecision;
                result.Message = "Persistence architecture not defined for this ticket.";
                result.BlockingIssues.Add("DatabaseEngine or DataAccessStyle missing.");
            }
        }

        if (result.BlockingIssues.Count > 0 && result.Status == BuildReadinessStatus.ReadyToBuild)
        {
            result.Status = BuildReadinessStatus.Error;
        }

        return result;
    }

    public async Task<BuildReadinessResult> ValidateProposalArchitectureAsync(BuilderProposal proposal, CancellationToken ct = default)
    {
        var result = new BuildReadinessResult();
        
        // Find generated tests
        var testChanges = proposal.Changes.Where(c => c.FilePath.Contains(".Tests", StringComparison.OrdinalIgnoreCase) || 
                                                     c.FilePath.Contains("Test", StringComparison.OrdinalIgnoreCase)).ToList();
        
        if (testChanges.Count == 0) return result;

        // Detect framework in generated code
        string? detectedFramework = null;
        foreach (var change in testChanges)
        {
            var content = change.FullContentAfter ?? "";
            if (content.Contains("using Xunit;") || content.Contains("[Fact]"))
            {
                detectedFramework = "xUnit";
                break;
            }
            if (content.Contains("using Microsoft.VisualStudio.TestTools.UnitTesting;") || content.Contains("[TestClass]"))
            {
                detectedFramework = "MSTest";
                break;
            }
            if (content.Contains("using NUnit.Framework;") || content.Contains("[Test]"))
            {
                detectedFramework = "NUnit";
                break;
            }
        }

        if (detectedFramework == null) return result;

        // Check project for matching framework
        var projectRoot = proposal.ProjectRoot;
        if (!Directory.Exists(projectRoot)) return result;

        var csprojFiles = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.AllDirectories);
        var testCsproj = csprojFiles.FirstOrDefault(f => f.Contains(".Tests", StringComparison.OrdinalIgnoreCase));
        
        if (testCsproj == null)
        {
            result.Status = BuildReadinessStatus.NeedsProjectProfileUpdate;
            result.Message = "No test project found to validate test framework.";
            return result;
        }

        var csprojContent = await File.ReadAllTextAsync(testCsproj, ct);
        bool hasReference = false;

        if (detectedFramework == "xUnit" && csprojContent.Contains("xunit", StringComparison.OrdinalIgnoreCase)) hasReference = true;
        else if (detectedFramework == "MSTest" && (csprojContent.Contains("MSTest.TestAdapter", StringComparison.OrdinalIgnoreCase) || csprojContent.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase))) hasReference = true;
        else if (detectedFramework == "NUnit" && csprojContent.Contains("NUnit", StringComparison.OrdinalIgnoreCase)) hasReference = true;

        if (!hasReference)
        {
            result.Status = BuildReadinessStatus.NeedsProjectProfileUpdate;
            result.Message = $"Generated tests use {detectedFramework}, but {Path.GetFileName(testCsproj)} does not reference {detectedFramework}. Update Project Profile, add {detectedFramework} packages, or regenerate using the existing test framework.";
            result.BlockingIssues.Add($"Test framework mismatch: {detectedFramework} missing in {Path.GetFileName(testCsproj)}");
        }

        return result;
    }
}
