using System;
using System.Collections.Generic;

namespace IronDev.Core.Models;

// ── Intent ────────────────────────────────────────────────────────────────────

public sealed class CreateTicketIntent
{
    public string Intent { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public string CommandText { get; init; } = string.Empty;
    public string WorkText { get; init; } = string.Empty;
    public long? SourceMessageId { get; init; }
    public bool RequiresClarification { get; init; }
    public IReadOnlyList<string> ClarificationQuestions { get; init; } = Array.Empty<string>();
}

// ── Routing ───────────────────────────────────────────────────────────────────

public enum ContextRequestKind
{
    GeneralChat,
    InspectCode,
    ExplainCode,
    VerifyImplementation,
    CreateTicket,
    CreateTicketsFromDiscussion,
    ChangeImplementation,
    ReplaceArchitecture,
    BuildTicket,
    ArchitectureAdvice,
    ArchitectureDecisionExploration
}

public sealed class ContextAgentRouteDecision
{
    public string OriginalUserRequest { get; set; } = string.Empty;
    public string EffectiveWorkText { get; set; } = string.Empty;
    public ContextRequestKind RequestKind { get; set; }
    public double Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    
    public bool AllowCodeSearch { get; set; }
    public bool AllowDeepLookup { get; set; }
    public bool AllowConflictAssessment { get; set; }
    public bool AllowConflictBlocking { get; set; }
    public bool AllowTicketCreation { get; set; }
    
