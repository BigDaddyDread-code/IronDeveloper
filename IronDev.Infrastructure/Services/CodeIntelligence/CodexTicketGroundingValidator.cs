using System;
using System.Collections.Generic;
using System.Linq;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services.CodeIntelligence;

public sealed class CodexTicketGroundingValidator : ICodexTicketGroundingValidator
{
    public IReadOnlyList<CodebaseTicketDraft> ValidateAndScore(
        IReadOnlyList<CodebaseTicketDraft> tickets,
        CodexProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentNullException.ThrowIfNull(snapshot);

        var knownFiles = snapshot.Files
            .Select(file => NormalizePath(file.FilePath))
            .Concat(snapshot.Symbols.Select(symbol => NormalizePath(symbol.FilePath)))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var symbolKeys = BuildSymbolKeys(snapshot.Symbols);

        return tickets
            .Select(ticket => ValidateTicket(ticket, knownFiles, symbolKeys, snapshot.ContextQualityScore))
            .ToList();
    }

    private static CodebaseTicketDraft ValidateTicket(
        CodebaseTicketDraft ticket,
        HashSet<string> knownFiles,
        SymbolKeySet symbolKeys,
        int contextQualityScore)
    {
        var affectedFiles = CleanDistinct(ticket.AffectedFiles);
        var affectedSymbols = CleanDistinct(ticket.AffectedSymbols);
        var warnings = new List<string>();

        foreach (var file in affectedFiles)
        {
            if (!FileExistsInSnapshot(file, knownFiles))
                warnings.Add($"Affected file was not found in the index: {file}");
        }

        foreach (var symbol in affectedSymbols)
        {
            if (!SymbolExistsInSnapshot(symbol, symbolKeys))
                warnings.Add($"Affected symbol was not found in the semantic index: {symbol}");
        }

        if (affectedFiles.Count == 0)
            warnings.Add("Ticket does not reference any affected files.");

        if (affectedSymbols.Count == 0)
            warnings.Add("Ticket does not reference any affected symbols.");

        var confidence = CalculateConfidence(
            affectedFiles.Count,
            affectedSymbols.Count,
            warnings.Count,
            contextQualityScore);

        return CopyTicket(ticket, affectedFiles, affectedSymbols, warnings, confidence);
    }

    private static CodebaseTicketDraft CopyTicket(
        CodebaseTicketDraft source,
        List<string> affectedFiles,
        List<string> affectedSymbols,
        List<string> groundingWarnings,
        int confidenceScore)
    {
        return new CodebaseTicketDraft
        {
            Title = source.Title,
            Category = source.Category,
            Summary = source.Summary,
            Problem = source.Problem,
            ProposedChange = source.ProposedChange,
            WhyNow = source.WhyNow,
            Background = source.Background,
            AcceptanceCriteria = source.AcceptanceCriteria,
            Priority = source.Priority,
            TicketType = source.TicketType,
            AffectedFiles = affectedFiles,
            AffectedSymbols = affectedSymbols,
            Dependencies = CleanDistinct(source.Dependencies),
            SuggestedBuildOrder = source.SuggestedBuildOrder,
            RiskLevel = source.RiskLevel,
            ConfidenceScore = confidenceScore,
            GroundingWarnings = groundingWarnings
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            TestSuggestions = CleanDistinct(source.TestSuggestions),
            UnitTests = source.UnitTests,
            IntegrationTests = source.IntegrationTests,
            ManualTests = source.ManualTests,
            RegressionTests = source.RegressionTests,
            BuildValidation = source.BuildValidation
        };
    }

    private static int CalculateConfidence(
        int fileCount,
        int symbolCount,
        int warningCount,
        int contextQualityScore)
    {
        var score = 50;

        if (fileCount > 0)
            score += 20;

        if (symbolCount > 0)
            score += 20;

        score -= warningCount * 10;

        if (contextQualityScore > 0)
            score = Math.Min(score, contextQualityScore + 10);

        return Math.Clamp(score, 0, 100);
    }

    private static List<string> CleanDistinct(IEnumerable<string>? values)
    {
        return (values ?? Enumerable.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static SymbolKeySet BuildSymbolKeys(IReadOnlyList<SemanticSymbolInfo> symbols)
    {
        var fullyQualifiedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in symbols)
        {
            AddKey(fullyQualifiedNames, symbol.FullyQualifiedName);
            AddKey(signatures, symbol.Signature);

            if (!string.IsNullOrWhiteSpace(symbol.ContainerName) && !string.IsNullOrWhiteSpace(symbol.Name))
                AddKey(fullyQualifiedNames, $"{symbol.ContainerName}.{symbol.Name}");

            var name = NormalizeSymbol(symbol.Name);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            names[name] = names.TryGetValue(name, out var count) ? count + 1 : 1;
        }

        return new SymbolKeySet(
            fullyQualifiedNames,
            signatures,
            names
                .Where(pair => pair.Value == 1)
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private static void AddKey(HashSet<string> keys, string? value)
    {
        var normalized = NormalizeSymbol(value);
        if (!string.IsNullOrWhiteSpace(normalized))
            keys.Add(normalized);
    }

    private static bool FileExistsInSnapshot(string file, HashSet<string> knownFiles)
    {
        var normalized = NormalizePath(file);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return knownFiles.Contains(normalized) ||
               knownFiles.Any(key =>
                   key.EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase) ||
                   normalized.EndsWith("/" + key, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SymbolExistsInSnapshot(string symbol, SymbolKeySet keys)
    {
        var normalized = NormalizeSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return keys.FullyQualifiedNames.Contains(normalized) ||
               keys.Signatures.Contains(normalized) ||
               keys.UniqueNames.Contains(normalized) ||
               keys.FullyQualifiedNames.Any(key =>
                   key.EndsWith("." + normalized, StringComparison.OrdinalIgnoreCase) ||
                   normalized.EndsWith("." + key, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var cleaned = value.Trim().Trim('`', '"', '\'').Replace('\\', '/');
        var lastColon = cleaned.LastIndexOf(':');
        if (lastColon > 1 &&
            lastColon < cleaned.Length - 1 &&
            cleaned[(lastColon + 1)..].All(char.IsDigit))
        {
            cleaned = cleaned[..lastColon];
        }

        return cleaned.TrimStart('/');
    }

    private static string NormalizeSymbol(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var cleaned = value.Trim().Trim('`', '"', '\'');
        var parenIndex = cleaned.IndexOf('(');
        if (parenIndex > 0)
            cleaned = cleaned[..parenIndex];

        return cleaned.Trim();
    }

    private sealed record SymbolKeySet(
        HashSet<string> FullyQualifiedNames,
        HashSet<string> Signatures,
        HashSet<string> UniqueNames);
}
