using System.Text.Json;
using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspacePrepareService : IDisposableWorkspacePrepareService
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IDisposableWorkspaceReadinessService _readinessService;

    public DisposableWorkspacePrepareService(IDisposableWorkspaceReadinessService readinessService)
    {
        _readinessService = readinessService;
    }

    public async Task<DisposableWorkspacePrepareResult> PrepareAsync(
        DisposableWorkspacePrepareRequest request,
        CancellationToken cancellationToken = default)
    {
        var readiness = await _readinessService.CheckAsync(
            new DisposableWorkspaceReadinessRequest
            {
                RunId = request.RunId,
                SourceRepo = request.SourceRepo,
                WorkspaceRoot = request.WorkspaceRoot,
                AllowDirtySourceRepo = request.AllowDirtySourceRepo
            },
            cancellationToken).ConfigureAwait(false);

        if (!string.Equals(readiness.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return FromReadinessFailure(request, readiness);
        }

        var checks = new List<DisposableWorkspaceReadinessCheck>(readiness.Data.Checks);
        var errors = new List<string>();
        var workspacePath = readiness.Data.WorkspacePath;
        var metadataPath = Path.Combine(workspacePath, ".irondev", "workspace.json");
        var filesCopied = 0;

        try
        {
            Directory.CreateDirectory(workspacePath);
            checks.Add(new DisposableWorkspaceReadinessCheck
            {
                Name = "workspace_directory_created",
                Status = "passed",
                Message = "Disposable workspace directory was created."
            });

            filesCopied = CopySource(readiness.Data.SourceRepo, workspacePath);
            checks.Add(new DisposableWorkspaceReadinessCheck
            {
                Name = "source_copied",
                Status = "passed",
                Message = $"Copied {filesCopied} source files into the disposable workspace."
            });

            Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
            await File.WriteAllTextAsync(
                metadataPath,
                JsonSerializer.Serialize(
                    new DisposableWorkspacePrepareMetadata
                    {
                        RunId = request.RunId,
                        SourceRepo = readiness.Data.SourceRepo,
                        WorkspacePath = workspacePath,
                        CreatedUtc = DateTimeOffset.UtcNow,
                        PreparationMethod = "copy",
                        SourceRepoMutated = false
                    },
                    MetadataJsonOptions),
                cancellationToken).ConfigureAwait(false);
            checks.Add(new DisposableWorkspaceReadinessCheck
            {
                Name = "metadata_written",
                Status = "passed",
                Message = "Disposable workspace preparation metadata was written."
            });

            return new DisposableWorkspacePrepareResult
            {
                Status = "succeeded",
                Summary = "Disposable workspace prepared.",
                ExitCode = 0,
                Data = new DisposableWorkspacePrepareData
                {
                    RunId = request.RunId,
                    SourceRepo = readiness.Data.SourceRepo,
                    WorkspaceRoot = readiness.Data.WorkspaceRoot,
                    WorkspacePath = workspacePath,
                    ReadinessStatus = readiness.Status,
                    Prepared = true,
                    PreparationMethod = "copy",
                    FilesCopied = filesCopied,
                    MetadataPath = metadataPath,
                    SourceRepoMutated = false,
                    Checks = checks
                },
                Errors = [],
                Warnings = readiness.Warnings
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            var message = $"Disposable workspace preparation failed: {ex.Message}";
            errors.Add(message);
            checks.Add(new DisposableWorkspaceReadinessCheck
            {
                Name = "workspace_prepare",
                Status = "failed",
                Message = message
            });

            return new DisposableWorkspacePrepareResult
            {
                Status = "failed",
                Summary = "Disposable workspace preparation failed.",
                ExitCode = 1,
                Data = new DisposableWorkspacePrepareData
                {
                    RunId = request.RunId,
                    SourceRepo = readiness.Data.SourceRepo,
                    WorkspaceRoot = readiness.Data.WorkspaceRoot,
                    WorkspacePath = workspacePath,
                    ReadinessStatus = readiness.Status,
                    Prepared = false,
                    PreparationMethod = "copy",
                    FilesCopied = filesCopied,
                    MetadataPath = metadataPath,
                    SourceRepoMutated = false,
                    Checks = checks
                },
                Errors = errors,
                Warnings = readiness.Warnings
            };
        }
    }

    private static DisposableWorkspacePrepareResult FromReadinessFailure(
        DisposableWorkspacePrepareRequest request,
        DisposableWorkspaceReadinessResult readiness)
    {
        return new DisposableWorkspacePrepareResult
        {
            Status = readiness.Status,
            Summary = "Disposable workspace prepare blocked by readiness check.",
            ExitCode = 1,
            Data = new DisposableWorkspacePrepareData
            {
                RunId = request.RunId,
                SourceRepo = readiness.Data.SourceRepo,
                WorkspaceRoot = readiness.Data.WorkspaceRoot,
                WorkspacePath = readiness.Data.WorkspacePath,
                ReadinessStatus = readiness.Status,
                Prepared = false,
                PreparationMethod = "copy",
                FilesCopied = 0,
                MetadataPath = null,
                SourceRepoMutated = false,
                Checks = readiness.Data.Checks
            },
            Errors = readiness.Errors,
            Warnings = readiness.Warnings
        };
    }

    private static int CopySource(string sourceRepo, string workspacePath)
    {
        Directory.CreateDirectory(workspacePath);
        return CopyDirectory(sourceRepo, sourceRepo, workspacePath);
    }

    private static int CopyDirectory(string sourceRoot, string currentSource, string targetRoot)
    {
        var filesCopied = 0;
        foreach (var directory in Directory.EnumerateDirectories(currentSource))
        {
            if (ShouldExclude(sourceRoot, directory))
                continue;

            Directory.CreateDirectory(Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, directory)));
            filesCopied += CopyDirectory(sourceRoot, directory, targetRoot);
        }

        foreach (var file in Directory.EnumerateFiles(currentSource))
        {
            if (ShouldExclude(sourceRoot, file))
                continue;

            var targetPath = Path.Combine(targetRoot, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: false);
            filesCopied++;
        }

        return filesCopied;
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

    private sealed record DisposableWorkspacePrepareMetadata
    {
        public required string RunId { get; init; }
        public required string SourceRepo { get; init; }
        public required string WorkspacePath { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
        public required string PreparationMethod { get; init; }
        public bool SourceRepoMutated { get; init; }
    }
}
