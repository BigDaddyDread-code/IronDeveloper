using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Sandbox;
using IronDev.Core.Workbench;
using IronDev.Infrastructure.Services.Sandbox;

namespace IronDev.Infrastructure.Services;

public sealed class RepositoryReadinessObservationException(string reasonCode, string message)
    : Exception(message)
{
    public const string ErrorCode = "repository_readiness_observation_failed";
    public string ReasonCode { get; } = reasonCode;
}

/// <summary>
/// Observes only the server-owned repository path through the already-hardened provisioning Git
/// runner.  Porcelain output is reduced to a boolean and a hash in memory and is never returned or
/// persisted.  The immutable index input is derived solely from deterministic ls-tree metadata.
/// </summary>
public sealed class RepositoryReadinessObserver : IRepositoryReadinessObserver
{
    private static readonly DateTime DeterministicGitEnvironmentTime = DateTime.UnixEpoch;
    private readonly IRepositoryProvisioningGitRunner _git;
    private readonly ISandboxSourceSnapshotBuilder _snapshots;

    public RepositoryReadinessObserver(
        IRepositoryProvisioningGitRunner git,
        ISandboxSourceSnapshotBuilder snapshots)
    {
        _git = git;
        _snapshots = snapshots;
    }

    public async Task<RepositoryObservationResult> ObserveAsync(
        ObserveRepositoryStateRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);

        var head = await RunAsync(
            request.CanonicalRepositoryPath,
            ["rev-parse", "--verify", "HEAD"],
            "RepositoryHeadUnavailable",
            cancellationToken).ConfigureAwait(false);
        var headCommit = OneObjectId(head.StandardOutput, "repository HEAD");

        var tree = await RunAsync(
            request.CanonicalRepositoryPath,
            ["rev-parse", "--verify", "HEAD^{tree}"],
            "RepositoryTreeUnavailable",
            cancellationToken).ConfigureAwait(false);
        var gitTreeId = OneObjectId(tree.StandardOutput, "repository tree");

        var status = await RunAsync(
            request.CanonicalRepositoryPath,
            ["status", "--porcelain=v1", "--untracked-files=all", "--no-renames"],
            "RepositoryStatusUnavailable",
            cancellationToken).ConfigureAwait(false);
        var rawStatus = NormalizeProcessText(status.StandardOutput);
        var worktreeState = rawStatus.Length == 0
            ? RepositoryWorktreeStates.Clean
            : RepositoryWorktreeStates.Dirty;
        var statusSha256 = Sha256(rawStatus);

