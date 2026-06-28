using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE13MovedBaseGuardTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecordedAtUtc = ObservedAtUtc.AddMinutes(1);
    private static readonly DateTimeOffset ExpiresAtUtc = ObservedAtUtc.AddMinutes(30);

    [TestMethod]
    public void ValidMatchingBaseEvidenceMayProceedToNextAuthorityGateOnly()
    {
        var decision = Evaluate(Request());

        Assert.AreEqual(MovedBaseGuardDecisionKind.MayProceedToNextAuthorityGate, decision.Decision);
        Assert.AreEqual(MovedBaseGuardBlockKind.None, decision.BlockKind);
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
    public void PositiveDecisionStillRequiresDirtyWorktreeGuard()
    {
        var decision = Evaluate(Request());

        Assert.IsTrue(decision.RequiresDirtyWorktreeGuard);
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
    [DataRow("tenant", "MovedBaseGuardTenantIdRequired")]
    [DataRow("project", "MovedBaseGuardProjectIdRequired")]
    [DataRow("operation", "MovedBaseGuardOperationIdRequired")]
    [DataRow("operation-invalid", "MovedBaseGuardOperationIdInvalid")]
    [DataRow("correlation", "MovedBaseGuardCorrelationIdRequired")]
    [DataRow("correlation-invalid", "MovedBaseGuardCorrelationIdInvalid")]
    [DataRow("surface", "MovedBaseGuardMutationSurfaceRequired")]
    [DataRow("attempt", "MovedBaseGuardAttemptRefRequired")]
    [DataRow("target", "MovedBaseGuardTargetRefRequired")]
    [DataRow("guard", "MovedBaseGuardGuardRefRequired")]
    [DataRow("observed-non-utc", "MovedBaseGuardObservedAtUtcMustBeUtc")]
    [DataRow("recorded-non-utc", "MovedBaseGuardRecordedAtUtcMustBeUtc")]
    [DataRow("recorded-before-observed", "MovedBaseGuardRecordedAtUtcBeforeObservedAtUtc")]
    [DataRow("guard-version", "MovedBaseGuardGuardVersionRequired")]
    [DataRow("reason", "MovedBaseGuardReasonCodeRequired")]
    [DataRow("source", "MovedBaseGuardSourceRequired")]
    public void InvalidRequestFailsClosed(string caseName, string expectedReason)
    {
        var decision = Evaluate(InvalidRequest(caseName));

        Assert.AreEqual(MovedBaseGuardDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual(MovedBaseGuardBlockKind.InvalidRequest, decision.BlockKind);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownSubjectBlocksFailClosed()
    {
        var decision = Evaluate(Request() with { SubjectKind = MovedBaseGuardSubjectKind.Unknown });

        Assert.AreEqual(MovedBaseGuardDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual("MovedBaseGuardSubjectKindUnknown", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownObservedStateBlocksUnknownState()
    {
        var decision = Evaluate(Request() with { ObservedState = MovedBaseObservedState.Unknown });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByUnknownBaseState, decision.Decision);
        Assert.AreEqual(MovedBaseGuardBlockKind.UnknownBaseState, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownEvidenceKindBlocksUntrusted()
    {
        var decision = Evaluate(Request() with { EvidenceKind = MovedBaseEvidenceKind.Unknown });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByUntrustedRefEvidence, decision.Decision);
        Assert.AreEqual(MovedBaseGuardBlockKind.UntrustedEvidence, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownTrustLevelBlocksUntrusted()
    {
        var decision = Evaluate(Request() with { EvidenceTrustLevel = MovedBaseEvidenceTrustLevel.Unknown });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByUntrustedRefEvidence, decision.Decision);
        Assert.AreEqual(MovedBaseGuardBlockKind.UntrustedEvidence, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(MovedBaseObservationFreshness.Unknown)]
    [DataRow(MovedBaseObservationFreshness.NotTimestamped)]
    public void UnknownOrNotTimestampedFreshnessBlocksStale(MovedBaseObservationFreshness freshness)
    {
        var decision = Evaluate(Request() with { ObservationFreshness = freshness });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByStaleRefObservation, decision.Decision);
        Assert.AreEqual(MovedBaseGuardBlockKind.StaleObservation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void StaleObservationBlocks()
    {
        var decision = Evaluate(Request() with { ObservationFreshness = MovedBaseObservationFreshness.Stale });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByStaleRefObservation, decision.Decision);
        Assert.AreEqual(MovedBaseGuardBlockKind.StaleObservation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void MaxAgeExceededBlocksStale()
    {
        var decision = Evaluate(Request() with { RecordedAtUtc = ObservedAtUtc.AddSeconds(MovedBaseGuardValidator.MaxObservationAgeSeconds + 1) });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByStaleRefObservation, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ExpiredObservationBlocks()
    {
        var decision = Evaluate(Request() with { EvidenceExpiresAtUtc = RecordedAtUtc });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByExpiredRefObservation, decision.Decision);
        Assert.AreEqual(MovedBaseGuardBlockKind.ExpiredObservation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("ref-observation", "MovedBaseGuardRefObservationRefRequired")]
    [DataRow("observed-base", "MovedBaseGuardObservedBaseRefRequired")]
    [DataRow("observed-head", "MovedBaseGuardObservedHeadRefRequired")]
    [DataRow("observed-branch", "MovedBaseGuardObservedBranchRefRequired")]
    [DataRow("observed-target-fingerprint", "MovedBaseGuardObservedTargetFingerprintRequired")]
    public void MatchingStateRequiresObservedEvidence(string caseName, string expectedReason)
    {
        var decision = Evaluate(MissingMatchingEvidenceRequest(caseName));

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByMissingRefEvidence, decision.Decision);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(MovedBaseObservedState.BaseMoved, MovedBaseGuardDecisionKind.BlockedByMovedBase, MovedBaseGuardBlockKind.MovedBase)]
    [DataRow(MovedBaseObservedState.MergeBaseMoved, MovedBaseGuardDecisionKind.BlockedByMovedMergeBase, MovedBaseGuardBlockKind.MovedMergeBase)]
    [DataRow(MovedBaseObservedState.HeadMoved, MovedBaseGuardDecisionKind.BlockedByMovedHead, MovedBaseGuardBlockKind.MovedHead)]
    [DataRow(MovedBaseObservedState.RemoteHeadMoved, MovedBaseGuardDecisionKind.BlockedByMovedRemoteHead, MovedBaseGuardBlockKind.MovedRemoteHead)]
    [DataRow(MovedBaseObservedState.BranchMoved, MovedBaseGuardDecisionKind.BlockedByMovedBranch, MovedBaseGuardBlockKind.MovedBranch)]
    public void MovedStatesBlockWithSpecificDecision(
        MovedBaseObservedState state,
        MovedBaseGuardDecisionKind expectedDecision,
        MovedBaseGuardBlockKind expectedBlock)
    {
        var decision = Evaluate(Request() with { ObservedState = state });

        Assert.AreEqual(expectedDecision, decision.Decision);
        Assert.AreEqual(expectedBlock, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(MovedBaseObservedState.Diverged)]
    [DataRow(MovedBaseObservedState.Ahead)]
    [DataRow(MovedBaseObservedState.Behind)]
    public void DivergedAheadOrBehindBlocksDivergedRef(MovedBaseObservedState state)
    {
        var decision = Evaluate(Request() with { ObservedState = state });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByDivergedRef, decision.Decision);
        Assert.AreEqual(MovedBaseGuardBlockKind.DivergedRef, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(MovedBaseObservedState.Missing)]
    [DataRow(MovedBaseObservedState.Deleted)]
    [DataRow(MovedBaseObservedState.Unavailable)]
    [DataRow(MovedBaseObservedState.Ambiguous)]
    public void MissingDeletedUnavailableOrAmbiguousBlocksUnknownState(MovedBaseObservedState state)
    {
        var decision = Evaluate(Request() with { ObservedState = state });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByUnknownBaseState, decision.Decision);
        Assert.AreEqual(MovedBaseGuardBlockKind.UnknownBaseState, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("base", "MovedBaseGuardExpectedBaseMismatch")]
    [DataRow("head", "MovedBaseGuardExpectedHeadMismatch")]
    [DataRow("remote-head", "MovedBaseGuardExpectedRemoteHeadMismatch")]
    [DataRow("branch", "MovedBaseGuardExpectedBranchMismatch")]
    [DataRow("merge-base-fingerprint", "MovedBaseGuardExpectedMergeBaseFingerprintMismatch")]
    [DataRow("target-fingerprint", "MovedBaseGuardExpectedTargetFingerprintMismatch")]
    public void ExpectedObservedMismatchBlocksInconsistent(string caseName, string expectedReason)
    {
        var decision = Evaluate(MismatchedRequest(caseName));

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByInconsistentRefEvidence, decision.Decision);
        Assert.AreEqual(MovedBaseGuardBlockKind.InconsistentEvidence, decision.BlockKind);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SelfReportedWithoutCorroborationBlocksMissingEvidence()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = MovedBaseEvidenceKind.RefObservation,
            EvidenceTrustLevel = MovedBaseEvidenceTrustLevel.SelfReported,
            PostStateObservationRef = "",
            DirtyWorktreeGuardRef = "",
            ValidationReceiptRef = "",
            PatchPackageRef = "",
            CommitPackageRef = "",
            PushReceiptRef = "",
            PullRequestProviderStateRef = "",
            ProviderStateRef = "",
            OperatorObservationRef = ""
        });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByMissingRefEvidence, decision.Decision);
        Assert.AreEqual("MovedBaseGuardSelfReportedCorroborationRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SelfReportedWithCorroborationStillCannotProceed()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = MovedBaseEvidenceKind.RefObservation,
            EvidenceTrustLevel = MovedBaseEvidenceTrustLevel.SelfReported,
            PostStateObservationRef = "post-state-observation:e13"
        });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByUntrustedRefEvidence, decision.Decision);
        Assert.AreEqual("MovedBaseGuardSelfReportedCannotProceedToAuthorityGate", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void TestFixtureSourceAcceptedForTestsButCannotProceed()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceTrustLevel = MovedBaseEvidenceTrustLevel.TestFixture,
            Source = "moved-base-test"
        });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByUntrustedRefEvidence, decision.Decision);
        Assert.AreEqual("MovedBaseGuardTestFixtureCannotProceedToAuthorityGate", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SyntheticTestObservationCannotProceed()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = MovedBaseEvidenceKind.SyntheticTestObservation,
            Source = "moved-base-test"
        });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByUntrustedRefEvidence, decision.Decision);
        Assert.AreEqual("MovedBaseGuardTestFixtureCannotProceedToAuthorityGate", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SyntheticTestObservationWithProductionSourceBlocks()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = MovedBaseEvidenceKind.SyntheticTestObservation,
            Source = "moved-base-production"
        });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByUntrustedRefEvidence, decision.Decision);
        Assert.AreEqual("MovedBaseGuardTestFixtureSourceRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void PostStateObservationBackedEvidenceRequiresPostStateObservationRef()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceTrustLevel = MovedBaseEvidenceTrustLevel.PostStateObservationBacked,
            PostStateObservationRef = ""
        });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByMissingRefEvidence, decision.Decision);
        Assert.AreEqual("MovedBaseGuardPostStateObservationRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void DirtyWorktreeGuardBackedEvidenceRequiresDirtyWorktreeGuardRef()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceTrustLevel = MovedBaseEvidenceTrustLevel.DirtyWorktreeGuardBacked,
            DirtyWorktreeGuardRef = ""
        });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByMissingRefEvidence, decision.Decision);
        Assert.AreEqual("MovedBaseGuardDirtyWorktreeGuardRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(MovedBaseEvidenceKind.ValidationReceipt, "validation", "MovedBaseGuardValidationReceiptRefRequired")]
    [DataRow(MovedBaseEvidenceKind.PatchPackageReceipt, "patch", "MovedBaseGuardPatchPackageRefRequired")]
    [DataRow(MovedBaseEvidenceKind.CommitPackageReceipt, "commit", "MovedBaseGuardCommitPackageRefRequired")]
    [DataRow(MovedBaseEvidenceKind.PushReceipt, "push", "MovedBaseGuardPushReceiptRefRequired")]
    public void ReceiptEvidenceRequiresMatchingRef(
        MovedBaseEvidenceKind evidenceKind,
        string caseName,
        string expectedReason)
    {
        var decision = Evaluate(MissingReceiptRequest(evidenceKind, caseName));

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByMissingRefEvidence, decision.Decision);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void GenericReceiptBackedTrustRequiresAtLeastOneReceiptRef()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = MovedBaseEvidenceKind.RefObservation,
            EvidenceTrustLevel = MovedBaseEvidenceTrustLevel.ReceiptBacked,
            ValidationReceiptRef = "",
            PatchPackageRef = "",
            CommitPackageRef = "",
            PushReceiptRef = ""
        });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByMissingRefEvidence, decision.Decision);
        Assert.AreEqual("MovedBaseGuardReceiptRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ProviderMetadataBackedEvidenceRequiresProviderStateRef()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceTrustLevel = MovedBaseEvidenceTrustLevel.ProviderMetadataBacked,
            ProviderStateRef = ""
        });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByMissingRefEvidence, decision.Decision);
        Assert.AreEqual("MovedBaseGuardProviderStateRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void PullRequestProviderMetadataRequiresPullRequestProviderStateRef()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = MovedBaseEvidenceKind.PullRequestProviderMetadata,
            PullRequestProviderStateRef = ""
        });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByMissingRefEvidence, decision.Decision);
        Assert.AreEqual("MovedBaseGuardPullRequestProviderStateRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void OperatorObservedEvidenceRequiresOperatorObservationRef()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = MovedBaseEvidenceKind.OperatorReportedObservation,
            EvidenceTrustLevel = MovedBaseEvidenceTrustLevel.OperatorObserved,
            OperatorObservationRef = ""
        });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByMissingRefEvidence, decision.Decision);
        Assert.AreEqual("MovedBaseGuardOperatorObservationRefRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void MatchingEvidenceWithTrustedRefsStillNotSourceSafety()
    {
        var decision = Evaluate(Request());

        CollectionAssert.Contains(decision.Warnings.ToList(), "matching base evidence is not source authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "moved-base guard is not source safety");
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void BaseMovedEvidenceDoesNotAuthorizeRebase()
    {
        var decision = Evaluate(Request() with { ObservedState = MovedBaseObservedState.BaseMoved });

        CollectionAssert.Contains(decision.Warnings.ToList(), "moved base evidence is not rebase authority");
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void HeadMovedEvidenceDoesNotAuthorizePullFetchOrPush()
    {
        var decision = Evaluate(Request() with { ObservedState = MovedBaseObservedState.HeadMoved });

        CollectionAssert.Contains(decision.Warnings.ToList(), "matching head evidence is not push authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "moved-base guard is not push authority");
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void BranchMovedEvidenceDoesNotAuthorizeCheckout()
    {
        var decision = Evaluate(Request() with { ObservedState = MovedBaseObservedState.BranchMoved });

        CollectionAssert.Contains(decision.Warnings.ToList(), "matching branch evidence is not checkout authority");
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void DivergenceEvidenceDoesNotAuthorizeMerge()
    {
        var decision = Evaluate(Request() with { ObservedState = MovedBaseObservedState.Diverged });

        CollectionAssert.Contains(decision.Warnings.ToList(), "matching merge-base evidence is not merge authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "moved-base guard is not merge authority");
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("raw-git-log")]
    [DataRow("raw-git-rev-parse")]
    [DataRow("raw-git-merge-base")]
    [DataRow("raw-branch-list")]
    [DataRow("raw-commit-graph")]
    [DataRow("raw-provider-branch-response")]
    [DataRow("raw-pull-request-response")]
    [DataRow("raw-patch")]
    [DataRow("raw-diff")]
    [DataRow("credential")]
    [DataRow("authority")]
    public void UnsafeMarkersAreRejected(string caseName)
    {
        var decision = Evaluate(Request() with { GuardRef = UnsafeReference(caseName) });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.AreEqual(MovedBaseGuardBlockKind.UnsafePayload, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("patch-package:e13")]
    [DataRow("merge-target:e13")]
    [DataRow("release-candidate:e13")]
    [DataRow("deploy-target:e13")]
    public void ValidDomainRefsAreNotRejected(string targetRef)
    {
        var decision = Evaluate(Request() with { TargetRef = targetRef });

        Assert.AreEqual(MovedBaseGuardDecisionKind.MayProceedToNextAuthorityGate, decision.Decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(typeof(MovedBaseGuardDecisionKind), "SafeToApply")]
    [DataRow(typeof(MovedBaseGuardDecisionKind), "SafeToCommit")]
    [DataRow(typeof(MovedBaseGuardDecisionKind), "SafeToPush")]
    [DataRow(typeof(MovedBaseGuardDecisionKind), "SafeToMerge")]
    [DataRow(typeof(MovedBaseGuardDecisionKind), "SourceSafe")]
    [DataRow(typeof(MovedBaseGuardDecisionKind), "CanCommit")]
    public void VocabularyAvoidsAuthorityShapedNames(Type enumType, string forbidden)
    {
        var names = Enum.GetNames(enumType);

        CollectionAssert.DoesNotContain(names, forbidden);
    }

    [TestMethod]
    public void ContractTypesExposeNoCommandExecutorOrActionAuthorityFields()
    {
        var fieldNames = typeof(MovedBaseGuardDecision)
            .GetProperties()
            .Select(static property => property.Name)
            .Concat(typeof(MovedBaseGuardRequest).GetProperties().Select(static property => property.Name))
            .ToArray();

        CollectionAssert.DoesNotContain(fieldNames, "CommandText");
        CollectionAssert.DoesNotContain(fieldNames, "ExecutorRef");
        CollectionAssert.DoesNotContain(fieldNames, "ActionAuthority");
        CollectionAssert.DoesNotContain(fieldNames, "CanApply");
        CollectionAssert.DoesNotContain(fieldNames, "CanCommit");
        CollectionAssert.DoesNotContain(fieldNames, "CanPush");
        CollectionAssert.DoesNotContain(fieldNames, "CanMerge");
    }

    [TestMethod]
    public void StaticScan_E13AddsNoExecutorWiring()
    {
        var source = StripStrings(E13CoreSource());

        AssertDoesNotContain(source, "Executor");
        AssertDoesNotContain(source, "RunProcessAsync");
        AssertDoesNotContain(source, "ProcessStartInfo");
        AssertDoesNotContain(source, "IControlled");
    }

    [TestMethod]
    public void StaticScan_E13AddsNoGitGithubSourceOrWorktreeAccess()
    {
        var source = StripStrings(E13CoreSource());

        AssertDoesNotContain(source, "HttpClient");
        AssertDoesNotContain(source, ".git");
        AssertDoesNotContain(source, "Directory.Get");
        AssertDoesNotContain(source, "File.Read");
    }

    [TestMethod]
    public void StaticScan_E13AddsNoProcessExecution()
    {
        var source = StripStrings(E13CoreSource());

        AssertDoesNotContain(source, "Process.");
        AssertDoesNotContain(source, "ProcessStartInfo");
        AssertDoesNotContain(source, "cmd.exe");
        AssertDoesNotContain(source, "powershell");
        AssertDoesNotContain(source, "bash");
    }

    [TestMethod]
    public void StaticScan_E13AddsNoFilesystemMutation()
    {
        var source = StripStrings(E13CoreSource());

        AssertDoesNotContain(source, "File.Write");
        AssertDoesNotContain(source, "Directory.Create");
        AssertDoesNotContain(source, "Delete(");
        AssertDoesNotContain(source, "Move(");
        AssertDoesNotContain(source, "Copy(");
        AssertDoesNotContain(source, "StreamWriter");
    }

    [TestMethod]
    public void StaticScan_E13AddsNoLockAcquisitionReleaseOrRenewal()
    {
        var source = StripStrings(E13CoreSource());

        AssertDoesNotContain(source, "Acquire");
        AssertDoesNotContain(source, "ReleaseAsync");
        AssertDoesNotContain(source, "ReleaseLease");
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
    public void FingerprintChangesWhenObservedStateChanges()
    {
        var first = Evaluate(Request());
        var second = Evaluate(Request() with { ObservedState = MovedBaseObservedState.BaseMoved });

        Assert.AreNotEqual(first.RecordFingerprint, second.RecordFingerprint);
    }

    [TestMethod]
    public void FingerprintChangesWhenObservedBaseChanges()
    {
        var first = Evaluate(Request());
        var second = Evaluate(Request() with
        {
            ExpectedBaseRef = "",
            ObservedBaseRef = "base:e13:moved"
        });

        Assert.AreNotEqual(first.RecordFingerprint, second.RecordFingerprint);
    }

    [TestMethod]
    public void FingerprintChangesWhenObservedHeadChanges()
    {
        var first = Evaluate(Request());
        var second = Evaluate(Request() with
        {
            ExpectedHeadRef = "",
            ObservedHeadRef = "head:e13:moved"
        });

        Assert.AreNotEqual(first.RecordFingerprint, second.RecordFingerprint);
    }

    [TestMethod]
    public void FingerprintChangesWhenObservedFingerprintChanges()
    {
        var first = Evaluate(Request());
        var second = Evaluate(Request() with
        {
            ExpectedTargetFingerprint = "",
            ObservedTargetFingerprint = "target-fingerprint:e13:moved"
        });

        Assert.AreNotEqual(first.RecordFingerprint, second.RecordFingerprint);
    }

    [TestMethod]
    public void FingerprintDoesNotIncludeRawPayloads()
    {
        var decision = Evaluate(Request() with { GuardRef = UnsafeReference("raw-git-log") });

        Assert.AreEqual(MovedBaseGuardDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.IsFalse(decision.RecordFingerprint.Contains("raw git log", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(decision.RecordFingerprint.Contains("[unsafe]", StringComparison.Ordinal));
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("A matching base is evidence. It is not permission to apply, commit, push, or merge.")]
    [DataRow("Base did not move is not a green light. Base moved is a stop sign.")]
    [DataRow("source safety")]
    [DataRow("source apply authority")]
    [DataRow("commit authority")]
    [DataRow("push authority")]
    [DataRow("merge authority")]
    [DataRow("rollback authority")]
    [DataRow("workflow continuation")]
    public void ReceiptContainsBoundaryLines(string phrase)
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "E13_MOVED_BASE_GUARD.md"));

        StringAssert.Contains(receipt, phrase);
    }

    private static MovedBaseGuardDecision Evaluate(MovedBaseGuardRequest request) =>
        new MovedBaseGuardService().Evaluate(request);

    private static MovedBaseGuardRequest Request() =>
        new()
        {
            TenantId = "tenant-e13",
            ProjectId = "project-e13",
            OperationId = "op_000000000000e013",
            CorrelationId = "corr_0000000000e01300",
            MutationSurface = MutationLeaseSurfaceKind.SourceApply,
            AttemptRef = "attempt:e13",
            TargetRef = "target:e13",
            GuardRef = "moved-base-guard:e13",
            SubjectKind = MovedBaseGuardSubjectKind.SourceApplyTarget,
            ObservedState = MovedBaseObservedState.Matching,
            EvidenceKind = MovedBaseEvidenceKind.RefObservation,
            EvidenceTrustLevel = MovedBaseEvidenceTrustLevel.RefObservationBacked,
            ObservationFreshness = MovedBaseObservationFreshness.Fresh,
            RefObservationRef = "ref-observation:e13",
            PostStateObservationRef = "post-state-observation:e13",
            DirtyWorktreeGuardRef = "dirty-worktree-guard:e13",
            ValidationReceiptRef = "validation-receipt:e13",
            PatchPackageRef = "patch-package:e13",
            CommitPackageRef = "commit-package:e13",
            PushReceiptRef = "push-receipt:e13",
            PullRequestProviderStateRef = "pull-request-provider-state:e13",
            ProviderStateRef = "provider-state:e13",
            OperatorObservationRef = "operator-observation:e13",
            ExpectedBaseRef = "base:e13",
            ObservedBaseRef = "base:e13",
            ExpectedHeadRef = "head:e13",
            ObservedHeadRef = "head:e13",
            ExpectedRemoteHeadRef = "remote-head:e13",
            ObservedRemoteHeadRef = "remote-head:e13",
            ExpectedBranchRef = "branch:e13",
            ObservedBranchRef = "branch:e13",
            ExpectedMergeBaseFingerprint = "merge-base-fingerprint:e13",
            ObservedMergeBaseFingerprint = "merge-base-fingerprint:e13",
            ExpectedTargetFingerprint = "target-fingerprint:e13",
            ObservedTargetFingerprint = "target-fingerprint:e13",
            ObservedAtUtc = ObservedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            EvidenceExpiresAtUtc = ExpiresAtUtc,
            GuardVersion = "moved-base-guard-v1",
            ReasonCode = "moved-base-observed",
            Source = "moved-base-guard"
        };

    private static MovedBaseGuardRequest InvalidRequest(string caseName) =>
        caseName switch
        {
            "tenant" => Request() with { TenantId = "" },
            "project" => Request() with { ProjectId = "" },
            "operation" => Request() with { OperationId = "" },
            "operation-invalid" => Request() with { OperationId = "run:e13" },
            "correlation" => Request() with { CorrelationId = "" },
            "correlation-invalid" => Request() with { CorrelationId = "op_000000000000e013" },
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

    private static MovedBaseGuardRequest MissingMatchingEvidenceRequest(string caseName) =>
        caseName switch
        {
            "ref-observation" => Request() with { RefObservationRef = "" },
            "observed-base" => Request() with { ObservedBaseRef = "" },
            "observed-head" => Request() with { ObservedHeadRef = "" },
            "observed-branch" => Request() with { ObservedBranchRef = "" },
            "observed-target-fingerprint" => Request() with { ObservedTargetFingerprint = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static MovedBaseGuardRequest MismatchedRequest(string caseName) =>
        caseName switch
        {
            "base" => Request() with { ObservedBaseRef = "base:e13:moved" },
            "head" => Request() with { ObservedHeadRef = "head:e13:moved" },
            "remote-head" => Request() with { ObservedRemoteHeadRef = "remote-head:e13:moved" },
            "branch" => Request() with { ObservedBranchRef = "branch:e13:moved" },
            "merge-base-fingerprint" => Request() with { ObservedMergeBaseFingerprint = "merge-base-fingerprint:e13:moved" },
            "target-fingerprint" => Request() with { ObservedTargetFingerprint = "target-fingerprint:e13:moved" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static MovedBaseGuardRequest MissingReceiptRequest(
        MovedBaseEvidenceKind evidenceKind,
        string caseName) =>
        caseName switch
        {
            "validation" => Request() with { EvidenceKind = evidenceKind, EvidenceTrustLevel = MovedBaseEvidenceTrustLevel.ReceiptBacked, ValidationReceiptRef = "" },
            "patch" => Request() with { EvidenceKind = evidenceKind, EvidenceTrustLevel = MovedBaseEvidenceTrustLevel.ReceiptBacked, PatchPackageRef = "" },
            "commit" => Request() with { EvidenceKind = evidenceKind, EvidenceTrustLevel = MovedBaseEvidenceTrustLevel.ReceiptBacked, CommitPackageRef = "" },
            "push" => Request() with { EvidenceKind = evidenceKind, EvidenceTrustLevel = MovedBaseEvidenceTrustLevel.ReceiptBacked, PushReceiptRef = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static string UnsafeReference(string caseName) =>
        caseName switch
        {
            "raw-git-log" => string.Concat("raw ", "git log"),
            "raw-git-rev-parse" => string.Concat("raw ", "git rev-parse"),
            "raw-git-merge-base" => string.Concat("raw ", "git merge-base"),
            "raw-branch-list" => "raw branch list",
            "raw-commit-graph" => "raw commit graph",
            "raw-provider-branch-response" => "raw provider branch response",
            "raw-pull-request-response" => "raw pull request response",
            "raw-patch" => "raw patch",
            "raw-diff" => "raw diff",
            "credential" => string.Concat("to", "ken", "=", "fake"),
            "authority" => "safe to merge",
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static void AssertRequiresFreshGates(MovedBaseGuardDecision decision)
    {
        Assert.IsTrue(decision.RequiresFreshAuthority);
        Assert.IsTrue(decision.RequiresFreshValidation);
        Assert.IsTrue(decision.RequiresFreshConcurrentGuard);
        Assert.IsTrue(decision.RequiresDirtyWorktreeGuard);
        Assert.IsTrue(decision.RequiresFreshPostStateObservation);
        Assert.IsTrue(decision.RequiresHumanReview);
    }

    private static void AssertNoAuthority(MovedBaseGuardDecision decision)
    {
        AssertRequiresFreshGates(decision);
        CollectionAssert.Contains(decision.Warnings.ToList(), "moved-base guard is read only");
        CollectionAssert.Contains(decision.Warnings.ToList(), "moved-base guard does not inspect git");
        CollectionAssert.Contains(decision.Warnings.ToList(), "moved-base guard does not call github");
        CollectionAssert.Contains(decision.Warnings.ToList(), "moved-base guard does not compare commits");
        CollectionAssert.Contains(decision.Warnings.ToList(), "fresh authority is required before any mutation");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "moved-base guard is not mutation execution");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "moved-base guard is not source apply authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "moved-base guard is not commit authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "moved-base guard is not push authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "moved-base guard is not pull request authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "moved-base guard is not merge authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "moved-base guard is not retry authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "moved-base guard is not recovery authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "moved-base guard is not rollback authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "moved-base guard is not workflow continuation");
    }

    private static string E13CoreSource()
    {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "MovedBaseGuard*.cs");
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
            $"Unexpected forbidden marker found in E13 source: {forbidden}");
}
