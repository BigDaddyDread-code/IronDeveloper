using System.Reflection;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("DryRunReceiptWriteIntegration")]
public sealed class DryRunReceiptWriteIntegrationTests
{
    private static readonly Guid RequestId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid PolicySatisfactionId = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private static readonly DateTimeOffset RequestedAtUtc = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PreparedAtUtc = new(2026, 6, 16, 12, 1, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartedAtUtc = new(2026, 6, 16, 12, 2, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CompletedAtUtc = new(2026, 6, 16, 12, 5, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task DryRunReceiptWriteIntegration_ExecutesDryRunBuildsAuditAndSavesReceipt()
    {
        var executor = new FakeExecutor();
        var store = new FakeReceiptStore();
        var writer = new ControlledDryRunReceiptWriter(executor, store, FixedTime());

        var result = await writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary(), ValidPlan());

        Assert.AreEqual(1, executor.Calls);
        Assert.AreEqual(1, store.Saved.Count);
        Assert.AreEqual(result.Audit, store.Saved.Single());
        Assert.AreNotEqual(Guid.Empty, result.DryRunExecutionAuditId);
        StringAssert.StartsWith(result.ExecutionReportHash, "sha256:");
        StringAssert.StartsWith(result.AuditHash, "sha256:");
        Assert.IsTrue(result.DryRunCompleted);
        Assert.IsTrue(result.DryRunSucceeded);
        AssertValid(result.Audit);
    }

    [TestMethod]
    public async Task DryRunReceiptWriteIntegration_BindsAuditToRequestPolicySubjectWorkspaceAndPlan()
    {
        var store = new FakeReceiptStore();
        var writer = new ControlledDryRunReceiptWriter(new FakeExecutor(), store, FixedTime());

        await writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary(), ValidPlan());
        var audit = store.Saved.Single();

        Assert.AreEqual(RequestId, audit.ControlledDryRunRequestId);
        Assert.AreEqual(ProjectId, audit.ProjectId);
        Assert.AreEqual(PolicySatisfactionId, audit.PolicySatisfactionId);
        Assert.AreEqual("sha256:policy-satisfaction", audit.PolicySatisfactionHash);
        Assert.AreEqual("PatchProposal", audit.SubjectKind);
        Assert.AreEqual("patch-proposal-123", audit.SubjectId);
        Assert.AreEqual("sha256:subject", audit.SubjectHash);
        Assert.AreEqual("workspace-123", audit.WorkspaceId);
        Assert.AreEqual("disposable workspace", audit.WorkspaceKind);
        Assert.AreEqual("sha256:workspace-boundary", audit.WorkspaceBoundaryHash);
        Assert.AreEqual("source-snapshot:abc123", audit.SourceSnapshotReference);
        Assert.AreEqual("validation-plan-123", audit.ValidationPlanId);
        Assert.AreEqual("sha256:validation-plan", audit.ValidationPlanHash);
    }

    [TestMethod]
    public async Task DryRunReceiptWriteIntegration_BindsAuditToExecutionReport()
    {
        var store = new FakeReceiptStore();
        var writer = new ControlledDryRunReceiptWriter(new FakeExecutor(), store, FixedTime());

        await writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary(), ValidPlan());
        var audit = store.Saved.Single();

        Assert.AreEqual(StartedAtUtc, audit.StartedAtUtc);
        Assert.AreEqual(CompletedAtUtc, audit.CompletedAtUtc);
        Assert.IsTrue(audit.DryRunCompleted);
        Assert.IsTrue(audit.DryRunSucceeded);
        Assert.AreEqual(1, audit.CommandAudits.Count);
        Assert.AreEqual("command-1", audit.CommandAudits.Single().CommandId);
        Assert.AreEqual(0, audit.CommandAudits.Single().ExitCode);
        StringAssert.StartsWith(audit.CommandAudits.Single().CommandHash, "sha256:");
        StringAssert.StartsWith(audit.CommandAudits.Single().StandardOutputSummaryHash, "sha256:");
        StringAssert.StartsWith(audit.CommandAudits.Single().StandardErrorSummaryHash, "sha256:");
    }

