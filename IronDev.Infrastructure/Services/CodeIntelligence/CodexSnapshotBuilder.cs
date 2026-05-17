using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data.Models;
using IronDev.Services;

namespace IronDev.Infrastructure.Services.CodeIntelligence;

public sealed class CodexSnapshotBuilder : ICodexSnapshotBuilder
{
    private static readonly HashSet<string> IncludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".xaml", ".csproj", ".json", ".xml", ".md", ".slnx", ".props", ".targets", ".sql"
    };

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", "packages", "node_modules", "dist", "build", "out", "target"
    };

    private readonly IProjectService _projectService;
    private readonly ICodeIndexService _codeIndexService;
    private readonly IProjectMemoryService _memoryService;
    private readonly ITicketService _ticketService;
    private readonly IProjectSemanticIndexService _semanticIndexService;
    private readonly ICodexContextQualityScorer _contextQualityScorer;
    private readonly IReadOnlyList<ILanguageSemanticIndexer> _languageIndexers;

    public CodexSnapshotBuilder(
        IProjectService projectService,
        ICodeIndexService codeIndexService,
        IProjectMemoryService memoryService,
        ITicketService ticketService,
        IProjectSemanticIndexService semanticIndexService,
        ICodexContextQualityScorer contextQualityScorer,
        IEnumerable<ILanguageSemanticIndexer> languageIndexers)
    {
        _projectService = projectService;
        _codeIndexService = codeIndexService;
        _memoryService = memoryService;
        _ticketService = ticketService;
        _semanticIndexService = semanticIndexService;
        _contextQualityScorer = contextQualityScorer;
        _languageIndexers = languageIndexers.ToList();
    }

    public async Task<CodexProjectSnapshot> BuildSnapshotAsync(
        CodexSnapshotBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await _projectService.GetByIdAsync(request.ProjectId, cancellationToken)
            ?? throw new InvalidOperationException($"Project {request.ProjectId} was not found.");

        var indexedFiles = await _codeIndexService.GetRecentFilesAsync(project.Id, request.MaxFiles, cancellationToken);
        var fileCandidates = BuildFileCandidates(project, indexedFiles, request.MaxFiles);
        var absolutePaths = fileCandidates
            .Select(f => f.AbsolutePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var semanticIndex = await BuildSemanticIndexAsync(project, cancellationToken);
        var semanticSymbols = semanticIndex.Symbols
            .Select(symbol => ToDisplaySymbol(project, symbol))
            .ToList();
        var adapterSymbols = await BuildAdapterSymbolsAsync(absolutePaths, request.MaxSymbols, cancellationToken);
        var storedSymbols = await BuildStoredSymbolsAsync(project, indexedFiles, request.MaxSymbols, cancellationToken);

        var allSymbols = semanticSymbols
            .Concat(adapterSymbols)
            .Concat(storedSymbols)
            .GroupBy(s => $"{s.LanguageId}|{s.FilePath}|{s.Kind}|{s.Name}|{s.StartLine}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(request.MaxSymbols)
            .ToList();

        var decisionsTask = _memoryService.GetRecentDecisionsAsync(project.Id, 25, cancellationToken);
        var ticketsTask = _ticketService.GetRecentTicketsAsync(project.Id, 50, cancellationToken);
        await Task.WhenAll(decisionsTask, ticketsTask);
        var decisions = (await decisionsTask).Select(ToDecisionSummary).ToList();
        var tickets = (await ticketsTask).Select(ToTicketSummary).ToList();

        var files = fileCandidates
            .Select(file => ToFileSummary(project, file, allSymbols))
            .ToList();

        var snapshot = new CodexProjectSnapshot
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            SolutionPath = FindSolutionPath(project.LocalPath),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Files = files,
            Symbols = allSymbols,
            Decisions = decisions,
            ExistingTickets = tickets,
            LanguageQuality = MergeLanguageQuality(BuildLanguageQuality(files, allSymbols), semanticIndex.LanguageQuality),
            SemanticWarnings = semanticIndex.Warnings
        };

        var quality = _contextQualityScorer.Score(snapshot);

        return new CodexProjectSnapshot
        {
            ProjectId = snapshot.ProjectId,
            ProjectName = snapshot.ProjectName,
            SolutionPath = snapshot.SolutionPath,
            CreatedAtUtc = snapshot.CreatedAtUtc,
            Files = snapshot.Files,
            Symbols = snapshot.Symbols,
            Decisions = snapshot.Decisions,
            ExistingTickets = snapshot.ExistingTickets,
            LanguageQuality = snapshot.LanguageQuality,
            SemanticWarnings = snapshot.SemanticWarnings,
            ContextQualityScore = quality.Score,
            MissingContextReasons = quality.MissingContextReasons
        };
    }

    private async Task<SemanticIndex> BuildSemanticIndexAsync(Project project, CancellationToken cancellationToken)
    {
        var semanticPath = FindSemanticEntryPath(project.LocalPath);
        if (string.IsNullOrWhiteSpace(semanticPath))
        {
            return new SemanticIndex
            {
                RootPath = project.LocalPath ?? string.Empty,
                IndexedAtUtc = DateTimeOffset.UtcNow,
                Warnings = ["No solution or C# project file was found for Roslyn semantic indexing."]
            };
        }

        return await _semanticIndexService.IndexProjectAsync(semanticPath, cancellationToken);
    }

    private async Task<IReadOnlyList<SemanticSymbolInfo>> BuildAdapterSymbolsAsync(
        IReadOnlyList<string> absolutePaths,
        int maxSymbols,
        CancellationToken cancellationToken)
    {
        var symbols = new List<SemanticSymbolInfo>();

        foreach (var indexer in _languageIndexers)
        {
            var paths = absolutePaths.Where(indexer.CanHandle).ToList();
            if (paths.Count == 0)
                continue;

            var indexed = await indexer.IndexAsync(paths, cancellationToken);
            symbols.AddRange(indexed);

            if (symbols.Count >= maxSymbols)
                break;
        }

        return symbols.Take(maxSymbols).ToList();
    }

    private async Task<IReadOnlyList<SemanticSymbolInfo>> BuildStoredSymbolsAsync(
        Project project,
        IReadOnlyList<ProjectFile> indexedFiles,
        int maxSymbols,
        CancellationToken cancellationToken)
    {
        var symbols = new List<SemanticSymbolInfo>();

        foreach (var file in indexedFiles)
        {
            if (symbols.Count >= maxSymbols)
                break;

            var entries = await _codeIndexService.GetSymbolsAsync(file.Id, cancellationToken);
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.SymbolName))
                    continue;

                symbols.Add(new SemanticSymbolInfo
                {
                    LanguageId = GetLanguageId(file.FilePath),
                    FilePath = ToDisplayPath(project, file.FilePath),
                    Name = entry.SymbolName,
                    Kind = entry.SymbolType ?? "symbol",
                    ContainerName = entry.Namespace,
                    Signature = FirstLine(entry.ChunkText),
                    Confidence = "Low"
                });
            }
        }

        return symbols.Take(maxSymbols).ToList();
    }

    private static IReadOnlyList<FileCandidate> BuildFileCandidates(
        Project project,
        IReadOnlyList<ProjectFile> indexedFiles,
        int maxFiles)
    {
        var candidates = new Dictionary<string, FileCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in indexedFiles.Take(maxFiles))
        {
            var absolutePath = ToAbsolutePath(project.LocalPath, file.FilePath);
            candidates[ToDisplayPath(project, file.FilePath)] = new FileCandidate(
                file.Id,
                ToDisplayPath(project, file.FilePath),
                absolutePath,
                file.FileExtension,
                file.LastIndexedDate);
        }

        if (!string.IsNullOrWhiteSpace(project.LocalPath) && Directory.Exists(project.LocalPath))
        {
            foreach (var absolutePath in EnumerateProjectFiles(project.LocalPath).Take(maxFiles))
            {
                var displayPath = ToDisplayPath(project, absolutePath);
                candidates.TryAdd(displayPath, new FileCandidate(
                    null,
                    displayPath,
                    absolutePath,
                    Path.GetExtension(absolutePath),
                    null));
            }
        }

        return candidates.Values.Take(maxFiles).ToList();
    }

    private static IndexedFileSummary ToFileSummary(
        Project project,
        FileCandidate file,
        IReadOnlyList<SemanticSymbolInfo> symbols)
    {
        var displayPath = ToDisplayPath(project, file.DisplayPath);
        var symbolCount = symbols.Count(s =>
            string.Equals(ToDisplayPath(project, s.FilePath), displayPath, StringComparison.OrdinalIgnoreCase));
        var size = File.Exists(file.AbsolutePath) ? new FileInfo(file.AbsolutePath).Length : (long?)null;

        return new IndexedFileSummary
        {
            IndexedFileId = file.IndexedFileId,
            FilePath = displayPath,
            Extension = string.IsNullOrWhiteSpace(file.Extension) ? Path.GetExtension(displayPath) : file.Extension,
            LanguageId = GetLanguageId(displayPath),
            SizeBytes = size,
            LastIndexedUtc = file.LastIndexedUtc,
            SymbolCount = symbolCount,
            Confidence = GetLanguageConfidence(GetLanguageId(displayPath))
        };
    }

    private static IReadOnlyList<LanguageContextQuality> BuildLanguageQuality(
        IReadOnlyList<IndexedFileSummary> files,
        IReadOnlyList<SemanticSymbolInfo> symbols)
    {
        return files
            .GroupBy(f => f.LanguageId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var symbolCount = symbols.Count(s => string.Equals(s.LanguageId, group.Key, StringComparison.OrdinalIgnoreCase));
                return new LanguageContextQuality
                {
                    LanguageId = group.Key,
                    Confidence = GetLanguageConfidence(group.Key),
                    FileCount = group.Count(),
                    SymbolCount = symbolCount,
                    Notes = GetLanguageNotes(group.Key)
                };
            })
            .OrderByDescending(q => q.SymbolCount)
            .ThenByDescending(q => q.FileCount)
            .ToList();
    }

    private static IReadOnlyList<LanguageContextQuality> MergeLanguageQuality(
        IReadOnlyList<LanguageContextQuality> structuralQuality,
        IReadOnlyList<LanguageContextQuality> semanticQuality)
    {
        var merged = structuralQuality
            .ToDictionary(q => q.LanguageId, StringComparer.OrdinalIgnoreCase);

        foreach (var semantic in semanticQuality)
        {
            if (!merged.TryGetValue(semantic.LanguageId, out var existing) || semantic.SymbolCount >= existing.SymbolCount)
                merged[semantic.LanguageId] = semantic;
        }

        return merged.Values
            .OrderByDescending(q => ConfidenceRank(q.Confidence))
            .ThenByDescending(q => q.SymbolCount)
            .ThenByDescending(q => q.FileCount)
            .ToList();
    }

    private static IEnumerable<string> EnumerateProjectFiles(string rootPath)
    {
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            string[] directories;
            try { directories = Directory.GetDirectories(current); }
            catch { continue; }

            foreach (var directory in directories)
            {
                if (!ExcludedDirectories.Contains(Path.GetFileName(directory)))
                    stack.Push(directory);
            }

            string[] files;
            try { files = Directory.GetFiles(current); }
            catch { continue; }

            foreach (var file in files)
            {
                if (IncludedExtensions.Contains(Path.GetExtension(file)))
                    yield return file;
            }
        }
    }

    private static string FindSolutionPath(string? localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath))
            return string.Empty;

        return Directory
            .EnumerateFiles(localPath, "*.sln*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string FindSemanticEntryPath(string? localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath) || !Directory.Exists(localPath))
            return string.Empty;

        var solution = FindSolutionPath(localPath);
        if (!string.IsNullOrWhiteSpace(solution))
            return solution;

        return Directory
            .EnumerateFiles(localPath, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => ExcludedDirectories.Contains(part)))
            .FirstOrDefault() ?? string.Empty;
    }

    private static int ConfidenceRank(string confidence)
        => confidence switch
        {
            "High" => 3,
            "Medium" => 2,
            "Low" => 1,
            _ => 0
        };

    private static ProjectDecisionSummary ToDecisionSummary(ProjectDecision decision)
        => new()
        {
            Id = decision.Id,
            Title = decision.Title,
            Status = decision.Status,
            Category = decision.Category,
            DetailPreview = Preview(decision.Detail, 240)
        };

    private static SemanticSymbolInfo ToDisplaySymbol(Project project, SemanticSymbolInfo symbol)
        => new()
        {
            LanguageId = symbol.LanguageId,
            FilePath = ToDisplayPath(project, symbol.FilePath),
            Name = symbol.Name,
            Kind = symbol.Kind,
            FullyQualifiedName = symbol.FullyQualifiedName,
            ContainerName = symbol.ContainerName,
            Signature = symbol.Signature,
            DocumentationComment = symbol.DocumentationComment,
            StartLine = symbol.StartLine,
            EndLine = symbol.EndLine,
            Confidence = symbol.Confidence
        };

    private static ProjectTicketSummary ToTicketSummary(ProjectTicket ticket)
        => new()
        {
            Id = ticket.Id,
            Title = ticket.Title,
            Status = ticket.Status,
            Priority = ticket.Priority,
            TicketType = ticket.TicketType,
            SummaryPreview = Preview(ticket.Summary ?? ticket.Problem ?? ticket.Content, 240)
        };

    private static string GetLanguageId(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".xaml" => "xaml",
            ".sql" => "sql",
            ".json" or ".xml" or ".csproj" or ".props" or ".targets" or ".slnx" => "config",
            ".md" => "markdown",
            _ => "text"
        };
    }

    private static string GetLanguageConfidence(string languageId)
        => languageId switch
        {
            "csharp" => "Medium",
            "xaml" => "Medium",
            "config" => "Low",
            "markdown" => "Low",
            "sql" => "Low",
            _ => "Low"
        };

    private static string GetLanguageNotes(string languageId)
        => languageId switch
        {
            "csharp" => "Roslyn semantic extraction is active with structural fallback.",
            "xaml" => "Structural XAML extraction covers named elements and bindings.",
            "config" => "Config files are included as context, not deep semantic symbols.",
            "markdown" => "Markdown is included as project context.",
            "sql" => "SQL files are included as text/config context in this slice.",
            _ => "Text-aware only."
        };

    private static string ToAbsolutePath(string? rootPath, string filePath)
    {
        if (Path.IsPathRooted(filePath))
            return filePath;

        return string.IsNullOrWhiteSpace(rootPath)
            ? filePath
            : Path.Combine(rootPath, filePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string ToDisplayPath(Project project, string filePath)
    {
        if (!Path.IsPathRooted(filePath) || string.IsNullOrWhiteSpace(project.LocalPath))
            return filePath.Replace('\\', '/');

        try
        {
            return Path.GetRelativePath(project.LocalPath, filePath).Replace('\\', '/');
        }
        catch
        {
            return filePath.Replace('\\', '/');
        }
    }

    private static string FirstLine(string value)
        => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;

    private static string Preview(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = string.Join(" ", value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    private sealed record FileCandidate(
        long? IndexedFileId,
        string DisplayPath,
        string AbsolutePath,
        string Extension,
        DateTime? LastIndexedUtc);
}
