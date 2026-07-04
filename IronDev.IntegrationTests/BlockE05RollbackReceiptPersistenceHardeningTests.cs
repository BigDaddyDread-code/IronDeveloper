using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestCategory("Receipt")]
[TestClass]
public sealed class BlockE05RollbackReceiptPersistenceHardeningTests
{
    private const string TenantId = "tenant-e05";
    private const string ProjectId = "project-e05";
    private const string OperationId = "op_000000000000e005";
    private const string OriginalOperationId = "op_000000000000e000";
    private const string CorrelationId = "corr_000000000000e005";
    private const string CommitSha = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    private static readonly DateTimeOffset StartedAtUtc = DateTimeOffset.Parse("2026-06-25T05:00:00Z");
    private static readonly DateTimeOffset CompletedAtUtc = DateTimeOffset.Parse("2026-06-25T05:03:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-25T05:04:00Z");
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-25T05:05:00Z");

    [DataTestMethod]
    [DataRow(RollbackTargetKind.SourceApply)]
    [DataRow(RollbackTargetKind.Commit)]
    [DataRow(RollbackTargetKind.Push)]
    [DataRow(RollbackTargetKind.DraftPullRequest)]
    public async Task ValidSucceededRollbackReceiptPersists(RollbackTargetKind targetKind)
    {
        var store = new RecordingStore();
        var result = await Service(store).PersistAsync(Request(TargetReceipt(targetKind)));

        Assert.AreEqual(RollbackReceiptPersistenceStatus.Persisted, result.PersistenceStatus);
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(1, store.Saved.Count);
        Assert.IsTrue(store.Saved[0].RecordFingerprint.StartsWith("sha256:", StringComparison.Ordinal));
        Assert.AreEqual(targetKind, result.RollbackTargetKind);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(RollbackReceiptOutcomeKind.Failed, "failed")]
    [DataRow(RollbackReceiptOutcomeKind.Interrupted, "interrupted")]
    [DataRow(RollbackReceiptOutcomeKind.Cancelled, "cancelled")]
    public async Task ValidNonSucceededTerminalRollbackReceiptPersists(
        RollbackReceiptOutcomeKind outcome,
        string reason)
    {
        var receipt = Receipt() with
        {
            OutcomeKind = outcome,
            OutcomeReasonCode = reason,
            RollbackResultRef = ""
        };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(RollbackReceiptPersistenceStatus.Persisted, result.PersistenceStatus);
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertNoAuthority(result);
    }

    [TestMethod]
    public async Task ValidStartedRollbackReceiptPersistsWithoutResultRef()
    {
        var receipt = Receipt() with
        {
            OutcomeKind = RollbackReceiptOutcomeKind.Started,
            OutcomeReasonCode = "started",
            RollbackResultRef = "",
            CompletedAtUtc = null
        };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(RollbackReceiptPersistenceStatus.Persisted, result.PersistenceStatus);
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
        Assert.AreEqual("rollback-receipt:e05", result.ReceiptId);
        Assert.AreEqual("rollback-attempt:e05", result.RollbackAttemptId);
        Assert.AreEqual(RollbackTargetKind.SourceApply, result.RollbackTargetKind);
        Assert.AreEqual("source-apply:e05", result.RollbackTargetRef);
        AssertNoRawOrSecretText(string.Join("\n", result.Issues.Concat(result.Warnings).Concat(result.ForbiddenAuthorityImplications)));
    }

