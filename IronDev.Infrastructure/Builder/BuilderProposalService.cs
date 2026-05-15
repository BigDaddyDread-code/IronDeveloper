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
        IProjectService projectService)
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
            ErrorMessage = readiness.IsReady ? null : string.Join("; ", readiness.BlockingIssues),
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
        
        if (!proposal.IsAllValid)
        {
            var failures = proposal.Changes.Where(c => !c.IsValid).ToList();
            trace.ErrorMessage = $"Rejected {failures.Count} files: " + 
                string.Join("; ", failures.Select(f => $"{f.FilePath}: {f.ValidationMessage}"));
        }
        
        _llmTraceService.AddTrace(trace);

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
            ErrorMessage = archValidation.IsReady ? null : archValidation.Message,
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
            throw new InvalidOperationException("Apply aborted: validation failed for one or more files.");
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
            _llmTraceService.AddTrace(new LlmTraceEntry
            {
                FeatureName = "Builder.FilesWritten",
                WorkspaceName = "Builder",
                ProjectId = projectId,
                TicketId = ticketId,
                ProposedFileCount = proposal.Changes.Count,
                ProposedFilesList = string.Join(", ", proposal.Changes.Select(c => c.FilePath)),
                WasSuccessful = true,
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
            OriginalRequest = inner.OriginalRequest,
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
                IsNewFile   = false, // TODO: derive from LLM if possible
                IsDeletion  = false,
                IsValid     = true
            });
        }

        return proposal;
    }

    private void ValidateProposal(BuilderProposal proposal)
    {
        if (proposal.Changes.Count == 0)
        {
            // Empty proposal is technically valid but we should probably note it.
            return;
        }

        foreach (var change in proposal.Changes)
        {
            if (string.IsNullOrWhiteSpace(change.FilePath))
            {
                MarkInvalid(change, "Empty file path.");
                continue;
            }

            // 1. Block absolute paths
            if (Path.IsPathRooted(change.FilePath))
            {
                MarkInvalid(change, "Absolute file paths are not allowed.");
                continue;
            }

            // 2. Block .. traversal
            if (change.FilePath.Contains(".."))
            {
                MarkInvalid(change, "Path traversal (..) is not allowed.");
                continue;
            }

            // 3. Block paths outside project root
            // Since paths are relative, we check if they would resolve outside.
            // But they are relative to root, so any path starting with / or \ might be risky if not handled.
            // The prompt says "relative file paths only".
            
            // 4. Deletion check (v1 restriction)
            if (change.IsDeletion)
            {
                MarkInvalid(change, "File deletions are not allowed in v1.");
                continue;
            }

            // 5. Host protection
            if (proposal.ProjectRoot.Contains("BookSeller", StringComparison.OrdinalIgnoreCase))
            {
                if (change.FilePath.Contains("IronDev", StringComparison.OrdinalIgnoreCase) || 
                    change.FilePath.Contains("IronDeveloper", StringComparison.OrdinalIgnoreCase))
                {
                    MarkInvalid(change, "Cannot modify host files when active project is external.");
                    continue;
                }
            }

            // 6. Structural Validation for C#
            if (change.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                ValidateCSharpStructure(proposal, change);
            }
        }
    }

    private void ValidateCSharpStructure(BuilderProposal proposal, ProposedFileChange change)
    {
        var content = change.FullContentAfter;
        if (string.IsNullOrWhiteSpace(content)) return;

        // Heuristic: reject if it looks like a generic standalone sample
        if (content.Contains("namespace Sample") || content.Contains("class Program") && !change.FilePath.Contains("Program.cs"))
        {
            MarkInvalid(change, "Proposal appears to be generic sample code.");
            return;
        }

        // BookSeller specific structural rules
        if (proposal.ProjectName.Contains("BookSeller", StringComparison.OrdinalIgnoreCase))
        {
            if (change.FilePath.Contains("BookService.cs"))
            {
                if (!content.Contains("namespace BookSeller.Core.Services"))
                    MarkInvalid(change, "Missing namespace BookSeller.Core.Services");
                if (!content.Contains("class BookService"))
                    MarkInvalid(change, "Missing class BookService");
                if (!content.Contains("IBookService"))
                    MarkInvalid(change, "IBookService implementation removed.");
                if (content.Contains("class Book") && !content.Contains("public class BookService")) // likely duplicate model
                    MarkInvalid(change, "Duplicate Book model class detected in service file.");
            }
            
            if (change.FilePath.Contains("Book.cs") && !content.Contains("namespace BookSeller.Core.Models"))
            {
                MarkInvalid(change, "Missing namespace BookSeller.Core.Models in Book model.");
            }
        }
    }

    private static void MarkInvalid(ProposedFileChange change, string message)
    {
        change.IsValid = false;
        change.ValidationMessage = message;
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
