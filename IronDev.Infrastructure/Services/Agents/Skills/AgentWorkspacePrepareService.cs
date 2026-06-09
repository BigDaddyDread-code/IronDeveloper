using System.Text.Json;
using IronDev.Core.Agents.Skills;

namespace IronDev.Infrastructure.Services.Agents.Skills;

public sealed class AgentWorkspacePrepareService : IAgentWorkspacePrepareService
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IAgentWorkspaceCheckService _workspaceCheckService;

    public AgentWorkspacePrepareService(IAgentWorkspaceCheckService workspaceCheckService)
    {
        _workspaceCheckService = workspaceCheckService ?? throw new ArgumentNullException(nameof(workspaceCheckService));
    }

    public async Task<AgentWorkspacePrepareResult> PrepareAsync(
        AgentWorkspacePrepareRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var check = await _workspaceCheckService.CheckAsync(
            new AgentWorkspaceCheckRequest
            {
                ProjectId = request.ProjectId,
                RunId = request.RunId,
                WorkspacePath = request.WorkspacePath,
                SourceRepo = request.SourceRepo,
                EvidencePaths = request.EvidencePaths
            },
            cancellationToken).ConfigureAwait(false);

        if (!check.ReadyForPrepare)
        {
            return new AgentWorkspacePrepareResult
            {
                Status = AgentSkillExecutionStatuses.BlockedByContext,
                PrepareAttempted = false,
                Prepared = false,
                ProjectId = request.ProjectId,
                RunId = request.RunId,
                WorkspacePath = check.WorkspacePath,
                SourceRepo = check.SourceRepo,
                SourceRepoExists = check.SourceRepoExists,
                WorkspacePathExists = check.WorkspacePathExists,
                SourceAndWorkspaceAreDistinct = check.SourceAndWorkspaceAreDistinct,
                FilesCopied = 0,
                DirectoriesCreated = 0,
                EvidencePaths = check.EvidencePaths,
                Warnings = check.Warnings,
                Blockers = check.Blockers
            };
        }

        var sourceRepo = NormalizePath(request.SourceRepo);
        var workspacePath = NormalizePath(request.WorkspacePath);
        var metadataPath = Path.Combine(workspacePath, ".irondev", "workspace.json");
        var directoriesCreated = 0;

        EnsureDirectory(workspacePath, ref directoriesCreated);
        var filesCopied = CopyDirectory(sourceRepo, sourceRepo, workspacePath, ref directoriesCreated);
        EnsureDirectory(Path.GetDirectoryName(metadataPath)!, ref directoriesCreated);
        await File.WriteAllTextAsync(
            metadataPath,
            JsonSerializer.Serialize(
                new AgentWorkspacePrepareMetadata
                {
                    RunId = request.RunId,
                    SourceRepo = sourceRepo,
                    WorkspacePath = workspacePath,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    PreparationMethod = "copy",
                    SourceRepoMutated = false
                },
                MetadataJsonOptions),
            cancellationToken).ConfigureAwait(false);

        return new AgentWorkspacePrepareResult
        {
            Status = AgentSkillExecutionStatuses.Succeeded,
            PrepareAttempted = true,
            Prepared = true,
            ProjectId = request.ProjectId,
            RunId = request.RunId,
            WorkspacePath = workspacePath,
            SourceRepo = sourceRepo,
            SourceRepoExists = true,
            WorkspacePathExists = true,
            SourceAndWorkspaceAreDistinct = true,
            FilesCopied = filesCopied,
            DirectoriesCreated = directoriesCreated,
            EvidencePaths = Merge(request.EvidencePaths, [metadataPath]),
            Warnings = check.Warnings,
            Blockers = []
        };
    }

    private static int CopyDirectory(
        string sourceRoot,
        string currentSource,
        string targetRoot,
        ref int directoriesCreated)
    {
        var filesCopied = 0;
        foreach (var directory in Directory.EnumerateDirectories(currentSource))
        {
            if (ShouldExclude(sourceRoot, directory))
                continue;

            EnsureDirectory(Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, directory)), ref directoriesCreated);
            filesCopied += CopyDirectory(sourceRoot, directory, targetRoot, ref directoriesCreated);
        }

        foreach (var file in Directory.EnumerateFiles(currentSource))
        {
            if (ShouldExclude(sourceRoot, file))
                continue;

            var targetPath = Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, file));
            EnsureDirectory(Path.GetDirectoryName(targetPath)!, ref directoriesCreated);
            File.Copy(file, targetPath, overwrite: false);
            filesCopied++;
        }

        return filesCopied;
    }

    private static void EnsureDirectory(string path, ref int directoriesCreated)
    {
        if (Directory.Exists(path))
            return;

        Directory.CreateDirectory(path);
        directoriesCreated++;
    }

    private static bool ShouldExclude(string sourceRepo, string path)
    {
        var relativePath = Path.GetRelativePath(sourceRepo, path).Replace('\\', '/');
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(segment =>
                string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, ".vs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "TestResults", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return relativePath.StartsWith("tools/dogfood/runs/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relativePath, "tools/dogfood/runs", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path.Trim());

    private static IReadOnlyList<string> Merge(params IEnumerable<string>[] values) =>
        values
            .SelectMany(value => value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private sealed record AgentWorkspacePrepareMetadata
    {
        public required string RunId { get; init; }
        public required string SourceRepo { get; init; }
        public required string WorkspacePath { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
        public required string PreparationMethod { get; init; }
        public bool SourceRepoMutated { get; init; }
    }
}
