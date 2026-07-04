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

        // â”€â”€ 1. Evaluate Build Readiness â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
            // Typed so callers can tell a readiness block (user resolves the ticket)
            // apart from proposal/model/service failures (operator investigates).
            throw new BuildReadinessBlockedException(
                $"Build blocked: {readiness.Message} " + string.Join(" ", readiness.BlockingIssues),
                readiness.BlockingIssues);
        }

        var context = await _contextService.AssembleContextAsync(projectId, ticketId, ct);
        var innerProposal = await _proposalService.GenerateProposalAsync(context, ct);

        var proposal = MapToBuilderProposal(innerProposal, context);
        ValidateProposal(proposal);

        // â”€â”€ 2. Trace Validation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ 3. Record Traceability Reference â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

    public Task ApplyProposalAsync(BuilderProposal proposal, CancellationToken ct = default)
    {
        // Retired by P0-4: the direct file-write path mutated source without the
        // governed evidence chain (no dry-run receipt, no promotion approval, no
        // apply receipt). The only apply path is the governed workspace spine via
        // skeleton runs: POST .../tickets/{ticketId}/skeleton-runs/{runId}/apply.
        throw new NotSupportedException(
            "Direct proposal apply is retired. Source changes travel through the governed " +
            "workspace apply spine (skeleton runs): critic package -> accepted approval -> " +
            "continuation -> evidence-chained copy-only apply.");
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
            GeneratedAt     = DateTime.UtcNow,
            ModelProvider   = inner.ModelProvider,
            ModelName       = inner.ModelName
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
