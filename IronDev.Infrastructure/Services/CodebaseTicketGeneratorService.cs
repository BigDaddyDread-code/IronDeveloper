using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly ICodexSnapshotBuilder    _snapshotBuilder;
    private readonly ICodexTicketGroundingValidator _groundingValidator;

    public CodebaseTicketGeneratorService(
        ILLMService           llmService,
        IProjectMemoryService memoryService,
        ILlmTraceService      llmTraceService,
        ICodexSnapshotBuilder snapshotBuilder,
        ICodexTicketGroundingValidator groundingValidator)
    {
        _llmService    = llmService;
        _memoryService = memoryService;
        _llmTraceService = llmTraceService;
        _snapshotBuilder = snapshotBuilder;
        _groundingValidator = groundingValidator;
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
            var snapshot  = await _snapshotBuilder.BuildSnapshotAsync(
                new CodexSnapshotBuildRequest
                {
                    ProjectId = projectId,
                    MaxFiles = 120,
                    MaxSymbols = 240
                },
                ct);

            var contextBuilder = new System.Text.StringBuilder();
            contextBuilder.AppendLine("PROJECT CONTEXT:");
            contextBuilder.AppendLine($"Project: {snapshot.ProjectName}");
            contextBuilder.AppendLine($"Solution: {snapshot.SolutionPath}");
            contextBuilder.AppendLine($"Context quality: {snapshot.ContextQualityScore}/100");
            if (snapshot.MissingContextReasons.Count > 0)
            {
                contextBuilder.AppendLine("Missing or weak context:");
                foreach (var reason in snapshot.MissingContextReasons.Take(8))
                    contextBuilder.AppendLine($"- {reason}");
            }

            contextBuilder.AppendLine("\nLANGUAGE QUALITY:");
            foreach (var quality in snapshot.LanguageQuality)
            {
                contextBuilder.AppendLine(
                    $"- {quality.LanguageId}: {quality.Confidence}, files={quality.FileCount}, symbols={quality.SymbolCount}. {quality.Notes}");
            }

            contextBuilder.AppendLine("\nFILES:");
            foreach (var file in snapshot.Files.Take(80))
            {
                contextBuilder.AppendLine(
                    $"- {file.FilePath} ({file.LanguageId}, symbols={file.SymbolCount}, confidence={file.Confidence})");
            }

            contextBuilder.AppendLine("\nSYMBOLS:");
            foreach (var symbol in snapshot.Symbols.Take(140))
            {
                var location = symbol.StartLine is null ? symbol.FilePath : $"{symbol.FilePath}:{symbol.StartLine}";
                var container = string.IsNullOrWhiteSpace(symbol.ContainerName) ? string.Empty : $"{symbol.ContainerName}.";
                var qualified = string.IsNullOrWhiteSpace(symbol.FullyQualifiedName) ? $"{container}{symbol.Name}" : symbol.FullyQualifiedName;
                contextBuilder.AppendLine(
                    $"- {symbol.LanguageId} {symbol.Kind} {qualified} @ {location}");
            }

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

            if (snapshot.ExistingTickets.Count > 0)
            {
                contextBuilder.AppendLine("\nEXISTING TICKETS:");
                foreach (var ticket in snapshot.ExistingTickets.Take(20))
                {
                    contextBuilder.AppendLine(
                        $"- [{ticket.Status}/{ticket.Priority}] {ticket.Title}: {ticket.SummaryPreview}");
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
You are IronDev's self-dogfood planner analyzing IronDev's own codebase and history.
Based only on the provided project snapshot and project memory, identify 5-8 technical improvements,
refactoring tasks, testing gaps, UX issues, or dogfood-loop improvements that would benefit the project.

For each item, output a structured ticket draft.
Return ONLY valid JSON matching this schema:
{{
  ""drafts"": [
    {{
      ""title"": ""string"",
      ""category"": ""UX|TechDebt|Architecture|Testing|Dogfood|Performance"",
      ""summary"": ""string"",
      ""problem"": ""string"",
      ""proposedChange"": ""string"",
      ""whyNow"": ""string"",
      ""background"": ""string"",
      ""acceptanceCriteria"": ""string"",
      ""priority"": ""Low|Medium|High|Critical"",
      ""ticketType"": ""Task|Bug|Feature|Spike|Chore"",
      ""affectedFiles"": [""actual/path/from/snapshot.cs""],
      ""affectedSymbols"": [""ActualSymbolFromSnapshot""],
      ""dependencies"": [""title of prior ticket if any""],
      ""suggestedBuildOrder"": 1,
      ""riskLevel"": ""Low|Medium|High"",
      ""confidenceScore"": 0,
      ""groundingWarnings"": [],
      ""testSuggestions"": [""specific test or build validation""],
      ""unitTests"": ""string"",
      ""integrationTests"": ""string"",
      ""manualTests"": ""string"",
      ""regressionTests"": ""string"",
      ""buildValidation"": ""dotnet build""
    }}
  ]
}}

Rules:
- Do not invent files. Use files from the FILES section.
- Do not invent symbols. Prefer symbols from the SYMBOLS section; omit symbols if unsure.
- Leave groundingWarnings empty. IronDev will validate grounding after generation.
- Rank tickets by suggestedBuildOrder.
- Prefer Alpha 0.1-sized improvements over giant rewrites.
- If context quality is weak, lower confidenceScore and explain the risk in background.
- Avoid duplicating existing tickets.

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
                ContextSummary =
                    $"Codex snapshot: quality={snapshot.ContextQualityScore}/100, " +
                    $"files={snapshot.Files.Count}, symbols={snapshot.Symbols.Count}, " +
                    $"tickets={snapshot.ExistingTickets.Count}, decisions={snapshot.Decisions.Count}",
                ContextQualityScore = snapshot.ContextQualityScore,
                SemanticSymbolCount = snapshot.Symbols.Count,
                SymbolsIncludedInPrompt = snapshot.Symbols.Take(140).Count(),
                MissingContextReasons = snapshot.MissingContextReasons.Take(20).ToList(),
                IndexWarnings = snapshot.SemanticWarnings.Take(20).ToList(),
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

                var rawDrafts = (result?.Drafts ?? [])
                    .OrderBy(d => d.SuggestedBuildOrder <= 0 ? int.MaxValue : d.SuggestedBuildOrder)
                    .ToList();
                var drafts = _groundingValidator.ValidateAndScore(rawDrafts, snapshot).ToList();

                var groundingWarningCount = drafts.Sum(d => d.GroundingWarnings.Count);
                trace.SymbolsReferencedByGeneratedTickets = drafts
                    .SelectMany(d => d.AffectedSymbols)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(80)
                    .ToList();
                trace.SymbolsReferenced = trace.SymbolsReferencedByGeneratedTickets;
                trace.FilesReferencedByGeneratedTickets = drafts
                    .SelectMany(d => d.AffectedFiles)
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(80)
                    .ToList();
                trace.ParsedResponseSummary = $"Parsed {drafts.Count} drafts. Grounding warnings: {groundingWarningCount}.";
                if (groundingWarningCount > 0)
                    trace.Warnings = $"Grounding warnings: {groundingWarningCount}";
                _llmTraceService.AddTrace(trace);
                return new CodebaseTicketGenerationResult
                {
                    Success      = true,
                    Drafts  = drafts,
                    ContextQualityScore = snapshot.ContextQualityScore,
                    MissingContextReasons = snapshot.MissingContextReasons.ToList()
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
