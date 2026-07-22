using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Sandbox;
using IronDev.Infrastructure.Services.Sandbox;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("WorkbenchSandbox")]
public sealed class SandboxSourceSnapshotBuilderSecurityTests
{
    [TestMethod]
    public void SnapshotBuilder_UsesOneLockedSourceHandleForManifestHashAndCopy()
    {
        var source = ReadRepositoryFile(
            "IronDev.Infrastructure/Services/Sandbox/SandboxSourceSnapshotBuilder.cs");

        Assert.IsFalse(source.Contains("File.OpenRead(source)", StringComparison.Ordinal));
        StringAssert.Contains(source, "sourceGuard.OpenExistingFile(Path.GetFileName(source), FileAccess.Read)");
        StringAssert.Contains(source, "FileFlagOpenReparsePoint");
        StringAssert.Contains(source, "FileShare.Read");
        StringAssert.Contains(source, "var copied = await CopyAndHashAsync(input, output, cancellationToken)");
        StringAssert.Contains(source, "hash.AppendData(buffer, 0, read);");
        StringAssert.Contains(source, "await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)");
    }

    [TestMethod]
    public async Task SnapshotBuilder_SourceReplacementIsSynchronouslyAttemptedWhileExactHandleIsOpen()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("The production snapshot source-handle contract is Windows-specific.");

        using var files = SnapshotTestFiles.Create();
        var replacement = Path.Combine(files.RootPath, "replacement.bin");
        await File.WriteAllBytesAsync(replacement, Enumerable.Repeat((byte)'z', files.Content.Length).ToArray());
        var replacementAttempted = false;
        var replacementBlocked = false;
        var coordination = new DelegateSnapshotCoordination((stage, path, _) =>
        {
            if (stage != SandboxSourceSnapshotCoordinationStage.SourceHandleOpened)
                return Task.CompletedTask;
            replacementAttempted = true;
            try
            {
                File.Copy(replacement, path, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                replacementBlocked = true;
            }
            return Task.CompletedTask;
        });
        var builder = new SandboxSourceSnapshotBuilder(coordination);

        var snapshot = await builder.CreateOrRecoverAsync(files.Request(Guid.NewGuid()));
        var copied = await File.ReadAllBytesAsync(
            Path.Combine(snapshot.SourcePath, files.RelativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.IsTrue(replacementAttempted,
            "The coordinated replacement was not attempted while the source handle was open.");
        Assert.IsTrue(replacementBlocked,
            "The exact source handle did not exclude a concurrent writer/replacement.");
        Assert.AreEqual(files.Content.Length, copied.Length);
        Assert.AreEqual(files.ContentSha256, Sha256(copied),
            "A successful snapshot must contain only bytes verified against the manifest.");
        Assert.IsTrue(builder.Cleanup(snapshot));
    }

    [TestMethod]
    public async Task SnapshotBuilder_ActualJunctionSwapIsRejectedWithoutAnyOutsideWrite()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("The production snapshot destination-handle contract is Windows-specific.");

        using var files = SnapshotTestFiles.Create();
        var outsideTarget = Directory.CreateDirectory(Path.Combine(files.RootPath, "outside-target")).FullName;
        var probe = Path.Combine(files.RootPath, "junction-probe");
        if (!TryCreateDirectoryJunction(probe, outsideTarget))
            Assert.Inconclusive("Directory junction creation is unavailable for the deterministic swap test.");
        Directory.Delete(probe);

        var executionId = Guid.NewGuid();
        var swapAttempted = false;
        var swapCompleted = false;
        string? swappedPath = null;
        var coordination = new DelegateSnapshotCoordination((stage, path, _) =>
        {
            if (stage != SandboxSourceSnapshotCoordinationStage.DestinationDirectoryReady)
                return Task.CompletedTask;
            swapAttempted = true;
            swappedPath = path;
            Directory.Move(path, path + ".displaced");
            swapCompleted = TryCreateDirectoryJunction(path, outsideTarget);
            return Task.CompletedTask;
        });
        var builder = new SandboxSourceSnapshotBuilder(coordination);

        var rejection = await Assert.ThrowsExactlyAsync<SandboxSourceSnapshotCleanupException>(() =>
            builder.CreateOrRecoverAsync(files.Request(executionId)));

        Assert.IsTrue(swapAttempted,
            "The directory replacement was not attempted inside the guarded write interval.");
        Assert.IsTrue(swapCompleted,
            "The adversarial directory replacement did not complete inside the guarded write interval.");
        Assert.IsInstanceOfType<SandboxContractValidationException>(rejection.InnerException);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(outsideTarget).Any(),
            "A swapped junction must never receive even a temporary or empty snapshot file.");

        Assert.IsNotNull(swappedPath);
        Assert.IsTrue(File.GetAttributes(swappedPath).HasFlag(FileAttributes.ReparsePoint));
        Directory.Delete(swappedPath);
        Assert.IsTrue(builder.CleanupRecovered(new SandboxSourceSnapshotRecoveryRequest(
            executionId,
            files.ProvisioningManifestSha256,
            files.SnapshotRoot)));
    }

