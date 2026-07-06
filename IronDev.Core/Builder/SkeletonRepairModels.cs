using IronDev.Core.Models;
using IronDev.Core.Workspaces;

namespace IronDev.Core.Builder;

/// <summary>
/// REPAIR-1 — bounded repair. When a skeleton run's disposable build/test fails,
/// the orchestrator may direct the Builder to attempt a repair: a NEW proposal
/// generated with the failure evidence in context, exercised in a fresh
/// attempt-scoped workspace.
///
/// Boundary: a repair attempt is proposal-shaped work, never authority. It cannot
/// approve, continue, or apply anything; the human gate is unchanged. Attempts are
/// bounded by explicit configuration (SkeletonRepair:MaxAttempts, default 0 = off),
/// and every attempt's evidence and events are preserved — attempt history is
/// never erased, because trust comes from seeing the mess.
/// </summary>
public enum SkeletonBuildFailureKind
{
    Unknown = 0,
    BuildFailed = 1,
    TestsFailed = 2,
    CommandTimedOut = 3
}

/// <summary>What failed, on which command, with a bounded evidence excerpt.</summary>
public sealed record SkeletonBuildFailureClassification
{
    public required SkeletonBuildFailureKind Kind { get; init; }
    public string FailedCommand { get; init; } = string.Empty;

    /// <summary>Bounded tail of the failing command's output, error lines preferred. Evidence, not judgment.</summary>
    public string Excerpt { get; init; } = string.Empty;
}

/// <summary>
/// Deterministic classification of a failed disposable workspace run. Pure
/// evidence shaping: it grants nothing and decides nothing — the orchestrator's
/// explicit attempt budget decides whether a repair is even attempted.
/// </summary>
public static class SkeletonBuildFailureClassifier
{
    private const int MaxExcerptChars = 4000;

    public static SkeletonBuildFailureClassification Classify(IReadOnlyList<DisposableWorkspaceCommandResult> commands)
    {
        var failed = commands.FirstOrDefault(command => command.TimedOut || command.ExitCode != 0);
        if (failed is null)
        {
            return new SkeletonBuildFailureClassification
            {
                Kind = SkeletonBuildFailureKind.Unknown,
                Excerpt = "No failing command was recorded; the workspace run failed for another reason."
            };
        }

        var kind = failed.TimedOut
            ? SkeletonBuildFailureKind.CommandTimedOut
            : LooksLikeTestCommand(failed)
                ? SkeletonBuildFailureKind.TestsFailed
                : LooksLikeBuildCommand(failed)
                    ? SkeletonBuildFailureKind.BuildFailed
                    : SkeletonBuildFailureKind.Unknown;

        return new SkeletonBuildFailureClassification
        {
            Kind = kind,
            FailedCommand = failed.DisplayName,
            Excerpt = BuildExcerpt(failed)
        };
    }

    private static bool LooksLikeTestCommand(DisposableWorkspaceCommandResult command) =>
        command.DisplayName.Contains("test", StringComparison.OrdinalIgnoreCase) ||
        command.Arguments.Any(argument => string.Equals(argument, "test", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeBuildCommand(DisposableWorkspaceCommandResult command) =>
        command.DisplayName.Contains("build", StringComparison.OrdinalIgnoreCase) ||
        command.Arguments.Any(argument => string.Equals(argument, "build", StringComparison.OrdinalIgnoreCase));

    private static string BuildExcerpt(DisposableWorkspaceCommandResult failed)
    {
        var combined = string.Join('\n', new[] { failed.StandardError, failed.StandardOutput }
            .Where(text => !string.IsNullOrWhiteSpace(text)));
        if (string.IsNullOrWhiteSpace(combined))
            return $"'{failed.DisplayName}' exited with code {failed.ExitCode} and produced no output.";

        // Prefer the lines that name the failure; fall back to the raw tail.
        var errorLines = combined.Split('\n')
            .Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("failed", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var excerpt = errorLines.Count > 0 ? string.Join('\n', errorLines) : combined;

        return excerpt.Length <= MaxExcerptChars ? excerpt : excerpt[^MaxExcerptChars..];
    }
}

/// <summary>The orchestrator's request for one bounded repair proposal.</summary>
public sealed record SkeletonRepairContext
{
    /// <summary>The attempt this repair produces (2 = first repair of a failed first attempt).</summary>
    public required int AttemptNumber { get; init; }

    public required SkeletonBuildFailureClassification Classification { get; init; }

    /// <summary>The proposal whose changes failed — the repair must see what it is repairing.</summary>
    public required BuilderProposal PreviousProposal { get; init; }
}
