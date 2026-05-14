using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IronDev.AI;
using IronDev.Core;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// Context Agent v1: a single-round Agentic RAG loop that:
///   1. Builds initial context via PromptContextBuilder.
///   2. Asks the LLM whether context is sufficient (structured JSON response).
///   3. If not sufficient: runs up to MaxCodeSearchQueries via ICodeIndexService.
///   4. If clarification is needed: returns the questions without a final prompt.
///   5. Assembles and returns the final enriched prompt.
///
/// Every stage emits an LlmTraceEntry with a shared TraceGroupId so the
/// LLM Console can filter the entire agent run as one unit.
/// </summary>
public sealed class ContextAgentService : IContextAgentService
{
    // ── Injected dependencies ─────────────────────────────────────────────────

    private readonly IPromptContextBuilder _contextBuilder;
    private readonly ICodeIndexService     _codeIndexService;
    private readonly ILLMService           _llmService;
    private readonly ILlmTraceService      _traceService;

    // ── Default limits (applied when the request does not override them) ──────

    private static readonly ContextAgentLimits DefaultLimits = new();

    // ── Sufficiency check prompt ──────────────────────────────────────────────

    private const string SufficiencySystemPrompt = """
        You are a context quality evaluator for an AI coding assistant.
        Given the assembled project context and a user question, decide whether
        the context is sufficient to answer the question accurately.

        Respond ONLY with valid JSON, no markdown, no explanation outside the JSON:

        {
          "isSufficient": <true|false>,
          "confidence": <0-10>,
          "reason": "<one sentence>",
          "requestedContext": {
            "codeSearchQueries": ["<query1>", "<query2>"],
            "clarificationQuestions": []
          }
        }

        Rules:
        - codeSearchQueries: list symbol names, file names, or method names to look up.
          Leave empty if context is sufficient.
        - clarificationQuestions: only when the user question is genuinely ambiguous.
          Leave empty when a code search would resolve the ambiguity.
        - confidence 8–10 = sufficient. 5–7 = borderline. 0–4 = definitely insufficient.
        - isSufficient MUST be true when confidence >= 7 and codeSearchQueries is empty.
        """;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ContextAgentService(
        IPromptContextBuilder contextBuilder,
        ICodeIndexService     codeIndexService,
        ILLMService           llmService,
        ILlmTraceService      traceService)
    {
        _contextBuilder   = contextBuilder;
        _codeIndexService = codeIndexService;
        _llmService       = llmService;
        _traceService     = traceService;
    }

    // ── Main pipeline ─────────────────────────────────────────────────────────

