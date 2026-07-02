using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestCategory("Receipt")]
[TestClass]
public sealed class BlockE02CommitReceiptPersistenceHardeningTests
{
    private const string TenantId = "tenant-e02";
    private const string ProjectId = "project-e02";
    private const string OperationId = "op_000000000000e002";
    private const string CorrelationId = "corr_000000000000e002";
    private const string CommitSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string CommitTreeHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly DateTimeOffset StartedAtUtc = DateTimeOffset.Parse("2026-06-25T02:00:00Z");
    private static readonly DateTimeOffset CompletedAtUtc = DateTimeOffset.Parse("2026-06-25T02:03:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-25T02:04:00Z");
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-25T02:05:00Z");

    [TestMethod]
    public async Task ValidSucceededCommitReceiptPersists()
    {
        var store = new RecordingStore();
        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(CommitReceiptPersistenceStatus.Persisted, result.PersistenceStatus);
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(1, store.Saved.Count);
        Assert.IsTrue(store.Saved[0].RecordFingerprint.StartsWith("sha256:", StringComparison.Ordinal));
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(CommitReceiptOutcomeKind.Failed, "failed")]
    [DataRow(CommitReceiptOutcomeKind.Interrupted, "interrupted")]
    public async Task ValidNonSucceededTerminalCommitReceiptPersists(CommitReceiptOutcomeKind outcome, string reason)
    {
        var receipt = Receipt() with
        {
            CommitSha = "",
            CommitTreeHash = "",
            OutcomeKind = outcome,
            OutcomeReasonCode = reason
        };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(CommitReceiptPersistenceStatus.Persisted, result.PersistenceStatus);
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
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
        Assert.AreEqual("commit-receipt:e02", result.ReceiptId);
        Assert.AreEqual("commit-attempt:e02", result.CommitAttemptId);
        Assert.AreEqual(CommitSha, result.CommitSha);
        AssertNoRawOrSecretText(string.Join("\n", result.Issues.Concat(result.Warnings).Concat(result.ForbiddenAuthorityImplications)));
    }

