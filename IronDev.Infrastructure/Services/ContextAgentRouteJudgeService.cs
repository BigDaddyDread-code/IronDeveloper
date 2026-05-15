using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services;

public sealed class ContextAgentRouteJudgeService : IContextAgentRouteJudge
{
    private readonly ILLMService _llmService;
    private readonly ILlmTraceService _traceService;

    private static readonly string[] InspectionPrefixes =
    [
        "check", "inspect", "what", "look", "explain", "how", "where", "find",
        "show", "why", "does", "is", "are", "can", "review", "verify", "who"
    ];

    private static readonly string[] ChangePrefixes =
    [
        "implement", "replace", "change", "build", "generate", "add", "update",
        "fix", "refactor", "remove", "rewrite", "migrate", "create"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ContextAgentRouteJudgeService(ILLMService llmService, ILlmTraceService traceService)
    {
        _llmService = llmService;
        _traceService = traceService;
    }

    public async Task<ContextAgentRouteDecision> DecideRouteAsync(ContextAgentRouteRequest request, CancellationToken cancellationToken = default)
    {
        var evidencePacket = BuildEvidencePacket(request);
        var prompt = BuildPrompt(evidencePacket);

        ContextAgentRouteDecision decision;
        bool usedLlmJudge = false;
        bool usedFallbackRules = false;
        string rawJson = string.Empty;

        try
        {
            // Pre-router catch obvious cases
            if (IsObviousFallback(request))
            {
                decision = FallbackRoute(request.UserRequest, request.InitialIntentFromPromptContextBuilder);
                usedFallbackRules = true;
            }
            else
            {
                rawJson = await _llmService.GetResponseAsync(prompt, cancellationToken);
                decision = ParseJsonDecision(rawJson, request.UserRequest);
                usedLlmJudge = true;
            }
        }
        catch (Exception ex)
        {
            rawJson = $"LLM Error: {ex.Message}";
            decision = FallbackRoute(request.UserRequest, request.InitialIntentFromPromptContextBuilder);
            usedFallbackRules = true;
        }

        var safetyOverrides = new List<string>();
        decision = new ContextAgentRouteDecision
        {
            OriginalUserRequest = decision.OriginalUserRequest,
            EffectiveWorkText = decision.EffectiveWorkText,
            RequestKind = decision.RequestKind,
            Confidence = decision.Confidence,
            Reason = decision.Reason,
            AllowCodeSearch = decision.AllowCodeSearch,
            AllowDeepLookup = decision.AllowDeepLookup,
            AllowConflictAssessment = decision.AllowConflictAssessment,
            AllowConflictBlocking = decision.AllowConflictBlocking,
            AllowTicketCreation = decision.AllowTicketCreation,
            RelatedTicketsAreContextOnly = decision.RelatedTicketsAreContextOnly,
            NeedsClarification = decision.NeedsClarification,
            ClarificationQuestions = decision.ClarificationQuestions,
            EvidenceUsed = decision.EvidenceUsed,
            Risks = decision.Risks,
            DeepLookupTargets = decision.DeepLookupTargets,
            UsedLlmJudge = usedLlmJudge,
            UsedFallbackRules = usedFallbackRules
        };

        decision = ApplySafetyValidation(decision, request.UserRequest, safetyOverrides);
        
        // Emit RouteGateDecision trace
        var tGate = new LlmTraceEntry
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            FeatureName = ContextAgentStage.RouteGateDecision,
            TraceGroupId = request.TraceGroupId,
            ProjectId = request.ProjectId,
            ChatSessionId = request.SessionId.ToString(),
            CurrentUserMessage = request.UserRequest,
            RawResponseText = 
                $"Route: {decision.RequestKind}\n" +
                $"AllowCodeSearch: {decision.AllowCodeSearch}\n" +
                $"AllowDeepLookup: {decision.AllowDeepLookup}\n" +
                $"AllowConflictAssessment: {decision.AllowConflictAssessment}\n" +
                $"AllowConflictBlocking: {decision.AllowConflictBlocking}\n" +
                $"AllowTicketCreation: {decision.AllowTicketCreation}\n" +
                $"RelatedTicketsAreContextOnly: {decision.RelatedTicketsAreContextOnly}",
            ParsedResponseSummary = $"Stages gated for {decision.RequestKind}",
            WasSuccessful = true
        };
        _traceService.AddTrace(tGate);

        EmitTrace(request, decision, prompt, rawJson, evidencePacket);

        return decision;
    }

