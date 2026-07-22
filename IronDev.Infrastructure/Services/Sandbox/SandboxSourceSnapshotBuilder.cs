using System.Buffers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IronDev.Core.Sandbox;
using Microsoft.Win32.SafeHandles;

[assembly: InternalsVisibleTo("IronDev.IntegrationTests")]

namespace IronDev.Infrastructure.Services.Sandbox;

public sealed record SandboxSourceSnapshotRequest(
    Guid ExecutionId,
    string RepositoryPath,
    string BaselineCommit,
    string GitTreeId,
    string ProvisioningManifestJson,
    string ProvisioningManifestSha256,
    string SnapshotRoot);

public sealed class SandboxSourceSnapshot
{
    internal SandboxSourceSnapshot(
        Guid executionId,
        string snapshotRoot,
        string sourcePath,
        string provisioningManifestSha256,
        string worktreeFingerprint,
        int fileCount,
        long totalBytes)
    {
        ExecutionId = executionId;
        SnapshotRoot = snapshotRoot;
        SourcePath = sourcePath;
        ProvisioningManifestSha256 = provisioningManifestSha256;
        WorktreeFingerprint = worktreeFingerprint;
        FileCount = fileCount;
        TotalBytes = totalBytes;
    }

    public Guid ExecutionId { get; }
    public string SnapshotRoot { get; }
    public string SourcePath { get; }
    public string ProvisioningManifestSha256 { get; }
    public string WorktreeFingerprint { get; }
    public int FileCount { get; }
    public long TotalBytes { get; }
}

public sealed record SandboxSourceSnapshotIdentity(
    string WorktreeFingerprint,
    int FileCount,
    long TotalBytes);

public sealed class SandboxSourceSnapshotRecoveryRequest
{
    internal SandboxSourceSnapshotRecoveryRequest(
        Guid executionId,
        string provisioningManifestSha256,
        string snapshotRoot)
    {
        ExecutionId = executionId;
        ProvisioningManifestSha256 = provisioningManifestSha256;
        SnapshotRoot = snapshotRoot;
    }

    public Guid ExecutionId { get; }
    public string ProvisioningManifestSha256 { get; }
    public string SnapshotRoot { get; }
}

internal enum SandboxSourceSnapshotCoordinationStage
{
    SourceHandleOpened,
    DestinationDirectoryReady,
    DestinationDirectoryGuarded
}

internal interface ISandboxSourceSnapshotCoordination
{
    Task ObserveAsync(
        SandboxSourceSnapshotCoordinationStage stage,
        string path,
        CancellationToken cancellationToken);
}

public interface ISandboxSourceSnapshotBuilder
{
    SandboxSourceSnapshotIdentity Describe(SandboxSourceSnapshotRequest request);

    Task<SandboxSourceSnapshot> CreateOrRecoverAsync(
        SandboxSourceSnapshotRequest request,
        CancellationToken cancellationToken = default);

    bool Cleanup(SandboxSourceSnapshot snapshot);

    bool CleanupRecovered(SandboxSourceSnapshotRecoveryRequest request);
}

public sealed class SandboxSourceSnapshotCleanupException(string message, Exception innerException)
    : Exception(message, innerException);

/// <summary>
/// Copies only the exact files in the immutable PR-05B provisioning manifest into an
/// owned snapshot. Live repository metadata, .git, untracked files and reparse targets
/// never enter the sandbox mount.
/// </summary>
public sealed class SandboxSourceSnapshotBuilder : ISandboxSourceSnapshotBuilder
{
    private const int MaximumFiles = 10_000;
    private const long MaximumTotalBytes = 1024L * 1024L * 1024L;
    private const string OwnershipMarkerName = "snapshot-owner.json";
    private readonly ISandboxSourceSnapshotCoordination _coordination;

    public SandboxSourceSnapshotBuilder()
        : this(NoopSandboxSourceSnapshotCoordination.Instance)
    {
    }

    internal SandboxSourceSnapshotBuilder(ISandboxSourceSnapshotCoordination coordination)
    {
        _coordination = coordination ?? throw new ArgumentNullException(nameof(coordination));
    }

    public SandboxSourceSnapshotIdentity Describe(SandboxSourceSnapshotRequest request)
    {
        Validate(request);
        var manifest = ParseManifest(request.ProvisioningManifestJson, request.ProvisioningManifestSha256);
        return Describe(request, manifest);
    }

