using System.Diagnostics;
using System.Reflection;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("PatchArtifactCreation")]
public sealed class PatchArtifactCreationIntegrationTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DryRunExecutionAuditId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ControlledDryRunRequestId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid PolicySatisfactionId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid PatchArtifactId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly DateTimeOffset CreatedAtUtc = new(2026, 6, 16, 16, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task PatchArtifactCreation_CreatesValidPatchArtifactFromSuccessfulDryRunReceipt()
    {
        var audit = ValidAudit();
        var receiptStore = new FakeReceiptStore(audit);
        var artifactStore = new FakePatchArtifactStore();
        var request = ValidRequest();
        var creator = Creator(receiptStore, artifactStore);

        var result = await creator.CreateAsync(request);

        Assert.AreEqual(1, receiptStore.GetCount);
        Assert.AreEqual(0, artifactStore.SaveCount);
        Assert.IsFalse(result.Stored);
        AssertValid(result.PatchArtifact);
        AssertValidBase(result.PatchArtifact, audit, request);
        Assert.AreEqual(request.ProjectId, result.PatchArtifact.ProjectId);
        Assert.AreEqual(audit.DryRunExecutionAuditId, result.PatchArtifact.DryRunExecutionAuditId);
        Assert.AreEqual(audit.AuditHash, result.PatchArtifact.DryRunAuditHash);
        Assert.AreEqual(request.DryRunReceiptHash, result.PatchArtifact.DryRunReceiptHash);
        Assert.AreEqual(audit.PolicySatisfactionId, result.PatchArtifact.PolicySatisfactionId);
        Assert.AreEqual(audit.PolicySatisfactionHash, result.PatchArtifact.PolicySatisfactionHash);
        Assert.AreEqual(audit.SubjectKind, result.PatchArtifact.SubjectKind);
        Assert.AreEqual(audit.SubjectId, result.PatchArtifact.SubjectId);
        Assert.AreEqual(audit.SubjectHash, result.PatchArtifact.SubjectHash);
        Assert.AreEqual(audit.SourceSnapshotReference, result.PatchArtifact.SourceSnapshotReference);
        Assert.AreEqual(request.SourceBaselineHash, result.PatchArtifact.SourceBaselineHash);
        Assert.AreEqual(audit.WorkspaceBoundaryHash, result.PatchArtifact.WorkspaceBoundaryHash);
        Assert.AreEqual(audit.ValidationPlanId, result.PatchArtifact.ValidationPlanId);
        Assert.AreEqual(audit.ValidationPlanHash, result.PatchArtifact.ValidationPlanHash);
        StringAssert.StartsWith(result.PatchHash, "sha256:");
        StringAssert.StartsWith(result.ChangeSetHash, "sha256:");
        CollectionAssert.Contains(result.PatchArtifact.EvidenceReferences.ToArray(), $"controlled-dry-run-receipt:{DryRunExecutionAuditId:D}");
        CollectionAssert.Contains(result.PatchArtifact.EvidenceReferences.ToArray(), $"controlled-dry-run-audit:{audit.AuditHash}");
        CollectionAssert.Contains(result.PatchArtifact.EvidenceReferences.ToArray(), $"patch-artifact-created-from-dry-run:{DryRunExecutionAuditId:D}");
    }

    [TestMethod]
    public async Task PatchArtifactCreation_CreateAndStorePersistsAfterValidation()
    {
        var receiptStore = new FakeReceiptStore(ValidAudit());
        var artifactStore = new FakePatchArtifactStore();
        var creator = Creator(receiptStore, artifactStore);

        var result = await creator.CreateAndStoreAsync(ValidRequest());

        Assert.AreEqual(1, artifactStore.SaveCount);
        Assert.AreSame(result.PatchArtifact, artifactStore.Saved.Single());
        Assert.IsTrue(result.Stored);
    }

    [TestMethod]
    public async Task PatchArtifactCreation_RejectsMissingDryRunReceipt()
    {
        var receiptStore = new FakeReceiptStore(null);
        var artifactStore = new FakePatchArtifactStore();

        await AssertCreationFailsAsync(
            () => Creator(receiptStore, artifactStore).CreateAndStoreAsync(ValidRequest()),
            "DRY_RUN_RECEIPT_NOT_FOUND");

        Assert.AreEqual(1, receiptStore.GetCount);
        Assert.AreEqual(0, artifactStore.SaveCount);
    }

    [TestMethod]
    public async Task PatchArtifactCreation_RejectsProjectMismatch()
    {
        var artifactStore = new FakePatchArtifactStore();
        var audit = ValidAudit() with { ProjectId = Guid.NewGuid() };

        await AssertCreationFailsAsync(
            () => Creator(new FakeReceiptStore(audit), artifactStore).CreateAndStoreAsync(ValidRequest()),
            "PROJECT_ID_MISMATCH");

        Assert.AreEqual(0, artifactStore.SaveCount);
    }

    [TestMethod]
    public async Task PatchArtifactCreation_RejectsDryRunAuditHashMismatch()
    {
        var artifactStore = new FakePatchArtifactStore();
        var audit = ValidAudit() with { AuditHash = "sha256:different-audit" };

        await AssertCreationFailsAsync(
            () => Creator(new FakeReceiptStore(audit), artifactStore).CreateAndStoreAsync(ValidRequest()),
            "DRY_RUN_AUDIT_HASH_MISMATCH");

        Assert.AreEqual(0, artifactStore.SaveCount);
    }

    [TestMethod]
    public async Task PatchArtifactCreation_RejectsIncompleteDryRun()
    {
        var artifactStore = new FakePatchArtifactStore();
        var audit = ValidAudit() with { DryRunCompleted = false };

        await AssertCreationFailsAsync(
            () => Creator(new FakeReceiptStore(audit), artifactStore).CreateAndStoreAsync(ValidRequest()),
            "DRY_RUN_NOT_COMPLETED");

        Assert.AreEqual(0, artifactStore.SaveCount);
    }

    [TestMethod]
    public async Task PatchArtifactCreation_RejectsFailedDryRun()
    {
        var artifactStore = new FakePatchArtifactStore();
        var audit = ValidAudit() with { DryRunCompleted = true, DryRunSucceeded = false };

        await AssertCreationFailsAsync(
            () => Creator(new FakeReceiptStore(audit), artifactStore).CreateAndStoreAsync(ValidRequest()),
            "DRY_RUN_NOT_SUCCESSFUL");

        Assert.AreEqual(0, artifactStore.SaveCount);
    }

    [TestMethod]
    public async Task PatchArtifactCreation_RejectsInvalidAudit()
    {
        var artifactStore = new FakePatchArtifactStore();
        var audit = ValidAudit() with { EvidenceReferences = [] };

        await AssertCreationFailsAsync(
            () => Creator(new FakeReceiptStore(audit), artifactStore).CreateAndStoreAsync(ValidRequest()),
            "DRY_RUN_AUDIT_INVALID");

        Assert.AreEqual(0, artifactStore.SaveCount);
    }

    [TestMethod]
    public async Task PatchArtifactCreation_RejectsInvalidRequestBeforeReceiptLookup()
    {
        var receiptStore = new FakeReceiptStore(ValidAudit());
        var artifactStore = new FakePatchArtifactStore();
        var invalid = ValidRequest() with { ProjectId = Guid.Empty };

        await AssertCreationFailsAsync(
            () => Creator(receiptStore, artifactStore).CreateAndStoreAsync(invalid),
            "PROJECT_ID_REQUIRED");

        Assert.AreEqual(0, receiptStore.GetCount);
        Assert.AreEqual(0, artifactStore.SaveCount);
    }

    [TestMethod]
    public async Task PatchArtifactCreation_RejectsUnsafeFileChanges()
    {
        var artifactStore = new FakePatchArtifactStore();
        var unsafePrivate = ValidRequest() with
        {
            FileChanges = [ModifyChange("src/private.cs") with { NormalizedDiff = "raw prompt leaked" }]
        };
        var unsafeAuthority = ValidRequest() with
        {
            FileChanges = [ModifyChange("src/authority.cs") with { NormalizedDiff = "source applied" }]
        };
        var unsafePath = ValidRequest() with
        {
            FileChanges = [ModifyChange("../outside.cs")]
        };

        await AssertCreationFailsAsync(
            () => Creator(new FakeReceiptStore(ValidAudit()), artifactStore).CreateAndStoreAsync(unsafePrivate),
            "PRIVATE_OR_RAW_MATERIAL_REJECTED");
        await AssertCreationFailsAsync(
            () => Creator(new FakeReceiptStore(ValidAudit()), artifactStore).CreateAndStoreAsync(unsafeAuthority),
            "AUTHORITY_CLAIM_REJECTED");
        await AssertCreationFailsAsync(
            () => Creator(new FakeReceiptStore(ValidAudit()), artifactStore).CreateAndStoreAsync(unsafePath),
            "PATCH_ARTIFACT_INVALID");

        Assert.AreEqual(0, artifactStore.SaveCount);
    }

    [TestMethod]
    public void PatchArtifactCreation_ComputesDeterministicChangeSetHash()
    {
        var change = ModifyChange("src/Foo.cs");
        var hash1 = PatchArtifactHashing.ComputeChangeSetHash([change]);
        var hash2 = PatchArtifactHashing.ComputeChangeSetHash([change]);
        var changedDiff = PatchArtifactHashing.ComputeChangeSetHash([change with { NormalizedDiff = change.NormalizedDiff + "\n+safe" }]);
        var changedAfter = PatchArtifactHashing.ComputeChangeSetHash([change with { AfterContentHash = "sha256:changed-after" }]);

        Assert.AreEqual(hash1, hash2);
        Assert.AreNotEqual(hash1, changedDiff);
        Assert.AreNotEqual(hash1, changedAfter);
    }

    [TestMethod]
    public async Task PatchArtifactCreation_ComputesDeterministicPatchHash()
    {
        var result1 = await Creator(new FakeReceiptStore(ValidAudit()), new FakePatchArtifactStore()).CreateAsync(ValidRequest());
        var result2 = await Creator(new FakeReceiptStore(ValidAudit()), new FakePatchArtifactStore()).CreateAsync(ValidRequest());
        var receiptChanged = await Creator(new FakeReceiptStore(ValidAudit()), new FakePatchArtifactStore()).CreateAsync(ValidRequest() with { DryRunReceiptHash = "sha256:changed-receipt" });
        var baselineChanged = await Creator(new FakeReceiptStore(ValidAudit()), new FakePatchArtifactStore()).CreateAsync(ValidRequest() with { SourceBaselineHash = "sha256:changed-source-baseline" });
        var fileChanged = await Creator(new FakeReceiptStore(ValidAudit()), new FakePatchArtifactStore()).CreateAsync(ValidRequest() with { FileChanges = [ModifyChange("src/Changed.cs")] });

        Assert.AreEqual(result1.PatchHash, result2.PatchHash);
        Assert.AreNotEqual(result1.PatchHash, receiptChanged.PatchHash);
        Assert.AreNotEqual(result1.PatchHash, baselineChanged.PatchHash);
        Assert.AreNotEqual(result1.PatchHash, fileChanged.PatchHash);
    }

    [TestMethod]
    public void PatchArtifactCreation_DoesNotAcceptCallerSuppliedPatchHash()
    {
        var names = typeof(PatchArtifactCreationRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).ToArray();

        CollectionAssert.DoesNotContain(names, "PatchHash");
        CollectionAssert.DoesNotContain(names, "ChangeSetHash");
    }

    [TestMethod]
    public void PatchArtifactCreation_UsesPatchBaseHashValidationBeforeSave()
    {
        var source = File.ReadAllText(CreatorSourcePath());
        var validationIndex = source.IndexOf("PatchBaseHashValidation.Validate", StringComparison.Ordinal);
        var saveIndex = source.IndexOf("SaveAsync", StringComparison.Ordinal);

        Assert.IsTrue(validationIndex >= 0, "Creator must call PatchBaseHashValidation.Validate.");
        Assert.IsTrue(saveIndex >= 0, "Creator must call SaveAsync.");
        Assert.IsTrue(validationIndex < saveIndex, "PatchBaseHashValidation.Validate must appear before SaveAsync in the creator source.");
    }

    [TestMethod]
    public async Task PatchArtifactCreation_StoreFailureDoesNotFallbackOrApplySource()
    {
        var artifactStore = new FakePatchArtifactStore { ThrowOnSave = true };

        await AssertThrowsAsync<InvalidOperationException>(() =>
            Creator(new FakeReceiptStore(ValidAudit()), artifactStore).CreateAndStoreAsync(ValidRequest()));

        Assert.AreEqual(1, artifactStore.SaveCount);
        AssertNoProductionToken("FallbackPatchArtifactStore");
        AssertNoProductionToken("ApplySourceAsync");
    }

    [TestMethod]
    public void PatchArtifactCreation_DoesNotRunDryRunOrCreateWorkspace()
    {
        foreach (var token in new[]
        {
            "IControlledDryRunExecutor",
            "IControlledDryRunProcessRunner",
            "ControlledDryRunProcessRunner",
            "ProcessStartInfo",
            "System.Diagnostics.Process",
            "CreateDisposableWorkspace",
            "PrepareDisposableWorkspace",
            "WorkspaceFactory",
            "CloneRepository",
            "CreateWorktree"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void PatchArtifactCreation_DoesNotApplySourceRollbackWorkflowRelease()
    {
        foreach (var token in new[]
        {
            "ApplySourceAsync",
            "SourceApplyService",
            "ControlledSourceApply",
            "ExecuteRollback",
            "ContinueWorkflowAsync",
            "ApproveReleaseAsync",
            "ReleaseReady = true",
            "CanApplySource = true"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void PatchArtifactCreation_DoesNotAddApiCliUi()
    {
        foreach (var file in Pr191ChangedFiles())
        {
            var relative = Path.GetRelativePath(RepoRoot(), file);
            foreach (var token in new[] { "Controller", "Program.cs", "Cli", "Tauri", "UI" })
            {
                Assert.IsFalse(relative.Contains(token, StringComparison.OrdinalIgnoreCase), $"PR191 must not add {token}: {relative}");
            }
        }
    }

    [TestMethod]
    public void PatchArtifactCreation_DoesNotCallModelsAgentsToolsMemoryRetrieval()
    {
        foreach (var token in new[]
        {
            "LLM",
            "model call",
            "AgentDispatch",
            "ToolExecution",
            "PromoteMemory",
            "ActivateRetrieval",
            "Vector",
            "Embedding",
            "Weaviate"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void PatchArtifactCreation_ResultBoundaryStatesNoDownstreamAuthority()
    {
        var boundary = PatchArtifactCreationBoundaryText.Boundary;
        var warnings = string.Join("\n", PatchArtifactCreationBoundaryText.Warnings);

        foreach (var statement in new[]
        {
            "Patch artifact creation is not source apply.",
            "Patch artifact creation is not rollback.",
            "Patch artifact creation is not workflow continuation.",
            "Patch artifact creation is not release readiness.",
            "Patch artifact creation does not authorize source mutation by itself.",
            "Patch artifact creation creates a proposed change package only.",
            "Created patch artifacts must still be reviewed before source apply."
        })
        {
            StringAssert.Contains(boundary, statement);
        }

        foreach (var statement in new[]
        {
            "Patch artifact creation creates a proposed change package only.",
            "Patch artifact creation does not apply source.",
            "Patch artifact creation does not authorize source mutation.",
            "Patch artifact must still be reviewed before source apply."
        })
        {
            StringAssert.Contains(warnings, statement);
        }
    }

    [TestMethod]
    public void PatchArtifactCreation_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        foreach (var statement in new[]
        {
            "PR191 adds Patch Artifact Creation Integration.",
            "This PR creates PatchArtifact records from existing dry-run evidence and supplied file-change data.",
            "This PR may persist created PatchArtifact records through IPatchArtifactStore.",
            "This PR does not apply source.",
            "This PR does not execute rollback.",
            "This PR does not continue workflow.",
            "This PR does not approve release.",
            "This PR does not add API.",
            "This PR does not add CLI.",
            "This PR does not add UI.",
            "This PR does not run dry-runs.",
            "This PR does not create disposable workspaces.",
            "Patch artifact creation is not source apply.",
            "Patch artifact creation is not rollback.",
            "Patch artifact creation is not workflow continuation.",
            "Patch artifact creation is not release readiness.",
            "Patch artifact creation does not authorize source mutation by itself.",
            "Patch artifact creation creates a proposed change package only.",
            "Created patch artifacts must still be reviewed before source apply.",
            "Created patch artifacts must remain bound to dry-run evidence and source baseline.",
            "Failed dry-run receipts may be evidence, but failed dry-run receipts must not create patch artifacts.",
            "Patch artifact creation requires completed successful dry-run evidence.",
            "Patch artifact creation computes patch hashes; it does not accept caller-supplied patch hashes.",
            "Patch artifact creation validates with PatchArtifactValidation and PatchBaseHashValidation before storage.",
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate",
            "The next Block S target is Patch Artifact Creation API.",
            "PR192 - Patch Artifact Creation API"
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private static PatchArtifactCreator Creator(FakeReceiptStore receiptStore, FakePatchArtifactStore artifactStore) =>
        new(receiptStore, artifactStore, new FixedTimeProvider(CreatedAtUtc), () => PatchArtifactId);

    private static PatchArtifactCreationRequest ValidRequest() => new()
    {
        ProjectId = ProjectId,
        DryRunExecutionAuditId = DryRunExecutionAuditId,
        DryRunAuditHash = "sha256:dry-run-audit-main",
        DryRunReceiptHash = "sha256:dry-run-receipt-main",
        PatchArtifactKind = "UnifiedDiffPackage",
        SourceBaselineHash = "sha256:source-baseline-main",
        FileChanges = [ModifyChange("src/Foo.cs")],
        EvidenceReferences = ["policy-satisfaction:main", "controlled-dry-run:main"],
        BoundaryMaxims = ["Patch artifact creation creates a proposed change package only."],
        Boundary = PatchArtifactCreationBoundaryText.Boundary
    };

    private static ControlledDryRunExecutionAudit ValidAudit() => new()
    {
        DryRunExecutionAuditId = DryRunExecutionAuditId,
        ProjectId = ProjectId,
        ControlledDryRunRequestId = ControlledDryRunRequestId,
        PolicySatisfactionId = PolicySatisfactionId,
        PolicySatisfactionHash = "sha256:policy-satisfaction-main",
        SubjectKind = "PatchProposal",
        SubjectId = "patch-proposal-main",
        SubjectHash = "sha256:subject-main",
        WorkspaceId = "workspace-main",
        WorkspaceKind = "DisposableDryRunWorkspace",
        WorkspaceBoundaryHash = "sha256:workspace-boundary-main",
        SourceSnapshotReference = "source-snapshot:main",
        ValidationPlanId = "validation-plan-main",
        ValidationPlanHash = "sha256:validation-plan-main",
        StartedAtUtc = CreatedAtUtc.AddMinutes(-5),
        CompletedAtUtc = CreatedAtUtc.AddMinutes(-1),
        DryRunCompleted = true,
        DryRunSucceeded = true,
        ExecutionReportHash = "sha256:execution-report-main",
        AuditHash = "sha256:dry-run-audit-main",
        CommandAudits =
        [
            new ControlledDryRunCommandAudit
            {
                CommandId = "test-command",
                WorkingDirectory = "workspace",
                Executable = "dotnet",
                CommandHash = "sha256:command-main",
                ExitCode = 0,
                TimedOut = false,
                StandardOutputSummaryHash = "sha256:stdout-main",
                StandardErrorSummaryHash = "sha256:stderr-main",
                StandardOutputSummary = "tests passed",
                StandardErrorSummary = "no errors"
            }
        ],
        EvidenceReferences = ["controlled-dry-run-request:main"],
        BoundaryMaxims = ["Dry-run execution audit records evidence only."],
        Boundary = ControlledDryRunExecutionAuditBoundaryText.Boundary
    };

    private static PatchArtifactFileChange ModifyChange(string path) => new()
    {
        Path = path,
        ChangeKind = "Modify",
        BeforeContentHash = $"sha256:before-{path}",
        AfterContentHash = $"sha256:after-{path}",
        DiffHash = $"sha256:diff-{path}",
        NormalizedDiff = $"modify {path}",
        IsBinary = false
    };

    private static void AssertValid(PatchArtifact artifact)
    {
        var validation = PatchArtifactValidation.Validate(artifact);
        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues.Select(issue => issue.Code)));
    }

    private static void AssertValidBase(PatchArtifact artifact, ControlledDryRunExecutionAudit audit, PatchArtifactCreationRequest request)
    {
        var validation = PatchBaseHashValidation.Validate(new PatchBaseHashValidationContext
        {
            PatchArtifact = artifact,
            ProjectId = request.ProjectId,
            ControlledDryRunRequestId = audit.ControlledDryRunRequestId,
            DryRunExecutionAuditId = audit.DryRunExecutionAuditId,
            DryRunAuditHash = audit.AuditHash,
            DryRunReceiptHash = request.DryRunReceiptHash,
            PolicySatisfactionId = audit.PolicySatisfactionId,
            PolicySatisfactionHash = audit.PolicySatisfactionHash,
            SubjectKind = audit.SubjectKind,
            SubjectId = audit.SubjectId,
            SubjectHash = audit.SubjectHash,
            SourceSnapshotReference = audit.SourceSnapshotReference,
            SourceBaselineHash = request.SourceBaselineHash,
            WorkspaceBoundaryHash = audit.WorkspaceBoundaryHash,
            ValidationPlanId = audit.ValidationPlanId,
            ValidationPlanHash = audit.ValidationPlanHash,
            EvidenceReferences = artifact.EvidenceReferences,
            BoundaryMaxims = artifact.BoundaryMaxims
        });

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues.Select(issue => issue.Code)));
    }

    private static async Task AssertCreationFailsAsync(Func<Task> action, string expectedCode)
    {
        var exception = await AssertThrowsAsync<PatchArtifactCreationException>(action);
        Assert.IsTrue(exception.Issues.Any(issue => issue.Code == expectedCode), string.Join(", ", exception.Issues.Select(issue => issue.Code)));
    }


    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        Assert.Fail($"Expected exception of type {typeof(TException).Name}.");
        throw new UnreachableException();
    }
    private static void AssertNoProductionToken(string token)
    {
        foreach (var file in Pr191ProductionFiles())
        {
            Assert.IsFalse(File.ReadAllText(file).Contains(token, StringComparison.Ordinal), $"Unexpected production token {token} in {file}.");
        }
    }

    private static string[] Pr191ProductionFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "IronDev.Core", "Governance", "PatchArtifactCreationModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IPatchArtifactCreator.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "PatchArtifactCreator.cs")
        ];
    }

    private static string[] Pr191ChangedFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "IronDev.Core", "Governance", "PatchArtifactCreationModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IPatchArtifactCreator.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "PatchArtifactCreator.cs"),
            Path.Combine(root, "Docs", "receipts", "PR191_PATCH_ARTIFACT_CREATION_INTEGRATION.md"),
            Path.Combine(root, "IronDev.IntegrationTests", "Governance", "PatchArtifactCreationIntegrationTests.cs")
        ];
    }

    private static string CreatorSourcePath() =>
        Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "PatchArtifactCreator.cs");

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR191_PATCH_ARTIFACT_CREATION_INTEGRATION.md");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class FakeReceiptStore(ControlledDryRunExecutionAudit? audit) : IControlledDryRunReceiptStore
    {
        public int GetCount { get; private set; }

        public Task SaveAsync(ControlledDryRunExecutionAudit audit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ControlledDryRunExecutionAudit?> GetAsync(Guid projectId, Guid dryRunExecutionAuditId, CancellationToken cancellationToken = default)
        {
            GetCount++;
            return Task.FromResult(audit);
        }

        public Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByRequestAsync(Guid projectId, Guid controlledDryRunRequestId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByPolicySatisfactionAsync(Guid projectId, Guid policySatisfactionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListBySubjectAsync(Guid projectId, string subjectKind, string subjectId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ControlledDryRunExecutionAudit>> ListByAuditHashAsync(Guid projectId, string auditHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakePatchArtifactStore : IPatchArtifactStore
    {
        public List<PatchArtifact> Saved { get; } = [];
        public int SaveCount { get; private set; }
        public bool ThrowOnSave { get; init; }

        public Task SaveAsync(PatchArtifact patchArtifact, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("save failed");
            }

            Saved.Add(patchArtifact);
            return Task.CompletedTask;
        }

        public Task<PatchArtifact?> GetAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PatchArtifact?>(Saved.SingleOrDefault(artifact => artifact.ProjectId == projectId && artifact.PatchArtifactId == patchArtifactId));

        public Task<IReadOnlyList<PatchArtifact>> ListByDryRunReceiptHashAsync(Guid projectId, string dryRunReceiptHash, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PatchArtifact>>(Saved.Where(artifact => artifact.ProjectId == projectId && artifact.DryRunReceiptHash == dryRunReceiptHash).ToArray());

        public Task<IReadOnlyList<PatchArtifact>> ListByDryRunAuditHashAsync(Guid projectId, string dryRunAuditHash, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PatchArtifact>>(Saved.Where(artifact => artifact.ProjectId == projectId && artifact.DryRunAuditHash == dryRunAuditHash).ToArray());

        public Task<IReadOnlyList<PatchArtifact>> ListByControlledDryRunRequestAsync(Guid projectId, Guid controlledDryRunRequestId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PatchArtifact>>(Saved.Where(artifact => artifact.ProjectId == projectId && artifact.ControlledDryRunRequestId == controlledDryRunRequestId).ToArray());

        public Task<IReadOnlyList<PatchArtifact>> ListBySubjectAsync(Guid projectId, string subjectKind, string subjectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PatchArtifact>>(Saved.Where(artifact => artifact.ProjectId == projectId && artifact.SubjectKind == subjectKind && artifact.SubjectId == subjectId).ToArray());

        public Task<IReadOnlyList<PatchArtifact>> ListByPatchHashAsync(Guid projectId, string patchHash, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PatchArtifact>>(Saved.Where(artifact => artifact.ProjectId == projectId && artifact.PatchHash == patchHash).ToArray());

        public Task<IReadOnlyList<PatchArtifact>> ListBySourceBaselineHashAsync(Guid projectId, string sourceBaselineHash, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PatchArtifact>>(Saved.Where(artifact => artifact.ProjectId == projectId && artifact.SourceBaselineHash == sourceBaselineHash).ToArray());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}