    [TestMethod]
    public void PersistedReceiptResultHasNoActionAuthorityFields()
    {
        var propertyNames = typeof(PersistCommitReceiptResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Approved", propertyNames);
        Assert.DoesNotContain("Authorized", propertyNames);
        Assert.IsFalse(propertyNames.Any(static name => name.StartsWith("Can", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("PushAuthority", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("PullRequestAuthority", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("MergeReady", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task DuplicateIdenticalReceiptIsIdempotent()
    {
        var store = new RecordingStore();
        var service = Service(store);

        var first = await service.PersistAsync(Request());
        var second = await service.PersistAsync(Request());

        Assert.AreEqual(CommitReceiptPersistenceStatus.Persisted, first.PersistenceStatus);
        Assert.AreEqual(CommitReceiptPersistenceStatus.AlreadyPersisted, second.PersistenceStatus);
        Assert.AreEqual(1, store.Saved.Count);
        AssertNoAuthority(second);
    }

    [TestMethod]
    public async Task DuplicateSameReceiptIdWithChangedMetadataConflicts()
    {
        var store = new RecordingStore();
        var service = Service(store);

        await service.PersistAsync(Request());
        var result = await service.PersistAsync(Request(Receipt() with { CommitPackageHash = Hash("changed") }));

        Assert.AreEqual(CommitReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "CommitReceiptPersistenceReceiptFingerprintConflict");
        Assert.AreEqual(1, store.Saved.Count);
    }

    [TestMethod]
    public async Task DuplicateSameCommitAttemptWithChangedTerminalOutcomeConflicts()
    {
        var store = new RecordingStore();
        var service = Service(store);

        await service.PersistAsync(Request());
        var result = await service.PersistAsync(Request(Receipt() with
        {
            ReceiptId = "commit-receipt:e02b",
            CommitSha = "",
            CommitTreeHash = "",
            OutcomeKind = CommitReceiptOutcomeKind.Failed,
            OutcomeReasonCode = "failed"
        }));

        Assert.AreEqual(CommitReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "CommitReceiptPersistenceAttemptTerminalOutcomeConflict");
    }

    [TestMethod]
    public async Task DuplicateSameCommitShaWithConflictingScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with
        {
            TenantId = "tenant-other",
            ReceiptId = "commit-receipt:other",
            CommitAttemptId = "commit-attempt:other",
            RecordFingerprint = Hash("existing")
        });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(CommitReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "CommitReceiptPersistenceExistingCommitShaScopeConflict");
    }

    [DataTestMethod]
    [DataRow("tenant", "CommitReceiptPersistenceTenantIdRequired")]
    [DataRow("project", "CommitReceiptPersistenceProjectIdRequired")]
    [DataRow("operation", "CommitReceiptPersistenceOperationIdRequired")]
    [DataRow("operation-invalid", "CommitReceiptPersistenceOperationIdInvalid")]
    [DataRow("correlation", "CommitReceiptPersistenceCorrelationIdRequired")]
    [DataRow("correlation-invalid", "CommitReceiptPersistenceCorrelationIdInvalid")]
    [DataRow("receipt", "CommitReceiptPersistenceRecordRequired")]
    [DataRow("as-of", "CommitReceiptPersistenceAsOfUtcRequired")]
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

        Assert.AreEqual(CommitReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("receipt-id", "CommitReceiptPersistenceReceiptIdRequired")]
    [DataRow("attempt-id", "CommitReceiptPersistenceAttemptIdRequired")]
    [DataRow("package-id", "CommitReceiptPersistencePackageIdRequired")]
    [DataRow("package-hash", "CommitReceiptPersistencePackageHashRequired")]
    [DataRow("source-apply-receipt", "CommitReceiptPersistenceSourceApplyReceiptIdRequired")]
    [DataRow("source-apply-attempt", "CommitReceiptPersistenceSourceApplyAttemptIdRequired")]
    [DataRow("patch-artifact-id", "CommitReceiptPersistencePatchArtifactIdRequired")]
    [DataRow("patch-artifact-hash", "CommitReceiptPersistencePatchArtifactHashRequired")]
    [DataRow("outcome", "CommitReceiptPersistenceOutcomeKindRequired")]
    [DataRow("commit-sha-required", "CommitReceiptPersistenceCommitShaRequired")]
    [DataRow("commit-sha-invalid", "CommitReceiptPersistenceCommitShaInvalid")]
    [DataRow("commit-sha-unexpected", "CommitReceiptPersistenceCommitShaUnexpected")]
    [DataRow("started", "CommitReceiptPersistenceStartedAtUtcRequired")]
    [DataRow("recorded", "CommitReceiptPersistenceRecordedAtUtcRequired")]
    [DataRow("completed", "CommitReceiptPersistenceCompletedAtUtcRequired")]
    [DataRow("completed-before-started", "CommitReceiptPersistenceCompletedBeforeStarted")]
    public async Task ReceiptValidationFailsClosed(string mutation, string expectedIssue)
    {
        var receipt = mutation switch
        {
            "receipt-id" => Receipt() with { ReceiptId = "" },
            "attempt-id" => Receipt() with { CommitAttemptId = "" },
            "package-id" => Receipt() with { CommitPackageId = "" },
            "package-hash" => Receipt() with { CommitPackageHash = "" },
            "source-apply-receipt" => Receipt() with { SourceApplyReceiptId = "" },
            "source-apply-attempt" => Receipt() with { SourceApplyAttemptId = "" },
            "patch-artifact-id" => Receipt() with { PatchArtifactId = "" },
            "patch-artifact-hash" => Receipt() with { PatchArtifactHash = "" },
            "outcome" => Receipt() with { OutcomeKind = CommitReceiptOutcomeKind.Unknown },
            "commit-sha-required" => Receipt() with { CommitSha = "" },
            "commit-sha-invalid" => Receipt() with { CommitSha = "main" },
            "commit-sha-unexpected" => Receipt() with { OutcomeKind = CommitReceiptOutcomeKind.Failed },
            "started" => Receipt() with { StartedAtUtc = default },
            "recorded" => Receipt() with { RecordedAtUtc = default },
            "completed" => Receipt() with { CompletedAtUtc = null },
            "completed-before-started" => Receipt() with { CompletedAtUtc = StartedAtUtc.AddMinutes(-1) },
            _ => Receipt()
        };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(CommitReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("raw-patch")]
    [DataRow("raw-diff")]
    [DataRow("raw-source")]
    [DataRow("raw-commit-message")]
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
            "raw-commit-message" => "raw commit message",
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

        Assert.AreEqual(CommitReceiptPersistenceStatus.RejectedUnsafePayload, result.PersistenceStatus);
        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, "CommitReceiptPersistenceRedactionReasonInvalid");
        AssertNoAuthority(result);
    }

    [TestMethod]
    public async Task RedactedReceiptRequiresRedactionReason()
    {
        var result = await Service().PersistAsync(Request(Receipt() with { IsRedacted = true, RedactionReason = "" }));

        Assert.AreEqual(CommitReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, "CommitReceiptPersistenceRedactionReasonRequired");
    }

    [DataTestMethod]
    [DataRow("tenant", "CommitReceiptPersistenceTenantMismatch")]
    [DataRow("project", "CommitReceiptPersistenceProjectMismatch")]
    [DataRow("operation", "CommitReceiptPersistenceOperationMismatch")]
    [DataRow("correlation", "CommitReceiptPersistenceCorrelationMismatch")]
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

        Assert.AreEqual(CommitReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public async Task ExistingSameReceiptIdDifferentScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with { TenantId = "tenant-other", RecordFingerprint = Hash("existing") });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(CommitReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "CommitReceiptPersistenceExistingReceiptScopeConflict");
    }

    [TestMethod]
    public async Task ExistingSameAttemptDifferentScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with
        {
            ReceiptId = "commit-receipt:other",
            TenantId = "tenant-other",
            CommitAttemptId = "commit-attempt:e02",
            RecordFingerprint = Hash("existing")
        });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(CommitReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "CommitReceiptPersistenceExistingAttemptScopeConflict");
    }

    [TestMethod]
    public void FingerprintIsDeterministicAndMetadataOnly()
    {
        var receipt = Receipt();
        var first = CommitReceiptPersistenceService.ComputeRecordFingerprint(receipt);
        var second = CommitReceiptPersistenceService.ComputeRecordFingerprint(receipt with { RecordFingerprint = Hash("ignored") });
        var changed = CommitReceiptPersistenceService.ComputeRecordFingerprint(receipt with { CommitPackageHash = Hash("changed") });

        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, changed);
        Assert.IsTrue(first.StartsWith("sha256:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PersistedCommitReceiptDoesNotImplyDownstreamAuthority()
    {
        var result = CommitReceiptPersistenceValidator.ValidateRecord(Receipt());

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not commit execution");
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not source authority");
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not push authority");
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not pull request authority");
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not merge readiness");
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not release readiness");
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not deployment readiness");
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not retry authority");
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not workflow continuation");
    }

    [TestMethod]
    public void ModelDoesNotExposeForbiddenActionFields()
    {
        var source = E02CoreSource();
        foreach (var forbidden in new[]
        {
            "CanPush",
            "CanCreatePullRequest",
            "CanMarkReady",
            "CanRequestReviewers",
            "CanMerge",
            "CanRelease",
            "CanDeploy",
            "CanContinue",
            "ReadyToPush",
            "ReadyToPR",
            "ReadyToMerge"
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public void StaticScan_E02AddsNoApiUiOpenApiOrSqlSurface()
    {
        var source = E02CoreSource();
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
    public void StaticScan_E02AddsNoMutationExecutorOrProcessSurface()
    {
        var source = E02CoreSource();
        foreach (var forbidden in new[]
        {
            "ControlledCommitExecutor",
            "CommitPackageBuilder.Build",
            "IControlledSourceApplyExecutor",
            "SourceApplyDryRunExecutor",
            "ApplyAsync(",
            "git ",
            "Process.Start",
            "RunProcessAsync",
            "File.ReadAllText",
            "Directory.",
            "PushExecutor",
            "PushGateway",
            "PullRequestExecutor",
            "PullRequestGateway",
            "ReadyForReviewExecutor",
            "ReviewerRequestExecutor",
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
    public void StaticScan_E02AddsNoRawPayloadFields()
    {
        var source = E02CoreSource();
        foreach (var forbidden in new[]
        {
            "RawPatch",
            "RawDiff",
            "SourceFileContent",
            "ChangedFileList",
            "RawCommitMessage",
            "RawCommitBody",
            "AuthorIdentityPayload",
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
    public void StaticScan_E02DoesNotCommitSecretShapedFixtureStrings()
    {
        var source = E02AllSource();
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
    public void StoreContractIsPersistenceOnly()
    {
        var methods = typeof(ICommitReceiptPersistenceStore).GetMethods().Select(static method => method.Name).ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "FindByReceiptIdAsync", "FindByCommitAttemptIdAsync", "FindByCommitShaAsync", "SaveAsync" },
            methods);
    }

    [TestMethod]
    public void E01FocusedContractStillExists()
    {
        var result = SourceApplyReceiptPersistenceValidator.ValidateRecord(new SourceApplyReceiptPersistenceRecord
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            ReceiptId = "source-apply-receipt:e02",
            SourceApplyAttemptId = "source-apply-attempt:e02",
            PatchArtifactId = "patch-artifact:e02",
            PatchArtifactHash = Hash("patch-artifact"),
            PatchBaseRef = "patch-base:e02",
            ValidationResultRef = "validation-result:e02",
            AcceptedApprovalRef = "accepted-approval:e02",
            PolicySatisfactionRef = "policy-satisfaction:e02",
            DryRunRef = "dry-run:e02",
            WorktreeBeforeRef = "worktree-before:e02",
            WorktreeAfterRef = "worktree-after:e02",
            OutcomeKind = SourceApplyReceiptOutcomeKind.Succeeded,
            OutcomeReasonCode = "completed",
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            Source = "e02-test",
            IsRedacted = false,
            RedactionReason = "",
            RecordFingerprint = ""
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, "receipt persistence is not commit authority");
    }

    [TestMethod]
    public void ReceiptRecordsCommitReceiptIsWitnessNotAuthorityBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "E02_COMMIT_RECEIPT_PERSISTENCE_HARDENING.md"));

        Assert.Contains("A commit receipt is a witness. It is not push authority.", receipt);
        Assert.Contains("Commit receipt persistence records bounded reference-only commit receipt metadata as a durable witness.", receipt);
        Assert.Contains("does not create commits, approve work, satisfy policy", receipt);
    }

    private static PersistCommitReceiptRequest Request(CommitReceiptPersistenceRecord? receipt = null) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            Receipt = receipt ?? Receipt(),
            AsOfUtc = AsOfUtc
        };

    private static CommitReceiptPersistenceRecord Receipt() =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            ReceiptId = "commit-receipt:e02",
            CommitAttemptId = "commit-attempt:e02",
            CommitPackageId = "commit-package:e02",
            CommitPackageHash = Hash("commit-package"),
            SourceApplyReceiptId = "source-apply-receipt:e02",
            SourceApplyAttemptId = "source-apply-attempt:e02",
            PatchArtifactId = "patch-artifact:e02",
            PatchArtifactHash = Hash("patch-artifact"),
            PatchBaseRef = "patch-base:e02",
            ValidationResultRef = "validation-result:e02",
            AcceptedApprovalRef = "accepted-approval:e02",
            PolicySatisfactionRef = "policy-satisfaction:e02",
            DryRunRef = "dry-run:e02",
            WorktreeBeforeRef = "worktree-before:e02",
            WorktreeAfterRef = "worktree-after:e02",
            RepositoryRef = "repository:e02",
            TargetBranchRef = "branch:e02",
            BaseCommitRef = "base-commit:e02",
            ParentCommitRef = "parent-commit:e02",
            CommitSha = CommitSha,
            CommitTreeHash = CommitTreeHash,
            OutcomeKind = CommitReceiptOutcomeKind.Succeeded,
            OutcomeReasonCode = "completed",
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            Source = "e02-test",
            IsRedacted = false,
            RedactionReason = "",
            RecordFingerprint = ""
        };

    private static CommitReceiptPersistenceService Service(RecordingStore? store = null) =>
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

    private static string E02CoreSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "CommitReceiptPersistenceModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "CommitReceiptPersistenceValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ICommitReceiptPersistenceStore.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "CommitReceiptPersistenceService.cs")));
    }

    private static string E02AllSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            E02CoreSource(),
            File.ReadAllText(Path.Combine(root, "IronDev.IntegrationTests", "BlockE02CommitReceiptPersistenceHardeningTests.cs")),
            File.ReadAllText(Path.Combine(root, "Docs", "receipts", "E02_COMMIT_RECEIPT_PERSISTENCE_HARDENING.md")));
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

    private static void AssertNoAuthority(PersistCommitReceiptResult result)
    {
        AssertContains(result.Warnings, "commit receipt persistence is reference only");
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not approval");
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not policy satisfaction");
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not push authority");
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not pull request authority");
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not merge readiness");
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not workflow continuation");
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

    private sealed class RecordingStore : ICommitReceiptPersistenceStore
    {
        public List<CommitReceiptPersistenceRecord> Saved { get; } = [];

        public void Seed(CommitReceiptPersistenceRecord record)
        {
            Saved.Add(record);
        }

        public Task<CommitReceiptPersistenceRecord?> FindByReceiptIdAsync(
            string receiptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.FirstOrDefault(receipt =>
                string.Equals(receipt.ReceiptId, receiptId, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<CommitReceiptPersistenceRecord>> FindByCommitAttemptIdAsync(
            string commitAttemptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CommitReceiptPersistenceRecord>>(Saved
                .Where(receipt => string.Equals(receipt.CommitAttemptId, commitAttemptId, StringComparison.OrdinalIgnoreCase))
                .ToArray());

        public Task<IReadOnlyList<CommitReceiptPersistenceRecord>> FindByCommitShaAsync(
            string commitSha,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CommitReceiptPersistenceRecord>>(Saved
                .Where(receipt => string.Equals(receipt.CommitSha, commitSha, StringComparison.OrdinalIgnoreCase))
                .ToArray());

        public Task SaveAsync(
            CommitReceiptPersistenceRecord record,
            CancellationToken cancellationToken = default)
        {
            Saved.Add(record);
            return Task.CompletedTask;
        }
    }
}
