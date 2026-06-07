using System.Security.Cryptography;
using System.Text.Json;

using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspaceDiffService : IDisposableWorkspaceDiffService
{
    private static readonly JsonSerializerOptions DiffJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<DisposableWorkspaceDiffResult> DiffAsync(
        DisposableWorkspaceDiffRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = NormalizePath(request.WorkspacePath);
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!Directory.Exists(workspacePath))
        {
            errors.Add($"Workspace path does not exist: {workspacePath}");
            return Failed(request, workspacePath, string.Empty, errors, warnings);
        }

        var metadataPath = Path.Combine(workspacePath, ".irondev", "workspace.json");
        if (!File.Exists(metadataPath))
        {
            errors.Add("Workspace preparation metadata was not found. Run 'irondev workspace prepare' before diff.");
            return Blocked(request, workspacePath, string.Empty, errors, warnings);
        }

        DisposableWorkspaceMetadata? metadata;
        try
        {
            await using var metadataStream = File.OpenRead(metadataPath);
            metadata = await JsonSerializer.DeserializeAsync<DisposableWorkspaceMetadata>(
                metadataStream,
                DiffJsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            errors.Add($"Workspace preparation metadata could not be read: {ex.Message}");
            return Blocked(request, workspacePath, string.Empty, errors, warnings);
        }

        if (metadata is null)
        {
            errors.Add("Workspace preparation metadata was empty.");
            return Blocked(request, workspacePath, string.Empty, errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(metadata.RunId))
        {
            errors.Add("Workspace metadata is missing runId.");
            return Blocked(request, workspacePath, string.Empty, errors, warnings);
        }

        if (!string.Equals(metadata.RunId, request.RunId, StringComparison.Ordinal))
        {
            errors.Add($"Workspace runId mismatch. Metadata runId '{metadata.RunId}' does not match requested runId '{request.RunId}'.");
            return Blocked(request, workspacePath, string.Empty, errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(metadata.SourceRepo))
        {
            errors.Add("Workspace metadata is missing sourceRepo.");
            return Blocked(request, workspacePath, string.Empty, errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(metadata.WorkspacePath))
        {
            errors.Add("Workspace metadata is missing workspacePath.");
            return Blocked(request, workspacePath, metadata.SourceRepo, errors, warnings);
        }

        var sourceRepo = NormalizePath(metadata.SourceRepo);
        var metadataWorkspacePath = NormalizePath(metadata.WorkspacePath);
        if (!PathsEqual(workspacePath, metadataWorkspacePath))
        {
            errors.Add("Workspace path does not match the prepared workspace metadata.");
            return Blocked(request, workspacePath, sourceRepo, errors, warnings);
        }

        if (!Directory.Exists(sourceRepo))
        {
            errors.Add($"Source repository from workspace metadata does not exist: {sourceRepo}");
            return Blocked(request, workspacePath, sourceRepo, errors, warnings);
        }

        if (PathContainsSegment(workspacePath, ".git") ||
            PathsEqual(workspacePath, sourceRepo) ||
            IsSameOrInside(sourceRepo, workspacePath))
        {
            errors.Add("Workspace path must be isolated from the source repository.");
            return Blocked(request, workspacePath, sourceRepo, errors, warnings);
        }

        try
        {
            var sourceFiles = await BuildHashMapAsync(sourceRepo, cancellationToken).ConfigureAwait(false);
            var workspaceFiles = await BuildHashMapAsync(workspacePath, cancellationToken).ConfigureAwait(false);

            var addedFiles = workspaceFiles.Keys.Except(sourceFiles.Keys, StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var deletedFiles = sourceFiles.Keys.Except(workspaceFiles.Keys, StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var commonFiles = sourceFiles.Keys.Intersect(workspaceFiles.Keys, StringComparer.Ordinal).ToArray();
            var modifiedFiles = commonFiles
                .Where(path => !string.Equals(sourceFiles[path], workspaceFiles[path], StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var unchangedFileCount = commonFiles.Length - modifiedFiles.Length;
            var changed = addedFiles.Length > 0 || modifiedFiles.Length > 0 || deletedFiles.Length > 0;
            var diffMetadataPath = Path.Combine(workspacePath, ".irondev", "runs", request.RunId, "diff.json");

            Directory.CreateDirectory(Path.GetDirectoryName(diffMetadataPath)!);
            var data = new DisposableWorkspaceDiffData
            {
                RunId = request.RunId,
                WorkspacePath = workspacePath,
                SourceRepo = sourceRepo,
                Changed = changed,
                AddedFiles = addedFiles,
                ModifiedFiles = modifiedFiles,
                DeletedFiles = deletedFiles,
                UnchangedFileCount = unchangedFileCount,
                DiffMetadataPath = diffMetadataPath,
                EvidencePaths = [diffMetadataPath],
                Errors = errors,
                Warnings = warnings
            };

            await File.WriteAllTextAsync(
                diffMetadataPath,
                JsonSerializer.Serialize(
                    new DisposableWorkspaceDiffMetadata
                    {
                        RunId = data.RunId,
                        WorkspacePath = data.WorkspacePath,
                        SourceRepo = data.SourceRepo,
                        CreatedUtc = DateTimeOffset.UtcNow,
                        Changed = data.Changed,
                        AddedFiles = data.AddedFiles,
                        ModifiedFiles = data.ModifiedFiles,
                        DeletedFiles = data.DeletedFiles,
                        UnchangedFileCount = data.UnchangedFileCount
                    },
                    DiffJsonOptions),
                cancellationToken).ConfigureAwait(false);

            return new DisposableWorkspaceDiffResult
            {
                Status = "succeeded",
                Summary = "Workspace diff completed.",
                ExitCode = 0,
                Data = data,
                Errors = errors,
                Warnings = warnings
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors.Add($"Workspace diff failed: {ex.Message}");
            return Failed(request, workspacePath, sourceRepo, errors, warnings);
        }
    }

    private static async Task<Dictionary<string, string>> BuildHashMapAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in EnumerateIncludedFiles(root, root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(root, file));
            await using var stream = File.OpenRead(file);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            files[relativePath] = Convert.ToHexString(hash).ToLowerInvariant();
        }

        return files;
    }

    private static IEnumerable<string> EnumerateIncludedFiles(string root, string current)
    {
        foreach (var directory in Directory.EnumerateDirectories(current))
        {
            if (ShouldExclude(root, directory))
                continue;

            foreach (var file in EnumerateIncludedFiles(root, directory))
                yield return file;
        }

        foreach (var file in Directory.EnumerateFiles(current))
        {
            if (!ShouldExclude(root, file))
                yield return file;
        }
    }

    private static DisposableWorkspaceDiffResult Blocked(
        DisposableWorkspaceDiffRequest request,
        string workspacePath,
        string sourceRepo,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
    {
        return new DisposableWorkspaceDiffResult
        {
            Status = "blocked",
            Summary = "Workspace diff was blocked.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, false, errors, warnings),
            Errors = errors,
            Warnings = warnings
        };
    }

    private static DisposableWorkspaceDiffResult Failed(
        DisposableWorkspaceDiffRequest request,
        string workspacePath,
        string sourceRepo,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
    {
        return new DisposableWorkspaceDiffResult
        {
            Status = "failed",
            Summary = "Workspace diff failed.",
            ExitCode = 1,
            Data = BuildData(request, workspacePath, sourceRepo, false, errors, warnings),
            Errors = errors,
            Warnings = warnings
        };
    }

    private static DisposableWorkspaceDiffData BuildData(
        DisposableWorkspaceDiffRequest request,
        string workspacePath,
        string sourceRepo,
        bool changed,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings)
    {
        return new DisposableWorkspaceDiffData
        {
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            SourceRepo = sourceRepo,
            Changed = changed,
            AddedFiles = [],
            ModifiedFiles = [],
            DeletedFiles = [],
            UnchangedFileCount = 0,
            DiffMetadataPath = null,
            EvidencePaths = [],
            Errors = errors,
            Warnings = warnings
        };
    }

    private static bool ShouldExclude(string root, string path)
    {
        var relativePath = NormalizeRelativePath(Path.GetRelativePath(root, path));
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(segment =>
                string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, ".vs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "TestResults", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, ".irondev", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return relativePath.StartsWith("tools/dogfood/runs/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relativePath, "tools/dogfood/runs", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path.Trim());

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            TrimEndingDirectorySeparator(NormalizePath(left)),
            TrimEndingDirectorySeparator(NormalizePath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrInside(string parent, string candidate)
    {
        var normalizedParent = TrimEndingDirectorySeparator(NormalizePath(parent));
        var normalizedCandidate = TrimEndingDirectorySeparator(NormalizePath(candidate));

        return string.Equals(normalizedCandidate, normalizedParent, StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidate.StartsWith(
                normalizedParent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidate.StartsWith(
                normalizedParent + Path.AltDirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathContainsSegment(string path, string segment)
    {
        return NormalizePath(path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(part => string.Equals(part, segment, StringComparison.OrdinalIgnoreCase));
    }

    private static string TrimEndingDirectorySeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed record DisposableWorkspaceMetadata
    {
        public string? RunId { get; init; }
        public string? SourceRepo { get; init; }
        public string? WorkspacePath { get; init; }
    }

    private sealed record DisposableWorkspaceDiffMetadata
    {
        public required string RunId { get; init; }
        public required string WorkspacePath { get; init; }
        public required string SourceRepo { get; init; }
        public required DateTimeOffset CreatedUtc { get; init; }
        public required bool Changed { get; init; }
        public IReadOnlyList<string> AddedFiles { get; init; } = [];
        public IReadOnlyList<string> ModifiedFiles { get; init; } = [];
        public IReadOnlyList<string> DeletedFiles { get; init; } = [];
        public int UnchangedFileCount { get; init; }
    }
}