    [TestMethod]
    public async Task SnapshotBuilder_RejectsSnapshotRootWithAReparseAncestorBeforePublishingOwnerState()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive("The production sandbox reparse-point contract is Windows-specific.");

        using var files = SnapshotTestFiles.Create();
        var target = Directory.CreateDirectory(Path.Combine(files.RootPath, "redirect-target"));
        var link = Path.Combine(files.RootPath, "redirect-link");
        try
        {
            Directory.CreateSymbolicLink(link, target.FullName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            if (!TryCreateDirectoryJunction(link, target.FullName))
                Assert.Inconclusive($"Directory reparse creation is unavailable: {exception.Message}");
        }

        var redirectedRoot = Path.Combine(link, "snapshots");
        var executionId = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<SandboxContractValidationException>(() =>
            new SandboxSourceSnapshotBuilder().CreateOrRecoverAsync(
                files.Request(executionId) with { SnapshotRoot = redirectedRoot }));

        Assert.IsFalse(Directory.Exists(Path.Combine(redirectedRoot, executionId.ToString("N"))));
        Assert.IsFalse(File.Exists(Path.Combine(
            redirectedRoot,
            $".{executionId:N}.snapshot-owner.json")));
    }

    [TestMethod]
    public void SnapshotBuilder_CleansTempOnlyCrashStateFromDeterministicPreparedSidecar()
    {
        using var files = SnapshotTestFiles.Create();
        var executionId = Guid.NewGuid();
        var request = files.Request(executionId);
        var sidecar = Path.Combine(files.SnapshotRoot, $".{executionId:N}.snapshot-owner.json");
        var preparedSidecar = sidecar + ".prepared";
        File.WriteAllText(preparedSidecar, "{\"partial\"", new UTF8Encoding(false));

        var cleaned = new SandboxSourceSnapshotBuilder().CleanupRecovered(
            new SandboxSourceSnapshotRecoveryRequest(
                executionId,
                request.ProvisioningManifestSha256,
                files.SnapshotRoot));

        Assert.IsTrue(cleaned);
        Assert.IsFalse(File.Exists(sidecar));
        Assert.IsFalse(File.Exists(preparedSidecar));
        Assert.IsFalse(Directory.Exists(Path.Combine(files.SnapshotRoot, executionId.ToString("N"))));
    }

    [TestMethod]
    public async Task SnapshotBuilder_CleanupRejectsCallerSelectedPathOutsideBoundSnapshotRoot()
    {
        using var files = SnapshotTestFiles.Create();
        var builder = new SandboxSourceSnapshotBuilder();
        var realSnapshot = await builder.CreateOrRecoverAsync(files.Request(Guid.NewGuid()));
        var arbitraryRoot = Directory.CreateDirectory(Path.Combine(files.RootPath, "arbitrary-root")).FullName;
        var arbitraryOwner = Directory.CreateDirectory(
            Path.Combine(arbitraryRoot, realSnapshot.ExecutionId.ToString("N"))).FullName;
        var arbitrarySource = Directory.CreateDirectory(Path.Combine(arbitraryOwner, "source")).FullName;
        File.WriteAllText(Path.Combine(arbitrarySource, "retain.txt"), "must survive", new UTF8Encoding(false));
        var forged = new SandboxSourceSnapshot(
            realSnapshot.ExecutionId,
            realSnapshot.SnapshotRoot,
            arbitrarySource,
            realSnapshot.ProvisioningManifestSha256,
            realSnapshot.WorktreeFingerprint,
            realSnapshot.FileCount,
            realSnapshot.TotalBytes);

        Assert.IsFalse(builder.Cleanup(forged));
        Assert.IsTrue(File.Exists(Path.Combine(arbitrarySource, "retain.txt")),
            "Cleanup must not recurse into a caller-selected path outside the bound snapshot root.");
        Assert.IsTrue(builder.Cleanup(realSnapshot));
    }

