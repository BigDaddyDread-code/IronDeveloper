using System.Text.Json;
using IronDev.Core.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
[TestCategory("ConfigBoundary")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
[TestCategory("ReleaseReadiness")]
public sealed class BlockREL1RootSafetyReleaseGateTests
{
    private const string ReceiptPath = "Docs/receipts/REL1_ROOT_SAFETY_RELEASE_GATE.md";

    [TestMethod]
    public void Rel1_RootSafetyGate_RequiresEveryReleaseRootKind()
    {
        var required = ReleaseRootSafetyGate.RequiredReleaseRootKinds.ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                LocalRootKind.LogsRoot,
                LocalRootKind.EvidenceRoot,
                LocalRootKind.WorkspaceRoot,
                LocalRootKind.DisposableWorkspaceRoot,
                LocalRootKind.SandboxRepositoryPath,
                LocalRootKind.CanaryMeasurementRoot,
                LocalRootKind.BatchMapEvidenceRoot,
                LocalRootKind.SmokeArtifactRoot
            },
            required);
    }

    [TestMethod]
    public void Rel1_RootSafetyGate_PassesDedicatedRootsButDoesNotGrantAuthority()
    {
        using var fixture = RootFixture.Create();
        var report = Evaluate(fixture, fixture.AllSafeRoots());

        Assert.AreEqual(ReleaseRootSafetyStatus.Passed, report.Status);
        Assert.IsTrue(report.Results.All(result => result.Status == ReleaseRootSafetyStatus.Passed));
        StringAssert.Contains(report.BoundaryStatement, "not evidence");
        StringAssert.Contains(report.BoundaryStatement, "not permission to mutate");
        StringAssert.Contains(report.Results.Single(result => result.Kind == LocalRootKind.SmokeArtifactRoot).NextSafeAction, "next independent release gate");
    }

    [TestMethod]
    public void Rel1_RootSafetyGate_BlocksWhenPolicyIsNotEvaluated()
    {
        using var fixture = RootFixture.Create();
        var report = ReleaseRootSafetyGate.Evaluate(new ReleaseRootSafetyRequest(
            fixture.RepositoryRoot,
            "Development",
            fixture.AllSafeRoots(),
            Evaluate: false));

        Assert.AreEqual(ReleaseRootSafetyStatus.NotEvaluated, report.Status);
        Assert.IsTrue(report.Results.All(result => result.Status == ReleaseRootSafetyStatus.NotEvaluated));
        Assert.IsTrue(report.Results.All(result => result.ReasonCode == "UnsafeRootPolicyMissing"));
        Assert.IsTrue(report.Results.All(result => result.NextSafeAction.Contains("Run the root safety gate", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Rel1_RootSafetyGate_BlocksMissingRequiredRootsWithTypedReason()
    {
        using var fixture = RootFixture.Create();
        var report = Evaluate(fixture, fixture.AllSafeRoots().Where(root => root.Kind != LocalRootKind.EvidenceRoot).ToArray());
        var result = report.Results.Single(root => root.Kind == LocalRootKind.EvidenceRoot);

        Assert.AreEqual(ReleaseRootSafetyStatus.Blocked, report.Status);
        Assert.AreEqual(ReleaseRootSafetyStatus.Blocked, result.Status);
        Assert.AreEqual("RootNotConfigured", result.ReasonCode);
        StringAssert.Contains(result.NextSafeAction, "Configure a dedicated local root");
    }

    [TestMethod]
    public void Rel1_RootSafetyGate_MapsRepositoryHazardsToReleaseReasonCodesAndRedactsPaths()
    {
        using var fixture = RootFixture.Create();

        var exact = EvaluateSingle(fixture, LocalRootKind.LogsRoot, fixture.RepositoryRoot);
        var child = EvaluateSingle(fixture, LocalRootKind.EvidenceRoot, Path.Combine(fixture.RepositoryRoot, "artifacts", "evidence"));

        Assert.AreEqual("RootEqualsSourceRepo", exact.ReasonCode);
        Assert.AreEqual("RootIsRepositoryChild", child.ReasonCode);
        Assert.AreEqual("<source-repo>", exact.RedactedDisplayPath);
        Assert.IsFalse(exact.RedactedDisplayPath.Contains(fixture.Root, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(child.RedactedDisplayPath.Contains(fixture.Root, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Rel1_RootSafetyGate_MapsBroadRootHazardsToReleaseReasonCodes()
    {
        using var fixture = RootFixture.Create();
        var driveRoot = Path.GetPathRoot(fixture.SafeWorkspaceRoot)!;
        var userRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tempRoot = Path.GetTempPath();

        Assert.AreEqual("RootIsDriveRoot", EvaluateSingle(fixture, LocalRootKind.WorkspaceRoot, driveRoot).ReasonCode);

        if (!string.IsNullOrWhiteSpace(userRoot))
        {
            var userResult = EvaluateSingle(fixture, LocalRootKind.LogsRoot, userRoot);
            Assert.AreEqual("RootIsUserHome", userResult.ReasonCode);
            Assert.IsFalse(userResult.RedactedDisplayPath.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
        }

        Assert.AreEqual("RootIsRawTempRoot", EvaluateSingle(fixture, LocalRootKind.SmokeArtifactRoot, tempRoot).ReasonCode);
    }

    [TestMethod]
    public void Rel1_RootSafetyGate_MapsMalformedAndNonResolvingRootsToReleaseReasonCodes()
    {
        using var fixture = RootFixture.Create();
        var filePath = Path.Combine(fixture.Root, "not-a-root.txt");
        File.WriteAllText(filePath, "not a directory");

        Assert.AreEqual("RootNotAbsolute", EvaluateSingle(fixture, LocalRootKind.WorkspaceRoot, "relative-root").ReasonCode);
        Assert.AreEqual("RootEscapesAllowedBase", EvaluateSingle(fixture, LocalRootKind.WorkspaceRoot, Path.Combine(fixture.Root, "..", "escape")).ReasonCode);
        Assert.AreEqual("RootDoesNotResolve", EvaluateSingle(fixture, LocalRootKind.WorkspaceRoot, filePath).ReasonCode);
        Assert.AreEqual("RootDoesNotResolve", EvaluateSingle(fixture, LocalRootKind.SandboxRepositoryPath, Path.Combine(fixture.Root, "missing-sandbox"), mustExist: true).ReasonCode);
    }

    [TestMethod]
    public void Rel1_RootSafetyGate_MapsSandboxSourceCollisionsToReleaseReasonCodes()
    {
        using var fixture = RootFixture.Create();

        Assert.AreEqual("SandboxEqualsSourceRepo", EvaluateSingle(fixture, LocalRootKind.SandboxRepositoryPath, fixture.RepositoryRoot).ReasonCode);
        Assert.AreEqual("SandboxUnderSourceRepo", EvaluateSingle(fixture, LocalRootKind.SandboxRepositoryPath, Path.Combine(fixture.RepositoryRoot, "sandbox")).ReasonCode);
        Assert.AreEqual("SourceRepoUnderSandbox", EvaluateSingle(fixture, LocalRootKind.SandboxRepositoryPath, fixture.Root).ReasonCode);
    }

    [TestMethod]
    public void Rel1_RootSafetyGate_MapsDurableRootsUnderDisposableWorkspace()
    {
        using var fixture = RootFixture.Create();
        var roots = fixture.AllSafeRoots()
            .Where(root => root.Kind is not LocalRootKind.EvidenceRoot and not LocalRootKind.LogsRoot)
            .Concat(
            [
                new ReleaseRootSafetyRoot(LocalRootKind.EvidenceRoot, "EvidenceRoot", Path.Combine(fixture.SafeDisposableRoot, "evidence")),
                new ReleaseRootSafetyRoot(LocalRootKind.LogsRoot, "LogsRoot", Path.Combine(fixture.SafeDisposableRoot, "logs"))
            ])
            .ToArray();
        var report = Evaluate(fixture, roots);

        CollectionAssert.Contains(report.BlockingResults.Select(result => result.ReasonCode).ToArray(), "EvidenceUnderDisposableWorkspace");
        CollectionAssert.Contains(report.BlockingResults.Select(result => result.ReasonCode).ToArray(), "LogsUnderDisposableWorkspace");
    }

    [TestMethod]
    public void Rel1_RootSafetyGate_MapsWorkspaceUnderEvidenceRoot()
    {
        using var fixture = RootFixture.Create();
        var roots = fixture.AllSafeRoots()
            .Where(root => root.Kind != LocalRootKind.WorkspaceRoot)
            .Append(new ReleaseRootSafetyRoot(LocalRootKind.WorkspaceRoot, "WorkspaceRoot", Path.Combine(fixture.SafeEvidenceRoot, "workspace")))
            .ToArray();
        var report = Evaluate(fixture, roots);

        CollectionAssert.Contains(report.BlockingResults.Select(result => result.ReasonCode).ToArray(), "WorkspaceUnderEvidenceRoot");
    }

    [TestMethod]
    public void Rel1_RootSafetyGate_RedactedConfigSummaryCanCarryReleaseRootStatusWithoutRawPaths()
    {
        using var fixture = RootFixture.Create();
        var report = Evaluate(fixture, fixture.AllSafeRoots());
        var rootResult = report.Results.Single(result => result.Kind == LocalRootKind.LogsRoot);
        var summary = new RedactedConfigSummaryService().Build(new RedactedConfigSummaryRequest
        {
            EnvironmentName = "Development",
            IsDevelopment = true,
            Roots =
            [
                new RootConfigInput(
                    ConfigRootKind.LogsRoot,
                    "LogsRoot",
                    fixture.SafeLogsRoot,
                    new ConfigRootSafetyEvaluation(
                        ConfigRootSafetyStatus.Safe,
                        rootResult.ReasonCode,
                        rootResult.NextSafeAction))
            ]
        });
        var serialized = JsonSerializer.Serialize(summary);

        StringAssert.Contains(serialized, "RootSafetyPassed");
        Assert.IsFalse(serialized.Contains(fixture.Root, StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(serialized.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Rel1_AlphaSmokeScript_BlocksMutationShapedRunsWhenRootSafetyBlocks()
    {
        var source = ReadRepositoryFile("Scripts/smoke/alpha-smoke.ps1");

        StringAssert.Contains(source, "RootSafetyNotEvaluated");
        StringAssert.Contains(source, "RootSafetyBlocked");
        StringAssert.Contains(source, "Complete-Smoke -RepoRoot $repoRoot -OverallStatus \"Blocked\" -ExitCode 1");
        StringAssert.Contains(source, "Smoke output root is unsafe");
        StringAssert.Contains(source, "Root safety is a precondition for smoke execution");
    }

    [TestMethod]
    public void Rel1_DoctorScript_ReportsRootSafetyGateAvailabilityWithoutClaimingAuthority()
    {
        var source = ReadRepositoryFile("Scripts/local/doctor-local.ps1");

        StringAssert.Contains(source, "ReleaseRootSafetyGate.cs");
        StringAssert.Contains(source, "J10RootSafetyReleaseGateAvailableNotInvoked");
        StringAssert.Contains(source, "DiagnosticOnly; NotAuthority; NotEvidence; NotApproval; NotReadiness");
    }

    [TestMethod]
    public void Rel1_ReceiptStatesReleaseGateBoundary()
    {
        var receipt = ReadRepositoryFile(ReceiptPath);

        StringAssert.Contains(receipt, "Root safety is a release precondition.");
        StringAssert.Contains(receipt, "It is not evidence, approval, policy satisfaction, source safety, execution authority, release readiness, and not permission to mutate.");
        StringAssert.Contains(receipt, "A clean root is a floor, not a launch key.");
    }

    [TestMethod]
    public void Rel1_NoBootstrapMutationOrAuthoritySurfaceAdded()
    {
        var source = string.Join(
            Environment.NewLine,
            ReadRepositoryFile("IronDev.Core/Configuration/ReleaseRootSafetyGateModels.cs"),
            ReadRepositoryFile("IronDev.Core/Configuration/ReleaseRootSafetyGate.cs"));

        foreach (var marker in new[]
        {
            "AcceptedApproval",
            "ControlledSourceApply",
            "ControlledCommit",
            "ControlledPush",
            "WorkflowContinuation",
            "ReleaseExecutor",
            "DeploymentExecutor",
            "CREATE DATABASE",
            "docker compose",
            "Start-Process",
            "File.WriteAll",
            "Directory.CreateDirectory",
            "Delete("
        })
        {
            AssertDoesNotContain(source, marker, "REL-1 root safety release gate source");
        }
    }

    private static ReleaseRootSafetyReport Evaluate(RootFixture fixture, IReadOnlyList<ReleaseRootSafetyRoot> roots) =>
        ReleaseRootSafetyGate.Evaluate(new ReleaseRootSafetyRequest(fixture.RepositoryRoot, "Development", roots));

    private static ReleaseRootSafetyRootResult EvaluateSingle(
        RootFixture fixture,
        LocalRootKind kind,
        string path,
        bool mustExist = false)
    {
        var report = Evaluate(
            fixture,
            [
                new ReleaseRootSafetyRoot(kind, kind.ToString(), path, Required: true, MustExist: mustExist)
            ]);

        return report.Results.First(result => result.Kind == kind);
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

    private sealed class RootFixture : IDisposable
    {
        private RootFixture(string root)
        {
            Root = root;
            RepositoryRoot = Path.Combine(root, "source");
            SafeWorkspaceRoot = Path.Combine(root, ".irondev", "workspaces");
            SafeDisposableRoot = Path.Combine(root, ".irondev", "disposable-workspaces");
            SafeEvidenceRoot = Path.Combine(root, ".irondev", "evidence");
            SafeLogsRoot = Path.Combine(root, ".irondev", "logs");
            SafeSandboxRoot = Path.Combine(root, "sandbox-repo");
            SafeCanaryRoot = Path.Combine(root, ".irondev", "canary");
            SafeBatchMapRoot = Path.Combine(root, ".irondev", "batch-map");
            SafeSmokeRoot = Path.Combine(root, ".irondev", "alpha-smoke");
        }

        public string Root { get; }
        public string RepositoryRoot { get; }
        public string SafeWorkspaceRoot { get; }
        public string SafeDisposableRoot { get; }
        public string SafeEvidenceRoot { get; }
        public string SafeLogsRoot { get; }
        public string SafeSandboxRoot { get; }
        public string SafeCanaryRoot { get; }
        public string SafeBatchMapRoot { get; }
        public string SafeSmokeRoot { get; }

        public static RootFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "irondev-rel1-" + Guid.NewGuid().ToString("N"));
            var fixture = new RootFixture(root);
            Directory.CreateDirectory(fixture.RepositoryRoot);
            Directory.CreateDirectory(fixture.SafeSandboxRoot);
            File.WriteAllText(Path.Combine(fixture.RepositoryRoot, "source.txt"), "source");
            return fixture;
        }

        public IReadOnlyList<ReleaseRootSafetyRoot> AllSafeRoots() =>
        [
            new ReleaseRootSafetyRoot(LocalRootKind.LogsRoot, "LogsRoot", SafeLogsRoot),
            new ReleaseRootSafetyRoot(LocalRootKind.EvidenceRoot, "EvidenceRoot", SafeEvidenceRoot),
            new ReleaseRootSafetyRoot(LocalRootKind.WorkspaceRoot, "WorkspaceRoot", SafeWorkspaceRoot),
            new ReleaseRootSafetyRoot(LocalRootKind.DisposableWorkspaceRoot, "DisposableWorkspaceRoot", SafeDisposableRoot),
            new ReleaseRootSafetyRoot(LocalRootKind.SandboxRepositoryPath, "SandboxRepoPath", SafeSandboxRoot),
            new ReleaseRootSafetyRoot(LocalRootKind.CanaryMeasurementRoot, "CanaryMeasurementRoot", SafeCanaryRoot),
            new ReleaseRootSafetyRoot(LocalRootKind.BatchMapEvidenceRoot, "BatchMapEvidenceRoot", SafeBatchMapRoot),
            new ReleaseRootSafetyRoot(LocalRootKind.SmokeArtifactRoot, "SmokeArtifactRoot", SafeSmokeRoot)
        ];

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