    public async Task<SandboxSourceSnapshot> CreateOrRecoverAsync(
        SandboxSourceSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var repository = NormalizeExistingDirectory(request.RepositoryPath);
        var snapshotRoot = NormalizeOrCreateRoot(request.SnapshotRoot);
        if (PathsOverlap(repository, snapshotRoot))
            throw new SandboxContractValidationException(
                "The sandbox snapshot root must not overlap the authoritative repository.");

        var manifest = ParseManifest(request.ProvisioningManifestJson, request.ProvisioningManifestSha256);
        var identity = Describe(request, manifest);
        var ownerPath = Path.Combine(snapshotRoot, request.ExecutionId.ToString("N"));
        var sourcePath = Path.Combine(ownerPath, "source");
        var ownerJson = OwnerJson(
            request.ExecutionId,
            request.ProvisioningManifestSha256,
            snapshotRoot);
        var ownershipSidecarPath = OwnershipSidecarPath(ownerPath);
        EnsureDirectChild(snapshotRoot, ownerPath);
        if (Directory.Exists(ownerPath) || File.Exists(ownerPath) ||
            File.Exists(ownershipSidecarPath) || File.Exists(PreparedMarkerPath(ownershipSidecarPath)))
        {
            if (!TryCleanupOwned(
                    snapshotRoot,
                    ownerPath,
                    request.ExecutionId,
                    request.ProvisioningManifestSha256))
                throw new SandboxContractValidationException(
                    "The sandbox snapshot path is occupied by an unknown resource.");
        }
        if (File.Exists(ownerPath))
            throw new SandboxContractValidationException(
                "The sandbox snapshot path is occupied by an unknown file.");

        long totalBytes = 0;
        try
        {
            // Publish recoverable ownership outside the not-yet-created owner directory first.
            // The sidecar is replaced only by a fully flushed marker inside the owned tree.
            await WriteAtomicTextFileAsync(
                snapshotRoot,
                ownershipSidecarPath,
                ownerJson,
                cancellationToken).ConfigureAwait(false);
            CreateSafeDirectoryChain(snapshotRoot, sourcePath);
            await WriteAtomicTextFileAsync(
                snapshotRoot,
                Path.Combine(ownerPath, OwnershipMarkerName),
                ownerJson,
                cancellationToken).ConfigureAwait(false);
            if (!TryDeleteExactMarker(snapshotRoot, ownershipSidecarPath, ownerJson))
                throw new SandboxContractValidationException(
                    "The temporary source snapshot ownership marker could not be retired safely.");

            foreach (var file in manifest.Files.OrderBy(file => file.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();
                totalBytes = checked(totalBytes + file.Utf8ByteLength);
                if (totalBytes > MaximumTotalBytes)
                    throw new SandboxContractValidationException(
                        "The repository snapshot exceeds the fixed source-size bound.");

                var source = ResolveSafeChild(repository, file.RelativePath);
                if (!WindowsSandboxPathValidator.TryNormalizeExistingFile(source, out source, out _))
                    throw new SandboxContractValidationException(
                        "A repository source file is missing, unsafe, or uses a reparse point.");

                var destination = ResolveSafeChild(sourcePath, file.RelativePath);
                var destinationDirectory = Path.GetDirectoryName(destination)!;
                CreateSafeDirectoryChain(sourcePath, destinationDirectory);
                using var sourceGuard = OpenDirectoryGuard(repository, Path.GetDirectoryName(source)!);
                await using var input = new FileStream(
                    sourceGuard.OpenExistingFile(Path.GetFileName(source), FileAccess.Read),
                    FileAccess.Read,
                    64 * 1024,
                    isAsync: true);
                if (input.Length != file.Utf8ByteLength)
                    throw new SandboxContractValidationException(
                        "A repository source file changed after provisioning.");
                await _coordination.ObserveAsync(
                    SandboxSourceSnapshotCoordinationStage.SourceHandleOpened,
                    source,
                    cancellationToken).ConfigureAwait(false);

                await _coordination.ObserveAsync(
                    SandboxSourceSnapshotCoordinationStage.DestinationDirectoryReady,
                    destinationDirectory,
                    cancellationToken).ConfigureAwait(false);
                using var destinationGuard = OpenDirectoryGuard(sourcePath, destinationDirectory);
                await _coordination.ObserveAsync(
                    SandboxSourceSnapshotCoordinationStage.DestinationDirectoryGuarded,
                    destinationDirectory,
                    cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(
                    destinationGuard.CreateNewFile(Path.GetFileName(destination)),
                    FileAccess.Write,
                    64 * 1024,
                    isAsync: true);
                var copied = await CopyAndHashAsync(input, output, cancellationToken).ConfigureAwait(false);
                sourceGuard.VerifyFileStillBound(input.SafeFileHandle, source);
                destinationGuard.VerifyFileStillBound(output.SafeFileHandle, destination);
                if (copied.ByteLength != file.Utf8ByteLength || output.Length != file.Utf8ByteLength)
                    throw new SandboxContractValidationException(
                        "A repository source file changed while its snapshot was being created.");
                if (!string.Equals(copied.Sha256, file.Sha256, StringComparison.Ordinal))
                    throw new SandboxContractValidationException(
                        "A repository source file no longer matches the qualified baseline manifest.");
            }

            EnsureSafeExistingDirectory(snapshotRoot, sourcePath);
            if (!IsClosedOwnedTree(ownerPath))
                throw new SandboxContractValidationException(
                    "The repository snapshot acquired an unsafe filesystem entry while it was being created.");

            var fingerprint = SandboxCanonicalJson.Sha256(
                "workbench-sandbox-source-snapshot-v1\n" +
                request.BaselineCommit + "\n" + request.GitTreeId + "\n" +
                request.ProvisioningManifestSha256 + "\n" + totalBytes);
            return new SandboxSourceSnapshot(
                request.ExecutionId,
                snapshotRoot,
                sourcePath,
                request.ProvisioningManifestSha256,
                fingerprint,
                identity.FileCount,
                identity.TotalBytes);
        }
        catch (Exception exception)
        {
            if (!TryCleanupOwned(
                    snapshotRoot,
                    ownerPath,
                    request.ExecutionId,
                    request.ProvisioningManifestSha256))
                throw new SandboxSourceSnapshotCleanupException(
                    "The owned source snapshot could not be removed after snapshot creation failed.",
                    exception);
            throw;
        }
    }

    public bool Cleanup(SandboxSourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        try
        {
            if (!Path.IsPathFullyQualified(snapshot.SnapshotRoot) ||
                !WindowsSandboxPathValidator.TryNormalizeExistingDirectory(
                    snapshot.SnapshotRoot,
                    out var snapshotRoot,
                    out _))
                return false;
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(snapshotRoot, snapshot.SnapshotRoot, comparison))
                return false;
            var manifestSha256 = SandboxCanonicalJson.NormalizeSha256(
                snapshot.ProvisioningManifestSha256,
                nameof(snapshot.ProvisioningManifestSha256));
            var source = Path.GetFullPath(snapshot.SourcePath);
            var owner = Directory.GetParent(source)?.FullName;
            if (owner is null || !string.Equals(Path.GetFileName(source), "source", StringComparison.Ordinal) ||
                !Guid.TryParseExact(Path.GetFileName(owner), "N", out var executionId) ||
                executionId != snapshot.ExecutionId ||
                !string.Equals(owner, Path.Combine(snapshotRoot, snapshot.ExecutionId.ToString("N")), comparison) ||
                !string.Equals(source, Path.Combine(owner, "source"), comparison))
                return false;
            return TryCleanupOwned(
                snapshotRoot,
                owner,
                snapshot.ExecutionId,
                manifestSha256);
        }
        catch
        {
            return false;
        }
    }

    public bool CleanupRecovered(SandboxSourceSnapshotRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExecutionId == Guid.Empty || string.IsNullOrWhiteSpace(request.SnapshotRoot))
            return false;
        try
        {
            SandboxCanonicalJson.NormalizeSha256(
                request.ProvisioningManifestSha256,
                nameof(request.ProvisioningManifestSha256));
            if (!Path.IsPathFullyQualified(request.SnapshotRoot))
                return false;
            var root = Path.GetFullPath(request.SnapshotRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(root))
                return true;
            if (string.Equals(root, Path.GetPathRoot(root)?.TrimEnd('\\', '/'),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                !WindowsSandboxPathValidator.TryNormalizeExistingDirectory(root, out root, out _))
                return false;
            var ownerPath = Path.Combine(root, request.ExecutionId.ToString("N"));
            EnsureDirectChild(root, ownerPath);
            return TryCleanupOwned(
                root,
                ownerPath,
                request.ExecutionId,
                request.ProvisioningManifestSha256);
        }
        catch
        {
            return false;
        }
    }

    private static ProvisioningManifest ParseManifest(string json, string expectedHash)
    {
        var normalizedHash = SandboxCanonicalJson.NormalizeSha256(expectedHash, nameof(expectedHash));
        if (!string.Equals(SandboxCanonicalJson.Sha256(json), normalizedHash, StringComparison.Ordinal))
            throw new SandboxContractValidationException(
                "The repository provisioning manifest failed its immutable hash check.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1 ||
                !root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array ||
                files.GetArrayLength() is < 1 or > MaximumFiles)
                throw new JsonException("The provisioning manifest shape is invalid.");

            var result = new List<ProvisioningFile>(files.GetArrayLength());
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orders = new HashSet<int>();
            foreach (var element in files.EnumerateArray())
            {
                var order = element.GetProperty("order").GetInt32();
                var relativePath = NormalizeRelativePath(element.GetProperty("path").GetString() ?? string.Empty);
                var hash = SandboxCanonicalJson.NormalizeSha256(
                    element.GetProperty("sha256").GetString() ?? string.Empty,
                    "provisioning file hash");
                var length = element.GetProperty("utf8ByteLength").GetInt64();
                if (order <= 0 || length < 0 || !orders.Add(order) || !paths.Add(relativePath))
                    throw new JsonException("The provisioning manifest contains duplicate or invalid files.");
                result.Add(new ProvisioningFile(order, relativePath, hash, length));
            }
            return new ProvisioningManifest(result);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new SandboxContractValidationException(
                "The repository provisioning manifest is unreadable or unsafe.");
        }
    }

