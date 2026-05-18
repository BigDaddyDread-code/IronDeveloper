using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services.KnowledgeCompiler;

public sealed class DiscussionResolverService : IDiscussionResolverService
{
    private readonly ILLMService _llmService;
    private readonly ILlmTraceService _traceService;

    public DiscussionResolverService(ILLMService llmService, ILlmTraceService traceService)
    {
        _llmService = llmService;
        _traceService = traceService;
    }

    public async Task<DiscussionResolutionResult> ResolveDiscussionAsync(
        DiscussionResolverRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.DiscussionTitle))
        {
            return new DiscussionResolutionResult
            {
                Success = false,
                ErrorMessage = "A discussion title is required."
            };
        }

        var prompt = BuildPrompt(request);
        var trace = new LlmTraceEntry
        {
            FeatureName = "ProjectKnowledgeCompiler.ResolveDiscussion",
            WorkspaceName = "KnowledgeCompiler",
            ProjectId = request.ProjectId,
            RequestText = prompt,
            CurrentUserMessage = request.DiscussionNotes,
            ContextSummary =
                $"Discussion={request.DiscussionTitle}; sourceDocumentId={request.SourceDiscussionDocumentId}; " +
                $"existing decisions={request.ExistingDecisionTitles.Count}; existing tickets={request.ExistingTicketTitles.Count}",
            CreatedAt = DateTime.UtcNow
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var response = await _llmService.GetResponseAsync(prompt, cancellationToken);
            trace.RawResponseText = response;
            trace.WasSuccessful = true;

            var parsed = JsonSerializer.Deserialize<DiscussionResolutionJson>(
                CleanJsonResponse(response),
                KnowledgeCompilerJson.Options);

            if (parsed == null)
            {
                return Failure("The model returned an empty resolution.", trace);
            }

            var proposals = parsed.Proposals
                .Where(p => !string.IsNullOrWhiteSpace(p.Title))
                .Select(p => NormalizeProposal(p, request.SourceDiscussionDocumentId))
                .ToList();

            if (proposals.Count == 0 &&
                string.IsNullOrWhiteSpace(parsed.ResolutionSummary) &&
                parsed.OpenQuestions.Count == 0)
            {
                return Failure("The model did not return a usable resolution.", trace);
            }

            trace.ParsedResponseSummary =
                $"Parsed {proposals.Count} proposals, {parsed.OpenQuestions.Count} open questions, confidence={parsed.ConfidenceScore}.";

            return new DiscussionResolutionResult
            {
                Success = true,
                ResolutionSummary = parsed.ResolutionSummary,
                Proposals = proposals,
                OpenQuestions = parsed.OpenQuestions
                    .Where(q => !string.IsNullOrWhiteSpace(q))
                    .Select(q => q.Trim())
                    .ToList(),
                BuildOrder = parsed.BuildOrder
                    .Where(step => !string.IsNullOrWhiteSpace(step))
                    .Select(step => step.Trim())
                    .ToList(),
                ConfidenceScore = Math.Clamp(parsed.ConfidenceScore, 0, 100)
            };
        }
        catch (Exception ex)
        {
            trace.WasSuccessful = false;
            trace.ErrorMessage = ex.Message;
            trace.ParsedResponseSummary = "Discussion resolution failed.";
            return new DiscussionResolutionResult
            {
                Success = false,
                ErrorMessage = $"Discussion resolution failed: {ex.Message}"
            };
        }
        finally
        {
            sw.Stop();
            trace.DurationMs = sw.ElapsedMilliseconds;
            _traceService.AddTrace(trace);
        }
    }

    private static DiscussionResolutionResult Failure(string message, LlmTraceEntry trace)
    {
        trace.WasSuccessful = false;
        trace.ErrorMessage = message;
        trace.ParsedResponseSummary = message;
        return new DiscussionResolutionResult
        {
            Success = false,
            ErrorMessage = message
        };
    }

    private static ArtefactProposal NormalizeProposal(
        ArtefactProposal proposal,
        long? sourceDiscussionDocumentId)
    {
        return new ArtefactProposal
        {
            Id = proposal.Id == Guid.Empty ? Guid.NewGuid() : proposal.Id,
            Kind = proposal.Kind,
            Status = ArtefactProposalStatus.Proposed,
            Title = proposal.Title.Trim(),
            Summary = proposal.Summary.Trim(),
            Detail = proposal.Detail.Trim(),
            Rationale = proposal.Rationale.Trim(),
            Category = proposal.Category.Trim(),
            Priority = string.IsNullOrWhiteSpace(proposal.Priority) ? "Medium" : proposal.Priority.Trim(),
            RiskLevel = string.IsNullOrWhiteSpace(proposal.RiskLevel) ? "Medium" : proposal.RiskLevel.Trim(),
            SuggestedBuildOrder = proposal.SuggestedBuildOrder,
            ConfidenceScore = Math.Clamp(proposal.ConfidenceScore, 0, 100),
            SourceDiscussionDocumentId = sourceDiscussionDocumentId,
            AcceptanceCriteria = CleanList(proposal.AcceptanceCriteria),
            TestSuggestions = CleanList(proposal.TestSuggestions),
            AffectedFiles = CleanList(proposal.AffectedFiles),
            AffectedSymbols = CleanList(proposal.AffectedSymbols),
            GroundingWarnings = CleanList(proposal.GroundingWarnings)
        };
    }

    private static IReadOnlyList<string> CleanList(IReadOnlyList<string> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();

    private static string BuildPrompt(DiscussionResolverRequest request)
    {
        var existingDecisions = request.ExistingDecisionTitles.Count == 0
            ? "None"
            : string.Join("\n", request.ExistingDecisionTitles.Select(title => $"- {title}"));
        var existingTickets = request.ExistingTicketTitles.Count == 0
            ? "None"
            : string.Join("\n", request.ExistingTicketTitles.Select(title => $"- {title}"));

        return $$"""
You are IronDev's Project Knowledge Compiler.

Resolve a project discussion into reviewable artefact proposals.
Do not silently decide or save anything. The user will review and accept or reject each proposal.

Return ONLY valid JSON matching this schema:
{
  "resolutionSummary": "short summary of what was clarified",
  "confidenceScore": 0,
  "openQuestions": ["remaining question"],
  "buildOrder": ["1. Add persisted discussion model", "2. Add resolver service"],
  "proposals": [
    {
      "kind": "Decision|ArchitectureDocument|ProjectDocument|Requirement|Risk|OpenQuestion|Ticket",
      "title": "Use SQL Server for Alpha project memory",
      "summary": "Short human-readable summary.",
      "detail": "Full proposed content.",
      "rationale": "Why this should exist.",
      "category": "Architecture",
      "priority": "Low|Medium|High|Critical",
      "riskLevel": "Low|Medium|High",
      "suggestedBuildOrder": 1,
      "confidenceScore": 0,
      "acceptanceCriteria": ["criterion"],
      "testSuggestions": ["test suggestion"],
      "affectedFiles": [],
      "affectedSymbols": [],
      "groundingWarnings": []
    }
  ]
}

Rules:
- Create proposals only from the discussion content and project summary.
- Prefer traceable decisions, docs, requirements, risks, open questions, and tickets.
- Tickets must be buildable and small enough for Alpha 0.1.
- Do not duplicate existing decisions or tickets.
- Keep document updates as proposals; do not claim they have already been applied.
- Every proposal should be useful even if accepted on its own.
- Use affectedFiles and affectedSymbols only when the discussion clearly names real code.

Project: {{request.ProjectName}}

Project summary:
{{request.ProjectSummary}}

Discussion title:
{{request.DiscussionTitle}}

Guided discussion prompt/document:
{{request.DiscussionPrompt}}

User discussion notes:
{{request.DiscussionNotes}}

Existing decisions:
{{existingDecisions}}

Existing tickets:
{{existingTickets}}
""";
    }

    private static string CleanJsonResponse(string input)
    {
        var cleaned = input.Trim();
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[7..];
        else if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[3..];

        if (cleaned.EndsWith("```", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^3].Trim();

        return cleaned;
    }

    private sealed class DiscussionResolutionJson
    {
        public string ResolutionSummary { get; set; } = string.Empty;
        public int ConfidenceScore { get; set; }
        public List<string> OpenQuestions { get; set; } = [];
        public List<string> BuildOrder { get; set; } = [];
        public List<ArtefactProposal> Proposals { get; set; } = [];
    }
}
