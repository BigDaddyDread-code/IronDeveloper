using System;
using System.Collections.Generic;

namespace IronDev.Core.Models;

public sealed class CodexProjectSnapshot
{
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string SolutionPath { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }

    public IReadOnlyList<IndexedFileSummary> Files { get; init; } = [];
    public IReadOnlyList<SemanticSymbolInfo> Symbols { get; init; } = [];
    public IReadOnlyList<ProjectDecisionSummary> Decisions { get; init; } = [];
    public IReadOnlyList<ProjectTicketSummary> ExistingTickets { get; init; } = [];
    public IReadOnlyList<LanguageContextQuality> LanguageQuality { get; init; } = [];
    public IReadOnlyList<string> SemanticWarnings { get; init; } = [];

    public int ContextQualityScore { get; init; }
    public IReadOnlyList<string> MissingContextReasons { get; init; } = [];

    public int FileCount => Files.Count;
    public int SemanticSymbolCount => Symbols.Count;
    public int DecisionCount => Decisions.Count;
    public int ExistingTicketCount => ExistingTickets.Count;
}

public sealed class CodexContextQualityResult
{
    public int Score { get; init; }
    public IReadOnlyList<string> MissingContextReasons { get; init; } = [];
}

public sealed class SemanticIndex
{
    public string RootPath { get; init; } = string.Empty;
    public DateTimeOffset IndexedAtUtc { get; init; }
    public IReadOnlyList<SemanticProjectInfo> Projects { get; init; } = [];
    public IReadOnlyList<SemanticSymbolInfo> Symbols { get; init; } = [];
    public IReadOnlyList<LanguageContextQuality> LanguageQuality { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class SemanticProjectInfo
{
    public string Name { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int SymbolCount { get; init; }
}

public sealed class IndexedFileSummary
{
    public long? IndexedFileId { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string Extension { get; init; } = string.Empty;
    public string LanguageId { get; init; } = "text";
    public long? SizeBytes { get; init; }
    public DateTime? LastIndexedUtc { get; init; }
    public int SymbolCount { get; init; }
    public string Confidence { get; init; } = "Low";
}

public sealed class SemanticSymbolInfo
{
    public string LanguageId { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string FullyQualifiedName { get; init; } = string.Empty;
    public string? ContainerName { get; init; }
    public string? Signature { get; init; }
    public string? DocumentationComment { get; init; }
    public int? StartLine { get; init; }
    public int? EndLine { get; init; }
    public string Confidence { get; init; } = "Medium";
}

public sealed class ProjectDecisionSummary
{
    public long Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string DetailPreview { get; init; } = string.Empty;
}

public sealed class ProjectTicketSummary
{
    public long Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string TicketType { get; init; } = string.Empty;
    public string SummaryPreview { get; init; } = string.Empty;
}

public sealed class LanguageContextQuality
{
    public string LanguageId { get; init; } = string.Empty;
    public string Confidence { get; init; } = "Low";
    public int FileCount { get; init; }
    public int SymbolCount { get; init; }
    public string Notes { get; init; } = string.Empty;
}

public sealed class CodexSnapshotBuildRequest
{
    public int ProjectId { get; init; }
    public int MaxFiles { get; init; } = 250;
    public int MaxSymbols { get; init; } = 600;
}
