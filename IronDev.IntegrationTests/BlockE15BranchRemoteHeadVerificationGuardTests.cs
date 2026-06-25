using System.Reflection;
using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE15BranchRemoteHeadVerificationGuardTests
{
    private static readonly DateTimeOffset ObservedAtUtc = new(2026, 6, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecordedAtUtc = ObservedAtUtc.AddMinutes(1);
    private static readonly DateTimeOffset ExpiresAtUtc = ObservedAtUtc.AddMinutes(30);

    [TestMethod]
    public void ValidFreshVerifiedBranchRemoteHeadMayProceedToNextAuthorityGateOnly()
    {
        var decision = Evaluate(Request());

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.MayProceedToNextAuthorityGate, decision.Decision);
        Assert.AreEqual(BranchRemoteHeadVerificationBlockKind.None, decision.BlockKind);
        AssertRequiresFreshGates(decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("authority")]
    [DataRow("validation")]
    [DataRow("dirty-worktree")]
    [DataRow("moved-base")]
    [DataRow("stale-validation")]
    [DataRow("concurrent")]
    [DataRow("post-state")]
    [DataRow("human")]
    public void PositiveDecisionStillRequiresOtherGates(string gate)
    {
        var decision = Evaluate(Request());

        switch (gate)
        {
            case "authority":
                Assert.IsTrue(decision.RequiresFreshAuthority);
                break;
            case "validation":
                Assert.IsTrue(decision.RequiresFreshValidation);
                break;
            case "dirty-worktree":
                Assert.IsTrue(decision.RequiresDirtyWorktreeGuard);
                break;
            case "moved-base":
                Assert.IsTrue(decision.RequiresMovedBaseGuard);
                break;
            case "stale-validation":
                Assert.IsTrue(decision.RequiresStaleValidationGuard);
                break;
            case "concurrent":
                Assert.IsTrue(decision.RequiresConcurrentGuard);
                break;
            case "post-state":
                Assert.IsTrue(decision.RequiresFreshPostStateObservation);
                break;
            case "human":
                Assert.IsTrue(decision.RequiresHumanReview);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(gate), gate, null);
        }

        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void LocalHeadAndRemoteHeadDoNotNeedToBeEqualUniversally()
    {
        var decision = Evaluate(Request() with
        {
            ExpectedLocalHeadRef = "head:local-ahead",
            ObservedLocalHeadRef = "head:local-ahead",
            ExpectedRemoteHeadRef = "head:remote-before-push",
            ObservedRemoteHeadRef = "head:remote-before-push"
        });

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.MayProceedToNextAuthorityGate, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void NullRequestFailsClosedWithoutThrowing()
    {
        var decision = Evaluate(null);

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual(BranchRemoteHeadVerificationBlockKind.InvalidRequest, decision.BlockKind);
        Assert.AreEqual("BranchRemoteHeadVerificationRequestRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("tenant", "BranchRemoteHeadTenantIdRequired")]
    [DataRow("project", "BranchRemoteHeadProjectIdRequired")]
    [DataRow("operation", "BranchRemoteHeadOperationIdInvalid")]
    [DataRow("correlation", "BranchRemoteHeadCorrelationIdInvalid")]
    [DataRow("surface", "BranchRemoteHeadMutationSurfaceRequired")]
    [DataRow("attempt", "BranchRemoteHeadAttemptRefRequired")]
    [DataRow("target", "BranchRemoteHeadTargetRefRequired")]
    [DataRow("guard", "BranchRemoteHeadGuardRefRequired")]
    [DataRow("observed-at", "BranchRemoteHeadObservedAtUtcRequired")]
    [DataRow("recorded-at", "BranchRemoteHeadRecordedAtUtcRequired")]
    [DataRow("observed-non-utc", "BranchRemoteHeadObservedAtUtcMustBeUtc")]
    [DataRow("recorded-non-utc", "BranchRemoteHeadRecordedAtUtcMustBeUtc")]
    [DataRow("recorded-before-observed", "BranchRemoteHeadRecordedAtUtcBeforeObservedAtUtc")]
    [DataRow("expiry-non-utc", "BranchRemoteHeadEvidenceExpiresAtUtcMustBeUtc")]
    [DataRow("expiry-before-observed", "BranchRemoteHeadEvidenceExpiresAtUtcBeforeObservedAtUtc")]
    [DataRow("guard-version", "BranchRemoteHeadGuardVersionRequired")]
    [DataRow("reason", "BranchRemoteHeadReasonCodeRequired")]
    [DataRow("source", "BranchRemoteHeadSourceRequired")]
    public void InvalidRequestFailsClosed(string caseName, string expectedIssue)
    {
        var decision = Evaluate(InvalidRequest(caseName));

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual(BranchRemoteHeadVerificationBlockKind.InvalidRequest, decision.BlockKind);
        Assert.AreEqual(expectedIssue, decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("subject", "BranchRemoteHeadSubjectKindUnknown", BranchRemoteHeadVerificationDecisionKind.Invalid)]
    [DataRow("evidence", "BranchRemoteHeadEvidenceKindUnknown", BranchRemoteHeadVerificationDecisionKind.BlockedByUntrustedEvidence)]
    [DataRow("trust", "BranchRemoteHeadEvidenceTrustLevelUnknown", BranchRemoteHeadVerificationDecisionKind.BlockedByUntrustedEvidence)]
    [DataRow("freshness", "BranchRemoteHeadObservationFreshnessUnknown", BranchRemoteHeadVerificationDecisionKind.BlockedByStaleObservation)]
    [DataRow("outcome", "BranchRemoteHeadVerificationOutcomeUnknown", BranchRemoteHeadVerificationDecisionKind.BlockedByInconsistentEvidence)]
    public void UnknownDimensionsBlockFailClosed(
        string caseName,
        string expectedReason,
        BranchRemoteHeadVerificationDecisionKind expectedDecision)
    {
        var decision = Evaluate(UnknownDimensionRequest(caseName));

        Assert.AreEqual(expectedDecision, decision.Decision);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void StaleObservationBlocks()
    {
        var decision = Evaluate(Request() with { ObservationFreshness = BranchRemoteHeadObservationFreshness.Stale });

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.BlockedByStaleObservation, decision.Decision);
        Assert.AreEqual(BranchRemoteHeadVerificationBlockKind.StaleObservation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void NotTimestampedObservationBlocks()
    {
        var decision = Evaluate(Request() with { ObservationFreshness = BranchRemoteHeadObservationFreshness.NotTimestamped });

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.BlockedByStaleObservation, decision.Decision);
        Assert.AreEqual("BranchRemoteHeadObservationStale", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void MaxAgeExceededBlocks()
    {
        var decision = Evaluate(Request() with { RecordedAtUtc = ObservedAtUtc.AddSeconds(BranchRemoteHeadVerificationValidator.MaxObservationAgeSeconds + 1) });

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.BlockedByStaleObservation, decision.Decision);
        Assert.AreEqual("BranchRemoteHeadObservationStale", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ExpiredObservationBlocks()
    {
        var decision = Evaluate(Request() with { EvidenceExpiresAtUtc = RecordedAtUtc });

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.BlockedByExpiredObservation, decision.Decision);
        Assert.AreEqual(BranchRemoteHeadVerificationBlockKind.ExpiredObservation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(BranchRemoteHeadVerificationOutcome.BranchMismatch, BranchRemoteHeadVerificationDecisionKind.BlockedByBranchMismatch, BranchRemoteHeadVerificationBlockKind.BranchMismatch, "BranchRemoteHeadBranchMismatch")]
    [DataRow(BranchRemoteHeadVerificationOutcome.RemoteMismatch, BranchRemoteHeadVerificationDecisionKind.BlockedByRemoteMismatch, BranchRemoteHeadVerificationBlockKind.RemoteMismatch, "BranchRemoteHeadRemoteMismatch")]
    [DataRow(BranchRemoteHeadVerificationOutcome.HeadMismatch, BranchRemoteHeadVerificationDecisionKind.BlockedByHeadMismatch, BranchRemoteHeadVerificationBlockKind.HeadMismatch, "BranchRemoteHeadHeadMismatch")]
    [DataRow(BranchRemoteHeadVerificationOutcome.BaseMismatch, BranchRemoteHeadVerificationDecisionKind.BlockedByBaseMismatch, BranchRemoteHeadVerificationBlockKind.BaseMismatch, "BranchRemoteHeadBaseMismatch")]
    [DataRow(BranchRemoteHeadVerificationOutcome.DetachedHead, BranchRemoteHeadVerificationDecisionKind.BlockedByDetachedHead, BranchRemoteHeadVerificationBlockKind.DetachedHead, "BranchRemoteHeadDetachedHead")]
    [DataRow(BranchRemoteHeadVerificationOutcome.AmbiguousBranch, BranchRemoteHeadVerificationDecisionKind.BlockedByAmbiguousBranch, BranchRemoteHeadVerificationBlockKind.AmbiguousBranch, "BranchRemoteHeadAmbiguousBranch")]
    [DataRow(BranchRemoteHeadVerificationOutcome.MissingBranch, BranchRemoteHeadVerificationDecisionKind.BlockedByMissingBranchRemoteHeadEvidence, BranchRemoteHeadVerificationBlockKind.MissingEvidence, "BranchRemoteHeadMissingBranch")]
    [DataRow(BranchRemoteHeadVerificationOutcome.MissingRemote, BranchRemoteHeadVerificationDecisionKind.BlockedByMissingBranchRemoteHeadEvidence, BranchRemoteHeadVerificationBlockKind.MissingEvidence, "BranchRemoteHeadMissingRemote")]
    [DataRow(BranchRemoteHeadVerificationOutcome.MissingHead, BranchRemoteHeadVerificationDecisionKind.BlockedByMissingBranchRemoteHeadEvidence, BranchRemoteHeadVerificationBlockKind.MissingEvidence, "BranchRemoteHeadMissingHead")]
    [DataRow(BranchRemoteHeadVerificationOutcome.RemoteUnavailable, BranchRemoteHeadVerificationDecisionKind.BlockedByRemoteUnavailable, BranchRemoteHeadVerificationBlockKind.RemoteUnavailable, "BranchRemoteHeadRemoteUnavailable")]
    [DataRow(BranchRemoteHeadVerificationOutcome.DeletedRemoteBranch, BranchRemoteHeadVerificationDecisionKind.BlockedByDeletedRemoteBranch, BranchRemoteHeadVerificationBlockKind.DeletedRemoteBranch, "BranchRemoteHeadDeletedRemoteBranch")]
    public void OutcomeBlocksMapToExplicitDecision(
        BranchRemoteHeadVerificationOutcome outcome,
        BranchRemoteHeadVerificationDecisionKind expectedDecision,
        BranchRemoteHeadVerificationBlockKind expectedBlock,
        string expectedReason)
    {
        var decision = Evaluate(Request() with { VerificationOutcome = outcome });

        Assert.AreEqual(expectedDecision, decision.Decision);
        Assert.AreEqual(expectedBlock, decision.BlockKind);
        Assert.AreEqual(expectedReason, decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("branch", "BranchRemoteHeadBranchObservationRefRequired")]
    [DataRow("remote", "BranchRemoteHeadRemoteObservationRefRequired")]
    [DataRow("head", "BranchRemoteHeadHeadObservationRefRequired")]
    [DataRow("composite", "BranchRemoteHeadCompositeObservationRefRequired")]
    [DataRow("provider", "BranchRemoteHeadProviderBranchStateRefRequired")]
    [DataRow("operator", "BranchRemoteHeadOperatorObservationRefRequired")]
    [DataRow("dirty", "BranchRemoteHeadDirtyWorktreeGuardRefRequired")]
    [DataRow("moved", "BranchRemoteHeadMovedBaseGuardRefRequired")]
    [DataRow("stale-validation", "BranchRemoteHeadStaleValidationGuardRefRequired")]
    [DataRow("concurrent", "BranchRemoteHeadConcurrentGuardDecisionRefRequired")]
    [DataRow("post-state", "BranchRemoteHeadPostStateObservationRefRequired")]
    public void VerifiedOutcomeRequiresEvidenceRefs(string caseName, string expectedIssue)
    {
        var decision = Evaluate(MissingEvidenceRequest(caseName));

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.BlockedByMissingBranchRemoteHeadEvidence, decision.Decision);
        Assert.AreEqual(expectedIssue, decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SelfReportedWithoutCorroborationBlocksMissingEvidence()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceTrustLevel = BranchRemoteHeadEvidenceTrustLevel.SelfReported,
            BranchObservationRef = "",
            RemoteObservationRef = "",
            HeadObservationRef = "",
            CompositeObservationRef = "",
            ProviderBranchStateRef = "",
            OperatorObservationRef = "",
            PostStateObservationRef = "",
            DirtyWorktreeGuardRef = "",
            MovedBaseGuardRef = ""
        });

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.BlockedByMissingBranchRemoteHeadEvidence, decision.Decision);
        Assert.AreEqual("BranchRemoteHeadSelfReportedCorroborationRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SelfReportedWithCorroborationStillCannotProceed()
    {
        var decision = Evaluate(Request() with { EvidenceTrustLevel = BranchRemoteHeadEvidenceTrustLevel.SelfReported });

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.BlockedByUntrustedEvidence, decision.Decision);
        Assert.AreEqual("BranchRemoteHeadSelfReportedCannotProceedToAuthorityGate", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void TestFixtureRequiresTestLabelledSource()
    {
        var decision = Evaluate(Request() with { EvidenceTrustLevel = BranchRemoteHeadEvidenceTrustLevel.TestFixture });

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.BlockedByUntrustedEvidence, decision.Decision);
        Assert.AreEqual("BranchRemoteHeadTestFixtureSourceRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void TestFixtureEvidenceCannotProceedToAuthorityGate()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceTrustLevel = BranchRemoteHeadEvidenceTrustLevel.TestFixture,
            Source = "branch-remote-head-test"
        });

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.BlockedByUntrustedEvidence, decision.Decision);
        Assert.AreEqual("BranchRemoteHeadTestFixtureCannotProceedToAuthorityGate", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void TestFixtureKindCannotProceedToAuthorityGate()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceKind = BranchRemoteHeadEvidenceKind.TestFixtureBranchObservation,
            Source = "branch-remote-head-test"
        });

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.BlockedByUntrustedEvidence, decision.Decision);
        Assert.AreEqual("BranchRemoteHeadTestFixtureCannotProceedToAuthorityGate", decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("branch", "BranchRemoteHeadExpectedBranchMismatch")]
    [DataRow("remote", "BranchRemoteHeadExpectedRemoteMismatch")]
    [DataRow("remote-url", "BranchRemoteHeadExpectedRemoteUrlFingerprintMismatch")]
    [DataRow("local-head", "BranchRemoteHeadExpectedLocalHeadMismatch")]
    [DataRow("remote-head", "BranchRemoteHeadExpectedRemoteHeadMismatch")]
    [DataRow("base", "BranchRemoteHeadExpectedBaseMismatch")]
    [DataRow("source-state", "BranchRemoteHeadExpectedSourceStateMismatch")]
    [DataRow("patch-package", "BranchRemoteHeadExpectedPatchPackageMismatch")]
    [DataRow("commit", "BranchRemoteHeadExpectedCommitMismatch")]
    public void ExpectedObservedMismatchBlocksInconsistent(string caseName, string expectedIssue)
    {
        var decision = Evaluate(MismatchedRequest(caseName));

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.BlockedByInconsistentEvidence, decision.Decision);
        Assert.AreEqual(BranchRemoteHeadVerificationBlockKind.InconsistentEvidence, decision.BlockKind);
        Assert.AreEqual(expectedIssue, decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("approval", "branch remote head verification guard is not approval")]
    [DataRow("policy", "branch remote head verification guard is not policy satisfaction")]
    [DataRow("source-safety", "branch remote head verification guard is not source safety")]
    [DataRow("validation", "branch remote head verification guard is not validation freshness")]
    [DataRow("apply", "branch remote head verification guard is not source apply authority")]
    [DataRow("commit", "branch remote head verification guard is not commit authority")]
    [DataRow("push", "branch remote head verification guard is not push authority")]
    [DataRow("merge", "branch remote head verification guard is not merge authority")]
    [DataRow("release", "branch remote head verification guard is not release authority")]
    [DataRow("deploy", "branch remote head verification guard is not deployment authority")]
    public void VerifiedBranchHeadEvidenceStillDoesNotGrantAuthority(string caseName, string forbidden)
    {
        var decision = Evaluate(Request());

        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), forbidden, caseName);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("raw-git-output")]
    [DataRow("raw-github-response")]
    [DataRow("raw-provider-response")]
    [DataRow("raw-command-line")]
    [DataRow("raw-remote-url")]
    [DataRow("remote-url-with-token")]
    [DataRow("credential")]
    [DataRow("raw-patch")]
    [DataRow("raw-diff")]
    [DataRow("authority")]
    public void UnsafeMarkersAreRejected(string caseName)
    {
        var decision = Evaluate(Request() with { BranchObservationRef = UnsafeReference(caseName) });

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.AreEqual(BranchRemoteHeadVerificationBlockKind.UnsafePayload, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("branch")]
    [DataRow("remote")]
    [DataRow("head")]
    [DataRow("remote-url")]
    [DataRow("provider")]
    public void UnsafePayloadDecisionDoesNotEchoRejectedEvidence(string caseName)
    {
        var unsafeValue = UnsafeReference("raw-provider-response");
        var decision = Evaluate(UnsafeFieldRequest(caseName, unsafeValue));

        AssertNoUnsafeDecisionEcho(decision, unsafeValue);
    }

    [TestMethod]
    public void UnsafePayloadFingerprintRedactsUnsafeMaterial()
    {
        var unsafeValue = UnsafeReference("raw-git-output");
        var decision = Evaluate(Request() with { HeadObservationRef = unsafeValue });

        Assert.IsFalse(decision.RecordFingerprint.Contains(unsafeValue, StringComparison.Ordinal));
        Assert.IsTrue(decision.RecordFingerprint.Contains("[unsafe]", StringComparison.Ordinal));
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("branch:feature/e15")]
    [DataRow("remote:origin")]
    [DataRow("head:e15")]
    [DataRow("base:e15")]
    [DataRow("patch-package:e15")]
    [DataRow("merge-target:e15")]
    [DataRow("release-candidate:e15")]
    [DataRow("deploy-target:e15")]
    public void ValidDomainRefsAreNotRejected(string targetRef)
    {
        var decision = Evaluate(Request() with { TargetRef = targetRef });

        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.MayProceedToNextAuthorityGate, decision.Decision);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void StaticScan_E15AddsNoGitGithubOrIoAccess()
    {
        var source = StripStrings(E15CoreSource());

        AssertDoesNotContain(source, "HttpClient");
        AssertDoesNotContain(source, "Octokit");
        AssertDoesNotContain(source, ".git");
        AssertDoesNotContain(source, "Directory.Get");
        AssertDoesNotContain(source, "File.Read");
    }

    [TestMethod]
    public void StaticScan_E15AddsNoProcessExecution()
    {
        var source = StripStrings(E15CoreSource());

        AssertDoesNotContain(source, "ProcessStartInfo");
        AssertDoesNotContain(source, "Process.Start");
        AssertDoesNotContain(source, "RunProcessAsync");
        AssertDoesNotContain(source, "cmd.exe");
        AssertDoesNotContain(source, "powershell");
        AssertDoesNotContain(source, "bash");
    }

    [TestMethod]
    public void StaticScan_E15AddsNoFileSystemMutation()
    {
        var source = StripStrings(E15CoreSource());

        AssertDoesNotContain(source, "File.Write");
        AssertDoesNotContain(source, "File.Append");
        AssertDoesNotContain(source, "Directory.Create");
        AssertDoesNotContain(source, "File.Delete");
        AssertDoesNotContain(source, "Directory.Delete");
    }

    [TestMethod]
    public void StaticScan_E15AddsNoValidationTestBuildRunnerAccess()
    {
        var source = StripStrings(E15CoreSource());

        AssertDoesNotContain(source, "dotnet test");
        AssertDoesNotContain(source, "dotnet build");
        AssertDoesNotContain(source, "TestRunner");
        AssertDoesNotContain(source, "BuildRunner");
        AssertDoesNotContain(source, "ValidationRunner");
    }

    [TestMethod]
    public void StaticScan_E15AddsNoDependencyInjectionOrExecutorWiring()
    {
        var source = StripStrings(E15CoreSource());

        AssertDoesNotContain(source, "IServiceCollection");
        AssertDoesNotContain(source, "AddScoped");
        AssertDoesNotContain(source, "AddSingleton");
        AssertDoesNotContain(source, "Executor");
    }

    [TestMethod]
    public void StaticScan_E15AddsNoLockAcquisitionReleaseOrRenewal()
    {
        var source = StripStrings(E15CoreSource());

        AssertDoesNotContain(source, "Acquire");
        AssertDoesNotContain(source, "ReleaseAsync");
        AssertDoesNotContain(source, "ReleaseLease");
        AssertDoesNotContain(source, "Renew");
        AssertDoesNotContain(source, "Enforce");
        AssertDoesNotContain(source, "MutationLeaseStore");
    }

    [DataTestMethod]
    [DataRow("Approved")]
    [DataRow("Authorized")]
    [DataRow("Allowed")]
    [DataRow("CanApply")]
    [DataRow("CanCommit")]
    [DataRow("CanPush")]
    [DataRow("CanMerge")]
    [DataRow("CanMutate")]
    [DataRow("CanDeploy")]
    [DataRow("PolicySatisfied")]
    [DataRow("ValidationFresh")]
    [DataRow("SourceSafe")]
    public void PublicModelNamesAvoidAuthorityShapedTerms(string forbidden)
    {
        var names = E15PublicModelNames()
            .Where(name => !string.Equals(name, nameof(BranchRemoteHeadVerificationDecisionKind.MayProceedToNextAuthorityGate), StringComparison.Ordinal))
            .ToArray();

        Assert.IsFalse(
            names.Any(name => name.Contains(forbidden, StringComparison.Ordinal)),
            $"Unexpected authority-shaped model name: {forbidden}");
    }

    [TestMethod]
    public void FingerprintIsStableForIdenticalRequest()
    {
        var left = Evaluate(Request()).RecordFingerprint;
        var right = Evaluate(Request()).RecordFingerprint;

        Assert.AreEqual(left, right);
    }

    [DataTestMethod]
    [DataRow("branch")]
    [DataRow("remote")]
    [DataRow("local-head")]
    [DataRow("remote-head")]
    [DataRow("base")]
    public void FingerprintChangesWhenObservedStateChanges(string caseName)
    {
        var left = Evaluate(Request()).RecordFingerprint;
        var right = Evaluate(FingerprintChangedRequest(caseName)).RecordFingerprint;

        Assert.AreNotEqual(left, right);
    }

    [TestMethod]
    public void FingerprintChangesWhenDecisionChanges()
    {
        var left = Evaluate(Request()).RecordFingerprint;
        var right = Evaluate(Request() with { ObservationFreshness = BranchRemoteHeadObservationFreshness.Stale }).RecordFingerprint;

        Assert.AreNotEqual(left, right);
    }

    [DataTestMethod]
    [DataRow("The branch you meant is not automatically the branch you are on.")]
    [DataRow("A matching branch/head observation is evidence. It is not source safety, validation freshness, approval, or mutation authority.")]
    [DataRow("does not call Git/GitHub/CI/shell/file system")]
    [DataRow("approval")]
    [DataRow("policy satisfaction")]
    [DataRow("source safety")]
    [DataRow("validation freshness")]
    [DataRow("source apply authority")]
    [DataRow("commit authority")]
    [DataRow("push authority")]
    [DataRow("merge authority")]
    [DataRow("workflow continuation")]
    public void ReceiptContainsBoundaryLines(string expected)
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "E15_BRANCH_REMOTE_HEAD_VERIFICATION_GUARD.md"));

        StringAssert.Contains(receipt, expected);
    }

    private static BranchRemoteHeadVerificationDecision Evaluate(BranchRemoteHeadVerificationRequest? request) =>
        new BranchRemoteHeadVerificationService().Evaluate(request);

    private static BranchRemoteHeadVerificationRequest Request() =>
        new()
        {
            TenantId = "tenant-e15",
            ProjectId = "project-e15",
            OperationId = "op_000000000000e015",
            CorrelationId = "corr_0000000000e01500",
            MutationSurface = MutationLeaseSurfaceKind.Push,
            AttemptRef = "attempt:e15",
            TargetRef = "target:e15",
            GuardRef = "branch-remote-head-guard:e15",
            SubjectKind = BranchRemoteHeadSubjectKind.PushTarget,
            EvidenceKind = BranchRemoteHeadEvidenceKind.BranchRemoteCompositeObservation,
            EvidenceTrustLevel = BranchRemoteHeadEvidenceTrustLevel.LocalObservationBacked,
            ObservationFreshness = BranchRemoteHeadObservationFreshness.Fresh,
            VerificationOutcome = BranchRemoteHeadVerificationOutcome.Verified,
            BranchObservationRef = "branch-observation:e15",
            RemoteObservationRef = "remote-observation:e15",
            HeadObservationRef = "head-observation:e15",
            CompositeObservationRef = "branch-remote-composite:e15",
            ProviderBranchStateRef = "provider-branch-state:e15",
            OperatorObservationRef = "operator-branch-observation:e15",
            ExpectedBranchRef = "branch:feature/e15",
            ObservedBranchRef = "branch:feature/e15",
            ExpectedRemoteRef = "remote:origin",
            ObservedRemoteRef = "remote:origin",
            ExpectedRemoteUrlFingerprint = "remote-fingerprint:e15",
            ObservedRemoteUrlFingerprint = "remote-fingerprint:e15",
            ExpectedLocalHeadRef = "head:e15-local",
            ObservedLocalHeadRef = "head:e15-local",
            ExpectedRemoteHeadRef = "head:e15-remote",
            ObservedRemoteHeadRef = "head:e15-remote",
            ExpectedBaseRef = "base:e15",
            ObservedBaseRef = "base:e15",
            ExpectedSourceStateRef = "source-state:e15",
            ObservedSourceStateRef = "source-state:e15",
            ExpectedPatchPackageRef = "patch-package:e15",
            ObservedPatchPackageRef = "patch-package:e15",
            ExpectedCommitRef = "commit:e15",
            ObservedCommitRef = "commit:e15",
            DirtyWorktreeGuardRef = "dirty-worktree-guard:e15",
            MovedBaseGuardRef = "moved-base-guard:e15",
            StaleValidationGuardRef = "stale-validation-guard:e15",
            ConcurrentGuardDecisionRef = "concurrent-guard-decision:e15",
            PostStateObservationRef = "post-state-observation:e15",
            ObservedAtUtc = ObservedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            EvidenceExpiresAtUtc = ExpiresAtUtc,
            GuardVersion = "branch-remote-head-guard-v1",
            ReasonCode = "branch-head-verified",
            Source = "branch-remote-head-guard"
        };

    private static BranchRemoteHeadVerificationRequest InvalidRequest(string caseName) =>
        caseName switch
        {
            "tenant" => Request() with { TenantId = "" },
            "project" => Request() with { ProjectId = "" },
            "operation" => Request() with { OperationId = "bad-operation" },
            "correlation" => Request() with { CorrelationId = "bad-correlation" },
            "surface" => Request() with { MutationSurface = MutationLeaseSurfaceKind.Unknown },
            "attempt" => Request() with { AttemptRef = "" },
            "target" => Request() with { TargetRef = "" },
            "guard" => Request() with { GuardRef = "" },
            "observed-at" => Request() with { ObservedAtUtc = default },
            "recorded-at" => Request() with { RecordedAtUtc = default },
            "observed-non-utc" => Request() with { ObservedAtUtc = new DateTimeOffset(2026, 6, 26, 0, 0, 0, TimeSpan.FromHours(12)) },
            "recorded-non-utc" => Request() with { RecordedAtUtc = new DateTimeOffset(2026, 6, 26, 0, 1, 0, TimeSpan.FromHours(12)) },
            "recorded-before-observed" => Request() with { RecordedAtUtc = ObservedAtUtc.AddSeconds(-1) },
            "expiry-non-utc" => Request() with { EvidenceExpiresAtUtc = new DateTimeOffset(2026, 6, 26, 0, 30, 0, TimeSpan.FromHours(12)) },
            "expiry-before-observed" => Request() with { EvidenceExpiresAtUtc = ObservedAtUtc },
            "guard-version" => Request() with { GuardVersion = "" },
            "reason" => Request() with { ReasonCode = "" },
            "source" => Request() with { Source = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static BranchRemoteHeadVerificationRequest UnknownDimensionRequest(string caseName) =>
        caseName switch
        {
            "subject" => Request() with { SubjectKind = BranchRemoteHeadSubjectKind.Unknown },
            "evidence" => Request() with { EvidenceKind = BranchRemoteHeadEvidenceKind.Unknown },
            "trust" => Request() with { EvidenceTrustLevel = BranchRemoteHeadEvidenceTrustLevel.Unknown },
            "freshness" => Request() with { ObservationFreshness = BranchRemoteHeadObservationFreshness.Unknown },
            "outcome" => Request() with { VerificationOutcome = BranchRemoteHeadVerificationOutcome.Unknown },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static BranchRemoteHeadVerificationRequest MissingEvidenceRequest(string caseName) =>
        caseName switch
        {
            "branch" => Request() with { BranchObservationRef = "" },
            "remote" => Request() with { RemoteObservationRef = "" },
            "head" => Request() with { HeadObservationRef = "" },
            "composite" => Request() with { CompositeObservationRef = "" },
            "provider" => Request() with
            {
                EvidenceKind = BranchRemoteHeadEvidenceKind.ProviderBranchState,
                EvidenceTrustLevel = BranchRemoteHeadEvidenceTrustLevel.ProviderMetadataBacked,
                ProviderBranchStateRef = ""
            },
            "operator" => Request() with
            {
                EvidenceKind = BranchRemoteHeadEvidenceKind.OperatorBranchObservation,
                EvidenceTrustLevel = BranchRemoteHeadEvidenceTrustLevel.OperatorObserved,
                OperatorObservationRef = ""
            },
            "dirty" => Request() with { DirtyWorktreeGuardRef = "" },
            "moved" => Request() with { MovedBaseGuardRef = "" },
            "stale-validation" => Request() with { StaleValidationGuardRef = "" },
            "concurrent" => Request() with { ConcurrentGuardDecisionRef = "" },
            "post-state" => Request() with { PostStateObservationRef = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static BranchRemoteHeadVerificationRequest MismatchedRequest(string caseName) =>
        caseName switch
        {
            "branch" => Request() with { ObservedBranchRef = "branch:other" },
            "remote" => Request() with { ObservedRemoteRef = "remote:upstream" },
            "remote-url" => Request() with { ObservedRemoteUrlFingerprint = "remote-fingerprint:other" },
            "local-head" => Request() with { ObservedLocalHeadRef = "head:other-local" },
            "remote-head" => Request() with { ObservedRemoteHeadRef = "head:other-remote" },
            "base" => Request() with { ObservedBaseRef = "base:other" },
            "source-state" => Request() with { ObservedSourceStateRef = "source-state:other" },
            "patch-package" => Request() with { ObservedPatchPackageRef = "patch-package:other" },
            "commit" => Request() with { ObservedCommitRef = "commit:other" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static BranchRemoteHeadVerificationRequest UnsafeFieldRequest(string caseName, string unsafeValue) =>
        caseName switch
        {
            "branch" => Request() with { BranchObservationRef = unsafeValue },
            "remote" => Request() with { RemoteObservationRef = unsafeValue },
            "head" => Request() with { HeadObservationRef = unsafeValue },
            "remote-url" => Request() with { ExpectedRemoteUrlFingerprint = unsafeValue },
            "provider" => Request() with { ProviderBranchStateRef = unsafeValue },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static BranchRemoteHeadVerificationRequest FingerprintChangedRequest(string caseName) =>
        caseName switch
        {
            "branch" => Request() with { ExpectedBranchRef = "branch:other", ObservedBranchRef = "branch:other" },
            "remote" => Request() with { ExpectedRemoteRef = "remote:other", ObservedRemoteRef = "remote:other" },
            "local-head" => Request() with { ExpectedLocalHeadRef = "head:other-local", ObservedLocalHeadRef = "head:other-local" },
            "remote-head" => Request() with { ExpectedRemoteHeadRef = "head:other-remote", ObservedRemoteHeadRef = "head:other-remote" },
            "base" => Request() with { ExpectedBaseRef = "base:other", ObservedBaseRef = "base:other" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static string UnsafeReference(string caseName) =>
        caseName switch
        {
            "raw-git-output" => string.Concat("raw ", "git output"),
            "raw-github-response" => string.Concat("raw ", "github response"),
            "raw-provider-response" => "raw provider response",
            "raw-command-line" => "raw command line",
            "raw-remote-url" => "raw remote url",
            "remote-url-with-token" => string.Concat("https://", "to", "ken", "@github.com/repo"),
            "credential" => string.Concat("pass", "word", "=", "fake"),
            "raw-patch" => "raw patch",
            "raw-diff" => "raw diff",
            "authority" => "safe to push",
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static void AssertRequiresFreshGates(BranchRemoteHeadVerificationDecision decision)
    {
        Assert.IsTrue(decision.RequiresFreshAuthority);
        Assert.IsTrue(decision.RequiresFreshValidation);
        Assert.IsTrue(decision.RequiresDirtyWorktreeGuard);
        Assert.IsTrue(decision.RequiresMovedBaseGuard);
        Assert.IsTrue(decision.RequiresStaleValidationGuard);
        Assert.IsTrue(decision.RequiresConcurrentGuard);
        Assert.IsTrue(decision.RequiresFreshPostStateObservation);
        Assert.IsTrue(decision.RequiresHumanReview);
    }

    private static void AssertNoAuthority(BranchRemoteHeadVerificationDecision decision)
    {
        AssertRequiresFreshGates(decision);
        CollectionAssert.Contains(decision.Warnings.ToList(), "branch remote head verification guard is read only");
        CollectionAssert.Contains(decision.Warnings.ToList(), "branch remote head verification guard does not call git");
        CollectionAssert.Contains(decision.Warnings.ToList(), "branch remote head verification guard does not call github");
        CollectionAssert.Contains(decision.Warnings.ToList(), "branch remote head verification guard does not fetch remotes");
        CollectionAssert.Contains(decision.Warnings.ToList(), "branch remote head verification guard does not inspect worktrees");
        CollectionAssert.Contains(decision.Warnings.ToList(), "the branch you meant is not automatically the branch you are on");
        CollectionAssert.Contains(decision.Warnings.ToList(), "matching branch head evidence is not mutation authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "branch remote head verification guard is not mutation execution");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "branch remote head verification guard is not source apply authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "branch remote head verification guard is not commit authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "branch remote head verification guard is not push authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "branch remote head verification guard is not pull request authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "branch remote head verification guard is not merge authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "branch remote head verification guard is not release authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "branch remote head verification guard is not deployment authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "branch remote head verification guard is not approval");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "branch remote head verification guard is not policy satisfaction");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "branch remote head verification guard is not validation freshness");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "branch remote head verification guard is not source safety");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "branch remote head verification guard is not workflow continuation");
    }

    private static void AssertNoUnsafeDecisionEcho(BranchRemoteHeadVerificationDecision decision, string unsafeValue)
    {
        Assert.AreEqual(BranchRemoteHeadVerificationDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.AreEqual(BranchRemoteHeadVerificationBlockKind.UnsafePayload, decision.BlockKind);
        AssertNoAuthority(decision);

        var stringValues = typeof(BranchRemoteHeadVerificationDecision)
            .GetProperties()
            .Where(static property => property.PropertyType == typeof(string))
            .Select(property => (Name: property.Name, Value: (string?)property.GetValue(decision) ?? string.Empty));

        foreach (var (name, value) in stringValues)
        {
            Assert.IsFalse(
                value.Contains(unsafeValue, StringComparison.Ordinal),
                $"{name} echoed unsafe value.");
        }
    }

    private static IEnumerable<string> E15PublicModelNames()
    {
        var types = new[]
        {
            typeof(BranchRemoteHeadSubjectKind),
            typeof(BranchRemoteHeadEvidenceKind),
            typeof(BranchRemoteHeadEvidenceTrustLevel),
            typeof(BranchRemoteHeadObservationFreshness),
            typeof(BranchRemoteHeadVerificationOutcome),
            typeof(BranchRemoteHeadVerificationDecisionKind),
            typeof(BranchRemoteHeadVerificationBlockKind),
            typeof(BranchRemoteHeadVerificationRequest),
            typeof(BranchRemoteHeadVerificationDecision)
        };

        foreach (var type in types)
        {
            yield return type.Name;
            foreach (var name in type.IsEnum ? Enum.GetNames(type) : [])
            {
                yield return name;
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                yield return property.Name;
            }
        }
    }

    private static string E15CoreSource()
    {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "BranchRemoteHeadVerification*.cs");
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
            $"Unexpected forbidden marker found in E15 source: {forbidden}");
}