    [TestMethod]
    public void PersistedReceiptResultHasNoActionAuthorityFields()
    {
        var propertyNames = typeof(PersistRollbackReceiptResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Approved", propertyNames);
        Assert.DoesNotContain("Authorized", propertyNames);
        Assert.DoesNotContain("Allowed", propertyNames);
        Assert.IsFalse(propertyNames.Any(IsCanAuthorityName));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("Retry", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("Resume", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("Recover", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("Continue", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("MergeReady", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("ReleaseReady", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("DeploymentReady", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task DuplicateIdenticalReceiptIsIdempotent()
    {
        var store = new RecordingStore();
        var service = Service(store);

        var first = await service.PersistAsync(Request());
        var second = await service.PersistAsync(Request());

        Assert.AreEqual(RollbackReceiptPersistenceStatus.Persisted, first.PersistenceStatus);
        Assert.AreEqual(RollbackReceiptPersistenceStatus.AlreadyPersisted, second.PersistenceStatus);
        Assert.AreEqual(1, store.Saved.Count);
        AssertNoAuthority(second);
    }

    [TestMethod]
    public async Task DuplicateSameReceiptIdWithChangedMetadataConflicts()
    {
        var store = new RecordingStore();
        var service = Service(store);

        await service.PersistAsync(Request());
        var result = await service.PersistAsync(Request(Receipt() with { OutcomeReasonCode = "changed" }));

        Assert.AreEqual(RollbackReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "RollbackReceiptPersistenceReceiptFingerprintConflict");
        Assert.AreEqual(1, store.Saved.Count);
    }

    [TestMethod]
    public async Task DuplicateSameRollbackAttemptWithChangedTerminalOutcomeConflicts()
    {
        var store = new RecordingStore();
        var service = Service(store);

        await service.PersistAsync(Request());
        var result = await service.PersistAsync(Request(Receipt() with
        {
            ReceiptId = "rollback-receipt:e05b",
            OutcomeKind = RollbackReceiptOutcomeKind.Failed,
            OutcomeReasonCode = "failed",
            RollbackResultRef = ""
        }));

        Assert.AreEqual(RollbackReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "RollbackReceiptPersistenceAttemptTerminalOutcomeConflict");
    }

    [TestMethod]
    public async Task DuplicateSameRollbackTargetRefWithConflictingScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with
        {
            TenantId = "tenant-other",
            ReceiptId = "rollback-receipt:other",
            RollbackAttemptId = "rollback-attempt:other",
            RollbackResultRef = "rollback-result:other",
            RecordFingerprint = Hash("existing")
        });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(RollbackReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "RollbackReceiptPersistenceExistingRollbackTargetScopeConflict");
    }

    [TestMethod]
    public async Task DuplicateSameRollbackResultRefWithConflictingScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with
        {
            TenantId = "tenant-other",
            ReceiptId = "rollback-receipt:other",
            RollbackAttemptId = "rollback-attempt:other",
            RollbackTargetRef = "source-apply:other",
            RecordFingerprint = Hash("existing")
        });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(RollbackReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "RollbackReceiptPersistenceExistingRollbackResultScopeConflict");
    }

    [DataTestMethod]
    [DataRow("tenant", "RollbackReceiptPersistenceTenantIdRequired")]
    [DataRow("project", "RollbackReceiptPersistenceProjectIdRequired")]
    [DataRow("operation", "RollbackReceiptPersistenceOperationIdRequired")]
    [DataRow("operation-invalid", "RollbackReceiptPersistenceOperationIdInvalid")]
    [DataRow("correlation", "RollbackReceiptPersistenceCorrelationIdRequired")]
    [DataRow("correlation-invalid", "RollbackReceiptPersistenceCorrelationIdInvalid")]
    [DataRow("receipt", "RollbackReceiptPersistenceRecordRequired")]
    [DataRow("as-of", "RollbackReceiptPersistenceAsOfUtcRequired")]
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

        Assert.AreEqual(RollbackReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("receipt-id", "RollbackReceiptPersistenceReceiptIdRequired")]
    [DataRow("attempt-id", "RollbackReceiptPersistenceAttemptIdRequired")]
    [DataRow("plan-ref", "RollbackReceiptPersistencePlanRefRequired")]
    [DataRow("target-kind", "RollbackReceiptPersistenceTargetKindRequired")]
    [DataRow("target-kind-invalid", "RollbackReceiptPersistenceTargetKindRequired")]
    [DataRow("target-ref", "RollbackReceiptPersistenceTargetRefRequired")]
    [DataRow("outcome", "RollbackReceiptPersistenceOutcomeKindRequired")]
    [DataRow("result-required", "RollbackReceiptPersistenceResultRefRequired")]
    [DataRow("started-result-unexpected", "RollbackReceiptPersistenceStartedResultRefUnexpected")]
    [DataRow("started", "RollbackReceiptPersistenceStartedAtUtcRequired")]
    [DataRow("recorded", "RollbackReceiptPersistenceRecordedAtUtcRequired")]
    [DataRow("completed", "RollbackReceiptPersistenceCompletedAtUtcRequired")]
    [DataRow("completed-before-started", "RollbackReceiptPersistenceCompletedBeforeStarted")]
    public async Task ReceiptValidationFailsClosed(string mutation, string expectedIssue)
    {
        var receipt = mutation switch
        {
            "receipt-id" => Receipt() with { ReceiptId = "" },
            "attempt-id" => Receipt() with { RollbackAttemptId = "" },
            "plan-ref" => Receipt() with { RollbackPlanRef = "" },
            "target-kind" => Receipt() with { RollbackTargetKind = RollbackTargetKind.Unknown },
            "target-kind-invalid" => Receipt() with { RollbackTargetKind = (RollbackTargetKind)99 },
            "target-ref" => Receipt() with { RollbackTargetRef = "" },
            "outcome" => Receipt() with { OutcomeKind = RollbackReceiptOutcomeKind.Unknown },
            "result-required" => Receipt() with { RollbackResultRef = "" },
            "started-result-unexpected" => Receipt() with { OutcomeKind = RollbackReceiptOutcomeKind.Started, CompletedAtUtc = null },
            "started" => Receipt() with { StartedAtUtc = default },
            "recorded" => Receipt() with { RecordedAtUtc = default },
            "completed" => Receipt() with { CompletedAtUtc = null },
            "completed-before-started" => Receipt() with { CompletedAtUtc = StartedAtUtc.AddMinutes(-1) },
            _ => Receipt()
        };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(RollbackReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(RollbackTargetKind.SourceApply, "missing", "RollbackReceiptPersistenceSourceApplyReceiptIdRequired")]
    [DataRow(RollbackTargetKind.Commit, "missing", "RollbackReceiptPersistenceCommitReceiptIdRequired")]
    [DataRow(RollbackTargetKind.Commit, "hash", "RollbackReceiptPersistenceCommitShaRequired")]
    [DataRow(RollbackTargetKind.Commit, "bad-hash", "RollbackReceiptPersistenceCommitShaInvalid")]
    [DataRow(RollbackTargetKind.Push, "missing", "RollbackReceiptPersistencePushReceiptIdRequired")]
    [DataRow(RollbackTargetKind.Push, "hash", "RollbackReceiptPersistenceCommitShaRequired")]
    [DataRow(RollbackTargetKind.Push, "bad-hash", "RollbackReceiptPersistenceCommitShaInvalid")]
    [DataRow(RollbackTargetKind.DraftPullRequest, "missing", "RollbackReceiptPersistenceDraftPullRequestReceiptIdRequired")]
    [DataRow(RollbackTargetKind.DraftPullRequest, "pr", "RollbackReceiptPersistencePullRequestRefRequired")]
    [DataRow(RollbackTargetKind.OperationState, "missing", "RollbackReceiptPersistenceOriginalOperationIdRequired")]
    public async Task TargetSpecificReceiptValidationFailsClosed(
        RollbackTargetKind targetKind,
        string mutation,
        string expectedIssue)
    {
        var receipt = TargetReceipt(targetKind);
        receipt = (targetKind, mutation) switch
        {
            (RollbackTargetKind.SourceApply, "missing") => receipt with { SourceApplyReceiptId = "" },
            (RollbackTargetKind.Commit, "missing") => receipt with { CommitReceiptId = "" },
            (RollbackTargetKind.Commit, "hash") => receipt with { CommitSha = "" },
            (RollbackTargetKind.Commit, "bad-hash") => receipt with { CommitSha = "not-a-sha" },
            (RollbackTargetKind.Push, "missing") => receipt with { PushReceiptId = "" },
            (RollbackTargetKind.Push, "hash") => receipt with { CommitSha = "" },
            (RollbackTargetKind.Push, "bad-hash") => receipt with { CommitSha = "not-a-sha" },
            (RollbackTargetKind.DraftPullRequest, "missing") => receipt with { DraftPullRequestReceiptId = "" },
            (RollbackTargetKind.DraftPullRequest, "pr") => receipt with { PullRequestRef = "" },
            (RollbackTargetKind.OperationState, "missing") => receipt with { OriginalOperationId = "" },
            _ => receipt
        };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(RollbackReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("record-tenant", "RollbackReceiptPersistenceRecordTenantIdRequired")]
    [DataRow("record-project", "RollbackReceiptPersistenceRecordProjectIdRequired")]
    [DataRow("record-operation", "RollbackReceiptPersistenceRecordOperationIdRequired")]
    [DataRow("record-correlation", "RollbackReceiptPersistenceRecordCorrelationIdRequired")]
    [DataRow("tenant-mismatch", "RollbackReceiptPersistenceTenantMismatch")]
    [DataRow("project-mismatch", "RollbackReceiptPersistenceProjectMismatch")]
    [DataRow("operation-mismatch", "RollbackReceiptPersistenceOperationMismatch")]
    [DataRow("correlation-mismatch", "RollbackReceiptPersistenceCorrelationMismatch")]
    public async Task CrossScopeReceiptValidationFailsClosed(string mutation, string expectedIssue)
    {
        var receipt = mutation switch
        {
            "record-tenant" => Receipt() with { TenantId = "" },
            "record-project" => Receipt() with { ProjectId = "" },
            "record-operation" => Receipt() with { OperationId = "" },
            "record-correlation" => Receipt() with { CorrelationId = "" },
            "tenant-mismatch" => Receipt() with { TenantId = "tenant-other" },
            "project-mismatch" => Receipt() with { ProjectId = "project-other" },
            "operation-mismatch" => Receipt() with { OperationId = "op_000000000000e099" },
            "correlation-mismatch" => Receipt() with { CorrelationId = "corr_000000000000e099" },
            _ => Receipt()
        };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(RollbackReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("source", "RollbackReceiptPersistenceSourceInvalid")]
    [DataRow("source-unknown", "RollbackReceiptPersistenceSourceUnknown")]
    [DataRow("receipt-id", "RollbackReceiptPersistenceReceiptIdInvalid")]
    [DataRow("attempt-id", "RollbackReceiptPersistenceAttemptIdInvalid")]
    [DataRow("plan-ref", "RollbackReceiptPersistencePlanRefInvalid")]
    [DataRow("result-ref", "RollbackReceiptPersistenceResultRefInvalid")]
    [DataRow("target-ref", "RollbackReceiptPersistenceTargetRefInvalid")]
    [DataRow("source-apply-ref", "RollbackReceiptPersistenceSourceApplyReceiptIdInvalid")]
    [DataRow("commit-ref", "RollbackReceiptPersistenceCommitReceiptIdInvalid")]
    [DataRow("push-ref", "RollbackReceiptPersistencePushReceiptIdInvalid")]
    [DataRow("draft-pr-ref", "RollbackReceiptPersistenceDraftPullRequestReceiptIdInvalid")]
    [DataRow("pr-ref", "RollbackReceiptPersistencePullRequestRefInvalid")]
    [DataRow("branch-ref", "RollbackReceiptPersistenceTargetBranchRefInvalid")]
    [DataRow("redaction", "RollbackReceiptPersistenceRedactionReasonInvalid")]
    public async Task UnsafeReferenceTextFailsClosed(string mutation, string expectedIssue)
    {
        var receipt = mutation switch
        {
            "source" => Receipt() with { Source = "raw rollback output" },
            "source-unknown" => Receipt() with { Source = "unknown" },
            "receipt-id" => Receipt() with { ReceiptId = "https://example.test/receipt" },
            "attempt-id" => Receipt() with { RollbackAttemptId = "rollback attempt prose" },
            "plan-ref" => Receipt() with { RollbackPlanRef = "../plan" },
            "result-ref" => Receipt() with { RollbackResultRef = "rollback-result:with space" },
            "target-ref" => Receipt() with { RollbackTargetRef = "source-apply@host" },
            "source-apply-ref" => Receipt() with { SourceApplyReceiptId = "source/apply" },
            "commit-ref" => TargetReceipt(RollbackTargetKind.Commit) with { CommitReceiptId = "commit\\receipt" },
            "push-ref" => TargetReceipt(RollbackTargetKind.Push) with { PushReceiptId = "push://receipt" },
            "draft-pr-ref" => TargetReceipt(RollbackTargetKind.DraftPullRequest) with { DraftPullRequestReceiptId = "draft@receipt" },
            "pr-ref" => TargetReceipt(RollbackTargetKind.DraftPullRequest) with { PullRequestRef = "pr ref prose" },
            "branch-ref" => Receipt() with { TargetBranchRef = "branch/feature" },
            "redaction" => Receipt() with { IsRedacted = true, RedactionReason = "private reasoning" },
            _ => Receipt()
        };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.IsTrue(
            result.PersistenceStatus is RollbackReceiptPersistenceStatus.InvalidRequest or RollbackReceiptPersistenceStatus.RejectedUnsafePayload,
            result.PersistenceStatus.ToString());
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("raw-plan")]
    [DataRow("raw-command")]
    [DataRow("raw-rollback-output")]
    [DataRow("raw-recovery-output")]
    [DataRow("inverse-patch")]
    [DataRow("raw-patch")]
    [DataRow("raw-diff")]
    [DataRow("raw-source")]
    [DataRow("raw-commit-message")]
    [DataRow("raw-push-output")]
    [DataRow("raw-git-output")]
    [DataRow("raw-github-output")]
    [DataRow("raw-pr-text")]
    [DataRow("validation-log")]
    [DataRow("raw-evidence")]
    [DataRow("raw-receipt")]
    [DataRow("pem")]
    [DataRow("password-connection")]
    [DataRow("bearer")]
    [DataRow("private-key")]
    public async Task UnsafePayloadMarkersAreRejected(string marker)
    {
        var unsafeText = marker switch
        {
            "raw-plan" => "raw rollback plan",
            "raw-command" => "raw rollback command",
            "raw-rollback-output" => "raw rollback output",
            "raw-recovery-output" => "raw recovery output",
            "inverse-patch" => "raw inverse patch",
            "raw-patch" => "raw patch",
            "raw-diff" => "diff --git",
            "raw-source" => "source file content",
            "raw-commit-message" => "commit message:",
            "raw-push-output" => "raw push output",
            "raw-git-output" => string.Concat("raw ", "gi", "t output"),
            "raw-github-output" => string.Concat("raw ", "git", "hub output"),
            "raw-pr-text" => "pull request body:",
            "validation-log" => "validation log",
            "raw-evidence" => "raw evidence payload",
            "raw-receipt" => "raw receipt payload",
            "pem" => FakePemMarker(),
            "password-connection" => FakePasswordConnectionString(),
            "bearer" => FakeBearerToken(),
            "private-key" => FakePrivateKeyMarker(),
            _ => marker
        };

        var result = await Service().PersistAsync(Request(Receipt() with { RollbackPlanRef = unsafeText }));

        Assert.AreEqual(RollbackReceiptPersistenceStatus.RejectedUnsafePayload, result.PersistenceStatus);
        Assert.IsTrue(result.Issues.Any(static issue => issue.EndsWith("Invalid", StringComparison.Ordinal)), string.Join(", ", result.Issues));
        AssertNoAuthority(result);
    }

    [TestMethod]
    public async Task RedactedReceiptRequiresRedactionReason()
    {
        var result = await Service().PersistAsync(Request(Receipt() with
        {
            IsRedacted = true,
            RedactionReason = ""
        }));

        Assert.AreEqual(RollbackReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, "RollbackReceiptPersistenceRedactionReasonRequired");
    }

    [DataTestMethod]
    [DataRow("rollback authority")]
    [DataRow("retry authority")]
    [DataRow("recovery authority")]
    [DataRow("resume authority")]
    [DataRow("workflow continuation")]
    [DataRow("source safety proof")]
    [DataRow("merge readiness")]
    [DataRow("release readiness")]
    [DataRow("deployment readiness")]
    [DataRow("policy satisfaction")]
    [DataRow("approval")]
    public async Task PersistedRollbackReceiptDoesNotImplyDownstreamAuthority(string boundary)
    {
        var result = await Service().PersistAsync(Request());
        var boundaryText = string.Join("\n", result.ForbiddenAuthorityImplications);

        StringAssert.Contains(boundaryText, boundary);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("Retry")]
    [DataRow("Resume")]
    [DataRow("Recover")]
    [DataRow("Continue")]
    [DataRow("Merge")]
    [DataRow("Release")]
    [DataRow("Deploy")]
    [DataRow("Approve")]
    [DataRow("Policy")]
    public void DataContractsDoNotAddAuthorityVocabulary(string forbidden)
    {
        var names = typeof(RollbackReceiptPersistenceRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .ToArray();

        Assert.IsFalse(names.Any(name => name.Contains(forbidden, StringComparison.Ordinal)), forbidden);
        Assert.IsFalse(names.Any(IsCanAuthorityName), string.Join(", ", names));
    }

    [TestMethod]
    public void StaticScan_E05CoreAddsNoApiUiOpenApiOrMutationSurface()
    {
        var source = E05CoreSource();

        AssertNoStaticToken(source, "Controller");
        AssertNoStaticToken(source, "MapGet");
        AssertNoStaticToken(source, "MapPost");
        AssertNoStaticToken(source, "OpenApi");
        AssertNoStaticToken(source, "Tauri");
        AssertNoStaticToken(source, "Frontend");
        AssertNoStaticToken(source, "ProcessStartInfo");
        AssertNoStaticToken(source, "HttpClient");
        AssertNoStaticToken(source, "File.ReadAllText");
        AssertNoStaticToken(source, "File.Write");
        AssertNoStaticToken(source, "RunProcessAsync");
        AssertNoStaticToken(source, "ExecuteRollback");
        AssertNoStaticToken(source, "ControlledRollbackExecutor");
        AssertNoStaticToken(source, "RollbackPlanner");
        AssertNoStaticToken(source, "RecoveryExecutor");
        AssertNoStaticToken(source, "RetryExecutor");
        AssertNoStaticToken(source, "ResumeExecutor");
        AssertNoStaticToken(source, "WorkflowContinuation");
        AssertNoStaticToken(source, "GitHubClient");
        AssertNoStaticToken(source, "GitClient");
        AssertNoStaticToken(source, "MergeExecutor");
        AssertNoStaticToken(source, "ReleaseExecutor");
        AssertNoStaticToken(source, "DeploymentExecutor");
        AssertNoStaticToken(source, "MemoryPromotion");
    }

    [TestMethod]
    public void StaticScan_E05CoreDoesNotPersistRawPayloadOrActionFields()
    {
        var source = E05CoreSource();

        AssertNoStaticToken(source, "RawRollbackPlan {");
        AssertNoStaticToken(source, "RawRollbackCommand {");
        AssertNoStaticToken(source, "RawRollbackOutput {");
        AssertNoStaticToken(source, "RawRecoveryOutput {");
        AssertNoStaticToken(source, "RawPatch {");
        AssertNoStaticToken(source, "RawDiff {");
        AssertNoStaticToken(source, "SourceContent {");
        AssertNoStaticToken(source, "PrTitle {");
        AssertNoStaticToken(source, "PrBody {");
        AssertNoStaticToken(source, "CommandText {");
        AssertNoStaticToken(source, "Action {");
        AssertNoStaticToken(source, "Executor {");
        AssertNoStaticToken(source, "CanRetry");
        AssertNoStaticToken(source, "CanRecover");
        AssertNoStaticToken(source, "CanResume");
        AssertNoStaticToken(source, "CanContinue");
        AssertNoStaticToken(source, "CanRollback");
        AssertNoStaticToken(source, "CanMerge");
        AssertNoStaticToken(source, "CanRelease");
        AssertNoStaticToken(source, "CanDeploy");
    }

    [TestMethod]
    public void StaticScan_E05FilesDoNotCommitContiguousSecretShapedFixtures()
    {
        var source = E05AllTouchedSource();

        AssertNoStaticToken(source, string.Concat("-----", "BEGIN"));
        AssertNoStaticToken(source, string.Concat("Pass", "word="));
        AssertNoStaticToken(source, string.Concat("Bear", "er "));
        AssertNoStaticToken(source, string.Concat("PRI", "VATE ", "KE", "Y-----"));
        AssertNoStaticToken(source, string.Concat("Server=db;", "User Id", "=sa;"));
    }

    [TestMethod]
    public void StoreContractRemainsPersistenceOnly()
    {
        var methodNames = typeof(IRollbackReceiptPersistenceStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(static method => method.Name)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "FindByReceiptIdAsync",
                "FindByRollbackAttemptIdAsync",
                "FindByRollbackTargetRefAsync",
                "FindByRollbackResultRefAsync",
                "SaveAsync"
            },
            methodNames);
    }

    [TestMethod]
    public void ReceiptDocumentsRollbackReceiptWitnessBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "E05_ROLLBACK_RECEIPT_PERSISTENCE_HARDENING.md"));

        StringAssert.Contains(receipt, "Rollback receipt persistence records bounded reference-only rollback receipt metadata as a durable witness.");
        StringAssert.Contains(receipt, "It does not execute rollback, plan rollback, approve work, satisfy policy, validate freshness, grant authority, retry, recover, resume, merge, release, deploy, promote memory, or continue workflow.");
        StringAssert.Contains(receipt, "A rollback receipt is a witness. It is not rollback authority.");
    }

    private static RollbackReceiptPersistenceRecord Receipt() =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            ReceiptId = "rollback-receipt:e05",
            RollbackAttemptId = "rollback-attempt:e05",
            RollbackPlanRef = "rollback-plan:e05",
            RollbackResultRef = "rollback-result:e05",
            RollbackTargetKind = RollbackTargetKind.SourceApply,
            RollbackTargetRef = "source-apply:e05",
            RollbackReasonCode = "operator-requested",
            OriginalOperationId = OriginalOperationId,
            OriginalAttemptId = "source-apply-attempt:e05",
            SourceApplyReceiptId = "source-apply-receipt:e05",
            CommitReceiptId = "",
            PushReceiptId = "",
            DraftPullRequestReceiptId = "",
            CommitSha = "",
            RepositoryRef = "repository:e05",
            TargetBranchRef = "branch:e05",
            PullRequestRef = "",
            PullRequestNumberRef = "",
            WorktreeBeforeRef = "worktree-before:e05",
            WorktreeAfterRef = "worktree-after:e05",
            ValidationResultRef = "validation-result:e05",
            OutcomeKind = RollbackReceiptOutcomeKind.Succeeded,
            OutcomeReasonCode = "rolled-back",
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            Source = "rollback-receipt-store",
            IsRedacted = false,
            RedactionReason = "",
            RecordFingerprint = ""
        };

    private static RollbackReceiptPersistenceRecord TargetReceipt(RollbackTargetKind targetKind)
    {
        var receipt = Receipt() with
        {
            RollbackTargetKind = targetKind,
            RollbackTargetRef = $"{targetKind.ToString().ToLowerInvariant()}:e05",
            SourceApplyReceiptId = "",
            CommitReceiptId = "",
            PushReceiptId = "",
            DraftPullRequestReceiptId = "",
            CommitSha = "",
            PullRequestRef = "",
            PullRequestNumberRef = "",
            OriginalOperationId = ""
        };

        return targetKind switch
        {
            RollbackTargetKind.SourceApply => receipt with
            {
                RollbackTargetRef = "source-apply:e05",
                SourceApplyReceiptId = "source-apply-receipt:e05"
            },
            RollbackTargetKind.Commit => receipt with
            {
                RollbackTargetRef = "commit:e05",
                CommitReceiptId = "commit-receipt:e05",
                CommitSha = CommitSha
            },
            RollbackTargetKind.Push => receipt with
            {
                RollbackTargetRef = "push:e05",
                PushReceiptId = "push-receipt:e05",
                CommitSha = CommitSha
            },
            RollbackTargetKind.DraftPullRequest => receipt with
            {
                RollbackTargetRef = "draft-pr:e05",
                DraftPullRequestReceiptId = "draft-pr-receipt:e05",
                PullRequestRef = "pull-request:e05",
                PullRequestNumberRef = "pull-request-number:e05"
            },
            RollbackTargetKind.OperationState => receipt with
            {
                RollbackTargetRef = "operation-state:e05",
                OriginalOperationId = OriginalOperationId,
                OriginalAttemptId = "operation-attempt:e05"
            },
            _ => receipt
        };
    }

    private static PersistRollbackReceiptRequest Request() => Request(Receipt());

    private static PersistRollbackReceiptRequest Request(RollbackReceiptPersistenceRecord? receipt) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            Receipt = receipt,
            AsOfUtc = AsOfUtc
        };

    private static RollbackReceiptPersistenceService Service(RecordingStore? store = null) =>
        new(store ?? new RecordingStore());

    private static void AssertNoAuthority(PersistRollbackReceiptResult result)
    {
        Assert.IsTrue(result.Warnings.Count > 0);
        Assert.IsTrue(result.ForbiddenAuthorityImplications.Count > 0);
        var text = string.Join("\n", result.ForbiddenAuthorityImplications);

        StringAssert.Contains(text, "not rollback execution");
        StringAssert.Contains(text, "not rollback planning");
        StringAssert.Contains(text, "not retry");
        StringAssert.Contains(text, "not recovery");
        StringAssert.Contains(text, "not resume");
        StringAssert.Contains(text, "not workflow continuation");
        StringAssert.Contains(text, "not merge");
        StringAssert.Contains(text, "not release");
        StringAssert.Contains(text, "not deploy");
        StringAssert.Contains(text, "not approval");
        StringAssert.Contains(text, "not policy satisfaction");
    }

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected), $"{expected} not found in {string.Join(", ", values)}");

    private static void AssertNoRawOrSecretText(string text)
    {
        Assert.IsFalse(text.Contains("raw patch", StringComparison.OrdinalIgnoreCase), text);
        Assert.IsFalse(text.Contains("diff --git", StringComparison.OrdinalIgnoreCase), text);
        Assert.IsFalse(text.Contains("source file content", StringComparison.OrdinalIgnoreCase), text);
        Assert.IsFalse(text.Contains("authorization:", StringComparison.OrdinalIgnoreCase), text);
    }

    private static void AssertNoStaticToken(string source, string token) =>
        Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"{token} was found.");

