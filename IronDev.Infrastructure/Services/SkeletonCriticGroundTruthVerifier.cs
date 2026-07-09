using IronDev.Core.Builder;
using IronDev.Core.RunReports;
using IronDev.Core.Workspaces;
using IronDev.Infrastructure.Services.Workspaces;
using IronDev.Services;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Services;

/// <summary>
/// P1-2 — trust but verify. Establishes ground truth for a work package without
/// taking the courier's word for anything:
/// - the package on disk is re-hashed against the hash announced when the run
///   halted — a package changed after halt is not the package that halted;
/// - the package is checked for internal contradictions (claimed success versus
///   its own recorded exit codes; full-fidelity changes that carry no content);
/// - the claimed command evidence files are checked on disk — a claim whose
///   receipt is missing is a claim, not evidence;
/// - the proposed changes plus the authored tests are RE-EXECUTED in a fresh
///   disposable workspace, and the outcome is compared to what the package claims.
///
/// Boundary: this verifier is the deterministic harness around the critic —
/// not the critic agent. The boxed agent keeps RunTool and MutateSource forbidden and
/// consumes this verification as evidence. Verification grants nothing and blocks
/// nothing by itself: mismatches become findings, and the human gate stays separate.
/// </summary>
public sealed class SkeletonCriticGroundTruthVerifier : ISkeletonCriticGroundTruthVerifier
{
    public const string PackageHashCheck = SkeletonGroundTruthCheckNames.PackageHash;
    public const string InternalConsistencyCheck = SkeletonGroundTruthCheckNames.InternalConsistency;
    public const string CommandEvidenceCheck = SkeletonGroundTruthCheckNames.CommandEvidence;
    public const string CriterionCoverageCheck = SkeletonGroundTruthCheckNames.CriterionCoverage;
    public const string ReExecutionCheck = SkeletonGroundTruthCheckNames.ReExecution;

    private readonly IRunEventStore _events;
    private readonly IProjectService _projects;
    private readonly IDisposableWorkspaceExecutionService _workspaces;
    private readonly IConfiguration _configuration;

    public SkeletonCriticGroundTruthVerifier(
        IRunEventStore events,
        IProjectService projects,
        IDisposableWorkspaceExecutionService workspaces,
        IConfiguration configuration)
    {
        _events = events;
        _projects = projects;
        _workspaces = workspaces;
        _configuration = configuration;
    }

    public async Task<SkeletonGroundTruthVerification> VerifyAsync(
        string runId,
        SkeletonCriticPackage package,
        string packagePath,
        string packageSha256,
        CancellationToken cancellationToken = default)
    {
        var checks = new List<SkeletonGroundTruthCheck>
        {
            await CheckPackageHashAsync(runId, packageSha256, cancellationToken).ConfigureAwait(false),
            CheckInternalConsistency(package),
            CheckCommandEvidence(package),
            CheckCriterionCoverageRecord(package)
        };
        checks.Add(await ReExecuteAsync(runId, package, cancellationToken).ConfigureAwait(false));

        return new SkeletonGroundTruthVerification { Checks = checks };
    }

    /// <summary>The hash announced at halt is what any approval binds to; the disk must still agree.</summary>
    private async Task<SkeletonGroundTruthCheck> CheckPackageHashAsync(
        string runId,
        string packageSha256,
        CancellationToken cancellationToken)
    {
        var events = await _events.GetEventsAsync(runId, cancellationToken).ConfigureAwait(false);
        // REVISE-1 (DOGFOOD-2 finding F-I): the LAST announcement is the CURRENT
        // canonical package — a green revision re-prepares the package and
        // announces it again. Comparing against the first announcement made every
        // revised run a permanent blocking mismatch.
        var announced = events
            .Where(runEvent => runEvent.EventType == "CriticReviewPackageReady")
            .Select(runEvent => runEvent.Payload.TryGetValue("packageSha256", out var value) ? value : string.Empty)
            .LastOrDefault(value => !string.IsNullOrEmpty(value));

        if (string.IsNullOrEmpty(announced))
        {
            return new SkeletonGroundTruthCheck
            {
                CheckName = PackageHashCheck,
                Passed = false,
                Expected = "a package hash announced when the run halted",
                Actual = "no announcement found in the run's durable events",
                Detail = "Without the halt announcement the package cannot be bound to what the run produced. Verifiability itself is degraded.",
                BlocksMerge = false
            };
        }

        var matches = string.Equals(announced, packageSha256, StringComparison.Ordinal);
        return new SkeletonGroundTruthCheck
        {
            CheckName = PackageHashCheck,
            Passed = matches,
            Expected = announced,
            Actual = packageSha256,
            Detail = matches
                ? "The package on disk is byte-identical to the one the run announced at halt."
                : "The package on disk is NOT the package the run announced at halt — it changed after the fact.",
            BlocksMerge = !matches
        };
    }

