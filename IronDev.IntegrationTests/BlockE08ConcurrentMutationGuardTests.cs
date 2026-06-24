using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE08ConcurrentMutationGuardTests
{
    private const string TenantId = "tenant-e08";
    private const string ProjectId = "project-e08";
    private const string OperationId = "op_000000000000e008";
    private const string CorrelationId = "corr_000000000000e008";
    private static readonly DateTimeOffset NowUtc = DateTimeOffset.Parse("2026-06-25T09:00:00Z");
    private static readonly DateTimeOffset StartedAtUtc = NowUtc.AddMinutes(-2);
    private static readonly DateTimeOffset UpdatedAtUtc = NowUtc.AddMinutes(-1);
    private static readonly DateTimeOffset ExpiresAtUtc = NowUtc.AddMinutes(5);

    [TestMethod]
    public async Task ValidRequestWithNoObservationsProceedsToNextGateOnly()
    {
        var decision = await EvaluateAsync(Request(), []);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.AllowedToProceedToNextGate, decision.Decision);
        Assert.AreEqual(ConcurrentMutationConflictKind.None, decision.ConflictKind);
        Assert.IsFalse(decision.SafeToReuseExistingAttempt);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("patch-package:e08")]
    [DataRow("merge-target:e08")]
    [DataRow("release-candidate:e08")]
    [DataRow("deploy-target:e08")]
    public async Task ValidDomainReferencesAreNotRejectedAsUnsafePayload(string targetRef)
    {
        var decision = await EvaluateAsync(Request() with { MutationTargetRef = targetRef }, []);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.AllowedToProceedToNextGate, decision.Decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("tenant", "ConcurrentMutationGuardTenantIdRequired")]
    [DataRow("project", "ConcurrentMutationGuardProjectIdRequired")]
    [DataRow("operation", "ConcurrentMutationGuardOperationIdRequired")]
    [DataRow("operation-invalid", "ConcurrentMutationGuardOperationIdInvalid")]
    [DataRow("correlation", "ConcurrentMutationGuardCorrelationIdRequired")]
    [DataRow("correlation-invalid", "ConcurrentMutationGuardCorrelationIdInvalid")]
    [DataRow("surface", "ConcurrentMutationGuardMutationSurfaceRequired")]
    [DataRow("target", "ConcurrentMutationGuardMutationTargetRefRequired")]
    [DataRow("attempt", "ConcurrentMutationGuardRequestedAttemptRefRequired")]
    [DataRow("idempotency-key", "ConcurrentMutationGuardIdempotencyKeyRefRequired")]
    [DataRow("idempotency-fingerprint", "ConcurrentMutationGuardIdempotencyFingerprintRequired")]
    [DataRow("now", "ConcurrentMutationGuardNowUtcRequired")]
    [DataRow("now-non-utc", "ConcurrentMutationGuardNowUtcMustBeUtc")]
    public async Task InvalidRequestFailsClosedBeforeStoreRead(string mutation, string expectedReason)
    {
        var request = mutation switch
        {
            "tenant" => Request() with { TenantId = "" },
            "project" => Request() with { ProjectId = "" },
            "operation" => Request() with { OperationId = "" },
            "operation-invalid" => Request() with { OperationId = "run_123" },
            "correlation" => Request() with { CorrelationId = "" },
            "correlation-invalid" => Request() with { CorrelationId = "not-correlation" },
            "surface" => Request() with { MutationSurface = MutationLeaseSurfaceKind.Unknown },
            "target" => Request() with { MutationTargetRef = "" },
            "attempt" => Request() with { RequestedAttemptRef = "" },
            "idempotency-key" => Request() with { IdempotencyKeyRef = "" },
            "idempotency-fingerprint" => Request() with { IdempotencyFingerprint = "" },
            "now" => Request() with { NowUtc = default },
            "now-non-utc" => Request() with { NowUtc = DateTimeOffset.Parse("2026-06-25T09:00:00+01:00") },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };
        var store = new FakeConcurrentMutationGuardReadStore([]);
        var service = new ConcurrentMutationGuardService(store);

        var decision = await service.EvaluateAsync(request, CancellationToken.None);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual(expectedReason, decision.Reason);
        Assert.AreEqual(0, store.ReadCount);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task ActiveSameTargetBlocks()
    {
        var decision = await EvaluateAsync(Request(), [Observation(state: ConcurrentMutationObservationState.InProgress, attemptRef: "attempt-other")]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.BlockedByActiveMutation, decision.Decision);
        Assert.AreEqual(ConcurrentMutationConflictKind.ActiveMutation, decision.ConflictKind);
        Assert.AreEqual("attempt-other", decision.ConflictRef);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task ActiveDifferentTargetDoesNotBlock()
    {
        var decision = await EvaluateAsync(Request(), [Observation(targetRef: "target:e08-other", attemptRef: "attempt-other")]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.AllowedToProceedToNextGate, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task ActiveSameTargetUnderDifferentTenantDoesNotBlock()
    {
        var decision = await EvaluateAsync(Request(), [Observation(tenantId: "tenant-other", attemptRef: "attempt-other")]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.AllowedToProceedToNextGate, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task ActiveSameTargetUnderDifferentProjectDoesNotBlock()
    {
        var decision = await EvaluateAsync(Request(), [Observation(projectId: "project-other", attemptRef: "attempt-other")]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.AllowedToProceedToNextGate, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task SameIdempotencyKeyAndFingerprintMarksSafeReuseMetadataOnly()
    {
        var decision = await EvaluateAsync(Request(), [Observation(state: ConcurrentMutationObservationState.Succeeded)]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.AllowedToProceedToNextGate, decision.Decision);
        Assert.IsTrue(decision.SafeToReuseExistingAttempt);
        StringAssert.Contains(string.Join("\n", decision.Warnings), "idempotency match is not replay authority");
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task SameIdempotencyKeyWithDifferentFingerprintBlocks()
    {
        var decision = await EvaluateAsync(
            Request(),
            [Observation(state: ConcurrentMutationObservationState.Succeeded, fingerprint: "fingerprint-different")]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.BlockedByConflictingIdempotency, decision.Decision);
        Assert.AreEqual(ConcurrentMutationConflictKind.ConflictingIdempotency, decision.ConflictKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task DifferentIdempotencyKeyWithActiveSameTargetBlocks()
    {
        var decision = await EvaluateAsync(
            Request(),
            [Observation(attemptRef: "attempt-other", idempotencyKeyRef: "idempotency-key:other", fingerprint: "fingerprint-other")]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.BlockedByActiveMutation, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task HeldLeaseByDifferentOwnerBlocks()
    {
        var request = Request() with
        {
            ObservedLeaseRef = "lease:e08",
            ObservedLeaseState = MutationLeaseState.ObservedHeld,
            ObservedLeaseOwnerRef = "owner:e08",
            ObservedFenceRef = "fence:e08",
            ObservedSequenceRef = "sequence:e08",
            ObservedExpiresAtUtc = ExpiresAtUtc
        };
        var observation = Observation(
            state: ConcurrentMutationObservationState.ObservedHeld,
            attemptRef: "attempt-other",
            leaseRef: "lease:e08-other",
            leaseOwnerRef: "owner:other",
            fenceRef: "fence:other",
            sequenceRef: "sequence:other");

        var decision = await EvaluateAsync(request, [observation]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.BlockedByConflictingLease, decision.Decision);
        Assert.AreEqual(ConcurrentMutationConflictKind.ConflictingLease, decision.ConflictKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task HeldLeaseBySameAttemptProceedsOnlyAsMetadata()
    {
        var request = Request() with
        {
            ObservedLeaseRef = "lease:e08",
            ObservedLeaseState = MutationLeaseState.ObservedHeld,
            ObservedLeaseOwnerRef = "owner:e08",
            ObservedFenceRef = "fence:e08",
            ObservedSequenceRef = "sequence:e08",
            ObservedExpiresAtUtc = ExpiresAtUtc
        };
        var observation = Observation(
            state: ConcurrentMutationObservationState.ObservedHeld,
            leaseRef: "lease:e08",
            leaseOwnerRef: "owner:e08",
            fenceRef: "fence:e08",
            sequenceRef: "sequence:e08");

        var decision = await EvaluateAsync(request, [observation]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.AllowedToProceedToNextGate, decision.Decision);
        Assert.IsTrue(decision.SafeToReuseExistingAttempt);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task ExpiredTimestampAloneDoesNotUnblockHeldLease()
    {
        var observation = Observation(
            state: ConcurrentMutationObservationState.ObservedHeld,
            attemptRef: "attempt-other",
            leaseRef: "lease:e08-other",
            leaseOwnerRef: "owner:other",
            fenceRef: "fence:other",
            sequenceRef: "sequence:other",
            expiresAtUtc: NowUtc.AddMinutes(-1));

        var decision = await EvaluateAsync(Request(), [observation]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.BlockedByConflictingLease, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task ExplicitObservedExpiredStateCanProceedToNextGate()
    {
        var observation = Observation(
            state: ConcurrentMutationObservationState.ObservedExpired,
            attemptRef: "attempt-other",
            expiresAtUtc: NowUtc.AddMinutes(-1),
            terminalOutcomeRef: "lease-expired:e08");

        var decision = await EvaluateAsync(Request(), [observation]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.AllowedToProceedToNextGate, decision.Decision);
        Assert.IsFalse(decision.SafeToReuseExistingAttempt);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task UnknownStateBlocks()
    {
        var decision = await EvaluateAsync(Request(), [Observation(state: ConcurrentMutationObservationState.Unknown)]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.BlockedByUnknownState, decision.Decision);
        Assert.AreEqual(ConcurrentMutationConflictKind.UnknownState, decision.ConflictKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task StaleObservationBlocks()
    {
        var observation = Observation(updatedAtUtc: NowUtc.AddMinutes(-20));

        var decision = await EvaluateAsync(Request(), [observation]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.BlockedByStaleObservation, decision.Decision);
        Assert.AreEqual(ConcurrentMutationConflictKind.StaleObservation, decision.ConflictKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task FutureObservationTimestampBlocks()
    {
        var observation = Observation(updatedAtUtc: NowUtc.AddMinutes(1));

        var decision = await EvaluateAsync(Request(), [observation]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.BlockedByStaleObservation, decision.Decision);
        Assert.AreEqual("ConcurrentMutationObservationTimestampInFuture", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task MalformedObservationBlocks()
    {
        var observation = Observation(attemptRef: "bad attempt whitespace");

        var decision = await EvaluateAsync(Request(), [observation]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.BlockedByUnknownState, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task TooManyObservationsBlocks()
    {
        var observations = Enumerable.Range(0, ConcurrentMutationGuardValidator.MaxObservations + 1)
            .Select(index => Observation(attemptRef: $"attempt-{index:00}", targetRef: $"target:e08-{index:00}"))
            .ToArray();

        var decision = await EvaluateAsync(Request(), observations);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.BlockedByStaleObservation, decision.Decision);
        Assert.AreEqual(ConcurrentMutationConflictKind.TooManyObservations, decision.ConflictKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("authority")]
    [DataRow("raw-patch")]
    [DataRow("git-output")]
    [DataRow("bearer")]
    [DataRow("token")]
    [DataRow("secret")]
    [DataRow("password")]
    public async Task UnsafeRequestMarkersAreRejected(string marker)
    {
        var unsafeValue = marker switch
        {
            "authority" => "approval granted",
            "raw-patch" => "diff --git a/file b/file",
            "git-output" => string.Concat("raw ", "gi", "t output: fake"),
            "bearer" => string.Concat("bear", "er fake"),
            "token" => string.Concat("token", "=fake"),
            "secret" => FakeSecretMarker(),
            "password" => FakePasswordMarker(),
            _ => throw new ArgumentOutOfRangeException(nameof(marker), marker, null)
        };
        var request = Request() with { MutationTargetRef = unsafeValue };

        var decision = await EvaluateAsync(request, []);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual(ConcurrentMutationConflictKind.UnsafePayload, decision.ConflictKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task UnsafeObservationMarkersAreRejected()
    {
        var observation = Observation(attemptRef: "approval granted");

        var decision = await EvaluateAsync(Request(), [observation]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.BlockedByUnknownState, decision.Decision);
        Assert.AreEqual(ConcurrentMutationConflictKind.UnsafePayload, decision.ConflictKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task DecisionFingerprintIsDeterministic()
    {
        var observations = new[]
        {
            Observation(state: ConcurrentMutationObservationState.Succeeded)
        };

        var first = await EvaluateAsync(Request(), observations);
        var second = await EvaluateAsync(Request(), observations);

        Assert.AreEqual(first.RecordFingerprint, second.RecordFingerprint);
        AssertNoAuthority(first);
    }

    [TestMethod]
    public async Task ObservationOrderingIsDeterministic()
    {
        var observations = new[]
        {
            Observation(attemptRef: "attempt-zeta", idempotencyKeyRef: "idempotency-key:zeta", fingerprint: "fingerprint-zeta"),
            Observation(attemptRef: "attempt-alpha", idempotencyKeyRef: "idempotency-key:alpha", fingerprint: "fingerprint-alpha")
        };

        var decision = await EvaluateAsync(Request(), observations);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.BlockedByActiveMutation, decision.Decision);
        Assert.AreEqual("attempt-alpha", decision.MatchedAttemptRef);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(ConcurrentMutationObservationState.Succeeded, "terminal:succeeded", "not authority to mutate")]
    [DataRow(ConcurrentMutationObservationState.Failed, "terminal:failed", "not retry authority")]
    [DataRow(ConcurrentMutationObservationState.Interrupted, "terminal:interrupted", "not resume authority")]
    [DataRow(ConcurrentMutationObservationState.Cancelled, "terminal:cancelled", "not rollback authority")]
    public async Task TerminalPriorAttemptDoesNotImplyAuthority(
        ConcurrentMutationObservationState state,
        string terminalOutcomeRef,
        string expectedWarning)
    {
        var observation = Observation(state: state, terminalOutcomeRef: terminalOutcomeRef);

        var decision = await EvaluateAsync(Request(), [observation]);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.AllowedToProceedToNextGate, decision.Decision);
        StringAssert.Contains(string.Join("\n", decision.Warnings), expectedWarning);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task StoreTruncationMarkerBlocksAsStaleUnsafeEvidence()
    {
        var store = new FakeConcurrentMutationGuardReadStore(
            [],
            resultWasTruncated: true,
            truncationReason: "too-many:e08");
        var service = new ConcurrentMutationGuardService(store);

        var decision = await service.EvaluateAsync(Request(), CancellationToken.None);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.BlockedByStaleObservation, decision.Decision);
        Assert.AreEqual(ConcurrentMutationConflictKind.TooManyObservations, decision.ConflictKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task NullStoreResultBlocksAsStaleEvidence()
    {
        var store = new FakeConcurrentMutationGuardReadStore([], returnNullResult: true);
        var service = new ConcurrentMutationGuardService(store);

        var decision = await service.EvaluateAsync(Request(), CancellationToken.None);

        Assert.AreEqual(ConcurrentMutationGuardDecisionKind.BlockedByStaleObservation, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ValidatorRejectsIncompleteObservedLeaseMetadata()
    {
        var request = Request() with
        {
            ObservedLeaseRef = "lease:e08",
            ObservedLeaseState = MutationLeaseState.ObservedHeld
        };

        var result = ConcurrentMutationGuardValidator.ValidateRequest(request);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToArray(), "ConcurrentMutationGuardObservedLeaseOwnerRefRequired");
        CollectionAssert.Contains(result.Issues.ToArray(), "ConcurrentMutationGuardObservedFenceRefRequired");
        CollectionAssert.Contains(result.Issues.ToArray(), "ConcurrentMutationGuardObservedSequenceRefRequired");
        CollectionAssert.Contains(result.Issues.ToArray(), "ConcurrentMutationGuardObservedExpiresAtUtcRequired");
    }

    [TestMethod]
    public void ContractTypesDoNotExposeAuthorityOrCommandFields()
    {
        var names = typeof(ConcurrentMutationGuardRequest)
            .Assembly
            .GetTypes()
            .Where(static type => type.Namespace == "IronDev.Core.Governance" &&
                type.Name.StartsWith("ConcurrentMutation", StringComparison.Ordinal))
            .SelectMany(static type => type.GetProperties().Select(property => $"{type.Name}.{property.Name}"))
            .ToArray();

        Assert.IsFalse(names.Any(static name => name.Contains("Command", StringComparison.Ordinal)), string.Join(", ", names));
        Assert.IsFalse(names.Any(static name => name.Contains("Executor", StringComparison.Ordinal)), string.Join(", ", names));
        Assert.IsFalse(names.Any(static name => name.Contains("CanMutate", StringComparison.Ordinal)), string.Join(", ", names));
        Assert.IsFalse(names.Any(static name => name.Contains("CanExecute", StringComparison.Ordinal)), string.Join(", ", names));
        Assert.IsFalse(names.Any(static name => name.Contains("CanContinue", StringComparison.Ordinal)), string.Join(", ", names));
    }

    [TestMethod]
    public void DecisionEnumDoesNotSayAllowedToMutate()
    {
        var names = Enum.GetNames<ConcurrentMutationGuardDecisionKind>();

        Assert.IsFalse(names.Any(static name => name.Contains("AllowedToMutate", StringComparison.Ordinal)), string.Join(", ", names));
        CollectionAssert.Contains(names, nameof(ConcurrentMutationGuardDecisionKind.AllowedToProceedToNextGate));
    }

    [TestMethod]
    public void StaticScan_E08ProductionFilesDoNotAcquireReleaseOrEnforceLocks()
    {
        var source = E08ProductionSource();

        AssertNoStaticToken(source, "SemaphoreSlim");
        AssertNoStaticToken(source, "Mutex");
        AssertNoStaticToken(source, "Monitor.Enter");
        AssertNoStaticToken(source, "ReaderWriterLock");
        AssertNoStaticToken(source, "ConcurrentDictionary");
        AssertNoStaticToken(source, "MemoryCache");
        AssertNoStaticToken(source, "lock (");
        AssertNoStaticToken(source, "sp_getapplock");
        AssertNoStaticToken(source, "pg_advisory_lock");
        AssertNoStaticToken(source, "AcquireLease");
        AssertNoStaticToken(source, "ReleaseLease");
        AssertNoStaticToken(source, "RenewLease");
    }

    [TestMethod]
    public void StaticScan_E08ProductionFilesDoNotWireExecutorsOrMutationSurfaces()
    {
        var source = E08ProductionSource();

        AssertNoStaticToken(source, "Controller");
        AssertNoStaticToken(source, "MapPost");
        AssertNoStaticToken(source, "OpenApi");
        AssertNoStaticToken(source, "Tauri");
        AssertNoStaticToken(source, "Frontend");
        AssertNoStaticToken(source, "ProcessStartInfo");
        AssertNoStaticToken(source, "HttpClient");
        AssertNoStaticToken(source, "File.Write");
        AssertNoStaticToken(source, "RunProcessAsync");
        AssertNoStaticToken(source, "ControlledSourceApplyExecutor");
        AssertNoStaticToken(source, "ControlledCommitExecutor");
        AssertNoStaticToken(source, "ControlledPushExecutor");
        AssertNoStaticToken(source, "ControlledRollbackExecutor");
        AssertNoStaticToken(source, "WorkflowContinuationExecutor");
        AssertNoStaticToken(source, "GitHubClient");
        AssertNoStaticToken(source, "GitClient");
        AssertNoStaticToken(source, "MergeExecutor");
        AssertNoStaticToken(source, "ReleaseExecutor");
        AssertNoStaticToken(source, "DeploymentExecutor");
        AssertNoStaticToken(source, "MemoryPromotionExecutor");
    }

    [TestMethod]
    public void StaticScan_E08ProductionFilesRemainMetadataOnly()
    {
        var source = E08ProductionSource();

        AssertNoStaticToken(source, "RawPatch {");
        AssertNoStaticToken(source, "RawDiff {");
        AssertNoStaticToken(source, "SourceContent {");
        AssertNoStaticToken(source, "CommandText {");
        AssertNoStaticToken(source, "Action {");
        AssertNoStaticToken(source, "CanMutate");
        AssertNoStaticToken(source, "CanExecute");
        AssertNoStaticToken(source, "CanContinue");
    }

    [TestMethod]
    public void StaticScan_E08FilesDoNotCommitContiguousSecretShapedFixtures()
    {
        var source = E08AllTouchedSource();

        AssertNoStaticToken(source, string.Concat("-----", "BEGIN"));
        AssertNoStaticToken(source, string.Concat("Pass", "word="));
        AssertNoStaticToken(source, string.Concat("Bear", "er "));
        AssertNoStaticToken(source, string.Concat("PRI", "VATE ", "KE", "Y-----"));
        AssertNoStaticToken(source, string.Concat("access_", "token="));
    }

    [TestMethod]
    public void ReceiptDocumentsGuardIsNotAuthorityBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "E08_CONCURRENT_MUTATION_GUARD.md"));

        StringAssert.Contains(receipt, "A concurrent mutation guard prevents overlap. It does not grant permission.");
        StringAssert.Contains(receipt, "No conflict found is not authority to mutate.");
        StringAssert.Contains(receipt, "E08 does not acquire, release, renew, or enforce leases.");
    }

    private static Task<ConcurrentMutationGuardDecision> EvaluateAsync(
        ConcurrentMutationGuardRequest request,
        IReadOnlyList<ConcurrentMutationObservation> observations)
    {
        var service = new ConcurrentMutationGuardService(new FakeConcurrentMutationGuardReadStore(observations));
        return service.EvaluateAsync(request, CancellationToken.None);
    }

    private static ConcurrentMutationGuardRequest Request() =>
        new()
        {
            TenantId = TenantId,
            ProjectId = ProjectId,
            OperationId = OperationId,
            CorrelationId = CorrelationId,
            MutationSurface = MutationLeaseSurfaceKind.SourceApply,
            MutationTargetRef = "target:e08",
            RequestedAttemptRef = "attempt:e08",
            IdempotencyKeyRef = "idempotency-key:e08",
            IdempotencyFingerprint = "fingerprint:e08",
            ObservedLeaseRef = null,
            ObservedLeaseState = null,
            ObservedLeaseOwnerRef = null,
            ObservedFenceRef = null,
            ObservedSequenceRef = null,
            ObservedExpiresAtUtc = null,
            NowUtc = NowUtc
        };

    private static ConcurrentMutationObservation Observation(
        string tenantId = TenantId,
        string projectId = ProjectId,
        MutationLeaseSurfaceKind mutationSurface = MutationLeaseSurfaceKind.SourceApply,
        string targetRef = "target:e08",
        string attemptRef = "attempt:e08",
        string idempotencyKeyRef = "idempotency-key:e08",
        string fingerprint = "fingerprint:e08",
        ConcurrentMutationObservationState state = ConcurrentMutationObservationState.InProgress,
        string? leaseRef = null,
        string? leaseOwnerRef = null,
        string? fenceRef = null,
        string? sequenceRef = null,
        DateTimeOffset? startedAtUtc = null,
        DateTimeOffset? updatedAtUtc = null,
        DateTimeOffset? expiresAtUtc = null,
        string? terminalOutcomeRef = null) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            MutationSurface = mutationSurface,
            MutationTargetRef = targetRef,
            AttemptRef = attemptRef,
            IdempotencyKeyRef = idempotencyKeyRef,
            IdempotencyFingerprint = fingerprint,
            ObservedState = state,
            ObservedLeaseRef = leaseRef,
            ObservedLeaseOwnerRef = leaseOwnerRef,
            ObservedFenceRef = fenceRef,
            ObservedSequenceRef = sequenceRef,
            ObservedStartedAtUtc = startedAtUtc ?? StartedAtUtc,
            ObservedUpdatedAtUtc = updatedAtUtc ?? UpdatedAtUtc,
            ObservedExpiresAtUtc = expiresAtUtc ?? (state == ConcurrentMutationObservationState.ObservedHeld ? ExpiresAtUtc : null),
            TerminalOutcomeRef = terminalOutcomeRef ?? (IsTerminal(state) ? $"terminal:{state}" : null)
        };

    private static bool IsTerminal(ConcurrentMutationObservationState state) =>
        state is ConcurrentMutationObservationState.Succeeded
            or ConcurrentMutationObservationState.Failed
            or ConcurrentMutationObservationState.Cancelled
            or ConcurrentMutationObservationState.Interrupted
            or ConcurrentMutationObservationState.ObservedReleased
            or ConcurrentMutationObservationState.ObservedExpired;

    private static void AssertNoAuthority(ConcurrentMutationGuardDecision decision)
    {
        Assert.IsTrue(decision.RequiresFreshAuthority);
        var text = string.Join("\n", decision.Warnings);
        StringAssert.Contains(text, "not mutation authority");
        StringAssert.Contains(text, "not lease authority");
        StringAssert.Contains(text, "not approval");
        StringAssert.Contains(text, "not policy satisfaction");
        StringAssert.Contains(text, "not source apply authority");
        StringAssert.Contains(text, "not commit authority");
        StringAssert.Contains(text, "not push authority");
        StringAssert.Contains(text, "not rollback authority");
        StringAssert.Contains(text, "not workflow continuation");
        StringAssert.Contains(text, "no conflict found is not authority to mutate");
    }

    private static void AssertNoStaticToken(string source, string token) =>
        Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"{token} was found.");

    private static string E08ProductionSource()
    {
        var root = RepoRoot();
        var paths = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "ConcurrentMutationGuardModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ConcurrentMutationGuardValidator.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IConcurrentMutationGuardReadStore.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ConcurrentMutationGuardService.cs")
        };

        return string.Join("\n", paths.Select(File.ReadAllText));
    }

    private static string E08AllTouchedSource()
    {
        var root = RepoRoot();
        var paths = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "ConcurrentMutationGuardModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ConcurrentMutationGuardValidator.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IConcurrentMutationGuardReadStore.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ConcurrentMutationGuardService.cs"),
            Path.Combine(root, "IronDev.IntegrationTests", "BlockE08ConcurrentMutationGuardTests.cs"),
            Path.Combine(root, "Docs", "receipts", "E08_CONCURRENT_MUTATION_GUARD.md")
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

    private static string FakeSecretMarker() =>
        string.Concat("sec", "ret=fake");

    private static string FakePasswordMarker() =>
        string.Concat("pass", "word=fake");

    private sealed class FakeConcurrentMutationGuardReadStore : IConcurrentMutationGuardReadStore
    {
        private readonly IReadOnlyList<ConcurrentMutationObservation> _observations;
        private readonly bool _resultWasTruncated;
        private readonly string _truncationReason;
        private readonly bool _returnNullResult;

        public FakeConcurrentMutationGuardReadStore(
            IReadOnlyList<ConcurrentMutationObservation> observations,
            bool resultWasTruncated = false,
            string truncationReason = "",
            bool returnNullResult = false)
        {
            _observations = observations;
            _resultWasTruncated = resultWasTruncated;
            _truncationReason = truncationReason;
            _returnNullResult = returnNullResult;
        }

        public int ReadCount { get; private set; }

        public Task<ConcurrentMutationGuardReadResult> FindPotentialConflictsAsync(
            ConcurrentMutationGuardRequest request,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            if (_returnNullResult)
            {
                return Task.FromResult<ConcurrentMutationGuardReadResult>(null!);
            }

            return Task.FromResult(new ConcurrentMutationGuardReadResult
            {
                Observations = _observations,
                WasTruncated = _resultWasTruncated,
                TruncationReason = _truncationReason
            });
        }
    }
}
