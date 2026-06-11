using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Builder;

/// <summary>
/// Workbench-level service for generating and validating code modification proposals.
/// Orchestrates the context assembly and AI generation steps.
/// </summary>
public sealed class BuilderProposalService : IBuilderProposalService
{
    private readonly IBuilderContextService     _contextService;
    private readonly ICodeChangeProposalService _proposalService;
    private readonly ILlmTraceService           _llmTraceService;
    private readonly ITicketService             _ticketService;
    private readonly IDotNetBuildService        _buildService;
    private readonly IDotNetTestService         _testService;
    private readonly IProjectProfileService     _projectProfileService;
    private readonly IBuildErrorClassifierService _errorClassifierService;
    private readonly IBuilderReadinessService _readinessService;
    private readonly IProjectService _projectService;
    private readonly IArtifactSourceReferenceService _referenceService;

    public BuilderProposalService(
        IBuilderContextService     contextService,
        ICodeChangeProposalService proposalService,
        ILlmTraceService           llmTraceService,
        ITicketService             ticketService,
        IDotNetBuildService        buildService,
        IDotNetTestService         testService,
        IProjectProfileService     projectProfileService,
        IBuildErrorClassifierService errorClassifierService,
        IBuilderReadinessService readinessService,
        IProjectService projectService,
        IArtifactSourceReferenceService referenceService)
    {
        _contextService  = contextService;
        _proposalService = proposalService;
        _llmTraceService = llmTraceService;
        _ticketService   = ticketService;
        _buildService    = buildService;
        _testService     = testService;
        _projectProfileService = projectProfileService;
        _errorClassifierService = errorClassifierService;
        _readinessService = readinessService;
        _projectService = projectService;
        _referenceService = referenceService;
    }

