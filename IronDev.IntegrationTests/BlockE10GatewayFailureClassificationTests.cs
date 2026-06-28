using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE10GatewayFailureClassificationTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ClassifiedAtUtc = ObservedAtUtc.AddMinutes(1);

    [TestMethod]
    public void ValidPreMutationFailureClassifiesOnlyForRetryAssessment()
    {
        var decision = Classify(Request());

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.Classified, decision.Decision);
        Assert.AreEqual(GatewayFailureClassificationBlockKind.None, decision.BlockKind);
        Assert.AreEqual(GatewayFailureRoutingHint.MayProceedToRetryAssessment, decision.RoutingHint);
        Assert.AreEqual("GatewayFailureClassified:PreMutationTimeout", decision.Reason);
        AssertRequiresFreshGates(decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ValidDecisionPreservesMatchedEvidenceRefs()
    {
        var request = Request();
        var decision = Classify(request);

        Assert.AreEqual(request.FailureEvidenceRef, decision.MatchedFailureEvidenceRef);
        Assert.AreEqual(request.FailureReceiptRef, decision.MatchedFailureReceiptRef);
        Assert.AreEqual(request.PostStateObservationRef, decision.MatchedPostStateObservationRef);
        Assert.AreEqual(request.ConcurrentGuardDecisionRef, decision.MatchedConcurrentGuardDecisionRef);
        Assert.AreEqual(request.LeaseObservationRef, decision.MatchedLeaseObservationRef);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("tenant", "GatewayFailureTenantIdRequired")]
    [DataRow("tenant-invalid", "GatewayFailureTenantIdInvalid")]
    [DataRow("project", "GatewayFailureProjectIdRequired")]
    [DataRow("project-invalid", "GatewayFailureProjectIdInvalid")]
    [DataRow("operation", "GatewayFailureOperationIdRequired")]
    [DataRow("operation-invalid", "GatewayFailureOperationIdInvalid")]
    [DataRow("correlation", "GatewayFailureCorrelationIdRequired")]
    [DataRow("correlation-invalid", "GatewayFailureCorrelationIdInvalid")]
    [DataRow("surface", "GatewayFailureMutationSurfaceRequired")]
    [DataRow("attempt", "GatewayFailureAttemptRefRequired")]
    [DataRow("gateway", "GatewayFailureGatewayRefRequired")]
    [DataRow("failure", "GatewayFailureFailureRefRequired")]
    [DataRow("observed", "GatewayFailureObservedAtUtcRequired")]
    [DataRow("observed-non-utc", "GatewayFailureObservedAtUtcMustBeUtc")]
    [DataRow("classified", "GatewayFailureClassifiedAtUtcRequired")]
    [DataRow("classified-non-utc", "GatewayFailureClassifiedAtUtcMustBeUtc")]
    [DataRow("classified-before-observed", "GatewayFailureClassifiedAtUtcBeforeObservedAtUtc")]
    [DataRow("classifier", "GatewayFailureClassifierVersionRequired")]
    [DataRow("reason", "GatewayFailureReasonCodeRequired")]
    [DataRow("source", "GatewayFailureSourceRequired")]
    public void InvalidRequestFailsClosed(string caseName, string expectedReason)
    {
        var decision = Classify(InvalidRequest(caseName));

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual(GatewayFailureClassificationBlockKind.InvalidRequest, decision.BlockKind);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void MissingFailureEvidenceOrReceiptBlocks()
    {
        var decision = Classify(Request() with { FailureEvidenceRef = "", FailureReceiptRef = "" });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.BlockedByMissingEvidence, decision.Decision);
        Assert.AreEqual(GatewayFailureClassificationBlockKind.MissingEvidence, decision.BlockKind);
        Assert.AreEqual("GatewayFailureEvidenceOrReceiptRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownFailureClassBlocks()
    {
        var decision = Classify(Request() with { FailureClass = GatewayFailureClass.Unknown });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.BlockedByUnknownFailureClass, decision.Decision);
        Assert.AreEqual(GatewayFailureClassificationBlockKind.UnknownFailureClass, decision.BlockKind);
        Assert.AreEqual(GatewayFailureRoutingHint.BlockedUntilClassified, decision.RoutingHint);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownFailurePhaseBlocks()
    {
        var decision = Classify(Request() with { FailurePhase = GatewayFailurePhase.Unknown });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.BlockedByUnknownFailurePhase, decision.Decision);
        Assert.AreEqual(GatewayFailureClassificationBlockKind.UnknownFailurePhase, decision.BlockKind);
        Assert.AreEqual(GatewayFailureRoutingHint.BlockedUntilClassified, decision.RoutingHint);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownMutationBoundaryBlocksToPostStateObservation()
    {
        var decision = Classify(Request() with { MutationBoundaryState = GatewayFailureMutationBoundaryState.Unknown });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.BlockedByUnknownMutationBoundary, decision.Decision);
        Assert.AreEqual(GatewayFailureClassificationBlockKind.UnknownMutationBoundary, decision.BlockKind);
        Assert.AreEqual(GatewayFailureRoutingHint.RequiresPostStateObservation, decision.RoutingHint);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(GatewayFailureMutationBoundaryState.MutationMayHaveStarted)]
    [DataRow(GatewayFailureMutationBoundaryState.MutationStarted)]
    [DataRow(GatewayFailureMutationBoundaryState.MutationPartiallyObserved)]
    [DataRow(GatewayFailureMutationBoundaryState.MutationCompleted)]
    public void MutationMayHaveStartedNeverRoutesToRetryAssessment(
        GatewayFailureMutationBoundaryState boundaryState)
    {
        var decision = Classify(Request() with
        {
            MutationBoundaryState = boundaryState,
            FailureClass = GatewayFailureClass.PreMutationTimeout,
            FailurePhase = GatewayFailurePhase.ProviderAcceptedBoundaryUnknown
        });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.Classified, decision.Decision);
        Assert.AreEqual(GatewayFailureRoutingHint.RequiresPostStateObservation, decision.RoutingHint);
        Assert.AreNotEqual(GatewayFailureRoutingHint.MayProceedToRetryAssessment, decision.RoutingHint);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(GatewayFailurePhase.ProviderRejectedAfterMutationStarted, GatewayFailureRoutingHint.RequiresPostStateObservation)]
    [DataRow(GatewayFailurePhase.ProviderAcceptedBoundaryUnknown, GatewayFailureRoutingHint.RequiresPostStateObservation)]
    [DataRow(GatewayFailurePhase.ManualCancellation, GatewayFailureRoutingHint.RequiresManualTriage)]
    [DataRow(GatewayFailurePhase.PostStateObservation, GatewayFailureRoutingHint.RequiresPostStateObservation)]
    public void NonPreMutationPhaseWithPreMutationClassDoesNotRouteToRetry(
        GatewayFailurePhase failurePhase,
        GatewayFailureRoutingHint expectedRouting)
    {
        var decision = Classify(Request() with
        {
            FailurePhase = failurePhase,
            FailureClass = GatewayFailureClass.PreMutationTimeout,
            MutationBoundaryState = GatewayFailureMutationBoundaryState.MutationNotStarted,
            PostStateObservationRef = "post-state:e10"
        });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.Classified, decision.Decision);
        Assert.AreEqual(expectedRouting, decision.RoutingHint);
        Assert.AreNotEqual(GatewayFailureRoutingHint.MayProceedToRetryAssessment, decision.RoutingHint);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(GatewayFailureClass.AuthorityBoundaryViolation, GatewayFailureRoutingHint.RequiresFreshAuthority)]
    [DataRow(GatewayFailureClass.ApprovalMissing, GatewayFailureRoutingHint.RequiresFreshAuthority)]
    [DataRow(GatewayFailureClass.ApprovalDenied, GatewayFailureRoutingHint.RequiresFreshAuthority)]
    [DataRow(GatewayFailureClass.PolicyDenied, GatewayFailureRoutingHint.RequiresFreshAuthority)]
    [DataRow(GatewayFailureClass.ValidationFailed, GatewayFailureRoutingHint.RequiresFreshValidation)]
    [DataRow(GatewayFailureClass.FreshnessExpired, GatewayFailureRoutingHint.RequiresFreshValidation)]
    [DataRow(GatewayFailureClass.StaleValidation, GatewayFailureRoutingHint.RequiresFreshValidation)]
    [DataRow(GatewayFailureClass.StalePatch, GatewayFailureRoutingHint.RequiresFreshValidation)]
    [DataRow(GatewayFailureClass.PatchBaseMoved, GatewayFailureRoutingHint.RequiresFreshValidation)]
    public void AuthorityAndValidationFailuresRouteToFreshGateAssessment(
        GatewayFailureClass failureClass,
        GatewayFailureRoutingHint expectedRouting)
    {
        var decision = Classify(Request() with { FailureClass = failureClass, FailurePhase = GatewayFailurePhase.AuthorityEvaluation });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.Classified, decision.Decision);
        Assert.AreEqual(expectedRouting, decision.RoutingHint);
        Assert.AreNotEqual(GatewayFailureRoutingHint.MayProceedToRetryAssessment, decision.RoutingHint);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(GatewayFailureClass.MutationBoundaryUnknown)]
    [DataRow(GatewayFailureClass.MutationMayHaveStarted)]
    [DataRow(GatewayFailureClass.PartialMutationObserved)]
    [DataRow(GatewayFailureClass.ProviderAcceptedButOutcomeUnknown)]
    [DataRow(GatewayFailureClass.ProviderRejectedAfterMutationStarted)]
    [DataRow(GatewayFailureClass.PostStateUnknown)]
    [DataRow(GatewayFailureClass.SourceStateUnknown)]
    public void UnknownOrStartedMutationFailureClassesRequirePostStateObservation(
        GatewayFailureClass failureClass)
    {
        var decision = Classify(Request() with
        {
            FailureClass = failureClass,
            FailurePhase = GatewayFailurePhase.PostStateObservation
        });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.Classified, decision.Decision);
        Assert.AreEqual(GatewayFailureRoutingHint.RequiresPostStateObservation, decision.RoutingHint);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(GatewayFailureClass.ReceiptConflict, GatewayFailureRoutingHint.RequiresReceiptConflictResolution)]
    [DataRow(GatewayFailureClass.StatusProjectionFailed, GatewayFailureRoutingHint.RequiresReadModelRebuild)]
    [DataRow(GatewayFailureClass.ReadModelStale, GatewayFailureRoutingHint.RequiresReadModelRebuild)]
    [DataRow(GatewayFailureClass.ReadModelUnavailable, GatewayFailureRoutingHint.RequiresReadModelRebuild)]
    [DataRow(GatewayFailureClass.RollbackPlanUnavailable, GatewayFailureRoutingHint.RequiresRollbackAssessment)]
    [DataRow(GatewayFailureClass.RecoveryPlanUnavailable, GatewayFailureRoutingHint.RequiresRecoveryAssessment)]
    [DataRow(GatewayFailureClass.InterruptedRun, GatewayFailureRoutingHint.RequiresRecoveryAssessment)]
    [DataRow(GatewayFailureClass.ManualCancellation, GatewayFailureRoutingHint.RequiresManualTriage)]
    [DataRow(GatewayFailureClass.OperatorAbort, GatewayFailureRoutingHint.RequiresManualTriage)]
    public void NonRetryFailuresRouteToTheirNextAssessmentOnly(
        GatewayFailureClass failureClass,
        GatewayFailureRoutingHint expectedRouting)
    {
        var decision = Classify(Request() with
        {
            FailureClass = failureClass,
            FailurePhase = GatewayFailurePhase.StatusProjection,
            MutationBoundaryState = GatewayFailureMutationBoundaryState.ObservationOnly
        });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.Classified, decision.Decision);
        Assert.AreEqual(expectedRouting, decision.RoutingHint);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(GatewayFailureClass.PreMutationInfrastructureFailure)]
    [DataRow(GatewayFailureClass.PreMutationDependencyUnavailable)]
    [DataRow(GatewayFailureClass.PreMutationTimeout)]
    [DataRow(GatewayFailureClass.PreMutationLeaseUnavailable)]
    [DataRow(GatewayFailureClass.PreMutationConcurrentGuardBlocked)]
    [DataRow(GatewayFailureClass.RateLimited)]
    [DataRow(GatewayFailureClass.ExternalProviderUnavailable)]
    [DataRow(GatewayFailureClass.ExternalProviderTimeout)]
    public void PreMutationInfrastructureFailuresMayProceedOnlyToRetryAssessment(
        GatewayFailureClass failureClass)
    {
        var decision = Classify(Request() with
        {
            FailureClass = failureClass,
            FailurePhase = failureClass == GatewayFailureClass.PreMutationLeaseUnavailable
                ? GatewayFailurePhase.LeaseObservation
                : GatewayFailurePhase.PreMutationDependencyCheck,
            MutationBoundaryState = GatewayFailureMutationBoundaryState.MutationNotStarted
        });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.Classified, decision.Decision);
        Assert.AreEqual(GatewayFailureRoutingHint.MayProceedToRetryAssessment, decision.RoutingHint);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void IdempotencyConflictRequiresIdempotencyEvidence()
    {
        var decision = Classify(Request() with
        {
            FailureClass = GatewayFailureClass.IdempotencyConflict,
            FailurePhase = GatewayFailurePhase.IdempotencyEvaluation,
            IdempotencyKeyRef = "",
            IdempotencyFingerprint = ""
        });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.BlockedByMissingIdempotencyEvidence, decision.Decision);
        Assert.AreEqual(GatewayFailureClassificationBlockKind.MissingIdempotencyEvidence, decision.BlockKind);
        Assert.AreEqual("GatewayFailureIdempotencyKeyRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void IdempotencyConflictRequiresFingerprintEvidence()
    {
        var decision = Classify(Request() with
        {
            FailureClass = GatewayFailureClass.IdempotencyConflict,
            FailurePhase = GatewayFailurePhase.IdempotencyEvaluation,
            IdempotencyFingerprint = ""
        });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.BlockedByMissingIdempotencyEvidence, decision.Decision);
        Assert.AreEqual("GatewayFailureIdempotencyFingerprintRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ConcurrentGuardFailureRequiresGuardEvidence()
    {
        var decision = Classify(Request() with
        {
            FailureClass = GatewayFailureClass.PreMutationConcurrentGuardBlocked,
            FailurePhase = GatewayFailurePhase.ConcurrentMutationGuard,
            ConcurrentGuardDecisionRef = ""
        });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.BlockedByMissingConcurrentGuardEvidence, decision.Decision);
        Assert.AreEqual(GatewayFailureClassificationBlockKind.MissingConcurrentGuardEvidence, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void LeaseFailureRequiresLeaseEvidence()
    {
        var decision = Classify(Request() with
        {
            FailureClass = GatewayFailureClass.PreMutationLeaseUnavailable,
            FailurePhase = GatewayFailurePhase.LeaseObservation,
            LeaseObservationRef = ""
        });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.BlockedByMissingLeaseEvidence, decision.Decision);
        Assert.AreEqual(GatewayFailureClassificationBlockKind.MissingLeaseEvidence, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(GatewayFailureClass.PostStateUnknown)]
    [DataRow(GatewayFailureClass.ProviderAcceptedButOutcomeUnknown)]
    [DataRow(GatewayFailureClass.ProviderRejectedAfterMutationStarted)]
    [DataRow(GatewayFailureClass.MutationMayHaveStarted)]
    [DataRow(GatewayFailureClass.PartialMutationObserved)]
    public void PostStateFailuresRequirePostStateEvidence(GatewayFailureClass failureClass)
    {
        var decision = Classify(Request() with
        {
            FailureClass = failureClass,
            FailurePhase = GatewayFailurePhase.PostStateObservation,
            PostStateObservationRef = ""
        });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.BlockedByMissingPostStateObservation, decision.Decision);
        Assert.AreEqual(GatewayFailureClassificationBlockKind.MissingPostStateObservation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("raw-patch")]
    [DataRow("raw-diff")]
    [DataRow("raw-source")]
    [DataRow("raw-commit-body")]
    [DataRow("provider-response")]
    [DataRow("private-reasoning")]
    [DataRow("credential")]
    [DataRow("authority")]
    public void UnsafePayloadMarkersBlockWithoutClassification(string caseName)
    {
        var request = Request() with { FailureEvidenceRef = UnsafeReference(caseName) };
        var decision = Classify(request);

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.AreEqual(GatewayFailureClassificationBlockKind.UnsafePayload, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("patch-package:e10")]
    [DataRow("merge-target:e10")]
    [DataRow("release-candidate:e10")]
    [DataRow("deploy-target:e10")]
    [DataRow("rollback-target:e10")]
    public void ValidDomainRefsAreNotRejectedAsUnsafe(string evidenceRef)
    {
        var decision = Classify(Request() with { FailureEvidenceRef = evidenceRef });

        Assert.AreEqual(GatewayFailureClassificationDecisionKind.Classified, decision.Decision);
        Assert.AreEqual(GatewayFailureRoutingHint.MayProceedToRetryAssessment, decision.RoutingHint);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void FingerprintIsStableForSameInput()
    {
        var first = Classify(Request());
        var second = Classify(Request());

        Assert.AreEqual(first.RecordFingerprint, second.RecordFingerprint);
        AssertNoAuthority(first);
    }

    [TestMethod]
    public void FingerprintChangesWhenClassChanges()
    {
        var first = Classify(Request());
        var second = Classify(Request() with { FailureClass = GatewayFailureClass.ExternalProviderTimeout });

        Assert.AreNotEqual(first.RecordFingerprint, second.RecordFingerprint);
        AssertNoAuthority(second);
    }

    [TestMethod]
    public void ContractDoesNotExposeExecutionAuthorityFields()
    {
        var fieldNames = typeof(GatewayFailureClassificationDecision)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        CollectionAssert.DoesNotContain(fieldNames, "CanRetry");
        CollectionAssert.DoesNotContain(fieldNames, "CanRecover");
        CollectionAssert.DoesNotContain(fieldNames, "CanRollback");
        CollectionAssert.DoesNotContain(fieldNames, "CanResume");
        CollectionAssert.DoesNotContain(fieldNames, "CanMutate");
        CollectionAssert.DoesNotContain(fieldNames, "AllowedToRetry");
        CollectionAssert.DoesNotContain(fieldNames, "RetryAuthorized");
    }

    [TestMethod]
    public void StaticScan_E10CoreAddsNoExecutorOrProviderMutationPath()
    {
        var source = E10CoreSource();

        AssertDoesNotContain(source, "IControlled");
        AssertDoesNotContain(source, "Executor");
        AssertDoesNotContain(source, "RunProcessAsync");
        AssertDoesNotContain(source, "ProcessStartInfo");
        AssertDoesNotContain(source, "Process.Start");
        AssertDoesNotContain(source, "HttpClient");
        AssertDoesNotContain(source, "GitHubClient");
        AssertDoesNotContain(source, "Octokit");
        AssertDoesNotContain(source, "WorkflowContinuationExecutor");
    }

    [TestMethod]
    public void StaticScan_E10CoreAddsNoRepositoryMutationOrRawPayloadReadPath()
    {
        var source = E10CoreSource();

        AssertDoesNotContain(source, "File.Write");
        AssertDoesNotContain(source, "File.ReadAllText");
        AssertDoesNotContain(source, "Directory.");
        AssertDoesNotContain(source, "git ");
        AssertDoesNotContain(source, "gh ");
        AssertDoesNotContain(source, "rawSource");
        AssertDoesNotContain(source, "rawPayload");
    }

    [TestMethod]
    public void ReceiptRecordsBoundaryReviewLine()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "E10_GATEWAY_FAILURE_CLASSIFICATION_STANDARD.md"));

        StringAssert.Contains(receipt, "A failure class explains what happened. It does not authorize what happens next.");
        StringAssert.Contains(receipt, "Failure classification is not retry, recovery, rollback, resume, or mutation authority.");
    }

    private static GatewayFailureClassificationDecision Classify(GatewayFailureClassificationRequest request) =>
        new GatewayFailureClassificationService().Classify(request);

    private static GatewayFailureClassificationRequest Request() =>
        new()
        {
            TenantId = "tenant-e10",
            ProjectId = "project-e10",
            OperationId = "op_000000000000e010",
            CorrelationId = "corr_0000000000e01000",
            MutationSurface = MutationLeaseSurfaceKind.SourceApply,
            AttemptRef = "attempt:e10",
            GatewayRef = "gateway:e10",
            FailureRef = "failure:e10",
            FailurePhase = GatewayFailurePhase.PreMutationDependencyCheck,
            FailureClass = GatewayFailureClass.PreMutationTimeout,
            MutationBoundaryState = GatewayFailureMutationBoundaryState.MutationNotStarted,
            FailureEvidenceRef = "failure-evidence:e10",
            FailureReceiptRef = "failure-receipt:e10",
            PostStateObservationRef = "post-state:e10",
            ConcurrentGuardDecisionRef = "concurrent-guard:e10",
            LeaseObservationRef = "lease-observation:e10",
            IdempotencyKeyRef = "idempotency-key:e10",
            IdempotencyFingerprint = "idempotency-fingerprint:e10",
            ObservedAtUtc = ObservedAtUtc,
            ClassifiedAtUtc = ClassifiedAtUtc,
            ClassifierVersion = "gateway-failure-v1",
            ReasonCode = "pre-mutation-timeout",
            Source = "gateway-failure-classifier"
        };

    private static GatewayFailureClassificationRequest InvalidRequest(string caseName) =>
        caseName switch
        {
            "tenant" => Request() with { TenantId = "" },
            "tenant-invalid" => Request() with { TenantId = "tenant with whitespace" },
            "project" => Request() with { ProjectId = "" },
            "project-invalid" => Request() with { ProjectId = "project with whitespace" },
            "operation" => Request() with { OperationId = "" },
            "operation-invalid" => Request() with { OperationId = "run:e10" },
            "correlation" => Request() with { CorrelationId = "" },
            "correlation-invalid" => Request() with { CorrelationId = "op_000000000000e010" },
            "surface" => Request() with { MutationSurface = MutationLeaseSurfaceKind.Unknown },
            "attempt" => Request() with { AttemptRef = "" },
            "gateway" => Request() with { GatewayRef = "" },
            "failure" => Request() with { FailureRef = "" },
            "observed" => Request() with { ObservedAtUtc = default },
            "observed-non-utc" => Request() with { ObservedAtUtc = new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.FromHours(12)) },
            "classified" => Request() with { ClassifiedAtUtc = default },
            "classified-non-utc" => Request() with { ClassifiedAtUtc = new DateTimeOffset(2026, 6, 25, 0, 1, 0, TimeSpan.FromHours(12)) },
            "classified-before-observed" => Request() with { ClassifiedAtUtc = ObservedAtUtc.AddSeconds(-1) },
            "classifier" => Request() with { ClassifierVersion = "" },
            "reason" => Request() with { ReasonCode = "" },
            "source" => Request() with { Source = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static string UnsafeReference(string caseName) =>
        caseName switch
        {
            "raw-patch" => "raw patch",
            "raw-diff" => "raw diff",
            "raw-source" => "raw source",
            "raw-commit-body" => "raw commit body",
            "provider-response" => "provider response body",
            "private-reasoning" => "private reasoning",
            "credential" => string.Concat("to", "ken", "=", "fake"),
            "authority" => "safe to mutate",
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static void AssertRequiresFreshGates(GatewayFailureClassificationDecision decision)
    {
        Assert.IsTrue(decision.RequiresFreshAuthority);
        Assert.IsTrue(decision.RequiresFreshValidation);
        Assert.IsTrue(decision.RequiresFreshConcurrentGuard);
        Assert.IsTrue(decision.RequiresFreshPostStateObservation);
        Assert.IsTrue(decision.RequiresHumanReview);
    }

    private static void AssertNoAuthority(GatewayFailureClassificationDecision decision)
    {
        AssertRequiresFreshGates(decision);
        CollectionAssert.Contains(decision.Warnings.ToList(), "gateway failure classification is read only");
        CollectionAssert.Contains(decision.Warnings.ToList(), "failure classification is not retry authority");
        CollectionAssert.Contains(decision.Warnings.ToList(), "routing hint is not executor eligibility");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "gateway failure classification is not mutation execution");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "gateway failure classification is not retry execution");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "gateway failure classification is not recovery execution");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "gateway failure classification is not rollback execution");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "gateway failure classification is not workflow continuation");
    }

    private static string E10CoreSource()
    {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "GatewayFailureClassification*.cs");
        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static void AssertDoesNotContain(string source, string forbidden) =>
        Assert.IsFalse(
            source.Contains(forbidden, StringComparison.Ordinal),
            $"Unexpected forbidden marker found in E10 source: {forbidden}");
}
