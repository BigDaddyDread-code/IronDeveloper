using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Builder;

public static class BuilderProposalValidator
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".xaml",
        ".csproj",
        ".sln",
        ".slnx",
        ".json",
        ".md",
        ".config",
        ".xml",
        ".razor",
        ".css",
        ".html",
        ".js",
        ".ts",
        ".sql",
        ".editorconfig",
        ".props",
        ".targets",
        ".txt",
        ".yml",
        ".yaml"
    };

    public static void Validate(BuilderProposal proposal)
    {
        proposal.ValidationIssues.Clear();
        proposal.ValidationWarnings.Clear();

        if (proposal.Changes.Count == 0)
        {
            AddIssue(proposal, "Proposal did not include any file changes.");
            return;
        }

        foreach (var change in proposal.Changes)
        {
            change.IsValid = true;
            change.ValidationMessage = string.Empty;

            ValidateFileChange(proposal, change);
        }

        ValidateCoverage(proposal);
    }

    private static void ValidateFileChange(BuilderProposal proposal, ProposedFileChange change)
    {
        if (string.IsNullOrWhiteSpace(change.FilePath))
        {
            MarkInvalid(proposal, change, "Empty file path.");
            return;
        }

        if (Path.IsPathRooted(change.FilePath) ||
            change.FilePath.StartsWith("/", StringComparison.Ordinal) ||
            change.FilePath.StartsWith("\\", StringComparison.Ordinal))
        {
            MarkInvalid(proposal, change, "Absolute file paths are not allowed.");
            return;
        }

        if (change.FilePath.Contains("..", StringComparison.Ordinal))
        {
            MarkInvalid(proposal, change, "Path traversal (..) is not allowed.");
            return;
        }

        if (change.FilePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            MarkInvalid(proposal, change, "File path contains invalid characters.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(proposal.ProjectRoot) && !ResolvesInsideRoot(proposal.ProjectRoot, change.FilePath))
        {
            MarkInvalid(proposal, change, "Resolved file path is outside the project root.");
            return;
        }

        if (change.IsDeletion)
        {
            MarkInvalid(proposal, change, "File deletions are not allowed in v1.");
            return;
        }

        var extension = Path.GetExtension(change.FilePath);
        if (!string.IsNullOrWhiteSpace(extension) && !AllowedExtensions.Contains(extension))
        {
            MarkInvalid(proposal, change, $"File type '{extension}' is not allowed for builder proposals.");
            return;
        }

        if (string.IsNullOrWhiteSpace(change.Diff) && string.IsNullOrWhiteSpace(change.FullContentAfter))
        {
            MarkInvalid(proposal, change, "Proposal must include a diff or full replacement content.");
            return;
        }

        if (string.IsNullOrWhiteSpace(change.Description))
        {
            AddWarning(proposal, $"{change.FilePath}: missing change description.");
        }

        if (proposal.ProjectRoot.Contains("BookSeller", StringComparison.OrdinalIgnoreCase) &&
            (change.FilePath.Contains("IronDev", StringComparison.OrdinalIgnoreCase) ||
             change.FilePath.Contains("IronDeveloper", StringComparison.OrdinalIgnoreCase)))
        {
            MarkInvalid(proposal, change, "Cannot modify host files when active project is external.");
            return;
        }

        if (change.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            ValidateCSharpStructure(proposal, change);
        }
    }

    private static void ValidateCSharpStructure(BuilderProposal proposal, ProposedFileChange change)
    {
        var content = change.FullContentAfter;
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        if (content.Contains("namespace Sample", StringComparison.Ordinal) ||
            content.Contains("class Program", StringComparison.Ordinal) && !change.FilePath.Contains("Program.cs", StringComparison.OrdinalIgnoreCase))
        {
            MarkInvalid(proposal, change, "Proposal appears to be generic sample code.");
            return;
        }

        if (content.Contains("public class", StringComparison.Ordinal) &&
            !content.Contains("namespace ", StringComparison.Ordinal) &&
            !change.FilePath.Contains("Program.cs", StringComparison.OrdinalIgnoreCase))
        {
            AddWarning(proposal, $"{change.FilePath}: C# class has no namespace in full replacement content.");
        }

        if (!proposal.ProjectName.Contains("BookSeller", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (change.FilePath.Contains("BookService.cs", StringComparison.OrdinalIgnoreCase))
        {
            if (!content.Contains("namespace BookSeller.Core.Services", StringComparison.Ordinal))
            {
                MarkInvalid(proposal, change, "Missing namespace BookSeller.Core.Services.");
            }

            if (!content.Contains("class BookService", StringComparison.Ordinal))
            {
                MarkInvalid(proposal, change, "Missing class BookService.");
            }

            if (!content.Contains("IBookService", StringComparison.Ordinal))
            {
                MarkInvalid(proposal, change, "IBookService implementation removed.");
            }

            if (content.Contains("class Book", StringComparison.Ordinal) && !content.Contains("public class BookService", StringComparison.Ordinal))
            {
                MarkInvalid(proposal, change, "Duplicate Book model class detected in service file.");
            }
        }

        if (change.FilePath.Contains("Book.cs", StringComparison.OrdinalIgnoreCase) &&
            !content.Contains("namespace BookSeller.Core.Models", StringComparison.Ordinal))
        {
            MarkInvalid(proposal, change, "Missing namespace BookSeller.Core.Models in Book model.");
        }
    }

    private static void ValidateCoverage(BuilderProposal proposal)
    {
        var request = $"{proposal.OriginalRequest} {proposal.Summary}".ToLowerInvariant();
        var paths = proposal.Changes.Select(c => c.FilePath.ToLowerInvariant()).ToList();

        if (ContainsAny(request, "test", "tests", "unit test") && !paths.Any(IsTestPath))
        {
            AddIssue(proposal, "Ticket asks for tests, but the proposal does not modify any test files.");
        }

        if (ContainsAny(request, "database", "persistence", "persist", "dapper", "sql", "repository") &&
            !paths.Any(IsPersistencePath))
        {
            AddIssue(proposal, "Ticket asks for persistence or data access work, but the proposal does not include data access files.");
        }

        if (ContainsAny(request, "api", "endpoint", "controller") && !paths.Any(IsApiPath))
        {
            AddIssue(proposal, "Ticket asks for API work, but the proposal does not include controller, endpoint, or API files.");
        }

        var hasProductionCode = paths.Any(path => path.EndsWith(".cs", StringComparison.Ordinal) && !IsTestPath(path));
        if (hasProductionCode && !paths.Any(IsTestPath))
        {
            AddWarning(proposal, "Proposal changes production C# code without adding or updating tests.");
        }
    }

    private static bool ResolvesInsideRoot(string rootPath, string relativePath)
    {
        var fullRoot = Path.GetFullPath(rootPath);
        var fullTarget = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var normalizedRoot = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullTarget.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullTarget.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTestPath(string path)
    {
        return path.Contains("test", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("spec", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPersistencePath(string path)
    {
        return path.Contains("data", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("repository", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("persistence", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("database", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("db", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsApiPath(string path)
    {
        return path.Contains("controller", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("endpoint", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("api", StringComparison.OrdinalIgnoreCase);
    }

    private static void MarkInvalid(BuilderProposal proposal, ProposedFileChange change, string message)
    {
        change.IsValid = false;
        change.ValidationMessage = message;
        AddIssue(proposal, string.IsNullOrWhiteSpace(change.FilePath) ? message : $"{change.FilePath}: {message}");
    }

    private static void AddIssue(BuilderProposal proposal, string message)
    {
        if (!proposal.ValidationIssues.Contains(message, StringComparer.OrdinalIgnoreCase))
        {
            proposal.ValidationIssues.Add(message);
        }
    }

    private static void AddWarning(BuilderProposal proposal, string message)
    {
        if (!proposal.ValidationWarnings.Contains(message, StringComparer.OrdinalIgnoreCase))
        {
            proposal.ValidationWarnings.Add(message);
        }
    }
}
