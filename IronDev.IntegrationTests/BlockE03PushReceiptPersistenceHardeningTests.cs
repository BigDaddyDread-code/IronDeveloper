using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE03PushReceiptPersistenceHardeningTests
{
    private const string TenantId = "tenant-e03";
    private const string ProjectId = "project-e03";
    private const string OperationId = "op_000000000000e003";
    private const string CorrelationId = "corr_000000000000e003";
    private const string CommitSha = "cccccccccccccccccccccccccccccccccccccccc";
    private const string CommitTreeHash = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private static readonly DateTimeOffset StartedAtUtc = DateTimeOffset.Parse("2026-06-25T03:00:00Z");
    private static readonly DateTimeOffset CompletedAtUtc = DateTimeOffset.Parse("2026-06-25T03:03:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-25T03:04:00Z");
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-25T03:05:00Z");

    [TestMethod]
    public async Task ValidSucceededPushReceiptPersists()
    {
        var store = new RecordingStore();
        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(PushReceiptPersistenceStatus.Persisted, result.PersistenceStatus);
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(1, store.Saved.Count);
        Assert.IsTrue(store.Saved[0].RecordFingerprint.StartsWith("sha256:", StringComparison.Ordinal));
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(PushReceiptOutcomeKind.Failed, "failed")]
    [DataRow(PushReceiptOutcomeKind.Interrupted, "interrupted")]
    public async Task ValidNonSucceededTerminalPushReceiptPersists(PushReceiptOutcomeKind outcome, string reason)
    {
        var receipt = Receipt() with
        {
            OutcomeKind = outcome,
            OutcomeReasonCode = reason,
            ObservedRemoteHeadRef = ""
        };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(PushReceiptPersistenceStatus.Persisted, result.PersistenceStatus);
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
        Assert.AreEqual("push-receipt:e03", result.ReceiptId);
        Assert.AreEqual("push-attempt:e03", result.PushAttemptId);
        Assert.AreEqual(CommitSha, result.CommitSha);
        Assert.AreEqual("branch:e03", result.TargetBranchRef);
        AssertNoRawOrSecretText(string.Join("\n", result.Issues.Concat(result.Warnings).Concat(result.ForbiddenAuthorityImplications)));
    }

    [TestMethod]
    public void PersistedReceiptResultHasNoActionAuthorityFields()
    {
        var propertyNames = typeof(PersistPushReceiptResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Approved", propertyNames);
        Assert.DoesNotContain("Authorized", propertyNames);
        Assert.IsFalse(propertyNames.Any(static name => name.StartsWith("Can", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("PullRequestAuthority", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("ReadyForReview", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("ReviewerRequest", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("MergeReady", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task DuplicateIdenticalReceiptIsIdempotent()
    {
        var store = new RecordingStore();
        var service = Service(store);

        var first = await service.PersistAsync(Request());
        var second = await service.PersistAsync(Request());

        Assert.AreEqual(PushReceiptPersistenceStatus.Persisted, first.PersistenceStatus);
        Assert.AreEqual(PushReceiptPersistenceStatus.AlreadyPersisted, second.PersistenceStatus);
        Assert.AreEqual(1, store.Saved.Count);
        AssertNoAuthority(second);
    }

    [TestMethod]
    public async Task DuplicateSameReceiptIdWithChangedMetadataConflicts()
    {
        var store = new RecordingStore();
        var service = Service(store);

        await service.PersistAsync(Request());
        var result = await service.PersistAsync(Request(Receipt() with { PushResultRef = "push-result:changed" }));

        Assert.AreEqual(PushReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "PushReceiptPersistenceReceiptFingerprintConflict");
        Assert.AreEqual(1, store.Saved.Count);
    }

    [TestMethod]
    public async Task DuplicateSamePushAttemptWithChangedTerminalOutcomeConflicts()
    {
        var store = new RecordingStore();
        var service = Service(store);

        await service.PersistAsync(Request());
        var result = await service.PersistAsync(Request(Receipt() with
        {
            ReceiptId = "push-receipt:e03b",
            OutcomeKind = PushReceiptOutcomeKind.Failed,
            OutcomeReasonCode = "failed",
            ObservedRemoteHeadRef = ""
        }));

        Assert.AreEqual(PushReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "PushReceiptPersistenceAttemptTerminalOutcomeConflict");
    }

    [TestMethod]
    public async Task DuplicateSameCommitShaAndTargetWithConflictingScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with
        {
            TenantId = "tenant-other",
            ReceiptId = "push-receipt:other",
            PushAttemptId = "push-attempt:other",
            RecordFingerprint = Hash("existing")
        });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(PushReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "PushReceiptPersistenceExistingCommitShaTargetScopeConflict");
    }

    [TestMethod]
    public async Task DuplicateSameObservedRemoteHeadWithConflictingScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with
        {
            TenantId = "tenant-other",
            ReceiptId = "push-receipt:other",
            PushAttemptId = "push-attempt:other",
            CommitSha = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            RecordFingerprint = Hash("existing")
        });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(PushReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "PushReceiptPersistenceExistingObservedRemoteHeadScopeConflict");
    }

    [DataTestMethod]
    [DataRow("tenant", "PushReceiptPersistenceTenantIdRequired")]
    [DataRow("project", "PushReceiptPersistenceProjectIdRequired")]
    [DataRow("operation", "PushReceiptPersistenceOperationIdRequired")]
    [DataRow("operation-invalid", "PushReceiptPersistenceOperationIdInvalid")]
    [DataRow("correlation", "PushReceiptPersistenceCorrelationIdRequired")]
    [DataRow("correlation-invalid", "PushReceiptPersistenceCorrelationIdInvalid")]
    [DataRow("receipt", "PushReceiptPersistenceRecordRequired")]
    [DataRow("as-of", "PushReceiptPersistenceAsOfUtcRequired")]
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

        Assert.AreEqual(PushReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("receipt-id", "PushReceiptPersistenceReceiptIdRequired")]
    [DataRow("attempt-id", "PushReceiptPersistenceAttemptIdRequired")]
    [DataRow("commit-receipt-id", "PushReceiptPersistenceCommitReceiptIdRequired")]
    [DataRow("commit-attempt-id", "PushReceiptPersistenceCommitAttemptIdRequired")]
    [DataRow("commit-sha", "PushReceiptPersistenceCommitShaRequired")]
    [DataRow("commit-sha-invalid", "PushReceiptPersistenceCommitShaInvalid")]
    [DataRow("repository", "PushReceiptPersistenceRepositoryRefRequired")]
    [DataRow("remote", "PushReceiptPersistenceRemoteRefRequired")]
    [DataRow("branch", "PushReceiptPersistenceTargetBranchRefRequired")]
    [DataRow("outcome", "PushReceiptPersistenceOutcomeKindRequired")]
    [DataRow("observed-required", "PushReceiptPersistenceObservedRemoteHeadRefRequired")]
    [DataRow("observed-unexpected", "PushReceiptPersistenceObservedRemoteHeadRefUnexpected")]
    [DataRow("started", "PushReceiptPersistenceStartedAtUtcRequired")]
    [DataRow("recorded", "PushReceiptPersistenceRecordedAtUtcRequired")]
    [DataRow("completed", "PushReceiptPersistenceCompletedAtUtcRequired")]
    [DataRow("completed-before-started", "PushReceiptPersistenceCompletedBeforeStarted")]
    [DataRow("source", "PushReceiptPersistenceSourceRequired")]
    [DataRow("source-unknown", "PushReceiptPersistenceSourceUnknown")]
    public async Task ReceiptValidationFailsClosed(string mutation, string expectedIssue)
    {
        var receipt = mutation switch
        {
            "receipt-id" => Receipt() with { ReceiptId = "" },
            "attempt-id" => Receipt() with { PushAttemptId = "" },
            "commit-receipt-id" => Receipt() with { CommitReceiptId = "" },
            "commit-attempt-id" => Receipt() with { CommitAttemptId = "" },
            "commit-sha" => Receipt() with { CommitSha = "" },
            "commit-sha-invalid" => Receipt() with { CommitSha = "main" },
            "repository" => Receipt() with { RepositoryRef = "" },
            "remote" => Receipt() with { RemoteRef = "" },
            "branch" => Receipt() with { TargetBranchRef = "" },
            "outcome" => Receipt() with { OutcomeKind = PushReceiptOutcomeKind.Unknown },
            "observed-required" => Receipt() with { ObservedRemoteHeadRef = "" },
            "observed-unexpected" => Receipt() with { OutcomeKind = PushReceiptOutcomeKind.Failed },
            "started" => Receipt() with { StartedAtUtc = default },
            "recorded" => Receipt() with { RecordedAtUtc = default },
            "completed" => Receipt() with { CompletedAtUtc = null },
            "completed-before-started" => Receipt() with { CompletedAtUtc = StartedAtUtc.AddMinutes(-1) },
            "source" => Receipt() with { Source = "" },
            "source-unknown" => Receipt() with { Source = "unknown" },
            _ => Receipt()
        };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(PushReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("raw-remote-url", "PushReceiptPersistenceRemoteRefInvalid")]
    [DataRow("raw-patch", "PushReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-diff", "PushReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-source", "PushReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-commit-message", "PushReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-push-output", "PushReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-git-output", "PushReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("validation-log", "PushReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-evidence", "PushReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-receipt", "PushReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("secret-key", "PushReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("connection-string", "PushReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("bearer-token", "PushReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("private-key", "PushReceiptPersistenceRedactionReasonInvalid")]
    public async Task UnsafePayloadMarkersAreRejected(string marker, string expectedIssue)
    {
        var receipt = marker == "raw-remote-url"
            ? Receipt() with { RemoteRef = SecretRemoteUrl() }
            : Receipt() with { IsRedacted = true, RedactionReason = UnsafeValue(marker) };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(PushReceiptPersistenceStatus.RejectedUnsafePayload, result.PersistenceStatus);
        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public async Task RedactedReceiptRequiresRedactionReason()
    {
        var result = await Service().PersistAsync(Request(Receipt() with { IsRedacted = true, RedactionReason = "" }));

        Assert.AreEqual(PushReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, "PushReceiptPersistenceRedactionReasonRequired");
    }

    [DataTestMethod]
    [DataRow("tenant", "PushReceiptPersistenceTenantMismatch")]
    [DataRow("project", "PushReceiptPersistenceProjectMismatch")]
    [DataRow("operation", "PushReceiptPersistenceOperationMismatch")]
    [DataRow("correlation", "PushReceiptPersistenceCorrelationMismatch")]
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

        Assert.AreEqual(PushReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public async Task ExistingSameReceiptIdDifferentScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with { TenantId = "tenant-other", RecordFingerprint = Hash("existing") });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(PushReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "PushReceiptPersistenceExistingReceiptScopeConflict");
    }

    [TestMethod]
    public async Task ExistingSameAttemptDifferentScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with
        {
            ReceiptId = "push-receipt:other",
            TenantId = "tenant-other",
            PushAttemptId = "push-attempt:e03",
            RecordFingerprint = Hash("existing")
        });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(PushReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "PushReceiptPersistenceExistingAttemptScopeConflict");
    }

    [TestMethod]
    public void FingerprintIsDeterministicAndMetadataOnly()
    {
        var receipt = Receipt();
        var first = PushReceiptPersistenceService.ComputeRecordFingerprint(receipt);
        var second = PushReceiptPersistenceService.ComputeRecordFingerprint(receipt with { RecordFingerprint = Hash("ignored") });
        var changed = PushReceiptPersistenceService.ComputeRecordFingerprint(receipt with { PushResultRef = "push-result:changed" });

        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, changed);
        Assert.IsTrue(first.StartsWith("sha256:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PersistedPushReceiptDoesNotImplyDownstreamAuthority()
    {
        var result = PushReceiptPersistenceValidator.ValidateRecord(Receipt());

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not push execution");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not commit execution");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not pull request authority");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not ready-for-review authority");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not reviewer-request authority");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not merge readiness");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not release readiness");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not deployment readiness");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not retry authority");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not rollback authority");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not workflow continuation");
    }

    [TestMethod]
    public void ModelDoesNotExposeForbiddenActionFields()
    {
        var source = E03CoreSource();
        foreach (var forbidden in new[]
        {
            "CanCreatePullRequest",
            "CanMarkReady",
            "CanRequestReviewers",
            "CanMerge",
            "CanRelease",
            "CanDeploy",
            "CanContinue",
            "ReadyToPR",
            "ReadyToMerge",
            "ReadyToRelease",
            "ReadyToDeploy"
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public void StaticScan_E03AddsNoApiUiOpenApiOrSqlSurface()
    {
        var source = E03CoreSource();
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
    public void StaticScan_E03AddsNoMutationExecutorOrProcessSurface()
    {
        var source = E03CoreSource();
        foreach (var forbidden in new[]
        {
            "ControlledPushExecutor",
            "IControlledPushGateway",
            "PushAsync(",
            "ControlledCommitExecutor",
            "IControlledCommitGateway",
            "IControlledSourceApplyExecutor",
            "SourceApplyDryRunExecutor",
            "ApplyAsync(",
            "git ",
            "Process.Start",
            "RunProcessAsync",
            "File.ReadAllText",
            "Directory.",
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
    public void StaticScan_E03AddsNoRawPayloadFields()
    {
        var source = E03CoreSource();
        foreach (var forbidden in new[]
        {
            "RawPatch",
            "RawDiff",
            "SourceFileContent",
            "ChangedFileList",
            "RawCommitMessage",
            "RawCommitBody",
            "RawPushOutput",
            "RawGitOutput",
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
    public void StaticScan_E03DoesNotCommitSecretShapedFixtureStrings()
    {
        var source = E03AllSource();
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
        var methods = typeof(IPushReceiptPersistenceStore).GetMethods().Select(static method => method.Name).ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "FindByReceiptIdAsync", "FindByPushAttemptIdAsync", "FindByCommitShaAsync", "FindByTargetBranchRefAsync", "SaveAsync" },
            methods);
    }

    [TestMethod]
    public void E02FocusedContractStillExists()
    {
        var result = CommitReceiptPersistenceValidator.ValidateRecord(new CommitReceiptPersistenceRecord
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            ReceiptId = "commit-receipt:e03",
            CommitAttemptId = "commit-attempt:e03",
            CommitPackageId = "commit-package:e03",
            CommitPackageHash = Hash("commit-package"),
            SourceApplyReceiptId = "source-apply-receipt:e03",
            SourceApplyAttemptId = "source-apply-attempt:e03",
            PatchArtifactId = "patch-artifact:e03",
            PatchArtifactHash = Hash("patch-artifact"),
            PatchBaseRef = "patch-base:e03",
            ValidationResultRef = "validation-result:e03",
            AcceptedApprovalRef = "accepted-approval:e03",
            PolicySatisfactionRef = "policy-satisfaction:e03",
            DryRunRef = "dry-run:e03",
            WorktreeBeforeRef = "worktree-before:e03",
            WorktreeAfterRef = "worktree-after:e03",
            RepositoryRef = "repository:e03",
            TargetBranchRef = "branch:e03",
            BaseCommitRef = "base-commit:e03",
            ParentCommitRef = "parent-commit:e03",
            CommitSha = CommitSha,
            CommitTreeHash = CommitTreeHash,
            OutcomeKind = CommitReceiptOutcomeKind.Succeeded,
            OutcomeReasonCode = "completed",
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            Source = "e03-test",
            IsRedacted = false,
            RedactionReason = "",
            RecordFingerprint = ""
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, "commit receipt persistence is not push authority");
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
            ReceiptId = "source-apply-receipt:e03",
            SourceApplyAttemptId = "source-apply-attempt:e03",
            PatchArtifactId = "patch-artifact:e03",
            PatchArtifactHash = Hash("patch-artifact"),
            PatchBaseRef = "patch-base:e03",
            ValidationResultRef = "validation-result:e03",
            AcceptedApprovalRef = "accepted-approval:e03",
            PolicySatisfactionRef = "policy-satisfaction:e03",
            DryRunRef = "dry-run:e03",
            WorktreeBeforeRef = "worktree-before:e03",
            WorktreeAfterRef = "worktree-after:e03",
            OutcomeKind = SourceApplyReceiptOutcomeKind.Succeeded,
            OutcomeReasonCode = "completed",
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            Source = "e03-test",
            IsRedacted = false,
            RedactionReason = "",
            RecordFingerprint = ""
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, "receipt persistence is not commit authority");
    }

    [TestMethod]
    public void ReceiptRecordsPushReceiptIsWitnessNotAuthorityBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "E03_PUSH_RECEIPT_PERSISTENCE_HARDENING.md"));

        Assert.Contains("A push receipt is a witness. It is not PR authority.", receipt);
        Assert.Contains("Push receipt persistence records bounded reference-only push receipt metadata as a durable witness.", receipt);
        Assert.Contains("It does not push, approve work, satisfy policy", receipt);
    }

    private static PersistPushReceiptRequest Request(PushReceiptPersistenceRecord? receipt = null) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            Receipt = receipt ?? Receipt(),
            AsOfUtc = AsOfUtc
        };

    private static PushReceiptPersistenceRecord Receipt() =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            ReceiptId = "push-receipt:e03",
            PushAttemptId = "push-attempt:e03",
            CommitReceiptId = "commit-receipt:e03",
            CommitAttemptId = "commit-attempt:e03",
            CommitSha = CommitSha,
            CommitTreeHash = CommitTreeHash,
            RepositoryRef = "repository:e03",
            RemoteRef = "remote:origin",
            TargetBranchRef = "branch:e03",
            ExpectedRemoteHeadRef = "remote-head:before",
            ObservedRemoteHeadRef = "remote-head:after",
            PushResultRef = "push-result:e03",
            SourceApplyReceiptId = "source-apply-receipt:e03",
            CommitPackageId = "commit-package:e03",
            PatchArtifactId = "patch-artifact:e03",
            PatchArtifactHash = Hash("patch-artifact"),
            ValidationResultRef = "validation-result:e03",
            AcceptedApprovalRef = "accepted-approval:e03",
            PolicySatisfactionRef = "policy-satisfaction:e03",
            OutcomeKind = PushReceiptOutcomeKind.Succeeded,
            OutcomeReasonCode = "completed",
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            Source = "e03-test",
            IsRedacted = false,
            RedactionReason = "",
            RecordFingerprint = ""
        };

    private static PushReceiptPersistenceService Service(RecordingStore? store = null) =>
        new(store ?? new RecordingStore());

    private static string Hash(string value) =>
        $"sha256:{value.Replace(":", "-", StringComparison.Ordinal).ToLowerInvariant()}";

    private static string UnsafeValue(string marker) =>
        marker switch
        {
            "raw-patch" => "raw patch material",
            "raw-diff" => "diff --git a/file b/file",
            "raw-source" => "source file content",
            "raw-commit-message" => "raw commit message",
            "raw-push-output" => "raw push output",
            "raw-git-output" => "raw git output",
            "validation-log" => "validation log line",
            "raw-evidence" => "raw evidence payload",
            "raw-receipt" => "raw receipt payload",
            "secret-key" => SecretKeyBlock(),
            "connection-string" => SecretConnectionText(),
            "bearer-token" => BearerText(),
            "private-key" => "private key material",
            _ => marker
        };

    private static string SecretRemoteUrl() =>
        string.Concat("https", "://", "user", ":", "pw", "@example.invalid/repo.git");

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

    private static string E03CoreSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "PushReceiptPersistenceModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "PushReceiptPersistenceValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "IPushReceiptPersistenceStore.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "PushReceiptPersistenceService.cs")));
    }

    private static string E03AllSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            E03CoreSource(),
            File.ReadAllText(Path.Combine(root, "IronDev.IntegrationTests", "BlockE03PushReceiptPersistenceHardeningTests.cs")),
            File.ReadAllText(Path.Combine(root, "Docs", "receipts", "E03_PUSH_RECEIPT_PERSISTENCE_HARDENING.md")));
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

    private static void AssertNoAuthority(PersistPushReceiptResult result)
    {
        AssertContains(result.Warnings, "push receipt persistence is reference only");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not approval");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not policy satisfaction");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not pull request authority");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not ready-for-review authority");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not reviewer-request authority");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not merge readiness");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not release readiness");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not deployment readiness");
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not workflow continuation");
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

    private sealed class RecordingStore : IPushReceiptPersistenceStore
    {
        public List<PushReceiptPersistenceRecord> Saved { get; } = [];

        public void Seed(PushReceiptPersistenceRecord record)
        {
            Saved.Add(record);
        }

        public Task<PushReceiptPersistenceRecord?> FindByReceiptIdAsync(
            string receiptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.FirstOrDefault(receipt =>
                string.Equals(receipt.ReceiptId, receiptId, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<PushReceiptPersistenceRecord>> FindByPushAttemptIdAsync(
            string pushAttemptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PushReceiptPersistenceRecord>>(Saved
                .Where(receipt => string.Equals(receipt.PushAttemptId, pushAttemptId, StringComparison.OrdinalIgnoreCase))
                .ToArray());

        public Task<IReadOnlyList<PushReceiptPersistenceRecord>> FindByCommitShaAsync(
            string commitSha,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PushReceiptPersistenceRecord>>(Saved
                .Where(receipt => string.Equals(receipt.CommitSha, commitSha, StringComparison.OrdinalIgnoreCase))
                .ToArray());

        public Task<IReadOnlyList<PushReceiptPersistenceRecord>> FindByTargetBranchRefAsync(
            string targetBranchRef,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PushReceiptPersistenceRecord>>(Saved
                .Where(receipt => string.Equals(receipt.TargetBranchRef, targetBranchRef, StringComparison.OrdinalIgnoreCase))
                .ToArray());

        public Task SaveAsync(
            PushReceiptPersistenceRecord record,
            CancellationToken cancellationToken = default)
        {
            Saved.Add(record);
            return Task.CompletedTask;
        }
    }
}
