using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services.KnowledgeCompiler;

public sealed class KnowledgeArtefactApplyService : IKnowledgeArtefactApplyService
{
    private readonly IProjectMemoryService _memoryService;
    private readonly ITicketService _ticketService;
    private readonly IArtifactSourceReferenceService _referenceService;
    private readonly ICurrentTenantContext _tenantContext;
    private readonly ISemanticMemoryService _semanticMemoryService;

    public KnowledgeArtefactApplyService(
        IProjectMemoryService memoryService,
        ITicketService ticketService,
        IArtifactSourceReferenceService referenceService,
        ICurrentTenantContext tenantContext,
        ISemanticMemoryService semanticMemoryService)
    {
        _memoryService = memoryService;
        _ticketService = ticketService;
        _referenceService = referenceService;
        _tenantContext = tenantContext;
        _semanticMemoryService = semanticMemoryService;
    }

    public async Task<ArtefactApplyResult> ApplyAsync(
        ArtefactApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var results = new List<AppliedArtefactResult>();
        foreach (var proposal in request.Proposals)
        {
            results.Add(await ApplyOneAsync(request.ProjectId, proposal, cancellationToken));
        }

        return new ArtefactApplyResult
        {
            AppliedCount = results.Count(r => r.Success),
            Results = results
        };
    }

    private async Task<AppliedArtefactResult> ApplyOneAsync(
        int projectId,
        ArtefactProposal proposal,
        CancellationToken cancellationToken)
    {
        try
        {
            return proposal.Kind switch
            {
                ArtefactProposalKind.Decision => await ApplyDecisionAsync(projectId, proposal, cancellationToken),
                ArtefactProposalKind.Ticket => await ApplyTicketAsync(projectId, proposal, cancellationToken),
                _ => await ApplyContextDocumentAsync(projectId, proposal, cancellationToken)
            };
        }
        catch (Exception ex)
        {
            return new AppliedArtefactResult
            {
                ProposalId = proposal.Id,
                Kind = proposal.Kind,
                Success = false,
                Message = ex.Message
            };
        }
    }

    private async Task<AppliedArtefactResult> ApplyDecisionAsync(
        int projectId,
        ArtefactProposal proposal,
        CancellationToken cancellationToken)
    {
        var decision = new ProjectDecision
        {
            ProjectId = projectId,
            Title = proposal.Title,
            Detail = string.IsNullOrWhiteSpace(proposal.Detail) ? proposal.Summary : proposal.Detail,
            Reason = proposal.Rationale,
            Category = string.IsNullOrWhiteSpace(proposal.Category) ? "Knowledge Compiler" : proposal.Category,
            Status = "Accepted",
            LinkedFilePaths = ToMultiline(proposal.AffectedFiles),
            LinkedSymbols = ToMultiline(proposal.AffectedSymbols)
        };

        var id = await _memoryService.SaveDecisionAsync(decision, cancellationToken);
        await RecordSourceReferenceAsync("Decision", id, projectId, proposal, cancellationToken);

        return Applied(proposal, "Decision", id, $"Decision saved: {proposal.Title}");
    }

    private async Task<AppliedArtefactResult> ApplyTicketAsync(
        int projectId,
        ArtefactProposal proposal,
        CancellationToken cancellationToken)
    {
        var ticket = new ProjectTicket
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            Title = proposal.Title,
            TicketType = "Feature",
            Priority = string.IsNullOrWhiteSpace(proposal.Priority) ? "Medium" : proposal.Priority,
            Summary = proposal.Summary,
            Background = proposal.Rationale,
            Problem = proposal.Detail,
            AcceptanceCriteria = ToBulletList(proposal.AcceptanceCriteria),
            TechnicalNotes = BuildTicketTechnicalNotes(proposal),
            Status = "Draft",
            Content = string.IsNullOrWhiteSpace(proposal.Summary) ? proposal.Detail : proposal.Summary,
            LinkedFilePaths = ToMultiline(proposal.AffectedFiles),
            LinkedSymbols = ToMultiline(proposal.AffectedSymbols),
            UnitTests = ToBulletList(proposal.TestSuggestions),
            BuildValidation = "dotnet build",
            ContextSummary = $"Knowledge Compiler ticket from discussion #{proposal.SourceDiscussionDocumentId}.",
            IsGenerated = true,
            GenerationNote = $"Generated by Project Knowledge Compiler from discussion #{proposal.SourceDiscussionDocumentId}."
        };

        var id = await _ticketService.SaveTicketAsync(ticket, cancellationToken);
        await RecordSourceReferenceAsync("Ticket", id, projectId, proposal, cancellationToken);

