using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("DryRunFailureRegression")]
public sealed class DryRunFailureRegressionTests
{
    private static readonly Guid RequestId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid PolicySatisfactionId = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private static readonly DateTimeOffset RequestedAtUtc = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PreparedAtUtc = new(2026, 6, 16, 12, 1, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartedAtUtc = new(2026, 6, 16, 12, 2, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CompletedAtUtc = new(2026, 6, 16, 12, 5, 0, TimeSpan.Zero);

    [TestMethod]
    public void DryRunFailureRegression_ReceiptExistsAndStatesTestOnly()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        foreach (var statement in new[]
        {
            "PR186 adds Dry-run Failure Regression Tests.",
            "This PR is tests/receipt only.",
            "This PR adds no production code.",
            "This PR adds no SQL.",
            "This PR adds no API.",
            "This PR adds no CLI.",
            "This PR adds no UI.",
            "This PR does not create disposable workspaces.",
            "This PR does not execute real dry-runs.",
            "This PR does not create patch artifacts.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "Invalid dry-run input must fail before execution.",
            "Executor failure must not write a receipt.",
            "Invalid execution reports must not write a receipt.",
            "Invalid audits must not write a receipt.",
            "Completed failed dry-runs may be recorded as evidence only.",
            "A failed dry-run receipt is not patch artifact creation.",
            "A failed dry-run receipt is not source apply.",
            "A failed dry-run receipt is not rollback.",
            "A failed dry-run receipt is not workflow continuation.",
            "A failed dry-run receipt is not release readiness.",
            "A failed dry-run receipt does not authorize source mutation by itself.",
            "No execution report means no receipt.",
            "No valid audit means no receipt.",
            "No valid store write means no receipt.",
            "No fallback store is allowed.",
            "No modified-audit retry is allowed.",
            "No downstream authority may be created from dry-run failure.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block R target is Dry-run Receipt Read API.",
            "PR187 - Dry-run Receipt Read API",
            "PR186 proves the cage fails closed. It does not add new doors."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public async Task DryRunFailureRegression_InvalidRequestDoesNotExecuteOrSave()
    {
        var executor = new FakeExecutor();
        var store = new FakeReceiptStore();
        var writer = Writer(executor, store);

        await AssertThrowsAsync<InvalidOperationException>(() => writer.ExecuteAndWriteAsync(ValidRequest() with { PolicySatisfactionHash = " " }, ValidBoundary(), ValidPlan()));

        Assert.AreEqual(0, executor.Calls);
        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task DryRunFailureRegression_ProjectMismatchDoesNotExecuteOrSave()
    {
        var executor = new FakeExecutor();
        var store = new FakeReceiptStore();
        var writer = Writer(executor, store);

        await AssertThrowsAsync<InvalidOperationException>(() => writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary() with { ProjectId = Guid.NewGuid() }, ValidPlan()));

        Assert.AreEqual(0, executor.Calls);
        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task DryRunFailureRegression_WorkspaceMismatchDoesNotExecuteOrSave()
    {
        var executor = new FakeExecutor();
        var store = new FakeReceiptStore();
        var writer = Writer(executor, store);

        await AssertThrowsAsync<InvalidOperationException>(() => writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary() with { WorkspaceId = "other-workspace" }, ValidPlan()));

        Assert.AreEqual(0, executor.Calls);
        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task DryRunFailureRegression_WorkspaceBoundaryHashMismatchDoesNotExecuteOrSave()
    {
        var executor = new FakeExecutor();
        var store = new FakeReceiptStore();
        var writer = Writer(executor, store);

        await AssertThrowsAsync<InvalidOperationException>(() => writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary() with { WorkspaceBoundaryHash = "sha256:other-workspace-boundary" }, ValidPlan()));

        Assert.AreEqual(0, executor.Calls);
        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task DryRunFailureRegression_ValidationPlanMismatchDoesNotExecuteOrSave()
    {
        var executor = new FakeExecutor();
        var store = new FakeReceiptStore();
        var writer = Writer(executor, store);

        await AssertThrowsAsync<InvalidOperationException>(() => writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary(), ValidPlan() with { ValidationPlanHash = "sha256:other-validation-plan" }));

        Assert.AreEqual(0, executor.Calls);
        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task DryRunFailureRegression_ExpiredWorkspaceBoundaryDoesNotExecuteOrSave()
    {
        var executor = new FakeExecutor();
        var store = new FakeReceiptStore();
        var writer = Writer(executor, store);

        await AssertThrowsAsync<InvalidOperationException>(() => writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary() with { ExpiresAtUtc = RequestedAtUtc.AddMinutes(10) }, ValidPlan()));

        Assert.AreEqual(0, executor.Calls);
        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task DryRunFailureRegression_InvalidExecutionPlanDoesNotExecuteOrSave()
    {
        foreach (var plan in new[]
        {
            ValidPlan() with { Commands = [] },
            ValidPlan() with { Commands = [ValidCommand() with { CommandId = " " }] },
            ValidPlan() with { Commands = [ValidCommand() with { AllowNetwork = true }] },
            ValidPlan() with { Commands = [ValidCommand() with { AllowSourceWorkspaceWrite = true }] }
        })
        {
            var executor = new FakeExecutor();
            var store = new FakeReceiptStore();
            var writer = Writer(executor, store);

            await AssertThrowsAsync<InvalidOperationException>(() => writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary(), plan));

            Assert.AreEqual(0, executor.Calls);
            Assert.AreEqual(0, store.SaveCalls);
        }
    }

    [TestMethod]
    public async Task DryRunFailureRegression_ExecutorThrowDoesNotSaveReceipt()
    {
        var executor = new FakeExecutor { ThrowOnExecute = true };
        var store = new FakeReceiptStore();
        var writer = Writer(executor, store);

        await AssertThrowsAsync<InvalidOperationException>(() => writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary(), ValidPlan()));

        Assert.AreEqual(1, executor.Calls);
        Assert.AreEqual(0, store.SaveCalls);
        Assert.AreEqual(0, store.Saved.Count);
    }

    [TestMethod]
    public async Task DryRunFailureRegression_ReportRequestMismatchDoesNotSaveReceipt()
    {
        var store = await AssertReportRejectedAsync(ValidReport() with { ControlledDryRunRequestId = Guid.NewGuid() });

        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task DryRunFailureRegression_ReportProjectMismatchDoesNotSaveReceipt()
    {
        var store = await AssertReportRejectedAsync(ValidReport() with { ProjectId = Guid.NewGuid() });

        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task DryRunFailureRegression_ReportWorkspaceBoundaryMismatchDoesNotSaveReceipt()
    {
        var store = await AssertReportRejectedAsync(ValidReport() with { WorkspaceBoundaryHash = "sha256:other-workspace-boundary" });

        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task DryRunFailureRegression_ReportValidationPlanMismatchDoesNotSaveReceipt()
    {
        var store = await AssertReportRejectedAsync(ValidReport() with { ValidationPlanHash = "sha256:other-validation-plan" });

        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task DryRunFailureRegression_ReportWithUnknownCommandDoesNotSaveReceipt()
    {
        var store = await AssertReportRejectedAsync(ValidReport() with { CommandReports = [ValidCommandReport() with { CommandId = "unknown-command" }] });

        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task DryRunFailureRegression_ReportWithoutCommandReportsDoesNotSaveReceipt()
    {
        var store = await AssertReportRejectedAsync(ValidReport() with { CommandReports = [] });

        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task DryRunFailureRegression_ReportWithPrivateMaterialDoesNotSaveReceipt()
    {
        foreach (var marker in new[] { "raw prompt", "chain-of-thought", "private reasoning", "scratchpad", "secret", "bearer" })
        {
            var store = await AssertReportRejectedAsync(ValidReport() with
            {
                CommandReports = [ValidCommandReport() with { StandardOutputSummary = $"summary leaked {marker}" }]
            });

            Assert.AreEqual(0, store.SaveCalls);
        }
    }

    [TestMethod]
    public async Task DryRunFailureRegression_ReportWithAuthorityClaimDoesNotSaveReceipt()
    {
        foreach (var marker in new[] { "patch artifact created", "source applied", "workflow continued", "release ready", "rollback executed" })
        {
            var store = await AssertReportRejectedAsync(ValidReport() with
            {
                CommandReports = [ValidCommandReport() with { StandardErrorSummary = $"summary claims {marker}" }]
            });

            Assert.AreEqual(0, store.SaveCalls);
        }
    }

    [TestMethod]
    public async Task DryRunFailureRegression_StoreFailureDoesNotRetryOrFallback()
    {
        var executor = new FakeExecutor();
        var store = new FakeReceiptStore { ThrowOnSave = true };
        var writer = Writer(executor, store);

        await AssertThrowsAsync<InvalidOperationException>(() => writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary(), ValidPlan()));

        Assert.AreEqual(1, executor.Calls);
        Assert.AreEqual(1, store.SaveCalls);
        Assert.AreEqual(0, store.Saved.Count);
    }

    [TestMethod]
    public async Task DryRunFailureRegression_CompletedFailedDryRunWritesFailureReceiptAsEvidenceOnly()
    {
        var executor = new FakeExecutor
        {
            Report = ValidReport() with
            {
                DryRunCompleted = true,
                DryRunSucceeded = false,
                CommandReports = [ValidCommandReport() with { ExitCode = 1, StandardErrorSummary = "tests failed safely" }]
            }
        };
        var store = new FakeReceiptStore();
        var writer = Writer(executor, store);

        var result = await writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary(), ValidPlan());

        Assert.AreEqual(1, store.SaveCalls);
        Assert.AreEqual(1, store.Saved.Count);
        Assert.IsTrue(store.Saved.Single().DryRunCompleted);
        Assert.IsFalse(store.Saved.Single().DryRunSucceeded);
        Assert.IsTrue(result.DryRunCompleted);
        Assert.IsFalse(result.DryRunSucceeded);
        StringAssert.Contains(result.Boundary, "Dry-run receipt write integration does not authorize source mutation by itself.");
        StringAssert.Contains(result.Boundary, "Dry-run receipt write integration records cage-run evidence only.");
    }

    [TestMethod]
    public void DryRunFailureRegression_FailedDryRunDoesNotCreatePatchArtifactOrApplySource()
    {
        foreach (var token in new[]
        {
            "Create" + "Patch" + "ArtifactAsync",
            "Patch" + "ArtifactStore",
            "Patch" + "ArtifactId = Guid.NewGuid",
            "Apply" + "SourceAsync",
            "Source" + "ApplyService",
            "Controlled" + "Source" + "Apply",
            "Can" + "Apply" + "Source = true"
        })
        {
            AssertChangedFilesDoNotContain(token);
        }
    }

    [TestMethod]
    public void DryRunFailureRegression_FailedDryRunDoesNotContinueWorkflowOrApproveRelease()
    {
        foreach (var token in new[]
        {
            "Continue" + "WorkflowAsync",
            "Approve" + "ReleaseAsync",
            "Release" + "Ready = true",
            "Can" + "Approve" + "Release = true"
        })
        {
            AssertChangedFilesDoNotContain(token);
        }
    }

    [TestMethod]
    public void DryRunFailureRegression_DoesNotAddSqlApiCliUi()
    {
        var expected = new[]
        {
            Path.Combine("Docs", "receipts", "PR186_DRY_RUN_FAILURE_REGRESSION_TESTS.md"),
            Path.Combine("IronDev.IntegrationTests", "Governance", "DryRunFailureRegressionTests.cs")
        };

        CollectionAssert.AreEqual(expected, Pr186ChangedFiles().Select(file => Path.GetRelativePath(RepoRoot(), file)).ToArray());

        foreach (var file in Pr186ChangedFiles())
        {
            var relative = Path.GetRelativePath(RepoRoot(), file);
            foreach (var token in new[] { "Database", "Controller", "Program.cs", "Cli", "Tauri", "UI" })
            {
                Assert.IsFalse(relative.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR186 must not add {token}: {relative}");
            }
        }
    }

    [TestMethod]
    public void DryRunFailureRegression_DoesNotAddRuntimeSchedulerAgentModelToolMemoryRetrieval()
    {
        foreach (var token in new[]
        {
            "IHosted" + "Service",
            "Background" + "Service",
            "Scheduler",
            "Agent" + "Dispatch",
            "model call",
            "LLM",
            "Tool" + "Execution",
            "Promote" + "Memory",
            "Activate" + "Retrieval"
        })
        {
            AssertChangedFilesDoNotContain(token);
        }
    }

    [TestMethod]
    public void DryRunFailureRegression_PinsPriorBoundaries()
    {
        var pr182 = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR182_DISPOSABLE_WORKSPACE_DRY_RUN_EXECUTOR.md"));
        var pr184 = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR184_DRY_RUN_RECEIPT_STORE.md"));
        var pr185 = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR185_DRY_RUN_RECEIPT_WRITE_INTEGRATION.md"));

        StringAssert.Contains(pr182, "Controlled dry-run execution is not source apply.");
        StringAssert.Contains(pr182, "Controlled dry-run report is in-memory only in PR182.");
        StringAssert.Contains(pr184, "Persisted dry-run receipt is not source apply.");
        StringAssert.Contains(pr184, "Dry-run receipt storage records evidence only.");
        StringAssert.Contains(pr185, "Dry-run receipt write integration is not source apply.");
        StringAssert.Contains(pr185, "Dry-run receipt write integration records cage-run evidence only.");
    }

    private static ControlledDryRunReceiptWriter Writer(FakeExecutor executor, FakeReceiptStore store) =>
        new(executor, store, FixedTime());

    private static async Task<FakeReceiptStore> AssertReportRejectedAsync(ControlledDryRunExecutionReport report)
    {
        var executor = new FakeExecutor { Report = report };
        var store = new FakeReceiptStore();
        var writer = Writer(executor, store);

        await AssertThrowsAsync<InvalidOperationException>(() => writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary(), ValidPlan()));

        Assert.AreEqual(1, executor.Calls);
        return store;
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
        BoundaryMaxims = ["controlled dry-run request is not execution"]
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

    private static ControlledDryRunExecutionReport ValidReport() => new()
    {
        ControlledDryRunRequestId = RequestId,
        ProjectId = ProjectId,
        WorkspaceId = "workspace-123",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        ValidationPlanId = "validation-plan-123",
        ValidationPlanHash = "sha256:validation-plan",
        StartedAtUtc = StartedAtUtc,
        CompletedAtUtc = CompletedAtUtc,
        CommandReports = [ValidCommandReport()],
        DryRunCompleted = true,
        DryRunSucceeded = true,
        Boundary = ControlledDryRunExecutionBoundaryText.Boundary,
        Warnings = ["Controlled dry-run report is in-memory only."]
    };

    private static ControlledDryRunCommandReport ValidCommandReport() => new()
    {
        CommandId = "command-1",
        ExitCode = 0,
        TimedOut = false,
        StandardOutputSummary = "tests passed",
        StandardErrorSummary = "none"
    };

    private static string WorkspaceRoot() =>
        Path.Combine(Path.GetTempPath(), "irondev-pr186-disposable-workspace");

    private static string WriteRoot() =>
        Path.Combine(WorkspaceRoot(), "write-root");

    private static TimeProvider FixedTime() =>
        new FixedTimeProvider(new DateTimeOffset(2026, 6, 16, 12, 10, 0, TimeSpan.Zero));

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
            Assert.Fail($"Expected {typeof(TException).Name}.");
        }
        catch (TException)
        {
        }
    }

    private static void AssertChangedFilesDoNotContain(string token)
    {
        foreach (var file in Pr186ChangedFiles().Where(file => !file.EndsWith("DryRunFailureRegressionTests.cs", StringComparison.Ordinal)))
        {
            Assert.IsFalse(File.ReadAllText(file).Contains(token, StringComparison.Ordinal), $"Unexpected token {token} in {file}.");
        }
    }

    private static string[] Pr186ChangedFiles() =>
    [
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR186_DRY_RUN_FAILURE_REGRESSION_TESTS.md"),
        Path.Combine(RepoRoot(), "IronDev.IntegrationTests", "Governance", "DryRunFailureRegressionTests.cs")
    ];

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR186_DRY_RUN_FAILURE_REGRESSION_TESTS.md");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class FakeExecutor : IControlledDryRunExecutor
    {
        public int Calls { get; private set; }
        public bool ThrowOnExecute { get; init; }
        public ControlledDryRunExecutionReport Report { get; init; } = ValidReport();

        public Task<ControlledDryRunExecutionReport> ExecuteAsync(
            ControlledDryRunRequest request,
            DisposableWorkspaceBoundary workspaceBoundary,
            ControlledDryRunExecutionPlan executionPlan,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (ThrowOnExecute)
            {
                throw new InvalidOperationException("executor failed");
            }

            return Task.FromResult(Report);
        }
    }

    private sealed class FakeReceiptStore : IControlledDryRunReceiptStore
    {
        public List<ControlledDryRunExecutionAudit> Saved { get; } = [];
        public bool ThrowOnSave { get; init; }
        public int SaveCalls { get; private set; }

        public Task SaveAsync(ControlledDryRunExecutionAudit audit, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("store failed");
            }

            Saved.Add(audit);
            return Task.CompletedTask;
        }

        public Task<ControlledDryRunExecutionAudit?> GetAsync(Guid projectId, Guid dryRunExecutionAuditId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByRequestAsync(Guid projectId, Guid controlledDryRunRequestId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByPolicySatisfactionAsync(Guid projectId, Guid policySatisfactionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListBySubjectAsync(Guid projectId, string subjectKind, string subjectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByAuditHashAsync(Guid projectId, string auditHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