    public async Task<ContextAgentResult> RunAsync(ContextAgentRequest request, CancellationToken ct = default)
    {
        var limits      = request.Limits ?? DefaultLimits;
        var traceGroupId = Guid.NewGuid().ToString("N");
        var warnings    = new List<string>();
        var evidence    = new List<CodeEvidence>();

        // ── Stage 1: Build initial context ────────────────────────────────────
        Guid? stage1Id = null;
        ChatContextPacket initialPacket;
        {
            var sw = Stopwatch.StartNew();
            try
            {
                initialPacket = await _contextBuilder.BuildPacketAsync(
                    request.ProjectId, request.SessionId, request.UserRequest, ct);
            }
            catch (Exception ex)
            {
                var failTrace = MakeTrace(ContextAgentStage.InitialContext, traceGroupId, null);
                failTrace.WasSuccessful = false;
                failTrace.ErrorMessage  = ex.Message;
                failTrace.DurationMs    = sw.ElapsedMilliseconds;
                failTrace.ProjectId     = request.ProjectId;
                _traceService.AddTrace(failTrace);

                return Fail(traceGroupId, $"Initial context build failed: {ex.Message}");
            }

            var t1 = MakeTrace(ContextAgentStage.InitialContext, traceGroupId, null);
            sw.Stop();
            t1.DurationMs       = sw.ElapsedMilliseconds;
            t1.WasSuccessful    = true;
            t1.ProjectId        = request.ProjectId;
            t1.ChatSessionId    = request.SessionId.ToString();
            t1.CurrentUserMessage = request.UserRequest;
            t1.ContextSummary   = SummarisePacket(initialPacket);
            t1.RequestText      = $"BuildPacketAsync(projectId={request.ProjectId}, sessionId={request.SessionId})";
            t1.ParsedResponseSummary = $"Intent={initialPacket.Intent} | Files={initialPacket.MatchedFilePaths.Count} | Memory={initialPacket.IncludedMemoryCount}";
            t1.Warnings         = initialPacket.RulesLoadWarning ?? string.Empty;
            stage1Id            = t1.Id;
            _traceService.AddTrace(t1);

            if (!string.IsNullOrWhiteSpace(initialPacket.RulesLoadWarning))
                warnings.Add(initialPacket.RulesLoadWarning);
        }

        // ── Stage 2: Sufficiency check ────────────────────────────────────────
        ContextSufficiencyResult sufficiency;
        Guid? stage2Id = null;
        {
            var checkPrompt = BuildSufficiencyPrompt(request.UserRequest, initialPacket);
            var sw = Stopwatch.StartNew();
            string rawJson;
            try
            {
                rawJson = await _llmService.GetResponseAsync(checkPrompt, ct);
            }
            catch (Exception ex)
            {
                var failTrace = MakeTrace(ContextAgentStage.SufficiencyCheck, traceGroupId, stage1Id);
                failTrace.WasSuccessful = false;
                failTrace.ErrorMessage  = ex.Message;
                failTrace.DurationMs    = sw.ElapsedMilliseconds;
                _traceService.AddTrace(failTrace);

                // Treat LLM failure as "sufficient enough to continue" so the caller
                // still gets a usable final prompt (the initial context).
                sufficiency = new ContextSufficiencyResult { IsSufficient = true, Confidence = 5,
                    Reason = $"Sufficiency check unavailable: {ex.Message}" };
                warnings.Add($"Sufficiency check LLM error: {ex.Message}");
                goto BuildFinalPrompt;
            }
            sw.Stop();

            sufficiency = ParseSufficiencyJson(rawJson);

            var t2 = MakeTrace(ContextAgentStage.SufficiencyCheck, traceGroupId, stage1Id);
            t2.DurationMs       = sw.ElapsedMilliseconds;
            t2.WasSuccessful    = !sufficiency.ParseError;
            t2.RequestText      = checkPrompt;
            t2.RawResponseText  = rawJson;
            t2.ParsedResponseSummary =
                $"Sufficient={sufficiency.IsSufficient} | Confidence={sufficiency.Confidence} | Queries={sufficiency.CodeSearchQueries.Count}";
            t2.ContextSummary   = $"Reason: {sufficiency.Reason}";
            t2.Warnings         = sufficiency.ParseError ? sufficiency.ParseErrorMessage : string.Empty;
            stage2Id            = t2.Id;
            _traceService.AddTrace(t2);

            if (sufficiency.ParseError)
                warnings.Add($"Sufficiency JSON parse error: {sufficiency.ParseErrorMessage}");
        }

        // ── Stage 3: Clarification required? ─────────────────────────────────
        if (sufficiency.ClarificationQuestions.Count > 0)
        {
            var t3 = MakeTrace(ContextAgentStage.ClarificationRequired, traceGroupId, stage2Id);
            t3.WasSuccessful    = true;
            t3.ParsedResponseSummary = $"Clarification needed: {sufficiency.ClarificationQuestions.Count} question(s)";
            t3.RawResponseText  = string.Join("\n", sufficiency.ClarificationQuestions);
            _traceService.AddTrace(t3);

            return new ContextAgentResult
            {
                TraceGroupId            = traceGroupId,
                IsClarificationRequired = true,
                ClarificationQuestions  = sufficiency.ClarificationQuestions,
                WasSuccessful           = true,
                ContextSummary          = SummarisePacket(initialPacket),
                Warnings                = string.Join("; ", warnings),
            };
        }

        // ── Stage 4: Context expansion (tool calls) ───────────────────────────
        if (!sufficiency.IsSufficient && sufficiency.CodeSearchQueries.Count > 0)
        {
            var queries = sufficiency.CodeSearchQueries
                .Take(limits.MaxCodeSearchQueries)
                .ToList();

            int toolCallCount = 0;

            foreach (var query in queries)
            {
                if (toolCallCount >= limits.MaxToolCallsPerRound) break;
                if (evidence.Select(e => e.FilePath).Distinct().Count() >= limits.MaxAddedFiles) break;
                if (evidence.Count >= limits.MaxSnippets) break;

                toolCallCount++;

                // Stage 4a: ToolCall trace
                var tCall = MakeTrace(ContextAgentStage.ToolCallSearch, traceGroupId, stage2Id);
                tCall.RequestText  = $"GetRelevantSnippetsAsync(projectId={request.ProjectId}, query=\"{query}\", take={limits.MaxSnippets})";
                tCall.WasSuccessful = true;

                var swCall = Stopwatch.StartNew();
                IReadOnlyList<IronDev.Data.Models.CodeIndexEntry> results;
                try
                {
                    results = await _codeIndexService.GetRelevantSnippetsAsync(
                        request.ProjectId, query, limits.MaxSnippets, ct);
                    tCall.WasSuccessful = true;
                }
                catch (Exception ex)
                {
                    tCall.WasSuccessful = false;
                    tCall.ErrorMessage  = ex.Message;
                    results             = Array.Empty<IronDev.Data.Models.CodeIndexEntry>();
                    warnings.Add($"Code search '{query}' failed: {ex.Message}");
                }
                swCall.Stop();
                tCall.DurationMs = swCall.ElapsedMilliseconds;
                _traceService.AddTrace(tCall);

                // Stage 4b: ToolResult trace
                var tResult = MakeTrace(ContextAgentStage.ToolResultSearch, traceGroupId, tCall.Id);
                tResult.WasSuccessful = true;
                tResult.DurationMs    = 0;

                var resultSb = new StringBuilder();
                int addedFromThisQuery = 0;
                foreach (var r in results)
                {
                    var distinctFilesNow = evidence.Select(e => e.FilePath).Distinct().Count();
                    if (distinctFilesNow >= limits.MaxAddedFiles) break;
                    if (evidence.Count >= limits.MaxSnippets) break;

                    var snippet = r.ChunkText.Length > 600
                        ? r.ChunkText[..600] + "\n...[TRUNCATED]..."
                        : r.ChunkText;

                    evidence.Add(new CodeEvidence
                    {
                        FilePath        = r.FilePath ?? "(unknown)",
                        SymbolName      = r.SymbolName ?? string.Empty,
                        Snippet         = snippet,
                        RetrievedByQuery = query,
                    });
                    addedFromThisQuery++;
                    resultSb.AppendLine($"  {r.FilePath} / {r.SymbolName}");
                }

                tResult.ParsedResponseSummary = $"Query=\"{query}\" → {results.Count} results, {addedFromThisQuery} added to context";
                tResult.RawResponseText       = resultSb.ToString();
                tResult.ContextSummary        = $"Evidence so far: {evidence.Count} snippet(s) from {evidence.Select(e => e.FilePath).Distinct().Count()} file(s)";
                _traceService.AddTrace(tResult);
            }
        }

        // ── Stage 5: Assemble final prompt ────────────────────────────────────
        BuildFinalPrompt:
        string finalPrompt;
        {
            finalPrompt = AssembleFinalPrompt(request, initialPacket, evidence, sufficiency, limits, warnings);

            var contextSummary = BuildFinalContextSummary(initialPacket, evidence, sufficiency);

            var tFinal = MakeTrace(ContextAgentStage.FinalAnswer, traceGroupId, stage2Id);
            tFinal.WasSuccessful      = true;
            tFinal.CurrentUserMessage = request.UserRequest;
            tFinal.RequestText        = finalPrompt;
            tFinal.ParsedResponseSummary =
                $"FinalPrompt assembled | Evidence={evidence.Count} | Expanded={evidence.Count > 0} | Chars={finalPrompt.Length}";
            tFinal.ContextSummary    = contextSummary;
            tFinal.Warnings          = string.Join("; ", warnings);
            _traceService.AddTrace(tFinal);

            return new ContextAgentResult
            {
                FinalPrompt         = finalPrompt,
                TraceGroupId        = traceGroupId,
                WasExpanded         = evidence.Count > 0,
                ExpandedFileCount   = evidence.Select(e => e.FilePath).Distinct().Count(),
                ExpandedSnippetCount = evidence.Count,
                WasSuccessful       = true,
                Evidence            = evidence,
                ContextSummary      = contextSummary,
                Warnings            = string.Join("; ", warnings),
            };
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static LlmTraceEntry MakeTrace(string featureName, string traceGroupId, Guid? parentId)
        => new()
        {
            FeatureName    = featureName,
            WorkspaceName  = "ContextAgent",
            TraceGroupId   = traceGroupId,
            ParentTraceId  = parentId,
            CreatedAt      = DateTime.UtcNow,
        };

    private static ContextAgentResult Fail(string traceGroupId, string reason)
        => new()
        {
            TraceGroupId  = traceGroupId,
            WasSuccessful = false,
            Warnings      = reason,
        };

    /// <summary>
    /// Builds the structured prompt sent to the LLM for the sufficiency check.
    /// Includes the system instructions, the assembled context, and the user question.
    /// </summary>
    private static string BuildSufficiencyPrompt(string userRequest, ChatContextPacket packet)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SufficiencySystemPrompt);
        sb.AppendLine();
        sb.AppendLine("=== ASSEMBLED CONTEXT (excerpt) ===");
        // Include just enough for the evaluator — truncate aggressively
        var promptExcerpt = packet.FormattedPrompt.Length > 4000
            ? packet.FormattedPrompt[..4000] + "\n...[TRUNCATED]..."
            : packet.FormattedPrompt;
        sb.AppendLine(promptExcerpt);
        sb.AppendLine();
        sb.AppendLine("=== USER QUESTION ===");
        sb.AppendLine(userRequest);
        return sb.ToString();
    }

