namespace IronDev.Core.Builder;

/// <summary>
/// An AI-generated ticket draft.
/// Not persisted until the user explicitly approves via ApproveDraftAsync.
///
/// Tests sub-fields (UnitTests … BuildValidation) are transient.
/// They are serialized into TechnicalNotes using the ## section-header
/// convention before SaveTicketAsync is called.
/// </summary>
public sealed class DraftTicket
{
    // ── Source linkage (memory-only in Phase 1–3) ──────────────────────────
    // TODO: persist SourceChatSessionId when schema supports a LinkedChatSessionId column.
    public long   SourceChatSessionId { get; set; }
    public long   SourceMessageId     { get; set; }
    public string SourceMessageText   { get; set; } = string.Empty;

    // ── Core ticket fields (map 1:1 to ProjectTicket columns) ─────────────
    public string  Title              { get; set; } = string.Empty;
    public string  TicketType         { get; set; } = "Task";
    public string  Priority           { get; set; } = "Medium";
    public string  Status             { get; set; } = "Draft";
    public string  Summary            { get; set; } = string.Empty;
    public string? Background         { get; set; }   // UI label: "Requirements"
    public string? AcceptanceCriteria { get; set; }
    public string? LinkedFilePaths    { get; set; } 
    public string? LinkedSymbols      { get; set; }
    public string? ImplementationPlan { get; set; }

    // ── Tests sub-fields (transient — packed into TechnicalNotes on save) ─
    public string UnitTests        { get; set; } = string.Empty;
    public string IntegrationTests { get; set; } = string.Empty;
    public string ManualTests      { get; set; } = string.Empty;
    public string RegressionTests  { get; set; } = string.Empty;
    public string BuildValidation  { get; set; } = string.Empty;

    // ── Generation metadata ────────────────────────────────────────────────
    /// <summary>True if this draft was produced by a service call (stub or LLM).</summary>
    public bool   IsGenerated    { get; set; }
    /// <summary>Human-readable note about how the draft was generated.</summary>
    public string GenerationNote { get; set; } = string.Empty;
}
