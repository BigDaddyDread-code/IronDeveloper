using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.Validation;

public sealed class ValidationRunReceiptBuilder
{
    public ValidationRunReceipt Build(
        ValidationLanePlan plan,
        IEnumerable<ValidationProcessResult> results,
        string branch,
        string commitSha,
        bool worktreeCleanBefore,
        bool worktreeCleanAfter,
        IEnumerable<string>? skippedLanes = null,
        IEnumerable<string>? skippedLaneReasons = null,
        ValidationCachePolicy? cachePolicy = null,
        IEnumerable<ValidationChangedFileClassification>? dirtyChangedFiles = null)
    {
        var resultArray = results.ToArray();
        var skipped = (skippedLanes ?? Array.Empty<string>()).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        var dirtyChangedFileArray = (dirtyChangedFiles ?? Array.Empty<ValidationChangedFileClassification>()).ToArray();
        var failureKinds = resultArray
            .Select(result => result.FailureClassification)
            .Where(kind => kind != ValidationFailureKind.Passed)
            .Concat(SyntheticFailureKinds(worktreeCleanAfter, dirtyChangedFileArray))
            .Distinct()
            .ToArray();
        var verdict = DetermineVerdict(plan, resultArray, skipped, failureKinds);
        var started = resultArray.Length == 0 ? DateTimeOffset.UtcNow : resultArray.Min(result => result.StartedUtc);
        var finished = resultArray.Length == 0 ? started : resultArray.Max(result => result.FinishedUtc);

        return new ValidationRunReceipt
        {
            ValidationRunId = "validation_run_" + Guid.NewGuid().ToString("N")[..12],
            ValidationPlanId = plan.ValidationPlanId,
            Branch = BlankToUnknown(branch),
            CommitSha = BlankToUnknown(commitSha),
            ChangedFilesHash = HashChangedFiles(plan.ChangedFiles),
            StartedUtc = started,
            FinishedUtc = finished,
            Verdict = verdict,
            RequiredLanes = plan.Lanes.Where(lane => lane.Requirement == ValidationLaneRequirement.Required).ToArray(),
            RecommendedLanes = plan.Lanes.Where(lane => lane.Requirement == ValidationLaneRequirement.Recommended).ToArray(),
            DeferredLanes = plan.Lanes.Where(lane => lane.Requirement == ValidationLaneRequirement.Deferred).ToArray(),
            Results = resultArray,
            FailureClassifications = failureKinds,
            SkippedLanes = skipped,
            SkippedLaneReasons = (skippedLaneReasons ?? Array.Empty<string>()).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
            DirtyChangedFiles = dirtyChangedFileArray,
            WorktreeCleanBefore = worktreeCleanBefore,
            WorktreeCleanAfter = worktreeCleanAfter,
            CachePolicy = cachePolicy ?? new ValidationCachePolicy(),
            Boundary = ValidationRuntimeBoundary.Evidence
        };
    }

    private static ValidationRunVerdict DetermineVerdict(
        ValidationLanePlan plan,
        ValidationProcessResult[] results,
        string[] skippedLanes,
        ValidationFailureKind[] failureKinds)
    {
        if (failureKinds.Any(kind => kind is ValidationFailureKind.EnvironmentAccessDenied or ValidationFailureKind.RestoreFailed))
            return ValidationRunVerdict.Blocked;

        var requiredLaneNames = plan.Lanes
            .Where(lane => lane.Requirement == ValidationLaneRequirement.Required)
            .Select(lane => lane.Name)
            .ToArray();
        var executedLaneNames = results
            .Select(result => result.LaneName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredSkipped = skippedLanes.Any(skipped => requiredLaneNames.Contains(skipped, StringComparer.OrdinalIgnoreCase));
        var requiredMissing = requiredLaneNames.Any(required => !executedLaneNames.Contains(required));

        if (requiredSkipped || requiredMissing)
            return ValidationRunVerdict.Incomplete;

        if (failureKinds.Length > 0)
            return ValidationRunVerdict.Failed;

        return ValidationRunVerdict.Passed;
    }

    private static string HashChangedFiles(IEnumerable<string> changedFiles)
    {
        var payload = string.Join('\n', changedFiles.Order(StringComparer.OrdinalIgnoreCase));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BlankToUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();

    private static IEnumerable<ValidationFailureKind> SyntheticFailureKinds(bool worktreeCleanAfter, ValidationChangedFileClassification[] dirtyChangedFiles)
    {
        if (dirtyChangedFiles.Length > 0)
            yield return ValidationFailureKind.DirtyGeneratedArtifacts;
        else if (!worktreeCleanAfter)
            yield return ValidationFailureKind.UnknownFailure;
    }
}

public sealed class ValidationReceiptWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<ValidationReceiptWriteResult> WriteAsync(string artifactsRoot, ValidationRunReceipt receipt, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(artifactsRoot);
        var receiptPath = Path.Combine(artifactsRoot, "validation-receipt.json");
        var summaryPath = Path.Combine(artifactsRoot, "validation-summary.md");
        await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(receipt, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(summaryPath, RenderSummary(receipt), cancellationToken).ConfigureAwait(false);
        return new ValidationReceiptWriteResult
        {
            Receipt = receipt,
            ReceiptPath = receiptPath,
            SummaryPath = summaryPath
        };
    }

    public static string RenderSummary(ValidationRunReceipt receipt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# BK0 Validation Receipt");
        builder.AppendLine();
        builder.AppendLine($"Run: {receipt.ValidationRunId}");
        builder.AppendLine($"Plan: {receipt.ValidationPlanId}");
        builder.AppendLine($"Verdict: {receipt.Verdict}");
        builder.AppendLine($"Branch: {receipt.Branch}");
        builder.AppendLine($"Commit: {receipt.CommitSha}");
        builder.AppendLine($"Changed files hash: {receipt.ChangedFilesHash}");
        builder.AppendLine();
        builder.AppendLine("Boundary: validation evidence does not approve, merge, release, deploy, mutate source, promote memory, satisfy policy, or continue workflow.");
        builder.AppendLine();
        builder.AppendLine("Required lanes:");
        foreach (var lane in receipt.RequiredLanes)
            builder.AppendLine($"- {lane.Name}: {lane.Reason}");
        builder.AppendLine();
        builder.AppendLine("Results:");
        foreach (var result in receipt.Results)
            builder.AppendLine($"- {result.LaneName}: {result.FailureClassification}, exit={result.ExitCode?.ToString() ?? "none"}, durationMs={result.DurationMs}");
        if (receipt.SkippedLanes.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Skipped lanes:");
            foreach (var skipped in receipt.SkippedLanes)
                builder.AppendLine($"- {skipped}");
        }
        if (!receipt.WorktreeCleanBefore || !receipt.WorktreeCleanAfter)
        {
            builder.AppendLine();
            builder.AppendLine($"Worktree clean before: {receipt.WorktreeCleanBefore}");
            builder.AppendLine($"Worktree clean after: {receipt.WorktreeCleanAfter}");
        }
        if (receipt.DirtyChangedFiles.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Dirty generated/temp files:");
            foreach (var dirty in receipt.DirtyChangedFiles)
                builder.AppendLine($"- {dirty.Path}: {dirty.Kind}");
        }
        if (receipt.FailureClassifications.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Failure classifications:");
            foreach (var failure in receipt.FailureClassifications)
                builder.AppendLine($"- {failure}");
        }

        return builder.ToString();
    }

}