        var listing = await RunAsync(
            request.CanonicalRepositoryPath,
            ["ls-tree", "-r", "-z", "--full-tree", "HEAD"],
            "RepositoryTreeUnavailable",
            cancellationToken).ConfigureAwait(false);
        var sources = ParseSources(listing.StandardOutput);
        var indexedContentSha256 = CodeIndexSnapshotCodec.ComputeIndexedContentHash(sources);
        var fingerprint = SnapshotCompatibleFingerprint(request, gitTreeId) ??
                          RepositoryReadinessCanonicalJson.Sha256(
                              RepositoryReadinessCanonicalJson.Serialize(new
                              {
                                  schemaVersion = 1,
                                  request.RepositoryBindingId,
                                  request.RepositoryBindingRevision,
                                  baselineCommit = request.BaselineCommit.ToLowerInvariant(),
                                  headCommit,
                                  gitTreeId,
                                  worktreeState,
                                  statusSha256,
                                  indexedContentSha256
                              }));
        var observedAtUtc = DateTimeOffset.UtcNow;
        var observation = new RepositoryStateObservation
        {
            Id = Guid.NewGuid(),
            RepositoryBindingId = request.RepositoryBindingId,
            RepositoryBindingRevision = request.RepositoryBindingRevision,
            BaselineCommit = request.BaselineCommit.ToLowerInvariant(),
            HeadCommit = headCommit,
            GitTreeId = gitTreeId,
            WorktreeState = worktreeState,
            WorktreeFingerprint = fingerprint,
            ObservedAtUtc = observedAtUtc,
            EvidenceHash = new string('0', 64)
        };
        observation = observation with
        {
            EvidenceHash = RepositoryStateObservationCodec.ComputeEvidenceHash(observation)
        };
        return new RepositoryObservationResult(
            RepositoryStateObservationCodec.NormalizeAndValidate(observation),
            sources);
    }

    private async Task<RepositoryProvisioningGitResult> RunAsync(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        RepositoryProvisioningGitResult result;
        try
        {
            result = await _git.RunAsync(
                repositoryPath,
                arguments,
                DeterministicGitEnvironmentTime,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is RepositoryProvisioningExecutionException or
                                           RepositoryProvisioningIntegrityException)
        {
            throw new RepositoryReadinessObservationException(
                reasonCode,
                "The controlled repository observation could not be completed.");
        }

        if (result.ExitCode != 0)
            throw new RepositoryReadinessObservationException(
                reasonCode,
                "The controlled repository observation did not succeed.");
        return result;
    }

    private static IReadOnlyList<CodeIndexSourceFingerprint> ParseSources(string output)
    {
        if (output is null)
            throw InvalidTree();
        var entries = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length > CodeIndexSnapshotCodec.MaximumSources)
            throw new RepositoryReadinessObservationException(
                "RepositoryTreeTooLarge",
                "The repository tree exceeds the bounded v0.1 index size.");

        var result = new List<CodeIndexSourceFingerprint>(entries.Length);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var separator = entry.IndexOf('\t');
            if (separator <= 0 || separator == entry.Length - 1)
                throw InvalidTree();
            var header = entry[..separator].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var relativePath = entry[(separator + 1)..];
            if (header.Length != 3 || !IsGitMode(header[0]) ||
                header[1] is not "blob" and not "commit" ||
                !IsGitObjectId(header[2]) || !IsSafeRelativePath(relativePath) ||
                !paths.Add(relativePath))
                throw InvalidTree();

            result.Add(new CodeIndexSourceFingerprint(
                index + 1,
                relativePath,
                RepositoryReadinessCanonicalJson.Sha256(
                    $"git-tree-source-v1\n{header[0]}\n{header[1]}\n{header[2].ToLowerInvariant()}")));
        }
        return result;
    }

    private static string OneObjectId(string output, string label)
    {
        var lines = NormalizeProcessText(output)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length != 1 || !IsGitObjectId(lines[0]))
            throw new RepositoryReadinessObservationException(
                "RepositoryIdentityInvalid",
                $"The controlled {label} result was invalid.");
        return lines[0].ToLowerInvariant();
    }

    private static string NormalizeProcessText(string value) =>
        (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\r', '\n');

    private static bool IsGitObjectId(string value) =>
        value.Length is 40 or 64 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool IsGitMode(string value) =>
        value.Length == 6 && value.All(static character => character is >= '0' and <= '7');

    private static bool IsSafeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > CodeIndexSnapshotCodec.MaximumRelativePathLength ||
            value != value.Trim() || Path.IsPathRooted(value) ||
            value.Contains('\\') || value.Any(static character => char.IsControl(character)))
            return false;
        return value.Split('/').All(static segment => segment is not "" and not "." and not "..");
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private string? SnapshotCompatibleFingerprint(
        ObserveRepositoryStateRequest request,
        string gitTreeId)
    {
        if (request.ProvisioningManifestJson is null && request.ProvisioningManifestSha256 is null)
            return null;
        if (string.IsNullOrWhiteSpace(request.ProvisioningManifestJson) ||
            string.IsNullOrWhiteSpace(request.ProvisioningManifestSha256))
            throw InvalidSnapshotAuthority();
        try
        {
            return _snapshots.Describe(new SandboxSourceSnapshotRequest(
                    request.RepositoryBindingId,
                    request.CanonicalRepositoryPath,
                    request.BaselineCommit.ToLowerInvariant(),
                    gitTreeId,
                    request.ProvisioningManifestJson,
                    request.ProvisioningManifestSha256,
                    request.CanonicalRepositoryPath))
                .WorktreeFingerprint;
        }
        catch (SandboxContractValidationException)
        {
            throw InvalidSnapshotAuthority();
        }
    }

    private static RepositoryReadinessObservationException InvalidTree() => new(
        "RepositoryTreeInvalid",
        "The controlled repository tree listing was invalid.");

    private static RepositoryReadinessObservationException InvalidSnapshotAuthority() => new(
        "RepositorySnapshotAuthorityInvalid",
        "The controlled repository snapshot authority was invalid.");

    private static void Validate(ObserveRepositoryStateRequest request)
    {
        if (request.ProjectId <= 0 || request.RepositoryBindingId == Guid.Empty ||
            request.RepositoryBindingRevision <= 0 || string.IsNullOrWhiteSpace(request.CanonicalRepositoryPath) ||
            !Path.IsPathFullyQualified(request.CanonicalRepositoryPath) ||
            !Directory.Exists(request.CanonicalRepositoryPath))
            throw new RepositoryReadinessObservationException(
                "RepositoryPathUnavailable",
                "The server-owned repository path is unavailable.");
        try
        {
            RepositoryReadinessCanonicalJson.NormalizeGitObjectId(
                request.BaselineCommit,
                nameof(request.BaselineCommit));
        }
        catch (RepositoryReadinessValidationException)
        {
            throw new RepositoryReadinessObservationException(
                "RepositoryBaselineInvalid",
                "The server-owned repository baseline is invalid.");
        }
    }
}