    /// <summary>A package that contradicts itself needs no external evidence to be wrong.</summary>
    private static SkeletonGroundTruthCheck CheckInternalConsistency(SkeletonCriticPackage package)
    {
        var contradictions = new List<string>();

        var failedCommands = package.CommandResults
            .Where(command => command.ExitCode != 0 || command.TimedOut)
            .Select(command => $"{command.DisplayName} (exit {command.ExitCode}{(command.TimedOut ? ", timed out" : "")})")
            .ToList();
        if (package.WorkspaceRunSucceeded && failedCommands.Count > 0)
            contradictions.Add($"claims the workspace run succeeded while recording failed commands: {string.Join(", ", failedCommands)}");
        if (!package.WorkspaceRunSucceeded && package.CommandResults.Count > 0 && failedCommands.Count == 0)
            contradictions.Add("claims the workspace run failed while every recorded command exited 0");

        var contentless = package.Changes
            .Where(change => !change.IsDeletion && change.FullContentAfter is null)
            .Select(change => change.FilePath)
            .ToList();
        if (contentless.Count > 0)
            contradictions.Add($"claims full fidelity while these changes carry no content: {string.Join(", ", contentless)}");

        return new SkeletonGroundTruthCheck
        {
            CheckName = InternalConsistencyCheck,
            Passed = contradictions.Count == 0,
            Expected = "a package whose claims agree with its own recorded evidence",
            Actual = contradictions.Count == 0 ? "no contradictions" : string.Join("; ", contradictions),
            Detail = contradictions.Count == 0
                ? "The package's claims are consistent with its own recorded command results and change contents."
                : "The package contradicts itself.",
            BlocksMerge = contradictions.Count > 0
        };
    }

    /// <summary>
    /// P1-4: the coverage record must be what the calculator yields from the
    /// package's own criteria and tests. Coverage HOLES are honest review material
    /// for the human; a coverage record that DISAGREES with recomputation is a
    /// forgery — someone edited the matrix instead of writing the tests.
    /// </summary>
    private static SkeletonGroundTruthCheck CheckCriterionCoverageRecord(SkeletonCriticPackage package)
    {
        var recomputed = SkeletonCriterionCoverageCalculator.Compute(package.AcceptanceCriteria, package.AuthoredTests);
        var recorded = package.CriterionCoverage;

        static string Fingerprint(IReadOnlyList<SkeletonCriterionCoverage> coverage) =>
            string.Join(" | ", coverage
                .OrderBy(row => row.Criterion, StringComparer.OrdinalIgnoreCase)
                .Select(row => $"{row.Criterion}=>{row.Covered}:[{string.Join(",", row.CoveringTests.OrderBy(test => test, StringComparer.OrdinalIgnoreCase))}]"));

        var expected = Fingerprint(recomputed);
        var actual = Fingerprint(recorded);
        var honest = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

        return new SkeletonGroundTruthCheck
        {
            CheckName = CriterionCoverageCheck,
            Passed = honest,
            Expected = expected,
            Actual = actual,
            Detail = honest
                ? "The recorded criterion-coverage matrix matches independent recomputation from the package's own criteria and tests."
                : "The recorded criterion-coverage matrix does NOT match recomputation — the matrix was edited instead of the tests being written.",
            BlocksMerge = !honest
        };
    }

    /// <summary>A claim whose receipt is missing is a claim, not evidence.</summary>
    private static SkeletonGroundTruthCheck CheckCommandEvidence(SkeletonCriticPackage package)
    {
        var missing = package.CommandResults
            .SelectMany(command => new[] { command.StandardOutputRef, command.StandardErrorRef })
            .Where(evidenceRef => !string.IsNullOrEmpty(evidenceRef) && !File.Exists(evidenceRef))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new SkeletonGroundTruthCheck
        {
            CheckName = CommandEvidenceCheck,
            Passed = missing.Count == 0,
            Expected = "every claimed command output file present on disk",
            Actual = missing.Count == 0 ? "all present" : $"missing: {string.Join(", ", missing)}",
            Detail = missing.Count == 0
                ? "Every command result's claimed output evidence exists on disk."
                : "The package cites command evidence that does not exist.",
            BlocksMerge = false
        };
    }

