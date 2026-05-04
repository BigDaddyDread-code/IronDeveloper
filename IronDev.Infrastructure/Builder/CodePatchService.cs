using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IronDev.Core.Builder;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Builder;

/// <summary>
/// Phase 4A: provides dry-run patch validation.
/// Does not write files. ApplyPatchesAsync is reserved for Phase 4B+.
/// </summary>
public sealed class CodePatchService : ICodePatchService
{
    // ── Git status ────────────────────────────────────────────────────────────

    public Task<GitStatus> GetGitStatusAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        // Phase 4B: run git status subprocess.
        return Task.FromResult(new GitStatus
        {
            HasUncommittedChanges = false,
            StatusOutput = "(git status check not yet implemented)"
        });
    }

    // ── Dry-run validation ────────────────────────────────────────────────────

    public Task<PatchValidationResult> DryRunValidateAsync(
        string projectPath,
        IReadOnlyList<FileChangeProposal> changes,
        CancellationToken cancellationToken = default)
    {
        var fileResults = new List<FilePatchValidation>();

        // Rule 1: proposal must have at least one file change
        if (changes == null || changes.Count == 0)
        {
            return Task.FromResult(new PatchValidationResult
            {
                AllValid    = false,
                Summary     = "Validation failed: proposal contains no file changes.",
                FileResults = fileResults
            });
        }

        var root = Path.GetFullPath(projectPath);

        foreach (var change in changes)
        {
            var result = ValidateChange(root, change);
            fileResults.Add(result);
        }

        var failed = fileResults.Where(r => !r.IsValid).ToList();
        bool allValid = failed.Count == 0;

        string summary = allValid
            ? $"Validation passed: {fileResults.Count} file change{(fileResults.Count == 1 ? "" : "s")} ready to apply."
            : $"Validation failed: {failed.Count} of {fileResults.Count} file change{(fileResults.Count == 1 ? "" : "s")} have errors.";

        return Task.FromResult(new PatchValidationResult
        {
            AllValid    = allValid,
            Summary     = summary,
            FileResults = fileResults
        });
    }

    private static FilePatchValidation ValidateChange(string rootPath, FileChangeProposal change)
    {
        // Rule 2: FilePath not empty
        if (string.IsNullOrWhiteSpace(change.FilePath))
        {
            return Fail(change.FilePath, "", "FilePath is empty.");
        }

        // Rule 3: resolved path must stay inside project root
        string resolved;
        try
        {
            resolved = Path.GetFullPath(Path.Combine(rootPath, change.FilePath));
        }
        catch (Exception ex)
        {
            return Fail(change.FilePath, "", $"FilePath could not be resolved: {ex.Message}");
        }

        if (!resolved.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(change.FilePath, resolved, "FilePath resolves outside the project root (path traversal).");
        }

        // Rule 4: file must exist
        if (!File.Exists(resolved))
        {
            return Fail(change.FilePath, resolved, $"File not found: {resolved}");
        }

        // Rule 5: BeforeSnippet not empty
        if (string.IsNullOrWhiteSpace(change.BeforeSnippet))
        {
            return Fail(change.FilePath, resolved, "BeforeSnippet is empty.");
        }

        // Rule 8: AfterSnippet not empty
        if (string.IsNullOrWhiteSpace(change.AfterSnippet))
        {
            return Fail(change.FilePath, resolved, "AfterSnippet is empty.");
        }

        // Rules 6 & 7: BeforeSnippet must exist exactly once
        string content = File.ReadAllText(resolved);
        int occurrences = CountOccurrences(content, change.BeforeSnippet);

        if (occurrences == 0)
        {
            return Fail(change.FilePath, resolved, "BeforeSnippet was not found in the file.");
        }

        if (occurrences > 1)
        {
            return Fail(change.FilePath, resolved,
                $"BeforeSnippet appears {occurrences} times; must appear exactly once to apply safely.");
        }

        return new FilePatchValidation
        {
            FilePath     = change.FilePath,
            ResolvedPath = resolved,
            IsValid      = true,
            Message      = "OK"
        };
    }

    private static FilePatchValidation Fail(string filePath, string resolved, string message) =>
        new() { FilePath = filePath, ResolvedPath = resolved, IsValid = false, Message = message };

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    // ── Apply (Phase 4B placeholder) ──────────────────────────────────────────

    public Task<PatchApplyResult> ApplyPatchesAsync(
        string projectPath,
        IReadOnlyList<FileChangeProposal> changes,
        CancellationToken cancellationToken = default)
    {
        // Phase 4B: write files after validation + developer approval.
        return Task.FromResult(PatchApplyResult.Failure(
            "ApplyPatchesAsync is not enabled yet — Phase 4B required."));
    }
}
