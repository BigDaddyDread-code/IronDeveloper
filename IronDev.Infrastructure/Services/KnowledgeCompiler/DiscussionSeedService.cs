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

public sealed class DiscussionSeedService : IDiscussionSeedService
{
    private readonly ILLMService _llmService;
    private readonly ILlmTraceService _traceService;

    public DiscussionSeedService(ILLMService llmService, ILlmTraceService traceService)
    {
        _llmService = llmService;
        _traceService = traceService;
    }

    public async Task<DiscussionSeedResult> GenerateDiscussionDocumentsAsync(
        DiscussionSeedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prompt = BuildPrompt(request);
        var trace = new LlmTraceEntry
        {
            FeatureName = "ProjectKnowledgeCompiler.SeedDiscussions",
            WorkspaceName = "KnowledgeCompiler",
            ProjectId = request.ProjectId,
            RequestText = prompt,
            CurrentUserMessage = request.ProjectSummary,
            ContextSummary = $"Project={request.ProjectName}; existing discussions={request.ExistingDiscussionTitles.Count}",
            CreatedAt = DateTime.UtcNow
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var response = await _llmService.GetResponseAsync(prompt, cancellationToken);
            trace.RawResponseText = response;
            trace.WasSuccessful = true;

            var wrapper = JsonSerializer.Deserialize<DiscussionSeedWrapper>(
                CleanJsonResponse(response),
                KnowledgeCompilerJson.Options);

            var discussions = (wrapper?.Discussions ?? [])
                .Where(d => !string.IsNullOrWhiteSpace(d.Title))
                .OrderBy(d => d.SuggestedOrder <= 0 ? int.MaxValue : d.SuggestedOrder)
                .ToList();

            if (discussions.Count == 0)
            {
                trace.WasSuccessful = false;
                trace.ErrorMessage = "The model returned no discussion documents.";
                trace.ParsedResponseSummary = "No discussions parsed.";
                return new DiscussionSeedResult
                {
                    Success = false,
                    ErrorMessage = "No discussion documents were returned."
                };
            }

            trace.ParsedResponseSummary = $"Parsed {discussions.Count} discussion documents.";
            return new DiscussionSeedResult
            {
                Success = true,
                Discussions = discussions
            };
        }
        catch (Exception ex)
        {
            trace.WasSuccessful = false;
            trace.ErrorMessage = ex.Message;
            trace.ParsedResponseSummary = "Discussion seed generation failed.";
            return new DiscussionSeedResult
            {
                Success = false,
                ErrorMessage = $"Discussion generation failed: {ex.Message}"
            };
        }
        finally
        {
            sw.Stop();
            trace.DurationMs = sw.ElapsedMilliseconds;
            _traceService.AddTrace(trace);
        }
    }

    private static string BuildPrompt(DiscussionSeedRequest request)
    {
        var existing = request.ExistingDiscussionTitles.Count == 0
            ? "None"
            : string.Join("\n", request.ExistingDiscussionTitles.Select(title => $"- {title}"));

        return $$"""
You are IronDev's Project Knowledge Compiler.

Generate guided discussion documents from a project summary.
These are thinking spaces, not final decisions and not tickets.

Return ONLY valid JSON matching this schema:
{
  "discussions": [
    {
      "title": "Database and Persistence",
      "purpose": "Work out what data needs to be stored and why.",
      "prompts": ["What entities must be persisted?"],
      "possibleOutputs": ["Database architecture decision", "Persistence tickets"],
      "suggestedArea": "Architecture",
      "suggestedOrder": 1
    }
  ]
}

Rules:
- Create 6-10 useful discussions.
- Match the discussions to this project, not a generic checklist.
- Do not decide the architecture for the user.
- Avoid duplicating existing discussion titles.
- Keep prompts concrete and review-friendly.

Project: {{request.ProjectName}}

Project summary:
{{request.ProjectSummary}}

Existing discussions:
{{existing}}
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

    private sealed class DiscussionSeedWrapper
    {
        public List<GeneratedDiscussionDocument> Discussions { get; set; } = [];
    }
}
