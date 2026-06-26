using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE06IdempotencyKeyContractTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecordedAtUtc = ObservedAtUtc.AddMinutes(1);
    private static readonly DateTimeOffset ExpiresAtUtc = ObservedAtUtc.AddMinutes(30);

    [TestMethod]
    public void FreshNewIdempotencyKeyMayProceedToNextAuthorityGateOnly()
    {
        var decision = Evaluate(Request());

        Assert.AreEqual(IdempotencyKeyDecisionKind.MayProceedToNextAuthorityGate, decision.Decision);
        Assert.AreEqual(IdempotencyKeyBlockKind.None, decision.BlockKind);
        AssertRequiresAllGates(decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(nameof(IdempotencyKeyContractDecision.RequiresFreshAuthority))]
    [DataRow(nameof(IdempotencyKeyContractDecision.RequiresAcceptedApproval))]
    [DataRow(nameof(IdempotencyKeyContractDecision.RequiresPolicySatisfaction))]
    [DataRow(nameof(IdempotencyKeyContractDecision.RequiresFreshValidation))]
    [DataRow(nameof(IdempotencyKeyContractDecision.RequiresConcurrentGuard))]
    [DataRow(nameof(IdempotencyKeyContractDecision.RequiresDirtyWorktreeGuard))]
    [DataRow(nameof(IdempotencyKeyContractDecision.RequiresMovedBaseGuard))]
    [DataRow(nameof(IdempotencyKeyContractDecision.RequiresStaleValidationGuard))]
    [DataRow(nameof(IdempotencyKeyContractDecision.RequiresBranchRemoteHeadVerification))]
    [DataRow(nameof(IdempotencyKeyContractDecision.RequiresFreshPostStateObservation))]
    [DataRow(nameof(IdempotencyKeyContractDecision.RequiresHumanReview))]
    public void PositiveDecisionStillRequiresFreshGates(string propertyName)
    {
        var decision = Evaluate(Request());
        var value = typeof(IdempotencyKeyContractDecision).GetProperty(propertyName)?.GetValue(decision);

        Assert.AreEqual(true, value);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("tenant", "IdempotencyTenantIdRequired")]
    [DataRow("project", "IdempotencyProjectIdRequired")]
    [DataRow("operation", "IdempotencyOperationIdRequired")]
    [DataRow("operation-invalid", "IdempotencyOperationIdInvalid")]
    [DataRow("correlation", "IdempotencyCorrelationIdRequired")]
    [DataRow("correlation-invalid", "IdempotencyCorrelationIdInvalid")]
    [DataRow("surface", "IdempotencyMutationSurfaceRequired")]
    [DataRow("attempt", "IdempotencyAttemptRefRequired")]
    [DataRow("target", "IdempotencyTargetRefRequired")]
    [DataRow("request", "IdempotencyRequestRefRequired")]
    [DataRow("scope", "IdempotencyScopeRefRequired")]
    [DataRow("request-fingerprint", "IdempotencyRequestFingerprintRequired")]
    [DataRow("observed-non-utc", "IdempotencyObservedAtUtcMustBeUtc")]
    [DataRow("recorded-non-utc", "IdempotencyRecordedAtUtcMustBeUtc")]
    [DataRow("recorded-before-observed", "IdempotencyRecordedAtUtcBeforeObservedAtUtc")]
    [DataRow("contract-version", "IdempotencyContractVersionRequired")]
    [DataRow("reason", "IdempotencyReasonCodeRequired")]
    [DataRow("source", "IdempotencySourceRequired")]
    public void InvalidRequestFailsClosed(string caseName, string expectedReason)
    {
        var decision = Evaluate(InvalidRequest(caseName));

        Assert.AreEqual(IdempotencyKeyDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual(IdempotencyKeyBlockKind.InvalidRequest, decision.BlockKind);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void NullRequestFailsClosed()
    {
        var decision = new IdempotencyKeyContractService().Evaluate(null);

        Assert.AreEqual(IdempotencyKeyDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual(IdempotencyKeyBlockKind.InvalidRequest, decision.BlockKind);
        Assert.AreEqual("IdempotencyKeyContractRequestRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void MissingIdempotencyKeyBlocks()
    {
        var decision = Evaluate(Request() with { IdempotencyKey = "" });

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByMissingIdempotencyKey, decision.Decision);
        Assert.AreEqual(IdempotencyKeyBlockKind.MissingKey, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("malformed")]
    [DataRow("whitespace")]
    [DataRow("overlong")]
    public void MalformedIdempotencyKeyBlocks(string caseName)
    {
        var decision = Evaluate(Request() with { IdempotencyKey = MalformedKey(caseName) });

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByMalformedIdempotencyKey, decision.Decision);
        Assert.AreEqual(IdempotencyKeyBlockKind.MalformedKey, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("authority")]
    [DataRow("raw-patch")]
    [DataRow("raw-diff")]
    [DataRow("credential")]
    [DataRow("private-key")]
    [DataRow("json-payload")]
    public void UnsafePayloadInKeyBlocksAndDoesNotEchoRawValue(string caseName)
    {
        var unsafeKey = UnsafeText(caseName);
        var decision = Evaluate(Request() with { IdempotencyKey = unsafeKey });

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.AreEqual(IdempotencyKeyBlockKind.UnsafePayload, decision.BlockKind);
        Assert.AreEqual("[unsafe-rejected]", decision.MatchedIdempotencyKey);
        Assert.IsFalse(decision.RecordFingerprint.Contains(unsafeKey, StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(decision.RecordFingerprint.Contains("[unsafe]", StringComparison.Ordinal));
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("subject")]
    [DataRow("evidence-kind")]
    [DataRow("trust-level")]
    [DataRow("freshness")]
    [DataRow("prior-state")]
    public void UnknownDimensionsBlock(string caseName)
    {
        var decision = Evaluate(UnknownDimensionRequest(caseName));

        Assert.AreNotEqual(IdempotencyKeyDecisionKind.MayProceedToNextAuthorityGate, decision.Decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("observation", "IdempotencyObservationRefRequired")]
    [DataRow("expected-request", "IdempotencyExpectedRequestFingerprintRequired")]
    [DataRow("observed-request", "IdempotencyObservedRequestFingerprintRequired")]
    [DataRow("authority-fingerprint", "IdempotencyAuthorityFingerprintRequired")]
    [DataRow("expected-authority", "IdempotencyExpectedAuthorityFingerprintRequired")]
    [DataRow("observed-authority", "IdempotencyObservedAuthorityFingerprintRequired")]
    [DataRow("target-fingerprint", "IdempotencyTargetFingerprintRequired")]
    [DataRow("expected-target", "IdempotencyExpectedTargetFingerprintRequired")]
    [DataRow("observed-target", "IdempotencyObservedTargetFingerprintRequired")]
    [DataRow("effect-fingerprint", "IdempotencyEffectFingerprintRequired")]
    [DataRow("expected-effect", "IdempotencyExpectedEffectFingerprintRequired")]
    [DataRow("observed-effect", "IdempotencyObservedEffectFingerprintRequired")]
    [DataRow("authority-receipt", "IdempotencyAuthorityReceiptRefRequired")]
    [DataRow("policy", "IdempotencyPolicySatisfactionRefRequired")]
    [DataRow("validation", "IdempotencyValidationReceiptRefRequired")]
    [DataRow("concurrent", "IdempotencyConcurrentGuardDecisionRefRequired")]
    [DataRow("dirty", "IdempotencyDirtyWorktreeGuardRefRequired")]
    [DataRow("moved-base", "IdempotencyMovedBaseGuardRefRequired")]
    [DataRow("stale-validation", "IdempotencyStaleValidationGuardRefRequired")]
    [DataRow("branch-head", "IdempotencyBranchRemoteHeadVerificationRefRequired")]
    [DataRow("post-state", "IdempotencyPostStateObservationRefRequired")]
    public void FreshNewKeyRequiresEvidenceRefsBeforeProceeding(string caseName, string expectedReason)
    {
        var decision = Evaluate(MissingEvidenceRequest(caseName));

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByMissingIdempotencyEvidence, decision.Decision);
        Assert.AreEqual(IdempotencyKeyBlockKind.MissingEvidence, decision.BlockKind);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void PriorCompletedSameRequestReturnsDuplicateCompletedNoExecution()
    {
        var decision = Evaluate(PriorRequest(IdempotencyPriorState.PriorCompletedSameRequest));

        Assert.AreEqual(IdempotencyKeyDecisionKind.DuplicateCompletedNoExecution, decision.Decision);
        Assert.AreEqual(IdempotencyKeyBlockKind.None, decision.BlockKind);
        CollectionAssert.Contains(decision.Warnings.ToList(), "duplicate completed evidence is not downstream authority");
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void PriorCompletedSameRequestDoesNotAuthorizeReplayOrWorkflowContinuation()
    {
        var decision = Evaluate(PriorRequest(IdempotencyPriorState.PriorCompletedSameRequest));

        Assert.AreEqual(IdempotencyKeyDecisionKind.DuplicateCompletedNoExecution, decision.Decision);
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "idempotency key is not retry authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "idempotency key is not workflow continuation authority");
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void PriorInProgressSameRequestBlocksDuplicate()
    {
        var decision = Evaluate(PriorRequest(IdempotencyPriorState.PriorInProgressSameRequest));

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByDuplicateInProgress, decision.Decision);
        Assert.AreEqual(IdempotencyKeyBlockKind.DuplicateInProgress, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void PriorFailedSameRequestBlocksRetry()
    {
        var decision = Evaluate(PriorRequest(IdempotencyPriorState.PriorFailedSameRequest));

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByPriorFailedAttempt, decision.Decision);
        Assert.AreEqual(IdempotencyKeyBlockKind.PriorFailedAttempt, decision.BlockKind);
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "idempotency key is not retry authority");
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void PriorCancelledSameRequestBlocksContinuation()
    {
        var decision = Evaluate(PriorRequest(IdempotencyPriorState.PriorCancelledSameRequest));

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByPriorCancelledAttempt, decision.Decision);
        Assert.AreEqual(IdempotencyKeyBlockKind.PriorCancelledAttempt, decision.BlockKind);
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "idempotency key is not workflow continuation authority");
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(IdempotencyPriorState.PriorUnavailable)]
    [DataRow(IdempotencyPriorState.Ambiguous)]
    public void PriorUnavailableOrAmbiguousBlocks(IdempotencyPriorState priorState)
    {
        var decision = Evaluate(PriorRequest(priorState));

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByAmbiguousIdempotencyState, decision.Decision);
        Assert.AreEqual(IdempotencyKeyBlockKind.AmbiguousState, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ExpiredPriorStateBlocks()
    {
        var decision = Evaluate(PriorRequest(IdempotencyPriorState.PriorExpired));

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByExpiredIdempotencyObservation, decision.Decision);
        Assert.AreEqual(IdempotencyKeyBlockKind.ExpiredObservation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(IdempotencyPriorState.PriorConflictingRequest, IdempotencyKeyDecisionKind.BlockedByConflictingRequestFingerprint)]
    [DataRow(IdempotencyPriorState.PriorConflictingAuthority, IdempotencyKeyDecisionKind.BlockedByConflictingAuthorityFingerprint)]
    [DataRow(IdempotencyPriorState.PriorConflictingTarget, IdempotencyKeyDecisionKind.BlockedByConflictingTargetFingerprint)]
    public void ConflictingPriorStatesBlock(IdempotencyPriorState priorState, IdempotencyKeyDecisionKind expected)
    {
        var decision = Evaluate(PriorRequest(priorState));

        Assert.AreEqual(expected, decision.Decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("same-key-different-request", IdempotencyKeyDecisionKind.BlockedByConflictingRequestFingerprint)]
    [DataRow("same-key-different-authority", IdempotencyKeyDecisionKind.BlockedByConflictingAuthorityFingerprint)]
    [DataRow("same-key-different-target", IdempotencyKeyDecisionKind.BlockedByConflictingTargetFingerprint)]
    [DataRow("same-key-different-effect", IdempotencyKeyDecisionKind.BlockedByConflictingIdempotencyKey)]
    [DataRow("expected-observed-request", IdempotencyKeyDecisionKind.BlockedByConflictingRequestFingerprint)]
    [DataRow("expected-observed-authority", IdempotencyKeyDecisionKind.BlockedByConflictingAuthorityFingerprint)]
    [DataRow("expected-observed-target", IdempotencyKeyDecisionKind.BlockedByConflictingTargetFingerprint)]
    [DataRow("expected-observed-effect", IdempotencyKeyDecisionKind.BlockedByConflictingIdempotencyKey)]
    public void FingerprintConflictsBlock(string caseName, IdempotencyKeyDecisionKind expected)
    {
        var decision = Evaluate(ConflictRequest(caseName));

        Assert.AreEqual(expected, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SelfReportedWithoutCorroborationBlocks()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = IdempotencyEvidenceKind.ClientProvidedKey,
            EvidenceTrustLevel = IdempotencyEvidenceTrustLevel.SelfReported,
            IdempotencyObservationRef = "",
            PriorReceiptRef = "",
            PriorOperationStatusRef = "",
            PriorLineageRef = "",
            ObservedRequestFingerprint = ""
        });

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByMissingIdempotencyEvidence, decision.Decision);
        Assert.AreEqual("IdempotencySelfReportedCorroborationRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SelfReportedWithCorroborationStillCannotProceed()
    {
        var decision = Evaluate(Request() with { EvidenceTrustLevel = IdempotencyEvidenceTrustLevel.SelfReported });

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByUntrustedIdempotencyEvidence, decision.Decision);
        Assert.AreEqual("IdempotencySelfReportedCannotProceedToAuthorityGate", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void TestFixtureRequiresTestLabelledSource()
    {
        var decision = Evaluate(Request() with { EvidenceTrustLevel = IdempotencyEvidenceTrustLevel.TestFixture, Source = "idempotency-production" });

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByUntrustedIdempotencyEvidence, decision.Decision);
        Assert.AreEqual("IdempotencyTestFixtureSourceRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("trust")]
    [DataRow("kind")]
    public void TestFixtureAndSyntheticTestKeyCannotProceed(string caseName)
    {
        var request = caseName == "trust"
            ? Request() with { EvidenceTrustLevel = IdempotencyEvidenceTrustLevel.TestFixture, Source = "idempotency-test" }
            : Request() with { EvidenceKind = IdempotencyEvidenceKind.SyntheticTestKey, Source = "idempotency-test" };
        var decision = Evaluate(request);

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByUntrustedIdempotencyEvidence, decision.Decision);
        Assert.AreEqual("IdempotencyTestFixtureCannotProceedToAuthorityGate", decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(IdempotencyObservationFreshness.Stale, IdempotencyKeyDecisionKind.BlockedByStaleIdempotencyObservation)]
    [DataRow(IdempotencyObservationFreshness.NotTimestamped, IdempotencyKeyDecisionKind.BlockedByStaleIdempotencyObservation)]
    [DataRow(IdempotencyObservationFreshness.Expired, IdempotencyKeyDecisionKind.BlockedByExpiredIdempotencyObservation)]
    public void StaleExpiredOrNotTimestampedObservationBlocks(
        IdempotencyObservationFreshness freshness,
        IdempotencyKeyDecisionKind expected)
    {
        var decision = Evaluate(Request() with { ObservationFreshness = freshness });

        Assert.AreEqual(expected, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void MaxObservationAgeExceededBlocks()
    {
        var decision = Evaluate(Request() with
        {
            RecordedAtUtc = ObservedAtUtc.AddSeconds(IdempotencyKeyContractValidator.MaxObservationAgeSeconds + 1)
        });

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByStaleIdempotencyObservation, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void EvidenceExpiryInPastBlocks()
    {
        var decision = Evaluate(Request() with { EvidenceExpiresAtUtc = RecordedAtUtc });

        Assert.AreEqual(IdempotencyKeyDecisionKind.BlockedByExpiredIdempotencyObservation, decision.Decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(IdempotencySubjectKind.SourceApply)]
    [DataRow(IdempotencySubjectKind.Commit)]
    [DataRow(IdempotencySubjectKind.Push)]
    [DataRow(IdempotencySubjectKind.DraftPullRequest)]
    [DataRow(IdempotencySubjectKind.PullRequestBranchUpdate)]
    [DataRow(IdempotencySubjectKind.ReadyForReview)]
    [DataRow(IdempotencySubjectKind.Rollback)]
    [DataRow(IdempotencySubjectKind.Retry)]
    [DataRow(IdempotencySubjectKind.Recovery)]
    [DataRow(IdempotencySubjectKind.WorkflowContinuation)]
    [DataRow(IdempotencySubjectKind.MergeReadiness)]
    [DataRow(IdempotencySubjectKind.ReleaseReadiness)]
    [DataRow(IdempotencySubjectKind.DeploymentReadiness)]
    public void IdempotencyContractCoversMutationAdjacentSubjects(IdempotencySubjectKind subject)
    {
        var decision = Evaluate(Request() with { SubjectKind = subject });

        Assert.AreEqual(IdempotencyKeyDecisionKind.MayProceedToNextAuthorityGate, decision.Decision);
        Assert.AreEqual(subject, decision.SubjectKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("idempotency key is not source apply authority")]
    [DataRow("idempotency key is not commit authority")]
    [DataRow("idempotency key is not push authority")]
    [DataRow("idempotency key is not pull request authority")]
    [DataRow("idempotency key is not ready-for-review authority")]
    [DataRow("idempotency key is not merge authority")]
    [DataRow("idempotency key is not release authority")]
    [DataRow("idempotency key is not deployment authority")]
    [DataRow("idempotency key is not retry authority")]
    [DataRow("idempotency key is not recovery authority")]
    [DataRow("idempotency key is not rollback authority")]
    [DataRow("idempotency key is not workflow continuation authority")]
    [DataRow("idempotency key is not approval")]
    [DataRow("idempotency key is not policy satisfaction")]
    [DataRow("idempotency key is not validation freshness")]
    [DataRow("idempotency key is not source safety")]
    public void IdempotencyKeyDoesNotGrantAuthority(string forbidden)
    {
        var decision = Evaluate(Request());

        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), forbidden);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("idem:e06:abc123")]
    [DataRow("idempotency:e06:abc123")]
    [DataRow("idem-source-apply:e06:abc123")]
    [DataRow("idem-commit:e06:abc123")]
    [DataRow("idem-push:e06:abc123")]
    public void ValidIdempotencyRefsAreAccepted(string key)
    {
        var decision = Evaluate(Request() with { IdempotencyKey = key });

        Assert.AreEqual(IdempotencyKeyDecisionKind.MayProceedToNextAuthorityGate, decision.Decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("source-apply-receipt:e06")]
    [DataRow("commit-receipt:e06")]
    [DataRow("push-receipt:e06")]
    [DataRow("draft-pr-receipt:e06")]
    [DataRow("release-candidate:e06")]
    [DataRow("workflow-continuation:e06")]
    [DataRow("patch-package:e06")]
    [DataRow("merge-target:e06")]
    [DataRow("deploy-target:e06")]
    public void BroadMarkerScanDoesNotRejectValidDomainRefs(string targetRef)
    {
        var decision = Evaluate(Request() with { TargetRef = targetRef });

        Assert.AreEqual(IdempotencyKeyDecisionKind.MayProceedToNextAuthorityGate, decision.Decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "IdempotencyApproved")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "IdempotencyAuthorized")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "DuplicateAuthorized")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "ReplayAuthorized")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "SafeToReplay")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "SafeToRetry")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "SafeToExecute")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "SafeToMutate")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "CanExecute")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "CanReplay")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "CanRetry")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "CanApply")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "CanCommit")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "CanPush")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "CanOpenPullRequest")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "CanMarkReadyForReview")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "CanMerge")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "CanRelease")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "CanDeploy")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "AuthoritySatisfied")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "PolicySatisfied")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "ValidationSatisfied")]
    [DataRow(typeof(IdempotencyKeyDecisionKind), "SourceSafe")]
    public void E06DoesNotIntroduceAuthorityShapedDecisionNames(Type enumType, string forbidden)
    {
        CollectionAssert.DoesNotContain(Enum.GetNames(enumType), forbidden);
    }

    [TestMethod]
    public void E06AddsNoApiCliUiWorkerOrOpenApiSurface()
    {
        var changedNames = E06AllowedFileNames();

        Assert.IsTrue(changedNames.All(static path =>
            path.StartsWith("IronDev.Core/Governance/", StringComparison.Ordinal) ||
            path.StartsWith("IronDev.IntegrationTests/", StringComparison.Ordinal) ||
            path.StartsWith("Docs/receipts/", StringComparison.Ordinal)));
        Assert.IsFalse(changedNames.Any(static path =>
            path.StartsWith("IronDev.Api/", StringComparison.Ordinal) ||
            path.StartsWith("IronDev.Cli/", StringComparison.Ordinal) ||
            path.StartsWith("IronDev.Frontend/", StringComparison.Ordinal) ||
            path.StartsWith("OpenApi/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void E06AddsNoPersistenceOrSqlSurface()
    {
        var source = StripStrings(E06CoreSource());

        AssertDoesNotContain(source, "Repository");
        AssertDoesNotContain(source, "Store");
        AssertDoesNotContain(source, "Sql");
        AssertDoesNotContain(source, "Db");
        AssertDoesNotContain(source, "Persistence");
    }

    [TestMethod]
    public void E06AddsNoGitGitHubProviderOrProcessCalls()
    {
        var source = StripStrings(E06CoreSource());

        AssertDoesNotContain(source, "GitHub");
        AssertDoesNotContain(source, "HttpClient");
        AssertDoesNotContain(source, "ProcessStartInfo");
        AssertDoesNotContain(source, "RunProcessAsync");
        AssertDoesNotContain(source, "ProviderGateway");
        AssertDoesNotContain(source, "ProviderClient");
        AssertDoesNotContain(source, "IProvider");
        AssertDoesNotContain(source, "File.Read");
        AssertDoesNotContain(source, "File.Write");
        AssertDoesNotContain(source, "Directory.");
    }

    [TestMethod]
    public void E06AddsNoExecutorWiring()
    {
        var source = StripStrings(E06CoreSource());

        AssertDoesNotContain(source, "IControlled");
        AssertDoesNotContain(source, "Gateway");
        AssertDoesNotContain(source, "Dispatch");
        AssertDoesNotContain(source, "ExecuteAsync");
        AssertDoesNotContain(source, "Execute(");
    }

    [DataTestMethod]
    [DataRow("E06 is a Block E backfill slice")]
    [DataRow("An idempotency key prevents accidental duplicate intent. It does not authorize execution.")]
    [DataRow("Same key is not same authority. Same request is not permission to run it again.")]
    [DataRow("source apply authority")]
    [DataRow("commit authority")]
    [DataRow("push authority")]
    [DataRow("pull request authority")]
    [DataRow("ready-for-review authority")]
    [DataRow("merge authority")]
    [DataRow("release authority")]
    [DataRow("deployment authority")]
    [DataRow("retry authority")]
    [DataRow("recovery authority")]
    [DataRow("workflow continuation")]
    [DataRow("approval")]
    [DataRow("policy satisfaction")]
    [DataRow("validation freshness")]
    [DataRow("source safety")]
    [DataRow("worktree safety")]
    [DataRow("branch safety")]
    [DataRow("mutation authority")]
    [DataRow("adds no idempotency store")]
    public void ReceiptContainsBoundaryLines(string phrase)
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "E06_IDEMPOTENCY_KEY_CONTRACT.md"));

        StringAssert.Contains(receipt, phrase);
    }

    private static IdempotencyKeyContractDecision Evaluate(IdempotencyKeyContractRequest request) =>
        new IdempotencyKeyContractService().Evaluate(request);

    private static IdempotencyKeyContractRequest Request() =>
        new()
        {
            TenantId = "tenant-e06",
            ProjectId = "project-e06",
            OperationId = "op_000000000000e006",
            CorrelationId = "corr_0000000000e00600",
            MutationSurface = MutationLeaseSurfaceKind.SourceApply,
            SubjectKind = IdempotencySubjectKind.SourceApply,
            AttemptRef = "attempt:e06",
            TargetRef = "target:e06",
            RequestRef = "request:e06",
            IdempotencyKey = "idem:e06:abc123",
            IdempotencyScopeRef = "idempotency-scope:e06",
            IdempotencyObservationRef = "idempotency-observation:e06",
            PriorAttemptRef = "prior-attempt:e06",
            PriorReceiptRef = "prior-receipt:e06",
            PriorOperationStatusRef = "prior-status:e06",
            PriorLineageRef = "prior-lineage:e06",
            RequestFingerprint = "fingerprint:e06:request",
            ExpectedRequestFingerprint = "fingerprint:e06:request",
            ObservedRequestFingerprint = "fingerprint:e06:request",
            AuthorityFingerprint = "fingerprint:e06:authority",
            ExpectedAuthorityFingerprint = "fingerprint:e06:authority",
            ObservedAuthorityFingerprint = "fingerprint:e06:authority",
            TargetFingerprint = "fingerprint:e06:target",
            ExpectedTargetFingerprint = "fingerprint:e06:target",
            ObservedTargetFingerprint = "fingerprint:e06:target",
            EffectFingerprint = "fingerprint:e06:effect",
            ExpectedEffectFingerprint = "fingerprint:e06:effect",
            ObservedEffectFingerprint = "fingerprint:e06:effect",
            EvidenceKind = IdempotencyEvidenceKind.ExecutorRequestKey,
            EvidenceTrustLevel = IdempotencyEvidenceTrustLevel.RequestFingerprintBacked,
            ObservationFreshness = IdempotencyObservationFreshness.Fresh,
            PriorState = IdempotencyPriorState.NoPriorObservation,
            AuthorityReceiptRef = "authority-receipt:e06",
            PolicySatisfactionRef = "policy-satisfaction:e06",
            ValidationReceiptRef = "validation-receipt:e06",
            ConcurrentGuardDecisionRef = "concurrent-guard:e06",
            DirtyWorktreeGuardRef = "dirty-worktree-guard:e06",
            MovedBaseGuardRef = "moved-base-guard:e06",
            StaleValidationGuardRef = "stale-validation-guard:e06",
            BranchRemoteHeadVerificationRef = "branch-remote-head:e06",
            PostStateObservationRef = "post-state-observation:e06",
            ObservedAtUtc = ObservedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            EvidenceExpiresAtUtc = ExpiresAtUtc,
            ContractVersion = "idempotency-key-contract-v1",
            ReasonCode = "idempotency-key-observed",
            Source = "idempotency-key-contract"
        };

    private static IdempotencyKeyContractRequest InvalidRequest(string caseName) =>
        caseName switch
        {
            "tenant" => Request() with { TenantId = "" },
            "project" => Request() with { ProjectId = "" },
            "operation" => Request() with { OperationId = "" },
            "operation-invalid" => Request() with { OperationId = "corr_0000000000e00600" },
            "correlation" => Request() with { CorrelationId = "" },
            "correlation-invalid" => Request() with { CorrelationId = "op_000000000000e006" },
            "surface" => Request() with { MutationSurface = MutationLeaseSurfaceKind.Unknown },
            "attempt" => Request() with { AttemptRef = "" },
            "target" => Request() with { TargetRef = "" },
            "request" => Request() with { RequestRef = "" },
            "scope" => Request() with { IdempotencyScopeRef = "" },
            "request-fingerprint" => Request() with { RequestFingerprint = "" },
            "observed-non-utc" => Request() with { ObservedAtUtc = new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.FromHours(12)) },
            "recorded-non-utc" => Request() with { RecordedAtUtc = new DateTimeOffset(2026, 6, 26, 0, 1, 0, TimeSpan.FromHours(12)) },
            "recorded-before-observed" => Request() with { RecordedAtUtc = ObservedAtUtc.AddSeconds(-1) },
            "contract-version" => Request() with { ContractVersion = "" },
            "reason" => Request() with { ReasonCode = "" },
            "source" => Request() with { Source = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static IdempotencyKeyContractRequest UnknownDimensionRequest(string caseName) =>
        caseName switch
        {
            "subject" => Request() with { SubjectKind = IdempotencySubjectKind.Unknown },
            "evidence-kind" => Request() with { EvidenceKind = IdempotencyEvidenceKind.Unknown },
            "trust-level" => Request() with { EvidenceTrustLevel = IdempotencyEvidenceTrustLevel.Unknown },
            "freshness" => Request() with { ObservationFreshness = IdempotencyObservationFreshness.Unknown },
            "prior-state" => Request() with { PriorState = IdempotencyPriorState.Unknown },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static IdempotencyKeyContractRequest MissingEvidenceRequest(string caseName) =>
        caseName switch
        {
            "observation" => Request() with { IdempotencyObservationRef = "" },
            "expected-request" => Request() with { ExpectedRequestFingerprint = "" },
            "observed-request" => Request() with { ObservedRequestFingerprint = "" },
            "authority-fingerprint" => Request() with { AuthorityFingerprint = "" },
            "expected-authority" => Request() with { ExpectedAuthorityFingerprint = "" },
            "observed-authority" => Request() with { ObservedAuthorityFingerprint = "" },
            "target-fingerprint" => Request() with { TargetFingerprint = "" },
            "expected-target" => Request() with { ExpectedTargetFingerprint = "" },
            "observed-target" => Request() with { ObservedTargetFingerprint = "" },
            "effect-fingerprint" => Request() with { EffectFingerprint = "" },
            "expected-effect" => Request() with { ExpectedEffectFingerprint = "" },
            "observed-effect" => Request() with { ObservedEffectFingerprint = "" },
            "authority-receipt" => Request() with { AuthorityReceiptRef = "" },
            "policy" => Request() with { PolicySatisfactionRef = "" },
            "validation" => Request() with { ValidationReceiptRef = "" },
            "concurrent" => Request() with { ConcurrentGuardDecisionRef = "" },
            "dirty" => Request() with { DirtyWorktreeGuardRef = "" },
            "moved-base" => Request() with { MovedBaseGuardRef = "" },
            "stale-validation" => Request() with { StaleValidationGuardRef = "" },
            "branch-head" => Request() with { BranchRemoteHeadVerificationRef = "" },
            "post-state" => Request() with { PostStateObservationRef = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static IdempotencyKeyContractRequest PriorRequest(IdempotencyPriorState priorState) =>
        Request() with
        {
            PriorState = priorState,
            EvidenceKind = IdempotencyEvidenceKind.ReceiptBackedKey,
            EvidenceTrustLevel = IdempotencyEvidenceTrustLevel.ReceiptBacked
        };

    private static IdempotencyKeyContractRequest ConflictRequest(string caseName) =>
        caseName switch
        {
            "same-key-different-request" => Request() with
            {
                ExpectedRequestFingerprint = "fingerprint:e06:request-other",
                ObservedRequestFingerprint = "fingerprint:e06:request-other"
            },
            "same-key-different-authority" => Request() with
            {
                ExpectedAuthorityFingerprint = "fingerprint:e06:authority-other",
                ObservedAuthorityFingerprint = "fingerprint:e06:authority-other"
            },
            "same-key-different-target" => Request() with
            {
                ExpectedTargetFingerprint = "fingerprint:e06:target-other",
                ObservedTargetFingerprint = "fingerprint:e06:target-other"
            },
            "same-key-different-effect" => Request() with
            {
                ExpectedEffectFingerprint = "fingerprint:e06:effect-other",
                ObservedEffectFingerprint = "fingerprint:e06:effect-other"
            },
            "expected-observed-request" => Request() with { ObservedRequestFingerprint = "fingerprint:e06:request-other" },
            "expected-observed-authority" => Request() with { ObservedAuthorityFingerprint = "fingerprint:e06:authority-other" },
            "expected-observed-target" => Request() with { ObservedTargetFingerprint = "fingerprint:e06:target-other" },
            "expected-observed-effect" => Request() with { ObservedEffectFingerprint = "fingerprint:e06:effect-other" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static string MalformedKey(string caseName) =>
        caseName switch
        {
            "malformed" => "same-key-is-authority",
            "whitespace" => "idem:e06:bad key",
            "overlong" => "idem:e06:" + new string('a', IdempotencyKeyContractValidator.MaxIdempotencyKeyLength),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static string UnsafeText(string caseName) =>
        caseName switch
        {
            "authority" => "safe to execute",
            "raw-patch" => "raw patch",
            "raw-diff" => "diff --git",
            "credential" => string.Concat("to", "ken", "=", "fake"),
            "private-key" => string.Concat("private ", "key"),
            "json-payload" => "json payload",
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static void AssertRequiresAllGates(IdempotencyKeyContractDecision decision)
    {
        Assert.IsTrue(decision.RequiresFreshAuthority);
        Assert.IsTrue(decision.RequiresAcceptedApproval);
        Assert.IsTrue(decision.RequiresPolicySatisfaction);
        Assert.IsTrue(decision.RequiresFreshValidation);
        Assert.IsTrue(decision.RequiresConcurrentGuard);
        Assert.IsTrue(decision.RequiresDirtyWorktreeGuard);
        Assert.IsTrue(decision.RequiresMovedBaseGuard);
        Assert.IsTrue(decision.RequiresStaleValidationGuard);
        Assert.IsTrue(decision.RequiresBranchRemoteHeadVerification);
        Assert.IsTrue(decision.RequiresFreshPostStateObservation);
        Assert.IsTrue(decision.RequiresHumanReview);
    }

    private static void AssertNoAuthority(IdempotencyKeyContractDecision decision)
    {
        AssertRequiresAllGates(decision);
        CollectionAssert.Contains(decision.Warnings.ToList(), "idempotency key contract is read only");
        CollectionAssert.Contains(decision.Warnings.ToList(), "idempotency key contract does not execute mutation");
        CollectionAssert.Contains(decision.Warnings.ToList(), "idempotency key contract does not retry mutation");
        CollectionAssert.Contains(decision.Warnings.ToList(), "idempotency key contract does not recover mutation");
        CollectionAssert.Contains(decision.Warnings.ToList(), "idempotency key contract does not continue workflow");
        CollectionAssert.Contains(decision.Warnings.ToList(), "idempotency key is not authority");
        CollectionAssert.Contains(decision.Warnings.ToList(), "same key is not same authority");
        CollectionAssert.Contains(decision.Warnings.ToList(), "fresh authority is required before mutation");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "idempotency key is not source apply authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "idempotency key is not commit authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "idempotency key is not push authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "idempotency key is not pull request authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "idempotency key is not retry authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "idempotency key is not recovery authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "idempotency key is not workflow continuation authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "idempotency key is not mutation authority");
    }

    private static string E06CoreSource()
    {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "IdempotencyKeyContract*.cs");
        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    private static IReadOnlyList<string> E06AllowedFileNames() =>
    [
        "IronDev.Core/Governance/IdempotencyKeyContractModels.cs",
        "IronDev.Core/Governance/IdempotencyKeyContractValidator.cs",
        "IronDev.Core/Governance/IdempotencyKeyContractService.cs",
        "IronDev.IntegrationTests/BlockE06IdempotencyKeyContractTests.cs",
        "Docs/receipts/E06_IDEMPOTENCY_KEY_CONTRACT.md"
    ];

    private static string StripStrings(string source) =>
        System.Text.RegularExpressions.Regex.Replace(source, "\"(?:\\\\.|[^\"])*\"", "\"\"");

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
            $"Unexpected forbidden marker found in E06 source: {forbidden}");
}
