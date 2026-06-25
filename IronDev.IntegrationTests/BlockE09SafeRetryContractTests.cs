using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE09SafeRetryContractTests
{
    private static readonly DateTimeOffset AssessedAtUtc = new(2026, 6, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NowUtc = AssessedAtUtc.AddMinutes(1);

    [TestMethod]
    public async Task ValidPreMutationFailedAttemptProceedsToAuthorityGateOnly()
    {
        var store = new FakeSafeRetryAttemptReadStore();
        var decision = await EvaluateAsync(Request(), store);

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.RetryRequestMayProceedToAuthorityGate, decision.Decision);
        Assert.AreEqual(SafeRetryAssessmentBlockKind.None, decision.BlockKind);
        Assert.IsTrue(decision.SafeRetryCandidateForNextGate);
        Assert.AreEqual(1, store.ReadCount);
        AssertRequiresFreshGates(decision);
        AssertNoAuthority(decision);
        CollectionAssert.Contains(decision.Warnings.ToList(), "retry classification is not retry authority");
    }

    [TestMethod]
    public async Task ValidDecisionAlwaysRequiresFreshAuthority()
    {
        var decision = await EvaluateAsync(Request());

        Assert.IsTrue(decision.RequiresFreshAuthority);
    }

    [TestMethod]
    public async Task ValidDecisionAlwaysRequiresFreshValidation()
    {
        var decision = await EvaluateAsync(Request());

        Assert.IsTrue(decision.RequiresFreshValidation);
    }

    [TestMethod]
    public async Task ValidDecisionAlwaysRequiresFreshConcurrentGuard()
    {
        var decision = await EvaluateAsync(Request());

        Assert.IsTrue(decision.RequiresFreshConcurrentGuard);
    }

    [TestMethod]
    public async Task ValidDecisionAlwaysRequiresFreshPostStateObservation()
    {
        var decision = await EvaluateAsync(Request());

        Assert.IsTrue(decision.RequiresFreshPostStateObservation);
    }

    [DataTestMethod]
    [DataRow("tenant", "SafeRetryTenantIdRequired")]
    [DataRow("project", "SafeRetryProjectIdRequired")]
    [DataRow("operation", "SafeRetryOperationIdRequired")]
    [DataRow("operation-invalid", "SafeRetryOperationIdInvalid")]
    [DataRow("correlation", "SafeRetryCorrelationIdRequired")]
    [DataRow("correlation-invalid", "SafeRetryCorrelationIdInvalid")]
    [DataRow("surface", "SafeRetryMutationSurfaceRequired")]
    [DataRow("target", "SafeRetryMutationTargetRefRequired")]
    [DataRow("failed-attempt", "SafeRetryFailedAttemptRefRequired")]
    [DataRow("previous-key", "SafeRetryPreviousIdempotencyKeyRefRequired")]
    [DataRow("previous-fingerprint", "SafeRetryPreviousIdempotencyFingerprintRequired")]
    [DataRow("proposed-attempt", "SafeRetryProposedRetryAttemptRefRequired")]
    [DataRow("proposed-key", "SafeRetryProposedRetryIdempotencyKeyRefRequired")]
    [DataRow("proposed-fingerprint", "SafeRetryProposedRetryIdempotencyFingerprintRequired")]
    [DataRow("lineage", "SafeRetryRetryLineageRefRequired")]
    [DataRow("guard-ref", "SafeRetryCurrentGuardDecisionRefRequired")]
    [DataRow("assessed", "SafeRetryAssessedAtUtcRequired")]
    [DataRow("assessed-non-utc", "SafeRetryAssessedAtUtcMustBeUtc")]
    [DataRow("now", "SafeRetryNowUtcRequired")]
    [DataRow("now-non-utc", "SafeRetryNowUtcMustBeUtc")]
    [DataRow("reason", "SafeRetryReasonCodeRequired")]
    [DataRow("source", "SafeRetrySourceRequired")]
    public async Task InvalidRequestFailsClosedBeforeStoreRead(string caseName, string expectedReason)
    {
        var store = new FakeSafeRetryAttemptReadStore();
        var decision = await EvaluateAsync(InvalidRequest(caseName), store);

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual(SafeRetryAssessmentBlockKind.InvalidRequest, decision.BlockKind);
        Assert.AreEqual(expectedReason, decision.Reason);
        Assert.AreEqual(0, store.ReadCount);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("failure-receipt", "SafeRetryFailureReceiptRefRequired")]
    [DataRow("terminal-outcome", "SafeRetryTerminalOutcomeRefRequired")]
    [DataRow("post-state", "SafeRetryPostStateObservationRefRequired")]
    public async Task MissingReceiptEvidenceBlocksBeforeStoreRead(string caseName, string expectedReason)
    {
        var store = new FakeSafeRetryAttemptReadStore();
        var decision = await EvaluateAsync(MissingReceiptRequest(caseName), store);

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.BlockedByMissingReceiptEvidence, decision.Decision);
        Assert.AreEqual(SafeRetryAssessmentBlockKind.MissingReceiptEvidence, decision.BlockKind);
        Assert.AreEqual(expectedReason, decision.Reason);
        Assert.AreEqual(0, store.ReadCount);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(SafeRetryAttemptOutcome.Succeeded, SafeRetryAssessmentDecisionKind.BlockedBySucceededAttempt)]
    [DataRow(SafeRetryAttemptOutcome.Cancelled, SafeRetryAssessmentDecisionKind.BlockedByCancelledAttempt)]
    [DataRow(SafeRetryAttemptOutcome.Interrupted, SafeRetryAssessmentDecisionKind.BlockedByInterruptedAttempt)]
    [DataRow(SafeRetryAttemptOutcome.InProgress, SafeRetryAssessmentDecisionKind.BlockedByNonTerminalAttempt)]
    [DataRow(SafeRetryAttemptOutcome.Requested, SafeRetryAssessmentDecisionKind.BlockedByNonTerminalAttempt)]
    [DataRow(SafeRetryAttemptOutcome.Unknown, SafeRetryAssessmentDecisionKind.BlockedByNonTerminalAttempt)]
    public async Task OnlyFailedTerminalAttemptsMayBeAssessed(
        SafeRetryAttemptOutcome outcome,
        SafeRetryAssessmentDecisionKind expectedDecision)
    {
        var store = new FakeSafeRetryAttemptReadStore();
        var decision = await EvaluateAsync(Request() with { FailedAttemptOutcome = outcome }, store);

        Assert.AreEqual(expectedDecision, decision.Decision);
        Assert.AreEqual(0, store.ReadCount);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task UnknownFailureClassBlocks()
    {
        var store = new FakeSafeRetryAttemptReadStore();
        var decision = await EvaluateAsync(Request() with { FailureClass = SafeRetryFailureClass.Unknown }, store);

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.BlockedByUnknownFailureClass, decision.Decision);
        Assert.AreEqual(0, store.ReadCount);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(SafeRetryFailureClass.ValidationFailed)]
    [DataRow(SafeRetryFailureClass.PolicyDenied)]
    [DataRow(SafeRetryFailureClass.ApprovalMissing)]
    [DataRow(SafeRetryFailureClass.AuthorityBoundaryViolation)]
    [DataRow(SafeRetryFailureClass.UnsafePayloadRejected)]
    [DataRow(SafeRetryFailureClass.SecretOrCredentialRejected)]
    [DataRow(SafeRetryFailureClass.MutationBoundaryUnknown)]
    [DataRow(SafeRetryFailureClass.MutationMayHaveStarted)]
    [DataRow(SafeRetryFailureClass.PartialMutationObserved)]
    [DataRow(SafeRetryFailureClass.ProviderRejectedAfterMutationStarted)]
    [DataRow(SafeRetryFailureClass.SourceStateUnknown)]
    [DataRow(SafeRetryFailureClass.ManualCancellation)]
    public async Task UnsafeFailureClassesBlock(SafeRetryFailureClass failureClass)
    {
        var decision = await EvaluateAsync(Request() with { FailureClass = failureClass });

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.BlockedByUnsafeFailureClass, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task IdempotencyConflictFailureClassBlocksSpecifically()
    {
        var decision = await EvaluateAsync(Request() with { FailureClass = SafeRetryFailureClass.IdempotencyConflict });

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.BlockedByConflictingIdempotency, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task PostStateUnknownFailureClassBlocksSpecifically()
    {
        var decision = await EvaluateAsync(Request() with { FailureClass = SafeRetryFailureClass.PostStateUnknown });

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.BlockedByUnknownPostState, decision.Decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(SafeRetryMutationBoundaryState.Unknown, SafeRetryAssessmentDecisionKind.BlockedByMutationBoundaryUnknown)]
    [DataRow(SafeRetryMutationBoundaryState.Started, SafeRetryAssessmentDecisionKind.BlockedByMutationMayHaveStarted)]
    [DataRow(SafeRetryMutationBoundaryState.PartiallyObserved, SafeRetryAssessmentDecisionKind.BlockedByMutationMayHaveStarted)]
    [DataRow(SafeRetryMutationBoundaryState.Completed, SafeRetryAssessmentDecisionKind.BlockedByMutationMayHaveStarted)]
    public async Task MutationBoundaryMustProveNotStarted(
        SafeRetryMutationBoundaryState boundaryState,
        SafeRetryAssessmentDecisionKind expectedDecision)
    {
        var store = new FakeSafeRetryAttemptReadStore();
        var decision = await EvaluateAsync(Request() with { MutationBoundaryState = boundaryState }, store);

        Assert.AreEqual(expectedDecision, decision.Decision);
        Assert.AreEqual(0, store.ReadCount);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(SafeRetryCurrentGuardState.BlockedByActiveMutation)]
    [DataRow(SafeRetryCurrentGuardState.BlockedByConflictingLease)]
    [DataRow(SafeRetryCurrentGuardState.BlockedByConflictingIdempotency)]
    [DataRow(SafeRetryCurrentGuardState.BlockedByStaleObservation)]
    [DataRow(SafeRetryCurrentGuardState.BlockedByUnknownState)]
    [DataRow(SafeRetryCurrentGuardState.Unknown)]
    public async Task CurrentGuardBlockedStateBlocksRetryConsideration(SafeRetryCurrentGuardState guardState)
    {
        var store = new FakeSafeRetryAttemptReadStore();
        var decision = await EvaluateAsync(Request() with { CurrentGuardDecision = guardState }, store);

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.BlockedByConcurrentMutationGuard, decision.Decision);
        Assert.AreEqual(0, store.ReadCount);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task SameIdempotencyKeyAndSameFingerprintProceedsOnlyAsMetadata()
    {
        var decision = await EvaluateAsync(Request());

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.RetryRequestMayProceedToAuthorityGate, decision.Decision);
        CollectionAssert.Contains(decision.Warnings.ToList(), "idempotency match is not retry authority");
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task SameIdempotencyKeyAndDifferentFingerprintBlocks()
    {
        var decision = await EvaluateAsync(Request() with { ProposedRetryIdempotencyFingerprint = "idempotency-fingerprint:e09:different" });

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.BlockedByConflictingIdempotency, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task DifferentIdempotencyKeyWithLineageProceedsOnlyToAuthorityGate()
    {
        var request = Request() with
        {
            ProposedRetryIdempotencyKeyRef = "idempotency-key:e09:new",
            ProposedRetryIdempotencyFingerprint = "idempotency-fingerprint:e09:new",
            ReasonCode = "new-key-after-timeout"
        };

        var decision = await EvaluateAsync(request);

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.RetryRequestMayProceedToAuthorityGate, decision.Decision);
        CollectionAssert.Contains(decision.Warnings.ToList(), "new idempotency key is not new authority");
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task DifferentIdempotencyKeyWithoutLineageBlocks()
    {
        var store = new FakeSafeRetryAttemptReadStore();
        var request = Request() with
        {
            ProposedRetryIdempotencyKeyRef = "idempotency-key:e09:new",
            ProposedRetryIdempotencyFingerprint = "idempotency-fingerprint:e09:new",
            RetryLineageRef = ""
        };
        var decision = await EvaluateAsync(request, store);

        Assert.AreNotEqual(SafeRetryAssessmentDecisionKind.RetryRequestMayProceedToAuthorityGate, decision.Decision);
        Assert.AreEqual(0, store.ReadCount);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task RetryBudgetExceededBlocks()
    {
        var decision = await EvaluateAsync(Request() with { PriorRetryCount = 3, MaxRetryCount = 3 });

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.BlockedByRetryBudget, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task PriorRetryCountUnderReportsLineageBlocks()
    {
        var store = new FakeSafeRetryAttemptReadStore
        {
            Result = LineageWithObservedCount(1)
        };

        var decision = await EvaluateAsync(Request() with { PriorRetryCount = 0, MaxRetryCount = 3 }, store);

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.BlockedByRetryBudget, decision.Decision);
        Assert.AreEqual("SafeRetryPriorRetryCountMismatch", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task PriorRetryCountOverReportsLineageBlocks()
    {
        var store = new FakeSafeRetryAttemptReadStore
        {
            Result = LineageWithObservedCount(1)
        };

        var decision = await EvaluateAsync(Request() with { PriorRetryCount = 2, MaxRetryCount = 3 }, store);

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.BlockedByRetryBudget, decision.Decision);
        Assert.AreEqual("SafeRetryPriorRetryCountMismatch", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task RetryBudgetUsesObservedLineageCount()
    {
        var store = new FakeSafeRetryAttemptReadStore
        {
            Result = LineageWithObservedCount(2)
        };

        var decision = await EvaluateAsync(Request() with { PriorRetryCount = 2, MaxRetryCount = 3 }, store);

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.RetryRequestMayProceedToAuthorityGate, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task ObservedLineageCountAtMaxBlocksEvenWhenRequestUnderReports()
    {
        var store = new FakeSafeRetryAttemptReadStore
        {
            Result = LineageWithObservedCount(3)
        };

        var decision = await EvaluateAsync(Request() with { PriorRetryCount = 0, MaxRetryCount = 3 }, store);

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.BlockedByRetryBudget, decision.Decision);
        Assert.AreEqual("SafeRetryBudgetExceededByObservedLineage", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task RetryMaxOverHardCapRejected()
    {
        var store = new FakeSafeRetryAttemptReadStore();
        var decision = await EvaluateAsync(Request() with { MaxRetryCount = SafeRetryContractValidator.MaxRetryCountHardCap + 1 }, store);

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual("SafeRetryMaxRetryCountHardCapExceeded", decision.Reason);
        Assert.AreEqual(0, store.ReadCount);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public async Task TruncatedLineageReadBlocks()
    {
        var store = new FakeSafeRetryAttemptReadStore
        {
            Result = SafeRetryLineageReadResult.Empty("retry-lineage:e09") with
            {
                WasTruncated = true,
                TruncationReason = "more-than-ten"
            }
        };

        var decision = await EvaluateAsync(Request(), store);

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.BlockedByRetryBudget, decision.Decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("tenant")]
    [DataRow("project")]
    [DataRow("target")]
    public async Task LineageScopeMismatchBlocks(string mismatch)
    {
        var attempt = PriorAttempt() with
        {
            TenantId = mismatch == "tenant" ? "tenant-other" : "tenant-e09",
            ProjectId = mismatch == "project" ? "project-other" : "project-e09",
            MutationTargetRef = mismatch == "target" ? "patch-package:e09:other" : "patch-package:e09"
        };
        var store = new FakeSafeRetryAttemptReadStore
        {
            Result = SafeRetryLineageReadResult.Empty("retry-lineage:e09") with
            {
                PriorAttempts = [attempt]
            }
        };

        var decision = await EvaluateAsync(Request(), store);

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.BlockedByMissingReceiptEvidence, decision.Decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("raw-patch")]
    [DataRow("raw-git-output")]
    [DataRow("credential")]
    [DataRow("authority")]
    public async Task UnsafeRequestMarkersAreRejected(string marker)
    {
        var unsafeValue = marker switch
        {
            "raw-patch" => "raw patch payload",
            "raw-git-output" => string.Concat("raw ", "gi", "t output"),
            "credential" => string.Concat("token", "=fake"),
            "authority" => "retry authorized",
            _ => throw new ArgumentOutOfRangeException(nameof(marker), marker, null)
        };
        var store = new FakeSafeRetryAttemptReadStore();

        var decision = await EvaluateAsync(Request() with { MutationTargetRef = unsafeValue }, store);

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.AreEqual(0, store.ReadCount);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("patch-package:e09")]
    [DataRow("merge-target:e09")]
    [DataRow("release-candidate:e09")]
    [DataRow("deploy-target:e09")]
    public async Task ValidDomainReferencesAreNotRejected(string targetRef)
    {
        var decision = await EvaluateAsync(Request() with { MutationTargetRef = targetRef });

        Assert.AreEqual(SafeRetryAssessmentDecisionKind.RetryRequestMayProceedToAuthorityGate, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void DecisionEnumDoesNotContainUnsafeRetryAuthorityNames()
    {
        var names = Enum.GetNames<SafeRetryAssessmentDecisionKind>();

        CollectionAssert.DoesNotContain(names, "AllowedToRetry");
        CollectionAssert.DoesNotContain(names, "RetryAuthorized");
        CollectionAssert.DoesNotContain(names, "RetryApproved");
        CollectionAssert.DoesNotContain(names, "SafeToRetry");
    }

    [TestMethod]
    public void ContractTypesExposeNoCommandExecutorOrActionAuthorityFields()
    {
        var types = new[]
        {
            typeof(SafeRetryAssessmentRequest),
            typeof(SafeRetryAssessmentDecision),
            typeof(SafeRetryLineageReadResult),
            typeof(SafeRetryPriorAttemptMetadata)
        };
        var forbiddenFragments = new[]
        {
            "Command",
            "Executor",
            "Action",
            "RunProcess",
            "Apply",
            "Commit",
            "Push",
            "PullRequest",
            "Rollback",
            "Schedule",
            "Queue"
        };

        foreach (var property in types.SelectMany(static type => type.GetProperties()))
        {
            foreach (var forbidden in forbiddenFragments)
            {
                Assert.IsFalse(
                    property.Name.Contains(forbidden, StringComparison.Ordinal),
                    $"{property.DeclaringType?.Name}.{property.Name} contains {forbidden}");
            }
        }
    }

    [TestMethod]
    public void StaticScanProvesNoExecutorWiring()
    {
        var source = CoreSource();
        var forbidden = new[]
        {
            "Executor",
            "IControlled",
            "RunProcessAsync",
            "ProcessStartInfo",
            "Process.Start",
            "WorkflowContinuation"
        };

        AssertForbiddenFragmentsAbsent(source, forbidden);
    }

    [TestMethod]
    public void StaticScanProvesNoGitGitHubSourceOrWorktreeAccess()
    {
        var source = CoreSource();
        var forbidden = new[]
        {
            "Octokit",
            "GitHubClient",
            "gh ",
            "git ",
            "IWorktree",
            "WorktreeInspector",
            "File.Write",
            "File.ReadAllText",
            "Directory."
        };

        AssertForbiddenFragmentsAbsent(source, forbidden);
    }

    [TestMethod]
    public void StaticScanProvesNoLockAcquisitionReleaseOrRenewal()
    {
        var source = CoreSource();
        var forbidden = new[]
        {
            "AcquireLease",
            "ReleaseLease",
            "RenewLease",
            "EnforceLease",
            "LockAsync",
            "UnlockAsync"
        };

        AssertForbiddenFragmentsAbsent(source, forbidden);
    }

    [TestMethod]
    public async Task DeterministicFingerprintStableAcrossIdenticalRequest()
    {
        var request = Request();
        var first = await EvaluateAsync(request);
        var second = await EvaluateAsync(request);

        Assert.AreEqual(first.RecordFingerprint, second.RecordFingerprint);
    }

    [TestMethod]
    public async Task DeterministicOrderingOfLineageObservations()
    {
        var request = Request();
        var early = PriorAttempt("attempt:e09:a", AssessedAtUtc.AddMinutes(-3));
        var late = PriorAttempt("attempt:e09:b", AssessedAtUtc.AddMinutes(-2));
        var first = await EvaluateAsync(
            request,
            new FakeSafeRetryAttemptReadStore
            {
                Result = SafeRetryLineageReadResult.Empty(request.RetryLineageRef) with
                {
                    PriorAttempts = [late, early]
                }
            });
        var second = await EvaluateAsync(
            request,
            new FakeSafeRetryAttemptReadStore
            {
                Result = SafeRetryLineageReadResult.Empty(request.RetryLineageRef) with
                {
                    PriorAttempts = [early, late]
                }
            });

        Assert.AreEqual(first.Decision, second.Decision);
        Assert.AreEqual(first.RecordFingerprint, second.RecordFingerprint);
    }

    [TestMethod]
    public void ReceiptDocContainsReviewLine()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        StringAssert.Contains(receipt, "A failed attempt may be explainable. It is not permission to try again.");
    }

    [TestMethod]
    public void ReceiptDocContainsKilljoyLine()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        StringAssert.Contains(receipt, "Retry classification is not retry authority.");
    }

    private static SafeRetryAssessmentRequest InvalidRequest(string caseName) =>
        caseName switch
        {
            "tenant" => Request() with { TenantId = "" },
            "project" => Request() with { ProjectId = "" },
            "operation" => Request() with { OperationId = "" },
            "operation-invalid" => Request() with { OperationId = "run_123" },
            "correlation" => Request() with { CorrelationId = "" },
            "correlation-invalid" => Request() with { CorrelationId = "corr_bad" },
            "surface" => Request() with { MutationSurface = MutationLeaseSurfaceKind.Unknown },
            "target" => Request() with { MutationTargetRef = "" },
            "failed-attempt" => Request() with { FailedAttemptRef = "" },
            "previous-key" => Request() with { PreviousIdempotencyKeyRef = "" },
            "previous-fingerprint" => Request() with { PreviousIdempotencyFingerprint = "" },
            "proposed-attempt" => Request() with { ProposedRetryAttemptRef = "" },
            "proposed-key" => Request() with { ProposedRetryIdempotencyKeyRef = "" },
            "proposed-fingerprint" => Request() with { ProposedRetryIdempotencyFingerprint = "" },
            "lineage" => Request() with { RetryLineageRef = "" },
            "guard-ref" => Request() with { CurrentGuardDecisionRef = "" },
            "assessed" => Request() with { AssessedAtUtc = default },
            "assessed-non-utc" => Request() with { AssessedAtUtc = new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.FromHours(1)) },
            "now" => Request() with { NowUtc = default },
            "now-non-utc" => Request() with { NowUtc = new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.FromHours(1)) },
            "reason" => Request() with { ReasonCode = "" },
            "source" => Request() with { Source = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static SafeRetryAssessmentRequest MissingReceiptRequest(string caseName) =>
        caseName switch
        {
            "failure-receipt" => Request() with { FailureReceiptRef = "" },
            "terminal-outcome" => Request() with { TerminalOutcomeRef = "" },
            "post-state" => Request() with { PostStateObservationRef = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static async Task<SafeRetryAssessmentDecision> EvaluateAsync(
        SafeRetryAssessmentRequest request,
        FakeSafeRetryAttemptReadStore? store = null)
    {
        store ??= new FakeSafeRetryAttemptReadStore();
        var service = new SafeRetryContractService(store);

        return await service.EvaluateAsync(request, CancellationToken.None);
    }

    private static SafeRetryAssessmentRequest Request() =>
        new()
        {
            TenantId = "tenant-e09",
            ProjectId = "project-e09",
            OperationId = "op_000000000000e009",
            CorrelationId = "corr_000000000000e009",
            MutationSurface = MutationLeaseSurfaceKind.SourceApply,
            MutationTargetRef = "patch-package:e09",
            FailedAttemptRef = "attempt:e09:failed",
            FailedAttemptOutcome = SafeRetryAttemptOutcome.Failed,
            FailureClass = SafeRetryFailureClass.PreMutationTimeout,
            MutationBoundaryState = SafeRetryMutationBoundaryState.NotStarted,
            FailureReceiptRef = "failure-receipt:e09",
            TerminalOutcomeRef = "terminal-outcome:e09",
            PostStateObservationRef = "post-state-observation:e09",
            PreviousIdempotencyKeyRef = "idempotency-key:e09",
            PreviousIdempotencyFingerprint = "idempotency-fingerprint:e09",
            ProposedRetryAttemptRef = "attempt:e09:retry",
            ProposedRetryIdempotencyKeyRef = "idempotency-key:e09",
            ProposedRetryIdempotencyFingerprint = "idempotency-fingerprint:e09",
            RetryLineageRef = "retry-lineage:e09",
            PriorRetryCount = 0,
            MaxRetryCount = 3,
            CurrentGuardDecision = SafeRetryCurrentGuardState.AllowedToProceedToNextGate,
            CurrentGuardDecisionRef = "guard-decision:e09",
            AssessedAtUtc = AssessedAtUtc,
            NowUtc = NowUtc,
            ReasonCode = "pre-mutation-timeout",
            Source = "safe-retry-contract"
        };

    private static SafeRetryPriorAttemptMetadata PriorAttempt(
        string attemptRef = "attempt:e09:previous",
        DateTimeOffset? observedAtUtc = null) =>
        new()
        {
            TenantId = "tenant-e09",
            ProjectId = "project-e09",
            MutationSurface = MutationLeaseSurfaceKind.SourceApply,
            MutationTargetRef = "patch-package:e09",
            AttemptRef = attemptRef,
            RetryLineageRef = "retry-lineage:e09",
            Outcome = SafeRetryAttemptOutcome.Failed,
            FailureClass = SafeRetryFailureClass.PreMutationTimeout,
            ObservedAtUtc = observedAtUtc ?? AssessedAtUtc.AddMinutes(-5)
        };

    private static SafeRetryLineageReadResult LineageWithObservedCount(int observedPriorRetryCount) =>
        SafeRetryLineageReadResult.Empty("retry-lineage:e09") with
        {
            ObservedPriorRetryCount = observedPriorRetryCount,
            PriorAttempts = Enumerable.Range(0, observedPriorRetryCount)
                .Select(index => PriorAttempt($"attempt:e09:previous-{index}", AssessedAtUtc.AddMinutes(-10 + index)))
                .ToArray()
        };

    private static void AssertRequiresFreshGates(SafeRetryAssessmentDecision decision)
    {
        Assert.IsTrue(decision.RequiresFreshAuthority);
        Assert.IsTrue(decision.RequiresFreshValidation);
        Assert.IsTrue(decision.RequiresFreshConcurrentGuard);
        Assert.IsTrue(decision.RequiresFreshPostStateObservation);
    }

    private static void AssertNoAuthority(SafeRetryAssessmentDecision decision)
    {
        AssertRequiresFreshGates(decision);
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "safe retry contract is not mutation authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "safe retry contract is not retry execution");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "safe retry contract is not resume authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "safe retry contract is not recovery authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "safe retry contract is not rollback authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "safe retry contract is not approval");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "safe retry contract is not policy satisfaction");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "safe retry contract is not workflow continuation");
    }

    private static void AssertForbiddenFragmentsAbsent(string source, IEnumerable<string> forbiddenFragments)
    {
        foreach (var forbidden in forbiddenFragments)
        {
            Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), $"Unexpected fragment: {forbidden}");
        }
    }

    private static string CoreSource()
    {
        var root = RepoRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Governance", "SafeRetryContractModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "SafeRetryContractValidator.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "ISafeRetryAttemptReadStore.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "SafeRetryContractService.cs")
        };

        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "E09_SAFE_RETRY_CONTRACT.md");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }

    private sealed class FakeSafeRetryAttemptReadStore : ISafeRetryAttemptReadStore
    {
        public int ReadCount { get; private set; }
        public SafeRetryLineageReadResult? Result { get; init; } = SafeRetryLineageReadResult.Empty("retry-lineage:e09");

        public Task<SafeRetryLineageReadResult?> FindRetryLineageAsync(
            SafeRetryAssessmentRequest request,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(Result);
        }
    }
}
