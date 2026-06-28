using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE11PostStateObservationStandardTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecordedAtUtc = ObservedAtUtc.AddMinutes(1);
    private static readonly DateTimeOffset ExpiresAtUtc = ObservedAtUtc.AddMinutes(30);

    [TestMethod]
    public void ValidCompleteNoChangeObservationIsAcceptedAsEvidenceOnly()
    {
        var decision = Evaluate(Request());

        Assert.AreEqual(PostStateObservationDecisionKind.AcceptedAsPostStateEvidence, decision.Decision);
        Assert.AreEqual(PostStateObservationBlockKind.None, decision.BlockKind);
        Assert.AreEqual(PostStateBoundarySignal.SupportsRetryAssessmentOnly, decision.BoundarySignal);
        AssertRequiresFreshGates(decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void CompleteNoChangeObservationStillRequiresFreshAuthority()
    {
        var decision = Evaluate(Request());

        Assert.IsTrue(decision.RequiresFreshAuthority);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void CompleteNoChangeObservationStillRequiresFreshValidation()
    {
        var decision = Evaluate(Request());

        Assert.IsTrue(decision.RequiresFreshValidation);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void CompleteNoChangeObservationStillRequiresFreshConcurrentGuard()
    {
        var decision = Evaluate(Request());

        Assert.IsTrue(decision.RequiresFreshConcurrentGuard);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void CompleteNoChangeObservationStillRequiresFreshPostStateObservationBeforeAction()
    {
        var decision = Evaluate(Request());

        Assert.IsTrue(decision.RequiresFreshPostStateObservation);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("tenant", "PostStateObservationTenantIdRequired")]
    [DataRow("project", "PostStateObservationProjectIdRequired")]
    [DataRow("operation", "PostStateObservationOperationIdRequired")]
    [DataRow("operation-invalid", "PostStateObservationOperationIdInvalid")]
    [DataRow("correlation", "PostStateObservationCorrelationIdRequired")]
    [DataRow("correlation-invalid", "PostStateObservationCorrelationIdInvalid")]
    [DataRow("surface", "PostStateObservationMutationSurfaceRequired")]
    [DataRow("attempt", "PostStateObservationAttemptRefRequired")]
    [DataRow("target", "PostStateObservationTargetRefRequired")]
    [DataRow("observation", "PostStateObservationObservationRefRequired")]
    [DataRow("observed-non-utc", "PostStateObservationObservedAtUtcMustBeUtc")]
    [DataRow("recorded-non-utc", "PostStateObservationRecordedAtUtcMustBeUtc")]
    [DataRow("recorded-before-observed", "PostStateObservationRecordedAtUtcBeforeObservedAtUtc")]
    [DataRow("observer-version", "PostStateObservationObserverVersionRequired")]
    [DataRow("reason", "PostStateObservationReasonCodeRequired")]
    [DataRow("source", "PostStateObservationSourceRequired")]
    public void InvalidRequestFailsClosed(string caseName, string expectedReason)
    {
        var decision = Evaluate(InvalidRequest(caseName));

        Assert.AreEqual(PostStateObservationDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual(PostStateObservationBlockKind.InvalidRequest, decision.BlockKind);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("subject", PostStateObservationDecisionKind.BlockedByUnknownSubject, PostStateObservationBlockKind.UnknownSubject)]
    [DataRow("method", PostStateObservationDecisionKind.BlockedByUnknownMethod, PostStateObservationBlockKind.UnknownMethod)]
    [DataRow("expectation", PostStateObservationDecisionKind.BlockedByUnknownTransition, PostStateObservationBlockKind.UnknownTransition)]
    [DataRow("transition", PostStateObservationDecisionKind.BlockedByUnknownTransition, PostStateObservationBlockKind.UnknownTransition)]
    [DataRow("completeness", PostStateObservationDecisionKind.BlockedByUnknownCompleteness, PostStateObservationBlockKind.UnknownCompleteness)]
    [DataRow("trust", PostStateObservationDecisionKind.BlockedByUnknownTrustLevel, PostStateObservationBlockKind.UnknownTrustLevel)]
    public void UnknownObservationDimensionsBlock(
        string caseName,
        PostStateObservationDecisionKind expectedDecision,
        PostStateObservationBlockKind expectedBlock)
    {
        var decision = Evaluate(UnknownDimensionRequest(caseName));

        Assert.AreEqual(expectedDecision, decision.Decision);
        Assert.AreEqual(expectedBlock, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownExpectationAllowedOnlyForObservationUnavailable()
    {
        var decision = Evaluate(Request() with
        {
            TransitionExpectation = PostStateTransitionExpectation.Unknown,
            ObservedTransition = PostStateObservedTransition.ObservationUnavailable,
            FailureClassificationRef = "failure-classification:e11"
        });

        Assert.AreEqual(PostStateObservationDecisionKind.AcceptedAsPostStateEvidence, decision.Decision);
        Assert.AreEqual(PostStateBoundarySignal.RequiresFreshObservation, decision.BoundarySignal);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ExpiredObservationBlocks()
    {
        var decision = Evaluate(Request() with { ObservationExpiresAtUtc = RecordedAtUtc });

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByExpiredObservation, decision.Decision);
        Assert.AreEqual(PostStateObservationBlockKind.ExpiredObservation, decision.BlockKind);
        Assert.AreEqual(PostStateBoundarySignal.RequiresFreshObservation, decision.BoundarySignal);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void StaleObservationBlocks()
    {
        var decision = Evaluate(Request() with { RecordedAtUtc = ObservedAtUtc.AddSeconds(PostStateObservationValidator.MaxObservationAgeSeconds + 1) });

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByStaleObservation, decision.Decision);
        Assert.AreEqual(PostStateObservationBlockKind.StaleObservation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("pre-ref", "PostStateObservationPreStateRefRequired")]
    [DataRow("pre-fingerprint", "PostStateObservationPreStateFingerprintRequired")]
    [DataRow("observed-ref", "PostStateObservationObservedPostStateRefRequired")]
    [DataRow("observed-fingerprint", "PostStateObservationObservedPostStateFingerprintRequired")]
    public void NoChangeObservationRequiresStateRefs(string caseName, string expectedReason)
    {
        var decision = Evaluate(MissingNoChangeEvidenceRequest(caseName));

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByMissingEvidence, decision.Decision);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void NoChangeFingerprintMismatchBlocksInconsistent()
    {
        var decision = Evaluate(Request() with { ObservedPostStateFingerprint = "fingerprint:e11:different" });

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByInconsistentState, decision.Decision);
        Assert.AreEqual(PostStateObservationBlockKind.InconsistentState, decision.BlockKind);
        Assert.AreEqual("PostStateObservationNoChangeFingerprintMismatch", decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("expected-ref", "PostStateObservationExpectedPostStateRefRequired")]
    [DataRow("expected-fingerprint", "PostStateObservationExpectedPostStateFingerprintRequired")]
    [DataRow("mutation-receipt", "PostStateObservationMutationReceiptRefRequired")]
    public void ExpectedChangeRequiresExpectedEvidence(string caseName, string expectedReason)
    {
        var decision = Evaluate(MissingExpectedChangeEvidenceRequest(caseName));

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByMissingEvidence, decision.Decision);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ExpectedChangeMatchingFingerprintAcceptedAsEvidenceOnly()
    {
        var decision = Evaluate(ExpectedChangeRequest());

        Assert.AreEqual(PostStateObservationDecisionKind.AcceptedAsPostStateEvidence, decision.Decision);
        Assert.AreEqual(PostStateBoundarySignal.RequiresPostStateTriage, decision.BoundarySignal);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ExpectedChangeMismatchBlocksInconsistent()
    {
        var decision = Evaluate(ExpectedChangeRequest() with { ObservedPostStateFingerprint = "fingerprint:e11:unexpected" });

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByInconsistentState, decision.Decision);
        Assert.AreEqual("PostStateObservationExpectedFingerprintMismatch", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ExpectedChangeWherePreEqualsObservedBlocksInconsistent()
    {
        var decision = Evaluate(ExpectedChangeRequest() with
        {
            ExpectedPostStateFingerprint = "fingerprint:e11:pre",
            ObservedPostStateFingerprint = "fingerprint:e11:pre"
        });

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByInconsistentState, decision.Decision);
        Assert.AreEqual("PostStateObservationExpectedChangeDidNotChange", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnexpectedChangeRoutesToPostStateTriage()
    {
        var decision = Evaluate(ChangedFailureRequest(PostStateObservedTransition.UnexpectedChangeObserved));

        Assert.AreEqual(PostStateObservationDecisionKind.AcceptedAsPostStateEvidence, decision.Decision);
        Assert.AreEqual(PostStateBoundarySignal.RequiresPostStateTriage, decision.BoundarySignal);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(PostStateObservedTransition.PartialChangeObserved, PostStateBoundarySignal.RequiresRecoveryAssessment)]
    [DataRow(PostStateObservedTransition.DivergentChangeObserved, PostStateBoundarySignal.RequiresManualTriage)]
    [DataRow(PostStateObservedTransition.ProviderAcceptedOutcomeUnknown, PostStateBoundarySignal.RequiresPostStateTriage)]
    [DataRow(PostStateObservedTransition.ProviderRejectedAfterMutationStarted, PostStateBoundarySignal.RequiresPostStateTriage)]
    [DataRow(PostStateObservedTransition.ObservationUnavailable, PostStateBoundarySignal.RequiresFreshObservation)]
    [DataRow(PostStateObservedTransition.ObservationFailed, PostStateBoundarySignal.RequiresFreshObservation)]
    public void UnsafeOrIncompleteTransitionsNeverSupportRetry(
        PostStateObservedTransition transition,
        PostStateBoundarySignal expectedSignal)
    {
        var decision = Evaluate(TransitionRequest(transition));

        Assert.AreEqual(PostStateObservationDecisionKind.AcceptedAsPostStateEvidence, decision.Decision);
        Assert.AreEqual(expectedSignal, decision.BoundarySignal);
        Assert.AreNotEqual(PostStateBoundarySignal.SupportsRetryAssessmentOnly, decision.BoundarySignal);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ProviderAcceptedOutcomeUnknownRequiresProviderStateRef()
    {
        var decision = Evaluate(TransitionRequest(PostStateObservedTransition.ProviderAcceptedOutcomeUnknown) with { ProviderStateRef = "" });

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByMissingEvidence, decision.Decision);
        Assert.AreEqual("PostStateObservationProviderStateRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ProviderRejectedBeforeMutationMaySupportRetryAssessmentOnlyWhenCompleteTrusted()
    {
        var decision = Evaluate(TransitionRequest(PostStateObservedTransition.ProviderRejectedBeforeMutation));

        Assert.AreEqual(PostStateObservationDecisionKind.AcceptedAsPostStateEvidence, decision.Decision);
        Assert.AreEqual(PostStateBoundarySignal.SupportsRetryAssessmentOnly, decision.BoundarySignal);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ProviderRejectedAfterMutationStartedRequiresObservedPostStateRef()
    {
        var decision = Evaluate(TransitionRequest(PostStateObservedTransition.ProviderRejectedAfterMutationStarted) with { ObservedPostStateRef = "" });

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByMissingEvidence, decision.Decision);
        Assert.AreEqual("PostStateObservationObservedPostStateRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(PostStateObservedTransition.ObservationUnavailable)]
    [DataRow(PostStateObservedTransition.ObservationFailed)]
    public void ObservationUnavailableOrFailedRequiresFailureClassificationRef(PostStateObservedTransition transition)
    {
        var decision = Evaluate(TransitionRequest(transition) with { FailureClassificationRef = "" });

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByMissingEvidence, decision.Decision);
        Assert.AreEqual("PostStateObservationFailureClassificationRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SelfReportedWithoutCorroborationBlocks()
    {
        var decision = Evaluate(Request() with
        {
            ObservationTrustLevel = PostStateObservationTrustLevel.SelfReported,
            FailureReceiptRef = "",
            MutationReceiptRef = "",
            ProviderStateRef = "",
            ReadModelStateRef = ""
        });

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByUntrustedObservation, decision.Decision);
        Assert.AreEqual("PostStateObservationSelfReportedCorroborationRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SelfReportedWithReceiptCorroborationAcceptedOnlyForTriage()
    {
        var decision = Evaluate(Request() with
        {
            ObservationTrustLevel = PostStateObservationTrustLevel.SelfReported,
            FailureReceiptRef = "failure-receipt:e11",
            MutationReceiptRef = "",
            ProviderStateRef = "",
            ReadModelStateRef = ""
        });

        Assert.AreEqual(PostStateObservationDecisionKind.AcceptedAsPostStateEvidence, decision.Decision);
        Assert.AreEqual(PostStateBoundarySignal.RequiresManualTriage, decision.BoundarySignal);
        Assert.AreNotEqual(PostStateBoundarySignal.SupportsRetryAssessmentOnly, decision.BoundarySignal);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ReadModelBackedRequiresReadModelStateRef()
    {
        var decision = Evaluate(Request() with
        {
            ObservationTrustLevel = PostStateObservationTrustLevel.ReadModelBacked,
            ReadModelStateRef = ""
        });

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByMissingEvidence, decision.Decision);
        Assert.AreEqual("PostStateObservationReadModelStateRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ReadModelBackedIsEvidenceOnly()
    {
        var decision = Evaluate(Request() with
        {
            ObservationTrustLevel = PostStateObservationTrustLevel.ReadModelBacked,
            ReadModelStateRef = "read-model:e11"
        });

        Assert.AreEqual(PostStateObservationDecisionKind.AcceptedAsPostStateEvidence, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ReceiptBackedRequiresReceiptRef()
    {
        var decision = Evaluate(Request() with
        {
            ObservationTrustLevel = PostStateObservationTrustLevel.ReceiptBacked,
            FailureReceiptRef = "",
            MutationReceiptRef = ""
        });

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByMissingEvidence, decision.Decision);
        Assert.AreEqual("PostStateObservationReceiptRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(PostStateObservationCompleteness.Partial)]
    [DataRow(PostStateObservationCompleteness.Truncated)]
    [DataRow(PostStateObservationCompleteness.Unavailable)]
    public void NonCompleteObservationNeverSupportsRetryAssessment(PostStateObservationCompleteness completeness)
    {
        var decision = Evaluate(Request() with { ObservationCompleteness = completeness });

        Assert.AreEqual(PostStateObservationDecisionKind.AcceptedAsPostStateEvidence, decision.Decision);
        Assert.AreNotEqual(PostStateBoundarySignal.SupportsRetryAssessmentOnly, decision.BoundarySignal);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void StaleCompletenessBlocks()
    {
        var decision = Evaluate(Request() with { ObservationCompleteness = PostStateObservationCompleteness.Stale });

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByStaleObservation, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SyntheticTestObservationAllowedOnlyWithTestSource()
    {
        var decision = Evaluate(Request() with
        {
            ObservationMethod = PostStateObservationMethod.SyntheticTestObservation,
            ObservationTrustLevel = PostStateObservationTrustLevel.TestFixture,
            Source = "post-state-test"
        });

        Assert.AreEqual(PostStateObservationDecisionKind.AcceptedAsPostStateEvidence, decision.Decision);
        Assert.AreNotEqual(PostStateBoundarySignal.SupportsRetryAssessmentOnly, decision.BoundarySignal);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SyntheticTestObservationDoesNotSupportRetryAssessment()
    {
        var decision = Evaluate(Request() with
        {
            ObservationMethod = PostStateObservationMethod.SyntheticTestObservation,
            ObservationTrustLevel = PostStateObservationTrustLevel.TestFixture,
            Source = "post-state-test"
        });

        Assert.AreEqual(PostStateObservationDecisionKind.AcceptedAsPostStateEvidence, decision.Decision);
        Assert.AreEqual(PostStateBoundarySignal.RequiresManualTriage, decision.BoundarySignal);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void TestFixtureNoChangeObservationDoesNotSupportRetryAssessment()
    {
        var decision = Evaluate(Request() with
        {
            ObservationTrustLevel = PostStateObservationTrustLevel.TestFixture,
            Source = "post-state-test"
        });

        Assert.AreEqual(PostStateObservationDecisionKind.AcceptedAsPostStateEvidence, decision.Decision);
        Assert.AreEqual(PostStateBoundarySignal.RequiresManualTriage, decision.BoundarySignal);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void TestFixtureProviderRejectedBeforeMutationDoesNotSupportRetryAssessment()
    {
        var decision = Evaluate(TransitionRequest(PostStateObservedTransition.ProviderRejectedBeforeMutation) with
        {
            ObservationTrustLevel = PostStateObservationTrustLevel.TestFixture,
            Source = "post-state-test"
        });

        Assert.AreEqual(PostStateObservationDecisionKind.AcceptedAsPostStateEvidence, decision.Decision);
        Assert.AreEqual(PostStateBoundarySignal.RequiresPostStateTriage, decision.BoundarySignal);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("method")]
    [DataRow("trust")]
    public void SyntheticTestObservationWithProductionSourceBlocks(string caseName)
    {
        var request = caseName == "method"
            ? Request() with { ObservationMethod = PostStateObservationMethod.SyntheticTestObservation, Source = "post-state-production" }
            : Request() with { ObservationTrustLevel = PostStateObservationTrustLevel.TestFixture, Source = "post-state-production" };
        var decision = Evaluate(request);

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByUntrustedObservation, decision.Decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("raw-patch")]
    [DataRow("raw-git-output")]
    [DataRow("provider-response-body")]
    [DataRow("credential")]
    [DataRow("authority")]
    public void UnsafeMarkersAreRejected(string caseName)
    {
        var decision = Evaluate(Request() with { ObservationRef = UnsafeReference(caseName) });

        Assert.AreEqual(PostStateObservationDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.AreEqual(PostStateObservationBlockKind.UnsafePayload, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("patch-package:e11")]
    [DataRow("merge-target:e11")]
    [DataRow("release-candidate:e11")]
    [DataRow("deploy-target:e11")]
    public void ValidDomainRefsAreNotRejected(string targetRef)
    {
        var decision = Evaluate(Request() with { TargetRef = targetRef });

        Assert.AreEqual(PostStateObservationDecisionKind.AcceptedAsPostStateEvidence, decision.Decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(typeof(PostStateObservationDecisionKind), "SafeToRetry")]
    [DataRow(typeof(PostStateObservationDecisionKind), "SourceSafe")]
    [DataRow(typeof(PostStateObservationDecisionKind), "CanContinue")]
    [DataRow(typeof(PostStateBoundarySignal), "Execute")]
    public void VocabularyAvoidsAuthorityShapedNames(Type enumType, string forbidden)
    {
        var names = Enum.GetNames(enumType);

        Assert.IsFalse(names.Any(name => name.Contains(forbidden, StringComparison.Ordinal)), forbidden);
    }

    [TestMethod]
    public void ContractTypesExposeNoCommandOrExecutionAuthorityFields()
    {
        var propertyNames = typeof(PostStateObservationDecision)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        Assert.IsFalse(propertyNames.Any(static name => name.Contains("Command", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("Executor", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.StartsWith("Can", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("Allowed", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("Authorized", StringComparison.Ordinal)));
        Assert.IsFalse(propertyNames.Any(static name => name.Contains("SourceSafe", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void StaticScan_E11CoreAddsNoExecutorWiring()
    {
        var source = E11CoreSource();

        AssertDoesNotContain(source, "IControlled");
        AssertDoesNotContain(source, "Executor");
        AssertDoesNotContain(source, "RunProcessAsync");
        AssertDoesNotContain(source, "ProcessStartInfo");
        AssertDoesNotContain(source, "Process.Start");
        AssertDoesNotContain(source, "HttpClient");
        AssertDoesNotContain(source, "GitHubClient");
        AssertDoesNotContain(source, "Octokit");
    }

    [TestMethod]
    public void StaticScan_E11CoreAddsNoGitSourceWorktreeOrLockAccess()
    {
        var source = E11CoreSource();

        AssertDoesNotContain(source, "File.Write");
        AssertDoesNotContain(source, "File.ReadAllText");
        AssertDoesNotContain(source, "Directory.");
        AssertDoesNotContain(source, "git ");
        AssertDoesNotContain(source, "gh ");
        AssertDoesNotContain(source, "WorktreeInspector");
        AssertDoesNotContain(source, "Acquire");
        AssertDoesNotContain(source, "Release");
        AssertDoesNotContain(source, "Renew");
    }

    [TestMethod]
    public void FingerprintIsStableForIdenticalRequest()
    {
        var first = Evaluate(Request());
        var second = Evaluate(Request());

        Assert.AreEqual(first.RecordFingerprint, second.RecordFingerprint);
        AssertNoAuthority(first);
    }

    [TestMethod]
    public void FingerprintChangesWhenObservedTransitionChanges()
    {
        var first = Evaluate(Request());
        var second = Evaluate(TransitionRequest(PostStateObservedTransition.ProviderRejectedBeforeMutation));

        Assert.AreNotEqual(first.RecordFingerprint, second.RecordFingerprint);
        AssertNoAuthority(second);
    }

    [TestMethod]
    public void FingerprintChangesWhenObservedFingerprintChanges()
    {
        var first = Evaluate(Request());
        var second = Evaluate(Request() with { ObservedPostStateFingerprint = "fingerprint:e11:changed" });

        Assert.AreNotEqual(first.RecordFingerprint, second.RecordFingerprint);
    }

    [TestMethod]
    public void FingerprintDoesNotIncludeRawPayloads()
    {
        var decision = Evaluate(Request());

        Assert.IsFalse(decision.RecordFingerprint.Contains("raw patch", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(decision.RecordFingerprint.Contains("provider response body", StringComparison.OrdinalIgnoreCase));
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ReceiptRecordsReviewLineAndKilljoy()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "E11_POST_STATE_OBSERVATION_STANDARD.md"));

        StringAssert.Contains(receipt, "Post-state observation is evidence. It is not source safety.");
        StringAssert.Contains(receipt, "Observed state is not permission to act on it.");
    }

    [DataTestMethod]
    [DataRow("source safety")]
    [DataRow("retry execution")]
    [DataRow("recovery execution")]
    [DataRow("workflow continuation")]
    public void ReceiptExplicitlyDeniesAuthority(string phrase)
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "E11_POST_STATE_OBSERVATION_STANDARD.md"));

        StringAssert.Contains(receipt, phrase);
    }

    private static PostStateObservationDecision Evaluate(PostStateObservationRequest request) =>
        new PostStateObservationService().Evaluate(request);

    private static PostStateObservationRequest Request() =>
        new()
        {
            TenantId = "tenant-e11",
            ProjectId = "project-e11",
            OperationId = "op_000000000000e011",
            CorrelationId = "corr_0000000000e01100",
            MutationSurface = MutationLeaseSurfaceKind.SourceApply,
            AttemptRef = "attempt:e11",
            TargetRef = "target:e11",
            ObservationRef = "observation:e11",
            SubjectKind = PostStateObservationSubjectKind.WorktreeState,
            ObservationMethod = PostStateObservationMethod.LocalMetadataReadback,
            TransitionExpectation = PostStateTransitionExpectation.NoChangeExpected,
            ObservedTransition = PostStateObservedTransition.NoChangeObserved,
            ObservationCompleteness = PostStateObservationCompleteness.Complete,
            ObservationTrustLevel = PostStateObservationTrustLevel.LocalMetadata,
            PreStateRef = "pre-state:e11",
            PreStateFingerprint = "fingerprint:e11:same",
            ExpectedPostStateRef = "expected-state:e11",
            ExpectedPostStateFingerprint = "fingerprint:e11:expected",
            ObservedPostStateRef = "observed-state:e11",
            ObservedPostStateFingerprint = "fingerprint:e11:same",
            FailureClassificationRef = "failure-classification:e11",
            FailureClassRef = "failure-class:e11",
            FailureReceiptRef = "failure-receipt:e11",
            MutationReceiptRef = "mutation-receipt:e11",
            ProviderStateRef = "provider-state:e11",
            ReadModelStateRef = "read-model:e11",
            ConcurrentGuardDecisionRef = "concurrent-guard:e11",
            LeaseObservationRef = "lease-observation:e11",
            IdempotencyKeyRef = "idempotency-key:e11",
            IdempotencyFingerprint = "idempotency-fingerprint:e11",
            ObservedAtUtc = ObservedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            ObservationExpiresAtUtc = ExpiresAtUtc,
            ObserverVersion = "post-state-v1",
            ReasonCode = "post-state-observed",
            Source = "post-state-observation"
        };

    private static PostStateObservationRequest ExpectedChangeRequest() =>
        Request() with
        {
            TransitionExpectation = PostStateTransitionExpectation.MutationExpected,
            ObservedTransition = PostStateObservedTransition.ExpectedChangeObserved,
            PreStateFingerprint = "fingerprint:e11:pre",
            ExpectedPostStateRef = "expected-state:e11",
            ExpectedPostStateFingerprint = "fingerprint:e11:post",
            ObservedPostStateFingerprint = "fingerprint:e11:post",
            MutationReceiptRef = "mutation-receipt:e11"
        };

    private static PostStateObservationRequest ChangedFailureRequest(PostStateObservedTransition transition) =>
        Request() with
        {
            TransitionExpectation = PostStateTransitionExpectation.MutationExpected,
            ObservedTransition = transition,
            ObservedPostStateFingerprint = "fingerprint:e11:changed",
            FailureClassificationRef = "failure-classification:e11"
        };

    private static PostStateObservationRequest TransitionRequest(PostStateObservedTransition transition) =>
        transition switch
        {
            PostStateObservedTransition.ProviderRejectedBeforeMutation => Request() with
            {
                TransitionExpectation = PostStateTransitionExpectation.MutationExpected,
                ObservedTransition = transition,
                FailureClassificationRef = "failure-classification:e11",
                ProviderStateRef = "provider-state:e11"
            },
            PostStateObservedTransition.ProviderAcceptedOutcomeUnknown => Request() with
            {
                TransitionExpectation = PostStateTransitionExpectation.MutationExpected,
                ObservedTransition = transition,
                FailureClassificationRef = "failure-classification:e11",
                ProviderStateRef = "provider-state:e11"
            },
            PostStateObservedTransition.ProviderRejectedAfterMutationStarted => Request() with
            {
                TransitionExpectation = PostStateTransitionExpectation.MutationExpected,
                ObservedTransition = transition,
                FailureClassificationRef = "failure-classification:e11",
                ProviderStateRef = "provider-state:e11",
                ObservedPostStateRef = "observed-state:e11"
            },
            PostStateObservedTransition.ObservationUnavailable => Request() with
            {
                TransitionExpectation = PostStateTransitionExpectation.Unknown,
                ObservedTransition = transition,
                ObservationCompleteness = PostStateObservationCompleteness.Unavailable,
                FailureClassificationRef = "failure-classification:e11"
            },
            PostStateObservedTransition.ObservationFailed => Request() with
            {
                TransitionExpectation = PostStateTransitionExpectation.ObservationOnly,
                ObservedTransition = transition,
                ObservationCompleteness = PostStateObservationCompleteness.Unavailable,
                FailureClassificationRef = "failure-classification:e11"
            },
            _ => ChangedFailureRequest(transition)
        };

    private static PostStateObservationRequest InvalidRequest(string caseName) =>
        caseName switch
        {
            "tenant" => Request() with { TenantId = "" },
            "project" => Request() with { ProjectId = "" },
            "operation" => Request() with { OperationId = "" },
            "operation-invalid" => Request() with { OperationId = "run:e11" },
            "correlation" => Request() with { CorrelationId = "" },
            "correlation-invalid" => Request() with { CorrelationId = "op_000000000000e011" },
            "surface" => Request() with { MutationSurface = MutationLeaseSurfaceKind.Unknown },
            "attempt" => Request() with { AttemptRef = "" },
            "target" => Request() with { TargetRef = "" },
            "observation" => Request() with { ObservationRef = "" },
            "observed-non-utc" => Request() with { ObservedAtUtc = new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.FromHours(12)) },
            "recorded-non-utc" => Request() with { RecordedAtUtc = new DateTimeOffset(2026, 6, 25, 0, 1, 0, TimeSpan.FromHours(12)) },
            "recorded-before-observed" => Request() with { RecordedAtUtc = ObservedAtUtc.AddSeconds(-1) },
            "observer-version" => Request() with { ObserverVersion = "" },
            "reason" => Request() with { ReasonCode = "" },
            "source" => Request() with { Source = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static PostStateObservationRequest UnknownDimensionRequest(string caseName) =>
        caseName switch
        {
            "subject" => Request() with { SubjectKind = PostStateObservationSubjectKind.Unknown },
            "method" => Request() with { ObservationMethod = PostStateObservationMethod.Unknown },
            "expectation" => Request() with { TransitionExpectation = PostStateTransitionExpectation.Unknown },
            "transition" => Request() with { ObservedTransition = PostStateObservedTransition.Unknown },
            "completeness" => Request() with { ObservationCompleteness = PostStateObservationCompleteness.Unknown },
            "trust" => Request() with { ObservationTrustLevel = PostStateObservationTrustLevel.Unknown },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static PostStateObservationRequest MissingNoChangeEvidenceRequest(string caseName) =>
        caseName switch
        {
            "pre-ref" => Request() with { PreStateRef = "" },
            "pre-fingerprint" => Request() with { PreStateFingerprint = "" },
            "observed-ref" => Request() with { ObservedPostStateRef = "" },
            "observed-fingerprint" => Request() with { ObservedPostStateFingerprint = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static PostStateObservationRequest MissingExpectedChangeEvidenceRequest(string caseName) =>
        caseName switch
        {
            "expected-ref" => ExpectedChangeRequest() with { ExpectedPostStateRef = "" },
            "expected-fingerprint" => ExpectedChangeRequest() with { ExpectedPostStateFingerprint = "" },
            "mutation-receipt" => ExpectedChangeRequest() with { MutationReceiptRef = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static string UnsafeReference(string caseName) =>
        caseName switch
        {
            "raw-patch" => "raw patch",
            "raw-git-output" => string.Concat("raw ", "gi", "t output"),
            "provider-response-body" => "provider response body",
            "credential" => string.Concat("to", "ken", "=", "fake"),
            "authority" => "source safe",
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static void AssertRequiresFreshGates(PostStateObservationDecision decision)
    {
        Assert.IsTrue(decision.RequiresFreshAuthority);
        Assert.IsTrue(decision.RequiresFreshValidation);
        Assert.IsTrue(decision.RequiresFreshConcurrentGuard);
        Assert.IsTrue(decision.RequiresFreshPostStateObservation);
        Assert.IsTrue(decision.RequiresHumanReview);
    }

    private static void AssertNoAuthority(PostStateObservationDecision decision)
    {
        AssertRequiresFreshGates(decision);
        CollectionAssert.Contains(decision.Warnings.ToList(), "post-state observation is read only");
        CollectionAssert.Contains(decision.Warnings.ToList(), "post-state observation is evidence only");
        CollectionAssert.Contains(decision.Warnings.ToList(), "post-state observation is not source safety");
        CollectionAssert.Contains(decision.Warnings.ToList(), "post-state observation is not retry authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "post-state observation is not mutation execution");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "post-state observation is not retry execution");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "post-state observation is not recovery execution");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "post-state observation is not rollback execution");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "post-state observation is not workflow continuation");
    }

    private static string E11CoreSource()
    {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "PostStateObservation*.cs");
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
            $"Unexpected forbidden marker found in E11 source: {forbidden}");
}
