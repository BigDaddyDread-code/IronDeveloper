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

    public BuilderProposalService(
        IBuilderContextService     contextService,
        ICodeChangeProposalService proposalService,
        ILlmTraceService           llmTraceService,
        ITicketService             ticketService,
        IDotNetBuildService        buildService,
        IDotNetTestService         testService)
    {
        _contextService  = contextService;
        _proposalService = proposalService;
        _llmTraceService = llmTraceService;
        _ticketService   = ticketService;
        _buildService    = buildService;
        _testService     = testService;
    }

    public async Task<BuilderProposal> GenerateProposalAsync(long ticketId, CancellationToken ct = default)
    {
        // Resolve ticket to find its ProjectId
        var ticket = await _ticketService.GetTicketByIdAsync(ticketId, ct)
            ?? throw new InvalidOperationException($"Ticket {ticketId} not found.");

        int projectId = ticket.ProjectId; 

        var context = await _contextService.AssembleContextAsync(projectId, ticketId, ct);
        var innerProposal = await _proposalService.GenerateProposalAsync(context, ct);

        var proposal = MapToBuilderProposal(innerProposal, context);
        ValidateProposal(proposal);

        // Trace validation
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
        // ── 1. Trace Requested ────────────────────────────────────────────────
        _llmTraceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.ApplyRequested",
            WorkspaceName = "Builder",
            ProjectId = proposal.ProjectId,
            TicketId = proposal.TicketId,
            ActiveProjectName = proposal.ProjectName,
            ActiveProjectPath = proposal.ProjectRoot,
            IsProposalOnly = false,
            ProposedFileCount = proposal.Changes.Count,
            CreatedAt = DateTime.UtcNow
        });

        // ── 2. Re-Validate for Safety ─────────────────────────────────────────
        ValidateProposal(proposal);
        if (!proposal.IsAllValid)
        {
            proposal.ApplyStatus = "Validation Failed";
            _llmTraceService.AddTrace(new LlmTraceEntry
            {
                FeatureName = "Builder.ApplyValidation",
                WorkspaceName = "Builder",
                ProjectId = proposal.ProjectId,
                TicketId = proposal.TicketId,
                ErrorMessage = "Apply aborted: validation failed for one or more files.",
                WasSuccessful = false,
                CreatedAt = DateTime.UtcNow
            });
            throw new InvalidOperationException("Apply aborted: validation failed for one or more files.");
        }

        // ── 3. Write Files ────────────────────────────────────────────────────
        proposal.ApplyStatus = "Applying...";
        try
        {
            var projectRoot = Path.GetFullPath(proposal.ProjectRoot);
            foreach (var change in proposal.Changes)
            {
                var targetPath = Path.GetFullPath(Path.Combine(projectRoot, change.FilePath));
                
                // Final safety guard: must be under root
                if (!targetPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Security violation: path {change.FilePath} is outside project root.");
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
                ProjectId = proposal.ProjectId,
                TicketId = proposal.TicketId,
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

        // ── 4. Run Build ──────────────────────────────────────────────────────
        proposal.BuildStatus = "Build Running...";
        var slnPath = Path.Combine(proposal.ProjectRoot, "BookSeller.sln");
        
        _llmTraceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.BuildStarted",
            WorkspaceName = "Builder",
            ProjectId = proposal.ProjectId,
            TicketId = proposal.TicketId,
            RequestText = $"dotnet build \"{slnPath}\" --no-incremental -v quiet",
            CreatedAt = DateTime.UtcNow
        });

        var buildResult = await _buildService.BuildAsync(slnPath, ct);
        proposal.BuildStatus = buildResult.Succeeded ? "Build Passed" : "Build Failed";
        proposal.BuildOutput = buildResult.Succeeded ? buildResult.StandardOutput : buildResult.StandardError;
        proposal.BuildDuration = buildResult.Elapsed;

        _llmTraceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.BuildResult",
            WorkspaceName = "Builder",
            ProjectId = proposal.ProjectId,
            TicketId = proposal.TicketId,
            DurationMs = (long)buildResult.Elapsed.TotalMilliseconds,
            WasSuccessful = buildResult.Succeeded,
            RawResponseText = buildResult.StandardOutput,
            ErrorMessage = buildResult.StandardError,
            CreatedAt = DateTime.UtcNow
        });

        if (!buildResult.Succeeded)
        {
            proposal.TestStatus = "Skipped (Build Failed)";
            return;
        }

        // ── 5. Run Tests ──────────────────────────────────────────────────────
        proposal.TestStatus = "Tests Running...";
        _llmTraceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.TestStarted",
            WorkspaceName = "Builder",
            ProjectId = proposal.ProjectId,
            TicketId = proposal.TicketId,
            RequestText = $"dotnet test \"{slnPath}\" --logger \"console;verbosity=minimal\"",
            CreatedAt = DateTime.UtcNow
        });

        var testResult = await _testService.TestAsync(slnPath, ct);
        proposal.TestStatus = testResult.Succeeded ? "Tests Passed" : "Tests Failed";
        proposal.TestOutput = testResult.Succeeded ? testResult.StandardOutput : testResult.StandardError;
        proposal.TestDuration = testResult.Elapsed;

        _llmTraceService.AddTrace(new LlmTraceEntry
        {
            FeatureName = "Builder.TestResult",
            WorkspaceName = "Builder",
            ProjectId = proposal.ProjectId,
            TicketId = proposal.TicketId,
            DurationMs = (long)testResult.Elapsed.TotalMilliseconds,
            WasSuccessful = testResult.Succeeded,
            RawResponseText = testResult.StandardOutput,
            ErrorMessage = testResult.StandardError,
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
        }
    }

    private static void MarkInvalid(ProposedFileChange change, string message)
    {
        change.IsValid = false;
        change.ValidationMessage = message;
    }
}
