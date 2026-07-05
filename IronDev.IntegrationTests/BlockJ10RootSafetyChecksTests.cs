using IronDev.Core.Configuration;
using IronDev.Core.Runs;
using IronDev.Core.Workspaces;
using IronDev.Infrastructure.Services.RunReports;
using IronDev.Infrastructure.Services.Runs;
using IronDev.Infrastructure.Services.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("ConfigBoundary")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed class BlockJ10RootSafetyChecksTests
{
    private const string ReceiptPath = "Docs/receipts/J10_LOGS_EVIDENCE_WORKSPACE_ROOT_SAFETY.md";

    [TestMethod]
    public void J10_RootSafetyValidator_RejectsRepositoryRootAndChildren()
    {
        using var fixture = RootFixture.Create();

        foreach (var path in new[]
        {
            fixture.RepositoryRoot,
            Path.Combine(fixture.RepositoryRoot, "IronDev.Api"),
            Path.Combine(fixture.RepositoryRoot, "tools", "dogfood", "proofs"),
            Path.Combine(fixture.RepositoryRoot, "artifacts")
        })
        {
            var result = Validate(LocalRootKind.WorkspaceRoot, path, fixture.RepositoryRoot);
            Assert.IsFalse(result.IsSafe, path);
            Assert.IsTrue(
                result.ReasonCode is "RepositoryRoot" or "UnderRepositoryRoot",
                $"Unexpected reason for {path}: {result.ReasonCode}");
        }
    }

    [TestMethod]
    public void J10_RootSafetyValidator_RejectsBroadSystemAndUserRoots()
    {
        using var fixture = RootFixture.Create();
        var userRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tempRoot = Path.GetTempPath();
        var driveRoot = Path.GetPathRoot(fixture.SafeWorkspaceRoot)!;

        foreach (var path in new[] { userRoot, tempRoot, driveRoot })
        {
            var result = Validate(LocalRootKind.WorkspaceRoot, path, fixture.RepositoryRoot);
            Assert.IsFalse(result.IsSafe, path);
        }

        Assert.IsTrue(Validate(LocalRootKind.WorkspaceRoot, fixture.SafeWorkspaceRoot, fixture.RepositoryRoot).IsSafe);
        Assert.IsTrue(Validate(LocalRootKind.EvidenceRoot, fixture.SafeEvidenceRoot, fixture.RepositoryRoot).IsSafe);
    }

    [TestMethod]
    public void J10_RootSafetyValidator_RejectsRelativeTraversalFilesAndReparsePoints()
    {
        using var fixture = RootFixture.Create();
        var filePath = Path.Combine(fixture.Root, "not-a-root.txt");
        File.WriteAllText(filePath, "not a directory");

        Assert.AreEqual("RelativePath", Validate(LocalRootKind.WorkspaceRoot, "relative-root", fixture.RepositoryRoot).ReasonCode);
        Assert.AreEqual("PathTraversal", Validate(LocalRootKind.WorkspaceRoot, Path.Combine(fixture.Root, "..", "escape"), fixture.RepositoryRoot).ReasonCode);
        Assert.AreEqual("PathIsFile", Validate(LocalRootKind.WorkspaceRoot, filePath, fixture.RepositoryRoot).ReasonCode);
    }

    [TestMethod]
    public void J10_RootSafetyValidator_RejectsNonExistingChildUnderReparsePointParent()
    {
        using var fixture = RootFixture.Create();
        var target = Path.Combine(fixture.Root, "junction-target");
        var junction = Path.Combine(fixture.Root, "junction-parent");
        Directory.CreateDirectory(target);

        if (!TryCreateDirectoryJunction(junction, target))
            Assert.Inconclusive("Directory junction creation is not available in this test environment.");

        try
        {
            var childUnderJunction = Path.Combine(junction, "workspaces");
            var result = Validate(LocalRootKind.WorkspaceRoot, childUnderJunction, fixture.RepositoryRoot);

            Assert.IsFalse(result.IsSafe);
            Assert.AreEqual("PathContainsSymlinkOrReparsePoint", result.ReasonCode);
            Assert.IsFalse(Directory.Exists(childUnderJunction), "Validation must not create a child under a reparse-point parent.");
        }
        finally
        {
            TryRemoveDirectoryJunction(junction);
        }
    }

    [TestMethod]
    public void J10_RootSafetyValidator_RejectsWorkspaceEvidenceOverlap()
    {
        using var fixture = RootFixture.Create();
        var evidenceUnderWorkspace = Path.Combine(fixture.SafeWorkspaceRoot, "evidence");
        var validation = LocalRootSafetyValidator.ValidateRootSet(
        [
            Request(LocalRootKind.DisposableWorkspaceRoot, fixture.SafeWorkspaceRoot, fixture.RepositoryRoot),
            Request(LocalRootKind.EvidenceRoot, evidenceUnderWorkspace, fixture.RepositoryRoot),
            Request(LocalRootKind.LogsRoot, Path.Combine(fixture.SafeWorkspaceRoot, "logs"), fixture.RepositoryRoot)
        ]);

        Assert.IsFalse(validation.IsSafe);
        CollectionAssert.Contains(validation.UnsafeResults.Select(result => result.ReasonCode).ToArray(), "EvidenceUnderWorkspace");
        CollectionAssert.Contains(validation.UnsafeResults.Select(result => result.ReasonCode).ToArray(), "LogsUnderWorkspace");
    }

    [TestMethod]
    public void J10_SandboxRepoPath_MustNotEqualOrContainSourceRepo()
    {
        using var fixture = RootFixture.Create();
        var parentSandbox = fixture.Root;

        Assert.AreEqual("SandboxEqualsSourceRepository", Validate(LocalRootKind.SandboxRepositoryPath, fixture.RepositoryRoot, fixture.RepositoryRoot, mustExist: true).ReasonCode);
        Assert.AreEqual("SandboxUnderSourceRepository", Validate(LocalRootKind.SandboxRepositoryPath, Path.Combine(fixture.RepositoryRoot, "sandbox"), fixture.RepositoryRoot).ReasonCode);
        Assert.AreEqual("SandboxContainsSourceRepository", Validate(LocalRootKind.SandboxRepositoryPath, parentSandbox, fixture.RepositoryRoot, mustExist: true).ReasonCode);
        Assert.IsTrue(Validate(LocalRootKind.SandboxRepositoryPath, fixture.SafeSandboxRoot, fixture.RepositoryRoot).IsSafe);
    }

    [TestMethod]
    public async Task J10_UnsafeDisposableWorkspaceRoot_BlocksBeforeWorkspaceUse()
    {
        using var fixture = RootFixture.Create();
        var service = new DisposableWorkspaceExecutionService(new InMemoryRunStore(), new InMemoryRunEventStore());
        var unsafeWorkspaceRoot = Path.Combine(fixture.RepositoryRoot, "artifacts", "workspace");
        var unsafeWorkspacePath = Path.Combine(unsafeWorkspaceRoot, "j10-run");

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.RunAsync(new DisposableWorkspaceRunRequest
            {
                RunId = "j10-run",
                SourcePath = fixture.RepositoryRoot,
                WorkspaceRoot = unsafeWorkspaceRoot,
                EvidenceRoot = fixture.SafeEvidenceRoot,
                Commands =
                [
                    new DisposableWorkspaceCommand
                    {
                        FileName = "cmd.exe",
                        Arguments = ["/c", "echo must-not-run"],
                        DisplayName = "must not run",
                        Timeout = TimeSpan.FromSeconds(10)
                    }
                ]
            }));

        StringAssert.Contains(ex.Message, "UnsafeDisposableWorkspaceRoot");
        Assert.IsFalse(Directory.Exists(unsafeWorkspacePath), "Unsafe workspace must block before creating the run workspace.");
    }

    [TestMethod]
    public async Task J10_DisposableWorkspaceRoot_UnderOuterRepository_BlocksWhenSourcePathIsProjectSubdirectory()
    {
        using var fixture = RootFixture.Create();
        var service = new DisposableWorkspaceExecutionService(new InMemoryRunStore(), new InMemoryRunEventStore());
        var projectSubdirectory = Path.Combine(fixture.RepositoryRoot, "Samples", "BookSeller");
        var unsafeWorkspaceRoot = Path.Combine(fixture.RepositoryRoot, "artifacts", "workspace");
        var unsafeWorkspacePath = Path.Combine(unsafeWorkspaceRoot, "j10-subdir-run");
        Directory.CreateDirectory(Path.Combine(fixture.RepositoryRoot, ".git"));
        Directory.CreateDirectory(projectSubdirectory);
        File.WriteAllText(Path.Combine(projectSubdirectory, "source.txt"), "source");

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.RunAsync(new DisposableWorkspaceRunRequest
            {
                RunId = "j10-subdir-run",
                SourcePath = projectSubdirectory,
                WorkspaceRoot = unsafeWorkspaceRoot,
                EvidenceRoot = fixture.SafeEvidenceRoot,
                Commands =
                [
                    new DisposableWorkspaceCommand
                    {
                        FileName = "cmd.exe",
                        Arguments = ["/c", "echo must-not-run"],
                        DisplayName = "must not run",
                        Timeout = TimeSpan.FromSeconds(10)
                    }
                ]
            }));

        StringAssert.Contains(ex.Message, "UnsafeDisposableWorkspaceRoot");
        StringAssert.Contains(ex.Message, "UnderRepositoryRoot");
        Assert.IsFalse(Directory.Exists(unsafeWorkspacePath), "Unsafe workspace must block before creating the run workspace.");
    }

    [TestMethod]
    public async Task J10_DisposableWorkspaceRoot_UnderLinkedWorktreeRepository_BlocksWhenSourcePathIsProjectSubdirectory()
    {
        using var fixture = RootFixture.Create();
        var service = new DisposableWorkspaceExecutionService(new InMemoryRunStore(), new InMemoryRunEventStore());
        var projectSubdirectory = Path.Combine(fixture.RepositoryRoot, "Samples", "BookSeller");
        var unsafeWorkspaceRoot = Path.Combine(fixture.RepositoryRoot, "artifacts", "workspace");
        var unsafeWorkspacePath = Path.Combine(unsafeWorkspaceRoot, "j10-linked-worktree-run");
        File.WriteAllText(Path.Combine(fixture.RepositoryRoot, ".git"), "gitdir: ../linked-worktree-git");
        Directory.CreateDirectory(projectSubdirectory);
        File.WriteAllText(Path.Combine(projectSubdirectory, "source.txt"), "source");

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.RunAsync(new DisposableWorkspaceRunRequest
            {
                RunId = "j10-linked-worktree-run",
                SourcePath = projectSubdirectory,
                WorkspaceRoot = unsafeWorkspaceRoot,
                EvidenceRoot = fixture.SafeEvidenceRoot,
                Commands = []
            }));

        StringAssert.Contains(ex.Message, "UnsafeDisposableWorkspaceRoot");
        StringAssert.Contains(ex.Message, "UnderRepositoryRoot");
        Assert.IsFalse(Directory.Exists(unsafeWorkspacePath), "Unsafe workspace must block before creating the run workspace.");
    }

    [TestMethod]
    public async Task J10_UnsafeEvidenceRoot_BlocksBeforeEvidenceWrite()
    {
        using var fixture = RootFixture.Create();
        var service = new DisposableWorkspaceExecutionService(new InMemoryRunStore(), new InMemoryRunEventStore());
        var unsafeEvidenceRoot = Path.Combine(fixture.SafeWorkspaceRoot, "evidence");

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.RunAsync(new DisposableWorkspaceRunRequest
            {
                RunId = "j10-evidence",
                SourcePath = fixture.RepositoryRoot,
                WorkspaceRoot = fixture.SafeWorkspaceRoot,
                EvidenceRoot = unsafeEvidenceRoot,
                Commands = []
            }));

        StringAssert.Contains(ex.Message, "EvidenceUnderWorkspace");
        Assert.IsFalse(Directory.Exists(unsafeEvidenceRoot), "Unsafe evidence root must block before evidence write.");
    }

    [TestMethod]
    public async Task J10_DefaultDisposableEvidenceRoot_IsSiblingNotUnderWorkspace()
    {
        using var fixture = RootFixture.Create();
        var service = new DisposableWorkspaceExecutionService(new InMemoryRunStore(), new InMemoryRunEventStore());

        var result = await service.RunAsync(new DisposableWorkspaceRunRequest
        {
            RunId = "j10-default-evidence",
            SourcePath = fixture.RepositoryRoot,
            WorkspaceRoot = fixture.SafeWorkspaceRoot,
            Commands = []
        });

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(Directory.Exists(result.EvidencePath));
        Assert.IsFalse(IsSameOrUnder(fixture.SafeWorkspaceRoot, result.EvidencePath), "Evidence must not be under disposable workspace root.");
    }

    [TestMethod]
    public void J10_ReceiptStatesRootSafetyBoundary()
    {
        var receipt = ReadRepositoryFile(ReceiptPath);

        StringAssert.Contains(receipt, "A safe root is a precondition for evidence.");
        StringAssert.Contains(receipt, "It is not evidence, approval, execution authority, or permission to mutate source.");
        StringAssert.Contains(receipt, "Workspace roots are disposable. Evidence and logs must survive cleanup.");
    }

    [TestMethod]
    public void J10_NoBootstrapOrAuthoritySurfaceAdded()
    {
        var source = string.Join(
            Environment.NewLine,
            ReadRepositoryFile("IronDev.Core/Configuration/LocalRootSafetyModels.cs"),
            ReadRepositoryFile("IronDev.Core/Configuration/LocalRootSafetyValidator.cs"));

        foreach (var marker in new[]
        {
            "AcceptedApproval",
            "ControlledSourceApply",
            "ControlledCommit",
            "ControlledPush",
            "MigrationBuilder",
            "CREATE DATABASE",
            "docker compose",
            "weaviate up"
        })
        {
            AssertDoesNotContain(source, marker, "J10 root safety source");
        }
    }

    private static LocalRootSafetyResult Validate(
        LocalRootKind kind,
        string path,
        string repositoryRoot,
        bool mustExist = false) =>
        LocalRootSafetyValidator.Validate(Request(kind, path, repositoryRoot, mustExist));

    private static LocalRootSafetyRequest Request(
        LocalRootKind kind,
        string path,
        string repositoryRoot,
        bool mustExist = false) =>
        new(kind, kind.ToString(), path, repositoryRoot, "Development", mustExist);

    private static bool IsSameOrUnder(string parent, string child)
    {
        var parentFull = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var childFull = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return childFull.StartsWith(parentFull, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepositoryFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static void AssertDoesNotContain(string text, string marker, string sourceName)
    {
        Assert.IsFalse(
            text.Contains(marker, StringComparison.OrdinalIgnoreCase),
            $"{sourceName} must not contain '{marker}'.");
    }

    private static bool TryCreateDirectoryJunction(string junctionPath, string targetPath)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\""
        });
        process!.WaitForExit();
        return process.ExitCode == 0 && Directory.Exists(junctionPath);
    }

    private static void TryRemoveDirectoryJunction(string junctionPath)
    {
        try
        {
            if (!Directory.Exists(junctionPath))
                return;

            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = $"/c rmdir \"{junctionPath}\""
            });
            process!.WaitForExit();
        }
        catch
        {
            // Best-effort cleanup; the fixture root cleanup handles normal directories.
        }
    }

    private sealed class RootFixture : IDisposable
    {
        private RootFixture(string root)
        {
            Root = root;
            RepositoryRoot = Path.Combine(root, "source");
            SafeWorkspaceRoot = Path.Combine(root, ".irondev", "workspaces");
            SafeEvidenceRoot = Path.Combine(root, ".irondev", "evidence");
            SafeSandboxRoot = Path.Combine(root, "sandbox-repo");
        }

        public string Root { get; }
        public string RepositoryRoot { get; }
        public string SafeWorkspaceRoot { get; }
        public string SafeEvidenceRoot { get; }
        public string SafeSandboxRoot { get; }

        public static RootFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "irondev-j10-" + Guid.NewGuid().ToString("N"));
            var fixture = new RootFixture(root);
            Directory.CreateDirectory(fixture.RepositoryRoot);
            Directory.CreateDirectory(fixture.SafeWorkspaceRoot);
            Directory.CreateDirectory(fixture.SafeEvidenceRoot);
            Directory.CreateDirectory(fixture.SafeSandboxRoot);
            File.WriteAllText(Path.Combine(fixture.RepositoryRoot, "source.txt"), "source");
            return fixture;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
