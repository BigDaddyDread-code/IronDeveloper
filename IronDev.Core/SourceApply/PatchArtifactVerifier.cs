using System.Text.RegularExpressions;

namespace IronDev.Core.SourceApply;

public static class PatchArtifactVerifier
{
    public const long DefaultMaxPatchBytes = 1024 * 1024;

    public static PatchArtifactVerificationResult Verify(SourceApplyRunMetadata run, long maxPatchBytes = DefaultMaxPatchBytes)
    {
        var reasons = new List<string>();
        var runMetadataExists = File.Exists(Path.Combine(run.RunPath, "run.json"));
        if (!runMetadataExists)
            reasons.Add("MissingRunMetadata");

        var patchPath = Path.Combine(run.RunPath, "patch.diff");
        var patchExists = File.Exists(patchPath);
        if (!patchExists)
            reasons.Add("MissingPatchArtifact");

        var patchSha = patchExists ? SourceApplyHash.FileSha256(patchPath) : string.Empty;
        var expectedSha = string.IsNullOrWhiteSpace(run.PatchSha256) ? patchSha : run.PatchSha256.Trim();
        var hashMatches = patchExists && string.Equals(patchSha, expectedSha, StringComparison.OrdinalIgnoreCase);
        if (patchExists && !hashMatches)
            reasons.Add("PatchHashMismatch");

        if (patchExists)
        {
            var fileInfo = new FileInfo(patchPath);
            if (fileInfo.Length == 0 || string.IsNullOrWhiteSpace(File.ReadAllText(patchPath)))
                reasons.Add("EmptyPatch");
            if (fileInfo.Length > maxPatchBytes)
                reasons.Add("PatchTooLarge");
        }

        var changedFilesPath = Path.Combine(run.RunPath, "changed-files.txt");
        var changedFilesExist = File.Exists(changedFilesPath);
        if (!changedFilesExist)
            reasons.Add("MissingChangedFiles");

        var changedFilesFromArtifact = changedFilesExist
            ? File.ReadAllLines(changedFilesPath).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        var changedFilesFromPatch = patchExists ? ExtractChangedFilesFromPatch(File.ReadAllText(patchPath)) : [];
        var expectedChangedFiles = run.ChangedFiles.Length > 0
            ? run.ChangedFiles.Select(item => item.Trim()).Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray()
            : changedFilesFromPatch;

        if (changedFilesExist && changedFilesFromArtifact.Length == 0)
            reasons.Add("MissingChangedFiles");

        var changedFilesMatchRun = changedFilesExist && SequenceEqualIgnoreCase(changedFilesFromArtifact, expectedChangedFiles);
        if (changedFilesExist && !changedFilesMatchRun)
            reasons.Add("ChangedFilesMismatch");

        var manualApplyInstructionsExist = File.Exists(Path.Combine(run.RunPath, "manual-apply-instructions.md"));
        if (!manualApplyInstructionsExist)
            reasons.Add("MissingManualApplyInstructions");

        var baseCommitMatchesRun = !string.IsNullOrWhiteSpace(run.BaseCommit);
        if (!baseCommitMatchesRun)
            reasons.Add("MissingBaseCommit");

        foreach (var file in changedFilesFromPatch.Concat(changedFilesFromArtifact).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IsForbiddenPatchPath(file))
            {
                reasons.Add(file.Contains(".git", StringComparison.OrdinalIgnoreCase) ? "PatchTouchesGitDirectory" : "PatchContainsForbiddenPath");
                break;
            }
        }

        return new PatchArtifactVerificationResult
        {
            PatchArtifactVerificationId = $"patch_verify_{Guid.NewGuid():N}",
            RunId = run.RunId,
            PatchPath = patchPath,
            PatchExists = patchExists,
            PatchSha256 = patchSha,
            ExpectedPatchSha256 = expectedSha,
            PatchHashMatchesRun = hashMatches,
            RunMetadataExists = runMetadataExists,
            BaseCommitMatchesRun = baseCommitMatchesRun,
            ChangedFilesMatchRun = changedFilesMatchRun,
            ManualApplyInstructionsExist = manualApplyInstructionsExist,
            Decision = reasons.Count == 0 ? PatchArtifactVerificationDecision.Verified : PatchArtifactVerificationDecision.Blocked,
            Reasons = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            VerifiedAtUtc = DateTimeOffset.UtcNow,
            Boundary = SourceApplyBoundary.None
        };
    }

    public static string[] ExtractChangedFilesFromPatch(string patchText)
    {
        var files = new List<string>();
        foreach (Match match in Regex.Matches(patchText, @"^diff --git a/(.*?) b/(.*?)$", RegexOptions.Multiline))
        {
            var path = match.Groups[2].Value.Trim();
            if (!string.IsNullOrWhiteSpace(path))
                files.Add(path);
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsForbiddenPatchPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        var normalized = path.Replace('\\', '/').Trim();
        return normalized.StartsWith("/", StringComparison.Ordinal) ||
               normalized.Contains("../", StringComparison.Ordinal) ||
               normalized.StartsWith("../", StringComparison.Ordinal) ||
               normalized.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains(".ssh/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("secrets/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SequenceEqualIgnoreCase(string[] first, string[] second) =>
        first.Length == second.Length && first.Zip(second).All(pair => string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));
}
