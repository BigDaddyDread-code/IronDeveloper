using System;
using System.Collections.Generic;

namespace IronDev.Core.Models;

/// <summary>
/// Represents a proposed code change for a single file.
/// </summary>
public sealed class ProposedFileChange
{
    public string FilePath { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Diff { get; set; } = string.Empty;
    public bool IsNewFile { get; set; }
    public bool IsDeletion { get; set; }
    public bool IsValid { get; set; }
    public string ValidationMessage { get; set; } = string.Empty;
    public string? FullContentAfter { get; set; }
}

/// <summary>
/// Represents a full AI-generated code modification proposal.
/// </summary>
public sealed class BuilderProposal
{
    public long TicketId { get; set; }
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
    public string OriginalRequest { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public List<ProposedFileChange> Changes { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    public bool IsAllValid => Changes.TrueForAll(c => c.IsValid);

    // ── Execution State ───────────────────────────────────────────────────
    public string ApplyStatus { get; set; } = "Not Started";
    public string BuildStatus { get; set; } = "Not Started";
    public string TestStatus { get; set; } = "Not Started";

    public string? BuildOutput { get; set; }
    public string? TestOutput { get; set; }
    public TimeSpan? BuildDuration { get; set; }
    public TimeSpan? TestDuration { get; set; }
    
    public BuildArchitectureReconciliation? Reconciliation { get; set; }
}

/// <summary>
/// Status categories for project build readiness.
/// </summary>
public enum BuildReadinessStatus
{
    ReadyToBuild,
    NeedsProjectProfileUpdate,
    NeedsArchitectureDecision,
    BlockedByExistingDecision,
    BlockedByConflict,
    NeedsClarification,
    Error
}

/// <summary>
/// Represents the result of a build readiness evaluation.
/// </summary>
public sealed class BuildReadinessResult
{
    public BuildReadinessStatus Status { get; set; } = BuildReadinessStatus.ReadyToBuild;
    public string Message { get; set; } = "Ready to build.";
    public List<string> Warnings { get; set; } = new();
    public List<string> BlockingIssues { get; set; } = new();
    
    public bool IsReady => Status == BuildReadinessStatus.ReadyToBuild;
}
