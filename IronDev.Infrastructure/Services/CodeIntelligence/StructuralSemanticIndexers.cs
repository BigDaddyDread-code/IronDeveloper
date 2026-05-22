using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services.CodeIntelligence;

public sealed class CSharpStructuralSemanticIndexer : ILanguageSemanticIndexer
{
    private static readonly Regex TypeRegex = new(
        @"\b(?:(?:public|private|protected|internal|sealed|static|abstract|partial)\s+)*(class|interface|record|struct|enum)\s+([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    private static readonly Regex MethodRegex = new(
        @"\b(?:(?:public|private|protected|internal|static|async|virtual|override|sealed|partial)\s+)+[A-Za-z_][A-Za-z0-9_<>,\?\[\]\s\.]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled);

    public string LanguageId => "csharp";
    public string Confidence => "Medium";

    public bool CanHandle(string filePath)
        => string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<SemanticSymbolInfo>> IndexAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var symbols = new List<SemanticSymbolInfo>();

        foreach (var filePath in filePaths.Where(CanHandle))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(filePath))
                continue;

            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
            string? currentType = null;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                var typeMatch = TypeRegex.Match(line);
                if (typeMatch.Success)
                {
                    currentType = typeMatch.Groups[2].Value;
                    symbols.Add(new SemanticSymbolInfo
                    {
                        LanguageId = LanguageId,
                        FilePath = filePath,
                        Name = currentType,
                        Kind = typeMatch.Groups[1].Value,
                        Signature = line,
                        StartLine = i + 1,
                        Confidence = Confidence
                    });
                    continue;
                }

                var methodMatch = MethodRegex.Match(line);
                if (methodMatch.Success && !IsControlKeyword(methodMatch.Groups[1].Value))
                {
                    symbols.Add(new SemanticSymbolInfo
                    {
                        LanguageId = LanguageId,
                        FilePath = filePath,
                        Name = methodMatch.Groups[1].Value,
                        Kind = "method",
                        ContainerName = currentType,
                        Signature = line,
                        StartLine = i + 1,
                        Confidence = Confidence
                    });
                }
            }
        }

        return symbols;
    }

    private static bool IsControlKeyword(string value)
        => value is "if" or "for" or "foreach" or "while" or "switch" or "catch" or "using" or "lock";
}

public sealed class XamlStructuralSemanticIndexer : ILanguageSemanticIndexer
{
    private static readonly Regex NamedElementRegex = new(
        @"<([A-Za-z_][A-Za-z0-9_\.:]*)[^>]*(?:x:Name|Name)=""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex BindingRegex = new(
        @"\{Binding\s+([^,\}]+)",
        RegexOptions.Compiled);

    public string LanguageId => "xaml";
    public string Confidence => "Medium";

    public bool CanHandle(string filePath)
        => string.Equals(Path.GetExtension(filePath), ".xaml", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<SemanticSymbolInfo>> IndexAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var symbols = new List<SemanticSymbolInfo>();

        foreach (var filePath in filePaths.Where(CanHandle))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(filePath))
                continue;

            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                foreach (Match match in NamedElementRegex.Matches(line))
                {
                    symbols.Add(new SemanticSymbolInfo
                    {
                        LanguageId = LanguageId,
                        FilePath = filePath,
                        Name = match.Groups[2].Value,
                        Kind = "named-element",
                        ContainerName = match.Groups[1].Value,
                        Signature = line.Trim(),
                        StartLine = i + 1,
                        Confidence = Confidence
                    });
                }

                foreach (Match match in BindingRegex.Matches(line))
                {
                    var bindingPath = match.Groups[1].Value.Trim();
                    if (string.IsNullOrWhiteSpace(bindingPath))
                        continue;

                    symbols.Add(new SemanticSymbolInfo
                    {
                        LanguageId = LanguageId,
                        FilePath = filePath,
                        Name = bindingPath,
                        Kind = "binding",
                        Signature = line.Trim(),
                        StartLine = i + 1,
                        Confidence = Confidence
                    });
                }
            }
        }

        return symbols;
    }
}

public sealed class ConfigStructuralSemanticIndexer : ILanguageSemanticIndexer
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".xml", ".csproj", ".props", ".targets", ".slnx", ".md"
    };

    public string LanguageId => "config";
    public string Confidence => "Low";

    public bool CanHandle(string filePath)
        => Extensions.Contains(Path.GetExtension(filePath));

    public Task<IReadOnlyList<SemanticSymbolInfo>> IndexAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var symbols = filePaths
            .Where(CanHandle)
            .Where(File.Exists)
            .Select(filePath => new SemanticSymbolInfo
            {
                LanguageId = LanguageId,
                FilePath = filePath,
                Name = Path.GetFileName(filePath),
                Kind = "configuration-file",
                Confidence = Confidence
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<SemanticSymbolInfo>>(symbols);
    }
}