    public bool RelatedTicketsAreContextOnly { get; set; }
    public bool NeedsClarification { get; set; }
    public IReadOnlyList<string> ClarificationQuestions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> EvidenceUsed { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Risks { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SafetyOverrides { get; set; } = Array.Empty<string>();
    public bool UsedLlmJudge { get; set; }
    public bool UsedFallbackRules { get; set; }
    
    public IReadOnlyList<DeepLookupTarget> DeepLookupTargets { get; set; } = Array.Empty<DeepLookupTarget>();
}

public sealed class DeepLookupTarget
{
    public string FilePath { get; set; } = string.Empty;
    public string SymbolName { get; set; } = string.Empty;
    public string ProofPattern { get; set; } = string.Empty;
    public bool Required { get; set; } = true;
}

public sealed class ContextAgentRouteRequest
{
    public string TraceGroupId { get; init; } = string.Empty;
    public int ProjectId { get; init; }
    public long SessionId { get; init; }
    public string UserRequest { get; init; } = string.Empty;
    public string RecentConversationSummary { get; init; } = string.Empty;
    public string InitialIntentFromPromptContextBuilder { get; init; } = string.Empty;
    public IReadOnlyList<IronDev.Data.Models.ProjectTicket> RecentTickets { get; init; } = Array.Empty<IronDev.Data.Models.ProjectTicket>();
    public IReadOnlyList<IronDev.Data.Models.ProjectDecision> RecentDecisions { get; init; } = Array.Empty<IronDev.Data.Models.ProjectDecision>();
    public IReadOnlyList<IronDev.Data.Models.ProjectRule> ProjectRules { get; init; } = Array.Empty<IronDev.Data.Models.ProjectRule>();
    public IReadOnlyList<string> RetrievedFilePaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RetrievedSymbols { get; init; } = Array.Empty<string>();
    public string? SelectedTicketTitle { get; init; }
    public string? SelectedPlanTitle { get; init; }
}

// ── Deep Code Evidence ────────────────────────────────────────────────────────

public enum DeepEvidenceType
{
    None,
    SymbolBody,
    FileWindow,
    FullSmallFile,
    PropertyDefinition,
    MethodBody
}

public enum ContextProofStatus
{
    NotProven,
    ProvenPresent,
    ProvenAbsent,
    InsufficientEvidence
}

public sealed class EvidenceProofResult
{
    public ContextProofStatus Status { get; set; } = ContextProofStatus.NotProven;
    public bool IsProven => Status == ContextProofStatus.ProvenPresent || Status == ContextProofStatus.ProvenAbsent;
    public List<string> MissingElements { get; } = new();
    public string ProofNotes { get; set; } = string.Empty;
    public bool EvidenceProofGateSkipped { get; set; }
    public string EvidenceProofGateSkipReason { get; set; } = string.Empty;
}

public sealed class DeepCodeEvidence
{
    public string FilePath { get; init; } = string.Empty;
    public string SymbolName { get; init; } = string.Empty;
    public DeepEvidenceType EvidenceType { get; init; }
    public string CodeText { get; init; } = string.Empty;
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public double Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
}

// ── Input ─────────────────────────────────────────────────────────────────────


/// <summary>
/// Request handed to IContextAgentService. Contains everything needed
/// to build, evaluate, and optionally expand context before the final answer.
/// </summary>
public sealed class ContextAgentRequest
{
    public int    ProjectId   { get; init; }
    public long   SessionId   { get; init; }
    public string UserRequest { get; init; } = string.Empty;
    public string RecentConversationSummary { get; init; } = string.Empty;

    // Optional navigation context
    public long? TicketId { get; init; }
    public long? PlanId   { get; init; }

    // Override limits per-call (null = use service defaults)
    public ContextAgentLimits? Limits { get; init; }

    // ── Intent extraction ───────────────────────────────────────────────────
    public CreateTicketIntent? CreateTicketIntent { get; init; }

    // ── Conflict assessment inputs (populated by ChatWorkspaceViewModel) ────
    /// <summary>Recent non-archived tickets for conflict detection.</summary>
    public IReadOnlyList<IronDev.Data.Models.ProjectTicket> RecentTickets { get; init; }
        = Array.Empty<IronDev.Data.Models.ProjectTicket>();
    /// <summary>Recent project decisions for conflict detection.</summary>
    public IReadOnlyList<IronDev.Data.Models.ProjectDecision> RecentDecisions { get; init; }
        = Array.Empty<IronDev.Data.Models.ProjectDecision>();
    /// <summary>Project rules for enforcement-level conflict detection.</summary>
    public IReadOnlyList<IronDev.Data.Models.ProjectRule> ProjectRules { get; init; }
        = Array.Empty<IronDev.Data.Models.ProjectRule>();
}

// ── Hard limits ───────────────────────────────────────────────────────────────

/// <summary>
/// Hard limits that control how much the Context Agent is allowed to expand
/// context in a single run. Keeps the agent bounded and deterministic.
/// </summary>
public sealed class ContextAgentLimits
{
    /// <summary>Maximum number of expansion rounds (default: 1).</summary>
    public int MaxExpansionRounds   { get; init; } = 1;
    /// <summary>Maximum tool calls per round (default: 2).</summary>
    public int MaxToolCallsPerRound { get; init; } = 2;
    /// <summary>Maximum code search queries per round (default: 3).</summary>
    public int MaxCodeSearchQueries { get; init; } = 3;
    /// <summary>Maximum distinct files added to expanded context (default: 5).</summary>
    public int MaxAddedFiles        { get; init; } = 5;
    /// <summary>Maximum snippets added to expanded context (default: 5).</summary>
    public int MaxSnippets          { get; init; } = 5;
    /// <summary>Maximum distinct symbols added (default: 20).</summary>
    public int MaxSymbols           { get; init; } = 20;
    /// <summary>Approximate character budget for the final prompt (default: 32,000 chars ≈ ~8k tokens).</summary>
    public int MaxContextChars      { get; init; } = 32_000;
}

// ── Sufficiency check ─────────────────────────────────────────────────────────

/// <summary>
/// Parsed response from the LLM sufficiency-check step.
/// The LLM is asked to return JSON that matches this shape.
/// </summary>
public sealed class ContextSufficiencyResult
{
    /// <summary>True when the LLM judges the initial context to be enough.</summary>
    public bool   IsSufficient { get; init; }

    /// <summary>0–10 confidence score from the LLM.</summary>
    public int    Confidence   { get; init; }

    /// <summary>Human-readable reason from the LLM.</summary>
    public string Reason       { get; init; } = string.Empty;

    /// <summary>Code search queries to run during context expansion.</summary>
    public IReadOnlyList<string> CodeSearchQueries      { get; init; } = Array.Empty<string>();

    /// <summary>Questions the user must answer before the agent can continue.</summary>
    public IReadOnlyList<string> ClarificationQuestions { get; init; } = Array.Empty<string>();

    /// <summary>True when the LLM response could not be parsed as valid JSON.</summary>
    public bool ParseError   { get; init; }
    public string ParseErrorMessage { get; init; } = string.Empty;
}

// ── Expanded evidence ─────────────────────────────────────────────────────────

/// <summary>
/// A single piece of code evidence retrieved during a context expansion tool call.
/// </summary>
public sealed class CodeEvidence
{
    public string FilePath   { get; init; } = string.Empty;
    public string SymbolName { get; init; } = string.Empty;
    public string Snippet    { get; init; } = string.Empty;
    /// <summary>Which search query retrieved this evidence.</summary>
    public string RetrievedByQuery { get; init; } = string.Empty;
    /// <summary>Human-readable reason this file was selected over alternatives.</summary>
    public string SelectionReason  { get; init; } = string.Empty;
}

// ── Retrieval trace diagnostics ───────────────────────────────────────────────

/// <summary>
/// Rich diagnostic summary of a single retrieval tool call.
/// Emitted into the ToolResult LlmTraceEntry so the LLM Console and
/// ExportTrace give full visibility into what the agent retrieved, filtered,
/// and why.
/// </summary>
public sealed class RetrievalTraceSummary
{
    public string   OriginalQuery        { get; init; } = string.Empty;
    public IReadOnlyList<string> ExpandedQueries { get; init; } = Array.Empty<string>();
    public int      RawResultCount       { get; init; }
    public int      AfterFilterCount     { get; init; }
    public int      ExcludedTestCount    { get; init; }
    public int      AddedToEvidenceCount { get; init; }
    public IReadOnlyList<SelectedEvidenceEntry> SelectedFiles { get; init; } = Array.Empty<SelectedEvidenceEntry>();

    public string ToTraceText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Original query:       {OriginalQuery}");
        if (ExpandedQueries.Count > 0)
            sb.AppendLine($"Expanded to:          {string.Join(", ", ExpandedQueries)}");
        sb.AppendLine($"Raw results:          {RawResultCount}");
        sb.AppendLine($"After production filter: {AfterFilterCount}");
        sb.AppendLine($"Excluded test files:  {ExcludedTestCount}");
        sb.AppendLine($"Added to evidence:    {AddedToEvidenceCount}");
        if (SelectedFiles.Count > 0)
        {
            sb.AppendLine("Selected evidence:");
            foreach (var s in SelectedFiles)
                sb.AppendLine($"  [{s.FilePath}] {s.Symbol} — {s.Reason}");
        }
        return sb.ToString().TrimEnd();
    }
}

public sealed class SelectedEvidenceEntry
{
    public string FilePath { get; init; } = string.Empty;
    public string Symbol   { get; init; } = string.Empty;
    public string Reason   { get; init; } = string.Empty;
}

// ── Final result ──────────────────────────────────────────────────────────────

/// <summary>
/// The complete output of a single ContextAgent run.
/// The caller uses <see cref="FinalPrompt"/> to call the real LLM.
/// </summary>
public sealed class ContextAgentResult
{
    // ── The assembled prompt ──────────────────────────────────────────────
    /// <summary>
    /// The final, expanded prompt that should be sent to the LLM for the answer.
    /// Null when <see cref="IsClarificationRequired"/> is true.
    /// </summary>
    public string? FinalPrompt { get; init; }

