using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("DisposableWorkspaceDryRunExecutor")]
public sealed class DisposableWorkspaceDryRunExecutorTests
{
    private static readonly Guid RequestId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid PolicySatisfactionId = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private static readonly DateTimeOffset RequestedAtUtc = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PreparedAtUtc = new(2026, 6, 16, 12, 1, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task DisposableWorkspaceDryRunExecutor_ExecutesAllowedCommandInsideDisposableWorkspace()
    {
        var runner = new FakeProcessRunner();
        var executor = new DisposableWorkspaceControlledDryRunExecutor(runner);

        var report = await executor.ExecuteAsync(ValidRequest(), ValidBoundary(), ValidPlan());

        Assert.AreEqual(1, runner.Calls.Count);
        Assert.IsTrue(report.DryRunCompleted);
        Assert.IsTrue(report.DryRunSucceeded);
        Assert.AreEqual(RequestId, report.ControlledDryRunRequestId);
        Assert.AreEqual(ProjectId, report.ProjectId);
        Assert.AreEqual("workspace-123", report.WorkspaceId);
        Assert.AreEqual("sha256:workspace-boundary", report.WorkspaceBoundaryHash);
        Assert.AreEqual("validation-plan-123", report.ValidationPlanId);
        Assert.AreEqual("sha256:validation-plan", report.ValidationPlanHash);
        StringAssert.Contains(report.Boundary, "Controlled dry-run execution is not source apply.");
        StringAssert.Contains(report.Boundary, "Controlled dry-run report is in-memory only in PR182.");
    }

    [TestMethod]
    public async Task DisposableWorkspaceDryRunExecutor_RejectsInvalidRequest()
    {
        var runner = new FakeProcessRunner();
        var executor = new DisposableWorkspaceControlledDryRunExecutor(runner);

        await AssertRejectsAsync(
            () => executor.ExecuteAsync(ValidRequest() with { PolicySatisfactionHash = " " }, ValidBoundary(), ValidPlan()));

        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task DisposableWorkspaceDryRunExecutor_RejectsProjectMismatch()
    {
        var runner = new FakeProcessRunner();
        var executor = new DisposableWorkspaceControlledDryRunExecutor(runner);

        await AssertRejectsAsync(
            () => executor.ExecuteAsync(ValidRequest(), ValidBoundary() with { ProjectId = Guid.NewGuid() }, ValidPlan()));

        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task DisposableWorkspaceDryRunExecutor_RejectsWorkspaceIdMismatch()
    {
        var runner = new FakeProcessRunner();
        var executor = new DisposableWorkspaceControlledDryRunExecutor(runner);

        await AssertRejectsAsync(
            () => executor.ExecuteAsync(ValidRequest(), ValidBoundary() with { WorkspaceId = "workspace-other" }, ValidPlan()));

        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task DisposableWorkspaceDryRunExecutor_RejectsWorkspaceBoundaryHashMismatch()
    {
        var runner = new FakeProcessRunner();
        var executor = new DisposableWorkspaceControlledDryRunExecutor(runner);

        await AssertRejectsAsync(
            () => executor.ExecuteAsync(ValidRequest(), ValidBoundary() with { WorkspaceBoundaryHash = "sha256:other" }, ValidPlan()));

        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task DisposableWorkspaceDryRunExecutor_RejectsValidationPlanHashMismatch()
    {
        var runner = new FakeProcessRunner();
        var executor = new DisposableWorkspaceControlledDryRunExecutor(runner);

        await AssertRejectsAsync(
            () => executor.ExecuteAsync(ValidRequest(), ValidBoundary(), ValidPlan() with { ValidationPlanHash = "sha256:other" }));

        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task DisposableWorkspaceDryRunExecutor_RejectsWorkingDirectoryOutsideWorkspace()
    {
        var runner = new FakeProcessRunner();
        var executor = new DisposableWorkspaceControlledDryRunExecutor(runner);
        var outsidePath = Path.Combine(Path.GetTempPath(), "irondev-outside-workspace");

        await AssertRejectsAsync(
            () => executor.ExecuteAsync(
                ValidRequest(),
                ValidBoundary(),
                ValidPlan() with { Commands = [ValidCommand() with { WorkingDirectory = outsidePath }] }));

        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task DisposableWorkspaceDryRunExecutor_RejectsWriteRootOutsideWorkspace()
    {
        var runner = new FakeProcessRunner();
        var executor = new DisposableWorkspaceControlledDryRunExecutor(runner);
        var outsidePath = Path.Combine(Path.GetTempPath(), "irondev-outside-write-root");

        await AssertRejectsAsync(
            () => executor.ExecuteAsync(
                ValidRequest(),
                ValidBoundary() with { AllowedWriteRootPath = outsidePath },
                ValidPlan()));

        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task DisposableWorkspaceDryRunExecutor_RejectsSourceWorkspacePath()
    {
        var runner = new FakeProcessRunner();
        var executor = new DisposableWorkspaceControlledDryRunExecutor(runner);

        await AssertRejectsAsync(
            () => executor.ExecuteAsync(
                ValidRequest(),
                ValidBoundary() with { WorkspaceKind = "source workspace" },
                ValidPlan()));

        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task DisposableWorkspaceDryRunExecutor_RejectsCommandThatClaimsSourceApply()
    {
        var runner = new FakeProcessRunner();
        var executor = new DisposableWorkspaceControlledDryRunExecutor(runner);

        await AssertRejectsAsync(
            () => executor.ExecuteAsync(
                ValidRequest(),
                ValidBoundary(),
                ValidPlan() with { Commands = [ValidCommand() with { Arguments = ["source apply"] }] }));

        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task DisposableWorkspaceDryRunExecutor_RejectsCommandThatCreatesPatchArtifact()
    {
        var runner = new FakeProcessRunner();
        var executor = new DisposableWorkspaceControlledDryRunExecutor(runner);

        await AssertRejectsAsync(
            () => executor.ExecuteAsync(
                ValidRequest(),
                ValidBoundary(),
                ValidPlan() with { Commands = [ValidCommand() with { Arguments = ["patch artifact"] }] }));

        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    public async Task DisposableWorkspaceDryRunExecutor_RejectsWorkflowOrReleaseCommand()
    {
        foreach (var marker in new[] { "ContinueWorkflow", "ApproveRelease", "ReleaseReady" })
        {
            var runner = new FakeProcessRunner();
            var executor = new DisposableWorkspaceControlledDryRunExecutor(runner);

            await AssertRejectsAsync(
                () => executor.ExecuteAsync(
                    ValidRequest(),
                    ValidBoundary(),
                    ValidPlan() with { Commands = [ValidCommand() with { Arguments = [marker] }] }));

            Assert.AreEqual(0, runner.Calls.Count);
        }
    }

    [TestMethod]
    public void DisposableWorkspaceDryRunExecutor_DoesNotPersistDryRunResult() =>
        AssertNoProductionToken("DryRunResultStore", "SaveDryRunResult", "SqlDryRunResult", "migrate_dry_run");

    [TestMethod]
    public void DisposableWorkspaceDryRunExecutor_DoesNotCreatePatchArtifact() =>
        AssertNoProductionToken("CreatePatchArtifactAsync", "PatchArtifactStore", "PatchArtifactId = Guid.NewGuid");

    [TestMethod]
    public void DisposableWorkspaceDryRunExecutor_DoesNotApplySource() =>
        AssertNoProductionToken("ApplySourceAsync", "SourceApplyService", "ControlledSourceApply", "CanApplySource = true");

    [TestMethod]
    public void DisposableWorkspaceDryRunExecutor_DoesNotContinueWorkflowOrApproveRelease() =>
        AssertNoProductionToken("ContinueWorkflowAsync", "ApproveReleaseAsync", "ReleaseReady = true", "CanApproveRelease = true");

    [TestMethod]
    public void DisposableWorkspaceDryRunExecutor_DoesNotAddApiCliUi()
    {
        foreach (var file in Pr182ChangedFiles())
        {
            var relative = Path.GetRelativePath(RepoRoot(), file);
            foreach (var token in new[] { "Controller", "Program.cs", "Cli", "Tauri", "UI" })
            {
                Assert.IsFalse(relative.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR182 must not add {token}: {relative}");
            }
        }
    }

    [TestMethod]
    public void DisposableWorkspaceDryRunExecutor_DoesNotCallModelsAgentsMemoryRetrieval() =>
        AssertNoProductionToken("LLM", "model call", "AgentDispatch", "PromoteMemory", "ActivateRetrieval");

    [TestMethod]
    public async Task DisposableWorkspaceDryRunExecutor_SanitizesOutput()
    {
        var runner = new FakeProcessRunner
        {
            Output = "raw prompt and chain-of-thought and private reasoning and secret",
            Error = "scratchpad plus bearer"
        };
        var executor = new DisposableWorkspaceControlledDryRunExecutor(runner);

        var report = await executor.ExecuteAsync(ValidRequest(), ValidBoundary(), ValidPlan());
        var commandReport = report.CommandReports.Single();

        Assert.DoesNotContain("raw prompt", commandReport.StandardOutputSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("chain-of-thought", commandReport.StandardOutputSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private reasoning", commandReport.StandardOutputSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", commandReport.StandardOutputSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scratchpad", commandReport.StandardErrorSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bearer", commandReport.StandardErrorSummary, StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(commandReport.StandardOutputSummary, "[redacted]");
    }

    [TestMethod]
    public void DisposableWorkspaceDryRunExecutor_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(ReceiptPath());
        foreach (var statement in new[]
        {
            "PR182 adds the Disposable Workspace Dry-run Executor.",
            "This PR executes controlled dry-runs only inside an explicit disposable workspace boundary.",
            "This PR does not create disposable workspaces.",
            "This PR does not persist dry-run results.",
            "This PR does not create patch artifacts.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not add SQL.",
            "This PR does not add API.",
            "This PR does not add CLI.",
            "This PR does not add UI.",
            "Controlled dry-run execution is not patch artifact creation.",
            "Controlled dry-run execution is not source apply.",
            "Controlled dry-run execution is not rollback.",
            "Controlled dry-run execution is not workflow continuation.",
            "Controlled dry-run execution is not release readiness.",
            "Controlled dry-run execution does not authorize source mutation by itself.",
            "Controlled dry-run report is in-memory only in PR182.",
            "Future controlled dry-runs must run only inside disposable/caged workspaces.",
            "The source workspace is not a dry-run workspace.",
            "The executor consumes a disposable workspace boundary; it does not create one.",
            "Workspace boundary hash must match the dry-run request.",
            "Validation plan hash must match the dry-run request.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block R target is Controlled Dry-run Result Contract.",
            "PR183 - Controlled Dry-run Result Contract"
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private static ControlledDryRunRequest ValidRequest() => new()
    {
        ControlledDryRunRequestId = RequestId,
        ProjectId = ProjectId,
        PolicySatisfactionId = PolicySatisfactionId,
        PolicySatisfactionHash = "sha256:policy-satisfaction",
        SubjectKind = "PatchProposal",
        SubjectId = "patch-proposal-123",
        SubjectHash = "sha256:subject",
        CapabilityCode = "source.apply.preview",
        WorkspaceKind = "disposable workspace",
        WorkspaceId = "workspace-123",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        RequestedOperation = "run focused tests",
        RequestedOperationHash = "sha256:requested-operation",
        ValidationPlanKind = "focused-test-band",
        ValidationPlanId = "validation-plan-123",
        ValidationPlanHash = "sha256:validation-plan",
        RequestedAtUtc = RequestedAtUtc,
        ExpiresAtUtc = RequestedAtUtc.AddHours(1),
        CorrelationId = "correlation-123",
        CausationId = "causation-123",
        EvidenceReferences = ["policy-satisfaction:99999999-8888-7777-6666-555555555555"],
        BoundaryMaxims = ["request is not execution"]
    };

    private static DisposableWorkspaceBoundary ValidBoundary() => new()
    {
        ProjectId = ProjectId,
        WorkspaceId = "workspace-123",
        WorkspaceKind = "disposable workspace",
        WorkspaceRootPath = WorkspaceRoot(),
        AllowedWriteRootPath = WriteRoot(),
        SourceSnapshotReference = "source-snapshot:abc123",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        PreparedAtUtc = PreparedAtUtc,
        ExpiresAtUtc = PreparedAtUtc.AddHours(1),
        BoundaryMaxims = ["workspace is disposable", "source workspace is not writable"]
    };

    private static ControlledDryRunExecutionPlan ValidPlan() => new()
    {
        ValidationPlanId = "validation-plan-123",
        ValidationPlanHash = "sha256:validation-plan",
        Commands = [ValidCommand()],
        ExpectedOutputArtifacts = ["test-summary"],
        BoundaryMaxims = ["execution is in disposable workspace only"]
    };

    private static ControlledDryRunCommand ValidCommand() => new()
    {
        CommandId = "command-1",
        WorkingDirectory = WriteRoot(),
        Executable = "dotnet",
        Arguments = ["test", "--no-restore"],
        TimeoutSeconds = 60
    };

    private static string WorkspaceRoot() =>
        Path.Combine(Path.GetTempPath(), "irondev-pr182-disposable-workspace");

    private static string WriteRoot() =>
        Path.Combine(WorkspaceRoot(), "write-root");

    private static async Task AssertRejectsAsync(Func<Task> action)
    {
        try
        {
            await action();
            Assert.Fail("Expected controlled dry-run executor to reject the request.");
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void AssertNoProductionToken(params string[] tokens)
    {
        foreach (var file in Pr182ProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in tokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Unexpected production token {token} in {file}.");
            }
        }
    }

    private static string[] Pr182ProductionFiles() =>
    [
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "DisposableWorkspaceBoundary.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "ControlledDryRunExecutionPlan.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "ControlledDryRunExecutionReport.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "IControlledDryRunExecutor.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "IControlledDryRunProcessRunner.cs"),
        Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "DisposableWorkspaceControlledDryRunExecutor.cs"),
        Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "ControlledDryRunProcessRunner.cs")
    ];

    private static string[] Pr182ChangedFiles() =>
    [
        .. Pr182ProductionFiles(),
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR182_DISPOSABLE_WORKSPACE_DRY_RUN_EXECUTOR.md"),
        Path.Combine(RepoRoot(), "IronDev.IntegrationTests", "Governance", "DisposableWorkspaceDryRunExecutorTests.cs")
    ];

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR182_DISPOSABLE_WORKSPACE_DRY_RUN_EXECUTOR.md");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class FakeProcessRunner : IControlledDryRunProcessRunner
    {
        public List<ControlledDryRunProcessRequest> Calls { get; } = [];
        public string Output { get; init; } = "tests passed";
        public string Error { get; init; } = string.Empty;
        public int ExitCode { get; init; }
        public bool TimedOut { get; init; }

        public Task<ControlledDryRunProcessResult> RunAsync(
            ControlledDryRunProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(request);
            return Task.FromResult(new ControlledDryRunProcessResult(
                request.CommandId,
                ExitCode,
                TimedOut,
                Output,
                Error));
        }
    }
}
