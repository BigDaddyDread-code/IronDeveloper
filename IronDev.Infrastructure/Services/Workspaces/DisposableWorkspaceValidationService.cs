using System.Text.Json;

using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

public sealed class DisposableWorkspaceValidationService : IDisposableWorkspaceValidationService
{
    private static readonly JsonSerializerOptions ValidationJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Profiles =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["dotnet-build-test"] = ["dotnet-build", "dotnet-test"]
        };

    private readonly IDisposableWorkspaceCommandService _commandService;

    public DisposableWorkspaceValidationService(IDisposableWorkspaceCommandService commandService)
    {
        _commandService = commandService;
    }

    public async Task<DisposableWorkspaceValidationResult> ValidateAsync(
        DisposableWorkspaceValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        var workspacePath = Path.GetFullPath(request.WorkspacePath.Trim());
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!Profiles.TryGetValue(request.ProfileId, out var commandIds))
        {
            errors.Add($"Workspace validation profile '{request.ProfileId}' is not allowlisted.");
            return Blocked(request, workspacePath, [], errors, warnings, validationMetadataPath: null);
        }

        var startedUtc = DateTimeOffset.UtcNow;
        var steps = new List<DisposableWorkspaceValidationStep>();
        var evidencePaths = new List<string>();
        var status = "succeeded";

        foreach (var commandId in commandIds)
        {
            var commandResult = await _commandService.RunAsync(
                new DisposableWorkspaceCommandRequest
                {
                    RunId = request.RunId,
                    WorkspacePath = workspacePath,
                    CommandId = commandId
                },
                cancellationToken).ConfigureAwait(false);

            var step = new DisposableWorkspaceValidationStep
            {
                CommandId = commandId,
                Status = commandResult.Status,
                ExitCode = commandResult.Data.ExitCode,
                Succeeded = commandResult.Data.Succeeded,
                EvidencePaths = commandResult.Data.EvidencePaths,
                Errors = commandResult.Errors,
                Warnings = commandResult.Warnings
            };
            steps.Add(step);
            evidencePaths.AddRange(step.EvidencePaths);
            errors.AddRange(step.Errors);
            warnings.AddRange(step.Warnings);

            if (!string.Equals(commandResult.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                status = string.Equals(commandResult.Status, "blocked", StringComparison.OrdinalIgnoreCase)
                    ? "blocked"
                    : "failed";
                break;
            }
        }

        var completedUtc = DateTimeOffset.UtcNow;
        var validationMetadataPath = Path.Combine(workspacePath, ".irondev", "runs", request.RunId, "validation.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(validationMetadataPath)!);
            await File.WriteAllTextAsync(
                validationMetadataPath,
                JsonSerializer.Serialize(
                    new DisposableWorkspaceValidationMetadata
                    {
                        RunId = request.RunId,
                        WorkspacePath = workspacePath,
                        ProfileId = request.ProfileId,
                        StartedUtc = startedUtc,
                        CompletedUtc = completedUtc,
                        Status = status,
                        Steps = steps
                    },
                    ValidationJsonOptions),
                cancellationToken).ConfigureAwait(false);
            evidencePaths.Add(validationMetadataPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors.Add($"Workspace validation metadata could not be written: {ex.Message}");
            return Blocked(request, workspacePath, steps, errors, warnings, validationMetadataPath);
        }

        var succeeded = string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase);
        return new DisposableWorkspaceValidationResult
        {
            Status = status,
            Summary = succeeded ? "Workspace validation completed." : "Workspace validation did not complete successfully.",
            ExitCode = succeeded ? 0 : 1,
            Data = new DisposableWorkspaceValidationData
            {
                RunId = request.RunId,
                WorkspacePath = workspacePath,
                ProfileId = request.ProfileId,
                Status = status,
                Succeeded = succeeded,
                Steps = steps,
                EvidencePaths = evidencePaths,
                ValidationMetadataPath = validationMetadataPath,
                Errors = errors,
                Warnings = warnings
            },
            Errors = errors,
            Warnings = warnings
        };
    }

    private static DisposableWorkspaceValidationResult Blocked(
        DisposableWorkspaceValidationRequest request,
        string workspacePath,
        IReadOnlyList<DisposableWorkspaceValidationStep> steps,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings,
        string? validationMetadataPath)
    {
        return new DisposableWorkspaceValidationResult
        {
            Status = "blocked",
            Summary = "Workspace validation was blocked.",
            ExitCode = 1,
            Data = new DisposableWorkspaceValidationData
            {
                RunId = request.RunId,
                WorkspacePath = workspacePath,
                ProfileId = request.ProfileId,
                Status = "blocked",
                Succeeded = false,
                Steps = steps,
                EvidencePaths = [],
                ValidationMetadataPath = validationMetadataPath,
                Errors = errors,
                Warnings = warnings
            },
            Errors = errors,
            Warnings = warnings
        };
    }

    private sealed record DisposableWorkspaceValidationMetadata
    {
        public required string RunId { get; init; }
        public required string WorkspacePath { get; init; }
        public required string ProfileId { get; init; }
        public required DateTimeOffset StartedUtc { get; init; }
        public required DateTimeOffset CompletedUtc { get; init; }
        public required string Status { get; init; }
        public IReadOnlyList<DisposableWorkspaceValidationStep> Steps { get; init; } = [];
    }
}