    private static string ResolveSafeChild(string root, string relativePath)
    {
        var canonicalRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(
            canonicalRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, comparison))
            throw new SandboxContractValidationException("A sandbox snapshot path escaped its owned root.");
        return path;
    }

    private static string NormalizeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathFullyQualified(value))
            throw new SandboxContractValidationException("A provisioning file path is unsafe.");
        var normalized = value.Trim().Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment =>
                segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new SandboxContractValidationException("A provisioning file path is unsafe.");
        return string.Join('/', segments);
    }

    private static void Validate(SandboxSourceSnapshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExecutionId == Guid.Empty || !IsLowerHex(request.BaselineCommit, 40) ||
            !IsLowerHex(request.GitTreeId, 40) ||
            string.IsNullOrWhiteSpace(request.RepositoryPath) ||
            string.IsNullOrWhiteSpace(request.SnapshotRoot))
            throw new SandboxContractValidationException(
                "An exact execution, repository baseline, Git tree, and snapshot root are required.");
    }

    private static string NormalizeExistingDirectory(string value)
    {
        if (!WindowsSandboxPathValidator.TryNormalizeExistingDirectory(value, out var path, out _))
            throw new SandboxContractValidationException("The authoritative repository path is unavailable or unsafe.");
        return path;
    }

    private static string NormalizeOrCreateRoot(string value)
    {
        if (!Path.IsPathFullyQualified(value))
            throw new SandboxContractValidationException("An absolute snapshot root is required.");
        var path = Path.GetFullPath(value)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(path, Path.GetPathRoot(path)?.TrimEnd('\\', '/'),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new SandboxContractValidationException("A filesystem root cannot be a snapshot root.");
        if (File.Exists(path))
            throw new SandboxContractValidationException(
                "The snapshot root is occupied by an unexpected file.");
        if (!Directory.Exists(path))
        {
            var existingAncestor = Directory.GetParent(path)?.FullName;
            while (!string.IsNullOrWhiteSpace(existingAncestor) && !Directory.Exists(existingAncestor))
            {
                if (File.Exists(existingAncestor))
                    throw new SandboxContractValidationException(
                        "A snapshot root ancestor is occupied by an unexpected file.");
                existingAncestor = Directory.GetParent(existingAncestor)?.FullName;
            }
            if (string.IsNullOrWhiteSpace(existingAncestor))
                throw new SandboxContractValidationException(
                    "The snapshot root has no safely inspectable existing ancestor.");
            CreateSafeDirectoryChain(existingAncestor, path);
        }
        if (!WindowsSandboxPathValidator.TryNormalizeExistingDirectory(path, out var canonicalPath, out _))
            throw new SandboxContractValidationException(
                "The snapshot root or one of its ancestors cannot be a reparse point.");
        return canonicalPath;
    }

    private static void CreateSafeDirectoryChain(string root, string destinationDirectory)
    {
        var canonicalRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var canonicalDestination = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!SameOrUnder(canonicalDestination, canonicalRoot, comparison))
            throw new SandboxContractValidationException(
                "A sandbox snapshot directory escaped its owned root.");

        EnsureSafeExistingDirectory(canonicalRoot, canonicalRoot);
        if (string.Equals(canonicalDestination, canonicalRoot, comparison))
            return;

        if (OperatingSystem.IsWindows())
        {
            using var guard = WindowsSnapshotDirectoryGuard.CreateChain(
                canonicalRoot,
                canonicalDestination);
            return;
        }

        var relative = Path.GetRelativePath(canonicalRoot, canonicalDestination);
        var current = canonicalRoot;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
                throw new SandboxContractValidationException(
                    "A sandbox snapshot directory escaped its owned root.");
            var next = Path.Combine(current, segment);
            EnsureSafeExistingDirectory(canonicalRoot, current);
            if (File.Exists(next))
                throw new SandboxContractValidationException(
                    "A sandbox snapshot directory is occupied by an unexpected file.");
            if (!Directory.Exists(next))
                Directory.CreateDirectory(next);
            EnsureSafeExistingDirectory(canonicalRoot, next);
            current = next;
        }
    }

    private static void EnsureSafeExistingDirectory(string root, string directory)
    {
        var expected = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var canonicalRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!SameOrUnder(expected, canonicalRoot, comparison) ||
            !WindowsSandboxPathValidator.TryNormalizeExistingDirectory(expected, out var inspected, out _) ||
            !string.Equals(inspected, expected, comparison))
            throw new SandboxContractValidationException(
                "A sandbox snapshot write ancestor is missing, unsafe, or uses a reparse point.");
    }

    private static async Task WriteAtomicTextFileAsync(
        string root,
        string path,
        string value,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(path) ?? throw new SandboxContractValidationException(
            "A sandbox snapshot file has no owned parent.");
        var temporaryPath = PreparedMarkerPath(path);
        using var guard = OpenDirectoryGuard(root, parent);
        var temporaryName = Path.GetFileName(temporaryPath);
        var finalName = Path.GetFileName(path);
        if (guard.NodeExists(temporaryName) || guard.NodeExists(finalName))
            throw new SandboxContractValidationException(
                "The source snapshot ownership marker publication path is already occupied.");
        try
        {
            await using (var stream = new FileStream(
                             guard.CreateNewFile(temporaryName),
                             FileAccess.Write,
                             16 * 1024,
                             isAsync: true))
            {
                await stream.WriteAsync(new UTF8Encoding(false).GetBytes(value), cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
                guard.VerifyFileStillBound(stream.SafeFileHandle, temporaryPath);
                guard.PublishCreatedFile(
                    stream.SafeFileHandle,
                    temporaryPath,
                    finalName);
            }

            if (!MarkerMatches(root, path, value))
                throw new SandboxContractValidationException(
                    "The source snapshot ownership marker failed atomic publication verification.");
        }
        finally
        {
            TryDeletePreparedMarker(root, temporaryPath);
        }
    }

    private static async Task<CopiedFile> CopyAndHashAsync(
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long byteLength = 0;
        try
        {
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                    break;
                byteLength = checked(byteLength + read);
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return new CopiedFile(
                byteLength,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static void EnsureDirectChild(string root, string path)
    {
        if (!string.Equals(Directory.GetParent(path)?.FullName, root,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new SandboxContractValidationException("The snapshot owner path is not a direct child of its root.");
    }

    private static bool PathsOverlap(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return SameOrUnder(left, right, comparison) || SameOrUnder(right, left, comparison);
    }

    private static bool SameOrUnder(string candidate, string root, StringComparison comparison) =>
        string.Equals(candidate, root, comparison) ||
        candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison);

    private static string OwnerJson(
        Guid executionId,
        string provisioningManifestSha256,
        string snapshotRoot) => SandboxCanonicalJson.Serialize(new
    {
        schemaVersion = 1,
        executionId,
        provisioningManifestSha256 = SandboxCanonicalJson.NormalizeSha256(
            provisioningManifestSha256,
            nameof(provisioningManifestSha256)),
        snapshotRootSha256 = SnapshotRootAuthority(snapshotRoot)
    });

    private static string OwnershipSidecarPath(string ownerPath) => Path.Combine(
        Directory.GetParent(ownerPath)?.FullName ?? throw new SandboxContractValidationException(
            "The source snapshot owner has no approved root."),
        $".{Path.GetFileName(ownerPath)}.{OwnershipMarkerName}");

    private static string PreparedMarkerPath(string markerPath) => markerPath + ".prepared";

    private static string SnapshotRootAuthority(string snapshotRoot)
    {
        var canonicalRoot = Path.GetFullPath(snapshotRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (OperatingSystem.IsWindows())
            canonicalRoot = canonicalRoot.ToUpperInvariant();
        return SandboxCanonicalJson.Sha256(
            "workbench-sandbox-source-snapshot-root-v1\n" + canonicalRoot);
    }

    private static bool MarkerMatches(string root, string markerPath, string expected)
    {
        try
        {
            if (!File.Exists(markerPath))
                return false;
            var parent = Path.GetDirectoryName(markerPath);
            if (string.IsNullOrWhiteSpace(parent))
                return false;
            using var guard = OpenDirectoryGuard(root, parent);
            using var stream = new FileStream(
                guard.OpenExistingFile(Path.GetFileName(markerPath), FileAccess.Read),
                FileAccess.Read,
                bufferSize: 4 * 1024,
                isAsync: true);
            if (stream.Length != Encoding.UTF8.GetByteCount(expected))
                return false;
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4 * 1024,
                leaveOpen: true);
            var actual = reader.ReadToEnd();
            guard.VerifyFileStillBound(stream.SafeFileHandle, markerPath);
            return string.Equals(actual, expected, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeleteExactMarker(string root, string markerPath, string expected)
    {
        try
        {
            if (!File.Exists(markerPath))
                return true;
            if (!MarkerMatches(root, markerPath, expected))
                return false;
            File.Delete(markerPath);
            return !File.Exists(markerPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryDeletePreparedMarker(string root, string path)
    {
        try
        {
            if (!File.Exists(path))
                return !Directory.Exists(path);
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(parent))
                return false;
            using (var guard = OpenDirectoryGuard(root, parent))
            {
                using var handle = guard.OpenExistingFile(Path.GetFileName(path), FileAccess.Read);
                guard.VerifyFileStillBound(handle, path);
            }
            File.Delete(path);
            return !File.Exists(path) && !Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCleanupOwned(
        string snapshotRoot,
        string ownerPath,
        Guid executionId,
        string provisioningManifestSha256)
    {
        try
        {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var canonicalRoot = Path.GetFullPath(snapshotRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var canonicalOwner = Path.GetFullPath(ownerPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(
                    canonicalOwner,
                    Path.Combine(canonicalRoot, executionId.ToString("N")),
                    comparison) ||
                !string.Equals(Directory.GetParent(canonicalOwner)?.FullName, canonicalRoot, comparison))
                return false;

            var expected = OwnerJson(executionId, provisioningManifestSha256, canonicalRoot);
            var marker = Path.Combine(ownerPath, OwnershipMarkerName);
            var sidecar = OwnershipSidecarPath(ownerPath);
            var sidecarPrepared = PreparedMarkerPath(sidecar);
            var markerExists = File.Exists(marker);
            var sidecarExists = File.Exists(sidecar);
            var sidecarPreparedExists = File.Exists(sidecarPrepared);
            if (File.Exists(ownerPath) ||
                markerExists && !MarkerMatches(canonicalRoot, marker, expected) ||
                sidecarExists && !MarkerMatches(canonicalRoot, sidecar, expected) ||
                Directory.Exists(sidecarPrepared))
                return false;
            if (!Directory.Exists(ownerPath))
            {
                var sidecarDeleted = !sidecarExists ||
                    TryDeleteExactMarker(canonicalRoot, sidecar, expected);
                var preparedDeleted = !sidecarPreparedExists ||
                    TryDeletePreparedMarker(canonicalRoot, sidecarPrepared);
                return sidecarDeleted && preparedDeleted;
            }
            if ((!markerExists && !sidecarExists) || !IsClosedOwnedTree(ownerPath))
                return false;
            Directory.Delete(ownerPath, recursive: true);
            if (Directory.Exists(ownerPath) ||
                sidecarExists && !TryDeleteExactMarker(canonicalRoot, sidecar, expected) ||
                sidecarPreparedExists && !TryDeletePreparedMarker(canonicalRoot, sidecarPrepared))
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static SandboxSourceSnapshotIdentity Describe(
        SandboxSourceSnapshotRequest request,
        ProvisioningManifest manifest)
    {
        long totalBytes = 0;
        foreach (var file in manifest.Files)
        {
            totalBytes = checked(totalBytes + file.Utf8ByteLength);
            if (totalBytes > MaximumTotalBytes)
                throw new SandboxContractValidationException(
                    "The repository snapshot exceeds the fixed source-size bound.");
        }
        return new SandboxSourceSnapshotIdentity(
            SandboxCanonicalJson.Sha256(
                "workbench-sandbox-source-snapshot-v1\n" +
                request.BaselineCommit + "\n" + request.GitTreeId + "\n" +
                request.ProvisioningManifestSha256 + "\n" + totalBytes),
            manifest.Files.Count,
            totalBytes);
    }

    private static bool IsLowerHex(string value, int length) =>
        value?.Length == length && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsClosedOwnedTree(string ownerPath)
    {
        try
        {
            if ((File.GetAttributes(ownerPath) & FileAttributes.ReparsePoint) != 0)
                return false;
            var entries = 0;
            var pending = new Stack<string>();
            pending.Push(ownerPath);
            while (pending.Count > 0)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    if (++entries > MaximumFiles * 3)
                        return false;
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        return false;
                    if ((attributes & FileAttributes.Directory) != 0)
                        pending.Push(entry);
                }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ISnapshotDirectoryGuard OpenDirectoryGuard(string root, string directory)
    {
        if (OperatingSystem.IsWindows())
            return WindowsSnapshotDirectoryGuard.Open(root, directory);
        return ManagedSnapshotDirectoryGuard.Open(root, directory);
    }

    private interface ISnapshotDirectoryGuard : IDisposable
    {
        bool NodeExists(string fileName);
        SafeFileHandle OpenExistingFile(string fileName, FileAccess access);
        SafeFileHandle CreateNewFile(string fileName);
        void PublishCreatedFile(
            SafeFileHandle handle,
            string expectedCurrentPath,
            string destinationFileName);
        void VerifyFileStillBound(SafeFileHandle handle, string expectedPath);
    }

    /// <summary>
    /// Holds every directory in the path without FILE_SHARE_DELETE and verifies
    /// each handle's final path immediately before a file open. A platform that
    /// still permits a directory rename while the handle is held is therefore
    /// rejected before the textual path can resolve through its replacement.
    /// File handles are opened with FILE_FLAG_OPEN_REPARSE_POINT and their final
    /// paths are checked before use.
    /// </summary>
    private sealed class WindowsSnapshotDirectoryGuard : ISnapshotDirectoryGuard
    {
        private const uint FileReadAttributes = 0x0080;
        private const uint FileTraverse = 0x0020;
        private const uint DeleteAccess = 0x00010000;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const int CreateNew = 1;
        private const int OpenExisting = 3;
        private const int ErrorFileNotFound = 2;
        private const int ErrorPathNotFound = 3;
        private const uint ObjectCaseInsensitive = 0x00000040;
        private const uint ObjectDontReparse = 0x00001000;
        private const uint NtFileOpen = 1;
        private const uint NtFileCreate = 2;
        private const uint FileWriteThrough = 0x00000002;
        private const uint FileSequentialOnly = 0x00000004;
        private const uint FileDirectoryFile = 0x00000001;
        private const uint FileNonDirectoryFile = 0x00000040;
        private const uint FileOpenReparsePoint = 0x00200000;
        private const uint NtFileOpenIf = 3;

        private readonly List<VerifiedDirectory> _directoryHandles = [];

        private WindowsSnapshotDirectoryGuard(
            string root,
            string directory,
            bool createMissingDirectories)
        {
            var canonicalRoot = Canonical(root);
            var canonicalDirectory = Canonical(directory);
            var comparison = StringComparison.OrdinalIgnoreCase;
            if (!SameOrUnder(canonicalDirectory, canonicalRoot, comparison))
                throw new SandboxContractValidationException(
                    "A snapshot no-follow directory escaped its approved root.");

            CurrentPath = canonicalRoot;
            _directoryHandles.Add(new VerifiedDirectory(
                OpenAndVerifyDirectory(canonicalRoot),
                canonicalRoot));
            if (string.Equals(canonicalDirectory, canonicalRoot, comparison))
                return;

            foreach (var segment in Path.GetRelativePath(canonicalRoot, canonicalDirectory)
                         .Split(
                             [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                             StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment is "." or "..")
                    throw new SandboxContractValidationException(
                        "A snapshot no-follow directory escaped its approved root.");
                var next = Path.Combine(CurrentPath, segment);
                var handle = OpenRelativeDirectory(
                    _directoryHandles[^1].Handle,
                    segment,
                    next,
                    createMissingDirectories);
                _directoryHandles.Add(new VerifiedDirectory(
                    handle,
                    next));
                CurrentPath = next;
            }
        }

        private string CurrentPath { get; set; }

        public static WindowsSnapshotDirectoryGuard Open(string root, string directory) =>
            new(root, directory, createMissingDirectories: false);

        public static WindowsSnapshotDirectoryGuard CreateChain(string root, string directory) =>
            new(root, directory, createMissingDirectories: true);

        public bool NodeExists(string fileName)
        {
            ValidateLeaf(fileName);
            VerifyDirectoriesStillBound();
            try
            {
                using var handle = OpenNode(
                    Path.Combine(CurrentPath, fileName),
                    FileReadAttributes,
                    FileShare.ReadWrite,
                    OpenExisting,
                    FileFlagOpenReparsePoint | FileFlagBackupSemantics,
                    allowMissing: true);
                return handle is not null;
            }
            catch (Win32Exception exception)
            {
                throw Violation(
                    $"A snapshot node could not be inspected without following reparse points: {exception.Message}");
            }
        }

        public SafeFileHandle OpenExistingFile(string fileName, FileAccess access)
        {
            ValidateLeaf(fileName);
            VerifyDirectoriesStillBound();
            var desiredAccess = access switch
            {
                FileAccess.Read => GenericRead | FileReadAttributes,
                FileAccess.ReadWrite => GenericRead | GenericWrite | FileReadAttributes,
                _ => throw new NotSupportedException($"Snapshot file access '{access}' is not supported.")
            };
            var path = Path.Combine(CurrentPath, fileName);
            try
            {
                var handle = OpenRelativeFile(
                    fileName,
                    desiredAccess,
                    NtFileOpen,
                    FileSequentialOnly | FileNonDirectoryFile | FileOpenReparsePoint);
                VerifyFile(handle, path);
                VerifyDirectoriesStillBound();
                return handle;
            }
            catch (SandboxContractValidationException)
            {
                throw;
            }
            catch (Win32Exception exception)
            {
                throw Violation(
                    $"A snapshot file could not be opened through its verified no-follow handle: {exception.Message}");
            }
        }

        public SafeFileHandle CreateNewFile(string fileName)
        {
            ValidateLeaf(fileName);
            VerifyDirectoriesStillBound();
            var path = Path.Combine(CurrentPath, fileName);
            try
            {
                var handle = OpenRelativeFile(
                    fileName,
                    GenericRead | GenericWrite | DeleteAccess | FileReadAttributes,
                    NtFileCreate,
                    FileWriteThrough | FileSequentialOnly | FileNonDirectoryFile | FileOpenReparsePoint);
                VerifyFile(handle, path);
                VerifyDirectoriesStillBound();
                return handle;
            }
            catch (SandboxContractValidationException)
            {
                throw;
            }
            catch (Win32Exception exception)
            {
                throw Violation(
                    $"A snapshot file could not be created through its verified parent handle: {exception.Message}");
            }
        }

        public void PublishCreatedFile(
            SafeFileHandle handle,
            string expectedCurrentPath,
            string destinationFileName)
        {
            ValidateLeaf(destinationFileName);
            VerifyDirectoriesStillBound();
            VerifyFile(handle, expectedCurrentPath);
            RenameRelative(
                handle,
                _directoryHandles[^1].Handle,
                destinationFileName);
            var destinationPath = Path.Combine(CurrentPath, destinationFileName);
            VerifyFile(handle, destinationPath);
            VerifyDirectoriesStillBound();
        }

        public void VerifyFileStillBound(SafeFileHandle handle, string expectedPath) =>
            VerifyFile(handle, expectedPath);

        public void Dispose()
        {
            for (var index = _directoryHandles.Count - 1; index >= 0; index--)
                _directoryHandles[index].Handle.Dispose();
        }

        private void VerifyDirectoriesStillBound()
        {
            foreach (var directory in _directoryHandles)
                VerifyFinalPath(directory.Handle, directory.ExpectedPath);
        }

        private static SafeFileHandle OpenAndVerifyDirectory(string path)
        {
            SafeFileHandle handle;
            try
            {
                handle = OpenNode(
                    path,
                    FileReadAttributes,
                    FileShare.ReadWrite,
                    OpenExisting,
                    FileFlagOpenReparsePoint | FileFlagBackupSemantics,
                    allowMissing: false)!;
            }
            catch (Win32Exception exception)
            {
                throw Violation(
                    $"A snapshot directory could not be opened without following reparse points: {exception.Message}");
            }

            try
            {
                var attributes = (FileAttributes)GetAttributeInfo(handle).FileAttributes;
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw Violation($"A snapshot directory is a reparse point: {path}");
                if (!attributes.HasFlag(FileAttributes.Directory))
                    throw Violation($"A snapshot path component is not a directory: {path}");
                VerifyFinalPath(handle, path);
                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        private static void VerifyFile(SafeFileHandle handle, string expectedPath)
        {
            var attributes = (FileAttributes)GetAttributeInfo(handle).FileAttributes;
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                throw Violation($"A snapshot file is a symbolic link or reparse point: {expectedPath}");
            if (attributes.HasFlag(FileAttributes.Directory))
                throw Violation($"A snapshot file path resolved to a directory: {expectedPath}");
            VerifyFinalPath(handle, expectedPath);
        }

        private static void VerifyFinalPath(SafeFileHandle handle, string expectedPath)
        {
            var buffer = new StringBuilder(512);
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0)
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"Could not resolve verified snapshot path '{expectedPath}'.");
            if (length >= buffer.Capacity)
            {
                buffer = new StringBuilder((int)length + 1);
                length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0)
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        $"Could not resolve verified snapshot path '{expectedPath}'.");
            }

            var finalPath = NormalizeFinalPath(buffer.ToString());
            if (!string.Equals(Canonical(finalPath), Canonical(expectedPath), StringComparison.OrdinalIgnoreCase))
                throw Violation(
                    $"A verified snapshot handle resolved outside its expected path. " +
                    $"Expected '{expectedPath}', resolved '{finalPath}'.");
        }

        private static SafeFileHandle? OpenNode(
            string path,
            uint desiredAccess,
            FileShare share,
            int creationDisposition,
            uint flags,
            bool allowMissing)
        {
            var handle = CreateFileW(
                path,
                desiredAccess,
                share,
                IntPtr.Zero,
                creationDisposition,
                flags,
                IntPtr.Zero);
            if (!handle.IsInvalid)
                return handle;

            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (allowMissing && error is ErrorFileNotFound or ErrorPathNotFound)
                return null;
            throw new Win32Exception(error, $"Could not open snapshot path without following reparse points: {path}");
        }

        private SafeFileHandle OpenRelativeFile(
            string fileName,
            uint desiredAccess,
            uint createDisposition,
            uint createOptions) => OpenRelativeNode(
                _directoryHandles[^1].Handle,
                fileName,
                desiredAccess,
                (uint)FileShare.Read,
                createDisposition,
                createOptions,
                "file");

        private static SafeFileHandle OpenRelativeDirectory(
            SafeFileHandle parent,
            string directoryName,
            string expectedPath,
            bool createIfMissing)
        {
            SafeFileHandle handle;
            try
            {
                handle = OpenRelativeNode(
                    parent,
                    directoryName,
                    FileReadAttributes | FileTraverse,
                    (uint)FileShare.ReadWrite,
                    createIfMissing ? NtFileOpenIf : NtFileOpen,
                    FileDirectoryFile | FileOpenReparsePoint |
                    (createIfMissing ? FileWriteThrough : 0),
                    "directory");
            }
            catch (Win32Exception exception)
            {
                throw Violation(
                    $"A snapshot directory could not be opened or created relative to its verified parent handle: " +
                    exception.Message);
            }

            try
            {
                var attributes = (FileAttributes)GetAttributeInfo(handle).FileAttributes;
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw Violation($"A snapshot directory is a reparse point: {expectedPath}");
                if (!attributes.HasFlag(FileAttributes.Directory))
                    throw Violation($"A snapshot path component is not a directory: {expectedPath}");
                VerifyFinalPath(handle, expectedPath);
                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        private static SafeFileHandle OpenRelativeNode(
            SafeFileHandle parent,
            string nodeName,
            uint desiredAccess,
            uint shareAccess,
            uint createDisposition,
            uint createOptions,
            string nodeKind)
        {
            var nameBuffer = Marshal.StringToHGlobalUni(nodeName);
            var unicodeStringPointer = IntPtr.Zero;
            SafeFileHandle? handle = null;
            try
            {
                var unicodeString = new UnicodeString
                {
                    Length = checked((ushort)Encoding.Unicode.GetByteCount(nodeName)),
                    MaximumLength = checked((ushort)(Encoding.Unicode.GetByteCount(nodeName) + sizeof(char))),
                    Buffer = nameBuffer
                };
                unicodeStringPointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
                Marshal.StructureToPtr(unicodeString, unicodeStringPointer, fDeleteOld: false);
                var attributes = new ObjectAttributes
                {
                    Length = Marshal.SizeOf<ObjectAttributes>(),
                    RootDirectory = parent.DangerousGetHandle(),
                    ObjectName = unicodeStringPointer,
                    Attributes = ObjectCaseInsensitive | ObjectDontReparse
                };
                var status = NtCreateFile(
                    out handle,
                    desiredAccess,
                    ref attributes,
                    out _,
                    IntPtr.Zero,
                    (uint)FileAttributes.Normal,
                    shareAccess,
                    createDisposition,
                    createOptions,
                    IntPtr.Zero,
                    0);
                if (status < 0 || handle is null || handle.IsInvalid)
                {
                    handle?.Dispose();
                    var error = RtlNtStatusToDosError(status);
                    throw new Win32Exception(
                        unchecked((int)error),
                        $"Could not open snapshot {nodeKind} relative to its verified directory handle " +
                        $"(NTSTATUS 0x{status:x8}).");
                }
                return handle;
            }
            finally
            {
                if (unicodeStringPointer != IntPtr.Zero)
                    Marshal.FreeHGlobal(unicodeStringPointer);
                Marshal.FreeHGlobal(nameBuffer);
            }
        }

        private static void RenameRelative(
            SafeFileHandle file,
            SafeFileHandle destinationDirectory,
            string destinationFileName)
        {
            var nameBytes = Encoding.Unicode.GetBytes(destinationFileName);
            var nameOffset = Marshal.OffsetOf<FileRenameInfoLayout>(
                nameof(FileRenameInfoLayout.FileName)).ToInt32();
            var buffer = Marshal.AllocHGlobal(checked(nameOffset + nameBytes.Length));
            try
            {
                var value = new FileRenameInfoLayout
                {
                    Flags = 0,
                    RootDirectory = destinationDirectory.DangerousGetHandle(),
                    FileNameLength = checked((uint)nameBytes.Length),
                    FileName = '\0'
                };
                Marshal.StructureToPtr(value, buffer, fDeleteOld: false);
                Marshal.Copy(nameBytes, 0, IntPtr.Add(buffer, nameOffset), nameBytes.Length);
                var status = NtSetInformationFile(
                    file,
                    out _,
                    buffer,
                    checked((uint)(nameOffset + nameBytes.Length)),
                    NtFileInformationClass.FileRenameInformation);
                if (status < 0)
                {
                    var error = RtlNtStatusToDosError(status);
                    throw new Win32Exception(
                        unchecked((int)error),
                        $"Could not atomically publish the snapshot ownership marker through its open handle " +
                        $"(NTSTATUS 0x{status:x8}, Win32 error {error}).");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static FileAttributeTagInfo GetAttributeInfo(SafeFileHandle handle)
        {
            if (!GetFileInformationByHandleEx(
                    handle,
                    FileInfoByHandleClass.FileAttributeTagInfo,
                    out var info,
                    (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Could not inspect snapshot path attributes.");
            return info;
        }

        private static string Canonical(string path) =>
            Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static string NormalizeFinalPath(string path)
        {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                return @"\\" + path[8..];
            if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                return path[4..];
            return path;
        }

        private static void ValidateLeaf(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                Path.IsPathRooted(fileName) ||
                !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
                fileName is "." or "..")
                throw Violation("A snapshot file name must be one safe path segment.");
        }

        private static SandboxContractValidationException Violation(string message) => new(message);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            FileShare shareMode,
            IntPtr securityAttributes,
            int creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(
            SafeFileHandle file,
            StringBuilder filePath,
            uint filePathLength,
            uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle file,
            FileInfoByHandleClass fileInformationClass,
            out FileAttributeTagInfo fileInformation,
            uint bufferSize);

        [DllImport("ntdll.dll")]
        private static extern int NtCreateFile(
            out SafeFileHandle fileHandle,
            uint desiredAccess,
            ref ObjectAttributes objectAttributes,
            out IoStatusBlock ioStatusBlock,
            IntPtr allocationSize,
            uint fileAttributes,
            uint shareAccess,
            uint createDisposition,
            uint createOptions,
            IntPtr eaBuffer,
            uint eaLength);

        [DllImport("ntdll.dll")]
        private static extern int NtSetInformationFile(
            SafeFileHandle fileHandle,
            out IoStatusBlock ioStatusBlock,
            IntPtr fileInformation,
            uint length,
            NtFileInformationClass fileInformationClass);

        [DllImport("ntdll.dll")]
        private static extern uint RtlNtStatusToDosError(int status);

        private enum FileInfoByHandleClass
        {
            FileAttributeTagInfo = 9
        }

        private enum NtFileInformationClass
        {
            FileRenameInformation = 10
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileAttributeTagInfo
        {
            public uint FileAttributes;
            public uint ReparseTag;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ObjectAttributes
        {
            public int Length;
            public IntPtr RootDirectory;
            public IntPtr ObjectName;
            public uint Attributes;
            public IntPtr SecurityDescriptor;
            public IntPtr SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoStatusBlock
        {
            public IntPtr Status;
            public IntPtr Information;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileRenameInfoLayout
        {
            public uint Flags;
            public IntPtr RootDirectory;
            public uint FileNameLength;
            public char FileName;
        }

        private sealed record VerifiedDirectory(SafeFileHandle Handle, string ExpectedPath);
    }

    private sealed class ManagedSnapshotDirectoryGuard : ISnapshotDirectoryGuard
    {
        private readonly string _directory;

        private ManagedSnapshotDirectoryGuard(string root, string directory)
        {
            EnsureSafeExistingDirectory(root, directory);
            _directory = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static ManagedSnapshotDirectoryGuard Open(string root, string directory) =>
            new(root, directory);

        public bool NodeExists(string fileName) =>
            File.Exists(Path.Combine(_directory, fileName)) ||
            Directory.Exists(Path.Combine(_directory, fileName));

        public SafeFileHandle OpenExistingFile(string fileName, FileAccess access)
        {
            var path = Path.Combine(_directory, fileName);
            if (!WindowsSandboxPathValidator.TryNormalizeExistingFile(path, out var canonical, out _) ||
                !string.Equals(canonical, path, StringComparison.Ordinal))
                throw new SandboxContractValidationException(
                    "A snapshot file is missing, unsafe, or uses a reparse point.");
            return File.OpenHandle(
                path,
                FileMode.Open,
                access,
                FileShare.Read,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        public SafeFileHandle CreateNewFile(string fileName) =>
            File.OpenHandle(
                Path.Combine(_directory, fileName),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read,
                FileOptions.Asynchronous | FileOptions.WriteThrough);

        public void PublishCreatedFile(
            SafeFileHandle handle,
            string expectedCurrentPath,
            string destinationFileName)
        {
            VerifyFileStillBound(handle, expectedCurrentPath);
            File.Move(
                expectedCurrentPath,
                Path.Combine(_directory, destinationFileName),
                overwrite: false);
        }

        public void VerifyFileStillBound(SafeFileHandle handle, string expectedPath)
        {
            if (handle.IsInvalid ||
                !WindowsSandboxPathValidator.TryNormalizeExistingFile(expectedPath, out var canonical, out _) ||
                !string.Equals(canonical, expectedPath, StringComparison.Ordinal))
                throw new SandboxContractValidationException(
                    "A snapshot file handle is no longer bound to its expected path.");
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoopSandboxSourceSnapshotCoordination : ISandboxSourceSnapshotCoordination
    {
        public static NoopSandboxSourceSnapshotCoordination Instance { get; } = new();

        public Task ObserveAsync(
            SandboxSourceSnapshotCoordinationStage stage,
            string path,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed record ProvisioningManifest(IReadOnlyList<ProvisioningFile> Files);
    private sealed record ProvisioningFile(int Order, string RelativePath, string Sha256, long Utf8ByteLength);
    private sealed record CopiedFile(long ByteLength, string Sha256);
}