    [TestMethod]
    public async Task SnapshotBuilder_OwnershipMarkerBindsCanonicalRootAndManifestHash()
    {
        using var files = SnapshotTestFiles.Create();
        var builder = new SandboxSourceSnapshotBuilder();
        var snapshot = await builder.CreateOrRecoverAsync(files.Request(Guid.NewGuid()));
        var marker = await File.ReadAllTextAsync(Path.Combine(
            Directory.GetParent(snapshot.SourcePath)!.FullName,
            "snapshot-owner.json"));

        StringAssert.Contains(marker, $"\"provisioningManifestSha256\":\"{snapshot.ProvisioningManifestSha256}\"");
        StringAssert.Contains(marker, "\"snapshotRootSha256\":");
        var wrongManifestAuthority = new SandboxSourceSnapshot(
            snapshot.ExecutionId,
            snapshot.SnapshotRoot,
            snapshot.SourcePath,
            new string('f', 64),
            snapshot.WorktreeFingerprint,
            snapshot.FileCount,
            snapshot.TotalBytes);
        Assert.IsFalse(builder.Cleanup(wrongManifestAuthority),
            "A manifest hash that does not match the exact ownership marker must not authorize deletion.");
        Assert.IsTrue(Directory.Exists(snapshot.SourcePath));
        Assert.IsTrue(builder.Cleanup(snapshot));
    }

    private sealed class DelegateSnapshotCoordination(
        Func<SandboxSourceSnapshotCoordinationStage, string, CancellationToken, Task> observer)
        : ISandboxSourceSnapshotCoordination
    {
        public Task ObserveAsync(
            SandboxSourceSnapshotCoordinationStage stage,
            string path,
            CancellationToken cancellationToken) => observer(stage, path, cancellationToken);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            current = current.Parent;
        }
        Assert.Fail($"Repository file was not found: {relativePath}");
        return string.Empty;
    }

    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static bool TryCreateDirectoryJunction(string linkPath, string targetPath)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("/d");
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("mklink");
            start.ArgumentList.Add("/J");
            start.ArgumentList.Add(linkPath);
            start.ArgumentList.Add(targetPath);
            using var process = Process.Start(start);
            if (process is null)
                return false;
            process.WaitForExit();
            return process.ExitCode == 0 && Directory.Exists(linkPath) &&
                File.GetAttributes(linkPath).HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    private sealed class SnapshotTestFiles : IDisposable
    {
        private SnapshotTestFiles(string rootPath)
        {
            RootPath = rootPath;
            RepositoryPath = Directory.CreateDirectory(Path.Combine(rootPath, "repository")).FullName;
            SnapshotRoot = Directory.CreateDirectory(Path.Combine(rootPath, "snapshots")).FullName;
            RelativePath = "src/App.bin";
            Content = Enumerable.Repeat((byte)'a', 512 * 1024).ToArray();
            ContentSha256 = Sha256(Content);
            SourceFile = Path.Combine(
                RepositoryPath,
                RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(SourceFile)!);
            File.WriteAllBytes(SourceFile, Content);
            ProvisioningManifestJson = SandboxCanonicalJson.Serialize(new
            {
                schemaVersion = 1,
                files = new[]
                {
                    new
                    {
                        order = 1,
                        path = RelativePath,
                        sha256 = ContentSha256,
                        utf8ByteLength = Content.LongLength
                    }
                }
            });
            ProvisioningManifestSha256 = SandboxCanonicalJson.Sha256(ProvisioningManifestJson);
        }

        public string RootPath { get; }
        public string RepositoryPath { get; }
        public string SnapshotRoot { get; }
        public string RelativePath { get; }
        public string SourceFile { get; }
        public byte[] Content { get; }
        public string ContentSha256 { get; }
        public string ProvisioningManifestJson { get; }
        public string ProvisioningManifestSha256 { get; }

        public static SnapshotTestFiles Create() =>
            new(Directory.CreateTempSubdirectory("irondev-pr06a-snapshot-").FullName);

        public SandboxSourceSnapshotRequest Request(Guid executionId) => new(
            executionId,
            RepositoryPath,
            new string('1', 40),
            new string('2', 40),
            ProvisioningManifestJson,
            ProvisioningManifestSha256,
            SnapshotRoot);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                    Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
                // A failed assertion should retain its original failure rather than a temp cleanup error.
            }
        }
    }
}
