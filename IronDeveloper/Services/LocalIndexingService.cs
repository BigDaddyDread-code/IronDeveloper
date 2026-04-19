using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using IronDev.Agent.Models;

namespace IronDev.Agent.Services;

public class LocalIndexingService
{
    private static readonly string[] AllowedExtensions = { ".cs", ".xaml", ".csproj", ".json", ".md" };
    private static readonly string[] ExcludedDirectories = { "bin", "obj", ".git" };

    public IEnumerable<CodeIndexEntry> IndexProject(Guid tenantId, Guid projectId, string projectPath)
    {
        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Project path not found: {projectPath}");
        }

        var indexEntries = new List<CodeIndexEntry>();
        var files = Directory.EnumerateFiles(projectPath, "*.*", SearchOption.AllDirectories)
            .Where(file => AllowedExtensions.Contains(Path.GetExtension(file)) &&
                           !ExcludedDirectories.Any(dir => file.Contains($"{Path.DirectorySeparatorChar}{dir}{Path.DirectorySeparatorChar}")));

        foreach (var file in files)
        {
            var fileInfo = new FileInfo(file);
            var fileContent = File.ReadAllText(file);

            var indexEntry = new CodeIndexEntry
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ProjectId = projectId,
                FilePath = file,
                FileExtension = fileInfo.Extension,
                LastModifiedUtc = fileInfo.LastWriteTimeUtc,
                FileHash = ComputeFileHash(fileContent),
                Namespace = ExtractNamespace(fileContent),
                TypeName = ExtractTypeName(fileContent),
                MethodNames = ExtractMethodNames(fileContent),
                ChunkText = ExtractChunks(fileContent),
                LastIndexedUtc = DateTime.UtcNow
            };

            indexEntries.Add(indexEntry);
        }

        return indexEntries;
    }

    private string ComputeFileHash(string content)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(hashBytes);
    }

    private string? ExtractNamespace(string content)
    {
        var match = Regex.Match(content, @"namespace\s+([a-zA-Z0-9_.]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private string? ExtractTypeName(string content)
    {
        var match = Regex.Match(content, @"class\s+([a-zA-Z0-9_]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private List<string> ExtractMethodNames(string content)
    {
        var matches = Regex.Matches(content, @"\b(public|private|protected|internal)\s+.*?\s+([a-zA-Z0-9_]+)\s*\(");
        return matches.Select(m => m.Groups[2].Value).ToList();
    }

    private List<string> ExtractChunks(string content)
    {
        var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return lines.Take(10).ToList(); // Take the first 10 lines as a simple chunking strategy
    }
}