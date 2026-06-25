using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE12DirtyWorktreeGuardTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecordedAtUtc = ObservedAtUtc.AddMinutes(1);
    private static readonly DateTimeOffset ExpiresAtUtc = ObservedAtUtc.AddMinutes(30);

    [TestMethod]
    public void ValidCleanWorktreeEvidenceMayProceedToNextAuthorityGateOnly()
    {
        var decision = Evaluate(Request());

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.MayProceedToNextAuthorityGate, decision.Decision);
        Assert.AreEqual(DirtyWorktreeGuardBlockKind.None, decision.BlockKind);
        AssertRequiresFreshGates(decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void PositiveDecisionStillRequiresFreshAuthority()
    {
        var decision = Evaluate(Request());

        Assert.IsTrue(decision.RequiresFreshAuthority);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void PositiveDecisionStillRequiresFreshValidation()
    {
        var decision = Evaluate(Request());

        Assert.IsTrue(decision.RequiresFreshValidation);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void PositiveDecisionStillRequiresFreshConcurrentGuard()
    {
        var decision = Evaluate(Request());

        Assert.IsTrue(decision.RequiresFreshConcurrentGuard);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void PositiveDecisionStillRequiresFreshPostStateObservation()
    {
        var decision = Evaluate(Request());

        Assert.IsTrue(decision.RequiresFreshPostStateObservation);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void PositiveDecisionStillRequiresHumanReview()
    {
        var decision = Evaluate(Request());

        Assert.IsTrue(decision.RequiresHumanReview);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("tenant", "DirtyWorktreeGuardTenantIdRequired")]
    [DataRow("project", "DirtyWorktreeGuardProjectIdRequired")]
    [DataRow("operation", "DirtyWorktreeGuardOperationIdRequired")]
    [DataRow("operation-invalid", "DirtyWorktreeGuardOperationIdInvalid")]
    [DataRow("correlation", "DirtyWorktreeGuardCorrelationIdRequired")]
    [DataRow("correlation-invalid", "DirtyWorktreeGuardCorrelationIdInvalid")]
    [DataRow("surface", "DirtyWorktreeGuardMutationSurfaceRequired")]
    [DataRow("attempt", "DirtyWorktreeGuardAttemptRefRequired")]
    [DataRow("target", "DirtyWorktreeGuardTargetRefRequired")]
    [DataRow("guard", "DirtyWorktreeGuardGuardRefRequired")]
    [DataRow("observed-non-utc", "DirtyWorktreeGuardObservedAtUtcMustBeUtc")]
    [DataRow("recorded-non-utc", "DirtyWorktreeGuardRecordedAtUtcMustBeUtc")]
    [DataRow("recorded-before-observed", "DirtyWorktreeGuardRecordedAtUtcBeforeObservedAtUtc")]
    [DataRow("guard-version", "DirtyWorktreeGuardGuardVersionRequired")]
    [DataRow("reason", "DirtyWorktreeGuardReasonCodeRequired")]
    [DataRow("source", "DirtyWorktreeGuardSourceRequired")]
    public void InvalidRequestFailsClosed(string caseName, string expectedReason)
    {
        var decision = Evaluate(InvalidRequest(caseName));

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual(DirtyWorktreeGuardBlockKind.InvalidRequest, decision.BlockKind);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownSubjectBlocksFailClosed()
    {
        var decision = Evaluate(Request() with { SubjectKind = DirtyWorktreeGuardSubjectKind.Unknown });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual("DirtyWorktreeGuardSubjectKindUnknown", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownWorktreeStateBlocksUnknownState()
    {
        var decision = Evaluate(Request() with { WorktreeState = DirtyWorktreeState.Unknown });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByUnknownWorktreeState, decision.Decision);
        Assert.AreEqual(DirtyWorktreeGuardBlockKind.UnknownWorktreeState, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownEvidenceKindBlocksUntrusted()
    {
        var decision = Evaluate(Request() with { EvidenceKind = DirtyWorktreeEvidenceKind.Unknown });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByUntrustedWorktreeEvidence, decision.Decision);
        Assert.AreEqual(DirtyWorktreeGuardBlockKind.UntrustedEvidence, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownTrustLevelBlocksUntrusted()
    {
        var decision = Evaluate(Request() with { EvidenceTrustLevel = DirtyWorktreeEvidenceTrustLevel.Unknown });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByUntrustedWorktreeEvidence, decision.Decision);
        Assert.AreEqual(DirtyWorktreeGuardBlockKind.UntrustedEvidence, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(DirtyWorktreeObservationFreshness.Unknown)]
    [DataRow(DirtyWorktreeObservationFreshness.NotTimestamped)]
    public void UnknownOrNotTimestampedFreshnessBlocksStale(DirtyWorktreeObservationFreshness freshness)
    {
        var decision = Evaluate(Request() with { ObservationFreshness = freshness });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByStaleWorktreeObservation, decision.Decision);
        Assert.AreEqual(DirtyWorktreeGuardBlockKind.StaleObservation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void StaleObservationBlocks()
    {
        var decision = Evaluate(Request() with { ObservationFreshness = DirtyWorktreeObservationFreshness.Stale });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByStaleWorktreeObservation, decision.Decision);
        Assert.AreEqual(DirtyWorktreeGuardBlockKind.StaleObservation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void MaxAgeExceededBlocksStale()
    {
        var decision = Evaluate(Request() with { RecordedAtUtc = ObservedAtUtc.AddSeconds(DirtyWorktreeGuardValidator.MaxObservationAgeSeconds + 1) });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByStaleWorktreeObservation, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ExpiredObservationBlocks()
    {
        var decision = Evaluate(Request() with { EvidenceExpiresAtUtc = RecordedAtUtc });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByExpiredWorktreeObservation, decision.Decision);
        Assert.AreEqual(DirtyWorktreeGuardBlockKind.ExpiredObservation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("worktree-observation", "DirtyWorktreeGuardWorktreeObservationRefRequired")]
    [DataRow("observed-head", "DirtyWorktreeGuardObservedHeadRefRequired")]
    [DataRow("observed-branch", "DirtyWorktreeGuardObservedBranchRefRequired")]
    [DataRow("observed-fingerprint", "DirtyWorktreeGuardObservedWorktreeFingerprintRequired")]
    public void CleanWorktreeRequiresObservedEvidence(string caseName, string expectedReason)
    {
        var decision = Evaluate(MissingCleanEvidenceRequest(caseName));

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByMissingWorktreeEvidence, decision.Decision);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(DirtyWorktreeState.Dirty)]
    [DataRow(DirtyWorktreeState.Modified)]
    [DataRow(DirtyWorktreeState.Untracked)]
    [DataRow(DirtyWorktreeState.Deleted)]
    [DataRow(DirtyWorktreeState.Renamed)]
    [DataRow(DirtyWorktreeState.Conflict)]
    [DataRow(DirtyWorktreeState.MergeInProgress)]
    [DataRow(DirtyWorktreeState.RebaseInProgress)]
    [DataRow(DirtyWorktreeState.CherryPickInProgress)]
    [DataRow(DirtyWorktreeState.DetachedHead)]
    [DataRow(DirtyWorktreeState.IndexLocked)]
    public void DirtyAndInProgressStatesBlock(DirtyWorktreeState state)
    {
        var decision = Evaluate(Request() with { WorktreeState = state });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByDirtyWorktree, decision.Decision);
        Assert.AreEqual(DirtyWorktreeGuardBlockKind.DirtyWorktree, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(DirtyWorktreeState.Unreadable)]
    [DataRow(DirtyWorktreeState.Unavailable)]
    public void UnreadableOrUnavailableBlocksUnknownState(DirtyWorktreeState state)
    {
        var decision = Evaluate(Request() with { WorktreeState = state });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByUnknownWorktreeState, decision.Decision);
        Assert.AreEqual(DirtyWorktreeGuardBlockKind.UnknownWorktreeState, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("head", "DirtyWorktreeGuardExpectedHeadMismatch")]
    [DataRow("branch", "DirtyWorktreeGuardExpectedBranchMismatch")]
    [DataRow("fingerprint", "DirtyWorktreeGuardExpectedFingerprintMismatch")]
    public void ExpectedObservedMismatchBlocksInconsistent(string caseName, string expectedReason)
    {
        var decision = Evaluate(MismatchedRequest(caseName));

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByInconsistentWorktreeEvidence, decision.Decision);
        Assert.AreEqual(DirtyWorktreeGuardBlockKind.InconsistentEvidence, decision.BlockKind);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SelfReportedWithoutCorroborationBlocksMissingEvidence()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = DirtyWorktreeEvidenceKind.WorktreeStateObservation,
            EvidenceTrustLevel = DirtyWorktreeEvidenceTrustLevel.SelfReported,
            PostStateObservationRef = "",
            FailureReceiptRef = "",
            MutationReceiptRef = "",
            ProviderStateRef = "",
            OperatorObservationRef = ""
        });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByMissingWorktreeEvidence, decision.Decision);
        Assert.AreEqual("DirtyWorktreeGuardSelfReportedCorroborationRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SelfReportedWithCorroborationStillCannotProceed()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = DirtyWorktreeEvidenceKind.WorktreeStateObservation,
            EvidenceTrustLevel = DirtyWorktreeEvidenceTrustLevel.SelfReported,
            PostStateObservationRef = "post-state-observation:e12"
        });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByUntrustedWorktreeEvidence, decision.Decision);
        Assert.AreEqual("DirtyWorktreeGuardSelfReportedCannotProceedToAuthorityGate", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void TestFixtureSourceAcceptedForTestsButCannotProceed()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceTrustLevel = DirtyWorktreeEvidenceTrustLevel.TestFixture,
            Source = "dirty-worktree-test"
        });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByUntrustedWorktreeEvidence, decision.Decision);
        Assert.AreEqual("DirtyWorktreeGuardTestFixtureCannotProceedToAuthorityGate", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SyntheticTestObservationCannotProceed()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = DirtyWorktreeEvidenceKind.SyntheticTestObservation,
            Source = "dirty-worktree-test"
        });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByUntrustedWorktreeEvidence, decision.Decision);
        Assert.AreEqual("DirtyWorktreeGuardTestFixtureCannotProceedToAuthorityGate", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SyntheticTestObservationWithProductionSourceBlocks()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = DirtyWorktreeEvidenceKind.SyntheticTestObservation,
            Source = "dirty-worktree-production"
        });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByUntrustedWorktreeEvidence, decision.Decision);
        Assert.AreEqual("DirtyWorktreeGuardTestFixtureSourceRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void PostStateObservationBackedEvidenceRequiresPostStateObservationRef()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceTrustLevel = DirtyWorktreeEvidenceTrustLevel.PostStateObservationBacked,
            PostStateObservationRef = ""
        });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByMissingWorktreeEvidence, decision.Decision);
        Assert.AreEqual("DirtyWorktreeGuardPostStateObservationRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ReceiptBackedEvidenceRequiresReceiptRef()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = DirtyWorktreeEvidenceKind.ReceiptBackedObservation,
            EvidenceTrustLevel = DirtyWorktreeEvidenceTrustLevel.ReceiptBacked,
            FailureReceiptRef = "",
            MutationReceiptRef = ""
        });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByMissingWorktreeEvidence, decision.Decision);
        Assert.AreEqual("DirtyWorktreeGuardReceiptRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ProviderMetadataBackedEvidenceRequiresProviderStateRef()
    {
        var decision = Evaluate(Request() with { ProviderStateRef = "" });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByMissingWorktreeEvidence, decision.Decision);
        Assert.AreEqual("DirtyWorktreeGuardProviderStateRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void OperatorObservedEvidenceRequiresOperatorObservationRef()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = DirtyWorktreeEvidenceKind.OperatorReportedObservation,
            EvidenceTrustLevel = DirtyWorktreeEvidenceTrustLevel.OperatorObserved,
            OperatorObservationRef = ""
        });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByMissingWorktreeEvidence, decision.Decision);
        Assert.AreEqual("DirtyWorktreeGuardOperatorObservationRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void CleanEvidenceWithTrustedRefsStillNotSourceSafety()
    {
        var decision = Evaluate(Request());

        CollectionAssert.Contains(decision.Warnings.ToList(), "clean worktree evidence is not mutation authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "dirty worktree guard is not source safety");
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void DirtyEvidenceDoesNotAuthorizeRollback()
    {
        var decision = Evaluate(Request() with { WorktreeState = DirtyWorktreeState.Dirty });

        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "dirty worktree guard is not rollback authority");
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ConflictEvidenceDoesNotAuthorizeRecovery()
    {
        var decision = Evaluate(Request() with { WorktreeState = DirtyWorktreeState.Conflict });

        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "dirty worktree guard is not recovery authority");
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void HeadMatchDoesNotAuthorizePush()
    {
        var decision = Evaluate(Request());

        CollectionAssert.Contains(decision.Warnings.ToList(), "head evidence is not push authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "dirty worktree guard is not push authority");
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void BranchMatchDoesNotAuthorizeCheckout()
    {
        var decision = Evaluate(Request());

        CollectionAssert.Contains(decision.Warnings.ToList(), "branch evidence is not checkout authority");
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("raw-git-status")]
    [DataRow("raw-file-list")]
    [DataRow("raw-git-output")]
    [DataRow("raw-patch")]
    [DataRow("raw-diff")]
    [DataRow("credential")]
    [DataRow("authority")]
    public void UnsafeMarkersAreRejected(string caseName)
    {
        var decision = Evaluate(Request() with { GuardRef = UnsafeReference(caseName) });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.AreEqual(DirtyWorktreeGuardBlockKind.UnsafePayload, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("patch-package:e12")]
    [DataRow("merge-target:e12")]
    [DataRow("release-candidate:e12")]
    [DataRow("deploy-target:e12")]
    public void ValidDomainRefsAreNotRejected(string targetRef)
    {
        var decision = Evaluate(Request() with { TargetRef = targetRef });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.MayProceedToNextAuthorityGate, decision.Decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(typeof(DirtyWorktreeGuardDecisionKind), "SafeToMutate")]
    [DataRow(typeof(DirtyWorktreeGuardDecisionKind), "SourceSafe")]
    [DataRow(typeof(DirtyWorktreeGuardDecisionKind), "CanCommit")]
    [DataRow(typeof(DirtyWorktreeGuardDecisionKind), "MutationAllowed")]
    public void VocabularyAvoidsAuthorityShapedNames(Type enumType, string forbidden)
    {
        var names = Enum.GetNames(enumType);

        CollectionAssert.DoesNotContain(names, forbidden);
    }

    [TestMethod]
    public void ContractTypesExposeNoCommandExecutorOrActionAuthorityFields()
    {
        var fieldNames = typeof(DirtyWorktreeGuardDecision)
            .GetProperties()
            .Select(static property => property.Name)
            .Concat(typeof(DirtyWorktreeGuardRequest).GetProperties().Select(static property => property.Name))
            .ToArray();

        CollectionAssert.DoesNotContain(fieldNames, "CommandText");
        CollectionAssert.DoesNotContain(fieldNames, "ExecutorRef");
        CollectionAssert.DoesNotContain(fieldNames, "ActionAuthority");
        CollectionAssert.DoesNotContain(fieldNames, "CanMutate");
        CollectionAssert.DoesNotContain(fieldNames, "CanApply");
        CollectionAssert.DoesNotContain(fieldNames, "CanCommit");
        CollectionAssert.DoesNotContain(fieldNames, "CanPush");
    }

    [TestMethod]
    public void StaticScan_E12AddsNoExecutorWiring()
    {
        var source = StripStrings(E12CoreSource());

        AssertDoesNotContain(source, "Executor");
        AssertDoesNotContain(source, "RunProcessAsync");
        AssertDoesNotContain(source, "ProcessStartInfo");
        AssertDoesNotContain(source, "IControlled");
    }

    [TestMethod]
    public void StaticScan_E12AddsNoGitGithubSourceOrWorktreeAccess()
    {
        var source = StripStrings(E12CoreSource());

        AssertDoesNotContain(source, "HttpClient");
        AssertDoesNotContain(source, ".git");
        AssertDoesNotContain(source, "GitHub");
        AssertDoesNotContain(source, "RepositoryRoot");
        AssertDoesNotContain(source, "Directory.Get");
        AssertDoesNotContain(source, "File.Read");
    }

    [TestMethod]
    public void StaticScan_E12AddsNoFilesystemMutation()
    {
        var source = StripStrings(E12CoreSource());

        AssertDoesNotContain(source, "File.Write");
        AssertDoesNotContain(source, "Directory.Create");
        AssertDoesNotContain(source, "Delete(");
        AssertDoesNotContain(source, "Move(");
        AssertDoesNotContain(source, "Copy(");
        AssertDoesNotContain(source, "StreamWriter");
    }

    [TestMethod]
    public void StaticScan_E12AddsNoLockAcquisitionReleaseOrRenewal()
    {
        var source = StripStrings(E12CoreSource());

        AssertDoesNotContain(source, "Acquire");
        AssertDoesNotContain(source, "Release");
        AssertDoesNotContain(source, "Renew");
        AssertDoesNotContain(source, "Enforce");
        AssertDoesNotContain(source, "MutationLeaseStore");
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
    public void FingerprintChangesWhenWorktreeStateChanges()
    {
        var first = Evaluate(Request());
        var second = Evaluate(Request() with { WorktreeState = DirtyWorktreeState.Modified });

        Assert.AreNotEqual(first.RecordFingerprint, second.RecordFingerprint);
    }

    [TestMethod]
    public void FingerprintChangesWhenObservedFingerprintChanges()
    {
        var first = Evaluate(Request());
        var second = Evaluate(Request() with
        {
            ExpectedWorktreeFingerprint = "",
            ObservedWorktreeFingerprint = "worktree-fingerprint:e12:different"
        });

        Assert.AreNotEqual(first.RecordFingerprint, second.RecordFingerprint);
    }

    [TestMethod]
    public void FingerprintDoesNotIncludeRawPayloads()
    {
        var decision = Evaluate(Request() with { GuardRef = UnsafeReference("raw-patch") });

        Assert.AreEqual(DirtyWorktreeGuardDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.IsFalse(decision.RecordFingerprint.Contains("raw patch", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(decision.RecordFingerprint.Contains("[unsafe]", StringComparison.Ordinal));
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("A clean worktree observation is evidence. It is not source authority.")]
    [DataRow("A dirty worktree is a stop sign. A clean worktree is not a green light.")]
    [DataRow("source safety")]
    [DataRow("commit authority")]
    [DataRow("push authority")]
    [DataRow("rollback authority")]
    [DataRow("workflow continuation")]
    public void ReceiptContainsBoundaryLines(string phrase)
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "E12_DIRTY_WORKTREE_GUARD.md"));

        StringAssert.Contains(receipt, phrase);
    }

    private static DirtyWorktreeGuardDecision Evaluate(DirtyWorktreeGuardRequest request) =>
        new DirtyWorktreeGuardService().Evaluate(request);

    private static DirtyWorktreeGuardRequest Request() =>
        new()
        {
            TenantId = "tenant-e12",
            ProjectId = "project-e12",
            OperationId = "op_000000000000e012",
            CorrelationId = "corr_0000000000e01200",
            MutationSurface = MutationLeaseSurfaceKind.SourceApply,
            AttemptRef = "attempt:e12",
            TargetRef = "target:e12",
            GuardRef = "dirty-worktree-guard:e12",
            SubjectKind = DirtyWorktreeGuardSubjectKind.RepositoryWorktree,
            WorktreeState = DirtyWorktreeState.Clean,
            EvidenceKind = DirtyWorktreeEvidenceKind.WorktreeStateObservation,
            EvidenceTrustLevel = DirtyWorktreeEvidenceTrustLevel.ProviderMetadataBacked,
            ObservationFreshness = DirtyWorktreeObservationFreshness.Fresh,
            WorktreeObservationRef = "worktree-observation:e12",
            PostStateObservationRef = "post-state-observation:e12",
            FailureClassificationRef = "failure-classification:e12",
            FailureReceiptRef = "failure-receipt:e12",
            MutationReceiptRef = "mutation-receipt:e12",
            ProviderStateRef = "provider-state:e12",
            OperatorObservationRef = "operator-observation:e12",
            ExpectedHeadRef = "head:e12",
            ObservedHeadRef = "head:e12",
            ExpectedBranchRef = "branch:e12",
            ObservedBranchRef = "branch:e12",
            ExpectedWorktreeFingerprint = "worktree-fingerprint:e12",
            ObservedWorktreeFingerprint = "worktree-fingerprint:e12",
            ObservedAtUtc = ObservedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            EvidenceExpiresAtUtc = ExpiresAtUtc,
            GuardVersion = "dirty-worktree-guard-v1",
            ReasonCode = "dirty-worktree-observed",
            Source = "dirty-worktree-guard"
        };

    private static DirtyWorktreeGuardRequest InvalidRequest(string caseName) =>
        caseName switch
        {
            "tenant" => Request() with { TenantId = "" },
            "project" => Request() with { ProjectId = "" },
            "operation" => Request() with { OperationId = "" },
            "operation-invalid" => Request() with { OperationId = "run:e12" },
            "correlation" => Request() with { CorrelationId = "" },
            "correlation-invalid" => Request() with { CorrelationId = "op_000000000000e012" },
            "surface" => Request() with { MutationSurface = MutationLeaseSurfaceKind.Unknown },
            "attempt" => Request() with { AttemptRef = "" },
            "target" => Request() with { TargetRef = "" },
            "guard" => Request() with { GuardRef = "" },
            "observed-non-utc" => Request() with { ObservedAtUtc = new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.FromHours(12)) },
            "recorded-non-utc" => Request() with { RecordedAtUtc = new DateTimeOffset(2026, 6, 25, 0, 1, 0, TimeSpan.FromHours(12)) },
            "recorded-before-observed" => Request() with { RecordedAtUtc = ObservedAtUtc.AddSeconds(-1) },
            "guard-version" => Request() with { GuardVersion = "" },
            "reason" => Request() with { ReasonCode = "" },
            "source" => Request() with { Source = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static DirtyWorktreeGuardRequest MissingCleanEvidenceRequest(string caseName) =>
        caseName switch
        {
            "worktree-observation" => Request() with { WorktreeObservationRef = "" },
            "observed-head" => Request() with { ObservedHeadRef = "" },
            "observed-branch" => Request() with { ObservedBranchRef = "" },
            "observed-fingerprint" => Request() with { ObservedWorktreeFingerprint = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static DirtyWorktreeGuardRequest MismatchedRequest(string caseName) =>
        caseName switch
        {
            "head" => Request() with { ObservedHeadRef = "head:e12:moved" },
            "branch" => Request() with { ObservedBranchRef = "branch:e12:moved" },
            "fingerprint" => Request() with { ObservedWorktreeFingerprint = "worktree-fingerprint:e12:moved" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static string UnsafeReference(string caseName) =>
        caseName switch
        {
            "raw-git-status" => string.Concat("raw ", "git status"),
            "raw-file-list" => "raw file list",
            "raw-git-output" => string.Concat("raw ", "git output"),
            "raw-patch" => "raw patch",
            "raw-diff" => "raw diff",
            "credential" => string.Concat("to", "ken", "=", "fake"),
            "authority" => "safe to mutate",
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static void AssertRequiresFreshGates(DirtyWorktreeGuardDecision decision)
    {
        Assert.IsTrue(decision.RequiresFreshAuthority);
        Assert.IsTrue(decision.RequiresFreshValidation);
        Assert.IsTrue(decision.RequiresFreshConcurrentGuard);
        Assert.IsTrue(decision.RequiresFreshPostStateObservation);
        Assert.IsTrue(decision.RequiresHumanReview);
    }

    private static void AssertNoAuthority(DirtyWorktreeGuardDecision decision)
    {
        AssertRequiresFreshGates(decision);
        CollectionAssert.Contains(decision.Warnings.ToList(), "dirty worktree guard is read only");
        CollectionAssert.Contains(decision.Warnings.ToList(), "dirty worktree guard does not inspect source");
        CollectionAssert.Contains(decision.Warnings.ToList(), "dirty worktree guard does not call git");
        CollectionAssert.Contains(decision.Warnings.ToList(), "dirty worktree evidence is not source authority");
        CollectionAssert.Contains(decision.Warnings.ToList(), "fresh authority is required before any mutation");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "dirty worktree guard is not mutation execution");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "dirty worktree guard is not source apply authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "dirty worktree guard is not commit authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "dirty worktree guard is not push authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "dirty worktree guard is not pull request authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "dirty worktree guard is not retry authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "dirty worktree guard is not recovery authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "dirty worktree guard is not rollback authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "dirty worktree guard is not workflow continuation");
    }

    private static string E12CoreSource()
    {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "DirtyWorktreeGuard*.cs");
        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

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
            $"Unexpected forbidden marker found in E12 source: {forbidden}");
}
