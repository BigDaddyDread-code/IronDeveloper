using System.Diagnostics;
using System.Security.Cryptography;
using IronDev.Core.RunReadiness;
using IronDev.Core.Workspaces;
using IronDev.Infrastructure.Services.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("SkeletonRun")]
public sealed class ControlledSourceMutationExecutorTests
{
    [TestMethod]
    public async Task NormalNestedAddAndModify_UseVerifiedHandlesAndVerifyHashes()
    {
        using var fixture = MutationFixture.Create();
        var addWorkspacePath = fixture.WriteWorkspace("src/nested/New.cs", "first");

        var add = await fixture.Executor.ExecuteAsync(fixture.Request(
            "add", "src/nested/New.cs", addWorkspacePath, string.Empty, Hash("first")));

        Assert.IsTrue(add.Succeeded, add.Evidence.Reason);
        Assert.AreEqual(ControlledSourceMutationReasonCodes.Applied, add.Evidence.ReasonCode);
        Assert.AreEqual("first", await File.ReadAllTextAsync(Path.Combine(fixture.ProjectRoot, "src", "nested", "New.cs")));
        Assert.AreEqual(Hash("first"), add.Evidence.ActualSourceHashAfter);

        var modifyWorkspacePath = fixture.WriteWorkspace("src/nested/New.cs", "second");
        var modify = await fixture.Executor.ExecuteAsync(fixture.Request(
            "modify", "src/nested/New.cs", modifyWorkspacePath, Hash("first"), Hash("second")));

        Assert.IsTrue(modify.Succeeded, modify.Evidence.Reason);
        Assert.AreEqual("second", await File.ReadAllTextAsync(Path.Combine(fixture.ProjectRoot, "src", "nested", "New.cs")));
        Assert.AreEqual(Hash("first"), modify.Evidence.ActualSourceHashBefore);
        Assert.AreEqual(Hash("second"), modify.Evidence.ActualSourceHashAfter);
    }

