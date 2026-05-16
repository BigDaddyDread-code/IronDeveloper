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

        // 6. Ticket clarity and architecture gates
        var ticket = await _ticketService.GetTicketByIdAsync(ticketId, ct);
        if (ticket == null)
        {
            result.Status = BuildReadinessStatus.NeedsClarification;
            result.Message = "Ticket not found. Select or save a ticket before running Builder.";
            result.BlockingIssues.Add("Ticket missing.");
            return result;
        }

        if (IsTicketScopeUnclear(ticket))
        {
            result.Status = BuildReadinessStatus.NeedsClarification;
            result.Message = "Ticket scope is unclear. Add a summary, problem, or acceptance criteria before running Builder.";
            result.BlockingIssues.Add("Ticket scope is unclear.");
        }

        var ticketText = GetTicketText(ticket);
        if (ticketText.Contains("persist", StringComparison.OrdinalIgnoreCase) ||
            ticketText.Contains("database", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(profile.DatabaseEngine) || string.IsNullOrWhiteSpace(profile.DataAccessStyle))
            {
                result.Status = BuildReadinessStatus.NeedsArchitectureDecision;
                result.Message = "Persistence architecture not defined for this ticket.";
                result.BlockingIssues.Add("DatabaseEngine or DataAccessStyle missing.");
            }
        }

        var openQuestions = await _memoryService.GetContextDocumentsAsync(projectId, documentType: "OpenQuestion", status: null, take: 50, cancellationToken: ct);
        var blockingQuestions = openQuestions
            .Where(q => IsPendingOpenQuestion(q) && IsQuestionRelevantToTicket(q, ticketText))
            .ToList();

        if (blockingQuestions.Count > 0)
        {
            result.Status = BuildReadinessStatus.NeedsArchitectureDecision;
            result.Message = "Resolve open project questions before running Builder for this ticket.";
            foreach (var question in blockingQuestions.Take(5))
            {
                result.BlockingIssues.Add($"Open question: {question.Title}");
            }
        }

        if (result.BlockingIssues.Count > 0 && result.Status == BuildReadinessStatus.ReadyToBuild)
        {
            result.Status = BuildReadinessStatus.Error;
        }

        return result;
    }

    private static bool IsTicketScopeUnclear(IronDev.Data.Models.ProjectTicket ticket)
    {
        return string.IsNullOrWhiteSpace(ticket.Summary) &&
               string.IsNullOrWhiteSpace(ticket.Problem) &&
               string.IsNullOrWhiteSpace(ticket.AcceptanceCriteria) &&
               string.IsNullOrWhiteSpace(ticket.TechnicalNotes);
    }

    private static string GetTicketText(IronDev.Data.Models.ProjectTicket ticket)
    {
        return string.Join(" ", new[]
        {
            ticket.Title,
            ticket.TicketType,
            ticket.Summary,
            ticket.Problem,
            ticket.AcceptanceCriteria,
            ticket.TechnicalNotes,
            ticket.LinkedFilePaths,
            ticket.LinkedSymbols
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static bool IsPendingOpenQuestion(IronDev.Data.Models.ProjectContextDocument document)
    {
        return document.DocumentType.Equals("OpenQuestion", StringComparison.OrdinalIgnoreCase) &&
               !document.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase) &&
               !document.Status.Equals("Superseded", StringComparison.OrdinalIgnoreCase) &&
               !document.Status.Equals("Archived", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsQuestionRelevantToTicket(IronDev.Data.Models.ProjectContextDocument question, string ticketText)
    {
        var lowerTicket = ticketText.ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(question.AppliesToArea) &&
            lowerTicket.Contains(question.AppliesToArea.ToLowerInvariant()))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(question.AppliesToCapability) &&
            lowerTicket.Contains(question.AppliesToCapability.ToLowerInvariant()))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(question.Tags))
        {
            var tags = question.Tags.Split([',', ';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (tags.Any(tag => tag.Length > 2 && lowerTicket.Contains(tag.ToLowerInvariant())))
            {
                return true;
            }
        }

        return false;
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
