using System;
using System.Collections.Generic;

namespace IronDev.Core.Builder;

// ── Context assembled before the AI is called ─────────────────────────────

public sealed class TicketBuildContext
{
    public int    ProjectId    { get; set; }
    public long   TicketId     { get; set; }
    public string ProjectName  { get; set; } = "";
    public string ProjectPath  { get; set; } = "";
    public string BuildCommand { get; set; } = "dotnet build";

    // Ticket fields
    public string  TicketTitle               { get; set; } = "";
    public string  TicketSummary             { get; set; } = "";
    public string? TicketAcceptanceCriteria  { get; set; }
    public string? TicketImplementationNotes { get; set; }
    public string? TicketBackground          { get; set; }
    public string? TicketProblem             { get; set; }

    // Linked plan
    public string? PlanTitle         { get; set; }
    public string? PlanGoal          { get; set; }
    public string? PlanSteps         { get; set; }
    public string? PlanAffectedFiles { get; set; }
    public string? PlanRisksNotes    { get; set; }

    // Memory / context
    public IReadOnlyList<string> Decisions         { get; set; } = [];
    public IReadOnlyList<string> AffectedFiles     { get; set; } = [];
    public IReadOnlyList<string> RetrievedSnippets { get; set; } = [];
    public IReadOnlyList<string> PastBuildFailures { get; set; } = [];
}

// ── AI-produced proposal (not yet applied) ────────────────────────────────

public sealed class FileChangeProposal
{
    public string FilePath      { get; set; } = "";
    public string ChangeReason  { get; set; } = "";
    public string BeforeSnippet { get; set; } = "";
    public string AfterSnippet  { get; set; } = "";
    /// <summary>Unified diff format. May be empty in Phase 1.</summary>
    public string Patch         { get; set; } = "";
}

public sealed class CodeChangeProposal
{
    public long   TicketId    { get; set; }
    public string Summary     { get; set; } = "";
    public string RiskNotes   { get; set; } = "";
    public string TestPlan    { get; set; } = "";
    public List<FileChangeProposal> FileChanges { get; set; } = [];
}

// ── Review/approval surface ───────────────────────────────────────────────

public sealed class TicketBuildPreview
{
    public long               TicketId       { get; set; }
    public string             TicketTitle    { get; set; } = "";
    public CodeChangeProposal Proposal       { get; set; } = new();
    public string             ContextSummary { get; set; } = "";

    public bool IsEmpty => Proposal.FileChanges.Count == 0;
}

/// <summary>Represents the developer's explicit approval to apply a proposal.</summary>
public sealed class TicketBuildApproval
{
    public int               ProjectId        { get; set; }
    public long              TicketId         { get; set; }
    public string            ProjectPath      { get; set; } = "";
    public string            BuildCommand     { get; set; } = "dotnet build";
    public CodeChangeProposal ApprovedProposal { get; set; } = new();
}

// ── Post-apply build result ───────────────────────────────────────────────

public sealed class TicketBuildResult
{
    public long          TicketId        { get; set; }
    public bool          PatchSucceeded  { get; set; }
    public bool          BuildSucceeded  { get; set; }
    public string        BuildOutput     { get; set; } = "";
    public string        BuildError      { get; set; } = "";
    public int           ExitCode        { get; set; }
    public List<string>  FilesChanged    { get; set; } = [];
    /// <summary>Set when an orchestration-level error occurs (not a build failure).</summary>
    public string        ErrorMessage    { get; set; } = "";
    public DateTime      CompletedUtc    { get; set; }
}

// ── Service result types ──────────────────────────────────────────────────

public sealed class DotNetBuildResult
{
    public bool     Succeeded      { get; set; }
    public int      ExitCode       { get; set; }
    public string   StandardOutput { get; set; } = "";
    public string   StandardError  { get; set; } = "";
    public DateTime StartedUtc     { get; set; }
    public DateTime FinishedUtc    { get; set; }
    public TimeSpan Elapsed        => FinishedUtc - StartedUtc;
}

public sealed class GitStatus
{
    public bool   HasUncommittedChanges { get; set; }
    public string StatusOutput          { get; set; } = "";
}

public sealed class PatchApplyResult
{
    public bool         Succeeded    { get; set; }
    public List<string> FilesWritten { get; set; } = [];
    public string       ErrorMessage { get; set; } = "";

    public static PatchApplyResult Success(List<string> files) =>
        new() { Succeeded = true, FilesWritten = files };

    public static PatchApplyResult Failure(string error) =>
        new() { Succeeded = false, ErrorMessage = error };
}