    private string BuildEvidencePacket(ContextAgentRouteRequest request)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"UserRequest: {request.UserRequest}");
        if (!string.IsNullOrWhiteSpace(request.RecentConversationSummary))
        {
            sb.AppendLine("RecentConversationSummary:");
            sb.AppendLine(request.RecentConversationSummary);
        }
        if (!string.IsNullOrWhiteSpace(request.InitialIntentFromPromptContextBuilder))
            sb.AppendLine($"InitialIntent: {request.InitialIntentFromPromptContextBuilder}");
        if (request.RecentTickets.Count > 0)
            sb.AppendLine($"RecentTickets: {request.RecentTickets.Count} items");
        if (request.RecentDecisions.Count > 0)
            sb.AppendLine($"RecentDecisions: {request.RecentDecisions.Count} items");
        return sb.ToString().TrimEnd();
    }

    private string BuildPrompt(string evidencePacket)
    {
        return $@"You are the Context Agent route judge.

Your job is not to answer the user.
Your job is to classify what the user is trying to do and decide which Context Agent stages are allowed to run.

Classify using only the evidence provided.

Important rules:
- Inspection, verification, explanation, review, ""look for"", ""check whether"", and ""what does"" requests must not be blocked by ticket conflicts.
- Ticket creation requests may run related-ticket and conflict checks.
- Change, replace, implement, or build requests may run architecture-decision conflict checks.
- COMMAND TEXT RULE: The words ""create a ticket"", ""extract candidates"", ""candidates from discussion"", and ""create tickets"" are command text, not work-domain text.
- You MUST strip these command phrases from the effectiveWorkText.
- The word ""ticket"" is only a work-domain signal if the work itself concerns ticket management (e.g., ""archive ticket"").
- If the user asks if a feature exists, supports something, or is implemented, classify as VerifyImplementation.
- Existing tickets and decisions are evidence, not automatic blockers.
- If unsure, ask clarification.
- Do not answer the user.
- Return valid JSON only.

Return this JSON shape:

{{
  ""requestKind"": ""GeneralChat|InspectCode|ExplainCode|VerifyImplementation|CreateTicket|CreateTicketsFromDiscussion|ChangeImplementation|ReplaceArchitecture|BuildTicket|ArchitectureAdvice|ArchitectureDecisionExploration"",
  ""confidence"": 0.0,
  ""effectiveWorkText"": ""Expanded and resolved request text (e.g. 'industry standard' -> 'industry standard persistence for BookSeller')"",
  ""reason"": """",
  ""allowCodeSearch"": true,
  ""allowDeepLookup"": true,
  ""allowConflictAssessment"": false,
  ""allowConflictBlocking"": false,
  ""allowTicketCreation"": false,
  ""relatedTicketsAreContextOnly"": true,
  ""needsClarification"": false,
  ""clarificationQuestions"": [],
  ""evidenceUsed"": [],
  ""risks"": []
}}

CONTINUATION RESOLUTION RULE:
If UserRequest is short, vague, or a follow-up (e.g. ""industry standard"", ""yes"", ""that one"", ""how?"", ""why?""):
1. Use RecentConversationSummary to resolve what the user is actually asking about.
2. Set effectiveWorkText to the fully resolved question (e.g. ""What is the industry-standard approach for BookSeller persistence?"").
3. If RecentConversationSummary does not provide enough context, set needsClarification=true.

ARCHITECTURE ADVICE RULE:
If the user asks for recommendations, ""best way"", ""industry standard"", ""options"", or comparisons for a feature (even if not implemented yet):
1. Classify as ArchitectureAdvice.
2. Set allowCodeSearch=true (to check existing patterns).
3. Set allowConflictAssessment=false.

Evidence Packet:
{evidencePacket}
";
    }

    private ContextAgentRouteDecision ParseJsonDecision(string json, string userRequest)
    {
        // Extract JSON part if markdown formatting is present
        var jsonSpan = json.AsSpan();
        int startIdx = jsonSpan.IndexOf('{');
        int endIdx = jsonSpan.LastIndexOf('}');
        
        if (startIdx >= 0 && endIdx >= 0 && endIdx > startIdx)
        {
            json = json.Substring(startIdx, endIdx - startIdx + 1);
        }

        var decision = JsonSerializer.Deserialize<ContextAgentRouteDecision>(json, JsonOptions);
        if (decision == null) throw new InvalidOperationException("Failed to deserialize route decision.");
        
        return new ContextAgentRouteDecision
        {
            OriginalUserRequest = userRequest,
            EffectiveWorkText = decision.EffectiveWorkText ?? string.Empty,
            RequestKind = decision.RequestKind,
            Confidence = decision.Confidence,
            Reason = decision.Reason ?? string.Empty,
            AllowCodeSearch = decision.AllowCodeSearch,
            AllowDeepLookup = decision.AllowDeepLookup,
            AllowConflictAssessment = decision.AllowConflictAssessment,
            AllowConflictBlocking = decision.AllowConflictBlocking,
            AllowTicketCreation = decision.AllowTicketCreation,
            RelatedTicketsAreContextOnly = decision.RelatedTicketsAreContextOnly,
            NeedsClarification = decision.NeedsClarification,
            ClarificationQuestions = decision.ClarificationQuestions ?? Array.Empty<string>(),
            EvidenceUsed = decision.EvidenceUsed ?? Array.Empty<string>(),
            Risks = decision.Risks ?? Array.Empty<string>(),
            DeepLookupTargets = decision.DeepLookupTargets ?? Array.Empty<DeepLookupTarget>()
        };
    }

    private bool IsObviousFallback(ContextAgentRouteRequest request)
    {
        var lower = (request.UserRequest ?? string.Empty).ToLowerInvariant().Trim();
        if (lower.StartsWith("/ticket") || lower.StartsWith("/create-ticket")) return true;
        
        // If it's just "create a ticket" and we have no context, it's a fallback.
        // If we HAVE context, we want the LLM to resolve it against the active topic.
        if (string.IsNullOrWhiteSpace(request.RecentConversationSummary) && lower.StartsWith("create a ticket")) return true;
        
        if (request.InitialIntentFromPromptContextBuilder == "CreateTicket") return true;
        return false;
    }

    private ContextAgentRouteDecision FallbackRoute(string request, string intent)
    {
        var lower = (request ?? string.Empty).ToLowerInvariant().Trim();
        
        if (intent == "CreateTicket" || lower.StartsWith("/ticket") || lower.StartsWith("/create-ticket") || lower.StartsWith("create a ticket"))
        {
            return new ContextAgentRouteDecision
            {
                OriginalUserRequest     = request ?? string.Empty,
                EffectiveWorkText       = request ?? string.Empty,
                RequestKind             = ContextRequestKind.CreateTicket,
                Confidence              = 1.0,
                Reason                  = "Deterministic pre-router: Explicit create ticket command.",
                AllowCodeSearch         = true,
                AllowDeepLookup         = true,
                AllowConflictAssessment = true,
                AllowConflictBlocking   = true,
                AllowTicketCreation     = true,
                RelatedTicketsAreContextOnly = false
            };
        }

        if (StartsWithAny(lower, InspectionPrefixes))
        {
            var kind = ContextRequestKind.InspectCode;
            if (lower.StartsWith("check") || lower.StartsWith("verify") || lower.Contains("support") || lower.Contains("implemented") || lower.Contains("exist")) 
                kind = ContextRequestKind.VerifyImplementation;
            if (lower.StartsWith("explain")) kind = ContextRequestKind.ExplainCode;

            return new ContextAgentRouteDecision
            {
                OriginalUserRequest     = request ?? string.Empty,
                EffectiveWorkText       = request ?? string.Empty,
                RequestKind             = kind,
                Confidence              = 0.9,
                Reason                  = "Deterministic pre-router: Inspection query.",
                AllowCodeSearch         = true,
                AllowDeepLookup         = true,
                AllowConflictAssessment = false,
                AllowConflictBlocking   = false,
                AllowTicketCreation     = false,
                RelatedTicketsAreContextOnly = true,
                DeepLookupTargets = IdentifyTargets(lower)
            };
        }

        if (StartsWithAny(lower, ChangePrefixes))
        {
            var kind = ContextRequestKind.ChangeImplementation;
            if (lower.StartsWith("replace")) kind = ContextRequestKind.ReplaceArchitecture;
            if (lower.StartsWith("build")) kind = ContextRequestKind.BuildTicket;

            return new ContextAgentRouteDecision
            {
                OriginalUserRequest     = request ?? string.Empty,
                EffectiveWorkText       = request ?? string.Empty,
                RequestKind             = kind,
                Confidence              = 0.8,
                Reason                  = "Deterministic pre-router: Change command.",
                AllowCodeSearch         = true,
                AllowDeepLookup         = true,
                AllowConflictAssessment = true,
                AllowConflictBlocking   = true,
                AllowTicketCreation     = false,
                RelatedTicketsAreContextOnly = false
            };
        }

        return new ContextAgentRouteDecision
        {
            OriginalUserRequest     = request ?? string.Empty,
            EffectiveWorkText       = request ?? string.Empty,
            RequestKind             = ContextRequestKind.GeneralChat,
            Confidence              = 0.5,
            Reason                  = "Deterministic pre-router: Fallback to general chat.",
            AllowCodeSearch         = true,
            AllowDeepLookup         = false,
            AllowConflictAssessment = false,
            AllowConflictBlocking   = false,
            AllowTicketCreation     = false,
            RelatedTicketsAreContextOnly = true
        };
    }

    private ContextAgentRouteDecision ApplySafetyValidation(ContextAgentRouteDecision decision, string userRequest, List<string> overrides)
    {
        var lower = userRequest.ToLowerInvariant().Trim();
        bool isInspection = decision.RequestKind == ContextRequestKind.InspectCode || 
                            decision.RequestKind == ContextRequestKind.ExplainCode || 
                            decision.RequestKind == ContextRequestKind.VerifyImplementation;

        bool isCreateTicket = decision.RequestKind == ContextRequestKind.CreateTicket ||
                              decision.RequestKind == ContextRequestKind.CreateTicketsFromDiscussion;

        bool allowConflictBlocking = decision.AllowConflictBlocking;
        bool needsClarification = decision.NeedsClarification;
        bool relatedTicketsAreContextOnly = decision.RelatedTicketsAreContextOnly;

        if (isInspection && allowConflictBlocking)
        {
            allowConflictBlocking = false;
            overrides.Add("Inspection requests cannot allow conflict blocking. Overriding AllowConflictBlocking to false.");
        }

        if (isInspection && !relatedTicketsAreContextOnly)
        {
            relatedTicketsAreContextOnly = true;
            overrides.Add("Inspection requests must treat related tickets as context-only. Overriding RelatedTicketsAreContextOnly to true.");
        }

        if (decision.Confidence < 0.70)
        {
            needsClarification = true;
            overrides.Add("Confidence is below 0.70. Setting NeedsClarification to true.");
        }

        if (isCreateTicket && !allowConflictBlocking)
        {
            allowConflictBlocking = true;
            overrides.Add("CreateTicket requests must allow conflict blocking. Overriding AllowConflictBlocking to true.");
        }

        string effectiveWorkText = decision.EffectiveWorkText;
        if (isCreateTicket)
        {
            string[] prefixes = [
                "create a ticket to ", "create a ticket ", "extract tickets from ", "extract candidates from ", 
                "create ticket candidates from ", "extract ticket candidates from ", "generate tickets from ",
                "create tickets ", "create ticket ", "extract candidates ", "candidates from discussion ",
                "tickets from discussion ", "discussion about "
            ];
            
            foreach (var p in prefixes)
            {
                if (effectiveWorkText.ToLowerInvariant().StartsWith(p))
                {
                    effectiveWorkText = effectiveWorkText.Substring(p.Length).Trim();
                    overrides.Add($"Stripped '{p}' from EffectiveWorkText.");
                    break;
                }
            }

            // Standalone word stripping if they are just leading the work text
            var words = effectiveWorkText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1)
            {
                var first = words[0].ToLowerInvariant();
                if (first == "ticket" || first == "tickets" || first == "candidates" || first == "discussion" || first == "create")
                {
                    effectiveWorkText = string.Join(" ", words.Skip(1)).Trim();
                    overrides.Add($"Stripped leading word '{first}' from EffectiveWorkText.");
                }
            }
        }

        return new ContextAgentRouteDecision
        {
            OriginalUserRequest = decision.OriginalUserRequest,
            EffectiveWorkText = effectiveWorkText,
            RequestKind = decision.RequestKind,
            Confidence = decision.Confidence,
            Reason = decision.Reason,
            AllowCodeSearch = decision.AllowCodeSearch,
            AllowDeepLookup = decision.AllowDeepLookup,
            AllowConflictAssessment = decision.AllowConflictAssessment,
            AllowConflictBlocking = allowConflictBlocking,
            AllowTicketCreation = decision.AllowTicketCreation,
            RelatedTicketsAreContextOnly = relatedTicketsAreContextOnly,
            NeedsClarification = needsClarification,
            ClarificationQuestions = decision.ClarificationQuestions,
            EvidenceUsed = decision.EvidenceUsed,
            Risks = decision.Risks,
            DeepLookupTargets = decision.DeepLookupTargets,
            SafetyOverrides = overrides,
            UsedLlmJudge = decision.UsedLlmJudge,
            UsedFallbackRules = decision.UsedFallbackRules
        };
    }

    private IReadOnlyList<DeepLookupTarget> IdentifyTargets(string lower)
    {
        var targets = new List<DeepLookupTarget>();
        
        if (lower.Contains("soft archive") || lower.Contains("archive ticket"))
        {
            targets.Add(new DeepLookupTarget { FilePath = "TicketService.cs", SymbolName = "ArchiveTicketAsync", ProofPattern = "Body" });
            targets.Add(new DeepLookupTarget { FilePath = "TicketService.cs", SymbolName = "GetRecentTicketsAsync", ProofPattern = "IsDeleted filter" });
            targets.Add(new DeepLookupTarget { FilePath = "DataModels.cs", SymbolName = "ProjectTicket", ProofPattern = "IsDeleted property" });
        }
        
        if (lower.Contains("auth") || lower.Contains("login") || lower.Contains("oauth"))
        {
            targets.Add(new DeepLookupTarget { FilePath = "AuthController.cs", SymbolName = "Login", ProofPattern = "Body" });
        }
        
        return targets;
    }

    private void EmitTrace(ContextAgentRouteRequest request, ContextAgentRouteDecision decision, string prompt, string rawJson, string evidencePacket)
    {
        var trace = new LlmTraceEntry
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            FeatureName = ContextAgentStage.RouteDecision,
            TraceGroupId = request.TraceGroupId,
            ProjectId = request.ProjectId,
            ChatSessionId = request.SessionId.ToString(),
            CurrentUserMessage = request.UserRequest,
            RequestText = $"Prompt Sent:\n{prompt}\n\nEvidence Packet:\n{evidencePacket}",
            RawResponseText = 
                $"Reason: {decision.Reason}\n" +
                $"AllowCodeSearch: {decision.AllowCodeSearch}\n" +
                $"AllowDeepLookup: {decision.AllowDeepLookup}\n" +
                $"AllowConflictAssessment: {decision.AllowConflictAssessment}\n" +
                $"AllowConflictBlocking: {decision.AllowConflictBlocking}\n" +
                $"AllowTicketCreation: {decision.AllowTicketCreation}\n" +
                $"RelatedTicketsAreContextOnly: {decision.RelatedTicketsAreContextOnly}\n" +
                $"\nRaw JSON:\n{rawJson}\n\nOverrides:\n{string.Join("\n", decision.SafetyOverrides)}",
            ParsedResponseSummary = $"Kind={decision.RequestKind} | Confidence={decision.Confidence} | Effective={decision.EffectiveWorkText}",
            ContextSummary = $"Kind={decision.RequestKind} | Confidence={decision.Confidence} | ConflictBlocking={decision.AllowConflictBlocking} | DeepLookup={decision.AllowDeepLookup} | UsedLlmJudge={decision.UsedLlmJudge} | UsedFallbackRules={decision.UsedFallbackRules}",
            WasSuccessful = true
        };
        _traceService.AddTrace(trace);
    }

    private static bool StartsWithAny(string text, string[] prefixes)
    {
        return prefixes.Any(p => text.StartsWith(p + " ") || text == p);
    }
}