    /// <summary>
    /// Assembles the final enriched prompt by combining the initial context
    /// with all expanded code evidence.
    /// </summary>
    private static string AssembleFinalPrompt(
        ContextAgentRequest       request,
        ChatContextPacket          initialPacket,
        IReadOnlyList<CodeEvidence> evidence,
        ContextSufficiencyResult   sufficiency,
        ContextAgentLimits         limits,
        IList<string>              warnings)
    {
        var sb = new StringBuilder();

        // Start with the initial PromptContextBuilder prompt as the foundation
        sb.Append(initialPacket.FormattedPrompt);

        // Inject evidence rule guard
        sb.AppendLine();
        sb.AppendLine("=== CODE EVIDENCE RULE ===");
        if (evidence.Count > 0)
        {
            sb.AppendLine($"The following code was retrieved and inspected ({evidence.Count} snippet(s) from {evidence.Select(e => e.FilePath).Distinct().Count()} file(s)):");
            sb.AppendLine("You MAY reference this code as evidence in your answer.");
            sb.AppendLine();

            // Append expanded code evidence
            sb.AppendLine("=== EXPANDED CODE EVIDENCE (Context Agent) ===");
            foreach (var ev in evidence)
            {
                sb.AppendLine($"File:   {ev.FilePath}");
                if (!string.IsNullOrWhiteSpace(ev.SymbolName))
                    sb.AppendLine($"Symbol: {ev.SymbolName}");
                sb.AppendLine($"Query:  {ev.RetrievedByQuery}");
                sb.AppendLine("```");
                sb.AppendLine(ev.Snippet);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("No additional code was retrieved during this request.");
            sb.AppendLine("If your answer requires inspecting specific code that is not in the context above, you MUST say:");
            sb.AppendLine("\"I do not have enough indexed code context to verify that.\"");
            sb.AppendLine("Do NOT invent code details not present in the retrieved snippets.");
        }

        // Context quality footer
        sb.AppendLine();
        sb.AppendLine("=== CONTEXT QUALITY ===");
        sb.AppendLine($"Sufficiency check confidence: {sufficiency.Confidence}/10");
        sb.AppendLine($"Reason: {sufficiency.Reason}");
        if (warnings.Count > 0)
            sb.AppendLine($"Warnings: {string.Join("; ", warnings)}");

        // Enforce hard character budget (truncate the initial prompt section only)
        var result = sb.ToString();
        if (result.Length > limits.MaxContextChars)
        {
            result = result[..limits.MaxContextChars] + "\n...[CONTEXT TRUNCATED — budget exceeded]...";
        }

        return result;
    }

    /// <summary>
    /// Parses the LLM JSON response for the sufficiency check.
    /// Gracefully handles malformed JSON by returning a ParseError result.
    /// </summary>
    public static ContextSufficiencyResult ParseSufficiencyJson(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return new ContextSufficiencyResult { ParseError = true, ParseErrorMessage = "Empty response" };

        // Strip markdown code fences if the LLM added them
        var cleaned = rawJson.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            var lastFence    = cleaned.LastIndexOf("```");
            if (firstNewline > 0 && lastFence > firstNewline)
                cleaned = cleaned[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            bool isSuff    = root.TryGetProperty("isSufficient", out var suffEl) && suffEl.GetBoolean();
            int  confidence= root.TryGetProperty("confidence", out var confEl) ? confEl.GetInt32() : 5;
            string reason  = root.TryGetProperty("reason", out var rEl) ? rEl.GetString() ?? string.Empty : string.Empty;

            var queries     = new List<string>();
            var questions   = new List<string>();

            if (root.TryGetProperty("requestedContext", out var rc))
            {
                if (rc.TryGetProperty("codeSearchQueries", out var cq))
                    foreach (var q in cq.EnumerateArray())
                    {
                        var s = q.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) queries.Add(s);
                    }

                if (rc.TryGetProperty("clarificationQuestions", out var clq))
                    foreach (var q in clq.EnumerateArray())
                    {
                        var s = q.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) questions.Add(s);
                    }
            }

            return new ContextSufficiencyResult
            {
                IsSufficient           = isSuff,
                Confidence             = Math.Clamp(confidence, 0, 10),
                Reason                 = reason,
                CodeSearchQueries      = queries,
                ClarificationQuestions = questions,
            };
        }
        catch (JsonException ex)
        {
            return new ContextSufficiencyResult
            {
                ParseError        = true,
                ParseErrorMessage = $"JSON parse error: {ex.Message}",
                // Fail-safe: treat as sufficient so caller still gets a prompt
                IsSufficient = true,
                Confidence   = 4,
                Reason       = "Sufficiency check could not be parsed.",
            };
        }
    }

    private static string SummarisePacket(ChatContextPacket packet)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Intent:           {packet.Intent}");
        sb.AppendLine($"Project indexed:  {(packet.IsProjectNotIndexed ? "No" : "Yes")}");
        sb.AppendLine($"Retrieved files:  {packet.MatchedFilePaths.Count}");
        sb.AppendLine($"Memory included:  {packet.IncludedMemoryCount}");
        sb.AppendLine($"Memory filtered:  {packet.FilteredMemoryCount}");
        sb.AppendLine($"Standards:        {packet.IncludedStandardsCount} included, {packet.FilteredStandardsCount} filtered");
        if (!string.IsNullOrWhiteSpace(packet.RulesLoadWarning))
            sb.AppendLine($"Warning:          {packet.RulesLoadWarning}");
        return sb.ToString().TrimEnd();
    }

    private static string BuildFinalContextSummary(
        ChatContextPacket packet,
        IReadOnlyList<CodeEvidence> evidence,
        ContextSufficiencyResult sufficiency)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SummarisePacket(packet));
        sb.AppendLine($"Sufficiency:      {sufficiency.Confidence}/10 — {sufficiency.Reason}");
        if (evidence.Count > 0)
        {
            sb.AppendLine($"Expanded files:   {evidence.Select(e => e.FilePath).Distinct().Count()}");
            sb.AppendLine($"Expanded snippets:{evidence.Count}");
            foreach (var f in evidence.Select(e => e.FilePath).Distinct().Take(5))
                sb.AppendLine($"  - {Path.GetFileName(f)}");
        }
        return sb.ToString().TrimEnd();
    }
}