    private static bool IsCanAuthorityName(string name) =>
        name.StartsWith("Can", StringComparison.Ordinal) &&
        name.Length > 3 &&
        char.IsUpper(name[3]);

    private static string E05CoreSource()
    {
        var root = RepoRoot();
        var paths = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackReceiptPersistenceModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackReceiptPersistenceValidator.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IRollbackReceiptPersistenceStore.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackReceiptPersistenceService.cs")
        };

        return string.Join("\n", paths.Select(File.ReadAllText));
    }

    private static string E05AllTouchedSource()
    {
        var root = RepoRoot();
        var paths = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackReceiptPersistenceModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackReceiptPersistenceValidator.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IRollbackReceiptPersistenceStore.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "RollbackReceiptPersistenceService.cs"),
            Path.Combine(root, "IronDev.IntegrationTests", "BlockE05RollbackReceiptPersistenceHardeningTests.cs"),
            Path.Combine(root, "Docs", "receipts", "E05_ROLLBACK_RECEIPT_PERSISTENCE_HARDENING.md")
        };

        return string.Join("\n", paths.Select(File.ReadAllText));
    }

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "IronDev.slnx")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        Assert.Fail("Repo root not found.");
        return string.Empty;
    }

    private static string Hash(string value) => $"sha256:{value}";

    private static string FakePemMarker() =>
        string.Join(
            Environment.NewLine,
            string.Concat("-----", "BEGIN ", "FA", "KE ", "PRI", "VATE ", "KE", "Y", "-----"),
            "not-a-real-key",
            string.Concat("-----", "END ", "FA", "KE ", "PRI", "VATE ", "KE", "Y", "-----"));

    private static string FakePasswordConnectionString() =>
        string.Concat("Server=fake;", "User Id=fake;", "Pass", "word=fake-value;");

    private static string FakeBearerToken() =>
        string.Concat("Bear", "er token-value");

    private static string FakePrivateKeyMarker() =>
        string.Concat("pri", "vate ", "ke", "y material");

    private sealed class RecordingStore : IRollbackReceiptPersistenceStore
    {
        private readonly List<RollbackReceiptPersistenceRecord> _records = [];

        public IReadOnlyList<RollbackReceiptPersistenceRecord> Saved => _records;

        public void Seed(RollbackReceiptPersistenceRecord record) => _records.Add(record);

        public Task<RollbackReceiptPersistenceRecord?> FindByReceiptIdAsync(
            string receiptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_records.FirstOrDefault(record => Same(record.ReceiptId, receiptId)));

        public Task<IReadOnlyList<RollbackReceiptPersistenceRecord>> FindByRollbackAttemptIdAsync(
            string rollbackAttemptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RollbackReceiptPersistenceRecord>>(
                _records.Where(record => Same(record.RollbackAttemptId, rollbackAttemptId)).ToArray());

        public Task<IReadOnlyList<RollbackReceiptPersistenceRecord>> FindByRollbackTargetRefAsync(
            string rollbackTargetRef,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RollbackReceiptPersistenceRecord>>(
                _records.Where(record => Same(record.RollbackTargetRef, rollbackTargetRef)).ToArray());

        public Task<IReadOnlyList<RollbackReceiptPersistenceRecord>> FindByRollbackResultRefAsync(
            string rollbackResultRef,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RollbackReceiptPersistenceRecord>>(
                _records.Where(record => Same(record.RollbackResultRef, rollbackResultRef)).ToArray());

        public Task SaveAsync(
            RollbackReceiptPersistenceRecord record,
            CancellationToken cancellationToken = default)
        {
            _records.Add(record);
            return Task.CompletedTask;
        }

        private static bool Same(string? left, string? right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
