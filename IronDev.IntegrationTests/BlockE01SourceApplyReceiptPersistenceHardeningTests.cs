using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE01SourceApplyReceiptPersistenceHardeningTests
{
    private const string TenantId = "tenant-e01";
    private const string ProjectId = "project-e01";
    private const string OperationId = "op_000000000000e001";
    private const string CorrelationId = "corr_000000000000e001";
    private static readonly DateTimeOffset StartedAtUtc = DateTimeOffset.Parse("2026-06-25T01:00:00Z");
    private static readonly DateTimeOffset CompletedAtUtc = DateTimeOffset.Parse("2026-06-25T01:03:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-25T01:04:00Z");
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-25T01:05:00Z");

    [TestMethod]
    public async Task ValidSourceApplyReceiptPersists()
    {
        var store = new RecordingStore();
        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(SourceApplyReceiptPersistenceStatus.Persisted, result.PersistenceStatus);
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(1, store.Saved.Count);
        Assert.IsTrue(store.Saved[0].RecordFingerprint.StartsWith("sha256:", StringComparison.Ordinal));
        AssertNoAuthority(result);
    }

    [TestMethod]
    public async Task PersistedReceiptResultContainsSafeIdsOnly()
    {
        var result = await Service().PersistAsync(Request());

        Assert.AreEqual(TenantId, result.TenantId);
        Assert.AreEqual(ProjectId, result.ProjectId);
        Assert.AreEqual(OperationId, result.OperationId);
        Assert.AreEqual(CorrelationId, result.CorrelationId);
        Assert.AreEqual("source-apply-receipt:e01", result.ReceiptId);
        Assert.AreEqual("source-apply-attempt:e01", result.SourceApplyAttemptId);
        AssertNoRawOrSecretText(string.Join("\n", result.Issues.Concat(result.Warnings).Concat(result.ForbiddenAuthorityImplications)));
    }

    [TestMethod]
    public void PersistedReceiptResultHasNoActionAuthorityFields()
    {
        var propertyNames = typeof(PersistSourceApplyReceiptResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Approved", propertyNames);
        Assert.DoesNotContain("Authorized", propertyNames);
        Assert.IsFalse(propertyNames.Any(static name => name.StartsWith("Can", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("Commit", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("Push", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("PullRequest", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task DuplicateIdenticalReceiptIsIdempotent()
    {
        var store = new RecordingStore();
        var service = Service(store);

        var first = await service.PersistAsync(Request());
        var second = await service.PersistAsync(Request());

        Assert.AreEqual(SourceApplyReceiptPersistenceStatus.Persisted, first.PersistenceStatus);
        Assert.AreEqual(SourceApplyReceiptPersistenceStatus.AlreadyPersisted, second.PersistenceStatus);
        Assert.AreEqual(1, store.Saved.Count);
        AssertNoAuthority(second);
    }

    [TestMethod]
    public async Task DuplicateSameReceiptIdWithChangedMetadataConflicts()
    {
        var store = new RecordingStore();
        var service = Service(store);

        await service.PersistAsync(Request());
        var result = await service.PersistAsync(Request(Receipt() with { PatchArtifactHash = Hash("changed") }));

        Assert.AreEqual(SourceApplyReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "SourceApplyReceiptPersistenceReceiptFingerprintConflict");
        Assert.AreEqual(1, store.Saved.Count);
    }

    [TestMethod]
    public async Task DuplicateSameSourceApplyAttemptWithChangedTerminalOutcomeConflicts()
    {
        var store = new RecordingStore();
        var service = Service(store);

        await service.PersistAsync(Request());
        var result = await service.PersistAsync(Request(Receipt() with
        {
            ReceiptId = "source-apply-receipt:e01b",
            OutcomeKind = SourceApplyReceiptOutcomeKind.Failed,
            OutcomeReasonCode = "failed"
        }));

        Assert.AreEqual(SourceApplyReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "SourceApplyReceiptPersistenceAttemptTerminalOutcomeConflict");
    }

    [DataTestMethod]
    [DataRow("tenant", "SourceApplyReceiptPersistenceTenantIdRequired")]
    [DataRow("project", "SourceApplyReceiptPersistenceProjectIdRequired")]
    [DataRow("operation", "SourceApplyReceiptPersistenceOperationIdRequired")]
    [DataRow("operation-invalid", "SourceApplyReceiptPersistenceOperationIdInvalid")]
    [DataRow("correlation", "SourceApplyReceiptPersistenceCorrelationIdRequired")]
    [DataRow("correlation-invalid", "SourceApplyReceiptPersistenceCorrelationIdInvalid")]
    [DataRow("receipt", "SourceApplyReceiptPersistenceRecordRequired")]
    [DataRow("as-of", "SourceApplyReceiptPersistenceAsOfUtcRequired")]
    public async Task RequestValidationFailsClosed(string mutation, string expectedIssue)
    {
        var request = mutation switch
        {
            "tenant" => Request() with { TenantId = "" },
            "project" => Request() with { ProjectId = "" },
            "operation" => Request() with { OperationId = "" },
            "operation-invalid" => Request() with { OperationId = "not-an-op" },
            "correlation" => Request() with { CorrelationId = "" },
            "correlation-invalid" => Request() with { CorrelationId = "not-correlation" },
            "receipt" => Request() with { Receipt = null },
            "as-of" => Request() with { AsOfUtc = default },
            _ => Request()
        };

        var result = await Service().PersistAsync(request);

        Assert.AreEqual(SourceApplyReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("receipt-id", "SourceApplyReceiptPersistenceReceiptIdRequired")]
    [DataRow("attempt-id", "SourceApplyReceiptPersistenceAttemptIdRequired")]
    [DataRow("patch-artifact-id", "SourceApplyReceiptPersistencePatchArtifactIdRequired")]
    [DataRow("patch-artifact-hash", "SourceApplyReceiptPersistencePatchArtifactHashRequired")]
    [DataRow("outcome", "SourceApplyReceiptPersistenceOutcomeKindRequired")]
    [DataRow("started", "SourceApplyReceiptPersistenceStartedAtUtcRequired")]
    [DataRow("recorded", "SourceApplyReceiptPersistenceRecordedAtUtcRequired")]
    [DataRow("completed", "SourceApplyReceiptPersistenceCompletedAtUtcRequired")]
    [DataRow("completed-before-started", "SourceApplyReceiptPersistenceCompletedBeforeStarted")]
    public async Task ReceiptValidationFailsClosed(string mutation, string expectedIssue)
    {
        var receipt = mutation switch
        {
            "receipt-id" => Receipt() with { ReceiptId = "" },
            "attempt-id" => Receipt() with { SourceApplyAttemptId = "" },
            "patch-artifact-id" => Receipt() with { PatchArtifactId = "" },
            "patch-artifact-hash" => Receipt() with { PatchArtifactHash = "" },
            "outcome" => Receipt() with { OutcomeKind = SourceApplyReceiptOutcomeKind.Unknown },
            "started" => Receipt() with { StartedAtUtc = default },
            "recorded" => Receipt() with { RecordedAtUtc = default },
            "completed" => Receipt() with { CompletedAtUtc = null },
            "completed-before-started" => Receipt() with { CompletedAtUtc = StartedAtUtc.AddMinutes(-1) },
            _ => Receipt()
        };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(SourceApplyReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("raw-patch")]
    [DataRow("raw-diff")]
    [DataRow("raw-source")]
    [DataRow("validation-log")]
    [DataRow("raw-evidence")]
    [DataRow("raw-receipt")]
    [DataRow("secret-key")]
    [DataRow("connection-string")]
    [DataRow("bearer-token")]
    [DataRow("private-key")]
    public async Task UnsafePayloadMarkersAreRejected(string marker)
    {
        var unsafeValue = marker switch
        {
            "raw-patch" => "raw patch material",
            "raw-diff" => "diff --git a/file b/file",
            "raw-source" => "source file content",
            "validation-log" => "validation log line",
            "raw-evidence" => "raw evidence payload",
            "raw-receipt" => "raw receipt payload",
            "secret-key" => SecretKeyBlock(),
            "connection-string" => SecretConnectionText(),
            "bearer-token" => BearerText(),
            "private-key" => "private key material",
            _ => marker
        };

        var result = await Service().PersistAsync(Request(Receipt() with { RedactionReason = unsafeValue, IsRedacted = true }));

        Assert.AreEqual(SourceApplyReceiptPersistenceStatus.RejectedUnsafePayload, result.PersistenceStatus);
        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "SourceApplyReceiptPersistenceRedactionReasonInvalid");
        AssertNoAuthority(result);
    }

    [TestMethod]
    public async Task RedactedReceiptRequiresRedactionReason()
    {
        var result = await Service().PersistAsync(Request(Receipt() with { IsRedacted = true, RedactionReason = "" }));

        Assert.AreEqual(SourceApplyReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, "SourceApplyReceiptPersistenceRedactionReasonRequired");
    }

    [DataTestMethod]
    [DataRow("tenant", "SourceApplyReceiptPersistenceTenantMismatch")]
    [DataRow("project", "SourceApplyReceiptPersistenceProjectMismatch")]
    [DataRow("operation", "SourceApplyReceiptPersistenceOperationMismatch")]
    [DataRow("correlation", "SourceApplyReceiptPersistenceCorrelationMismatch")]
    public async Task CrossScopeReceiptFailsClosed(string mismatch, string expectedIssue)
    {
        var receipt = mismatch switch
        {
            "tenant" => Receipt() with { TenantId = "tenant-other" },
            "project" => Receipt() with { ProjectId = "project-other" },
            "operation" => Receipt() with { OperationId = "op_000000000000e999" },
            "correlation" => Receipt() with { CorrelationId = "corr_000000000000e999" },
            _ => Receipt()
        };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(SourceApplyReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public async Task ExistingSameReceiptIdDifferentScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with { TenantId = "tenant-other", RecordFingerprint = Hash("existing") });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(SourceApplyReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "SourceApplyReceiptPersistenceExistingReceiptScopeConflict");
    }

    [TestMethod]
    public async Task ExistingSameAttemptDifferentScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with
        {
            ReceiptId = "source-apply-receipt:other",
            TenantId = "tenant-other",
            SourceApplyAttemptId = "source-apply-attempt:e01",
            RecordFingerprint = Hash("existing")
        });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(SourceApplyReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "SourceApplyReceiptPersistenceExistingAttemptScopeConflict");
    }

    [TestMethod]
    public void FingerprintIsDeterministicAndMetadataOnly()
    {
        var receipt = Receipt();
        var first = SourceApplyReceiptPersistenceService.ComputeRecordFingerprint(receipt);
        var second = SourceApplyReceiptPersistenceService.ComputeRecordFingerprint(receipt with { RecordFingerprint = Hash("ignored") });
        var changed = SourceApplyReceiptPersistenceService.ComputeRecordFingerprint(receipt with { PatchArtifactHash = Hash("changed") });

        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, changed);
        Assert.IsTrue(first.StartsWith("sha256:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PersistedReceiptDoesNotImplyDownstreamAuthority()
    {
        var result = SourceApplyReceiptPersistenceValidator.ValidateRecord(Receipt());

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, "receipt persistence is not source apply");
        AssertContains(result.ForbiddenAuthorityImplications, "receipt persistence is not commit authority");
        AssertContains(result.ForbiddenAuthorityImplications, "receipt persistence is not push authority");
        AssertContains(result.ForbiddenAuthorityImplications, "receipt persistence is not pull request authority");
        AssertContains(result.ForbiddenAuthorityImplications, "receipt persistence is not retry authority");
        AssertContains(result.ForbiddenAuthorityImplications, "receipt persistence is not workflow continuation");
    }

    [TestMethod]
    public void ModelDoesNotExposeForbiddenActionFields()
    {
        var source = E01CoreSource();
        foreach (var forbidden in new[]
        {
            "CanCommit",
            "CanPush",
            "CanCreatePullRequest",
            "CanMerge",
            "CanContinue",
            "ReadyToCommit",
            "ReadyToPush",
            "ReadyToPR",
            "ReadyToMerge"
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public void StaticScan_E01AddsNoApiUiOpenApiOrSqlSurface()
    {
        var source = E01CoreSource();
        foreach (var forbidden in new[]
        {
            "ControllerBase",
            "MapGet",
            "MapPost",
            "OpenApi",
            "MigrationBuilder",
            "SqlConnection",
            ".tsx",
            ".ts"
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [TestMethod]
    public void StaticScan_E01AddsNoMutationExecutorOrProcessSurface()
    {
        var source = E01CoreSource();
        foreach (var forbidden in new[]
        {
            "IControlledSourceApplyExecutor",
            "SourceApplyDryRunExecutor",
            "ApplyAsync(",
            "git ",
            "Process.Start",
            "RunProcessAsync",
            "File.ReadAllText",
            "Directory.",
            "CommitExecutor",
            "PushExecutor",
            "PullRequestExecutor",
            "PullRequestGateway",
            "MergeExecutor",
            "ReleaseExecutor",
            "ReleaseGateway",
            "DeployExecutor",
            "DeploymentExecutor",
            "DeploymentGateway",
            "MemoryPromotionExecutor",
            "WorkflowContinuationExecutor",
            "RollbackExecutor",
            "RecoveryExecutor",
            "RetryExecutor",
            "ResumeExecutor"
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [TestMethod]
    public void StaticScan_E01AddsNoRawPayloadFields()
    {
        var source = E01CoreSource();
        foreach (var forbidden in new[]
        {
            "RawPatch",
            "RawDiff",
            "SourceFileContent",
            "ValidationLog",
            "RawReceiptPayload",
            "RawEvidencePayload",
            "PromptText",
            "PrivateReasoning",
            "ConnectionString"
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public void StaticScan_E01DoesNotCommitSecretShapedFixtureStrings()
    {
        var source = E01AllSource();
        foreach (var forbidden in new[]
        {
            string.Concat("-----", "BEGIN"),
            string.Concat("BEGIN ", "PRI", "VATE ", "KE", "Y"),
            string.Concat("pass", "word", "="),
            string.Concat("Pass", "word", "="),
            string.Concat("Serv", "er=db;", "User", " Id=sa;", "Pass", "word"),
            string.Concat("Bear", "er abc")
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [TestMethod]
    public void ReceiptRecordsReceiptIsWitnessNotAuthorityBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "E01_SOURCE_APPLY_RECEIPT_PERSISTENCE_HARDENING.md"));

        Assert.Contains("A source-apply receipt is a witness. It is not permission.", receipt);
        Assert.Contains("Source apply receipt persistence records bounded reference-only receipt metadata as a durable witness.", receipt);
        Assert.Contains("does not perform source apply, approve work, satisfy policy", receipt);
    }

    private static PersistSourceApplyReceiptRequest Request(SourceApplyReceiptPersistenceRecord? receipt = null) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            Receipt = receipt ?? Receipt(),
            AsOfUtc = AsOfUtc
        };

    private static SourceApplyReceiptPersistenceRecord Receipt() =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            ReceiptId = "source-apply-receipt:e01",
            SourceApplyAttemptId = "source-apply-attempt:e01",
            PatchArtifactId = "patch-artifact:e01",
            PatchArtifactHash = Hash("patch-artifact"),
            PatchBaseRef = "patch-base:e01",
            ValidationResultRef = "validation-result:e01",
            AcceptedApprovalRef = "accepted-approval:e01",
            PolicySatisfactionRef = "policy-satisfaction:e01",
            DryRunRef = "dry-run:e01",
            WorktreeBeforeRef = "worktree-before:e01",
            WorktreeAfterRef = "worktree-after:e01",
            OutcomeKind = SourceApplyReceiptOutcomeKind.Succeeded,
            OutcomeReasonCode = "completed",
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            Source = "e01-test",
            IsRedacted = false,
            RedactionReason = "",
            RecordFingerprint = ""
        };

    private static SourceApplyReceiptPersistenceService Service(RecordingStore? store = null) =>
        new(store ?? new RecordingStore());

    private static string Hash(string value) =>
        $"sha256:{value.Replace(":", "-", StringComparison.Ordinal).ToLowerInvariant()}";

    private static string SecretConnectionText() =>
        string.Concat("Serv", "er=db;", "User", " Id=sa;", "Pass", "word=pw");

    private static string BearerText() =>
        string.Concat("Bear", "er ", "abc.def.ghi");

    private static string SecretKeyBlock() =>
        string.Join(
            Environment.NewLine,
            SecretKeyBoundary("BEGIN"),
            "not-a-real-key",
            SecretKeyBoundary("END"));

    private static string SecretKeyBoundary(string direction) =>
        string.Concat("-----", direction, " FAKE ", "PRI", "VATE ", "KE", "Y", "-----");

    private static string E01CoreSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "SourceApplyReceiptPersistenceModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "SourceApplyReceiptPersistenceValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ISourceApplyReceiptPersistenceStore.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "SourceApplyReceiptPersistenceService.cs")));
    }

    private static string E01AllSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            E01CoreSource(),
            File.ReadAllText(Path.Combine(root, "IronDev.IntegrationTests", "BlockE01SourceApplyReceiptPersistenceHardeningTests.cs")),
            File.ReadAllText(Path.Combine(root, "Docs", "receipts", "E01_SOURCE_APPLY_RECEIPT_PERSISTENCE_HARDENING.md")));
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "IronDev.slnx")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static void AssertNoAuthority(PersistSourceApplyReceiptResult result)
    {
        AssertContains(result.Warnings, "source apply receipt persistence is reference only");
        AssertContains(result.ForbiddenAuthorityImplications, "receipt persistence is not approval");
        AssertContains(result.ForbiddenAuthorityImplications, "receipt persistence is not policy satisfaction");
        AssertContains(result.ForbiddenAuthorityImplications, "receipt persistence is not commit authority");
        AssertContains(result.ForbiddenAuthorityImplications, "receipt persistence is not push authority");
        AssertContains(result.ForbiddenAuthorityImplications, "receipt persistence is not workflow continuation");
    }

    private static void AssertNoRawOrSecretText(string text)
    {
        Assert.DoesNotContain("raw patch", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw diff", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source file content", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private key", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection string", text, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertContains<T>(IEnumerable<T> values, T expected) =>
        Assert.IsTrue(
            values.Contains(expected),
            $"Expected '{expected}' in [{string.Join(", ", values)}].");

    private sealed class RecordingStore : ISourceApplyReceiptPersistenceStore
    {
        public List<SourceApplyReceiptPersistenceRecord> Saved { get; } = [];

        public void Seed(SourceApplyReceiptPersistenceRecord record)
        {
            Saved.Add(record);
        }

        public Task<SourceApplyReceiptPersistenceRecord?> FindByReceiptIdAsync(
            string receiptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.FirstOrDefault(receipt =>
                string.Equals(receipt.ReceiptId, receiptId, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<SourceApplyReceiptPersistenceRecord>> FindBySourceApplyAttemptIdAsync(
            string sourceApplyAttemptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SourceApplyReceiptPersistenceRecord>>(Saved
                .Where(receipt => string.Equals(receipt.SourceApplyAttemptId, sourceApplyAttemptId, StringComparison.OrdinalIgnoreCase))
                .ToArray());

        public Task SaveAsync(
            SourceApplyReceiptPersistenceRecord record,
            CancellationToken cancellationToken = default)
        {
            Saved.Add(record);
            return Task.CompletedTask;
        }
    }
}
