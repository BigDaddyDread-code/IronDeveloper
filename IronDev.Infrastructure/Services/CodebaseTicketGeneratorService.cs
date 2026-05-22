using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly ILLMService                    _llmService;
    private readonly IProjectMemoryService          _memoryService;
    private readonly ILlmTraceService               _llmTraceService;
    private readonly ICodexSnapshotBuilder          _snapshotBuilder;
    private readonly ICodexTicketGroundingValidator _groundingValidator;
    private readonly ICodebaseTicketPromptBuilder   _promptBuilder;
    private readonly ICodebaseTicketResponseParser  _responseParser;

    public CodebaseTicketGeneratorService(
        ILLMService                    llmService,
        IProjectMemoryService          memoryService,
        ILlmTraceService               llmTraceService,
        ICodexSnapshotBuilder          snapshotBuilder,
        ICodexTicketGroundingValidator groundingValidator,
        ICodebaseTicketPromptBuilder   promptBuilder,
        ICodebaseTicketResponseParser  responseParser)
    {
        _llmService         = llmService;
        _memoryService      = memoryService;
        _llmTraceService    = llmTraceService;
        _snapshotBuilder    = snapshotBuilder;
        _groundingValidator = groundingValidator;
        _promptBuilder      = promptBuilder;
        _responseParser     = responseParser;
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
                    ProjectId  = projectId,
                    MaxFiles   = 120,
                    MaxSymbols = 240
                },
                ct);

            // 2. Build prompt
            var prompt = _promptBuilder.Build(new CodebaseTicketPromptInputs
            {
                Snapshot        = snapshot,
                ProjectSummary  = summary?.Summary,
                RecentDecisions = decisions
                    .Select(d => $"{d.Title}: {d.Detail}")
                    .ToList(),
                ProjectRules    = rules
                    .Where(r => r.AppliesTo == "Both" || r.AppliesTo == "Ticket")
                    .Select(r =>
                    {
                        var hint = string.IsNullOrWhiteSpace(r.ValidationHint)
                            ? string.Empty
                            : $"  Validation Hint: {r.ValidationHint}";
                        return $"[{r.EnforcementLevel}] {r.Name}: {r.Description}{hint}";
                    })
                    .ToList()
            });

            // 3. Call LLM with tracing
            var trace = new LlmTraceEntry
            {
                FeatureName    = "CodebaseAnalysis",
                WorkspaceName  = "Architect",
                ProjectId      = projectId,
                RequestText    = prompt,
                ContextSummary =
                    $"Codex snapshot: quality={snapshot.ContextQualityScore}/100, " +
                    $"files={snapshot.Files.Count}, symbols={snapshot.Symbols.Count}, " +
                    $"tickets={snapshot.ExistingTickets.Count}, decisions={snapshot.Decisions.Count}",
                ContextQualityScore      = snapshot.ContextQualityScore,
                SemanticSymbolCount      = snapshot.Symbols.Count,
                SymbolsIncludedInPrompt  = snapshot.Symbols.Take(140).Count(),
                MissingContextReasons    = snapshot.MissingContextReasons.Take(20).ToList(),
                IndexWarnings            = snapshot.SemanticWarnings.Take(20).ToList(),
                CreatedAt                = DateTime.UtcNow
            };

            var sw = System.Diagnostics.Stopwatch.StartNew();
            string response;
            try
            {
                response = await _llmService.GetResponseAsync(prompt, ct);
                trace.WasSuccessful    = true;
                trace.RawResponseText  = response;
            }
            catch (Exception ex)
            {
                trace.WasSuccessful  = false;
                trace.ErrorMessage   = ex.Message;
                _llmTraceService.AddTrace(trace);
                throw;
            }
            finally
            {
                sw.Stop();
                trace.DurationMs = sw.ElapsedMilliseconds;
            }

            // 4. Parse & validate
            System.Diagnostics.Trace.WriteLine(
                "[CodebaseTicketGeneratorService] LLM response received.");

            IReadOnlyList<CodebaseTicketDraft> rawDrafts;
            try
            {
                rawDrafts = _responseParser.Parse(response);
            }
            catch (Exception jsonEx)
            {
                trace.ParsedResponseSummary = "JSON Parse Failure";
                trace.ErrorMessage          = jsonEx.Message;
                _llmTraceService.AddTrace(trace);

                System.Diagnostics.Trace.WriteLine(
                    $"[CodebaseTicketGenerator] Deserialisation failed: {jsonEx.Message}");

                var preview = response.Length > 300 ? response[..300] + "..." : response;
                return new CodebaseTicketGenerationResult
                {
                    Success      = false,
                    ErrorMessage = $"Draft generation failed: {jsonEx.Message}. Raw response preview: {preview}"
                };
            }

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
            trace.ParsedResponseSummary =
                $"Parsed {drafts.Count} drafts. Grounding warnings: {groundingWarningCount}.";
            if (groundingWarningCount > 0)
                trace.Warnings = $"Grounding warnings: {groundingWarningCount}";
            _llmTraceService.AddTrace(trace);

            return new CodebaseTicketGenerationResult
            {
                Success               = true,
                Drafts                = drafts,
                ContextQualityScore   = snapshot.ContextQualityScore,
                MissingContextReasons = snapshot.MissingContextReasons.ToList(),
                FileCount             = snapshot.Files.Count,
                SemanticSymbolCount   = snapshot.Symbols.Count,
                IndexWarningCount     = snapshot.SemanticWarnings.Count,
                IndexWarnings         = snapshot.SemanticWarnings.ToList()
            };
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
}
