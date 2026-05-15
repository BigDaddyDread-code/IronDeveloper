using System;

namespace IronDev.Core.Models;

public class LlmTraceEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string FeatureName { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string CurrentUserMessage { get; set; } = string.Empty;
    public string ContextSummary { get; set; } = string.Empty;
    public int? EstimatedTokens { get; set; }
    public string RequestText { get; set; } = string.Empty;
    public string RawRequestJson { get; set; } = string.Empty;
    public string RawResponseText { get; set; } = string.Empty;
    public string ParsedResponseSummary { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string Warnings { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public bool WasSuccessful { get; set; }

    // ── Context / Navigation Metadata ─────────────────────────────────────
    public string WorkspaceName { get; set; } = string.Empty;
    public int? ProjectId { get; set; }
    public string? ChatSessionId { get; set; }
    public long? TicketId { get; set; }
    public long? PlanId { get; set; }

    // ── Target-Aware Metadata ─────────────────────────────────────────────
    public string ActiveProjectName { get; set; } = string.Empty;
    public string ActiveProjectPath { get; set; } = string.Empty;
    public bool? IsExternalProject { get; set; }

    // ── Grouping & Hierarchy ──────────────────────────────────────────────
    // Used to link multiple calls together (e.g. Iterative generator pass 1, 2, 3)
    public string? TraceGroupId { get; set; }
    public Guid? ParentTraceId { get; set; }

    // ── Token & Context Internals ─────────────────────────────────────────
    public string PromptContextPackId { get; set; } = string.Empty;
    public int? TokenUsageInput { get; set; }
    public int? TokenUsageOutput { get; set; }
    public int? TokenUsageTotal { get; set; }

    // ── Write Root Guard Diagnostics ──────────────────────────────────────
    public string AllowedWriteRoot { get; set; } = string.Empty;
    public string ProposedWritePath { get; set; } = string.Empty;
    public bool? WriteAllowed { get; set; }
    public string BlockReason { get; set; } = string.Empty;
    public bool? IsProposalOnly { get; set; }
    public int? ProposedFileCount { get; set; }
    public string ProposedFilesList { get; set; } = string.Empty;
}