    [TestMethod]
    public void DryRunReceiptWriteIntegration_ComputesDeterministicExecutionReportHash()
    {
        var first = ControlledDryRunAuditHashing.ComputeExecutionReportHash(ValidRequest(), ValidBoundary(), ValidPlan(), ValidReport());
        var second = ControlledDryRunAuditHashing.ComputeExecutionReportHash(ValidRequest(), ValidBoundary(), ValidPlan(), ValidReport());
        var changed = ControlledDryRunAuditHashing.ComputeExecutionReportHash(
            ValidRequest(),
            ValidBoundary(),
            ValidPlan(),
            ValidReport() with
            {
                CommandReports = [ValidCommandReport() with { StandardOutputSummary = "different safe summary" }]
            });

        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, changed);
    }

    [TestMethod]
    public void DryRunReceiptWriteIntegration_ComputesDeterministicAuditHash()
    {
        var audit = ValidAudit();
        var first = ControlledDryRunAuditHashing.ComputeAuditHash(audit);
        var second = ControlledDryRunAuditHashing.ComputeAuditHash(audit);

        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, ControlledDryRunAuditHashing.ComputeAuditHash(audit with { SubjectHash = "sha256:changed-subject" }));
        Assert.AreNotEqual(first, ControlledDryRunAuditHashing.ComputeAuditHash(audit with { WorkspaceBoundaryHash = "sha256:changed-workspace" }));
        Assert.AreNotEqual(first, ControlledDryRunAuditHashing.ComputeAuditHash(audit with { ValidationPlanHash = "sha256:changed-plan" }));
        Assert.AreNotEqual(first, ControlledDryRunAuditHashing.ComputeAuditHash(audit with { CommandAudits = [ValidCommandAudit() with { ExitCode = 1 }] }));
    }

    [TestMethod]
    public async Task DryRunReceiptWriteIntegration_RejectsInvalidRequestBeforeExecution()
    {
        var executor = new FakeExecutor();
        var store = new FakeReceiptStore();
        var writer = new ControlledDryRunReceiptWriter(executor, store, FixedTime());

        await AssertThrowsAsync<InvalidOperationException>(() => writer.ExecuteAndWriteAsync(ValidRequest() with { PolicySatisfactionHash = " " }, ValidBoundary(), ValidPlan()));

        Assert.AreEqual(0, executor.Calls);
        Assert.AreEqual(0, store.Saved.Count);
    }

    [TestMethod]
    public async Task DryRunReceiptWriteIntegration_DoesNotSaveWhenExecutorThrows()
    {
        var executor = new FakeExecutor { ThrowOnExecute = true };
        var store = new FakeReceiptStore();
        var writer = new ControlledDryRunReceiptWriter(executor, store, FixedTime());

        await AssertThrowsAsync<InvalidOperationException>(() => writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary(), ValidPlan()));

        Assert.AreEqual(1, executor.Calls);
        Assert.AreEqual(0, store.Saved.Count);
    }

    [TestMethod]
    public async Task DryRunReceiptWriteIntegration_DoesNotSaveInvalidAudit()
    {
        var executor = new FakeExecutor
        {
            Report = ValidReport() with
            {
                CommandReports = [ValidCommandReport() with { StandardOutputSummary = "private reasoning leaked" }]
            }
        };
        var store = new FakeReceiptStore();
        var writer = new ControlledDryRunReceiptWriter(executor, store, FixedTime());

        await AssertThrowsAsync<InvalidOperationException>(() => writer.ExecuteAndWriteAsync(ValidRequest(), ValidBoundary(), ValidPlan()));

        Assert.AreEqual(1, executor.Calls);
        Assert.AreEqual(0, store.Saved.Count);
    }

    [TestMethod]
    public void DryRunReceiptWriteIntegration_DoesNotCallProcessRunnerDirectly()
    {
        foreach (var token in new[]
        {
            "IControlledDryRunProcessRunner",
            "ControlledDryRunProcessRunner",
            "ProcessStartInfo",
            "Process.Start",
            "System.Diagnostics.Process"
        })
        {
            AssertNoPr185ProductionToken(token);
        }
    }

    [TestMethod]
    public void DryRunReceiptWriteIntegration_DoesNotCreateWorkspace()
    {
        foreach (var token in new[]
        {
            "CreateDisposableWorkspace",
            "PrepareDisposableWorkspace",
            "WorkspaceFactory",
            "CloneRepository",
            "CheckoutWorktree",
            "CreateWorktree"
        })
        {
            AssertNoPr185ProductionToken(token);
        }
    }

    [TestMethod]
    public void DryRunReceiptWriteIntegration_DoesNotCreatePatchArtifactOrApplySource()
    {
        foreach (var token in new[]
        {
            "CreatePatchArtifactAsync",
            "PatchArtifactStore",
            "PatchArtifactId = Guid.NewGuid",
            "ApplySourceAsync",
            "SourceApplyService",
            "ControlledSourceApply"
        })
        {
            AssertNoPr185ProductionToken(token);
        }
    }

    [TestMethod]
    public void DryRunReceiptWriteIntegration_DoesNotContinueWorkflowOrApproveRelease()
    {
        foreach (var token in new[]
        {
            "ContinueWorkflowAsync",
            "ApproveReleaseAsync",
            "ReleaseReady = true",
            "CanApproveRelease = true"
        })
        {
            AssertNoPr185ProductionToken(token);
        }
    }

    [TestMethod]
    public void DryRunReceiptWriteIntegration_DoesNotAddSqlApiCliUi()
    {
        foreach (var file in Pr185ChangedFiles())
        {
            var relative = Path.GetRelativePath(RepoRoot(), file);
            foreach (var token in new[] { "Database", "Controller", "Program.cs", "Cli", "Tauri", "UI" })
            {
                Assert.IsFalse(relative.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR185 must not add {token}: {relative}");
            }
        }
    }

    [TestMethod]
    public void DryRunReceiptWriteIntegration_DoesNotCallModelsAgentsMemoryRetrieval()
    {
        foreach (var token in new[]
        {
            "LLM",
            "model call",
            "AgentDispatch",
            "PromoteMemory",
            "ActivateRetrieval",
            "ToolExecution"
        })
        {
            AssertNoPr185ProductionToken(token);
        }
    }

    [TestMethod]
    public void DryRunReceiptWriteIntegration_UsesReceiptStoreOnlyForPersistence()
    {
        var source = File.ReadAllText(WriterSourcePath());

        StringAssert.Contains(source, "IControlledDryRunReceiptStore");
        StringAssert.Contains(source, ".SaveAsync(");

        foreach (var token in new[] { "SqlConnection", "IDbConnection", "File.Write", "WriteAllText", "AlternateStore", "FallbackStore" })
        {
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"Unexpected persistence token {token}.");
        }
    }

    [TestMethod]
    public void DryRunReceiptWriteIntegration_DoesNotAddReadApi()
    {
        foreach (var token in new[] { "ReadApi", "QueryService", "GetByRequest", "ListByRequest", "Controller" })
        {
            AssertNoPr185ProductionToken(token);
        }
    }

    [TestMethod]
    public void DryRunReceiptWriteIntegration_ResultBoundaryStatesNoDownstreamAuthority()
    {
        foreach (var statement in ReceiptWriteBoundaryStatements())
        {
            StringAssert.Contains(ControlledDryRunReceiptWriteBoundaryText.Boundary, statement);
        }
    }

    [TestMethod]
    public void DryRunReceiptWriteIntegration_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        foreach (var statement in new[]
        {
            "PR185 adds the Dry-run Receipt Write Integration.",
            "This PR composes the controlled dry-run executor, dry-run execution audit contract, and dry-run receipt store.",
            "This PR executes controlled dry-runs only through `IControlledDryRunExecutor`.",
            "This PR writes receipts only through `IControlledDryRunReceiptStore`.",
            "This PR does not call the process runner directly.",
            "This PR does not create disposable workspaces.",
            "This PR does not create patch artifacts.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not add SQL.",
            "This PR does not add API.",
            "This PR does not add CLI.",
            "This PR does not add UI.",
            "The write integration creates a dry-run audit from an execution report and writes it to the receipt store.",
            "The write integration does not package the audit into a patch artifact.",
            "The write integration does not spend the receipt as source-apply authority.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block R target is Dry-run Receipt Read API.",
            "PR186 - Dry-run Receipt Read API",
            "PR185 writes the cage-run receipt. It does not package or spend it."
        })
        {
            StringAssert.Contains(receipt, statement);
        }

        foreach (var statement in ReceiptWriteBoundaryStatements())
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public void DryRunReceiptWriteIntegration_PinsPR184StoreBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR184_DRY_RUN_RECEIPT_STORE.md"));

        StringAssert.Contains(receipt, "Persisted dry-run receipt is not source apply.");
        StringAssert.Contains(receipt, "Dry-run receipt storage records evidence only.");
    }

    [TestMethod]
    public void DryRunReceiptWriteIntegration_PinsPR182ExecutorBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR182_DISPOSABLE_WORKSPACE_DRY_RUN_EXECUTOR.md"));

        StringAssert.Contains(receipt, "Controlled dry-run execution is not patch artifact creation.");
        StringAssert.Contains(receipt, "Controlled dry-run execution is not source apply.");
        StringAssert.Contains(receipt, "Controlled dry-run report is in-memory only in PR182.");
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

    private static ControlledDryRunExecutionAudit ValidAudit() => new()
    {
        DryRunExecutionAuditId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"),
        ProjectId = ProjectId,
        ControlledDryRunRequestId = RequestId,
        PolicySatisfactionId = PolicySatisfactionId,
        PolicySatisfactionHash = "sha256:policy-satisfaction",
        SubjectKind = "PatchProposal",
        SubjectId = "patch-proposal-123",
        SubjectHash = "sha256:subject",
        WorkspaceId = "workspace-123",
        WorkspaceKind = "disposable workspace",
        WorkspaceBoundaryHash = "sha256:workspace-boundary",
        SourceSnapshotReference = "source-snapshot:abc123",
        ValidationPlanId = "validation-plan-123",
        ValidationPlanHash = "sha256:validation-plan",
        StartedAtUtc = StartedAtUtc,
        CompletedAtUtc = CompletedAtUtc,
        DryRunCompleted = true,
        DryRunSucceeded = true,
        ExecutionReportHash = "sha256:execution-report",
        AuditHash = "sha256:audit",
        CommandAudits = [ValidCommandAudit()],
        EvidenceReferences = ["controlled-dry-run-request:11111111-2222-3333-4444-555555555555"],
        BoundaryMaxims = ["audit records evidence only"],
        Boundary = ControlledDryRunExecutionAuditBoundaryText.Boundary
    };

    private static ControlledDryRunCommandAudit ValidCommandAudit() => new()
    {
        CommandId = "command-1",
        WorkingDirectory = WriteRoot(),
        Executable = "dotnet",
        CommandHash = "sha256:command",
        ExitCode = 0,
        TimedOut = false,
        StandardOutputSummaryHash = "sha256:stdout",
        StandardErrorSummaryHash = "sha256:stderr",
        StandardOutputSummary = "tests passed",
        StandardErrorSummary = "none"
    };

    private static string[] ReceiptWriteBoundaryStatements() =>
    [
        "Dry-run receipt write integration is not patch artifact creation.",
        "Dry-run receipt write integration is not source apply.",
        "Dry-run receipt write integration is not rollback.",
        "Dry-run receipt write integration is not workflow continuation.",
        "Dry-run receipt write integration is not release readiness.",
        "Dry-run receipt write integration does not authorize source mutation by itself.",
        "Dry-run receipt write integration records cage-run evidence only."
    ];

    private static string WorkspaceRoot() =>
        Path.Combine(Path.GetTempPath(), "irondev-pr185-disposable-workspace");

    private static string WriteRoot() =>
        Path.Combine(WorkspaceRoot(), "write-root");

    private static TimeProvider FixedTime() =>
        new FixedTimeProvider(new DateTimeOffset(2026, 6, 16, 12, 10, 0, TimeSpan.Zero));

    private static void AssertValid(ControlledDryRunExecutionAudit audit)
    {
        var result = ControlledDryRunExecutionAuditValidation.Validate(audit);
        Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code}:{issue.Field}:{issue.Message}")));
    }

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

    private static void AssertNoPr185ProductionToken(string token)
    {
        foreach (var file in Pr185ProductionFiles())
        {
            Assert.IsFalse(File.ReadAllText(file).Contains(token, StringComparison.Ordinal), $"Unexpected production token {token} in {file}.");
        }
    }

    private static string[] Pr185ProductionFiles() =>
    [
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "IControlledDryRunReceiptWriter.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "ControlledDryRunReceiptWriteModels.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "ControlledDryRunAuditHashing.cs"),
        Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "ControlledDryRunReceiptWriter.cs")
    ];

    private static string[] Pr185ChangedFiles() =>
    [
        .. Pr185ProductionFiles(),
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR185_DRY_RUN_RECEIPT_WRITE_INTEGRATION.md"),
        Path.Combine(RepoRoot(), "IronDev.IntegrationTests", "Governance", "DryRunReceiptWriteIntegrationTests.cs")
    ];

    private static string WriterSourcePath() =>
        Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "ControlledDryRunReceiptWriter.cs");

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR185_DRY_RUN_RECEIPT_WRITE_INTEGRATION.md");

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

        public Task SaveAsync(ControlledDryRunExecutionAudit audit, CancellationToken cancellationToken = default)
        {
            Saved.Add(audit);
            return Task.CompletedTask;
        }

        public Task<ControlledDryRunExecutionAudit?> GetAsync(Guid projectId, Guid dryRunExecutionAuditId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.SingleOrDefault(audit => audit.ProjectId == projectId && audit.DryRunExecutionAuditId == dryRunExecutionAuditId));

        public Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByRequestAsync(Guid projectId, Guid controlledDryRunRequestId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ControlledDryRunExecutionAudit>>(Saved.Where(audit => audit.ProjectId == projectId && audit.ControlledDryRunRequestId == controlledDryRunRequestId).ToArray());

        public Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByPolicySatisfactionAsync(Guid projectId, Guid policySatisfactionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ControlledDryRunExecutionAudit>>(Saved.Where(audit => audit.ProjectId == projectId && audit.PolicySatisfactionId == policySatisfactionId).ToArray());

        public Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListBySubjectAsync(Guid projectId, string subjectKind, string subjectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ControlledDryRunExecutionAudit>>(Saved.Where(audit => audit.ProjectId == projectId && audit.SubjectKind == subjectKind && audit.SubjectId == subjectId).ToArray());

        public Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByAuditHashAsync(Guid projectId, string auditHash, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ControlledDryRunExecutionAudit>>(Saved.Where(audit => audit.ProjectId == projectId && audit.AuditHash == auditHash).ToArray());
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