        return Applied(proposal, "Ticket", id, $"Ticket draft saved: {proposal.Title}");
    }

    private async Task<AppliedArtefactResult> ApplyContextDocumentAsync(
        int projectId,
        ArtefactProposal proposal,
        CancellationToken cancellationToken)
    {
        var document = new ProjectContextDocument
        {
            ProjectId = projectId,
            DocumentType = MapDocumentType(proposal.Kind),
            AuthorityLevel = proposal.Kind == ArtefactProposalKind.OpenQuestion ? "OpenQuestion" : "ResolvedKnowledge",
            Status = "Active",
            Title = proposal.Title,
            Content = BuildDocumentContent(proposal),
            Summary = proposal.Summary,
            Tags = BuildTags(proposal),
            AppliesToCapability = proposal.Category,
            AppliesToArea = proposal.Kind.ToString(),
            Source = proposal.SourceDiscussionDocumentId.HasValue
                ? $"DiscussionDocument:{proposal.SourceDiscussionDocumentId.Value}"
                : "ProjectKnowledgeCompiler"
        };

        var id = await _memoryService.SaveContextDocumentAsync(document, cancellationToken);
        document.Id = id;
        await RecordSourceReferenceAsync("ProjectContextDocument", id, projectId, proposal, cancellationToken);
        await TryEmbedContextDocumentAsync(document, cancellationToken);

        return Applied(proposal, "ProjectContextDocument", id, $"Document saved: {proposal.Title}");
    }

    private async Task TryEmbedContextDocumentAsync(
        ProjectContextDocument document,
        CancellationToken cancellationToken)
    {
        try
        {
            await _semanticMemoryService.EmbedAndStoreAsync(document, cancellationToken);
        }
        catch
        {
            // Saving project knowledge must not fail just because the retrieval index is unavailable.
        }
    }

    private async Task RecordSourceReferenceAsync(
        string artifactType,
        long artifactId,
        int projectId,
        ArtefactProposal proposal,
        CancellationToken cancellationToken)
    {
        if (!proposal.SourceDiscussionDocumentId.HasValue)
            return;

        await _referenceService.RecordReferenceAsync(new ArtifactSourceReference
        {
            TenantId = _tenantContext.TenantId,
            ProjectId = projectId,
            ArtifactType = artifactType,
            ArtifactId = artifactId,
            SourceType = "DiscussionDocument",
            SourceId = proposal.SourceDiscussionDocumentId.Value,
            ReferenceType = "CreatedFrom",
            Summary = $"{artifactType} '{proposal.Title}' created from discussion #{proposal.SourceDiscussionDocumentId.Value}.",
            RelevanceScore = proposal.ConfidenceScore > 0 ? proposal.ConfidenceScore / 100m : null,
            IsRequired = true
        }, cancellationToken);
    }

    private static AppliedArtefactResult Applied(
        ArtefactProposal proposal,
        string artifactType,
        long artifactId,
        string message)
    {
        return new AppliedArtefactResult
        {
            ProposalId = proposal.Id,
            Kind = proposal.Kind,
            Success = true,
            ArtifactType = artifactType,
            ArtifactId = artifactId,
            Message = message
        };
    }

    private static string MapDocumentType(ArtefactProposalKind kind) => kind switch
    {
        ArtefactProposalKind.ArchitectureDocument => "ArchitectureDocument",
        ArtefactProposalKind.ProjectDocument => "ProjectDocument",
        ArtefactProposalKind.Requirement => "Requirement",
        ArtefactProposalKind.Risk => "Risk",
        ArtefactProposalKind.OpenQuestion => "OpenQuestion",
        _ => "ProjectDocument"
    };

    private static string BuildDocumentContent(ArtefactProposal proposal)
    {
        var sb = new StringBuilder();
        AppendSection(sb, "Summary", proposal.Summary);
        AppendSection(sb, "Detail", proposal.Detail);
        AppendSection(sb, "Rationale", proposal.Rationale);
        AppendSection(sb, "Confidence", proposal.ConfidenceScore > 0 ? $"{proposal.ConfidenceScore}/100" : string.Empty);
        AppendList(sb, "Affected Files", proposal.AffectedFiles);
        AppendList(sb, "Affected Symbols", proposal.AffectedSymbols);
        AppendList(sb, "Acceptance Criteria", proposal.AcceptanceCriteria);
        AppendList(sb, "Test Suggestions", proposal.TestSuggestions);
        AppendList(sb, "Grounding Warnings", proposal.GroundingWarnings);
        if (proposal.SourceDiscussionDocumentId.HasValue)
            AppendSection(sb, "Source", $"DiscussionDocument:{proposal.SourceDiscussionDocumentId.Value}");
        return sb.ToString().TrimEnd();
    }

    private static string BuildTicketTechnicalNotes(ArtefactProposal proposal)
    {
        var sb = new StringBuilder();
        AppendSection(sb, "Proposed Change", proposal.Detail);
        AppendSection(sb, "Rationale", proposal.Rationale);
        AppendSection(sb, "Risk Level", proposal.RiskLevel);
        AppendSection(sb, "Suggested Build Order", proposal.SuggestedBuildOrder > 0 ? proposal.SuggestedBuildOrder.ToString() : string.Empty);
        AppendSection(sb, "Confidence", proposal.ConfidenceScore > 0 ? $"{proposal.ConfidenceScore}/100" : string.Empty);
        AppendList(sb, "Affected Files", proposal.AffectedFiles);
        AppendList(sb, "Affected Symbols", proposal.AffectedSymbols);
        AppendList(sb, "Grounding Warnings", proposal.GroundingWarnings);
        if (proposal.SourceDiscussionDocumentId.HasValue)
            AppendSection(sb, "Source Discussion", $"DiscussionDocument:{proposal.SourceDiscussionDocumentId.Value}");
        return sb.ToString().TrimEnd();
    }

    private static string? BuildTags(ArtefactProposal proposal)
    {
        var tags = new List<string> { "knowledge-compiler", proposal.Kind.ToString() };
        if (!string.IsNullOrWhiteSpace(proposal.Category))
            tags.Add(proposal.Category);
        return string.Join(",", tags);
    }

    private static string? ToMultiline(IReadOnlyList<string> values)
        => values.Count == 0 ? null : string.Join("\n", values);

    private static string? ToBulletList(IReadOnlyList<string> values)
        => values.Count == 0 ? null : string.Join("\n", values.Select(value => $"- {value}"));

    private static void AppendSection(StringBuilder sb, string title, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (sb.Length > 0) sb.AppendLine();
        sb.AppendLine(title);
        sb.AppendLine(value.Trim());
    }

    private static void AppendList(StringBuilder sb, string title, IReadOnlyList<string> values)
    {
        if (values.Count == 0) return;
        if (sb.Length > 0) sb.AppendLine();
        sb.AppendLine(title);
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
            sb.AppendLine($"- {value.Trim()}");
    }
}
