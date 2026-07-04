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
    private readonly IConversationContextResolver _conversationResolver;

    private static readonly string[] InspectionPrefixes =
    [
        "check", "inspect", "what", "look", "explain", "how", "where", "find",
        "show", "why", "does", "is", "are", "can", "review", "verify", "who"
    ];

    private static readonly string[] ChangePrefixes =
    [
        "implement", "replace", "change", "fix", "refactor", "remove", "rewrite", "migrate",
        "update"
    ];

    private static readonly string[] TicketCommandPrefixes =
    [
        "create a ticket",
        "create ticket",
        "build ticket",
        "build a ticket",
        "make this a ticket",
        "make this into a ticket",
        "turn this into a ticket",
        "turn this into tickets",
        "create tickets",
        "create work packet"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ContextAgentRouteJudgeService(
        ILLMService llmService,
        ILlmTraceService traceService,
        IConversationContextResolver? conversationResolver = null)
    {
        _llmService = llmService;
        _traceService = traceService;
        _conversationResolver = conversationResolver ?? new ConversationContextResolver();
    }

    public async Task<ContextAgentRouteDecision> DecideRouteAsync(ContextAgentRouteRequest request, CancellationToken cancellationToken = default)
    {
        var evidencePacket = BuildEvidencePacket(request);
        var prompt = BuildPrompt(evidencePacket);

        ContextAgentRouteDecision decision;
        bool usedLlmJudge = false;
        bool usedFallbackRules = false;
        bool usedConversationResolver = false;
        string rawJson = string.Empty;

        try
        {
            var resolution = _conversationResolver.Resolve(request);
            if (resolution.IsResolved || resolution.NeedsClarification)
            {
                decision = ToRouteDecision(request, resolution);
                usedConversationResolver = true;
                rawJson = BuildResolutionTraceText(resolution);
            }
            // Pre-router catch obvious cases
            else if (IsObviousFallback(request))
            {
                decision = ForceExplicitCommitGuardrails(
                    FallbackRoute(request.UserRequest, request.InitialIntentFromPromptContextBuilder),
                    request.UserRequest);
                usedFallbackRules = true;
            }
            else
            {
                rawJson = await _llmService.GetResponseAsync(prompt, cancellationToken);
                decision = ForceExplicitCommitGuardrails(ParseJsonDecision(rawJson, request.UserRequest), request.UserRequest);
                usedLlmJudge = true;
            }
        }
        catch (Exception ex)
        {
            rawJson = $"LLM Error: {ex.Message}";
            decision = ForceExplicitCommitGuardrails(
                FallbackRoute(request.UserRequest, request.InitialIntentFromPromptContextBuilder),
                request.UserRequest);
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
            UsedFallbackRules = usedFallbackRules,
            UsedConversationContextResolver = usedConversationResolver,
            ContextModeHint = decision.ContextModeHint
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

        if (usedConversationResolver)
        {
            var tResolution = new LlmTraceEntry
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                FeatureName = ContextAgentStage.IntentContextResolution,
                TraceGroupId = request.TraceGroupId,
                ProjectId = request.ProjectId,
                ChatSessionId = request.SessionId.ToString(),
                CurrentUserMessage = request.UserRequest,
                RequestText = request.RecentConversationSummary,
                RawResponseText = rawJson,
                ParsedResponseSummary = $"ModeHint={decision.ContextModeHint} | Effective={decision.EffectiveWorkText}",
                WasSuccessful = true
            };
            _traceService.AddTrace(tResolution);
        }

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
        if (request.ConversationContextSnapshot is { HasUsefulState: true } snapshot)
        {
            sb.AppendLine("ConversationContextSnapshot:");
            sb.AppendLine($"ActiveTopic: {snapshot.ActiveTopic}");
            sb.AppendLine($"CurrentGoal: {snapshot.CurrentGoal}");
            sb.AppendLine($"ContextMode: {snapshot.ContextMode}");
            sb.AppendLine($"PendingDecision: {snapshot.PendingDecision}");
            sb.AppendLine($"LastRecommendation: {snapshot.LastRecommendation}");
            if (snapshot.LastOptionsPresented.Count > 0)
                sb.AppendLine($"LastOptionsPresented: {string.Join("; ", snapshot.LastOptionsPresented)}");
            if (snapshot.KnownFacts.Count > 0)
                sb.AppendLine($"KnownFacts: {string.Join("; ", snapshot.KnownFacts)}");
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
- Change, replace, implement, or build requests are context-retrieval hints only unless the user explicitly asks for ticket creation.
- Do not classify generic build/generate/add requests as BuildTicket unless explicit handoff language exists.
- COMMAND TEXT RULE: The words ""create a ticket"", ""extract candidates"", ""candidates from discussion"", and ""create tickets"" are command text, not work-domain text.
- You MUST strip these command phrases from the effectiveWorkText.
- The word ""ticket"" is only a work-domain signal if the work itself concerns ticket management (e.g., ""archive ticket"").
- If the user asks if a feature exists, supports something, or is implemented, classify as VerifyImplementation.
- Existing tickets and decisions are evidence, not automatic blockers.
- If unsure, ask clarification.
- Chat governance mode is not decided here.
  - Do not force Formalization or Confirmation.
  - Save/capture/record discussion intent is owned by LlmChatModeClassifier, not this route judge.
  - Route hints may describe retrieval/check stages, but they must not become final chat mode authority.
- Do not answer the user.
- Return valid JSON only.

Return this JSON shape:

{{
  ""requestKind"": ""GeneralChat|InspectCode|ExplainCode|VerifyImplementation|CreateTicket|CreateTicketsFromDiscussion|ChangeImplementation|ReplaceArchitecture|BuildTicket|ArchitectureAdvice|ArchitectureDecisionExploration"",
  ""confidence"": 0.0,
  ""effectiveWorkText"": ""Expanded and resolved request text (e.g. 'industry standard' -> 'industry standard persistence for the active project')"",
  ""reason"": """",
  ""contextModeHint"": ""Exploration"",
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
2. Set effectiveWorkText to the fully resolved question (e.g. ""What is the industry-standard approach for this project's persistence?"").
3. If RecentConversationSummary does not provide enough context, set needsClarification=true.

ARCHITECTURE ADVICE RULE:
If the user asks for recommendations, ""best way"", ""industry standard"", ""options"", or comparisons for a feature (even if not implemented yet):
1. Classify as ArchitectureAdvice.
2. Set allowCodeSearch=false unless the user explicitly asks whether something exists in code.
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
            ContextModeHint = NormalizeContextModeHint(decision.ContextModeHint),
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

    private static ContextAgentRouteDecision ToRouteDecision(
        ContextAgentRouteRequest request,
        ConversationContextResolution resolution)
        => new()
        {
            OriginalUserRequest = request.UserRequest,
            EffectiveWorkText = resolution.EffectiveRequest,
            RequestKind = resolution.RequestKind,
            Confidence = resolution.NeedsClarification ? 0.5 : 0.95,
            Reason = resolution.Reason,
            AllowCodeSearch = resolution.RequiresCodeEvidence,
            AllowDeepLookup = resolution.RequiresCodeEvidence,
            AllowConflictAssessment = resolution.AllowsTicketCreation,
            AllowConflictBlocking = resolution.AllowsTicketCreation,
            AllowTicketCreation = resolution.AllowsTicketCreation,
            RelatedTicketsAreContextOnly = !resolution.AllowsTicketCreation,
            NeedsClarification = resolution.NeedsClarification,
            ClarificationQuestions = resolution.ClarificationQuestions,
            EvidenceUsed = resolution.EvidenceUsed,
            ContextModeHint = NormalizeContextModeHint(resolution.ContextMode)
        };

    private static string BuildResolutionTraceText(ConversationContextResolution resolution)
    {
        var snapshot = resolution.Snapshot;
        return
            $"OriginalRequest: {resolution.OriginalRequest}\n" +
            $"EffectiveRequest: {resolution.EffectiveRequest}\n" +
            $"ContextMode: {resolution.ContextMode}\n" +
            $"RequestKind: {resolution.RequestKind}\n" +
            $"NeedsClarification: {resolution.NeedsClarification}\n" +
            $"RequiresCodeEvidence: {resolution.RequiresCodeEvidence}\n" +
            $"AllowsTicketCreation: {resolution.AllowsTicketCreation}\n" +
            $"EvidenceUsed: {string.Join(", ", resolution.EvidenceUsed)}\n" +
            $"Reason: {resolution.Reason}\n" +
            $"ActiveTopic: {snapshot?.ActiveTopic}\n" +
            $"PendingDecision: {snapshot?.PendingDecision}\n" +
            $"LastRecommendation: {snapshot?.LastRecommendation}";
    }

    private bool IsObviousFallback(ContextAgentRouteRequest request)
    {
        var lower = (request.UserRequest ?? string.Empty).ToLowerInvariant().Trim();
        if (lower.StartsWith("/ticket") || lower.StartsWith("/create-ticket")) return true;
        
        // If it's just "create a ticket" and we have no context, it's a fallback.
        // If we HAVE context, we want the LLM to resolve it against the active topic.
        if (string.IsNullOrWhiteSpace(request.RecentConversationSummary) && lower.StartsWith("create a ticket")) return true;
        
        return false;
    }

    private ContextAgentRouteDecision FallbackRoute(string request, string intent)
    {
        var lower = (request ?? string.Empty).ToLowerInvariant().Trim();
        
        if (lower.StartsWith("/ticket") || lower.StartsWith("/create-ticket") || lower.StartsWith("create a ticket"))
        {
            return new ContextAgentRouteDecision
            {
                OriginalUserRequest     = request ?? string.Empty,
                EffectiveWorkText       = request ?? string.Empty,
                RequestKind             = ContextRequestKind.CreateTicket,
                Confidence              = 1.0,
                Reason                  = "Deterministic pre-router: Explicit create ticket route hint.",
                AllowCodeSearch         = true,
                AllowDeepLookup         = true,
                AllowConflictAssessment = true,
                AllowConflictBlocking   = true,
                AllowTicketCreation     = true,
                RelatedTicketsAreContextOnly = false,
                ContextModeHint = "Exploration"
            };
        }

        if (HasExplicitBuildTicketIntent(lower))
        {
            return new ContextAgentRouteDecision
            {
                OriginalUserRequest     = request ?? string.Empty,
                EffectiveWorkText       = request ?? string.Empty,
                RequestKind             = ContextRequestKind.BuildTicket,
                Confidence              = 0.9,
                Reason                  = "Deterministic pre-router: Explicit build-ticket route hint.",
                AllowCodeSearch         = true,
                AllowDeepLookup         = true,
                AllowConflictAssessment = true,
                AllowConflictBlocking   = true,
                AllowTicketCreation     = true,
                RelatedTicketsAreContextOnly = false,
                ContextModeHint = "Exploration"
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
                ContextModeHint = "Exploration",
                DeepLookupTargets = IdentifyTargets(lower)
            };
        }

        if (StartsWithAny(lower, ChangePrefixes))
        {
            var kind = ContextRequestKind.ChangeImplementation;
            if (lower.StartsWith("replace")) kind = ContextRequestKind.ReplaceArchitecture;

            return new ContextAgentRouteDecision
            {
                OriginalUserRequest     = request ?? string.Empty,
                EffectiveWorkText       = request ?? string.Empty,
                RequestKind             = kind,
                Confidence              = 0.72,
                Reason                  = "Deterministic pre-router: Change route hint.",
                AllowCodeSearch         = true,
                AllowDeepLookup         = true,
                AllowConflictAssessment = false,
                AllowConflictBlocking   = false,
                AllowTicketCreation     = false,
                RelatedTicketsAreContextOnly = true,
                ContextModeHint = "Exploration"
            };
        }

        return new ContextAgentRouteDecision
        {
            OriginalUserRequest     = request ?? string.Empty,
            EffectiveWorkText       = request ?? string.Empty,
            RequestKind             = ContextRequestKind.GeneralChat,
            Confidence              = 0.74,
            Reason                  = "Deterministic pre-router: Fallback to general chat.",
            AllowCodeSearch         = true,
            AllowDeepLookup         = false,
            AllowConflictAssessment = false,
            AllowConflictBlocking   = false,
            AllowTicketCreation     = false,
            RelatedTicketsAreContextOnly = true,
            ContextModeHint = "Exploration"
        };
    }

    private ContextAgentRouteDecision ApplySafetyValidation(ContextAgentRouteDecision decision, string userRequest, List<string> overrides)
    {
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

        if (isCreateTicket && !needsClarification && decision.Confidence < 0.55)
        {
            needsClarification = true;
            overrides.Add("Create-ticket request with weak confidence. Setting NeedsClarification to true.");
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
            UsedFallbackRules = decision.UsedFallbackRules,
            UsedConversationContextResolver = decision.UsedConversationContextResolver,
            ContextModeHint = NormalizeContextModeHint(decision.ContextModeHint)
        };
    }

    private static ContextAgentRouteDecision ForceExplicitCommitGuardrails(
        ContextAgentRouteDecision decision,
        string userRequest)
    {
        var lower = (userRequest ?? string.Empty).ToLowerInvariant().Trim();
        var originalRequestKind = decision.RequestKind;

        if (DecisionRequiresExplicitLane(decision.RequestKind) && !HasExplicitRouteCommand(lower, decision.RequestKind))
        {
            decision.RequestKind = ContextRequestKind.ChangeImplementation;
            decision.ContextModeHint = "Exploration";
            decision.Confidence = Math.Max(decision.Confidence, 0.72);
            decision.AllowConflictAssessment = false;
            decision.AllowConflictBlocking = false;
            decision.AllowTicketCreation = false;
            decision.RelatedTicketsAreContextOnly = true;
            decision.Reason = string.IsNullOrWhiteSpace(decision.Reason)
                ? $"Demoted {originalRequestKind}: explicit lane-lock text missing."
                : $"{decision.Reason} | Demoted {originalRequestKind}: explicit lane-lock text missing.";
        }

        if ((decision.RequestKind == ContextRequestKind.ChangeImplementation ||
             decision.RequestKind == ContextRequestKind.ReplaceArchitecture) &&
            string.IsNullOrWhiteSpace(decision.ContextModeHint))
        {
            decision.ContextModeHint = "Exploration";
        }

        decision.ContextModeHint = NormalizeContextModeHint(decision.ContextModeHint);
        return decision;
    }

    private static bool DecisionRequiresExplicitLane(ContextRequestKind requestKind) =>
        requestKind is
            ContextRequestKind.BuildTicket or
            ContextRequestKind.CreateTicket or
            ContextRequestKind.CreateTicketsFromDiscussion;

    private static bool HasExplicitRouteCommand(string lower, ContextRequestKind kind) =>
        kind switch
        {
            ContextRequestKind.CreateTicket or ContextRequestKind.CreateTicketsFromDiscussion => HasExplicitTicketRouteIntent(lower),
            ContextRequestKind.BuildTicket => HasExplicitBuildTicketIntent(lower),
            _ => false
        };

    private static bool HasExplicitTicketRouteIntent(string lower)
    {
        if (string.IsNullOrWhiteSpace(lower))
            return false;

        if (ContainsAny(lower, TicketCommandPrefixes))
            return true;

        if ((ContainsWord(lower, "make") || ContainsWord(lower, "turn") || ContainsWord(lower, "convert")) &&
            ContainsAny(lower, ["a ticket", "this into a ticket", "to a ticket"]))
        {
            return true;
        }

        if (ContainsWord(lower, "create") &&
            ContainsAny(lower, ["ticket", "work packet"]))
        {
            return true;
        }

        return false;
    }

    private static bool HasExplicitBuildTicketIntent(string lower)
    {
        return ContainsAny(lower, ["build ticket", "build me a ticket", "build a ticket", "generate a ticket", "generate tickets", "create a ticket"]);
    }

    private static string? NormalizeContextModeHint(string? hint)
    {
        if (string.Equals(hint, "Formalization", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(hint, "Confirmation", StringComparison.OrdinalIgnoreCase))
        {
            return "Exploration";
        }

        return string.IsNullOrWhiteSpace(hint) ? "Exploration" : hint;
    }

    private static bool ContainsAny(string lower, string[] terms)
    {
        return terms.Any(term => lower.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsWord(string lower, string term)
    {
        var token = term.Trim();
        if (token.Length == 0 || lower.Length == 0)
            return false;

        if (string.Equals(lower, token, StringComparison.OrdinalIgnoreCase))
            return true;

        return lower.Contains($" {token} ", StringComparison.OrdinalIgnoreCase) ||
               lower.StartsWith($"{token} ", StringComparison.OrdinalIgnoreCase) ||
               lower.EndsWith($" {token}", StringComparison.OrdinalIgnoreCase);
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
                $"ContextModeHint: {decision.ContextModeHint}\n" +
                $"UsedConversationContextResolver: {decision.UsedConversationContextResolver}\n" +
                $"\nRaw JSON:\n{rawJson}\n\nOverrides:\n{string.Join("\n", decision.SafetyOverrides)}",
            ParsedResponseSummary = $"Kind={decision.RequestKind} | Confidence={decision.Confidence} | Effective={decision.EffectiveWorkText}",
            ContextSummary = $"Kind={decision.RequestKind} | Confidence={decision.Confidence} | ModeHint={decision.ContextModeHint} | ConflictBlocking={decision.AllowConflictBlocking} | DeepLookup={decision.AllowDeepLookup} | UsedResolver={decision.UsedConversationContextResolver} | UsedLlmJudge={decision.UsedLlmJudge} | UsedFallbackRules={decision.UsedFallbackRules}",
            WasSuccessful = true
        };
        _traceService.AddTrace(trace);
    }

    private static bool StartsWithAny(string text, string[] prefixes)
    {
        return prefixes.Any(p => text.StartsWith(p + " ") || text == p);
    }
}