    // ── Clarification path ────────────────────────────────────────────────
    /// <summary>True when the agent needs user input before it can continue.</summary>
    public bool IsClarificationRequired { get; init; }

    /// <summary>Questions to show the user when <see cref="IsClarificationRequired"/> is true.</summary>
    public IReadOnlyList<string> ClarificationQuestions { get; init; } = Array.Empty<string>();

    // ── Diagnostics ───────────────────────────────────────────────────────
    public string TraceGroupId      { get; init; } = string.Empty;
    public string ContextSummary    { get; init; } = string.Empty;
    public bool   WasExpanded       { get; init; }
    public int    ExpandedFileCount { get; init; }
    public int    ExpandedSnippetCount { get; init; }
    public bool   WasSuccessful     { get; init; }
    public string Warnings          { get; init; } = string.Empty;
    public IReadOnlyList<CodeEvidence> Evidence { get; init; } = Array.Empty<CodeEvidence>();
    public IReadOnlyList<TicketCandidate> TicketCandidates { get; init; } = Array.Empty<TicketCandidate>();

    // ── Evidence rule ─────────────────────────────────────────────────────
    /// <summary>
    /// True if code evidence was retrieved and injected into the final prompt.
    /// Used to enforce the evidence rule: if the final answer claims code was
    /// inspected, this must be true.
    /// </summary>
    public bool HasCodeEvidence => Evidence.Count > 0;

