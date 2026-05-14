using System;
using System.Collections.Generic;

namespace IronDev.Core.Models;

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

    // Optional navigation context
    public long? TicketId { get; init; }
    public long? PlanId   { get; init; }

    // Override limits per-call (null = use service defaults)
    public ContextAgentLimits? Limits { get; init; }
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

    // ── Evidence rule ─────────────────────────────────────────────────────
    /// <summary>
    /// True if code evidence was retrieved and injected into the final prompt.
    /// Used to enforce the evidence rule: if the final answer claims code was
    /// inspected, this must be true.
    /// </summary>
    public bool HasCodeEvidence => Evidence.Count > 0;
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
    public const string FinalAnswer           = "ContextAgent.FinalAnswer";
}
