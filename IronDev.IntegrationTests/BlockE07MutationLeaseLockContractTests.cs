using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE07MutationLeaseLockContractTests
{
    private const string TenantId = "tenant-e07";
    private const string ProjectId = "project-e07";
    private const string OperationId = "op_000000000000e007";
    private const string CorrelationId = "corr_000000000000e007";
    private static readonly DateTimeOffset RequestedAtUtc = DateTimeOffset.Parse("2026-06-25T07:00:00Z");
    private static readonly DateTimeOffset ObservedAtUtc = DateTimeOffset.Parse("2026-06-25T07:01:00Z");
    private static readonly DateTimeOffset ExpiresAtUtc = DateTimeOffset.Parse("2026-06-25T07:06:00Z");
    private static readonly DateTimeOffset ReleasedAtUtc = DateTimeOffset.Parse("2026-06-25T07:02:00Z");
    private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-25T07:03:00Z");

    [TestMethod]
    public void ValidLeaseContractRequestValidates()
    {
        var result = MutationLeaseContractValidator.ValidateRequest(Request());

        Assert.AreEqual(MutationLeaseContractValidationStatus.Valid, result.ValidationStatus);
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(TenantId, result.TenantId);
        Assert.AreEqual(ProjectId, result.ProjectId);
        Assert.AreEqual(OperationId, result.OperationId);
        Assert.AreEqual(CorrelationId, result.CorrelationId);
        Assert.AreEqual(MutationLeaseSurfaceKind.SourceApply, result.MutationSurfaceKind);
        Assert.AreEqual("source-apply:e07", result.MutationTargetRef);
        Assert.AreEqual("idempotency-fingerprint:e07", result.IdempotencyKeyFingerprint);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(MutationLeaseState.Requested)]
    [DataRow(MutationLeaseState.ObservedHeld)]
    [DataRow(MutationLeaseState.ObservedDenied)]
    [DataRow(MutationLeaseState.ObservedReleased)]
    [DataRow(MutationLeaseState.ObservedExpired)]
    [DataRow(MutationLeaseState.ObservedConflicted)]
    public void ValidLeaseContractRecordValidates(MutationLeaseState state)
    {
        var result = MutationLeaseContractValidator.ValidateRecord(Record(state));

        Assert.AreEqual(MutationLeaseContractValidationStatus.Valid, result.ValidationStatus, string.Join(", ", result.Issues));
        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void ValidScopeBuildsDeterministicLeaseKeyFromSafeMetadata()
    {
        var scope = Scope();
        var validation = MutationLeaseContractValidator.ValidateScope(scope);
        var key = MutationLeaseContractValidator.CanonicalLeaseKey(scope);

        Assert.IsTrue(validation.IsValid, string.Join(", ", validation.Issues));
        StringAssert.Contains(key, TenantId);
        StringAssert.Contains(key, ProjectId);
        StringAssert.Contains(key, OperationId);
        StringAssert.Contains(key, CorrelationId);
        StringAssert.Contains(key, "SourceApply");
        StringAssert.Contains(key, "source-apply:e07");
        StringAssert.Contains(key, "idempotency-fingerprint:e07");
        Assert.IsFalse(key.Contains("idempotency-key:e07", StringComparison.OrdinalIgnoreCase), key);
        AssertNoAuthority(validation);
    }

    [DataTestMethod]
    [DataRow("tenant", "MutationLeaseContractTenantIdRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("project", "MutationLeaseContractProjectIdRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("operation", "MutationLeaseContractOperationIdRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("operation-invalid", "MutationLeaseContractOperationIdInvalid", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("correlation", "MutationLeaseContractCorrelationIdRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("correlation-invalid", "MutationLeaseContractCorrelationIdInvalid", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("surface", "MutationLeaseContractMutationSurfaceKindRequired", MutationLeaseContractValidationStatus.UnsupportedMutationKind)]
    [DataRow("surface-invalid", "MutationLeaseContractMutationSurfaceKindRequired", MutationLeaseContractValidationStatus.UnsupportedMutationKind)]
    [DataRow("target", "MutationLeaseContractMutationTargetRefRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("idempotency-key", "MutationLeaseContractIdempotencyKeyRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("idempotency-fingerprint", "MutationLeaseContractIdempotencyKeyFingerprintRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("lease-mode", "MutationLeaseContractLeaseModeRequired", MutationLeaseContractValidationStatus.UnsupportedLeaseMode)]
    [DataRow("lease-mode-invalid", "MutationLeaseContractLeaseModeRequired", MutationLeaseContractValidationStatus.UnsupportedLeaseMode)]
    [DataRow("owner", "MutationLeaseContractLeaseOwnerRefRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("duration-missing", "MutationLeaseContractRequestedLeaseDurationSecondsRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("duration-negative", "MutationLeaseContractRequestedLeaseDurationSecondsPositiveRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("duration-long", "MutationLeaseContractRequestedLeaseDurationSecondsBoundedRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("requested-at", "MutationLeaseContractRequestedAtUtcRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("requested-at-non-utc", "MutationLeaseContractRequestedAtUtcMustBeUtc", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("as-of", "MutationLeaseContractAsOfUtcRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("as-of-non-utc", "MutationLeaseContractAsOfUtcMustBeUtc", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("source", "MutationLeaseContractSourceRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("source-unknown", "MutationLeaseContractSourceUnknown", MutationLeaseContractValidationStatus.InvalidRequest)]
    public void RequestValidationFailsClosed(
        string mutation,
        string expectedIssue,
        MutationLeaseContractValidationStatus expectedStatus)
    {
        var request = mutation switch
        {
            "tenant" => Request() with { TenantId = "" },
            "project" => Request() with { ProjectId = "" },
            "operation" => Request() with { OperationId = "" },
            "operation-invalid" => Request() with { OperationId = "run_123" },
            "correlation" => Request() with { CorrelationId = "" },
            "correlation-invalid" => Request() with { CorrelationId = "not-correlation" },
            "surface" => Request() with { MutationSurfaceKind = MutationLeaseSurfaceKind.Unknown },
            "surface-invalid" => Request() with { MutationSurfaceKind = (MutationLeaseSurfaceKind)999 },
            "target" => Request() with { MutationTargetRef = "" },
            "idempotency-key" => Request() with { IdempotencyKey = "" },
            "idempotency-fingerprint" => Request() with { IdempotencyKeyFingerprint = "" },
            "lease-mode" => Request() with { LeaseMode = MutationLeaseMode.Unknown },
            "lease-mode-invalid" => Request() with { LeaseMode = (MutationLeaseMode)999 },
            "owner" => Request() with { LeaseOwnerRef = "" },
            "duration-missing" => Request() with { RequestedLeaseDurationSeconds = 0 },
            "duration-negative" => Request() with { RequestedLeaseDurationSeconds = -1 },
            "duration-long" => Request() with { RequestedLeaseDurationSeconds = MutationLeaseContractValidator.MaximumRequestedLeaseDurationSeconds + 1 },
            "requested-at" => Request() with { RequestedAtUtc = default },
            "requested-at-non-utc" => Request() with { RequestedAtUtc = DateTimeOffset.Parse("2026-06-25T07:00:00+01:00") },
            "as-of" => Request() with { AsOfUtc = default },
            "as-of-non-utc" => Request() with { AsOfUtc = DateTimeOffset.Parse("2026-06-25T07:03:00+01:00") },
            "source" => Request() with { Source = "" },
            "source-unknown" => Request() with { Source = "unknown" },
            _ => Request()
        };

        var result = MutationLeaseContractValidator.ValidateRequest(request);

        Assert.AreEqual(expectedStatus, result.ValidationStatus, string.Join(", ", result.Issues));
        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("state", "MutationLeaseContractLeaseStateRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("held-token", "MutationLeaseContractLeaseTokenRefRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("held-fence", "MutationLeaseContractFenceTokenRefRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("held-expiry", "MutationLeaseContractExpiresAtUtcRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("held-expiry-before-observed", "MutationLeaseContractExpiresAtUtcMustBeAfterObservedAtUtc", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("released-timestamp", "MutationLeaseContractReleasedAtUtcRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("released-before-observed", "MutationLeaseContractReleasedAtUtcBeforeObservedAtUtc", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("denied-reason", "MutationLeaseContractDeniedReasonCodeRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("conflict-reason", "MutationLeaseContractConflictReasonCodeRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("observed-at", "MutationLeaseContractObservedAtUtcRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    [DataRow("redaction", "MutationLeaseContractRedactionReasonRequired", MutationLeaseContractValidationStatus.InvalidRequest)]
    public void RecordStateValidationFailsClosed(
        string mutation,
        string expectedIssue,
        MutationLeaseContractValidationStatus expectedStatus)
    {
        var record = mutation switch
        {
            "state" => Record() with { LeaseState = MutationLeaseState.Unknown },
            "held-token" => Record(MutationLeaseState.ObservedHeld) with { LeaseTokenRef = "" },
            "held-fence" => Record(MutationLeaseState.ObservedHeld) with { FenceTokenRef = "" },
            "held-expiry" => Record(MutationLeaseState.ObservedHeld) with { ExpiresAtUtc = null },
            "held-expiry-before-observed" => Record(MutationLeaseState.ObservedHeld) with { ExpiresAtUtc = ObservedAtUtc.AddSeconds(-1) },
            "released-timestamp" => Record(MutationLeaseState.ObservedReleased) with { ReleasedAtUtc = null },
            "released-before-observed" => Record(MutationLeaseState.ObservedReleased) with { ReleasedAtUtc = ObservedAtUtc.AddSeconds(-1) },
            "denied-reason" => Record(MutationLeaseState.ObservedDenied) with { DeniedReasonCode = "" },
            "conflict-reason" => Record(MutationLeaseState.ObservedConflicted) with { ConflictReasonCode = "" },
            "observed-at" => Record(MutationLeaseState.ObservedHeld) with { ObservedAtUtc = null },
            "redaction" => Record(MutationLeaseState.Requested) with { IsRedacted = true, RedactionReason = "" },
            _ => Record()
        };

        var result = MutationLeaseContractValidator.ValidateRecord(record);

        Assert.AreEqual(expectedStatus, result.ValidationStatus, string.Join(", ", result.Issues));
        Assert.IsFalse(result.IsValid);
        AssertContains(result.Issues, expectedIssue);
        AssertNoAuthority(result);
    }

    [TestMethod]
    public void RequestedStateDoesNotRequireLeaseOrFenceTokens()
    {
        var result = MutationLeaseContractValidator.ValidateRecord(Record(MutationLeaseState.Requested) with
        {
            LeaseTokenRef = "",
            FenceTokenRef = "",
            LeaseSequenceRef = "",
            ObservedAtUtc = null,
            ExpiresAtUtc = null
        });

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        Assert.AreEqual(MutationLeaseContractValidationStatus.Valid, result.ValidationStatus);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("target-path")]
    [DataRow("target-url")]
    [DataRow("target-wildcard")]
    [DataRow("target-all")]
    [DataRow("idempotency-key")]
    [DataRow("idempotency-fingerprint")]
    [DataRow("owner")]
    [DataRow("lease-token")]
    [DataRow("fence-token")]
    [DataRow("sequence")]
    [DataRow("reason")]
    [DataRow("source")]
    public void UnsafeReferenceAndReasonTextFailsClosed(string mutation)
    {
        var record = mutation switch
        {
            "target-path" => Record() with { MutationTargetRef = "../target" },
            "target-url" => Record() with { MutationTargetRef = "https://user:secret@example.test/repo" },
            "target-wildcard" => Record() with { MutationTargetRef = "*" },
            "target-all" => Record() with { MutationTargetRef = "all" },
            "idempotency-key" => Record() with { IdempotencyKey = "idempotency key prose" },
            "idempotency-fingerprint" => Record() with { IdempotencyKeyFingerprint = "fingerprint://bad" },
            "owner" => Record() with { LeaseOwnerRef = "owner@host" },
            "lease-token" => Record(MutationLeaseState.ObservedHeld) with { LeaseTokenRef = "lease/token" },
            "fence-token" => Record(MutationLeaseState.ObservedHeld) with { FenceTokenRef = "fence token" },
            "sequence" => Record(MutationLeaseState.ObservedHeld) with { LeaseSequenceRef = "sequence\\one" },
            "reason" => Record(MutationLeaseState.ObservedDenied) with { DeniedReasonCode = "approved" },
            "source" => Record() with { Source = "continue workflow" },
            _ => Record()
        };

        var result = MutationLeaseContractValidator.ValidateRecord(record);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.ValidationStatus is MutationLeaseContractValidationStatus.InvalidRequest or MutationLeaseContractValidationStatus.RejectedUnsafePayload, result.ValidationStatus.ToString());
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("raw-patch")]
    [DataRow("raw-diff")]
    [DataRow("raw-source")]
    [DataRow("raw-commit-message")]
    [DataRow("raw-pr-text")]
    [DataRow("raw-git-output")]
    [DataRow("raw-github-output")]
    [DataRow("raw-rollback-output")]
    [DataRow("raw-recovery-output")]
    [DataRow("raw-evidence")]
    [DataRow("raw-receipt")]
    [DataRow("command")]
    [DataRow("pem")]
    [DataRow("password-connection")]
    [DataRow("bearer")]
    [DataRow("private-key")]
    [DataRow("approved")]
    [DataRow("authorized")]
    [DataRow("allowed")]
    [DataRow("continue")]
    [DataRow("retry")]
    [DataRow("resume")]
    public void UnsafePayloadMarkersAreRejected(string marker)
    {
        var unsafeText = marker switch
        {
            "raw-patch" => "raw patch",
            "raw-diff" => "diff --git",
            "raw-source" => "source file content",
            "raw-commit-message" => "commit message:",
            "raw-pr-text" => "pull request body:",
            "raw-git-output" => string.Concat("raw ", "gi", "t output"),
            "raw-github-output" => string.Concat("raw ", "git", "hub output"),
            "raw-rollback-output" => "raw rollback output",
            "raw-recovery-output" => "raw recovery output",
            "raw-evidence" => "raw evidence payload",
            "raw-receipt" => "raw receipt payload",
            "command" => "command text",
            "pem" => FakePemMarker(),
            "password-connection" => FakePasswordConnectionString(),
            "bearer" => FakeBearerToken(),
            "private-key" => FakePrivateKeyMarker(),
            "approved" => "approved",
            "authorized" => "authorized",
            "allowed" => "allowed",
            "continue" => "continue workflow",
            "retry" => "retry now",
            "resume" => "resume now",
            _ => marker
        };

        var result = MutationLeaseContractValidator.ValidateRequest(Request() with { MutationTargetRef = unsafeText });

        Assert.AreEqual(MutationLeaseContractValidationStatus.RejectedUnsafePayload, result.ValidationStatus, string.Join(", ", result.Issues));
        Assert.IsFalse(result.IsValid);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow("source apply authority")]
    [DataRow("commit authority")]
    [DataRow("push authority")]
    [DataRow("pr authority")]
    [DataRow("rollback authority")]
    [DataRow("retry authority")]
    [DataRow("recovery authority")]
    [DataRow("resume authority")]
    [DataRow("merge readiness")]
    [DataRow("release readiness")]
    [DataRow("deployment readiness")]
    [DataRow("workflow continuation")]
    [DataRow("idempotency key match is not permission")]
    [DataRow("fence token is not mutation authority")]
    [DataRow("non-expired lease metadata is not permission")]
    [DataRow("expired lease metadata is not permission")]
    public void LeaseHeldDoesNotImplyDownstreamAuthority(string boundary)
    {
        var result = MutationLeaseContractValidator.ValidateRecord(Record(MutationLeaseState.ObservedHeld));
        var text = string.Join("\n", result.ForbiddenAuthorityImplications);

        Assert.IsTrue(result.IsValid, string.Join(", ", result.Issues));
        StringAssert.Contains(text, boundary);
        AssertNoAuthority(result);
    }

    [DataTestMethod]
    [DataRow(typeof(MutationLeaseContractValidationResult))]
    [DataRow(typeof(MutationLeaseContractRequest))]
    [DataRow(typeof(MutationLeaseContractRecord))]
    [DataRow(typeof(MutationLeaseScope))]
    public void ContractTypesDoNotExposeCanAuthorityFields(Type type)
    {
        var names = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .ToArray();

        Assert.IsFalse(names.Any(IsCanAuthorityName), string.Join(", ", names));
        Assert.IsFalse(names.Any(static name => name.Contains("Command", StringComparison.Ordinal)), string.Join(", ", names));
        Assert.IsFalse(names.Any(static name => name.Contains("Executor", StringComparison.Ordinal)), string.Join(", ", names));
    }

    [TestMethod]
    public void ContractEnumsDoNotUseAuthorityVocabulary()
    {
        var names = Enum.GetNames<MutationLeaseContractValidationStatus>()
            .Concat(Enum.GetNames<MutationLeaseMode>())
            .Concat(Enum.GetNames<MutationLeaseState>())
            .Concat(Enum.GetNames<MutationLeaseSurfaceKind>())
            .ToArray();

        Assert.IsFalse(names.Any(static name => name.Contains("Authorized", StringComparison.Ordinal)), string.Join(", ", names));
        Assert.IsFalse(names.Any(static name => name.Contains("Approved", StringComparison.Ordinal)), string.Join(", ", names));
        Assert.IsFalse(names.Any(static name => name.Contains("Allowed", StringComparison.Ordinal)), string.Join(", ", names));
        Assert.IsFalse(names.Any(IsCanAuthorityName), string.Join(", ", names));
        Assert.IsFalse(names.Any(static name => name.Contains("Granted", StringComparison.Ordinal)), string.Join(", ", names));
    }

    [TestMethod]
    public void StaticScan_E07ProductionFilesDoNotAcquireReleaseOrRenewLocks()
    {
        var source = E07ProductionSource();

        AssertNoStaticToken(source, "SemaphoreSlim");
        AssertNoStaticToken(source, "Mutex");
        AssertNoStaticToken(source, "Monitor.Enter");
        AssertNoStaticToken(source, "ReaderWriterLock");
        AssertNoStaticToken(source, "ConcurrentDictionary");
        AssertNoStaticToken(source, "MemoryCache");
        AssertNoStaticToken(source, "lock (");
        AssertNoStaticToken(source, "Task.Delay");
        AssertNoStaticToken(source, "Thread.Sleep");
        AssertNoStaticToken(source, "DateTimeOffset.UtcNow");
        AssertNoStaticToken(source, "DateTime.UtcNow");
        AssertNoStaticToken(source, "Stopwatch.StartNew");
        AssertNoStaticToken(source, "sp_getapplock");
        AssertNoStaticToken(source, "pg_advisory_lock");
    }

    [TestMethod]
    public void StaticScan_E07ProductionFilesDoNotWireExecutorsOrMutationSurfaces()
    {
        var source = E07ProductionSource();

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
        AssertNoStaticToken(source, "ControlledSourceApplyExecutor");
        AssertNoStaticToken(source, "ControlledCommitExecutor");
        AssertNoStaticToken(source, "ControlledPushExecutor");
        AssertNoStaticToken(source, "ControlledRollbackExecutor");
        AssertNoStaticToken(source, "WorkflowContinuationExecutor");
        AssertNoStaticToken(source, "ContinueWorkflowExecutor");
        AssertNoStaticToken(source, "GitHubClient");
        AssertNoStaticToken(source, "GitClient");
        AssertNoStaticToken(source, "MergeExecutor");
        AssertNoStaticToken(source, "ReleaseExecutor");
        AssertNoStaticToken(source, "DeploymentExecutor");
        AssertNoStaticToken(source, "MemoryPromotionExecutor");
        AssertNoStaticToken(source, "PromoteMemoryExecutor");
    }

    [TestMethod]
    public void StaticScan_E07ProductionFilesDoNotPersistRawPayloadOrActionFields()
    {
        var source = E07ProductionSource();

        AssertNoStaticToken(source, "RawPatch {");
        AssertNoStaticToken(source, "RawDiff {");
        AssertNoStaticToken(source, "SourceContent {");
        AssertNoStaticToken(source, "CommandText {");
        AssertNoStaticToken(source, "Action {");
        AssertNoStaticToken(source, "Executor {");
        AssertNoStaticToken(source, "CanMutate");
        AssertNoStaticToken(source, "CanExecute");
        AssertNoStaticToken(source, "CanContinue");
    }

    [TestMethod]
    public void StaticScan_E07FilesDoNotCommitContiguousSecretShapedFixtures()
    {
        var source = E07AllTouchedSource();

        AssertNoStaticToken(source, string.Concat("-----", "BEGIN"));
        AssertNoStaticToken(source, string.Concat("Pass", "word="));
        AssertNoStaticToken(source, string.Concat("Bear", "er "));
        AssertNoStaticToken(source, string.Concat("PRI", "VATE ", "KE", "Y-----"));
        AssertNoStaticToken(source, string.Concat("Server=db;", "User Id", "=sa;"));
    }

    [TestMethod]
    public void ReceiptDocumentsLeaseIsNotAuthorityBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "E07_MUTATION_LEASE_LOCK_CONTRACT.md"));

        StringAssert.Contains(receipt, "Mutation lease/lock contract defines bounded reference-only lease metadata for future mutation executors.");
        StringAssert.Contains(receipt, "It does not acquire, release, renew, or enforce leases; does not approve work; does not satisfy policy; does not validate freshness; does not grant authority; does not execute mutation; and does not continue workflow.");
        StringAssert.Contains(receipt, "A mutation lease is a concurrency witness. It is not mutation authority.");
    }

    private static MutationLeaseScope Scope() =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            MutationSurfaceKind = MutationLeaseSurfaceKind.SourceApply,
            MutationTargetRef = "source-apply:e07",
            IdempotencyKey = "idempotency-key:e07",
            IdempotencyKeyFingerprint = "idempotency-fingerprint:e07"
        };

    private static MutationLeaseContractRequest Request() =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            MutationSurfaceKind = MutationLeaseSurfaceKind.SourceApply,
            MutationTargetRef = "source-apply:e07",
            IdempotencyKey = "idempotency-key:e07",
            IdempotencyKeyFingerprint = "idempotency-fingerprint:e07",
            LeaseMode = MutationLeaseMode.ExclusiveMutation,
            LeaseOwnerRef = "run:e07",
            RequestedLeaseDurationSeconds = 300,
            RequestedAtUtc = RequestedAtUtc,
            AsOfUtc = AsOfUtc,
            ReasonCode = "mutation-requested",
            Source = "mutation-lease-contract-test"
        };

    private static MutationLeaseContractRecord Record(
        MutationLeaseState state = MutationLeaseState.ObservedHeld)
    {
        var record = new MutationLeaseContractRecord
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            MutationSurfaceKind = MutationLeaseSurfaceKind.SourceApply,
            MutationTargetRef = "source-apply:e07",
            IdempotencyKey = "idempotency-key:e07",
            IdempotencyKeyFingerprint = "idempotency-fingerprint:e07",
            LeaseMode = MutationLeaseMode.ExclusiveMutation,
            LeaseState = state,
            LeaseOwnerRef = "run:e07",
            LeaseTokenRef = "",
            FenceTokenRef = "",
            LeaseSequenceRef = "",
            RequestedAtUtc = RequestedAtUtc,
            ObservedAtUtc = null,
            ExpiresAtUtc = null,
            ReleasedAtUtc = null,
            DeniedReasonCode = "",
            ConflictReasonCode = "",
            Source = "mutation-lease-contract-test",
            IsRedacted = false,
            RedactionReason = "",
            RecordFingerprint = "lease-record-fingerprint:e07"
        };

        return state switch
        {
            MutationLeaseState.ObservedHeld => record with
            {
                LeaseTokenRef = "lease-token:e07",
                FenceTokenRef = "fence-token:e07",
                LeaseSequenceRef = "lease-sequence:e07",
                ObservedAtUtc = ObservedAtUtc,
                ExpiresAtUtc = ExpiresAtUtc
            },
            MutationLeaseState.ObservedDenied => record with
            {
                ObservedAtUtc = ObservedAtUtc,
                DeniedReasonCode = "active-lease"
            },
            MutationLeaseState.ObservedReleased => record with
            {
                LeaseTokenRef = "lease-token:e07",
                ObservedAtUtc = ObservedAtUtc,
                ReleasedAtUtc = ReleasedAtUtc
            },
            MutationLeaseState.ObservedExpired => record with
            {
                ObservedAtUtc = ObservedAtUtc,
                ExpiresAtUtc = ExpiresAtUtc
            },
            MutationLeaseState.ObservedConflicted => record with
            {
                ObservedAtUtc = ObservedAtUtc,
                ConflictReasonCode = "scope-conflict"
            },
            _ => record
        };
    }

    private static void AssertNoAuthority(MutationLeaseContractValidationResult result)
    {
        Assert.IsTrue(result.Warnings.Count > 0);
        Assert.IsTrue(result.ForbiddenAuthorityImplications.Count > 0);
        var text = string.Join("\n", result.ForbiddenAuthorityImplications);

        StringAssert.Contains(text, "not mutation execution");
        StringAssert.Contains(text, "not lock acquisition");
        StringAssert.Contains(text, "not executor permission");
        StringAssert.Contains(text, "not approval");
        StringAssert.Contains(text, "not policy satisfaction");
        StringAssert.Contains(text, "not source apply authority");
        StringAssert.Contains(text, "not commit authority");
        StringAssert.Contains(text, "not push authority");
        StringAssert.Contains(text, "not rollback authority");
        StringAssert.Contains(text, "not workflow continuation");
    }

    private static void AssertContains(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected), $"{expected} not found in {string.Join(", ", values)}");

    private static void AssertNoStaticToken(string source, string token) =>
        Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"{token} was found.");

    private static bool IsCanAuthorityName(string name) =>
        name.StartsWith("Can", StringComparison.Ordinal) &&
        name.Length > 3 &&
        char.IsUpper(name[3]);

    private static string E07ProductionSource()
    {
        var root = RepoRoot();
        var paths = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "MutationLeaseContractModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "MutationLeaseContractValidator.cs")
        };

        return string.Join("\n", paths.Select(File.ReadAllText));
    }

    private static string E07AllTouchedSource()
    {
        var root = RepoRoot();
        var paths = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "MutationLeaseContractModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "MutationLeaseContractValidator.cs"),
            Path.Combine(root, "IronDev.IntegrationTests", "BlockE07MutationLeaseLockContractTests.cs"),
            Path.Combine(root, "Docs", "receipts", "E07_MUTATION_LEASE_LOCK_CONTRACT.md")
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
}