    // ── Conflict assessment ────────────────────────────────────────────────────
    /// <summary>
    /// Set when the agent ran a conflict assessment stage.
    /// Null when the agent did not detect a ticket-creation intent or when
    /// the conflict assessment is not enabled.
    /// </summary>
    public TicketConflictAssessment? ConflictAssessment { get; init; }
    public bool EvidenceProofGateSkipped { get; init; }
    public string EvidenceProofGateSkipReason { get; init; } = string.Empty;
}

public sealed class TicketCandidate
{
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string SuggestedDomain { get; set; } = string.Empty;
    public string ExistingRelatedWork { get; set; } = string.Empty;
}

// ── Stage names (trace FeatureName values) ───────────────────────────────────

/// <summary>
/// Canonical feature name constants used in LlmTraceEntry.FeatureName for
/// each stage of the Context Agent pipeline. Keeps trace filtering consistent.
/// </summary>
public static class ContextAgentStage
{
    public const string InitialContext        = "ContextAgent.InitialContext";
    public const string SufficiencyCheck      = "ContextAgent.SufficiencyCheck";
    public const string ToolCallSearch        = "ContextAgent.ToolCall.SearchCodeIndex";
    public const string ToolResultSearch      = "ContextAgent.ToolResult.SearchCodeIndex";
    public const string ClarificationRequired = "ContextAgent.ClarificationRequired";
    public const string ConflictAssessment    = "ContextAgent.ConflictAssessment";
    public const string FinalAnswer           = "ContextAgent.FinalAnswer";
    public const string IntentCreateTicket    = "ChatIntent.CreateTicket";
    public const string DeepCodeEvidence      = "ContextAgent.DeepCodeEvidence";
    public const string RouteDecision         = "ContextAgent.RouteDecision";
    public const string RouteGateDecision     = "ContextAgent.RouteGateDecision";
    public const string EvidenceProofGate     = "ContextAgent.EvidenceProofGate";
    public const string EvidenceProofGateSkipped = "ContextAgent.EvidenceProofGateSkipped";
    public const string DirectDeepLookupTargets = "ContextAgent.DirectDeepLookupTargets";
    public const string CandidateExtraction   = "ContextAgent.CandidateExtraction";
}

// ── Conflict assessment models ────────────────────────────────────────────────

/// <summary>
/// Classification of how a requested ticket relates to existing project work.
/// </summary>
public static class ConflictClassification
{
    /// <summary>No conflict — safe to create as-is.</summary>
    public const string Compatible        = "Compatible";
    /// <summary>Exact or near-exact duplicate of an existing ticket.</summary>
    public const string Duplicate         = "Duplicate";
    /// <summary>Same domain, significant overlap but not identical scope.</summary>
    public const string Overlaps          = "Overlaps";
    /// <summary>Same domain but a directly opposing technical approach.</summary>
    public const string Conflicts         = "Conflicts";
    /// <summary>User explicitly wants to supersede an existing approach.</summary>
    public const string ReplacesExisting  = "ReplacesExisting";
    /// <summary>An architectural decision should be made before any ticket is created.</summary>
    public const string NeedsDecision     = "NeedsDecision";
    /// <summary>Too ambiguous to classify — clarification required.</summary>
    public const string NeedsClarification = "NeedsClarification";
}

/// <summary>
/// Recommended action the caller (or UI) should take based on the assessment.
/// </summary>
public static class RecommendedAction
{
    public const string CreateSeparate   = "CreateSeparate";
    public const string UpdateExisting   = "UpdateExisting";
    public const string AskClarification = "AskClarification";
    public const string CreateSpike      = "CreateSpike";
    public const string ReplaceExisting  = "ReplaceExisting";
    public const string CreateDecision   = "CreateDecision";
    public const string Cancel           = "Cancel";
}

/// <summary>
/// A single existing ticket or plan that overlaps / conflicts with the requested work.
/// </summary>
public sealed class RelatedTicketMatch
{
    public long   TicketId     { get; init; }
    public string Title        { get; init; } = string.Empty;
    public string Status       { get; init; } = string.Empty;
    public string OverlapReason { get; init; } = string.Empty;
    /// <summary>0.0–1.0 confidence that this ticket is actually related.</summary>
    public double Confidence   { get; init; }
}

/// <summary>
/// Full conflict assessment produced for a ticket-creation request.
/// Encapsulates everything needed for the UI to decide whether to proceed.
/// </summary>
public sealed class TicketConflictAssessment
{
    public bool   HasConflict        { get; init; }
    public string Classification     { get; init; } = ConflictClassification.Compatible;
    /// <summary>The technical/functional domain detected in the request (e.g. "REST authentication").</summary>
    public string Domain             { get; init; } = string.Empty;
    /// <summary>Technical approach detected in existing tickets/decisions (e.g. "OAuth").</summary>
    public string ExistingApproach   { get; init; } = string.Empty;
    /// <summary>Technical approach in the new request (e.g. "API key authentication").</summary>
    public string RequestedApproach  { get; init; } = string.Empty;