    public async Task<BuilderProposal> GenerateProposalAsync(long ticketId, CancellationToken ct = default)
    {
        // Resolve ticket to find its ProjectId
        var ticket = await _ticketService.GetTicketByIdAsync(ticketId, ct)
            ?? throw new InvalidOperationException($"Ticket {ticketId} not found.");

        int projectId = ticket.ProjectId; 

        // ── 1. Evaluate Build Readiness ───────────────────────────────────────
        var readiness = await _readinessService.EvaluateReadinessAsync(projectId, ticketId, ct);
        
        _llmTraceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Ticket.BuildReadinessEvaluation",
            WorkspaceName = "Builder",
            ProjectId = projectId,
            TicketId = ticketId,
            WasSuccessful = readiness.IsReady,
            ParsedResponseSummary = $"Readiness Status: {readiness.Status}",
            ErrorMessage = readiness.IsReady ? string.Empty : string.Join("; ", readiness.BlockingIssues),
            CreatedAt = DateTime.UtcNow
        });

        if (!readiness.IsReady)
        {
            throw new InvalidOperationException($"Build blocked: {readiness.Message} " + string.Join(" ", readiness.BlockingIssues));
        }

        var context = await _contextService.AssembleContextAsync(projectId, ticketId, ct);
        var innerProposal = await _proposalService.GenerateProposalAsync(context, ct);

        var proposal = MapToBuilderProposal(innerProposal, context);
        ValidateProposal(proposal);

        // ── 2. Trace Validation ───────────────────────────────────────────────
        var trace = new LlmTraceEntry
        {
            FeatureName = "Builder.ProposalValidation",
            WorkspaceName = "Builder",
            ProjectId = projectId,
            TicketId = ticketId,
            ActiveProjectName = context.ProjectName,
            ActiveProjectPath = context.ProjectPath,
            IsProposalOnly = true,
            ProposedFileCount = proposal.Changes.Count,
            ProposedFilesList = string.Join(", ", proposal.Changes.Select(c => c.FilePath)),
            WasSuccessful = proposal.IsAllValid,
            ParsedResponseSummary = proposal.IsAllValid ? "Validation Passed" : "Validation Failed",
            CreatedAt = DateTime.UtcNow
        };
        
        if (proposal.HasValidationIssues)
        {
            trace.ErrorMessage = string.Join("; ", proposal.ValidationIssues);
        }
        else if (proposal.HasValidationWarnings)
        {
            trace.ErrorMessage = string.Join("; ", proposal.ValidationWarnings);
        }
        
        _llmTraceService.AddTrace(trace);

        // ── 3. Record Traceability Reference ──────────────────────────────────
        // Since BuilderProposal isn't persisted with a unique BIGINT ID yet,
        // we link the Proposal-to-Ticket relationship using TicketId as ArtifactId.
        await _referenceService.RecordReferenceAsync(new ArtifactSourceReference
        {
            TenantId = ticket.TenantId,
            ProjectId = projectId,
            ArtifactType = "BuilderProposal",
            ArtifactId = ticketId,
            SourceType = "Ticket",
            SourceId = ticketId,
            ReferenceType = "GeneratedFrom",
            Summary = $"Generated proposal with {proposal.Changes.Count} changes from ticket #{ticketId}."
        }, ct);

        return proposal;
    }

    public Task<BuilderProposal> GenerateProposalFromRequestAsync(int projectId, string request, CancellationToken ct = default)
    {
        // For request-based generation, we'd need a way to assemble context without a ticket.
        // Phase 1: Not implemented.
        throw new NotImplementedException("Request-based proposal generation is not implemented yet.");
    }

    public async Task ApplyProposalAsync(BuilderProposal proposal, CancellationToken ct = default)
    {
        var projectId = proposal.ProjectId;
        var ticketId = proposal.TicketId;

        // ── 1. Trace Requested ────────────────────────────────────────────────
        _llmTraceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.ApplyRequested",
            WorkspaceName = "Builder",
            ProjectId = projectId,
            TicketId = ticketId,
            ActiveProjectName = proposal.ProjectName,
            ActiveProjectPath = proposal.ProjectRoot,
            IsProposalOnly = false,
            ProposedFileCount = proposal.Changes.Count,
            CreatedAt = DateTime.UtcNow
        });

        // ── 2. Evaluate Build Readiness ───────────────────────────────────────
        var readiness = await _readinessService.EvaluateReadinessAsync(projectId, ticketId, ct);
        if (!readiness.IsReady)
        {
            throw new InvalidOperationException($"Apply blocked: {readiness.Message}");
        }

        // ── 3. Validate Architecture (Test Framework Mismatch) ────────────────
        var archValidation = await _readinessService.ValidateProposalArchitectureAsync(proposal, ct);
        
        _llmTraceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.ApplyValidation",
            WorkspaceName = "Builder",
            ProjectId = projectId,
            TicketId = ticketId,
            WasSuccessful = archValidation.IsReady,
            ParsedResponseSummary = archValidation.IsReady ? "Architecture Validation Passed" : "Architecture Validation Failed",
            ErrorMessage = archValidation.IsReady ? string.Empty : archValidation.Message,
            CreatedAt = DateTime.UtcNow
        });

        if (!archValidation.IsReady)
        {
            proposal.ApplyStatus = "Blocked: Architecture Mismatch";
            throw new InvalidOperationException(archValidation.Message);
        }

        // ── 4. Re-Validate for Safety ─────────────────────────────────────────
        ValidateProposal(proposal);
        if (!proposal.IsAllValid)
        {
            proposal.ApplyStatus = "Validation Failed";
            throw new InvalidOperationException("Apply aborted: validation failed. " + proposal.ValidationSummary);
        }

        // ── 5. Write Files ────────────────────────────────────────────────────
        proposal.ApplyStatus = "Applying...";
        try
        {
            var profile = await _projectProfileService.GetProjectProfileAsync(projectId, ct);
            var safeRoot = !string.IsNullOrEmpty(profile?.SafeWriteRoot) ? Path.GetFullPath(profile.SafeWriteRoot) : Path.GetFullPath(proposal.ProjectRoot);
            
            if (profile != null && !profile.AllowBuilderApply)
            {
                 throw new InvalidOperationException($"Builder apply is disabled for project '{proposal.ProjectName}'.");
            }

            foreach (var change in proposal.Changes)
            {
                var targetPath = Path.GetFullPath(Path.Combine(safeRoot, change.FilePath));
                
                // Final safety guard: must be under safe root
                if (!targetPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Security violation: path {change.FilePath} is outside safe write root.");
                }

                if (change.FullContentAfter == null)
                {
                    throw new InvalidOperationException($"No content provided for {change.FilePath}.");
                }

                var dir = Path.GetDirectoryName(targetPath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await File.WriteAllTextAsync(targetPath, change.FullContentAfter, ct);
            }

            proposal.ApplyStatus = "Applied";
            await _projectService.MarkIndexStaleAsync(
                projectId,
                "Builder applied file changes; code index no longer matches disk.",
                ct);

            _llmTraceService.AddTrace(new LlmTraceEntry
            {
                FeatureName = "Builder.FilesWritten",
                WorkspaceName = "Builder",
                ProjectId = projectId,
                TicketId = ticketId,
                ProposedFileCount = proposal.Changes.Count,
                ProposedFilesList = string.Join(", ", proposal.Changes.Select(c => c.FilePath)),
                WasSuccessful = true,
                ParsedResponseSummary = "Files written and project index marked stale.",
                CreatedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            proposal.ApplyStatus = $"Apply Failed: {ex.Message}";
            throw;
        }

        // ── 6. Run Build ──────────────────────────────────────────────────────
        proposal.BuildStatus = "Build Running...";
        var profileForBuild = await _projectProfileService.GetProjectProfileAsync(projectId, ct);
        var buildCmd = await _projectProfileService.GetDefaultCommandAsync(projectId, "Build", ct);
        
        var buildTarget = profileForBuild?.SolutionFile;

        if (string.IsNullOrWhiteSpace(buildCmd?.CommandText))
        {
            proposal.BuildStatus = "Build Failed";
            proposal.BuildOutput = "Build command is not configured. Set it in Project Profile.";
            proposal.TestStatus = "Skipped (Build Failed)";
            return;
        }

        if (string.IsNullOrWhiteSpace(buildTarget) || !File.Exists(buildTarget))
        {
            proposal.BuildStatus = "Build Failed";
            proposal.BuildOutput = $"Solution file does not exist: {(string.IsNullOrWhiteSpace(buildTarget) ? "Not set" : buildTarget)}. Update Project Profile.";
            proposal.TestStatus = "Skipped (Build Failed)";
            return;
        }

        _llmTraceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.BuildStarted",
            WorkspaceName = "Builder",
            ProjectId = projectId,
            TicketId = ticketId,
            RequestText = buildCmd.CommandText,
            CreatedAt = DateTime.UtcNow
        });

        var buildResult = await _buildService.BuildAsync(buildTarget, ct);
        proposal.BuildStatus = buildResult.Succeeded ? "Build Passed" : "Build Failed";
        proposal.BuildOutput = FormatExecutionOutput(buildResult.Succeeded, buildResult.Command, buildResult.WorkingDirectory, buildResult.ExitCode, buildResult.StandardOutput, buildResult.StandardError, buildResult.Elapsed);
        proposal.BuildDuration = buildResult.Elapsed;

        _llmTraceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.BuildResult",
            WorkspaceName = "Builder",
            ProjectId = projectId,
            TicketId = ticketId,
            DurationMs = (long)buildResult.Elapsed.TotalMilliseconds,
            WasSuccessful = buildResult.Succeeded,
            RawResponseText = buildResult.StandardOutput,
            ErrorMessage = buildResult.StandardError,
            ParsedResponseSummary = $"Build {(buildResult.Succeeded ? "Succeeded" : "Failed")}. Command: {buildResult.Command}",
            CreatedAt = DateTime.UtcNow
        });

        if (!buildResult.Succeeded)
        {
            proposal.TestStatus = "Skipped (Build Failed)";

            var reconciliation = await _errorClassifierService.ClassifyBuildFailureAsync(
                buildResult, 
                profileForBuild ?? new IronDev.Data.Models.ProjectProfile(), 
                proposal.ProjectRoot, 
                ct);

            if (reconciliation != null)
            {
                proposal.Reconciliation = reconciliation;

                _llmTraceService.AddTrace(new LlmTraceEntry
                {
                    FeatureName = "Builder.BuildFailureClassified",
                    WorkspaceName = "Builder",
                    ProjectId = projectId,
                    TicketId = ticketId,
                    WasSuccessful = true,
                    ParsedResponseSummary = $"Failure Category: {reconciliation.FailureCategory}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            _llmTraceService.AddTrace(new LlmTraceEntry
            {
                FeatureName = "Builder.BuildCycleCompleted",
                WorkspaceName = "Builder",
                ProjectId = projectId,
                TicketId = ticketId,
                WasSuccessful = false,
                ParsedResponseSummary = "Build cycle failed at Build step.",
                CreatedAt = DateTime.UtcNow
            });

            return;
        }

        // ── 7. Run Tests ──────────────────────────────────────────────────────
        proposal.TestStatus = "Tests Running...";
        var testCmd = await _projectProfileService.GetDefaultCommandAsync(projectId, "Test", ct);
        
        if (string.IsNullOrWhiteSpace(testCmd?.CommandText))
        {
            proposal.TestStatus = "Tests Failed";
            proposal.TestOutput = "Test command is not configured. Set it in Project Profile.";
            return;
        }

        _llmTraceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.TestStarted",
            WorkspaceName = "Builder",
            ProjectId = projectId,
            TicketId = ticketId,
            RequestText = testCmd.CommandText,
            CreatedAt = DateTime.UtcNow
        });

        var testResult = await _testService.TestAsync(buildTarget, ct);
        proposal.TestStatus = testResult.Succeeded ? "Tests Passed" : "Tests Failed";
        proposal.TestOutput = FormatExecutionOutput(testResult.Succeeded, testResult.Command, testResult.WorkingDirectory, testResult.ExitCode, testResult.StandardOutput, testResult.StandardError, testResult.Elapsed);
        proposal.TestDuration = testResult.Elapsed;

        _llmTraceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.TestResult",
            WorkspaceName = "Builder",
            ProjectId = projectId,
            TicketId = ticketId,
            DurationMs = (long)testResult.Elapsed.TotalMilliseconds,
            WasSuccessful = testResult.Succeeded,
            RawResponseText = testResult.StandardOutput,
            ErrorMessage = testResult.StandardError,
            ParsedResponseSummary = $"Tests {(testResult.Succeeded ? "Passed" : "Failed")}. Command: {testResult.Command}",
            CreatedAt = DateTime.UtcNow
        });

        _llmTraceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.BuildCycleCompleted",
            WorkspaceName = "Builder",
            ProjectId = projectId,
            TicketId = ticketId,
            WasSuccessful = testResult.Succeeded,
            ParsedResponseSummary = $"Build cycle completed. Tests {(testResult.Succeeded ? "Passed" : "Failed")}.",
            CreatedAt = DateTime.UtcNow
        });
    }

    private static BuilderProposal MapToBuilderProposal(CodeChangeProposal inner, TicketBuildContext context)
    {
        var proposal = new BuilderProposal
        {
            TicketId        = inner.TicketId,
            ProjectId       = context.ProjectId,
            ProjectName     = context.ProjectName,
            ProjectRoot     = context.ProjectPath,
            OriginalRequest = string.IsNullOrWhiteSpace(inner.OriginalRequest)
                ? $"{context.TicketTitle}. {context.TicketSummary}".Trim()
                : inner.OriginalRequest,
            Summary         = inner.Summary,
            Rationale       = inner.Rationale,
            GeneratedAt     = DateTime.UtcNow
        };

        foreach (var change in inner.FileChanges)
        {
            proposal.Changes.Add(new ProposedFileChange
            {
                FilePath    = change.FilePath,
                Description = change.ChangeReason,
                Diff        = change.Patch,
                FullContentAfter = change.FullContentAfter,
                IsNewFile   = false,
                IsDeletion  = false,
                IsValid     = true
            });
        }

        return proposal;
    }

    private static void ValidateProposal(BuilderProposal proposal)
    {
        BuilderProposalValidator.Validate(proposal);
    }

    private static string FormatExecutionOutput(bool success, string command, string workingDir, int exitCode, string stdout, string stderr, TimeSpan duration)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"STATUS: {(success ? "SUCCESS" : "FAILED")}");
        sb.AppendLine($"COMMAND: {command}");
        sb.AppendLine($"WORKING DIR: {workingDir}");
        sb.AppendLine($"EXIT CODE: {exitCode}");
        sb.AppendLine($"DURATION: {duration.TotalSeconds:F2}s");
        sb.AppendLine();
        sb.AppendLine("--- STDOUT ---");
        sb.AppendLine(string.IsNullOrWhiteSpace(stdout) ? "(empty)" : stdout);
        sb.AppendLine();
        sb.AppendLine("--- STDERR ---");
        sb.AppendLine(string.IsNullOrWhiteSpace(stderr) ? "(empty)" : stderr);
        return sb.ToString();
    }
}
