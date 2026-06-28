using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockE14StaleValidationGuardTests
{
    private static readonly DateTimeOffset ValidatedAtUtc = new(2026, 6, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecordedAtUtc = ValidatedAtUtc.AddMinutes(1);
    private static readonly DateTimeOffset ExpiresAtUtc = ValidatedAtUtc.AddMinutes(30);

    [TestMethod]
    public void ValidFreshPassedValidationMayProceedToNextAuthorityGateOnly()
    {
        var decision = Evaluate(Request());

        Assert.AreEqual(StaleValidationGuardDecisionKind.MayProceedToNextAuthorityGate, decision.Decision);
        Assert.AreEqual(StaleValidationGuardBlockKind.None, decision.BlockKind);
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
    public void PositiveDecisionStillRequiresMovedBaseGuard()
    {
        var decision = Evaluate(Request());

        Assert.IsTrue(decision.RequiresMovedBaseGuard);
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
    [DataRow("tenant", "StaleValidationGuardTenantIdRequired")]
    [DataRow("project", "StaleValidationGuardProjectIdRequired")]
    [DataRow("operation", "StaleValidationGuardOperationIdInvalid")]
    [DataRow("correlation", "StaleValidationGuardCorrelationIdInvalid")]
    [DataRow("surface", "StaleValidationGuardMutationSurfaceRequired")]
    [DataRow("attempt", "StaleValidationGuardAttemptRefRequired")]
    [DataRow("target", "StaleValidationGuardTargetRefRequired")]
    [DataRow("guard", "StaleValidationGuardGuardRefRequired")]
    [DataRow("validated-at", "StaleValidationGuardValidatedAtUtcRequired")]
    [DataRow("recorded-at", "StaleValidationGuardRecordedAtUtcRequired")]
    [DataRow("validated-non-utc", "StaleValidationGuardValidatedAtUtcMustBeUtc")]
    [DataRow("recorded-non-utc", "StaleValidationGuardRecordedAtUtcMustBeUtc")]
    [DataRow("recorded-before-validated", "StaleValidationGuardRecordedAtUtcBeforeValidatedAtUtc")]
    [DataRow("expiry-non-utc", "StaleValidationGuardEvidenceExpiresAtUtcMustBeUtc")]
    [DataRow("expiry-before-validated", "StaleValidationGuardEvidenceExpiresAtUtcBeforeValidatedAtUtc")]
    [DataRow("guard-version", "StaleValidationGuardGuardVersionRequired")]
    [DataRow("reason", "StaleValidationGuardReasonCodeRequired")]
    [DataRow("source", "StaleValidationGuardSourceRequired")]
    public void InvalidRequestFailsClosed(string caseName, string expectedIssue)
    {
        var decision = Evaluate(InvalidRequest(caseName));

        Assert.AreEqual(StaleValidationGuardDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual(StaleValidationGuardBlockKind.InvalidRequest, decision.BlockKind);
        Assert.AreEqual(expectedIssue, decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownSubjectBlocksFailClosed()
    {
        var decision = Evaluate(Request() with { SubjectKind = StaleValidationSubjectKind.Unknown });

        Assert.AreEqual(StaleValidationGuardDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual("StaleValidationGuardSubjectKindUnknown", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownValidationEvidenceKindBlocksUntrusted()
    {
        var decision = Evaluate(Request() with { ValidationEvidenceKind = ValidationEvidenceKind.Unknown });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByUntrustedValidationEvidence, decision.Decision);
        Assert.AreEqual("StaleValidationGuardValidationEvidenceKindUnknown", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownTrustLevelBlocksUntrusted()
    {
        var decision = Evaluate(Request() with { EvidenceTrustLevel = ValidationEvidenceTrustLevel.Unknown });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByUntrustedValidationEvidence, decision.Decision);
        Assert.AreEqual("StaleValidationGuardEvidenceTrustLevelUnknown", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownFreshnessBlocksStale()
    {
        var decision = Evaluate(Request() with { ObservationFreshness = ValidationObservationFreshness.Unknown });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByStaleValidation, decision.Decision);
        Assert.AreEqual("StaleValidationGuardObservationFreshnessUnknown", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void NotTimestampedFreshnessBlocksStale()
    {
        var decision = Evaluate(Request() with { ObservationFreshness = ValidationObservationFreshness.NotTimestamped });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByStaleValidation, decision.Decision);
        Assert.AreEqual("StaleValidationGuardValidationStale", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownValidationOutcomeBlocksUnknownState()
    {
        var decision = Evaluate(Request() with { ValidationOutcome = ValidationOutcomeState.Unknown });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByUnknownValidationState, decision.Decision);
        Assert.AreEqual("StaleValidationGuardValidationOutcomeUnknown", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnknownValidationScopeBlocksFailClosed()
    {
        var decision = Evaluate(Request() with { ValidationScope = ValidationScopeKind.Unknown });

        Assert.AreEqual(StaleValidationGuardDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual("StaleValidationGuardValidationScopeUnknown", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void StaleObservationBlocks()
    {
        var decision = Evaluate(Request() with { ObservationFreshness = ValidationObservationFreshness.Stale });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByStaleValidation, decision.Decision);
        Assert.AreEqual(StaleValidationGuardBlockKind.StaleValidation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void MaxAgeExceededBlocksStale()
    {
        var decision = Evaluate(Request() with { RecordedAtUtc = ValidatedAtUtc.AddSeconds(StaleValidationGuardValidator.MaxValidationAgeSeconds + 1) });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByStaleValidation, decision.Decision);
        Assert.AreEqual("StaleValidationGuardValidationStale", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void ExpiredObservationBlocks()
    {
        var decision = Evaluate(Request() with { EvidenceExpiresAtUtc = RecordedAtUtc });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByExpiredValidation, decision.Decision);
        Assert.AreEqual(StaleValidationGuardBlockKind.ExpiredValidation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("validation-evidence", "StaleValidationGuardValidationEvidenceRefRequired")]
    [DataRow("observed-target", "StaleValidationGuardObservedValidationTargetRefRequired")]
    [DataRow("observed-fingerprint", "StaleValidationGuardObservedValidationFingerprintRequired")]
    [DataRow("concurrent-guard", "StaleValidationGuardConcurrentGuardDecisionRefRequired")]
    [DataRow("dirty-worktree-guard", "StaleValidationGuardDirtyWorktreeGuardRefRequired")]
    [DataRow("moved-base-guard", "StaleValidationGuardMovedBaseGuardRefRequired")]
    [DataRow("post-state", "StaleValidationGuardPostStateObservationRefRequired")]
    public void PassedValidationRequiresEvidenceRefs(string caseName, string expectedIssue)
    {
        var decision = Evaluate(MissingEvidenceRequest(caseName));

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByMissingValidationEvidence, decision.Decision);
        Assert.AreEqual(expectedIssue, decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(ValidationOutcomeState.Failed)]
    [DataRow(ValidationOutcomeState.TimedOut)]
    [DataRow(ValidationOutcomeState.Cancelled)]
    public void FailedValidationOutcomesBlock(ValidationOutcomeState outcome)
    {
        var decision = Evaluate(Request() with { ValidationOutcome = outcome });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByFailedValidation, decision.Decision);
        Assert.AreEqual(StaleValidationGuardBlockKind.FailedValidation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow(ValidationOutcomeState.NotRun)]
    [DataRow(ValidationOutcomeState.Partial)]
    [DataRow(ValidationOutcomeState.Unavailable)]
    public void IncompleteValidationOutcomesBlock(ValidationOutcomeState outcome)
    {
        var decision = Evaluate(Request() with { ValidationOutcome = outcome });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByIncompleteValidation, decision.Decision);
        Assert.AreEqual(StaleValidationGuardBlockKind.IncompleteValidation, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("target", "StaleValidationGuardExpectedValidationTargetMismatch")]
    [DataRow("fingerprint", "StaleValidationGuardExpectedValidationFingerprintMismatch")]
    [DataRow("source-state", "StaleValidationGuardExpectedSourceStateMismatch")]
    [DataRow("patch-package", "StaleValidationGuardExpectedPatchPackageMismatch")]
    [DataRow("commit", "StaleValidationGuardExpectedCommitMismatch")]
    [DataRow("head", "StaleValidationGuardExpectedHeadMismatch")]
    [DataRow("base", "StaleValidationGuardExpectedBaseMismatch")]
    public void ExpectedObservedMismatchBlocksInconsistent(string caseName, string expectedIssue)
    {
        var decision = Evaluate(MismatchedRequest(caseName));

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByInconsistentValidationEvidence, decision.Decision);
        Assert.AreEqual(StaleValidationGuardBlockKind.InconsistentEvidence, decision.BlockKind);
        Assert.AreEqual(expectedIssue, decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SelfReportedWithoutCorroborationBlocksMissingEvidence()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceTrustLevel = ValidationEvidenceTrustLevel.SelfReported,
            ValidationReceiptRef = "",
            BuildReceiptRef = "",
            TestReceiptRef = "",
            GovernanceReceiptRef = "",
            ProviderCiStateRef = "",
            OperatorObservationRef = "",
            PostStateObservationRef = "",
            DirtyWorktreeGuardRef = "",
            MovedBaseGuardRef = ""
        });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByMissingValidationEvidence, decision.Decision);
        Assert.AreEqual("StaleValidationGuardSelfReportedCorroborationRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SelfReportedWithCorroborationStillCannotProceed()
    {
        var decision = Evaluate(Request() with { EvidenceTrustLevel = ValidationEvidenceTrustLevel.SelfReported });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByUntrustedValidationEvidence, decision.Decision);
        Assert.AreEqual("StaleValidationGuardSelfReportedCannotProceedToAuthorityGate", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void TestFixtureSourceAcceptedForTestsButCannotProceed()
    {
        var decision = Evaluate(Request() with
        {
            EvidenceTrustLevel = ValidationEvidenceTrustLevel.TestFixture,
            Source = "stale-validation-test"
        });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByUntrustedValidationEvidence, decision.Decision);
        Assert.AreEqual("StaleValidationGuardTestFixtureCannotProceedToAuthorityGate", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void SyntheticTestValidationCannotProceed()
    {
        var decision = Evaluate(Request() with
        {
            ValidationEvidenceKind = ValidationEvidenceKind.SyntheticTestValidation,
            Source = "stale-validation-test"
        });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByUntrustedValidationEvidence, decision.Decision);
        Assert.AreEqual("StaleValidationGuardTestFixtureCannotProceedToAuthorityGate", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void TestFixtureWithProductionSourceBlocks()
    {
        var decision = Evaluate(Request() with { EvidenceTrustLevel = ValidationEvidenceTrustLevel.TestFixture });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByUntrustedValidationEvidence, decision.Decision);
        Assert.AreEqual("StaleValidationGuardTestFixtureSourceRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("receipt-backed", "StaleValidationGuardReceiptRefRequired")]
    [DataRow("composite-receipt", "StaleValidationGuardReceiptRefRequired")]
    [DataRow("build", "StaleValidationGuardBuildReceiptRefRequired")]
    [DataRow("test", "StaleValidationGuardTestReceiptRefRequired")]
    [DataRow("governance", "StaleValidationGuardGovernanceReceiptRefRequired")]
    [DataRow("provider", "StaleValidationGuardProviderCiStateRefRequired")]
    [DataRow("operator", "StaleValidationGuardOperatorObservationRefRequired")]
    public void EvidenceModesRequireMatchingRefs(string caseName, string expectedIssue)
    {
        var decision = Evaluate(MissingModeEvidenceRequest(caseName));

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByMissingValidationEvidence, decision.Decision);
        Assert.AreEqual(expectedIssue, decision.Reason);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("approval", "stale validation guard is not approval")]
    [DataRow("policy", "stale validation guard is not policy satisfaction")]
    [DataRow("source-safety", "stale validation guard is not source safety")]
    [DataRow("apply", "stale validation guard is not source apply authority")]
    [DataRow("commit", "stale validation guard is not commit authority")]
    [DataRow("push", "stale validation guard is not push authority")]
    [DataRow("merge", "stale validation guard is not merge authority")]
    public void PassedValidationAndTargetMatchStillDoNotGrantAuthority(string caseName, string forbidden)
    {
        var decision = Evaluate(Request());

        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), forbidden, caseName);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("raw-test-output")]
    [DataRow("raw-build-output")]
    [DataRow("raw-ci-log")]
    [DataRow("raw-console-output")]
    [DataRow("raw-failure-log")]
    [DataRow("raw-stack-trace")]
    [DataRow("raw-command-line")]
    [DataRow("raw-provider-response")]
    [DataRow("raw-patch")]
    [DataRow("raw-diff")]
    [DataRow("credential")]
    [DataRow("authority")]
    public void UnsafeMarkersAreRejected(string caseName)
    {
        var decision = Evaluate(Request() with { ValidationEvidenceRef = UnsafeReference(caseName) });

        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.AreEqual(StaleValidationGuardBlockKind.UnsafePayload, decision.BlockKind);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void NullRequestFailsClosedWithoutThrowing()
    {
        var decision = Evaluate(null);

        Assert.AreEqual(StaleValidationGuardDecisionKind.Invalid, decision.Decision);
        Assert.AreEqual(StaleValidationGuardBlockKind.InvalidRequest, decision.BlockKind);
        Assert.AreEqual("StaleValidationGuardRequestRequired", decision.Reason);
        AssertNoAuthority(decision);
    }

    [TestMethod]
    public void UnsafePayloadDecisionDoesNotEchoValidationEvidenceRef()
    {
        var unsafeValue = UnsafeReference("raw-test-output");
        var decision = Evaluate(Request() with { ValidationEvidenceRef = unsafeValue });

        AssertNoUnsafeDecisionEcho(decision, unsafeValue);
    }

    [TestMethod]
    public void UnsafePayloadDecisionDoesNotEchoCredentialMaterial()
    {
        var unsafeValue = UnsafeReference("credential");
        var decision = Evaluate(Request() with { ValidationEvidenceRef = unsafeValue });

        AssertNoUnsafeDecisionEcho(decision, unsafeValue);
    }

    [TestMethod]
    public void UnsafePayloadDecisionDoesNotEchoRawDiff()
    {
        var unsafeValue = UnsafeReference("raw-diff");
        var decision = Evaluate(Request() with { ValidationEvidenceRef = unsafeValue });

        AssertNoUnsafeDecisionEcho(decision, unsafeValue);
    }

    [TestMethod]
    public void UnsafePayloadDecisionDoesNotEchoRawPatch()
    {
        var unsafeValue = UnsafeReference("raw-patch");
        var decision = Evaluate(Request() with { ValidationEvidenceRef = unsafeValue });

        AssertNoUnsafeDecisionEcho(decision, unsafeValue);
    }

    [TestMethod]
    public void UnsafePayloadDecisionDoesNotEchoRawProviderResponse()
    {
        var unsafeValue = UnsafeReference("raw-provider-response");
        var decision = Evaluate(Request() with { ValidationEvidenceRef = unsafeValue });

        AssertNoUnsafeDecisionEcho(decision, unsafeValue);
    }

    [DataTestMethod]
    [DataRow("patch-package:e14")]
    [DataRow("merge-target:e14")]
    [DataRow("release-candidate:e14")]
    [DataRow("deploy-target:e14")]
    public void ValidDomainRefsAreNotRejected(string targetRef)
    {
        var decision = Evaluate(Request() with { TargetRef = targetRef });

        Assert.AreEqual(StaleValidationGuardDecisionKind.MayProceedToNextAuthorityGate, decision.Decision);
        AssertNoAuthority(decision);
    }

    [DataTestMethod]
    [DataRow("ValidationApproved")]
    [DataRow("SafeToApply")]
    [DataRow("SafeToCommit")]
    [DataRow("SafeToPush")]
    [DataRow("SafeToMerge")]
    [DataRow("PolicySatisfied")]
    public void DecisionEnumAvoidsAuthorityShapedNames(string forbidden)
    {
        var names = Enum.GetNames<StaleValidationGuardDecisionKind>();

        CollectionAssert.DoesNotContain(names, forbidden);
    }

    [TestMethod]
    public void ContractTypesExposeNoCommandExecutorOrActionAuthorityFields()
    {
        var modelTypes = new[]
        {
            typeof(StaleValidationGuardRequest),
            typeof(StaleValidationGuardDecision),
            typeof(StaleValidationGuardValidationResult)
        };
        var forbidden = new[] { "Command", "Executor", "Action", "CanApply", "CanCommit", "CanPush", "CanMerge", "CanMutate", "Authorized", "Approved", "Allowed" };
        var members = modelTypes.SelectMany(type => type.GetProperties().Select(property => $"{type.Name}.{property.Name}"));

        foreach (var member in members)
        {
            foreach (var marker in forbidden)
            {
                Assert.IsFalse(member.Contains(marker, StringComparison.Ordinal), $"{member} contains authority-shaped marker {marker}");
            }
        }
    }

    [TestMethod]
    public void StaticScan_E14AddsNoExecutorWiring()
    {
        var source = StripStrings(E14CoreSource());

        AssertDoesNotContain(source, "Executor");
        AssertDoesNotContain(source, "IServiceCollection");
        AssertDoesNotContain(source, "AddScoped");
        AssertDoesNotContain(source, "AddSingleton");
    }

    [TestMethod]
    public void StaticScan_E14AddsNoGitGithubSourceOrWorktreeAccess()
    {
        var source = StripStrings(E14CoreSource());

        AssertDoesNotContain(source, "HttpClient");
        AssertDoesNotContain(source, "Octokit");
        AssertDoesNotContain(source, ".git");
        AssertDoesNotContain(source, "Directory.Get");
        AssertDoesNotContain(source, "File.Read");
    }

    [TestMethod]
    public void StaticScan_E14AddsNoProcessExecution()
    {
        var source = StripStrings(E14CoreSource());

        AssertDoesNotContain(source, "ProcessStartInfo");
        AssertDoesNotContain(source, "Process.Start");
        AssertDoesNotContain(source, "RunProcessAsync");
        AssertDoesNotContain(source, "cmd.exe");
        AssertDoesNotContain(source, "powershell");
        AssertDoesNotContain(source, "bash");
    }

    [TestMethod]
    public void StaticScan_E14AddsNoFileSystemMutation()
    {
        var source = StripStrings(E14CoreSource());

        AssertDoesNotContain(source, "File.Write");
        AssertDoesNotContain(source, "File.Append");
        AssertDoesNotContain(source, "Directory.Create");
        AssertDoesNotContain(source, "Delete");
        AssertDoesNotContain(source, "File.Move");
        AssertDoesNotContain(source, "Directory.Move");
    }

    [TestMethod]
    public void StaticScan_E14AddsNoValidationTestBuildRunnerAccess()
    {
        var source = StripStrings(E14CoreSource());

        AssertDoesNotContain(source, "dotnet test");
        AssertDoesNotContain(source, "dotnet build");
        AssertDoesNotContain(source, "TestRunner");
        AssertDoesNotContain(source, "BuildRunner");
        AssertDoesNotContain(source, "ValidationRunner");
    }

    [TestMethod]
    public void StaticScan_E14AddsNoCiGithubApiAccess()
    {
        var source = StripStrings(E14CoreSource());

        AssertDoesNotContain(source, "HttpClient");
        AssertDoesNotContain(source, "GraphQL");
        AssertDoesNotContain(source, "REST");
        AssertDoesNotContain(source, "Actions");
        AssertDoesNotContain(source, "WorkflowDispatch");
    }

    [TestMethod]
    public void StaticScan_E14AddsNoLockAcquisitionReleaseOrRenewal()
    {
        var source = StripStrings(E14CoreSource());

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
        var left = Evaluate(Request()).RecordFingerprint;
        var right = Evaluate(Request()).RecordFingerprint;

        Assert.AreEqual(left, right);
    }

    [TestMethod]
    public void FingerprintChangesWhenValidationOutcomeChanges()
    {
        var left = Evaluate(Request()).RecordFingerprint;
        var right = Evaluate(Request() with { ValidationOutcome = ValidationOutcomeState.Failed }).RecordFingerprint;

        Assert.AreNotEqual(left, right);
    }

    [TestMethod]
    public void FingerprintChangesWhenValidationTargetChanges()
    {
        var left = Evaluate(Request()).RecordFingerprint;
        var right = Evaluate(Request() with { ObservedValidationTargetRef = "validation-target:e14-other", ExpectedValidationTargetRef = "validation-target:e14-other" }).RecordFingerprint;

        Assert.AreNotEqual(left, right);
    }

    [TestMethod]
    public void FingerprintChangesWhenValidationFingerprintChanges()
    {
        var left = Evaluate(Request()).RecordFingerprint;
        var right = Evaluate(Request() with { ObservedValidationFingerprint = "validation-fingerprint:e14-other", ExpectedValidationFingerprint = "validation-fingerprint:e14-other" }).RecordFingerprint;

        Assert.AreNotEqual(left, right);
    }

    [TestMethod]
    public void FingerprintDoesNotIncludeRawPayloads()
    {
        var decision = Evaluate(Request() with { ValidationEvidenceRef = UnsafeReference("raw-test-output") });

        Assert.IsFalse(decision.RecordFingerprint.Contains(UnsafeReference("raw-test-output"), StringComparison.Ordinal));
        Assert.IsTrue(decision.RecordFingerprint.Contains("[unsafe]", StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow("Validation passed then is not validation fresh now.")]
    [DataRow("A fresh validation record is evidence. It is not approval, policy satisfaction, or mutation authority.")]
    [DataRow("approval")]
    [DataRow("policy satisfaction")]
    [DataRow("source safety")]
    [DataRow("validation execution")]
    [DataRow("source apply authority")]
    [DataRow("commit authority")]
    [DataRow("push authority")]
    [DataRow("merge authority")]
    [DataRow("workflow continuation")]
    public void ReceiptContainsBoundaryLines(string expected)
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "E14_STALE_VALIDATION_GUARD.md"));

        StringAssert.Contains(receipt, expected);
    }

    private static StaleValidationGuardDecision Evaluate(StaleValidationGuardRequest? request) =>
        new StaleValidationGuardService().Evaluate(request);

    private static StaleValidationGuardRequest Request() =>
        new()
        {
            TenantId = "tenant-e14",
            ProjectId = "project-e14",
            OperationId = "op_000000000000e014",
            CorrelationId = "corr_0000000000e01400",
            MutationSurface = MutationLeaseSurfaceKind.SourceApply,
            AttemptRef = "attempt:e14",
            TargetRef = "target:e14",
            GuardRef = "stale-validation-guard:e14",
            SubjectKind = StaleValidationSubjectKind.SourceApplyTarget,
            ValidationEvidenceKind = ValidationEvidenceKind.FocusedTestResult,
            EvidenceTrustLevel = ValidationEvidenceTrustLevel.ReceiptBacked,
            ObservationFreshness = ValidationObservationFreshness.Fresh,
            ValidationOutcome = ValidationOutcomeState.Passed,
            ValidationScope = ValidationScopeKind.FocusedSlice,
            ValidationEvidenceRef = "validation-evidence:e14",
            ValidationReceiptRef = "validation-receipt:e14",
            BuildReceiptRef = "build-receipt:e14",
            TestReceiptRef = "test-receipt:e14",
            GovernanceReceiptRef = "governance-receipt:e14",
            ProviderCiStateRef = "provider-ci-state:e14",
            OperatorObservationRef = "operator-observation:e14",
            PostStateObservationRef = "post-state-observation:e14",
            DirtyWorktreeGuardRef = "dirty-worktree-guard:e14",
            MovedBaseGuardRef = "moved-base-guard:e14",
            ConcurrentGuardDecisionRef = "concurrent-guard-decision:e14",
            ExpectedValidationTargetRef = "validation-target:e14",
            ObservedValidationTargetRef = "validation-target:e14",
            ExpectedValidationFingerprint = "validation-fingerprint:e14",
            ObservedValidationFingerprint = "validation-fingerprint:e14",
            ExpectedSourceStateRef = "source-state:e14",
            ObservedSourceStateRef = "source-state:e14",
            ExpectedPatchPackageRef = "patch-package:e14",
            ObservedPatchPackageRef = "patch-package:e14",
            ExpectedCommitRef = "commit:e14",
            ObservedCommitRef = "commit:e14",
            ExpectedHeadRef = "head:e14",
            ObservedHeadRef = "head:e14",
            ExpectedBaseRef = "base:e14",
            ObservedBaseRef = "base:e14",
            ValidatedAtUtc = ValidatedAtUtc,
            RecordedAtUtc = RecordedAtUtc,
            EvidenceExpiresAtUtc = ExpiresAtUtc,
            GuardVersion = "stale-validation-guard-v1",
            ReasonCode = "validation-fresh",
            Source = "stale-validation-guard"
        };

    private static StaleValidationGuardRequest InvalidRequest(string caseName) =>
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
            "validated-at" => Request() with { ValidatedAtUtc = default },
            "recorded-at" => Request() with { RecordedAtUtc = default },
            "validated-non-utc" => Request() with { ValidatedAtUtc = new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.FromHours(12)) },
            "recorded-non-utc" => Request() with { RecordedAtUtc = new DateTimeOffset(2026, 6, 25, 0, 1, 0, TimeSpan.FromHours(12)) },
            "recorded-before-validated" => Request() with { RecordedAtUtc = ValidatedAtUtc.AddSeconds(-1) },
            "expiry-non-utc" => Request() with { EvidenceExpiresAtUtc = new DateTimeOffset(2026, 6, 25, 0, 30, 0, TimeSpan.FromHours(12)) },
            "expiry-before-validated" => Request() with { EvidenceExpiresAtUtc = ValidatedAtUtc },
            "guard-version" => Request() with { GuardVersion = "" },
            "reason" => Request() with { ReasonCode = "" },
            "source" => Request() with { Source = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static StaleValidationGuardRequest MissingEvidenceRequest(string caseName) =>
        caseName switch
        {
            "validation-evidence" => Request() with { ValidationEvidenceRef = "" },
            "observed-target" => Request() with { ObservedValidationTargetRef = "" },
            "observed-fingerprint" => Request() with { ObservedValidationFingerprint = "" },
            "concurrent-guard" => Request() with { ConcurrentGuardDecisionRef = "" },
            "dirty-worktree-guard" => Request() with { DirtyWorktreeGuardRef = "" },
            "moved-base-guard" => Request() with { MovedBaseGuardRef = "" },
            "post-state" => Request() with { PostStateObservationRef = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static StaleValidationGuardRequest MismatchedRequest(string caseName) =>
        caseName switch
        {
            "target" => Request() with { ObservedValidationTargetRef = "validation-target:e14-other" },
            "fingerprint" => Request() with { ObservedValidationFingerprint = "validation-fingerprint:e14-other" },
            "source-state" => Request() with { ObservedSourceStateRef = "source-state:e14-other" },
            "patch-package" => Request() with { ObservedPatchPackageRef = "patch-package:e14-other" },
            "commit" => Request() with { ObservedCommitRef = "commit:e14-other" },
            "head" => Request() with { ObservedHeadRef = "head:e14-other" },
            "base" => Request() with { ObservedBaseRef = "base:e14-other" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static StaleValidationGuardRequest MissingModeEvidenceRequest(string caseName) =>
        caseName switch
        {
            "receipt-backed" => Request() with
            {
                EvidenceTrustLevel = ValidationEvidenceTrustLevel.ReceiptBacked,
                ValidationReceiptRef = "",
                BuildReceiptRef = "",
                TestReceiptRef = "",
                GovernanceReceiptRef = ""
            },
            "composite-receipt" => Request() with
            {
                ValidationEvidenceKind = ValidationEvidenceKind.CompositeValidationReceipt,
                ValidationReceiptRef = "",
                BuildReceiptRef = "",
                TestReceiptRef = "",
                GovernanceReceiptRef = ""
            },
            "build" => Request() with { EvidenceTrustLevel = ValidationEvidenceTrustLevel.BuildReceiptBacked, BuildReceiptRef = "" },
            "test" => Request() with { EvidenceTrustLevel = ValidationEvidenceTrustLevel.TestReceiptBacked, TestReceiptRef = "" },
            "governance" => Request() with { EvidenceTrustLevel = ValidationEvidenceTrustLevel.GovernanceReceiptBacked, GovernanceReceiptRef = "" },
            "provider" => Request() with { ValidationEvidenceKind = ValidationEvidenceKind.ProviderCiStatus, ProviderCiStateRef = "" },
            "operator" => Request() with { EvidenceTrustLevel = ValidationEvidenceTrustLevel.OperatorObserved, OperatorObservationRef = "" },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static string UnsafeReference(string caseName) =>
        caseName switch
        {
            "raw-test-output" => "raw test output",
            "raw-build-output" => "raw build output",
            "raw-ci-log" => string.Concat("raw ", "ci log"),
            "raw-console-output" => "raw console output",
            "raw-failure-log" => "raw failure log",
            "raw-stack-trace" => "raw stack trace",
            "raw-command-line" => "raw command line",
            "raw-provider-response" => "raw provider response",
            "raw-patch" => "raw patch",
            "raw-diff" => "raw diff",
            "credential" => string.Concat("to", "ken", "=", "fake"),
            "authority" => "validation approved",
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static void AssertRequiresFreshGates(StaleValidationGuardDecision decision)
    {
        Assert.IsTrue(decision.RequiresFreshAuthority);
        Assert.IsTrue(decision.RequiresFreshValidation);
        Assert.IsTrue(decision.RequiresFreshConcurrentGuard);
        Assert.IsTrue(decision.RequiresDirtyWorktreeGuard);
        Assert.IsTrue(decision.RequiresMovedBaseGuard);
        Assert.IsTrue(decision.RequiresFreshPostStateObservation);
        Assert.IsTrue(decision.RequiresHumanReview);
    }

    private static void AssertNoAuthority(StaleValidationGuardDecision decision)
    {
        AssertRequiresFreshGates(decision);
        CollectionAssert.Contains(decision.Warnings.ToList(), "stale validation guard is read only");
        CollectionAssert.Contains(decision.Warnings.ToList(), "stale validation guard does not run validation");
        CollectionAssert.Contains(decision.Warnings.ToList(), "stale validation guard does not run tests");
        CollectionAssert.Contains(decision.Warnings.ToList(), "stale validation guard does not run builds");
        CollectionAssert.Contains(decision.Warnings.ToList(), "stale validation guard does not call ci");
        CollectionAssert.Contains(decision.Warnings.ToList(), "stale validation guard does not call github");
        CollectionAssert.Contains(decision.Warnings.ToList(), "stale validation guard does not read logs");
        CollectionAssert.Contains(decision.Warnings.ToList(), "validation passed then is not validation fresh now");
        CollectionAssert.Contains(decision.Warnings.ToList(), "validation passed is not approval");
        CollectionAssert.Contains(decision.Warnings.ToList(), "validation passed is not policy satisfaction");
        CollectionAssert.Contains(decision.Warnings.ToList(), "validation passed is not source safety");
        CollectionAssert.Contains(decision.Warnings.ToList(), "validation target match is not mutation authority");
        CollectionAssert.Contains(decision.Warnings.ToList(), "fresh authority is required before any mutation");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "stale validation guard is not mutation execution");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "stale validation guard is not source apply authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "stale validation guard is not commit authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "stale validation guard is not push authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "stale validation guard is not pull request authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "stale validation guard is not merge authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "stale validation guard is not retry authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "stale validation guard is not recovery authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "stale validation guard is not rollback authority");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "stale validation guard is not approval");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "stale validation guard is not policy satisfaction");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "stale validation guard is not validation execution");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "stale validation guard is not source safety");
        CollectionAssert.Contains(decision.ForbiddenAuthorityImplications.ToList(), "stale validation guard is not workflow continuation");
    }

    private static void AssertNoUnsafeDecisionEcho(StaleValidationGuardDecision decision, string unsafeValue)
    {
        Assert.AreEqual(StaleValidationGuardDecisionKind.BlockedByUnsafePayload, decision.Decision);
        Assert.AreEqual(StaleValidationGuardBlockKind.UnsafePayload, decision.BlockKind);
        AssertNoAuthority(decision);

        var stringValues = typeof(StaleValidationGuardDecision)
            .GetProperties()
            .Where(static property => property.PropertyType == typeof(string))
            .Select(property => (Name: property.Name, Value: (string?)property.GetValue(decision) ?? string.Empty));

        foreach (var (name, value) in stringValues)
        {
            Assert.IsFalse(
                value.Contains(unsafeValue, StringComparison.Ordinal),
                $"{name} echoed unsafe value.");
        }

        Assert.AreEqual("[unsafe-rejected]", decision.MatchedValidationEvidenceRef);
    }

    private static string E14CoreSource()
    {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "StaleValidationGuard*.cs");
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
            $"Unexpected forbidden marker found in E14 source: {forbidden}");
}
