using IronDev.Core.Workbench;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace IronDev.Infrastructure.Services;

public sealed class PhysicalRepositorySetupFileSystemInspector : IRepositorySetupFileSystemInspector
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
}

public sealed class RepositorySetupPathPolicy : IRepositorySetupPathPolicy
{
    private readonly IRepositorySetupFileSystemInspector _fileSystem;
    private readonly IRepositorySetupForbiddenRootCatalog _forbiddenRoots;

    public RepositorySetupPathPolicy(
        IRepositorySetupFileSystemInspector fileSystem,
        IRepositorySetupForbiddenRootCatalog forbiddenRoots)
    {
        _fileSystem = fileSystem;
        _forbiddenRoots = forbiddenRoots;
    }

    public RepositorySetupPathAssessment Assess(
        string approvedWorkspaceRoot,
        string directChildName,
        bool inspectEnvironment)
    {
        if (string.IsNullOrWhiteSpace(approvedWorkspaceRoot))
        {
            return Unavailable(
                RepositorySetupReasonCodes.WorkspaceRootNotConfigured,
                "Greenfield repository setup is unavailable because no isolated repository workspace is configured.");
        }

        if (string.IsNullOrWhiteSpace(directChildName) ||
            directChildName is "." or ".." ||
            directChildName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\']) >= 0)
            return Unsafe("The server-derived repository directory name is not a safe direct child.");

        var rawRoot = approvedWorkspaceRoot.Trim();
        if (ContainsTraversal(rawRoot) || IsUncOrDevicePath(rawRoot) || !Path.IsPathFullyQualified(rawRoot))
            return Unsafe("The approved repository workspace root must be a plain absolute local path without traversal, UNC, or device syntax.");

        string root;
        string target;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rawRoot));
            target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, directChildName)));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Unsafe("The approved repository workspace root could not be normalized safely.");
        }

        var driveRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(driveRoot) || string.Equals(root, driveRoot, StringComparison.OrdinalIgnoreCase))
            return Unsafe("A drive root cannot be used as the approved repository workspace root.");

        foreach (var forbiddenRoot in _forbiddenRoots.GetForbiddenRoots())
        {
            if (PathsOverlap(root, forbiddenRoot))
                return Unsafe("The approved repository workspace overlaps a protected OS, user, temporary, application, or source-repository root.");
        }

        var expectedParent = Path.GetDirectoryName(target);
        if (!string.Equals(expectedParent, root, StringComparison.OrdinalIgnoreCase) ||
            !target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return Unsafe("The repository target must be a direct child of the approved workspace root.");

        if (!inspectEnvironment)
            return Available(root, target);

        if (!_fileSystem.DirectoryExists(root))
        {
            return new RepositorySetupPathAssessment(
                false,
                false,
                RepositorySetupReasonCodes.WorkspaceRootUnavailable,
                "The configured isolated repository workspace is not available in this environment.",
                root,
                target);
        }

        if (ContainsReparsePoint(root))
            return Unsafe("The approved repository workspace root is, or is below, a reparse point.", root, target);

        if (_fileSystem.DirectoryExists(target) || _fileSystem.FileExists(target))
            return Unsafe("The server-derived repository target already exists.", root, target);

        return Available(root, target);
    }

    private bool ContainsReparsePoint(string path)
    {
        var current = path;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (_fileSystem.DirectoryExists(current) &&
                (_fileSystem.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return true;

            var parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent ?? string.Empty;
        }

        return false;
    }

    private static bool ContainsTraversal(string path) => path
        .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(segment => segment == "..");

    private static bool IsUncOrDevicePath(string path) =>
        path.StartsWith("\\\\", StringComparison.Ordinal) ||
        path.StartsWith("//", StringComparison.Ordinal) ||
        path.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
        path.StartsWith("\\\\.\\", StringComparison.Ordinal);

    private static bool PathsOverlap(string candidate, string forbidden)
    {
        if (string.IsNullOrWhiteSpace(forbidden))
            return false;
        string normalized;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(forbidden));
        }
        catch
        {
            return true;
        }

        return string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(candidate + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static RepositorySetupPathAssessment Available(string root, string target) => new(
        true,
        false,
        RepositorySetupReasonCodes.Ready,
        "The deterministic setup plan is ready for explicit confirmation.",
        root,
        target);

    private static RepositorySetupPathAssessment Unavailable(string reasonCode, string message) => new(
        false,
        false,
        reasonCode,
        message,
        string.Empty,
        string.Empty);

    private static RepositorySetupPathAssessment Unsafe(
        string message,
        string root = "",
        string target = "") => new(
        false,
        true,
        RepositorySetupUnsafePathException.ErrorCode,
        message,
        root,
        target);
}

public sealed class RepositorySetupForbiddenRootCatalog : IRepositorySetupForbiddenRootCatalog
{
    private readonly IReadOnlyList<string> _roots;

    public RepositorySetupForbiddenRootCatalog(IHostEnvironment environment, IConfiguration configuration)
    {
        var candidates = new List<string?>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.GetTempPath(),
            environment.ContentRootPath,
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
            FindSourceRepositoryRoot(environment.ContentRootPath),
            FindSourceRepositoryRoot(Directory.GetCurrentDirectory())
        };
        candidates.AddRange(configuration
            .GetSection("WorkbenchRepositorySetup:AdditionalForbiddenRoots")
            .Get<string[]>() ?? []);
        _roots = candidates
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Normalize(value!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> GetForbiddenRoots() => _roots;

    private static string? FindSourceRepositoryRoot(string? start)
    {
        if (string.IsNullOrWhiteSpace(start))
            return null;
        DirectoryInfo? current;
        try
        {
            current = new DirectoryInfo(Path.GetFullPath(start));
        }
        catch
        {
            return start;
        }

        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;
            current = current.Parent;
        }
        return null;
    }

    private static string Normalize(string value)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        }
        catch
        {
            return value.Trim();
        }
    }
}