    [TestMethod]
    public async Task MultipleFiles_ApplyOnlyToQualifiedProject_WithoutGitMutation()
    {
        using var fixture = MutationFixture.Create();
        foreach (var (path, content) in new[] { ("a/One.cs", "one"), ("b/Two.cs", "two") })
        {
            var workspacePath = fixture.WriteWorkspace(path, content);
            var result = await fixture.Executor.ExecuteAsync(fixture.Request(
                "add", path, workspacePath, string.Empty, Hash(content)));
            Assert.IsTrue(result.Succeeded, result.Evidence.Reason);
        }

        Assert.AreEqual("one", await File.ReadAllTextAsync(Path.Combine(fixture.ProjectRoot, "a", "One.cs")));
        Assert.AreEqual("two", await File.ReadAllTextAsync(Path.Combine(fixture.ProjectRoot, "b", "Two.cs")));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.ProjectRoot, ".git")), "Controlled apply must not commit or create Git state.");
    }

    [TestMethod]
    public async Task BatchPrevalidationFailure_DoesNotMutateAnEarlierValidOperation()
    {
        using var fixture = MutationFixture.Create();
        var firstWorkspacePath = fixture.WriteWorkspace("src/First.cs", "first");
        var secondWorkspacePath = fixture.WriteWorkspace("src/Second.cs", "approved");
        File.WriteAllText(secondWorkspacePath, "drifted");

        var batch = await fixture.Executor.ExecuteBatchAsync(
        [
            fixture.Request("add", "src/First.cs", firstWorkspacePath, string.Empty, Hash("first")),
            fixture.Request("add", "src/Second.cs", secondWorkspacePath, string.Empty, Hash("approved"))
        ]);

        Assert.IsFalse(batch.Succeeded);
        Assert.IsFalse(batch.SourceRepoMutated);
        Assert.AreEqual(1, batch.FailureOperationIndex);
        Assert.AreEqual(
            ControlledSourceMutationReasonCodes.WorkspaceHashMismatch,
            batch.FailureEvidence?.ReasonCode);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.ProjectRoot, "src", "First.cs")));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.ProjectRoot, "src", "Second.cs")));
    }

    [DataTestMethod]
    [DataRow("capability-disabled")]
    [DataRow("session")]
    [DataRow("project-path")]
    [DataRow("sandbox-root")]
    [DataRow("qualification")]
    public async Task LiveCapabilityDriftImmediatelyBeforeMutation_FailsClosed(string drift)
    {
        using var fixture = MutationFixture.Create();
        var sourcePath = fixture.WriteProject("src/Existing.cs", "original");
        var workspacePath = fixture.WriteWorkspace("src/Existing.cs", "changed");
        fixture.Capability.Drift(drift, fixture.Root);

        var result = await fixture.Executor.ExecuteAsync(fixture.Request(
            "modify", "src/Existing.cs", workspacePath, Hash("original"), Hash("changed")));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ControlledSourceMutationReasonCodes.CapabilityChangedBeforeMutation, result.Evidence.ReasonCode);
        Assert.IsFalse(result.Evidence.SourceRepoMutated);
        Assert.AreEqual("original", await File.ReadAllTextAsync(sourcePath));
        Assert.AreEqual(fixture.ExpectedEvidenceHash, result.Evidence.PreviousReadinessEvidenceHash);
        Assert.AreNotEqual(fixture.ExpectedEvidenceHash, result.Evidence.LiveReadinessEvidenceHash);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Evidence.NextSafeAction));
    }

    [TestMethod]
    public async Task NestedJunctionAdd_IsRefusedWithoutOutsideWrite()
    {
        using var fixture = MutationFixture.Create();
        var outside = fixture.CreateOutsideDirectory();
        fixture.CreateJunction(Path.Combine(fixture.ProjectRoot, "src"), outside);
        var workspacePath = fixture.WriteWorkspace("src/Escape.cs", "escape");

        var result = await fixture.Executor.ExecuteAsync(fixture.Request(
            "add", "src/Escape.cs", workspacePath, string.Empty, Hash("escape")));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ControlledSourceMutationReasonCodes.DestinationPathUnsafe, result.Evidence.ReasonCode);
        Assert.IsFalse(result.Evidence.SourceRepoMutated);
        Assert.IsFalse(File.Exists(Path.Combine(outside, "Escape.cs")));
    }

    [TestMethod]
    public async Task NestedJunctionModify_IsRefusedWithoutOutsideWrite()
    {
        using var fixture = MutationFixture.Create();
        var outside = fixture.CreateOutsideDirectory();
        var outsideFile = Path.Combine(outside, "Existing.cs");
        await File.WriteAllTextAsync(outsideFile, "outside-original");
        fixture.CreateJunction(Path.Combine(fixture.ProjectRoot, "src"), outside);
        var workspacePath = fixture.WriteWorkspace("src/Existing.cs", "changed");

        var result = await fixture.Executor.ExecuteAsync(fixture.Request(
            "modify", "src/Existing.cs", workspacePath, Hash("outside-original"), Hash("changed")));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ControlledSourceMutationReasonCodes.DestinationPathUnsafe, result.Evidence.ReasonCode);
        Assert.IsFalse(result.Evidence.SourceRepoMutated);
        Assert.AreEqual("outside-original", await File.ReadAllTextAsync(outsideFile));
    }

    [TestMethod]
    public async Task WorkspaceJunction_IsRefusedWithoutReadingOrCopyingOutsideContent()
    {
        using var fixture = MutationFixture.Create();
        var outside = fixture.CreateOutsideDirectory();
        await File.WriteAllTextAsync(Path.Combine(outside, "Secret.cs"), "secret");
        fixture.CreateJunction(Path.Combine(fixture.WorkspaceRoot, "src"), outside);

        var result = await fixture.Executor.ExecuteAsync(fixture.Request(
            "add", "src/Secret.cs", Path.Combine(fixture.WorkspaceRoot, "src", "Secret.cs"), string.Empty, Hash("secret")));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ControlledSourceMutationReasonCodes.WorkspacePathUnsafe, result.Evidence.ReasonCode);
        Assert.IsFalse(result.Evidence.SourceRepoMutated);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.ProjectRoot, "src", "Secret.cs")));
        Assert.IsNull(result.Evidence.ActualWorkspaceHashBefore, "The outside workspace target must be refused before its content is read.");
    }

    [TestMethod]
    public async Task DestinationReparsePoint_IsRefusedWithoutChangingItsOutsideTarget()
    {
        using var fixture = MutationFixture.Create();
        var outside = fixture.CreateOutsideDirectory();
        var outsideFile = Path.Combine(outside, "sentinel.txt");
        await File.WriteAllTextAsync(outsideFile, "outside-original");
        var destination = Path.Combine(fixture.ProjectRoot, "src", "Existing.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        // A directory junction is creatable without SeCreateSymbolicLinkPrivilege
        // and exercises the same exact-destination ReparsePoint rejection branch
        // used for file and directory symbolic links.
        fixture.CreateJunction(destination, outside);
        var workspacePath = fixture.WriteWorkspace("src/Existing.cs", "changed");

        var result = await fixture.Executor.ExecuteAsync(fixture.Request(
            "modify", "src/Existing.cs", workspacePath, Hash("outside-original"), Hash("changed")));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ControlledSourceMutationReasonCodes.DestinationPathUnsafe, result.Evidence.ReasonCode);
        Assert.IsFalse(result.Evidence.SourceRepoMutated);
        Assert.AreEqual("outside-original", await File.ReadAllTextAsync(outsideFile));
    }

    private static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private sealed class MutationFixture : IDisposable
    {
        private readonly List<string> _junctions = [];

        private MutationFixture(
            string root,
            string sandboxRoot,
            string projectRoot,
            string workspaceRoot,
            MutableCapabilityService capability)
        {
            Root = root;
            SandboxRoot = sandboxRoot;
            ProjectRoot = projectRoot;
            WorkspaceRoot = workspaceRoot;
            Capability = capability;
            Executor = new ControlledSourceMutationExecutor(capability);
        }

        public string Root { get; }
        public string SandboxRoot { get; }
        public string ProjectRoot { get; }
        public string WorkspaceRoot { get; }
        public MutableCapabilityService Capability { get; }
        public ControlledSourceMutationExecutor Executor { get; }
        public string ExpectedEvidenceHash => "run-start-evidence-v1";

        public static MutationFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "irondev-fix010-" + Guid.NewGuid().ToString("N"));
            var sandbox = Path.Combine(root, "sandbox");
            var project = Path.Combine(sandbox, "project");
            var workspace = Path.Combine(root, "workspace");
            Directory.CreateDirectory(project);
            Directory.CreateDirectory(workspace);
            var capability = new MutableCapabilityService(Ready(project, sandbox));
            return new MutationFixture(root, sandbox, project, workspace, capability);
        }

        public ControlledSourceMutationRequest Request(
            string operation,
            string relativePath,
            string workspaceSourcePath,
            string expectedSourceHash,
            string expectedWorkspaceHash) =>
            new()
            {
                ProjectId = 41,
                RunId = "run-41",
                ApplyAttemptId = "run-41-apply-001",
                ExpectedReadinessEvidenceHash = ExpectedEvidenceHash,
                QualifiedSandboxRoot = SandboxRoot,
                QualifiedProjectRoot = ProjectRoot,
                QualifiedWorkspaceRoot = WorkspaceRoot,
                OperationKind = operation,
                RelativePath = relativePath,
                WorkspaceSourcePath = workspaceSourcePath,
                ExpectedSourceHash = expectedSourceHash,
                ExpectedWorkspaceHash = expectedWorkspaceHash,
                ExpectedLauncherSessionId = "session-1",
                ExpectedSandboxRootFingerprint = "sandbox-fingerprint",
                ExpectedProjectPathFingerprint = "project-fingerprint",
                ExpectedQualificationId = "qualification-1",
                ExpectedQualificationFingerprint = "qualification-fingerprint"
            };

        public string WriteProject(string relativePath, string content)
        {
            var path = Path.Combine(ProjectRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public string WriteWorkspace(string relativePath, string content)
        {
            var path = Path.Combine(WorkspaceRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public string CreateOutsideDirectory()
        {
            var outside = Path.Combine(Root, "outside-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outside);
            return outside;
        }

        public void CreateJunction(string link, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            var start = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/d /c mklink /J \"{link}\" \"{target}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(start)!;
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode,
                $"Could not create test junction. stdout={process.StandardOutput.ReadToEnd()} stderr={process.StandardError.ReadToEnd()}");
            _junctions.Add(link);
        }

        public void Dispose()
        {
            foreach (var junction in _junctions.OrderByDescending(path => path.Length))
            {
                try { if (Directory.Exists(junction)) Directory.Delete(junction); }
                catch { }
            }
            try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
            catch { }
        }

        private static ProjectApplyCapability Ready(string projectRoot, string sandboxRoot) => new()
        {
            ProjectId = 41,
            IsReady = true,
            State = "Ready",
            ReasonCode = ProjectApplyCapabilityReasonCodes.Ready,
            Reason = "ready",
            NextSafeAction = ProjectApplyCapabilityCommands.RestartInSandboxApplyMode,
            SessionMode = ProjectRunPurposes.ProjectFeatureWork,
            LauncherSessionId = "session-1",
            SandboxRoot = sandboxRoot,
            ProjectPath = projectRoot,
            SandboxRootFingerprint = "sandbox-fingerprint",
            ProjectPathFingerprint = "project-fingerprint",
            QualificationId = "qualification-1",
            QualificationFingerprint = "qualification-fingerprint",
            ReadinessEvidenceHash = "run-start-evidence-v1"
        };
    }

    private sealed class MutableCapabilityService(ProjectApplyCapability current) : IProjectApplyCapabilityService
    {
        public ProjectApplyCapability Current { get; private set; } = current;

        public Task<ProjectApplyCapability> EvaluateAsync(int projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Current with { ProjectId = projectId });

        public Task<ProjectApplyCapability> QualifyDisposableProjectAsync(
            int projectId,
            int qualifyingActorUserId,
            CancellationToken cancellationToken = default) => EvaluateAsync(projectId, cancellationToken);

        public void Drift(string kind, string root)
        {
            Current = kind switch
            {
                "capability-disabled" => Current with
                {
                    IsReady = false,
                    ReasonCode = ProjectApplyCapabilityReasonCodes.ProjectApplyCapabilityDisabled,
                    ReadinessEvidenceHash = "disabled-evidence"
                },
                "session" => Current with { LauncherSessionId = "session-2", ReadinessEvidenceHash = "session-evidence" },
                "project-path" => Current with
                {
                    ProjectPath = Path.Combine(root, "different-project"),
                    ProjectPathFingerprint = "different-project-fingerprint",
                    ReadinessEvidenceHash = "project-evidence"
                },
                "sandbox-root" => Current with
                {
                    SandboxRoot = Path.Combine(root, "different-sandbox"),
                    SandboxRootFingerprint = "different-sandbox-fingerprint",
                    ReadinessEvidenceHash = "sandbox-evidence"
                },
                "qualification" => Current with
                {
                    QualificationId = "qualification-2",
                    QualificationFingerprint = "different-qualification-fingerprint",
                    ReadinessEvidenceHash = "qualification-evidence"
                },
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }
    }
}