    /// <summary>
    /// The decisive check: materialize the package's changes and authored tests in a
    /// fresh disposable workspace and run build + test again. What was claimed must
    /// reproduce from the package's own contents — nothing from the original run is reused.
    /// </summary>
    private async Task<SkeletonGroundTruthCheck> ReExecuteAsync(
        string runId,
        SkeletonCriticPackage package,
        CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(package.ProjectId, cancellationToken).ConfigureAwait(false);
        if (project is null || string.IsNullOrWhiteSpace(project.LocalPath) || !Directory.Exists(project.LocalPath))
        {
            return new SkeletonGroundTruthCheck
            {
                CheckName = ReExecutionCheck,
                Passed = false,
                Expected = "an independent re-execution of the package's changes and tests",
                Actual = "re-execution unavailable: the project's local path is missing",
                Detail = "The package's claims could not be independently reproduced. Unverified claims are weaker evidence and the review says so.",
                BlocksMerge = false
            };
        }

        var fileWrites = package.Changes
            .Where(change => change.IsDeletion || change.FullContentAfter is not null)
            .Select(change => new DisposableWorkspaceFileWrite
            {
                RelativePath = change.FilePath,
                Content = change.FullContentAfter,
                IsDeletion = change.IsDeletion
            })
            .Concat(package.AuthoredTests.Select(test => new DisposableWorkspaceFileWrite
            {
                RelativePath = test.RelativePath,
                Content = test.Content
            }))
            .ToList();

        DisposableWorkspaceRunResult reRun;
        try
        {
            reRun = await _workspaces.RunAsync(new DisposableWorkspaceRunRequest
            {
                RunId = $"{runId}-critic-verify",
                SourcePath = project.LocalPath,
                WorkspaceRoot = ResolveWorkspaceRoot(),
                EvidenceRoot = ResolveEvidenceRoot(),
                CleanWorkspaceOnSuccess = true,
                PreserveWorkspaceOnFailure = true,
                PreserveWorkspaceOnCancellation = true,
                FileWrites = fileWrites,
                Commands = DotNetCommandProfile.BuildAndTest(project.LocalPath, ReadTimeout("BuildTimeoutSeconds"), ReadTimeout("TestTimeoutSeconds"))
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SkeletonGroundTruthCheck
            {
                CheckName = ReExecutionCheck,
                Passed = false,
                Expected = "an independent re-execution of the package's changes and tests",
                Actual = $"re-execution failed to run: {exception.Message}",
                Detail = "The package's claims could not be independently reproduced. Unverified claims are weaker evidence and the review says so.",
                BlocksMerge = false
            };
        }

        var reproduced = reRun.Succeeded == package.WorkspaceRunSucceeded;
        return new SkeletonGroundTruthCheck
        {
            CheckName = ReExecutionCheck,
            Passed = reproduced,
            Expected = $"workspace run succeeded = {package.WorkspaceRunSucceeded} (the package's claim)",
            Actual = $"workspace run succeeded = {reRun.Succeeded} (independent re-execution; evidence: {reRun.EvidencePath})",
            Detail = reproduced
                ? "The package's build/test claim reproduces from its own contents in a fresh workspace."
                : "The package's build/test claim does NOT reproduce. The evidence disagrees with the courier.",
            BlocksMerge = !reproduced
        };
    }

    private int ReadTimeout(string key)
    {
        var value = _configuration[$"DisposableBuild:{key}"];
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : 120;
    }

    private string ResolveWorkspaceRoot()
    {
        var configured = _configuration["DisposableBuild:WorkspaceRoot"] ?? _configuration["LocalTest:WorkspaceRoot"];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevDisposableWorkspaces")
            : configured;
    }

    private string ResolveEvidenceRoot()
    {
        var configured = _configuration["DisposableBuild:EvidenceRoot"] ?? _configuration["LocalTest:LogsRoot"];
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "IronDevDisposableEvidence")
            : configured;
        return Path.Combine(root, "runs");
    }
}