    public IReadOnlyList<RelatedTicketMatch> RelatedTickets { get; init; } = Array.Empty<RelatedTicketMatch>();
    /// <summary>Decisions that are relevant to or contradicted by the request.</summary>
    public IReadOnlyList<string> ConflictingDecisions { get; init; } = Array.Empty<string>();

    public string RecommendedAction  { get; init; } = Models.RecommendedAction.CreateSeparate;
    /// <summary>Clarification questions to ask the user before creating the ticket.</summary>
    public IReadOnlyList<string> Questions { get; init; } = Array.Empty<string>();

    // ── Blocking rules ────────────────────────────────────────────────────────

    /// <summary>
    /// True when the assessment result is strong enough to block silent ticket creation.
    /// Only Compatible and CreateSeparate are allowed to proceed without user confirmation.
    /// </summary>
    public bool BlocksTicketCreation =>
        Classification is ConflictClassification.Duplicate
                       or ConflictClassification.Overlaps
                       or ConflictClassification.Conflicts
                       or ConflictClassification.ReplacesExisting
                       or ConflictClassification.NeedsDecision
                       or ConflictClassification.NeedsClarification;

    public string ToTraceText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"HasConflict:      {HasConflict}");
        sb.AppendLine($"Classification:   {Classification}");
        sb.AppendLine($"Domain:           {Domain}");
        if (!string.IsNullOrWhiteSpace(ExistingApproach))
            sb.AppendLine($"ExistingApproach: {ExistingApproach}");
        if (!string.IsNullOrWhiteSpace(RequestedApproach))
            sb.AppendLine($"RequestedApproach:{RequestedApproach}");
        sb.AppendLine($"RelatedTickets:   {RelatedTickets.Count}");
        foreach (var t in RelatedTickets)
            sb.AppendLine($"  [{t.TicketId}] {t.Title} ({t.Status}) — {t.OverlapReason} ({t.Confidence:P0})");
        if (ConflictingDecisions.Count > 0)
        {
            sb.AppendLine($"ConflictingDecisions: {ConflictingDecisions.Count}");
            foreach (var d in ConflictingDecisions)
                sb.AppendLine($"  {d}");
        }
        sb.AppendLine($"Recommended:      {RecommendedAction}");
        sb.AppendLine($"Blocks creation:  {BlocksTicketCreation}");
        if (Questions.Count > 0)
        {
            sb.AppendLine("Questions:");
            foreach (var q in Questions)
                sb.AppendLine($"  - {q}");
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Input context passed to IContextConflictService.
/// Contains recent tickets, decisions, and rules for the conflict assessment.
/// </summary>
public sealed class ConflictAssessmentContext
{
    public string UserRequest { get; init; } = string.Empty;
    public IReadOnlyList<IronDev.Data.Models.ProjectTicket> RecentTickets { get; init; }
        = Array.Empty<IronDev.Data.Models.ProjectTicket>();
    public IReadOnlyList<IronDev.Data.Models.ProjectDecision> RecentDecisions { get; init; }
        = Array.Empty<IronDev.Data.Models.ProjectDecision>();
    public IReadOnlyList<IronDev.Data.Models.ProjectRule> ProjectRules { get; init; }
        = Array.Empty<IronDev.Data.Models.ProjectRule>();
}
