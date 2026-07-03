using IronDev.Core.Workspaces;

namespace IronDev.Infrastructure.Services.Workspaces;

/// <summary>
/// Backend-owned dotnet build/test command profile for disposable workspaces.
/// Shared by the disposable ticket build run and the skeleton run so both execute
/// the same allow-listed commands.
/// </summary>
public static class DotNetCommandProfile
{
    public static IReadOnlyList<DisposableWorkspaceCommand> BuildAndTest(
        string projectPath,
        int buildTimeoutSeconds,
        int testTimeoutSeconds)
    {
        var target = FindDotNetTarget(projectPath);
        var args = string.IsNullOrWhiteSpace(target)
            ? Array.Empty<string>()
            : new[] { target };

        return
        [
            new DisposableWorkspaceCommand
            {
                FileName = "dotnet",
                Arguments = args.Prepend("build").Append("--nologo").ToArray(),
                DisplayName = "dotnet build",
                Timeout = TimeSpan.FromSeconds(buildTimeoutSeconds)
            },
            new DisposableWorkspaceCommand
            {
                FileName = "dotnet",
                Arguments = args.Prepend("test").Append("--nologo").ToArray(),
                DisplayName = "dotnet test",
                Timeout = TimeSpan.FromSeconds(testTimeoutSeconds)
            }
        ];
    }

    public static string? FindDotNetTarget(string projectPath)
    {
        var solution = Directory.EnumerateFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(projectPath, "*.slnx", SearchOption.TopDirectoryOnly))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(solution))
            return Path.GetFileName(solution);

        var project = Directory.EnumerateFiles(projectPath, "*.*proj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return project is null ? null : Path.GetRelativePath(projectPath, project);
    }
}
