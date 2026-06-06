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
///   2. Detects vague/ambiguous requests and returns clarification proactively.
///   3. Asks the LLM whether context is sufficient (structured JSON response).
///   4. If not sufficient: expands queries via RetrievalQualityHelpers, then
///      runs code-index searches with production-first ranking and test-file exclusion.
///   5. If clarification is needed (LLM-detected): returns questions without a prompt.
///   6. Assembles and returns the final enriched prompt with rich evidence.
///
/// Every stage emits an LlmTraceEntry with a shared TraceGroupId so the
/// LLM Console can filter the entire agent run as one unit.
/// </summary>
public sealed class ContextAgentService : IContextAgentService
{
    // ── Injected dependencies ─────────────────────────────────────────────────

    private readonly IPromptContextBuilder    _contextBuilder;
    private readonly ICodeIndexService        _codeIndexService;
    private readonly ILLMService              _llmService;
    private readonly ILlmTraceService         _traceService;
    private readonly IContextConflictService? _conflictService;
    private readonly IDeepCodeLookupService   _deepLookupService;
    private readonly IContextAgentRouteJudge  _routeJudge;

    // ── Default limits ────────────────────────────────────────────────────────

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
            "codeSearchQueries": ["<symbol or method name>", "<another>"],
            "clarificationQuestions": []
          }
        }

        Rules:
        - codeSearchQueries: list CONCRETE C# symbol names, method names, or class names.
          Do NOT list broad UI labels like "LLM Console" — use "LlmConsoleViewModel" instead.
          Do NOT list vague terms like "soft archive" — use "ArchiveTicketAsync" instead.
          Leave empty if context is sufficient.
        - clarificationQuestions: ONLY when the user question is genuinely ambiguous AND
          a code search cannot resolve the ambiguity. Leave empty otherwise.
        - confidence 8–10 = sufficient. 5–7 = borderline. 0–4 = definitely insufficient.
        - isSufficient MUST be true when confidence >= 7 and codeSearchQueries is empty.
        - If the request is a vague create/fix without a clear domain, set clarificationQuestions.
        """;

    // ── Constructor ───────────────────────────────────────────────────────────

    public ContextAgentService(
        IPromptContextBuilder    contextBuilder,
        ICodeIndexService        codeIndexService,
        ILLMService              llmService,
        ILlmTraceService         traceService,
        IContextConflictService? conflictService = null,
        IDeepCodeLookupService?  deepLookupService = null,
        IContextAgentRouteJudge? routeJudge = null)
    {
        _contextBuilder    = contextBuilder;
        _codeIndexService  = codeIndexService;
        _llmService        = llmService;
        _traceService      = traceService;
        _conflictService   = conflictService;
        // In some tests it might be null, but we prefer injecting a real one
        _deepLookupService = deepLookupService ?? new DeepCodeLookupService(codeIndexService);
        _routeJudge        = routeJudge ?? new ContextAgentRouteJudgeService(llmService, traceService);
    }

    // ── Main pipeline ─────────────────────────────────────────────────────────

    public async Task<ContextAgentResult> RunAsync(ContextAgentRequest request, CancellationToken ct = default)
    {
        var limits       = request.Limits ?? DefaultLimits;
        var traceGroupId = string.IsNullOrWhiteSpace(request.TraceGroupId)
            ? Guid.NewGuid().ToString("N")
            : request.TraceGroupId;
        var warnings     = new List<string>();
        var evidence     = new List<CodeEvidence>();
        var candidates   = new List<TicketCandidate>();
        var proofResult  = new EvidenceProofResult { Status = ContextProofStatus.NotProven }; 

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
            t1.DurationMs            = sw.ElapsedMilliseconds;
            t1.WasSuccessful         = true;
            t1.ProjectId             = request.ProjectId;
            t1.ChatSessionId         = request.SessionId.ToString();
            t1.CurrentUserMessage    = request.UserRequest;
            t1.ContextSummary        = SummarisePacket(initialPacket);
            t1.RequestText           = $"BuildPacketAsync(projectId={request.ProjectId}, sessionId={request.SessionId})";
            t1.ParsedResponseSummary = $"Intent={initialPacket.Intent} | Files={initialPacket.MatchedFilePaths.Count} | Memory={initialPacket.IncludedMemoryCount}";
            t1.Warnings              = initialPacket.RulesLoadWarning ?? string.Empty;
            stage1Id                 = t1.Id;
            _traceService.AddTrace(t1);

            if (!string.IsNullOrWhiteSpace(initialPacket.RulesLoadWarning))
                warnings.Add(initialPacket.RulesLoadWarning);
        }

        // ── Stage 1.5: Executive Route Decision ──────────────────────────────
        var routeRequest = new ContextAgentRouteRequest
        {
            TraceGroupId = traceGroupId,
            ProjectId = request.ProjectId,
            SessionId = request.SessionId,
            UserRequest = request.UserRequest,
            RecentConversationSummary = request.RecentConversationSummary,
            ConversationContextSnapshot = ConversationContextResolver.TryParseSnapshot(request.RecentConversationSummary),
            InitialIntentFromPromptContextBuilder = initialPacket.Intent.ToString(),
            RecentTickets = request.RecentTickets,
            RecentDecisions = request.RecentDecisions,
            ProjectRules = request.ProjectRules,
            RetrievedFilePaths = initialPacket.MatchedFilePaths,
            RetrievedSymbols = new List<string>(), // We don't have symbols from initial context currently
            SelectedTicketTitle = string.Empty,
            SelectedPlanTitle = string.Empty
        };

        var route = await _routeJudge.DecideRouteAsync(routeRequest, ct);
        var effectiveWorkText = route.EffectiveWorkText;

        if (request.CreateTicketIntent != null)
        {
            if (request.CreateTicketIntent.RequiresClarification)
            {
            return new ContextAgentResult
            {
                TraceGroupId = traceGroupId,
                ResultType = ContextAgentResultType.Clarification,
                IsClarificationRequired = true,
                ClarificationQuestions = request.CreateTicketIntent.ClarificationQuestions,
                Proposal = new AgentProposal
                {
                    Intent = request.CreateTicketIntent.Intent,
                    Message = "Create-ticket command needs clarification before drafting work.",
                    RecommendedNextActions =
                    [
                        "Ask the user to restate the feature request in one sentence.",
                        "Collect a concrete acceptance target."
                    ],
                    EvidenceSourceIds = Array.Empty<string>(),
                    RequiresApproval = true
                },
                AllowsProseResponse = false,
                WasSuccessful = true,
                ContextSummary = "Clarification required before ticket draft action can run.",
                Warnings = string.Join("; ", warnings),
            };
        }

            if (!string.IsNullOrWhiteSpace(request.CreateTicketIntent.WorkText)
                && request.RecentTickets.Count == 0)
            {
            return new ContextAgentResult
            {
                TraceGroupId = traceGroupId,
                ResultType = ContextAgentResultType.ActionRequired,
                Proposal = new AgentProposal
                {
                    Intent = request.CreateTicketIntent.Intent,
                    Message = "Create draft ticket workflow should handle this command.",
                    RecommendedNextActions =
                    [
                        "Open draft-ticket UI.",
                        "Render user work as candidate draft payload."
                    ],
                    EvidenceSourceIds = Array.Empty<string>(),
                    RequiresApproval = true
                },
                AllowsProseResponse = false,
                WasSuccessful = true,
                ContextSummary = $"Action routed: {request.CreateTicketIntent.Intent}",
                Warnings = string.Join("; ", warnings),
            };
        }

            return new ContextAgentResult
            {
                TraceGroupId = traceGroupId,
                ResultType = ContextAgentResultType.ActionBlocked,
                Proposal = new AgentProposal
                {
                    Intent = request.CreateTicketIntent.Intent,
                    Message = "I found a ticket-creation command, but no source work was available to turn into draft tickets.",
                    RecommendedNextActions =
                    [
                        "Select or generate a candidate ticket list first.",
                        "Use 'ticket this' after an assistant response.",
                        "Write the work directly after the command."
                    ],
                    EvidenceSourceIds = Array.Empty<string>(),
                    RequiresApproval = true
                },
                AllowsProseResponse = false,
                WasSuccessful = false,
                ContextSummary = "Action blocked: missing source work for ticket draft action.",
                Warnings = string.Join("; ", warnings),
            };
        }

        // ── Stage 0: Pre-check — clarification-first for vague requests ───────
        // Done before code search if it's obvious from user request (vague 'create ticket')
        var (shouldClarify, matched) = route.AllowConflictBlocking 
            ? RetrievalQualityHelpers.ShouldPreferClarification(effectiveWorkText)
            : (false, string.Empty);
        if (shouldClarify)
        {
            var questions = RetrievalQualityHelpers.GetDeleteClarificationQuestions();

            var t0 = MakeTrace(ContextAgentStage.ClarificationRequired, traceGroupId, null);
            t0.WasSuccessful         = true;
            t0.CurrentUserMessage    = request.UserRequest;
            t0.RequestText           = 
                $"UserRequest: {request.UserRequest}\n" +
                $"Matched Pattern: {matched}\n" +
                $"Reason: Request matched vague create/fix pattern — clarification preferred over code search.\n" +
                $"Candidate Domains: Chat, Tickets (Soft Archive), Tickets (Hard Delete), Implementation Plans";
            t0.ParsedResponseSummary = $"Pre-check clarification: {questions.Count} question(s) (vague intent detected)";
            t0.RawResponseText       = string.Join("\n", questions);
            t0.ContextSummary        = "Request matched vague create/fix pattern — clarification preferred over code search.";
            _traceService.AddTrace(t0);

            return new ContextAgentResult
            {
                TraceGroupId            = traceGroupId,
                IsClarificationRequired = true,
                ClarificationQuestions  = questions,
                WasSuccessful           = true,
                ContextSummary          = "Clarification required: vague intent detected before LLM call.",
                Warnings                = string.Join("; ", warnings),
            };
        }

        // ── Stage 2: Sufficiency check ────────────────────────────────────────
        ContextSufficiencyResult sufficiency;
        Guid? stage2Id = null;
        // Hoisted here so the goto BuildFinalPrompt path in Stage 2 cannot bypass it.
        TicketConflictAssessment? conflictAssessment = null;
        {
            var checkPrompt = BuildSufficiencyPrompt(effectiveWorkText, initialPacket, route);
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

                sufficiency = new ContextSufficiencyResult { IsSufficient = true, Confidence = 5,
                    Reason = $"Sufficiency check unavailable: {ex.Message}" };
                warnings.Add($"Sufficiency check LLM error: {ex.Message}");
                goto RunFinalGating;
            }
            sw.Stop();

            sufficiency = ParseSufficiencyJson(rawJson);

            var t2 = MakeTrace(ContextAgentStage.SufficiencyCheck, traceGroupId, stage1Id);
            t2.DurationMs            = sw.ElapsedMilliseconds;
            t2.WasSuccessful         = !sufficiency.ParseError;
            t2.RequestText           = checkPrompt;
            t2.RawResponseText       = rawJson;
            t2.ParsedResponseSummary =
                $"Sufficient={sufficiency.IsSufficient} | Confidence={sufficiency.Confidence} | Queries={sufficiency.CodeSearchQueries.Count} | Questions={sufficiency.ClarificationQuestions.Count}";
            t2.ContextSummary        = $"Reason: {sufficiency.Reason}";
            t2.Warnings              = sufficiency.ParseError ? sufficiency.ParseErrorMessage : string.Empty;
            stage2Id                 = t2.Id;
            _traceService.AddTrace(t2);

            if (sufficiency.ParseError)
                warnings.Add($"Sufficiency JSON parse error: {sufficiency.ParseErrorMessage}");
        }

        // ── Stage 3: Clarification required (LLM-detected)? ──────────────────
        if (sufficiency.ClarificationQuestions.Count > 0)
        {
            var t3 = MakeTrace(ContextAgentStage.ClarificationRequired, traceGroupId, stage2Id);
            t3.WasSuccessful         = true;
            t3.RequestText           = 
                $"UserRequest: {request.UserRequest}\n" +
                $"Reason: LLM detected insufficient context and requested clarification.\n" +
                $"Confidence: {sufficiency.Confidence}\n" +
                $"LLM Reason: {sufficiency.Reason}";
            t3.ParsedResponseSummary = $"Clarification needed: {sufficiency.ClarificationQuestions.Count} question(s) (LLM-detected)";
            t3.RawResponseText       = string.Join("\n", sufficiency.ClarificationQuestions);
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

        // ── Stage 3.5: Conflict assessment ─────────────────────────────────────
        // Runs when the request carries recent tickets (for example from a shell chat surface
        // determined this looks like a ticket-creation request) AND a conflict
        // service is wired.  Detection is deterministic — no extra LLM call.
        if (_conflictService != null &&
            (request.RecentTickets.Count > 0 ||
             request.RecentDecisions.Count > 0 ||
             request.ProjectRules.Count > 0))
        {
            if (!route.AllowConflictAssessment)
            {
                var tSkip = MakeTrace(ContextAgentStage.ConflictAssessment, traceGroupId, stage2Id);
                tSkip.WasSuccessful = true;
                tSkip.RequestText = $"UserRequest: {request.UserRequest}\nSkipped: RouteDecision disabled conflict assessment.";
                tSkip.ParsedResponseSummary = $"ConflictAssessment skipped: Route is {route.RequestKind}.";
                tSkip.ContextSummary = "Conflict assessment is gated to ticket creation and change intents only.";
                _traceService.AddTrace(tSkip);
            }
            else
            {
            var conflictCtx = new ConflictAssessmentContext
            {
                UserRequest      = effectiveWorkText,
                RecentTickets    = request.RecentTickets,
                RecentDecisions  = request.RecentDecisions,
                ProjectRules     = request.ProjectRules,
            };

            var swConflict = Stopwatch.StartNew();
            try
            {
                conflictAssessment = await _conflictService.AssessAsync(conflictCtx, ct);
            }
            catch (Exception ex)
            {
                warnings.Add($"Conflict assessment error: {ex.Message}");
            }
            swConflict.Stop();

            if (conflictAssessment != null)
            {
                var tConflict = MakeTrace(ContextAgentStage.ConflictAssessment, traceGroupId, stage2Id);
                tConflict.WasSuccessful      = true;
                tConflict.DurationMs         = swConflict.ElapsedMilliseconds;
                tConflict.CurrentUserMessage = request.UserRequest;
                
                var sbReq = new StringBuilder();
                sbReq.AppendLine($"UserRequest:      {request.UserRequest}");
                sbReq.AppendLine($"RecentTickets:    {request.RecentTickets.Count} considered");
                foreach (var t in request.RecentTickets) sbReq.AppendLine($"  - [{t.Id}] {t.Title}");
                sbReq.AppendLine($"RecentDecisions:  {request.RecentDecisions.Count} considered");
                foreach (var d in request.RecentDecisions) sbReq.AppendLine($"  - {d.Title}");
                sbReq.AppendLine($"ProjectRules:     {request.ProjectRules.Count} considered");
                foreach (var r in request.ProjectRules) sbReq.AppendLine($"  - {r.Name}");
                sbReq.AppendLine("Decision Rules:   Deterministic title/summary overlap + domain matching");
                
                tConflict.RequestText        = sbReq.ToString().TrimEnd();
                tConflict.ParsedResponseSummary =
                    $"Classification={conflictAssessment.Classification} | " +
                    $"Related={conflictAssessment.RelatedTickets.Count} | " +
                    $"Conflicts={conflictAssessment.ConflictingDecisions.Count} | " +
                    $"Blocks={conflictAssessment.BlocksTicketCreation} | " +
                    $"Domain={conflictAssessment.Domain}";
                tConflict.RawResponseText    = conflictAssessment.ToTraceText();
                tConflict.ContextSummary     =
                    $"Action={conflictAssessment.RecommendedAction} | " +
                    $"ExistingApproach={conflictAssessment.ExistingApproach} | " +
                    $"RequestedApproach={conflictAssessment.RequestedApproach}";
                _traceService.AddTrace(tConflict);

                // Block silent creation when the conflict is strong
                if (conflictAssessment.BlocksTicketCreation && route.AllowConflictBlocking)
                {
                    var blockQuestions = conflictAssessment.Questions.Count > 0
                        ? conflictAssessment.Questions
                        : new[] { $"A conflict was detected ({conflictAssessment.Classification}). Please clarify before creating this ticket." };

                    var tBlock = MakeTrace(ContextAgentStage.ClarificationRequired, traceGroupId, stage2Id);
                    tBlock.WasSuccessful         = true;
                    tBlock.RequestText           = 
                        $"UserRequest: {request.UserRequest}\n" +
                        $"Matched Conflict: {conflictAssessment.Classification}\n" +
                        $"Reason: Technical conflict blocks silent ticket creation.\n" +
                        $"Domain: {conflictAssessment.Domain}";
                    tBlock.ParsedResponseSummary = $"Blocked by conflict: {conflictAssessment.Classification}";
                    tBlock.RawResponseText       = string.Join("\n", blockQuestions);
                    _traceService.AddTrace(tBlock);

                    return new ContextAgentResult
                    {
                        TraceGroupId            = traceGroupId,
                        IsClarificationRequired = true,
                        ClarificationQuestions  = blockQuestions,
                        WasSuccessful           = true,
                        ContextSummary          = SummarisePacket(initialPacket),
                        Warnings                = string.Join("; ", warnings),
                        ConflictAssessment      = conflictAssessment,
                    };
                }
            }
        }
    }

    // ── Stage 4: Context expansion (direct + tool calls) ─────────────────
    if (!sufficiency.IsSufficient && route.AllowCodeSearch)
    {
        int toolCallCount = 0;
        int deepLookupsCount = 0;
        int totalDeepChars = 0;
        const int MaxDeepLookups = 5; 
        const int MaxTotalDeepChars = 20000;

        // ── Stage 4.0: Direct Deep Lookup for identified targets ──────────────
            if (route.AllowDeepLookup && route.DeepLookupTargets.Count > 0)
            {
                var tTargets = MakeTrace(ContextAgentStage.DirectDeepLookupTargets, traceGroupId, stage2Id);
                tTargets.RequestText = $"Targets identified by RouteJudge: {route.DeepLookupTargets.Count}\nWorkText: {route.EffectiveWorkText}";
                var targetsSummary = new StringBuilder();
                int successCount = 0;

                foreach (var target in route.DeepLookupTargets)
                {
                    bool success = false;
                    string failureReason = string.Empty;
                    int evidenceLength = 0;

                    try
                    {
                        var deepResult = await _deepLookupService.GetDeepCodeEvidenceAsync(
                            request.ProjectId, target.FilePath, target.SymbolName, target.ProofPattern, ct);

                        if (deepResult != null)
                        {
                            evidence.Add(new CodeEvidence
                            {
                                FilePath = deepResult.FilePath,
                                SymbolName = deepResult.SymbolName,
                                Snippet = deepResult.CodeText,
                                RetrievedByQuery = "[Direct Deep Lookup]",
                                SelectionReason = $"Directly targeted by RouteJudge: {target.ProofPattern} (EvidenceType: {deepResult.EvidenceType})"
                            });
                            success = true;
                            successCount++;
                            evidenceLength = deepResult.CodeText.Length;
                            deepLookupsCount++;
                            totalDeepChars += evidenceLength;
                        }
                        else
                        {
                            failureReason = "Target not found in index or body unparsable.";
                            if (target.Required)
                                warnings.Add($"Required verification target {target.FilePath}/{target.SymbolName} was not found.");
                        }
                    }
                    catch (Exception ex)
                    {
                        failureReason = ex.Message;
                        warnings.Add($"Direct lookup error for {target.FilePath}: {ex.Message}");
                    }

                targetsSummary.AppendLine($"Target: {target.FilePath} / {target.SymbolName}");
                targetsSummary.AppendLine($"  Pattern: {target.ProofPattern}");
                targetsSummary.AppendLine($"  Attempted: true");
                targetsSummary.AppendLine($"  Success: {success}");
                if (!success) targetsSummary.AppendLine($"  FailureReason: {failureReason}");
                targetsSummary.AppendLine($"  EvidenceLength: {evidenceLength}");
                targetsSummary.AppendLine($"  AppendedToFinalEvidence: {success}");
                targetsSummary.AppendLine($"  AppendedToProofGate: {success}");
                targetsSummary.AppendLine();
            }

            tTargets.WasSuccessful = successCount > 0;
            tTargets.RawResponseText = targetsSummary.ToString().TrimEnd();
            tTargets.ParsedResponseSummary = $"Direct targets processed: {successCount}/{route.DeepLookupTargets.Count} successful.";
            _traceService.AddTrace(tTargets);
        }

            // ── Stage 4.1: Keyword fallback ───────────────────────────────────
            var expandedQueries = RetrievalQualityHelpers
                .ExpandQueries(sufficiency.CodeSearchQueries)
                .Take(limits.MaxCodeSearchQueries)
                .ToList();

            foreach (var query in expandedQueries)
            {
                if (toolCallCount >= limits.MaxToolCallsPerRound) break;
                if (evidence.Select(e => e.FilePath).Distinct().Count() >= limits.MaxAddedFiles) break;
                if (evidence.Count >= limits.MaxSnippets) break;

                toolCallCount++;

                // Stage 4.1a: ToolCall trace
                var tCall = MakeTrace(ContextAgentStage.ToolCallSearch, traceGroupId, stage2Id);
                tCall.RequestText   = 
                    $"Action: GetRelevantSnippetsAsync\n" +
                    $"Query: {query}\n" +
                    $"OriginalQueries: {string.Join(", ", sufficiency.CodeSearchQueries)}\n" +
                    $"Filtering: Production-first (exclude tests)\n" +
                    $"MaxSnippets: {limits.MaxSnippets}";
                tCall.WasSuccessful = true;

                var swCall = Stopwatch.StartNew();
                IReadOnlyList<IronDev.Data.Models.CodeIndexEntry> rawResults;
                try
                {
                    rawResults = await _codeIndexService.GetRelevantSnippetsAsync(
                        request.ProjectId, query, limits.MaxSnippets, ct);
                }
                catch (Exception ex)
                {
                    tCall.WasSuccessful = false;
                    tCall.ErrorMessage  = ex.Message;
                    rawResults          = Array.Empty<IronDev.Data.Models.CodeIndexEntry>();
                    warnings.Add($"Code search '{query}' failed: {ex.Message}");
                }
                swCall.Stop();
                tCall.DurationMs = swCall.ElapsedMilliseconds;
                _traceService.AddTrace(tCall);

                // ── Apply production-first ranking and test-file filtering ────
                int rawCount  = rawResults.Count;
                int testCount = rawResults.Count(r => RetrievalQualityHelpers.IsTestFile(r.FilePath));

                // Rank: exclude test files, then sort by route-aware boost + production boost + depth
                var ranked = RetrievalQualityHelpers.RankAndFilter(rawResults, route, excludeTests: true);
                ranked     = RetrievalQualityHelpers.PreferDeepSnippets(ranked);

                int afterFilterCount = ranked.Count;

                // Stage 4.1b: ToolResult trace with full retrieval transparency
                var tResult = MakeTrace(ContextAgentStage.ToolResultSearch, traceGroupId, tCall.Id);
                tResult.WasSuccessful = true;
                tResult.DurationMs    = 0;

                int addedFromThisQuery = 0;
                var selectedEntries    = new List<SelectedEvidenceEntry>();

                foreach (var r in ranked)
                {
                    if (evidence.Select(e => e.FilePath).Distinct().Count() >= limits.MaxAddedFiles) break;
                    if (evidence.Count >= limits.MaxSnippets) break;

                    // Skip if we already have this exact symbol from direct lookup
                    bool alreadyHave = evidence.Any(e => 
                        e.FilePath.EndsWith(r.FilePath ?? string.Empty, StringComparison.OrdinalIgnoreCase) && 
                        e.SymbolName.Equals(r.SymbolName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                    if (alreadyHave) continue;

                    var snippet = r.ChunkText?.Length > 800
                        ? r.ChunkText[..800] + "\n...[TRUNCATED]..."
                        : r.ChunkText ?? string.Empty;

                    var snippetText = r.ChunkText ?? string.Empty;
                    var selectionReason = DetermineSelectionReason(r);
                    bool isShallow = RetrievalQualityHelpers.IsShallowSnippet(snippetText, r.SymbolName ?? string.Empty, query);

                    bool isSemantic = RetrievalQualityHelpers.IsSemanticSymbol(r.SymbolName);

                    if (isShallow && isSemantic && route.AllowDeepLookup && deepLookupsCount < MaxDeepLookups && totalDeepChars < MaxTotalDeepChars)
                    {
                        var deepResult = await _deepLookupService.GetDeepCodeEvidenceAsync(
                            request.ProjectId, r.FilePath ?? string.Empty, r.SymbolName ?? string.Empty, query, ct);

                        if (deepResult != null)
                        {
                            deepLookupsCount++;
                            var addedChars = deepResult.CodeText.Length;
                            totalDeepChars += addedChars;
                            snippetText = deepResult.CodeText;
                            selectionReason = deepResult.Reason;

                            var tDeep = MakeTrace(ContextAgentStage.DeepCodeEvidence, traceGroupId, tCall.Id);
                            tDeep.WasSuccessful = true;
                            tDeep.RequestText = $"OriginalQuery: {query}\n" +
                                                $"SelectedFile: {deepResult.FilePath}\n" +
                                                $"SelectedSymbol: {deepResult.SymbolName}\n" +
                                                $"ShallowSnippetLength: {r.ChunkText?.Length ?? 0}\n" +
                                                $"Reason: Shallow snippet detected. Triggered deep lookup.\n" +
                                                $"EvidenceType: {deepResult.EvidenceType}\n" +
                                                $"DeepEvidenceLength: {addedChars}";
                            tDeep.ParsedResponseSummary = $"Deep evidence retrieved: {deepResult.EvidenceType} ({addedChars} chars)";
                            _traceService.AddTrace(tDeep);
                        }
                        else
                        {
                            var tDeep = MakeTrace(ContextAgentStage.DeepCodeEvidence, traceGroupId, tCall.Id);
                            tDeep.WasSuccessful = false;
                            tDeep.RequestText = $"OriginalQuery: {query}\n" +
                                                $"SelectedFile: {r.FilePath}\n" +
                                                $"SelectedSymbol: {r.SymbolName}\n" +
                                                $"ShallowSnippetLength: {r.ChunkText?.Length ?? 0}\n" +
                                                $"Reason: Shallow snippet detected. Triggered deep lookup.";
                            tDeep.ParsedResponseSummary = "Deep lookup failed or returned no evidence.";
                            _traceService.AddTrace(tDeep);
                            
                            warnings.Add($"Deep lookup failed for {r.FilePath} ({r.SymbolName}). Final answer should remain honest.");
                        }
                    }

                    if (!isShallow || deepLookupsCount == MaxDeepLookups || snippetText == (r.ChunkText ?? string.Empty))
                    {
                        if (snippetText.Length > 800)
                        {
                            snippetText = snippetText[..800] + "\n...[TRUNCATED]...";
                        }
                    }

                    evidence.Add(new CodeEvidence
                    {
                        FilePath         = r.FilePath ?? "(unknown)",
                        SymbolName       = r.SymbolName ?? string.Empty,
                        Snippet          = snippetText,
                        RetrievedByQuery = query,
                        SelectionReason  = selectionReason,
                    });

                    selectedEntries.Add(new SelectedEvidenceEntry
                    {
                        FilePath = r.FilePath ?? "(unknown)",
                        Symbol   = r.SymbolName ?? string.Empty,
                        Reason   = selectionReason,
                    });
                    addedFromThisQuery++;
                }

                // Determine which queries this execution represents (original + expansions)
                var originalForThisQuery = sufficiency.CodeSearchQueries
                    .FirstOrDefault(q => expandedQueries.First() == query || q.Contains(query, StringComparison.OrdinalIgnoreCase))
                    ?? query;

                var retrievalSummary = new RetrievalTraceSummary
                {
                    OriginalQuery        = originalForThisQuery,
                    ExpandedQueries      = expandedQueries.Skip(1).ToList(), // first entry is original
                    RawResultCount       = rawCount,
                    AfterFilterCount     = afterFilterCount,
                    ExcludedTestCount    = testCount,
                    AddedToEvidenceCount = addedFromThisQuery,
                    SelectedFiles        = selectedEntries,
                };

                tResult.ParsedResponseSummary =
                    $"Query=\"{query}\" | Raw={rawCount} | ExcludedTests={testCount} | AfterFilter={afterFilterCount} | Added={addedFromThisQuery}";
                tResult.RequestText      = 
                    $"OriginalQuery: {originalForThisQuery}\n" +
                    $"ExpandedQueries: {string.Join(", ", expandedQueries)}\n" +
                    $"ProductionFilter: true\n" +
                    $"ExcludeTests: true\n" +
                    $"MaxResults: {limits.MaxSnippets}";
                tResult.RawResponseText  = retrievalSummary.ToTraceText();
                tResult.ContextSummary   = $"Evidence so far: {evidence.Count} snippet(s) from {evidence.Select(e => e.FilePath).Distinct().Count()} file(s)";
                _traceService.AddTrace(tResult);
            }
        }

        // ── Stage 4.5: Evidence Proof Gate ───────────────────────────────────
        RunFinalGating:
        // ── Stage 4.2: Candidate Extraction (for CreateTicketsFromDiscussion) ──
        if (route.RequestKind == ContextRequestKind.CreateTicketsFromDiscussion)
        {
            var (extracted, traceRaw) = await ExtractCandidatesAsync(request, initialPacket, ct);
            candidates = extracted;
            
            var tExt = MakeTrace(ContextAgentStage.CandidateExtraction, traceGroupId, stage2Id);
            tExt.WasSuccessful = candidates.Count > 0;
            tExt.RequestText = $"UserRequest: {request.UserRequest}\nSummary: {request.RecentConversationSummary}";
            tExt.RawResponseText = traceRaw;
            tExt.ParsedResponseSummary = $"Extracted {candidates.Count} candidate ticket(s) from discussion.";
            _traceService.AddTrace(tExt);
        }

        // ── Stage 4.5: Evidence Proof Gate ───────────────────────────────────
        bool shouldRunProof = route.RequestKind is ContextRequestKind.VerifyImplementation 
                            or ContextRequestKind.InspectCode 
                            or ContextRequestKind.ExplainCode;
        
        if (shouldRunProof)
        {
            proofResult = CheckEvidenceProof(route, evidence);
            var tProof = MakeTrace(ContextAgentStage.EvidenceProofGate, traceGroupId, stage2Id);
            tProof.WasSuccessful = proofResult.IsProven;
            tProof.RequestText = $"Proof Requirements for: {route.RequestKind}\nWorkText: {route.EffectiveWorkText}";
            
            var sbProof = new StringBuilder();
            sbProof.AppendLine($"Status: {proofResult.Status}");
            sbProof.AppendLine($"IsProven: {proofResult.IsProven}");
            if (!string.IsNullOrWhiteSpace(proofResult.ProofNotes))
                sbProof.AppendLine($"Notes: {proofResult.ProofNotes}");
            if (proofResult.MissingElements.Count > 0)
            {
                sbProof.AppendLine("Missing Elements:");
                foreach (var m in proofResult.MissingElements)
                    sbProof.AppendLine($"  - {m}");
            }
            tProof.RawResponseText = sbProof.ToString().TrimEnd();
            
            tProof.ParsedResponseSummary = proofResult.Status switch
            {
                ContextProofStatus.ProvenPresent => "Implementation proven present.",
                ContextProofStatus.ProvenAbsent  => "Implementation proven absent.",
                ContextProofStatus.NotProven     => $"Proof failed: {proofResult.MissingElements.Count} element(s) missing.",
                ContextProofStatus.InsufficientEvidence => "Insufficient evidence to confirm existence.",
                _ => "Unknown proof status."
            };
            _traceService.AddTrace(tProof);
        }
        else
        {
            var tSkip = MakeTrace(ContextAgentStage.EvidenceProofGateSkipped, traceGroupId, stage2Id);
            tSkip.WasSuccessful = true;
            tSkip.ParsedResponseSummary = $"Proof gate skipped for route: {route.RequestKind}";
            tSkip.RawResponseText = $"Reason=<route does not require implementation proof>. Route is {route.RequestKind}.";
            _traceService.AddTrace(tSkip);
            
            proofResult = new EvidenceProofResult { Status = ContextProofStatus.NotProven, EvidenceProofGateSkipped = true, EvidenceProofGateSkipReason = $"Route is {route.RequestKind}" };
        }

        // ── Stage 5: Assemble final prompt ────────────────────────────────────
        string finalPrompt;
        {
            finalPrompt = AssembleFinalPrompt(request, initialPacket, evidence, sufficiency, limits, warnings, route, proofResult, candidates);

            var contextSummary = BuildFinalContextSummary(initialPacket, evidence, sufficiency);

            var tFinal = MakeTrace(ContextAgentStage.FinalAnswer, traceGroupId, stage2Id);
            tFinal.WasSuccessful      = true;
            tFinal.CurrentUserMessage = request.UserRequest;
            tFinal.RequestText        = finalPrompt;
            tFinal.ParsedResponseSummary =
                $"FinalPrompt assembled | Evidence={evidence.Count} | Expanded={evidence.Count > 0} | TestFilesExcluded=true | Chars={finalPrompt.Length}";
            tFinal.ContextSummary = contextSummary;
            tFinal.Warnings       = string.Join("; ", warnings);
            _traceService.AddTrace(tFinal);

            return new ContextAgentResult
            {
                FinalPrompt          = finalPrompt,
                TraceGroupId         = traceGroupId,
                WasExpanded          = evidence.Count > 0,
                ExpandedFileCount    = evidence.Select(e => e.FilePath).Distinct().Count(),
                ExpandedSnippetCount = evidence.Count,
                WasSuccessful        = true,
                Evidence             = evidence,
                TicketCandidates     = candidates,
                ContextSummary       = contextSummary,
                Warnings             = string.Join("; ", warnings),
                ConflictAssessment   = conflictAssessment,
                EvidenceProofGateSkipped = proofResult.EvidenceProofGateSkipped,
                EvidenceProofGateSkipReason = proofResult.EvidenceProofGateSkipReason,
            };
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static LlmTraceEntry MakeTrace(string featureName, string traceGroupId, Guid? parentId)
        => new()
        {
            FeatureName   = featureName,
            WorkspaceName = "ContextAgent",
            TraceGroupId  = traceGroupId,
            ParentTraceId = parentId,
            CreatedAt     = DateTime.UtcNow,
        };

    private static ContextAgentResult Fail(string traceGroupId, string reason)
        => new()
        {
            TraceGroupId  = traceGroupId,
            WasSuccessful = false,
            Warnings      = reason,
        };

    private EvidenceProofResult CheckEvidenceProof(ContextAgentRouteDecision route, List<CodeEvidence> evidence)
    {
        var result = new EvidenceProofResult { Status = ContextProofStatus.ProvenPresent };
        var lowerWork = (route.EffectiveWorkText ?? string.Empty).ToLowerInvariant();

        // ── 1. Soft Archive Verification ─────────────────────────────────────
        if (route.RequestKind == ContextRequestKind.VerifyImplementation && (lowerWork.Contains("archive") || lowerWork.Contains("soft delete")))
        {
            bool hasArchiveMethod = evidence.Any(e => 
                e.FilePath.EndsWith("TicketService.cs", StringComparison.OrdinalIgnoreCase) && 
                e.SymbolName == "ArchiveTicketAsync" && 
                (e.Snippet.Contains("{") || e.Snippet.Contains("=>")));
            
            bool hasIsDeleted = evidence.Any(e => 
                e.FilePath.EndsWith("DataModels.cs", StringComparison.OrdinalIgnoreCase) && 
                (e.SymbolName == "ProjectTicket" || e.SymbolName == "IsDeleted") && 
                e.Snippet.Contains("public bool IsDeleted") && 
                e.Snippet.Contains("{ get; set; }"));

            bool hasFiltering = evidence.Any(e => 
                e.FilePath.EndsWith("TicketService.cs", StringComparison.OrdinalIgnoreCase) && 
                e.SymbolName == "GetRecentTicketsAsync" && 
                (e.Snippet.Contains("IsDeleted") || e.Snippet.Contains("!IsDeleted")));

            if (!hasArchiveMethod) result.MissingElements.Add("ArchiveTicketAsync method body (TicketService.cs)");
            if (!hasIsDeleted)    result.MissingElements.Add("ProjectTicket.IsDeleted property (DataModels.cs)");
            if (!hasFiltering)    result.MissingElements.Add("GetRecentTicketsAsync IsDeleted filter (TicketService.cs)");

            if (result.MissingElements.Count > 0)
            {
                int totalRequired = 3; 
                int foundCount = totalRequired - result.MissingElements.Count;
                result.Status = foundCount == 0 ? ContextProofStatus.NotProven : ContextProofStatus.InsufficientEvidence;
                if (foundCount > 0)
                {
                    result.ProofNotes = $"I verified {foundCount} of {totalRequired} required implementation elements, but {result.MissingElements.Count} are missing from the retrieved snippets.";
                }
            }
        }
        
        // ── 2. OAuth Existence Verification ──────────────────────────────────
        else if (lowerWork.Contains("oauth"))
        {
            // existence questions like "Does it support..." or "Check for..."
            bool hasOAuthKeywords = evidence.Any(e => e.Snippet.Contains("oauth", StringComparison.OrdinalIgnoreCase) 
                                                   || e.Snippet.Contains("external-login", StringComparison.OrdinalIgnoreCase));
            
            bool hasOAuthController = evidence.Any(e => e.Snippet.Contains("OAuthController") || e.Snippet.Contains("ExternalLogin"));
            bool hasOAuthConfig     = evidence.Any(e => e.Snippet.Contains("AddOAuth") || e.Snippet.Contains("OAuthOptions") || e.Snippet.Contains("JwtBearerOptions") == false && e.Snippet.Contains("AuthenticationOptions"));
            bool hasOAuthFlow       = evidence.Any(e => e.Snippet.Contains("AuthorizationCode") || e.Snippet.Contains("TokenExchange"));

            if (!hasOAuthKeywords)
            {
                result.Status = ContextProofStatus.ProvenAbsent;
                result.ProofNotes = "I found authentication logic (JWT), but no mentions of OAuth or external providers.";
            }
            else if (!hasOAuthController && !hasOAuthConfig && !hasOAuthFlow)
            {
                result.Status = ContextProofStatus.NotProven;
                result.MissingElements.Add("OAuth controller/action");
                result.MissingElements.Add("OAuth middleware/config");
                result.MissingElements.Add("Authorization code flow logic");
                result.ProofNotes = "I found 'oauth' keywords in the index, but the retrieved snippets do not show a full implementation.";
            }
        }

        return result;
    }

    private static string DetermineSelectionReason(IronDev.Data.Models.CodeIndexEntry entry)
    {
        if (RetrievalQualityHelpers.IsTestFile(entry.FilePath))
            return "test file (should not appear — excluded upstream)";

        var path   = entry.FilePath ?? string.Empty;
        var symbol = entry.SymbolName ?? string.Empty;

        if (path.Contains("TicketService", StringComparison.OrdinalIgnoreCase) &&
            symbol.Contains("Archive", StringComparison.OrdinalIgnoreCase))
            return "primary implementation (TicketService.ArchiveTicketAsync)";

        if (path.Contains("TicketService", StringComparison.OrdinalIgnoreCase))
            return "primary implementation (TicketService)";

        if (path.Contains("DataModels", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Models", StringComparison.OrdinalIgnoreCase))
            return "core data model";

        if (path.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase))
            return "infrastructure implementation";

        if (path.Contains("ViewModels", StringComparison.OrdinalIgnoreCase))
            return "view model";

        if (path.Contains("Views", StringComparison.OrdinalIgnoreCase))
            return "view / XAML";

        if (path.Contains("Interfaces", StringComparison.OrdinalIgnoreCase))
            return "interface definition";

        if (RetrievalQualityHelpers.IsDeepSnippet(entry.ChunkText))
            return "production code (deep snippet)";

        return "production code";
    }

    private static string BuildSufficiencyPrompt(string userRequest, ChatContextPacket packet, ContextAgentRouteDecision route)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SufficiencySystemPrompt);
        sb.AppendLine();
        sb.AppendLine($"=== CURRENT ROUTE: {route.RequestKind} ===");
        if (route.RequestKind == ContextRequestKind.ArchitectureAdvice)
        {
            sb.AppendLine("INSTRUCTION: This is an architecture advice request.");
            sb.AppendLine("- Do NOT search for non-existing symbols (e.g. 'BookRepository', 'DbContext') unless the user explicitly mentions they should exist.");
            sb.AppendLine("- Instead, search for EXISTING architectural patterns and project profiles to ground your recommendations.");
        }
        sb.AppendLine();
        sb.AppendLine("=== ASSEMBLED CONTEXT (excerpt) ===");
        var promptExcerpt = packet.FormattedPrompt.Length > 4000
            ? packet.FormattedPrompt[..4000] + "\n...[TRUNCATED]..."
            : packet.FormattedPrompt;
        sb.AppendLine(promptExcerpt);
        sb.AppendLine();
        sb.AppendLine("=== USER QUESTION ===");
        sb.AppendLine(userRequest);
        return sb.ToString();
    }

    private async Task<(List<TicketCandidate> Candidates, string TraceRaw)> ExtractCandidatesAsync(ContextAgentRequest request, ChatContextPacket packet, CancellationToken ct)
    {
        string prompt = $@"You are the Context Agent.
Extract potential ticket candidates from the following project discussion.

User Request: {request.UserRequest}

Project Context:
- Project ID: {request.ProjectId}
- Recent Conversation Summary:
{request.RecentConversationSummary}

Existing Tickets (for relationship checking):
{string.Join("\n", request.RecentTickets.Select(t => $"[{t.Id}] {t.Title}"))}

Extraction rules:
- Focus on actionable tasks discussed in the recent conversation.
- Do NOT claim implementation proof.
- Do NOT save tickets to the database.
- For each candidate, identify if any existing tickets are semantically related.
- Return a valid JSON list of TicketCandidate objects.

JSON Shape:
[
  {{
    ""title"": ""Short descriptive title"",
    ""summary"": ""Clear explanation of the work required."",
    ""suggestedDomain"": ""e.g. UI, REST API, Database"",
    ""existingRelatedWork"": ""e.g. Related to [22] - already mentions soft delete.""
  }}
]

Return JSON only.";

        string rawJson = string.Empty;
        var candidates = new List<TicketCandidate>();
        try
        {
            rawJson = await _llmService.GetResponseAsync(prompt, ct);
            var cleaned = rawJson.Trim();
            if (cleaned.StartsWith("```"))
            {
                var firstNewline = cleaned.IndexOf('\n');
                var lastFence    = cleaned.LastIndexOf("```");
                if (firstNewline > 0 && lastFence > firstNewline)
                    cleaned = cleaned[(firstNewline + 1)..lastFence].Trim();
            }

            var result = JsonSerializer.Deserialize<List<TicketCandidate>>(cleaned, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result != null) candidates.AddRange(result);
        }
        catch (Exception ex)
        {
            rawJson += $"\nExtraction Error: {ex.Message}";
        }

        return (candidates, rawJson);
    }

    private static string AssembleFinalPrompt(
        ContextAgentRequest         request,
        ChatContextPacket           initialPacket,
        List<CodeEvidence>          evidence,
        ContextSufficiencyResult    sufficiency,
        ContextAgentLimits          limits,
        List<string>                warnings,
        ContextAgentRouteDecision   route,
        EvidenceProofResult         proof,
        List<TicketCandidate>       candidates)
    {
        var sb = new StringBuilder();

        if (route.RequestKind == ContextRequestKind.CreateTicketsFromDiscussion)
        {
            sb.AppendLine("=== TICKET CANDIDATES EXTRACTED FROM DISCUSSION ===");
            if (candidates.Count > 0)
            {
                sb.AppendLine("The following candidate tickets were extracted for review. These have NOT been saved yet.");
                sb.AppendLine();
                for (var i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i];
                    sb.AppendLine($"{i + 1}. **{c.Title}**");
                    if (!string.IsNullOrWhiteSpace(c.SuggestedDomain))
                        sb.AppendLine($"   - **Domain:** {c.SuggestedDomain}");
                    if (!string.IsNullOrWhiteSpace(c.Summary))
                        sb.AppendLine($"   - **Summary:** {c.Summary}");
                    if (!string.IsNullOrWhiteSpace(c.ExistingRelatedWork))
                        sb.AppendLine($"   - **Related:** {c.ExistingRelatedWork}");
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("I could not extract any distinct actionable ticket candidates from the recent discussion.");
            }

            sb.AppendLine();
            sb.AppendLine("INSTRUCTION: Present these candidates using standard Markdown only.");
            sb.AppendLine("Use the exact shape shown above: numbered items, bold titles, and indented bullets with bold labels followed by a space.");
            sb.AppendLine("Do NOT write compact labels such as Domain:Database or Summary:Text.");
            sb.AppendLine("Do NOT claim you have created, saved, or implemented these tickets yet.");
            sb.AppendLine("End by telling the user to review the candidates and say `create tickets` to open draft ticket review.");
            return sb.ToString();
        }

        sb.Append(initialPacket.FormattedPrompt);

        sb.AppendLine();
        sb.AppendLine("=== CONTEXT AGENT GUIDELINES ===");
        sb.AppendLine($"- Your current route is: {route.RequestKind}");
        if (!string.IsNullOrWhiteSpace(route.ContextModeHint))
            sb.AppendLine($"- Current context mode hint is: {route.ContextModeHint}");
        if (!string.Equals(route.EffectiveWorkText, request.UserRequest, StringComparison.Ordinal))
        {
            sb.AppendLine($"- Original user text: {request.UserRequest}");
            sb.AppendLine($"- Resolved effective request: {route.EffectiveWorkText}");
            sb.AppendLine("- Answer the resolved effective request, while preserving the user's wording where it matters.");
        }
        
        if (route.RequestKind == ContextRequestKind.InspectCode || 
            route.RequestKind == ContextRequestKind.VerifyImplementation || 
            route.RequestKind == ContextRequestKind.ExplainCode ||
            route.RequestKind == ContextRequestKind.GeneralChat)
        {
            sb.AppendLine("- DO NOT emit <decision> tags. The user is asking for inspection/explanation, not for you to finalize a technical decision.");
            sb.AppendLine("- SELF-REFERENCE RULE: You must NOT use Context Agent internal code (like ContextAgentService or ContextConflictService) as evidence that a product feature exists or does not exist.");
            sb.AppendLine("- If those files appear in the evidence, use them ONLY to explain your own diagnostic process, not to prove product behavior.");
        }

        bool shouldEnforceHonesty = route.RequestKind is ContextRequestKind.VerifyImplementation 
                                or ContextRequestKind.InspectCode 
                                or ContextRequestKind.ExplainCode;

        if (route.RequestKind == ContextRequestKind.ArchitectureAdvice)
        {
            sb.AppendLine("- ARCHITECTURE ADVICE MODE: The user is seeking recommendations and industry-standard approaches.");
            sb.AppendLine("- You SHOULD provide expert recommendations even if the feature does not exist yet.");
            sb.AppendLine("- Label recommendations clearly as recommendations (e.g., 'I recommend...').");
            sb.AppendLine("- If no implementation exists, state: 'No implementation exists yet for this feature. Here are my recommendations:'");
            sb.AppendLine("- After providing your advice, you MUST ask the user: 'Should I record this as a [ProjectName] architecture decision?'");
            sb.AppendLine("- Do NOT pretend implementation exists if it doesn't.");
            sb.AppendLine("- Use existing project profile, standards, and decisions as grounding if available.");
        }

        if (route.RequestKind == ContextRequestKind.ArchitectureDecisionExploration)
        {
            sb.AppendLine("- ARCHITECTURE DECISION MODE: The user is confirming or selecting an architectural choice from the recent discussion.");
            sb.AppendLine("- Treat the resolved effective request as the decision being finalized unless it asks for more comparison.");
            sb.AppendLine("- Acknowledge the selected choice briefly. Do NOT repeat the full recommendation list.");
            sb.AppendLine("- You MUST emit exactly one hidden decision tag in this format:");
            sb.AppendLine("<decision>Decision Title | The detailed rule</decision>");
            sb.AppendLine("- The decision title should name the project and choice. The detailed rule should include the selected technology and how it will be used.");
            sb.AppendLine("- Do NOT ask 'Should I record this as a [ProjectName] architecture decision?' after the user has already confirmed the choice.");
            sb.AppendLine("- Do NOT claim implementation exists unless code evidence is present.");
        }

        if (shouldEnforceHonesty && proof.Status != ContextProofStatus.ProvenPresent)
        {
            sb.AppendLine();
            sb.AppendLine("=== HONESTY WARNING: INCOMPLETE EVIDENCE ===");
            if (proof.Status == ContextProofStatus.ProvenAbsent)
            {
                sb.AppendLine("The requested feature/implementation appears to be ABSENT from the project based on indexed evidence.");
                sb.AppendLine("WORDING RULE: Do NOT say 'The feature does not exist'.");
                sb.AppendLine("INSTEAD say: 'I found no indexed evidence proving X exists in the current codebase.'");
                if (!string.IsNullOrWhiteSpace(proof.ProofNotes))
                    sb.AppendLine(proof.ProofNotes);
            }
            else
            {
                sb.AppendLine("The retrieved evidence is INCOMPLETE to prove implementation existence/details.");
                
                // Identify what was successfully found from identified targets
                var foundElements = new List<string>();
                if (route.RequestKind == ContextRequestKind.VerifyImplementation || route.RequestKind == ContextRequestKind.InspectCode)
                {
                    foreach (var target in route.DeepLookupTargets)
                    {
                        // Match if file path matches AND (symbol matches OR snippet contains symbol OR it's a known property match)
                        bool found = evidence.Any(e => 
                            e.FilePath.EndsWith(target.FilePath, StringComparison.OrdinalIgnoreCase) && 
                            (e.SymbolName.Equals(target.SymbolName, StringComparison.OrdinalIgnoreCase) || 
                             e.Snippet.Contains(target.SymbolName) ||
                             (target.SymbolName == "ProjectTicket" && e.SymbolName == "IsDeleted")));
                             
                        if (found)
                        {
                            foundElements.Add($"{target.SymbolName} (in {target.FilePath})");
                        }
                    }
                }

                if (foundElements.Count > 0)
                {
                    sb.AppendLine("The following required elements WERE successfully found and retrieved:");
                    foreach (var found in foundElements)
                    {
                        sb.AppendLine($"- [FOUND] {found}");
                    }
                    sb.AppendLine();
                }

                if (proof.MissingElements.Count > 0)
                {
                    sb.AppendLine("The following required elements were NOT found in the retrieved code snippets:");
                    foreach (var missing in proof.MissingElements)
                    {
                        sb.AppendLine($"- [MISSING] {missing}");
                    }
                }
                if (!string.IsNullOrWhiteSpace(proof.ProofNotes))
                    sb.AppendLine($"Note: {proof.ProofNotes}");
            }
            sb.AppendLine();
            sb.AppendLine("BE HONEST. Do not claim the implementation is complete or correct if evidence is missing or absent.");
            sb.AppendLine("State clearly what you found and what is missing.");
        }

        sb.AppendLine();
        sb.AppendLine("=== CODE EVIDENCE RULE ===");
        if (evidence.Count > 0)
        {
            sb.AppendLine($"The following code was retrieved and inspected ({evidence.Count} snippet(s) from {evidence.Select(e => e.FilePath).Distinct().Count()} file(s)).");
            sb.AppendLine("Test fixture files were excluded. Only production implementation code is shown.");
            sb.AppendLine("You MAY reference this code as evidence in your answer.");
            sb.AppendLine();

            sb.AppendLine("=== EXPANDED CODE EVIDENCE (Context Agent) ===");
            foreach (var ev in evidence)
            {
                sb.AppendLine($"File:   {ev.FilePath}");
                if (!string.IsNullOrWhiteSpace(ev.SymbolName))
                    sb.AppendLine($"Symbol: {ev.SymbolName}");
                if (!string.IsNullOrWhiteSpace(ev.SelectionReason))
                    sb.AppendLine($"Reason: {ev.SelectionReason}");
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
            if (route.RequestKind == ContextRequestKind.ArchitectureAdvice ||
                route.RequestKind == ContextRequestKind.ArchitectureDecisionExploration)
            {
                sb.AppendLine("This route does not require code evidence. Use project profile, decisions, standards, facts, and conversation state.");
                sb.AppendLine("Do NOT claim implementation exists unless code evidence is present.");
            }
            else
            {
                sb.AppendLine("If your answer requires inspecting specific code that is not in the context above, you MUST say:");
                sb.AppendLine("\"I do not have enough indexed code context to verify that.\"");
                sb.AppendLine("Do NOT invent code details not present in the retrieved snippets.");
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== CONTEXT QUALITY ===");
        sb.AppendLine($"Sufficiency check confidence: {sufficiency.Confidence}/10");
        sb.AppendLine($"Reason: {sufficiency.Reason}");
        if (warnings.Count > 0)
        {
            sb.AppendLine($"Warnings: {string.Join("; ", warnings)}");
            if (warnings.Any(w => w.Contains("Deep lookup failed")))
            {
                sb.AppendLine();
                sb.AppendLine("GROUNDING RULE: Deep lookup failed for a candidate file. You MUST remain honest and state:");
                sb.AppendLine("\"I found the likely file/symbol, but the indexed/deep evidence still does not prove the implementation details.\"");
                sb.AppendLine("Do not invent or assume implementation details.");
            }
        }

        var result = sb.ToString();
        if (result.Length > limits.MaxContextChars)
            result = result[..limits.MaxContextChars] + "\n...[CONTEXT TRUNCATED — budget exceeded]...";

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
            int confidence = 5;
            if (root.TryGetProperty("confidence", out var confEl))
            {
                if (confEl.ValueKind == JsonValueKind.Number)
                    confidence = (int)Math.Round(confEl.GetDouble());
                else if (confEl.ValueKind == JsonValueKind.String && int.TryParse(confEl.GetString(), out var val))
                    confidence = val;
            }

            string reason  = root.TryGetProperty("reason", out var rEl) ? rEl.GetString() ?? string.Empty : string.Empty;

            var queries   = new List<string>();
            var questions = new List<string>();

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
                IsSufficient      = true,
                Confidence        = 4,
                Reason            = "Sufficiency check could not be parsed.",
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
            var prodFiles = evidence.Where(e => !RetrievalQualityHelpers.IsTestFile(e.FilePath))
                                    .Select(e => e.FilePath).Distinct().ToList();
            sb.AppendLine($"Expanded files:   {evidence.Select(e => e.FilePath).Distinct().Count()} (production only)");
            sb.AppendLine($"Expanded snippets:{evidence.Count}");
            foreach (var f in prodFiles.Take(5))
                sb.AppendLine($"  - {Path.GetFileName(f)}");
        }
        return sb.ToString().TrimEnd();
    }
}
