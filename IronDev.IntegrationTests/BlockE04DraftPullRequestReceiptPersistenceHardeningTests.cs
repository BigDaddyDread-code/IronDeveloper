using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestCategory("Receipt")]
[TestClass]
public sealed class BlockE04DraftPullRequestReceiptPersistenceHardeningTests
{
    private const string TenantId = "tenant-e04";
    private const string ProjectId = "project-e04";
    private const string OperationId = "op_000000000000e004";
    private const string CorrelationId = "corr_000000000000e004";
    private const string CommitSha = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    private static readonly DateTimeOffset StartedAtUtc = DateTimeOffset.Parse("2026-06-25T04:00:00Z");
    private static readonly DateTimeOffset CompletedAtUtc = DateTimeOffset.Parse("2026-06-25T04:03:00Z");
    private static readonly DateTimeOffset RecordedAtUtc = DateTimeOffset.Parse("2026-06-25T04:04:00Z");
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-25T04:05:00Z");

    [TestMethod]
    public async Task ValidSucceededDraftPullRequestReceiptPersists()
    {
        var store = new RecordingStore();
        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.Persisted, result.PersistenceStatus);
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(1, store.Saved.Count);
        Assert.IsTrue(store.Saved[0].RecordFingerprint.StartsWith("sha256:", StringComparison.Ordinal));
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(DraftPullRequestReceiptOutcomeKind.Failed, "failed")]
    [DataRow(DraftPullRequestReceiptOutcomeKind.Interrupted, "interrupted")]
    public async Task ValidNonSucceededTerminalDraftPullRequestReceiptPersists(
        DraftPullRequestReceiptOutcomeKind outcome,
        string reason)
    {
        var receipt = Receipt() with
        {
            OutcomeKind = outcome,
            OutcomeReasonCode = reason,
            PullRequestRef = "",
            PullRequestNumberRef = "",
            PullRequestWebRef = "",
            ObservedDraftState = DraftPullRequestObservedState.Unavailable
        };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.Persisted, result.PersistenceStatus);
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
        Assert.AreEqual("draft-pr-receipt:e04", result.ReceiptId);
        Assert.AreEqual("draft-pr-attempt:e04", result.DraftPullRequestAttemptId);
        Assert.AreEqual("pull-request:e04", result.PullRequestRef);
        Assert.AreEqual("pull-request-number:e04", result.PullRequestNumberRef);
        Assert.AreEqual(DraftPullRequestObservedState.Draft, result.ObservedDraftState);
        AssertNoRawOrSecretText(string.Join("\n", result.Issues.Concat(result.Warnings).Concat(result.ForbiddenAuthorityImplications)));
    }

    [TestMethod]
    public void PersistedReceiptResultHasNoActionAuthorityFields()
    {
        var propertyNames = typeof(PersistDraftPullRequestReceiptResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Approved", propertyNames);
        Assert.DoesNotContain("Authorized", propertyNames);
        Assert.IsFalse(propertyNames.Any(static name => name.StartsWith("Can", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("ReadyForReview", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("ReviewerRequest", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("MergeReady", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("ReleaseReady", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task DuplicateIdenticalReceiptIsIdempotent()
    {
        var store = new RecordingStore();
        var service = Service(store);

        var first = await service.PersistAsync(Request());
        var second = await service.PersistAsync(Request());

        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.Persisted, first.PersistenceStatus);
        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.AlreadyPersisted, second.PersistenceStatus);
        Assert.AreEqual(1, store.Saved.Count);
        AssertNoAuthority(second);
    }

    [TestMethod]
    public async Task DuplicateSameReceiptIdWithChangedMetadataConflicts()
    {
        var store = new RecordingStore();
        var service = Service(store);

        await service.PersistAsync(Request());
        var result = await service.PersistAsync(Request(Receipt() with { PullRequestBodyHash = Hash("changed-body") }));

        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "DraftPullRequestReceiptPersistenceReceiptFingerprintConflict");
        Assert.AreEqual(1, store.Saved.Count);
    }

    [TestMethod]
    public async Task DuplicateSameDraftPullRequestAttemptWithChangedTerminalOutcomeConflicts()
    {
        var store = new RecordingStore();
        var service = Service(store);

        await service.PersistAsync(Request());
        var result = await service.PersistAsync(Request(Receipt() with
        {
            ReceiptId = "draft-pr-receipt:e04b",
            OutcomeKind = DraftPullRequestReceiptOutcomeKind.Failed,
            OutcomeReasonCode = "failed",
            PullRequestRef = "",
            PullRequestNumberRef = "",
            PullRequestWebRef = "",
            ObservedDraftState = DraftPullRequestObservedState.Unavailable
        }));

        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "DraftPullRequestReceiptPersistenceAttemptTerminalOutcomeConflict");
    }

    [TestMethod]
    public async Task DuplicateSamePullRequestRefWithConflictingScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with
        {
            TenantId = "tenant-other",
            ReceiptId = "draft-pr-receipt:other",
            DraftPullRequestAttemptId = "draft-pr-attempt:other",
            PullRequestNumberRef = "pull-request-number:other",
            RecordFingerprint = Hash("existing")
        });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "DraftPullRequestReceiptPersistenceExistingPullRequestRefScopeConflict");
    }

    [TestMethod]
    public async Task DuplicateSamePullRequestNumberRefWithConflictingScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with
        {
            TenantId = "tenant-other",
            ReceiptId = "draft-pr-receipt:other",
            DraftPullRequestAttemptId = "draft-pr-attempt:other",
            PullRequestRef = "pull-request:other",
            RecordFingerprint = Hash("existing")
        });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "DraftPullRequestReceiptPersistenceExistingPullRequestNumberScopeConflict");
    }

    [DataTestMethod]
    [DataRow("tenant", "DraftPullRequestReceiptPersistenceTenantIdRequired")]
    [DataRow("project", "DraftPullRequestReceiptPersistenceProjectIdRequired")]
    [DataRow("operation", "DraftPullRequestReceiptPersistenceOperationIdRequired")]
    [DataRow("operation-invalid", "DraftPullRequestReceiptPersistenceOperationIdInvalid")]
    [DataRow("correlation", "DraftPullRequestReceiptPersistenceCorrelationIdRequired")]
    [DataRow("correlation-invalid", "DraftPullRequestReceiptPersistenceCorrelationIdInvalid")]
    [DataRow("receipt", "DraftPullRequestReceiptPersistenceRecordRequired")]
    [DataRow("as-of", "DraftPullRequestReceiptPersistenceAsOfUtcRequired")]
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

        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("receipt-id", "DraftPullRequestReceiptPersistenceReceiptIdRequired")]
    [DataRow("attempt-id", "DraftPullRequestReceiptPersistenceAttemptIdRequired")]
    [DataRow("push-receipt-id", "DraftPullRequestReceiptPersistencePushReceiptIdRequired")]
    [DataRow("push-attempt-id", "DraftPullRequestReceiptPersistencePushAttemptIdRequired")]
    [DataRow("commit-receipt-id", "DraftPullRequestReceiptPersistenceCommitReceiptIdRequired")]
    [DataRow("commit-attempt-id", "DraftPullRequestReceiptPersistenceCommitAttemptIdRequired")]
    [DataRow("commit-sha", "DraftPullRequestReceiptPersistenceCommitShaRequired")]
    [DataRow("commit-sha-invalid", "DraftPullRequestReceiptPersistenceCommitShaInvalid")]
    [DataRow("repository", "DraftPullRequestReceiptPersistenceRepositoryRefRequired")]
    [DataRow("provider", "DraftPullRequestReceiptPersistenceProviderRefRequired")]
    [DataRow("base-branch", "DraftPullRequestReceiptPersistenceBaseBranchRefRequired")]
    [DataRow("head-branch", "DraftPullRequestReceiptPersistenceHeadBranchRefRequired")]
    [DataRow("outcome", "DraftPullRequestReceiptPersistenceOutcomeKindRequired")]
    [DataRow("pr-ref", "DraftPullRequestReceiptPersistencePullRequestRefRequired")]
    [DataRow("pr-number", "DraftPullRequestReceiptPersistencePullRequestNumberRefRequired")]
    [DataRow("draft-state", "DraftPullRequestReceiptPersistenceObservedDraftStateDraftRequired")]
    [DataRow("not-draft", "DraftPullRequestReceiptPersistenceObservedDraftStateNotDraft")]
    [DataRow("pr-ref-unexpected", "DraftPullRequestReceiptPersistencePullRequestRefUnexpected")]
    [DataRow("started", "DraftPullRequestReceiptPersistenceStartedAtUtcRequired")]
    [DataRow("recorded", "DraftPullRequestReceiptPersistenceRecordedAtUtcRequired")]
    [DataRow("completed", "DraftPullRequestReceiptPersistenceCompletedAtUtcRequired")]
    [DataRow("completed-before-started", "DraftPullRequestReceiptPersistenceCompletedBeforeStarted")]
    [DataRow("source", "DraftPullRequestReceiptPersistenceSourceRequired")]
    [DataRow("source-unknown", "DraftPullRequestReceiptPersistenceSourceUnknown")]
    public async Task ReceiptValidationFailsClosed(string mutation, string expectedIssue)
    {
        var receipt = mutation switch
        {
            "receipt-id" => Receipt() with { ReceiptId = "" },
            "attempt-id" => Receipt() with { DraftPullRequestAttemptId = "" },
            "push-receipt-id" => Receipt() with { PushReceiptId = "" },
            "push-attempt-id" => Receipt() with { PushAttemptId = "" },
            "commit-receipt-id" => Receipt() with { CommitReceiptId = "" },
            "commit-attempt-id" => Receipt() with { CommitAttemptId = "" },
            "commit-sha" => Receipt() with { CommitSha = "" },
            "commit-sha-invalid" => Receipt() with { CommitSha = "main" },
            "repository" => Receipt() with { RepositoryRef = "" },
            "provider" => Receipt() with { ProviderRef = "" },
            "base-branch" => Receipt() with { BaseBranchRef = "" },
            "head-branch" => Receipt() with { HeadBranchRef = "" },
            "outcome" => Receipt() with { OutcomeKind = DraftPullRequestReceiptOutcomeKind.Unknown },
            "pr-ref" => Receipt() with { PullRequestRef = "" },
            "pr-number" => Receipt() with { PullRequestNumberRef = "" },
            "draft-state" => Receipt() with { ObservedDraftState = DraftPullRequestObservedState.Unknown },
            "not-draft" => Receipt() with { ObservedDraftState = DraftPullRequestObservedState.NotDraft },
            "pr-ref-unexpected" => Receipt() with { OutcomeKind = DraftPullRequestReceiptOutcomeKind.Failed },
            "started" => Receipt() with { StartedAtUtc = default },
            "recorded" => Receipt() with { RecordedAtUtc = default },
            "completed" => Receipt() with { CompletedAtUtc = null },
            "completed-before-started" => Receipt() with { CompletedAtUtc = StartedAtUtc.AddMinutes(-1) },
            "source" => Receipt() with { Source = "" },
            "source-unknown" => Receipt() with { Source = "unknown" },
            _ => Receipt()
        };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("raw-pr-url", "DraftPullRequestReceiptPersistencePullRequestWebRefInvalid")]
    [DataRow("raw-api-request", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-api-response", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-pr-title", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-pr-body", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-patch", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-diff", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-source", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-commit-message", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-push-output", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-git-output", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-github-output", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("validation-log", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-evidence", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("raw-receipt", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("secret-key", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("connection-string", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("bearer-token", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    [DataRow("private-key", "DraftPullRequestReceiptPersistenceRedactionReasonInvalid")]
    public async Task UnsafePayloadMarkersAreRejected(string marker, string expectedIssue)
    {
        var receipt = marker == "raw-pr-url"
            ? Receipt() with { PullRequestWebRef = SecretPrUrl() }
            : Receipt() with { IsRedacted = true, RedactionReason = UnsafeValue(marker) };

        var result = await Service().PersistAsync(Request(receipt));

        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.RejectedUnsafePayload, result.PersistenceStatus);
        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public async Task RedactedReceiptRequiresRedactionReason()
    {
        var result = await Service().PersistAsync(Request(Receipt() with { IsRedacted = true, RedactionReason = "" }));

        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, "DraftPullRequestReceiptPersistenceRedactionReasonRequired");
    }

    [DataTestMethod]
    [DataRow("tenant", "DraftPullRequestReceiptPersistenceTenantMismatch")]
    [DataRow("project", "DraftPullRequestReceiptPersistenceProjectMismatch")]
    [DataRow("operation", "DraftPullRequestReceiptPersistenceOperationMismatch")]
    [DataRow("correlation", "DraftPullRequestReceiptPersistenceCorrelationMismatch")]
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

        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.InvalidRequest, result.PersistenceStatus);
        AssertContains(result.Issues, expectedIssue);
    }

    [TestMethod]
    public async Task ExistingSameReceiptIdDifferentScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with { TenantId = "tenant-other", RecordFingerprint = Hash("existing") });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "DraftPullRequestReceiptPersistenceExistingReceiptScopeConflict");
    }

    [TestMethod]
    public async Task ExistingSameAttemptDifferentScopeConflicts()
    {
        var store = new RecordingStore();
        store.Seed(Receipt() with
        {
            ReceiptId = "draft-pr-receipt:other",
            TenantId = "tenant-other",
            DraftPullRequestAttemptId = "draft-pr-attempt:e04",
            RecordFingerprint = Hash("existing")
        });

        var result = await Service(store).PersistAsync(Request());

        Assert.AreEqual(DraftPullRequestReceiptPersistenceStatus.Conflict, result.PersistenceStatus);
        AssertContains(result.Issues, "DraftPullRequestReceiptPersistenceExistingAttemptScopeConflict");
    }

    [TestMethod]
    public void FingerprintIsDeterministicAndMetadataOnly()
    {
        var receipt = Receipt();
        var first = DraftPullRequestReceiptPersistenceService.ComputeRecordFingerprint(receipt);
        var second = DraftPullRequestReceiptPersistenceService.ComputeRecordFingerprint(receipt with { RecordFingerprint = Hash("ignored") });
        var changed = DraftPullRequestReceiptPersistenceService.ComputeRecordFingerprint(receipt with { PullRequestBodyHash = Hash("changed") });

        Assert.AreEqual(first, second);
        Assert.AreNotEqual(first, changed);
        Assert.IsTrue(first.StartsWith("sha256:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void PersistedDraftPullRequestReceiptDoesNotImplyDownstreamAuthority()
    {
        var result = DraftPullRequestReceiptPersistenceValidator.ValidateRecord(Receipt());

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not pr creation");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not ready-for-review");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not reviewer request");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not merge");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not release");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not deploy");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not push execution");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not approval");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not policy satisfaction");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not retry authority");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not workflow continuation");
    }

    [TestMethod]
    public void ModelDoesNotExposeForbiddenActionFields()
    {
        var source = E04CoreSource();
        foreach (var forbidden in new[]
        {
            "CanMarkReady",
            "CanRequestReviewers",
            "CanMerge",
            "CanRelease",
            "CanDeploy",
            "CanContinue",
            "ReadyForReviewAuthority",
            "ReadyToMerge",
            "ReadyToRelease",
            "ReadyToDeploy"
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public void StaticScan_E04AddsNoApiUiOpenApiOrSqlSurface()
    {
        var source = E04CoreSource();
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
    public void StaticScan_E04AddsNoMutationExecutorClientOrProcessSurface()
    {
        var source = E04CoreSource();
        foreach (var forbidden in new[]
        {
            "ControlledDraftPullRequestExecutor",
            "IControlledDraftPullRequestGateway",
            "CreateDraft",
            "gh pr create",
            "GitHubClient",
            "Octokit",
            "ControlledPushExecutor",
            "IControlledPushGateway",
            "PushAsync(",
            "ControlledCommitExecutor",
            "IControlledCommitGateway",
            "IControlledSourceApplyExecutor",
            "ApplyAsync(",
            "git ",
            "Process.Start",
            "RunProcessAsync",
            "File.ReadAllText",
            "Directory.",
            "ReadyForReviewExecutor",
            "ReviewerRequestExecutor",
            "MergeExecutor",
            "ReleaseExecutor",
            "DeployExecutor",
            "DeploymentExecutor",
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
    public void StaticScan_E04AddsNoRawPayloadFields()
    {
        var source = E04CoreSource();
        foreach (var forbidden in new[]
        {
            "RawPullRequestTitle",
            "RawPullRequestBody",
            "RawPatch",
            "RawDiff",
            "SourceFileContent",
            "ChangedFileList",
            "RawCommitMessage",
            "RawCommitBody",
            "RawPushOutput",
            "RawGitOutput",
            "RawGitHubOutput",
            "ApiResponseBody",
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
    public void StaticScan_E04DoesNotCommitSecretShapedFixtureStrings()
    {
        var source = E04AllSource();
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
        var methods = typeof(IDraftPullRequestReceiptPersistenceStore).GetMethods().Select(static method => method.Name).ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "FindByReceiptIdAsync", "FindByDraftPullRequestAttemptIdAsync", "FindByPullRequestRefAsync", "FindByPullRequestNumberRefAsync", "SaveAsync" },
            methods);
    }

    [TestMethod]
    public void E03FocusedContractStillExists()
    {
        var result = PushReceiptPersistenceValidator.ValidateRecord(new PushReceiptPersistenceRecord
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            ReceiptId = "push-receipt:e04",
            PushAttemptId = "push-attempt:e04",
            CommitReceiptId = "commit-receipt:e04",
            CommitAttemptId = "commit-attempt:e04",
            CommitSha = CommitSha,
            CommitTreeHash = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
            RepositoryRef = "repository:e04",
            RemoteRef = "remote:origin",
            TargetBranchRef = "branch:e04",
            ExpectedRemoteHeadRef = "remote-head:before",
            ObservedRemoteHeadRef = "remote-head:after",
            PushResultRef = "push-result:e04",
            SourceApplyReceiptId = "source-apply-receipt:e04",
            CommitPackageId = "commit-package:e04",
            PatchArtifactId = "patch-artifact:e04",
            PatchArtifactHash = Hash("patch-artifact"),
            ValidationResultRef = "validation-result:e04",
            AcceptedApprovalRef = "accepted-approval:e04",
            PolicySatisfactionRef = "policy-satisfaction:e04",
            OutcomeKind = PushReceiptOutcomeKind.Succeeded,
            OutcomeReasonCode = "completed",
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            Source = "e04-test",
            IsRedacted = false,
            RedactionReason = "",
            RecordFingerprint = ""
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, "push receipt persistence is not pull request authority");
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
            ReceiptId = "commit-receipt:e04",
            CommitAttemptId = "commit-attempt:e04",
            CommitPackageId = "commit-package:e04",
            CommitPackageHash = Hash("commit-package"),
            SourceApplyReceiptId = "source-apply-receipt:e04",
            SourceApplyAttemptId = "source-apply-attempt:e04",
            PatchArtifactId = "patch-artifact:e04",
            PatchArtifactHash = Hash("patch-artifact"),
            PatchBaseRef = "patch-base:e04",
            ValidationResultRef = "validation-result:e04",
            AcceptedApprovalRef = "accepted-approval:e04",
            PolicySatisfactionRef = "policy-satisfaction:e04",
            DryRunRef = "dry-run:e04",
            WorktreeBeforeRef = "worktree-before:e04",
            WorktreeAfterRef = "worktree-after:e04",
            RepositoryRef = "repository:e04",
            TargetBranchRef = "branch:e04",
            BaseCommitRef = "base-commit:e04",
            ParentCommitRef = "parent-commit:e04",
            CommitSha = CommitSha,
            CommitTreeHash = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
            OutcomeKind = CommitReceiptOutcomeKind.Succeeded,
            OutcomeReasonCode = "completed",
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            Source = "e04-test",
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
            ReceiptId = "source-apply-receipt:e04",
            SourceApplyAttemptId = "source-apply-attempt:e04",
            PatchArtifactId = "patch-artifact:e04",
            PatchArtifactHash = Hash("patch-artifact"),
            PatchBaseRef = "patch-base:e04",
            ValidationResultRef = "validation-result:e04",
            AcceptedApprovalRef = "accepted-approval:e04",
            PolicySatisfactionRef = "policy-satisfaction:e04",
            DryRunRef = "dry-run:e04",
            WorktreeBeforeRef = "worktree-before:e04",
            WorktreeAfterRef = "worktree-after:e04",
            OutcomeKind = SourceApplyReceiptOutcomeKind.Succeeded,
            OutcomeReasonCode = "completed",
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            Source = "e04-test",
            IsRedacted = false,
            RedactionReason = "",
            RecordFingerprint = ""
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertContains(result.ForbiddenAuthorityImplications, "receipt persistence is not commit authority");
    }

    [TestMethod]
    public void ReceiptRecordsDraftPullRequestReceiptIsWitnessNotAuthorityBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Docs",
            "receipts",
            "E04_DRAFT_PR_RECEIPT_PERSISTENCE_HARDENING.md"));

        Assert.Contains("A draft PR receipt is a witness. It is not review, merge, release, or workflow authority.", receipt);
        Assert.Contains("Draft PR receipt persistence records bounded reference-only draft pull request receipt metadata as a durable witness.", receipt);
        Assert.Contains("It does not create PRs, approve work, satisfy policy", receipt);
    }

    private static PersistDraftPullRequestReceiptRequest Request(DraftPullRequestReceiptPersistenceRecord? receipt = null) =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            Receipt = receipt ?? Receipt(),
            AsOfUtc = AsOfUtc
        };

    private static DraftPullRequestReceiptPersistenceRecord Receipt() =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            ReceiptId = "draft-pr-receipt:e04",
            DraftPullRequestAttemptId = "draft-pr-attempt:e04",
            PushReceiptId = "push-receipt:e04",
            PushAttemptId = "push-attempt:e04",
            CommitReceiptId = "commit-receipt:e04",
            CommitAttemptId = "commit-attempt:e04",
            CommitSha = CommitSha,
            RepositoryRef = "repository:e04",
            ProviderRef = "provider:github",
            BaseBranchRef = "base-branch:main",
            HeadBranchRef = "head-branch:e04",
            PullRequestRef = "pull-request:e04",
            PullRequestNumberRef = "pull-request-number:e04",
            PullRequestWebRef = "pull-request-web:e04",
            PullRequestTitleHash = Hash("title"),
            PullRequestBodyHash = Hash("body"),
            ObservedDraftState = DraftPullRequestObservedState.Draft,
            OutcomeKind = DraftPullRequestReceiptOutcomeKind.Succeeded,
            OutcomeReasonCode = "completed",
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = CompletedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            Source = "e04-test",
            IsRedacted = false,
            RedactionReason = "",
            RecordFingerprint = ""
        };

    private static DraftPullRequestReceiptPersistenceService Service(RecordingStore? store = null) =>
        new(store ?? new RecordingStore());

    private static string Hash(string value) =>
        $"sha256:{value.Replace(":", "-", StringComparison.Ordinal).ToLowerInvariant()}";

    private static string UnsafeValue(string marker) =>
        marker switch
        {
            "raw-api-request" => "raw api request",
            "raw-api-response" => "raw api response",
            "raw-pr-title" => "raw pr title",
            "raw-pr-body" => "raw pr body",
            "raw-patch" => "raw patch material",
            "raw-diff" => "diff --git a/file b/file",
            "raw-source" => "source file content",
            "raw-commit-message" => "raw commit message",
            "raw-push-output" => "raw push output",
            "raw-git-output" => "raw git output",
            "raw-github-output" => "raw github output",
            "validation-log" => "validation log line",
            "raw-evidence" => "raw evidence payload",
            "raw-receipt" => "raw receipt payload",
            "secret-key" => SecretKeyBlock(),
            "connection-string" => SecretConnectionText(),
            "bearer-token" => BearerText(),
            "private-key" => "private key material",
            _ => marker
        };

    private static string SecretPrUrl() =>
        string.Concat("https", "://", "token", ":", "pw", "@example.invalid/pull/1");

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

    private static string E04CoreSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "DraftPullRequestReceiptPersistenceModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "DraftPullRequestReceiptPersistenceValidator.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "IDraftPullRequestReceiptPersistenceStore.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "DraftPullRequestReceiptPersistenceService.cs")));
    }

    private static string E04AllSource()
    {
        var root = FindRepositoryRoot();
        return string.Join(
            Environment.NewLine,
            E04CoreSource(),
            File.ReadAllText(Path.Combine(root, "IronDev.IntegrationTests", "BlockE04DraftPullRequestReceiptPersistenceHardeningTests.cs")),
            File.ReadAllText(Path.Combine(root, "Docs", "receipts", "E04_DRAFT_PR_RECEIPT_PERSISTENCE_HARDENING.md")));
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

    private static void AssertNoAuthority(PersistDraftPullRequestReceiptResult result)
    {
        AssertContains(result.Warnings, "draft pr receipt persistence is reference only");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not approval");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not policy satisfaction");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not pr creation");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not ready-for-review");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not reviewer request");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not merge");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not release");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not deploy");
        AssertContains(result.ForbiddenAuthorityImplications, "draft pr receipt persistence is not workflow continuation");
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

    private sealed class RecordingStore : IDraftPullRequestReceiptPersistenceStore
    {
        public List<DraftPullRequestReceiptPersistenceRecord> Saved { get; } = [];

        public void Seed(DraftPullRequestReceiptPersistenceRecord record)
        {
            Saved.Add(record);
        }

        public Task<DraftPullRequestReceiptPersistenceRecord?> FindByReceiptIdAsync(
            string receiptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.FirstOrDefault(receipt =>
                string.Equals(receipt.ReceiptId, receiptId, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<DraftPullRequestReceiptPersistenceRecord>> FindByDraftPullRequestAttemptIdAsync(
            string draftPullRequestAttemptId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DraftPullRequestReceiptPersistenceRecord>>(Saved
                .Where(receipt => string.Equals(receipt.DraftPullRequestAttemptId, draftPullRequestAttemptId, StringComparison.OrdinalIgnoreCase))
                .ToArray());

        public Task<IReadOnlyList<DraftPullRequestReceiptPersistenceRecord>> FindByPullRequestRefAsync(
            string pullRequestRef,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DraftPullRequestReceiptPersistenceRecord>>(Saved
                .Where(receipt => string.Equals(receipt.PullRequestRef, pullRequestRef, StringComparison.OrdinalIgnoreCase))
                .ToArray());

        public Task<IReadOnlyList<DraftPullRequestReceiptPersistenceRecord>> FindByPullRequestNumberRefAsync(
            string pullRequestNumberRef,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DraftPullRequestReceiptPersistenceRecord>>(Saved
                .Where(receipt => string.Equals(receipt.PullRequestNumberRef, pullRequestNumberRef, StringComparison.OrdinalIgnoreCase))
                .ToArray());

        public Task SaveAsync(
            DraftPullRequestReceiptPersistenceRecord record,
            CancellationToken cancellationToken = default)
        {
            Saved.Add(record);
            return Task.CompletedTask;
        }
    }
}
