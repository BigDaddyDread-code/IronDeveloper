using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using IronDev.Core.Interfaces;
using IronDev.Data.Models;

namespace IronDev.Infrastructure.Services;

public sealed class ProjectProfileDetectionService : IProjectProfileDetectionService
{
    public async Task<ProjectProfileDetectionResult> DetectAsync(
        string projectRoot,
        int projectId = 0,
        CancellationToken ct = default)
    {
        var result = new ProjectProfileDetectionResult();
        var root = Path.GetFullPath(projectRoot);

        result.Profile.ProjectId = projectId;
        result.Profile.SafeWriteRoot = root;
        result.Profile.IsExternalProject = true;
        result.Profile.AllowWritesOutsideProjectRoot = false;
        result.Profile.AllowBuilderApply = false;
        result.Profile.DatabaseEngine = "None";
        result.Profile.DataAccessStyle = "None";
        result.Profile.TestFramework = "None";

        result.BuildCommand.ProjectId = projectId;
        result.BuildCommand.CommandType = "Build";
        result.BuildCommand.WorkingDirectory = root;
        result.BuildCommand.TimeoutSeconds = 300;
        result.BuildCommand.IsDefault = true;
        result.BuildCommand.IsEnabled = true;

        result.TestCommand.ProjectId = projectId;
        result.TestCommand.CommandType = "Test";
        result.TestCommand.WorkingDirectory = root;
        result.TestCommand.TimeoutSeconds = 300;
        result.TestCommand.IsDefault = true;
        result.TestCommand.IsEnabled = true;

        if (!Directory.Exists(root))
        {
            result.Warnings.Add($"Project root does not exist: {root}");
            result.BuildCommand.CommandText = "dotnet build";
            result.TestCommand.CommandText = "dotnet test";
            return result;
        }

        var solutionFile = FindPreferredFile(root, "*.slnx")
            ?? FindPreferredFile(root, "*.sln")
            ?? FindPreferredFile(root, "*.csproj");

        if (solutionFile != null)
        {
            result.Profile.SolutionFile = solutionFile;
            result.BuildCommand.CommandText = $"dotnet build \"{solutionFile}\" --no-incremental -v quiet";
            result.TestCommand.CommandText = $"dotnet test \"{solutionFile}\" --logger \"console;verbosity=minimal\"";
            result.DetectedFacts.Add($"Build target detected: {Path.GetFileName(solutionFile)}");
        }
        else
        {
            result.Warnings.Add("No .slnx, .sln, or .csproj file was detected.");
            result.BuildCommand.CommandText = "dotnet build";
            result.TestCommand.CommandText = "dotnet test";
        }

        var projectFiles = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IsIgnoredPath(p))
            .OrderBy(p => p.Length)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var projectFile in projectFiles)
        {
            ct.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(projectFile, ct);
            DetectFromProjectFile(result, projectFile, content);
        }

        if (string.IsNullOrWhiteSpace(result.Profile.PrimaryLanguage) && projectFiles.Length > 0)
            result.Profile.PrimaryLanguage = "C#";

        if (string.IsNullOrWhiteSpace(result.Profile.Framework))
            result.Profile.Framework = projectFiles.Length > 0 ? ".NET" : "Unknown";

        if (string.IsNullOrWhiteSpace(result.Profile.ApplicationType))
            result.Profile.ApplicationType = projectFiles.Length > 0 ? "External Sandbox / .NET Project" : "Unknown";

        return result;
    }

    private static string? FindPreferredFile(string root, string pattern)
    {
        return Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly)
                   .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                   .FirstOrDefault()
               ?? Directory.GetFiles(root, pattern, SearchOption.AllDirectories)
                   .Where(p => !IsIgnoredPath(p))
                   .OrderBy(p => p.Length)
                   .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                   .FirstOrDefault();
    }

    private static void DetectFromProjectFile(ProjectProfileDetectionResult result, string projectFile, string content)
    {
        result.Profile.PrimaryLanguage ??= "C#";

        var xml = TryParseProject(content);
        var targetFramework = xml?
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            ?.Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            result.Profile.RuntimeVersion ??= targetFramework;
            result.Profile.Framework ??= ToFrameworkDisplayName(targetFramework);
        }

        if (content.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
            result.Profile.ApplicationType ??= "ASP.NET Core";
        else if (content.Contains("Microsoft.NET.Sdk.WindowsDesktop", StringComparison.OrdinalIgnoreCase) ||
                 content.Contains("<UseWPF>true</UseWPF>", StringComparison.OrdinalIgnoreCase))
            result.Profile.ApplicationType ??= "WPF";
        else if (content.Contains("Avalonia", StringComparison.OrdinalIgnoreCase))
            result.Profile.ApplicationType ??= "Avalonia";

        if (content.Contains("xunit", StringComparison.OrdinalIgnoreCase))
            result.Profile.TestFramework = "xUnit";
        else if (content.Contains("MSTest", StringComparison.OrdinalIgnoreCase))
            result.Profile.TestFramework = "MSTest";
        else if (content.Contains("nunit", StringComparison.OrdinalIgnoreCase))
            result.Profile.TestFramework = "NUnit";

        if (content.Contains("Dapper", StringComparison.OrdinalIgnoreCase))
            result.Profile.DataAccessStyle = "Dapper";
        else if (content.Contains("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase))
            result.Profile.DataAccessStyle = "EF Core";

        if (content.Contains("Microsoft.Data.SqlClient", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("System.Data.SqlClient", StringComparison.OrdinalIgnoreCase))
            result.Profile.DatabaseEngine = "SQL Server";
        else if (content.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            result.Profile.DatabaseEngine = "SQLite";

        if (content.Contains("Serilog", StringComparison.OrdinalIgnoreCase))
            result.DetectedFacts.Add($"Serilog referenced by {Path.GetFileName(projectFile)}.");
    }

    private static XDocument? TryParseProject(string content)
    {
        try
        {
            return XDocument.Parse(content);
        }
        catch
        {
            return null;
        }
    }

    private static string ToFrameworkDisplayName(string targetFramework)
    {
        if (targetFramework.StartsWith("net", StringComparison.OrdinalIgnoreCase) &&
            targetFramework.Length >= 5 &&
            char.IsDigit(targetFramework[3]))
        {
            var version = targetFramework[3..].Replace(".0", string.Empty, StringComparison.OrdinalIgnoreCase);
            return $".NET {version}";
        }

        return targetFramework;
    }

    private static bool IsIgnoredPath(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
           || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
           || path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}
