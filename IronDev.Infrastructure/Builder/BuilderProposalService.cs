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

    public BuilderProposalService(
        IBuilderContextService     contextService,
        ICodeChangeProposalService proposalService,
        ILlmTraceService           llmTraceService,
        ITicketService             ticketService)
    {
        _contextService  = contextService;
        _proposalService = proposalService;
        _llmTraceService = llmTraceService;
        _ticketService   = ticketService;
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
