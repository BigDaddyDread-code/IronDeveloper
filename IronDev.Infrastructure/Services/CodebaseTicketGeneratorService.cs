using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

public sealed class CodebaseTicketGeneratorService : ICodebaseTicketGeneratorService
{
    private readonly ILLMService             _llmService;
    private readonly IProjectMemoryService    _memoryService;
    private readonly ILlmTraceService         _llmTraceService;

    public CodebaseTicketGeneratorService(
        ILLMService           llmService,
        IProjectMemoryService memoryService,
        ILlmTraceService      llmTraceService)
    {
        _llmService    = llmService;
        _memoryService = memoryService;
        _llmTraceService = llmTraceService;
    }

    public async Task<CodebaseTicketGenerationResult> GenerateTicketsAsync(
        int projectId, 
        CancellationToken ct = default)
    {
        try
        {
            // 1. Assemble context
            var summary   = await _memoryService.GetLatestSummaryAsync(projectId, ct);
            var decisions = await _memoryService.GetRecentDecisionsAsync(projectId, 10, ct);
            var rules     = await _memoryService.GetProjectRulesAsync(projectId, ct);

            var contextBuilder = new System.Text.StringBuilder();
            contextBuilder.AppendLine("PROJECT CONTEXT:");
            if (summary != null)
            {
                contextBuilder.AppendLine($"Summary: {summary.Summary}");
            }
            else
            {
                contextBuilder.AppendLine("No project summary available.");
            }

            if (decisions.Count > 0)
            {
                contextBuilder.AppendLine("\nRECENT ARCHITECTURAL DECISIONS:");
                foreach (var d in decisions)
                {
                    contextBuilder.AppendLine($"- {d.Title}: {d.Detail}");
                }
            }

            if (rules.Count > 0)
            {
                contextBuilder.AppendLine("\nPROJECT RULES AND STANDARDS:");
                foreach (var r in rules.Where(r => r.AppliesTo == "Both" || r.AppliesTo == "Ticket"))
                {
                    contextBuilder.AppendLine($"- [{r.EnforcementLevel}] {r.Name}: {r.Description}");
                    if (!string.IsNullOrWhiteSpace(r.ValidationHint))
                        contextBuilder.AppendLine($"  Validation Hint: {r.ValidationHint}");
                }
            }

            // 2. Prepare Prompt
            var prompt = $@"
You are a senior technical architect analyzing a project's codebase and history.
Based on the provided context, identify 3-5 technical improvements, refactoring tasks, or maintenance items that would benefit the project.

For each item, output a structured ticket draft.
Return ONLY valid JSON matching this schema:
{{
  ""drafts"": [
    {{
      ""title"": ""string"",
      ""summary"": ""string"",
      ""background"": ""string"",
      ""acceptanceCriteria"": ""string"",
      ""priority"": ""Low|Medium|High|Critical"",
      ""ticketType"": ""Task|Bug|Feature|Spike|Chore"",
      ""unitTests"": ""string"",
      ""integrationTests"": ""string"",
      ""manualTests"": ""string"",
      ""regressionTests"": ""string"",
      ""buildValidation"": ""dotnet build""
    }}
  ]
}}

{contextBuilder}

Focus on actionable, specific improvements. Avoid generic advice.
";

            // 3. Call LLM with tracing
            var trace = new LlmTraceEntry
            {
                FeatureName = "CodebaseAnalysis",
                WorkspaceName = "Architect",
                ProjectId = projectId,
                RequestText = prompt,
                CreatedAt = DateTime.UtcNow
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            string response;
            try
            {
                response = await _llmService.GetResponseAsync(prompt, ct);
                trace.WasSuccessful = true;
                trace.RawResponseText = response;
            }
            catch (Exception ex)
            {
                trace.WasSuccessful = false;
                trace.ErrorMessage = ex.Message;
                _llmTraceService.AddTrace(trace);
                throw;
            }
            finally
            {
                sw.Stop();
                trace.DurationMs = sw.ElapsedMilliseconds;
            }
            
            // 4. Parse
            var cleaned = CleanJsonResponse(response);

            // Diagnostics before deserialisation
            System.Diagnostics.Trace.WriteLine("[CodebaseTicketGeneratorService.GenerateTicketsAsync] LLM response received.");
            System.Diagnostics.Trace.WriteLine($"[CodebaseTicketGenerator] Response empty: {string.IsNullOrWhiteSpace(cleaned)}");
            var preview = cleaned.Length > 300 ? cleaned[..300] + "..." : cleaned;
            System.Diagnostics.Trace.WriteLine($"[CodebaseTicketGenerator] Raw preview: {preview}");

            GenerationWrapper? result;
            try
            {
                result = JsonSerializer.Deserialize<GenerationWrapper>(cleaned, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                trace.ParsedResponseSummary = $"Parsed {result?.Drafts?.Count ?? 0} drafts.";
                _llmTraceService.AddTrace(trace);
                return new CodebaseTicketGenerationResult
                {
                    Success      = true,
                    Drafts  = result?.Drafts ?? []
                };
            }
            catch (Exception jsonEx)
            {
                trace.ParsedResponseSummary = "JSON Parse Failure";
                trace.ErrorMessage = jsonEx.Message;
                _llmTraceService.AddTrace(trace);

                System.Diagnostics.Trace.WriteLine(
                    $"[CodebaseTicketGenerator] Deserialisation failed — target: GenerationWrapper — error: {jsonEx.Message}");
                return new CodebaseTicketGenerationResult
                {
                    Success      = false,
                    ErrorMessage = $"Draft generation failed: {jsonEx.Message}. Raw response preview: {preview}"
                };
            }
        }
        catch (Exception ex)
        {
            return new CodebaseTicketGenerationResult
            {
                Success      = false,
                ErrorMessage = $"Generation failed: {ex.Message}"
            };
        }
    }

    private static string CleanJsonResponse(string input)
    {
        var cleaned = input.Trim();
        if (cleaned.StartsWith("```json"))
            cleaned = cleaned.Substring(7);
        else if (cleaned.StartsWith("```"))
            cleaned = cleaned.Substring(3);

        if (cleaned.EndsWith("```"))
            cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();

        return cleaned;
    }

    private sealed class GenerationWrapper
    {
        public List<CodebaseTicketDraft> Drafts { get; set; } = [];
    }
}
